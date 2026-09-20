using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Models;
using Seiri.Core.Tagging;
using Seiri.Infrastructure;
using Seiri.Services;

namespace Seiri.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly LibraryService _libraries;
    private readonly ISettingsStore _settingsStore;
    private readonly TaggingService _tagging;
    private readonly LibraryWatcher _watcher = new();
    private readonly ContentHasher _hasher;
    private readonly TagAliasStore _aliasStore;
    private CancellationTokenSource? _taggingCts;
    private CancellationTokenSource? _hashCts;
    private OnnxEmbedder? _embedder;
    private RailSection _gallerySection = RailSection.All;
    private string? _galleryRoot;

    public ShellViewModel(
        LibraryService libraries,
        ISettingsStore settingsStore,
        ThumbnailGenerator thumbs,
        IAppHome appHome,
        ModelDownloader downloader,
        TaggingService tagging,
        IReadOnlyList<ModelCatalogEntry> catalog)
    {
        _libraries = libraries;
        _settingsStore = settingsStore;
        _tagging = tagging;
        AppHome = appHome;
        Downloader = downloader;
        Settings = settingsStore.Load();
        Settings.ModelPresets = new Dictionary<string, ThresholdPreset>(
            Settings.ModelPresets ?? [], StringComparer.OrdinalIgnoreCase);
        Settings.EnabledModelIds ??= [];
        Settings.CustomModels ??= [];
        Settings.HardwareAcceleration ??= true;
        Thumbs = new ThumbnailQueue(thumbs, libraries);
        Gallery = new GalleryViewModel(this);
        _hasher = new ContentHasher(libraries);
        _aliasStore = new TagAliasStore(appHome);
        foreach (var entry in catalog.Concat(Settings.CustomModels))
        {
            if (Models.Any(m => m.Entry.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Models.Add(new ModelCardViewModel(this, entry));
        }

        _watcher.Changed += OnWatchedLibraryChanged;
        ApplyTheme();
        NotifyTaggingState();
    }

    public GalleryViewModel Gallery { get; }
    public AppSettings Settings { get; }
    public IAppHome AppHome { get; }
    public ModelDownloader Downloader { get; }
    public ThumbnailQueue Thumbs { get; }

    public bool UseHardwareAcceleration
    {
        get => Settings.HardwareAcceleration != false;
        set
        {
            Settings.HardwareAcceleration = value;
            PersistSettings();
            Gallery.NotifyImageSourceChanged();
        }
    }
    public ObservableCollection<ModelCardViewModel> Models { get; } = [];

    public ObservableCollection<LibraryInfo> Libraries { get; } = [];

    [ObservableProperty]
    public partial bool RailExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewOpen { get; set; } = true;

    [ObservableProperty]
    public partial RailSection CurrentSection { get; set; } = RailSection.All;

    [ObservableProperty]
    public partial string? CurrentLibraryRoot { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Add a folder to start";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsTagging { get; set; }

    [ObservableProperty]
    public partial double TaggingPercent { get; set; }

    [ObservableProperty]
    public partial string TaggingMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLibraryLoading { get; set; } = true;

    [ObservableProperty]
    public partial string LoadingMessage { get; set; } = "Loading library…";

    public bool CanTag => !IsTagging && HasEnabledModel && Libraries.Count > 0;
    public bool CanTagCurrent => CanTag && Gallery.SelectedItem is { Kind: MediaKind.Image };
    public bool HasEnabledModel => Models.Any(IsTaggerEnabled);
    public bool HasSimilarModel => Models.Any(m =>
        m.IsInstalled && m.Entry.Preprocess.Equals("Clip224", StringComparison.OrdinalIgnoreCase));
    public string AliasStatus => _libraries.Aliases is { IsLoaded: true } aliases
        ? $"{aliases.AliasCount} aliases loaded · implications stored, not applied to sidecars"
        : "Not downloaded";

    private static bool IsTaggerEnabled(ModelCardViewModel model) =>
        model.IsInstalled && model.IsEnabled
        && !model.Entry.Preprocess.Equals("Clip224", StringComparison.OrdinalIgnoreCase);
    public string TagUntaggedLabel => Gallery.TaggedFilter == TaggedFilter.Failed ? "Retry failed" : "Tag untagged";

    public double RailWidth => RailExpanded ? 240 : 48;

    public void NotifyPageTitle() => OnPropertyChanged(nameof(PageTitle));

    public string PageTitle => CurrentSection switch
    {
        RailSection.Favorites => "Favorites",
        RailSection.Tagging => "Tagging",
        RailSection.Tags => "Tags",
        RailSection.Directory when CurrentLibraryRoot is not null => new DirectoryInfo(CurrentLibraryRoot).Name,
        RailSection.Settings => "Settings",
        _ => "Gallery"
    };

    public bool ShowGallery => !IsSettingsOpen && CurrentSection != RailSection.Tags;
    public bool ShowTagsPage => CurrentSection == RailSection.Tags;

    public async Task InitializeAsync()
    {
        RailExpanded = Settings.RailExpanded;
        IsPreviewOpen = Settings.PreviewPaneOpen;
        Gallery.Layout = Settings.Layout;
        Gallery.Density = Settings.LayoutDensity;
        Gallery.GroupBy = Settings.GroupBy;
        OnPropertyChanged(nameof(RailWidth));

        IsLibraryLoading = true;
        LoadingMessage = "Opening libraries…";
        try
        {
            var infos = await Task.Run(() => _libraries.LoadPersistedAsync());
            Libraries.Clear();
            foreach (var info in infos)
            {
                Libraries.Add(info);
                if (!info.IsOffline)
                {
                    _watcher.Watch(info.RootPath);
                }
            }

            if (Settings.UseDanbooruAliases)
            {
                LoadingMessage = "Loading tag aliases…";
                try
                {
                    var loaded = await Task.Run(() => _aliasStore.Load());
                    _libraries.Aliases = loaded.IsLoaded ? loaded : null;
                }
                catch (Exception ex)
                {
                    AppLog.Error("load aliases", ex);
                }
            }

            LoadingMessage = "Reading the gallery index…";
            await Gallery.RefreshAsync();
        }
        finally
        {
            IsLibraryLoading = false;
            LoadingMessage = "Loading library…";
        }

        _hashCts = new CancellationTokenSource();
        AppLog.Run(() => HashInBackgroundAsync(_hashCts.Token), "background hash");
    }

    public MediaQuery CreateQuery()
    {
        var seed = new MediaQuery
        {
            Section = CurrentSection == RailSection.Settings ? RailSection.All : CurrentSection,
            LibraryRoot = CurrentSection == RailSection.Directory ? CurrentLibraryRoot : null,
            Kind = Gallery.KindFilter,
            Tagged = CurrentSection == RailSection.Tagging ? Gallery.TaggedFilter : TaggedFilter.All,
            Sort = Gallery.Sort,
            Direction = Gallery.Direction
        };
        return QueryParser.Parse(Gallery.SearchText, seed);
    }

    public async Task AddLibraryAsync(string path)
    {
        ErrorMessage = null;
        Status = "Scanning…";
        LibraryInfo info;
        try
        {
            info = await _libraries.AddAsync(path);
        }
        catch (Exception ex)
        {
            AppLog.Error($"AddLibrary {path}", ex);
            ErrorMessage = ex.Message;
            Status = Gallery.Subtitle;
            throw;
        }
        var existing = Libraries.FirstOrDefault(l => l.RootPath.Equals(info.RootPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Libraries.Remove(existing);
        }

        Libraries.Add(info);
        _watcher.Watch(info.RootPath);
        await Gallery.RefreshAsync();
        Status = $"{info.FileCount} files";
    }

    public async Task RemoveLibraryAsync(string path, bool deleteGenerated)
    {
        _watcher.Unwatch(path);
        await _libraries.RemoveAsync(path, deleteGenerated);
        var existing = Libraries.FirstOrDefault(l => l.RootPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Libraries.Remove(existing);
        }

        if (CurrentLibraryRoot?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
        {
            Navigate(RailSection.All, null);
        }

        await Gallery.RefreshAsync();
    }

    public async Task RescanAsync(string? root = null)
    {
        Status = "Scanning…";
        try
        {
            if (root is not null)
            {
                var index = _libraries.OpenIndexes.FirstOrDefault(i => i.RootPath.Equals(root, StringComparison.OrdinalIgnoreCase));
                if (index is not null)
                {
                    await _libraries.ScanAsync(index);
                }
            }
            else
            {
                foreach (var index in _libraries.OpenIndexes.ToList())
                {
                    await _libraries.ScanAsync(index);
                }
            }

            await RefreshLibraryInfosAsync();
            await Gallery.RefreshAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Rescan {root}", ex);
            ErrorMessage = $"Scan failed: {ex.Message}";
            Status = Gallery.Subtitle;
        }
    }

    public async Task RefreshLibraryInfosAsync()
    {
        for (var i = 0; i < Libraries.Count; i++)
        {
            var root = Libraries[i].RootPath;
            var index = _libraries.OpenIndexes.FirstOrDefault(x =>
                x.RootPath.Equals(root, StringComparison.OrdinalIgnoreCase));
            if (index is null)
            {
                continue;
            }

            Libraries[i] = await index.GetInfoAsync();
        }
    }

    public void Navigate(RailSection section, string? libraryRoot)
    {
        var fromSettings = CurrentSection == RailSection.Settings;
        if (section == RailSection.Settings)
        {
            if (CurrentSection != RailSection.Settings)
            {
                _gallerySection = CurrentSection;
                _galleryRoot = CurrentLibraryRoot;
            }

            CurrentSection = RailSection.Settings;
            IsSettingsOpen = true;
            Gallery.CloseSuggestions();
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(ShowGallery));
            OnPropertyChanged(nameof(ShowTagsPage));
            return;
        }

        CurrentSection = section;
        CurrentLibraryRoot = libraryRoot;
        IsSettingsOpen = false;
        Gallery.CloseSuggestions();
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(ShowGallery));
        OnPropertyChanged(nameof(ShowTagsPage));
        Gallery.NotifyHeader();
        OnPropertyChanged(nameof(ShowDirectoryTagging));
        NotifyTaggingState();
        if (section == RailSection.Tags)
        {
            AppLog.Run(() => Gallery.LoadTagBrowserAsync(), "LoadTagBrowser");
            return;
        }

        if (fromSettings
            && section == _gallerySection
            && string.Equals(libraryRoot, _galleryRoot, StringComparison.OrdinalIgnoreCase)
            && Gallery.Items.Count > 0)
        {
            return;
        }

        AppLog.Run(() => Gallery.RefreshAsync(), "Navigate refresh");
    }

    public void PersistSettings()
    {
        Settings.RailExpanded = RailExpanded;
        Settings.PreviewPaneOpen = IsPreviewOpen;
        try
        {
            _settingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            AppLog.Error("PersistSettings", ex);
            ErrorMessage = "Could not save settings.";
        }

        ApplyTheme();
    }

    public void ApplyTheme()
    {
        if (App.Window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = Settings.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    public bool ShowDirectoryTagging => CurrentSection == RailSection.Directory && CurrentLibraryRoot is not null;

    public void PauseWatcher() => _watcher.Pause();

    public void ResumeWatcher() => _watcher.Resume();

    public Task DeleteItemAsync(MediaItem item) => _libraries.DeleteItemAsync(item);

    public Task RenameItemAsync(MediaItem item, RenamePlan plan) => _libraries.RenameItemAsync(item, plan);

    public Task SetFavoriteAsync(MediaItem item, bool value) => _libraries.SetFavoriteAsync(item, value);

    public Task<IReadOnlyList<MediaItem>> QueryAsync(MediaQuery query) => _libraries.QueryAsync(query);

    public Task<int> CountAsync(MediaQuery query) => _libraries.CountAsync(query);

    public Task<IReadOnlyList<string>> GetTagsAsync(MediaItem item) => _libraries.GetTagsAsync(item);

    public Task<IReadOnlyList<TagRecord>> GetTagRecordsAsync(MediaItem item) => _libraries.GetTagRecordsAsync(item);

    public Task<IReadOnlyList<TagRecord>> SuggestTagsAsync(string? prefix) => _libraries.SuggestTagsAsync(prefix);

    public Task<IReadOnlyList<string>> ListFolderNamesAsync() => _libraries.ListFolderNamesAsync();

    public Task<IReadOnlyList<TagRecord>> ListTagsAsync() => _libraries.ListTagsAsync();

    public Task<IReadOnlyList<string>> ListExtensionsAsync() => _libraries.ListExtensionsAsync();

    public Task<MediaItem?> SaveTagsAsync(MediaItem item, IReadOnlyList<string> tags) =>
        _libraries.SetTagsAsync(item, tags, Settings);

    public Task<MediaItem?> SaveTagsAsync(MediaItem item, IReadOnlyList<TagRecord> tags) =>
        _libraries.SetTagsAsync(item, tags, Settings);

    public void NotifyTaggingState()
    {
        OnPropertyChanged(nameof(CanTag));
        OnPropertyChanged(nameof(CanTagCurrent));
        OnPropertyChanged(nameof(HasEnabledModel));
        OnPropertyChanged(nameof(TagUntaggedLabel));
    }

    public Task UnloadModelAsync(string id) => _tagging.UnloadAsync(id);

    public void DisposeWatcher()
    {
        _taggingCts?.Cancel();
        _hashCts?.Cancel();
        _watcher.Dispose();
        Downloader.Dispose();
        AppLog.Run(() => _tagging.DisposeAsync().AsTask(), "dispose tagging");
        if (_embedder is not null)
        {
            AppLog.Run(() => _embedder.DisposeAsync().AsTask(), "dispose embedder");
        }
    }

    public async Task EnsureHashesAsync()
    {
        Status = "Hashing files…";
        try
        {
            await _hasher.HashMissingAsync(new Progress<string>(msg => Status = msg));
        }
        finally
        {
            Status = Gallery.Subtitle;
        }
    }

    public Task<IReadOnlyList<IReadOnlyList<MediaItem>>> ListDuplicateGroupsAsync() =>
        _libraries.ListDuplicateGroupsAsync();

    public Task AddCustomModelAsync(ModelCatalogEntry entry)
    {
        if (Models.Any(m => m.Entry.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = "A model with that id is already in the list.";
            return Task.CompletedTask;
        }

        Settings.CustomModels.Add(entry);
        PersistSettings();
        Models.Add(new ModelCardViewModel(this, entry));
        NotifyTaggingState();
        return Task.CompletedTask;
    }

    public void PersistCustomModels()
    {
        Settings.CustomModels = Models
            .Select(m => m.Entry)
            .Where(e => e.Id.StartsWith("custom-", StringComparison.OrdinalIgnoreCase)
                || e.Preprocess.Equals("Clip224", StringComparison.OrdinalIgnoreCase))
            .ToList();
        PersistSettings();
    }

    public async Task DownloadAliasesAsync()
    {
        Status = "Downloading Danbooru aliases…";
        try
        {
            var catalog = await _aliasStore.DownloadAsync();
            _libraries.Aliases = Settings.UseDanbooruAliases ? catalog : null;
            Status = $"{catalog.AliasCount} aliases, {catalog.ImplicationCount} implications (search-only; sidecars unchanged)";
            OnPropertyChanged(nameof(AliasStatus));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not download aliases: {ex.Message}";
            Status = Gallery.Subtitle;
        }
    }

    public void ApplyAliasSetting()
    {
        if (Settings.UseDanbooruAliases)
        {
            var loaded = _aliasStore.Load();
            _libraries.Aliases = loaded.IsLoaded ? loaded : null;
        }
        else
        {
            _libraries.Aliases = null;
        }

        PersistSettings();
        OnPropertyChanged(nameof(AliasStatus));
        AppLog.Run(() => Gallery.RefreshAsync(), "alias setting refresh");
    }

    public async Task<IReadOnlyList<(MediaItem Item, float Score)>> FindSimilarAsync(MediaItem seed)
    {
        var card = Models.FirstOrDefault(m =>
            m.IsInstalled && m.Entry.Preprocess.Equals("Clip224", StringComparison.OrdinalIgnoreCase));
        if (card is null)
        {
            ErrorMessage = "Install a similar-images encoder in Settings (CLIP-style ONNX).";
            return [];
        }

        Status = "Finding similar images…";
        try
        {
            AppLog.Write($"Find similar seed={seed.FileName} model={card.Entry.Id}");
            _embedder ??= new OnnxEmbedder(card.Entry, AppHome, Settings.ExecutionProvider);
            var pixels = await ImagePixelLoader.LoadScaledAsync(seed.FullPath);
            var vector = await _embedder.EmbedAsync(pixels);
            await _libraries.UpsertEmbeddingAsync(seed, card.Entry.Id, vector);

            var others = await _libraries.ListEmbeddingsAsync(card.Entry.Id);
            if (others.Count < 8)
            {
                await BackfillEmbeddingsAsync(card.Entry, 24);
                others = await _libraries.ListEmbeddingsAsync(card.Entry.Id);
            }

            return others
                .Where(o => o.Item.Id != seed.Id || !o.Item.LibraryRoot.Equals(seed.LibraryRoot, StringComparison.OrdinalIgnoreCase))
                .Where(o => o.Vector.Length == vector.Length)
                .Select(o => (o.Item, Score: OnnxEmbedder.Cosine(vector, o.Vector)))
                .OrderByDescending(o => o.Score)
                .Take(24)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error("Find similar", ex);
            ErrorMessage = $"Similar search failed: {ex.Message}";
            return [];
        }
        finally
        {
            Status = Gallery.Subtitle;
        }
    }

    private async Task BackfillEmbeddingsAsync(ModelCatalogEntry entry, int limit)
    {
        if (_embedder is null)
        {
            return;
        }

        var query = CreateQuery();
        query.Kind = MediaKindFilter.Images;
        var items = (await QueryAsync(query)).Where(i => i.Kind == MediaKind.Image).Take(limit).ToList();
        foreach (var item in items)
        {
            if (await _libraries.GetEmbeddingAsync(item, entry.Id) is not null)
            {
                continue;
            }

            if (!File.Exists(item.FullPath))
            {
                continue;
            }

            try
            {
                var pixels = await ImagePixelLoader.LoadScaledAsync(item.FullPath);
                var vector = await _embedder.EmbedAsync(pixels);
                await _libraries.UpsertEmbeddingAsync(item, entry.Id, vector);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Similar backfill {item.FileName}", ex);
            }
        }
    }

    private async Task HashInBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(2500, cancellationToken);
            await _hasher.HashMissingAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("background hash", ex);
        }
    }

    private void OnWatchedLibraryChanged(string root)
    {
        App.DispatcherQueue.TryEnqueue(() => AppLog.Run(() => RescanAsync(root), $"watch rescan {root}"));
    }

    partial void OnRailExpandedChanged(bool value) => OnPropertyChanged(nameof(RailWidth));

    [RelayCommand]
    private void ToggleRail()
    {
        RailExpanded = !RailExpanded;
        PersistSettings();
    }

    [RelayCommand]
    private void TogglePreview()
    {
        IsPreviewOpen = !IsPreviewOpen;
        PersistSettings();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        foreach (var model in Models)
        {
            model.RefreshInstalled();
        }

        NotifyTaggingState();
        Navigate(RailSection.Settings, null);
    }

    [RelayCommand]
    private void CloseSettings() => Navigate(_gallerySection, _galleryRoot);

    [RelayCommand]
    private void CancelTagging() => _taggingCts?.Cancel();

    [RelayCommand]
    private async Task TagFolderUntaggedAsync()
    {
        var query = CreateQuery();
        query.Kind = MediaKindFilter.Images;
        query.Tagged = TaggedFilter.Untagged;
        var items = (await QueryAsync(query)).Where(i => i.Kind == MediaKind.Image).ToList();
        if (items.Count == 0)
        {
            ErrorMessage = "No untagged images in this folder.";
            return;
        }

        await TagItemsAsync(items, overwrite: false, title: "Tag untagged in this folder", primary: "Tag untagged");
    }

    [RelayCommand]
    private async Task RetagFolderAsync()
    {
        var query = CreateQuery();
        query.Kind = MediaKindFilter.Images;
        query.Tagged = TaggedFilter.All;
        var items = (await QueryAsync(query)).Where(i => i.Kind == MediaKind.Image).ToList();
        if (items.Count == 0)
        {
            ErrorMessage = "No images in this folder.";
            return;
        }

        await TagItemsAsync(
            items,
            overwrite: true,
            title: "Retag all in this folder",
            primary: "Retag");
    }

    [RelayCommand]
    private async Task TagUntaggedAsync()
    {
        var retryFailed = CurrentSection == RailSection.Tagging && Gallery.TaggedFilter == TaggedFilter.Failed;
        var query = CreateQuery();
        query.Kind = MediaKindFilter.Images;
        query.Tagged = retryFailed ? TaggedFilter.Failed : TaggedFilter.Untagged;
        var items = (await QueryAsync(query)).Where(i => i.Kind == MediaKind.Image).ToList();
        if (items.Count == 0)
        {
            ErrorMessage = retryFailed ? "No failed images to retry." : "No untagged images.";
            return;
        }

        await TagItemsAsync(
            items,
            overwrite: retryFailed,
            title: retryFailed ? "Retry failed images" : "Tag untagged images",
            primary: retryFailed ? "Retry" : "Tag untagged");
    }

    public async Task TagItemsAsync(IReadOnlyList<MediaItem> items, bool overwrite, string title, string primary)
    {
        if (IsTagging)
        {
            return;
        }

        if (!HasEnabledModel)
        {
            ErrorMessage = "Download and enable a model in Settings → Models to tag.";
            return;
        }

        var images = items.Where(i => i.Kind == MediaKind.Image).ToList();
        if (images.Count == 0)
        {
            ErrorMessage = "Select an image to tag. Videos are skipped.";
            return;
        }

        var enabled = Models.Where(IsTaggerEnabled).Select(m => m.Entry).ToList();
        var names = string.Join(", ", enabled.Select(e => e.DisplayName));
        var dialog = new ContentDialog
        {
            Title = title,
            Content = overwrite
                ? $"{images.Count} photo{(images.Count == 1 ? "" : "s")} will be tagged with {names}. Existing sidecar tags will be replaced. Nothing is uploaded."
                : $"{images.Count} photo{(images.Count == 1 ? "" : "s")} will be tagged with {names}. Videos are skipped. Tags are written to sidecar .txt files next to each image. Nothing is uploaded.",
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.Window.Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        IsTagging = true;
        TaggingPercent = 0;
        TaggingMessage = "Loading models…";
        NotifyTaggingState();
        _taggingCts?.Cancel();
        var cts = _taggingCts = new CancellationTokenSource();
        var yieldGpu = ExecutionProviders.WantsDirectMl(Settings.ExecutionProvider);
        var previousHardware = Settings.HardwareAcceleration;
        if (yieldGpu)
        {
            GpuWork.BeginUiYield();
            Settings.HardwareAcceleration = false;
            Gallery.NotifyImageSourceChanged();
            TaggingMessage = "Preparing GPU…";
            await Task.Delay(1000);
        }

        try
        {
            var lastUi = new long[1];
            var progress = new Progress<TaggingProgress>(p =>
            {
                if (!p.ShouldPublishUi(ref lastUi[0]))
                {
                    return;
                }

                TaggingPercent = p.Percent;
                TaggingMessage = p.Phase == "loading"
                    ? p.CurrentFile
                    : $"{p.Done}/{p.Total} · {p.CurrentFile} · {p.Tagged} tagged, {p.Failed} failed";
            });
            _watcher.Pause();
            Thumbs.PauseHeavyWork();
            TaggingRunResult result;
            try
            {
                result = await Task.Run(
                    () => _tagging.RunAsync(images, enabled, Settings, progress, cts.Token, overwrite),
                    cts.Token);
            }
            finally
            {
                Thumbs.ResumeHeavyWork();
                _watcher.Resume();
            }
            TaggingMessage = $"{result.Tagged} tagged, {result.Failed} failed, {result.Skipped} skipped.";
            await Gallery.RefreshAsync(force: true);
            if (result.Failed > 0)
            {
                ErrorMessage = $"{result.Failed} image{(result.Failed == 1 ? "" : "s")} failed. Review them on the Failed filter.";
            }
        }
        catch (OperationCanceledException)
        {
            TaggingMessage = "Tagging cancelled.";
            await Gallery.RefreshAsync(force: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("TagItems", ex);
            ErrorMessage = ex.Message;
            TaggingMessage = "Tagging failed to start.";
        }
        finally
        {
            if (yieldGpu)
            {
                Settings.HardwareAcceleration = previousHardware;
                GpuWork.EndUiYield();
                Gallery.NotifyImageSourceChanged();
            }

            IsTagging = false;
            NotifyTaggingState();
        }
    }

}

