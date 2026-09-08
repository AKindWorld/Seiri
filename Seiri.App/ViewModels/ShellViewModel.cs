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
    private readonly ThumbnailGenerator _thumbs;
    private readonly TaggingService _tagging;
    private readonly LibraryWatcher _watcher = new();
    private CancellationTokenSource? _taggingCts;

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
        _thumbs = thumbs;
        _tagging = tagging;
        AppHome = appHome;
        Downloader = downloader;
        Settings = settingsStore.Load();
        Settings.ModelPresets = new Dictionary<string, ThresholdPreset>(
            Settings.ModelPresets ?? [], StringComparer.OrdinalIgnoreCase);
        Settings.EnabledModelIds ??= [];
        Gallery = new GalleryViewModel(this);
        foreach (var entry in catalog)
        {
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

    public bool CanTag => !IsTagging && HasEnabledModel && Libraries.Count > 0;
    public bool CanTagCurrent => CanTag && Gallery.SelectedItem is { Kind: MediaKind.Image };
    public bool HasEnabledModel => Models.Any(m => m.IsInstalled && m.IsEnabled);
    public string TagUntaggedLabel => Gallery.TaggedFilter == TaggedFilter.Failed ? "Retry failed" : "Tag untagged";

    public double RailWidth => RailExpanded ? 240 : 48;

    public string PageTitle => CurrentSection switch
    {
        RailSection.Favorites => "Favorites",
        RailSection.Tagging => "Tagging",
        RailSection.Directory when CurrentLibraryRoot is not null => new DirectoryInfo(CurrentLibraryRoot).Name,
        RailSection.Settings => "Settings",
        _ => "Gallery"
    };

    public async Task InitializeAsync()
    {
        RailExpanded = Settings.RailExpanded;
        IsPreviewOpen = Settings.PreviewPaneOpen;
        Gallery.Layout = Settings.Layout;
        Gallery.Density = Settings.LayoutDensity;
        OnPropertyChanged(nameof(RailWidth));

        var infos = await _libraries.LoadPersistedAsync();
        Libraries.Clear();
        foreach (var info in infos)
        {
            Libraries.Add(info);
            if (!info.IsOffline)
            {
                _watcher.Watch(info.RootPath);
            }
        }

        await Gallery.RefreshAsync();
        foreach (var info in Libraries.Where(i => !i.IsOffline).ToList())
        {
            await GenerateThumbsAsync(info.RootPath);
        }

        await Gallery.RefreshAsync();
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
        var info = await _libraries.AddAsync(path);
        var existing = Libraries.FirstOrDefault(l => l.RootPath.Equals(info.RootPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Libraries.Remove(existing);
        }

        Libraries.Add(info);
        _watcher.Watch(info.RootPath);
        await GenerateThumbsAsync(info.RootPath);
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
        if (root is not null)
        {
            var index = _libraries.OpenIndexes.FirstOrDefault(i => i.RootPath.Equals(root, StringComparison.OrdinalIgnoreCase));
            if (index is not null)
            {
                await _libraries.ScanAsync(index);
                await GenerateThumbsAsync(root);
            }
        }
        else
        {
            foreach (var index in _libraries.OpenIndexes.ToList())
            {
                await _libraries.ScanAsync(index);
                await GenerateThumbsAsync(index.RootPath);
            }
        }

        await Gallery.RefreshAsync();
    }

    public void Navigate(RailSection section, string? libraryRoot)
    {
        CurrentSection = section;
        CurrentLibraryRoot = libraryRoot;
        IsSettingsOpen = section == RailSection.Settings;
        Gallery.CloseSuggestions();
        OnPropertyChanged(nameof(PageTitle));
        NotifyTaggingState();
        _ = Gallery.RefreshAsync();
    }

    public void PersistSettings()
    {
        Settings.RailExpanded = RailExpanded;
        Settings.PreviewPaneOpen = IsPreviewOpen;
        _settingsStore.Save(Settings);
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

    public Task SetFavoriteAsync(MediaItem item, bool value) => _libraries.SetFavoriteAsync(item, value);

    public Task<IReadOnlyList<MediaItem>> QueryAsync(MediaQuery query) => _libraries.QueryAsync(query);

    public Task<IReadOnlyList<string>> GetTagsAsync(MediaItem item) => _libraries.GetTagsAsync(item);

    public Task<IReadOnlyList<TagRecord>> SuggestTagsAsync(string? prefix) => _libraries.SuggestTagsAsync(prefix);

    public Task<MediaItem?> SaveTagsAsync(MediaItem item, IReadOnlyList<string> tags) =>
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
        _watcher.Dispose();
        Downloader.Dispose();
        _ = _tagging.DisposeAsync();
    }

    private void OnWatchedLibraryChanged(string root)
    {
        App.DispatcherQueue.TryEnqueue(() => _ = RescanAsync(root));
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
    private void CloseSettings() => Navigate(RailSection.All, null);

    [RelayCommand]
    private void CancelTagging() => _taggingCts?.Cancel();

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

        var enabled = Models.Where(m => m.IsInstalled && m.IsEnabled).Select(m => m.Entry).ToList();
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
        try
        {
            var progress = new Progress<TaggingProgress>(p =>
            {
                TaggingPercent = p.Percent;
                TaggingMessage = p.Phase == "loading"
                    ? p.CurrentFile
                    : $"{p.Done}/{p.Total} · {p.CurrentFile} · {p.Tagged} tagged, {p.Failed} failed";
            });
            _watcher.Pause();
            TaggingRunResult result;
            try
            {
                result = await Task.Run(
                    () => _tagging.RunAsync(images, enabled, Settings, progress, cts.Token, overwrite),
                    cts.Token);
            }
            finally
            {
                _watcher.Resume();
            }
            await Gallery.RefreshAsync();
            TaggingMessage = $"{result.Tagged} tagged, {result.Failed} failed, {result.Skipped} skipped.";
            if (result.Failed > 0)
            {
                ErrorMessage = $"{result.Failed} image{(result.Failed == 1 ? "" : "s")} failed. Review them on the Failed filter.";
            }
        }
        catch (OperationCanceledException)
        {
            TaggingMessage = "Tagging cancelled.";
            await Gallery.RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            TaggingMessage = "Tagging failed to start.";
        }
        finally
        {
            IsTagging = false;
            NotifyTaggingState();
        }
    }

    private async Task GenerateThumbsAsync(string root)
    {
        var query = new MediaQuery { LibraryRoot = root, Kind = MediaKindFilter.All };
        var items = await _libraries.QueryAsync(query);
        var n = 0;
        foreach (var item in items)
        {
            try
            {
                var size = ImageDimensions.TryRead(item.FullPath);
                if (size is { } dims && (item.Width != dims.Width || item.Height != dims.Height))
                {
                    await _libraries.SetDimensionsAsync(item, dims.Width, dims.Height);
                }

                await _thumbs.GenerateAsync(item.LibraryRoot, item.RelPath);
            }
            catch (Exception ex)
            {
                ErrorMessage ??= $"Thumbnail failed for {item.FileName}: {ex.Message}";
            }

            n++;
            if (n % 25 == 0)
            {
                Status = $"Thumbnails {n}/{items.Count}";
            }
        }
    }
}
