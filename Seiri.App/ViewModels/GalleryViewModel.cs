using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seiri.Core;
using Seiri.Core.Models;
using Seiri.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace Seiri.ViewModels;

public partial class GalleryViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _queryRefresh;

    public GalleryViewModel(ShellViewModel shell)
    {
        _shell = shell;
    }

    public ResettableCollection<MediaItem> Items { get; } = new();
    public ResettableCollection<GalleryEntry> Entries { get; } = new();
    public ObservableCollection<TagRecord> SelectedTags { get; } = [];
    public ResettableCollection<TagRecord> PreviewRatingTags { get; } = new();
    public ResettableCollection<TagRecord> PreviewCharacterTags { get; } = new();
    public ResettableCollection<TagRecord> PreviewCopyrightTags { get; } = new();
    public ResettableCollection<TagRecord> PreviewGeneralTags { get; } = new();
    public bool HasPreviewRating => PreviewRatingTags.Count > 0;
    public bool HasPreviewCharacter => PreviewCharacterTags.Count > 0;
    public bool HasPreviewCopyright => PreviewCopyrightTags.Count > 0;
    public bool HasPreviewGeneral => PreviewGeneralTags.Count > 0;
    public ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    [ObservableProperty]
    public partial MediaItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsSelectMode { get; set; }

    [ObservableProperty]
    public partial int SelectionVersion { get; set; }

    public int SelectedCount => IsSelectMode ? _selectedKeys.Count : SelectedItem is null ? 0 : 1;
    public bool IsMultiPreview => IsSelectMode && _selectedKeys.Count > 1;
    public bool ShowSinglePreview => SelectedItem is not null && !IsMultiPreview;
    public int ExtraSelectedCount => Math.Max(0, _selectedKeys.Count - CollageItems.Count);
    public string MultiMeta { get; private set; } = string.Empty;
    public string FavoriteGlyph => SelectedItem?.IsFavorite == true ? "\uE735" : "\uE734";
    public ResettableCollection<MediaItem> CollageItems { get; } = new();
    public ResettableCollection<TagRecord> CommonTags { get; } = new();
    public bool HasCommonTags => CommonTags.Count > 0;

    [ObservableProperty]
    public partial string BulkTagText { get; set; } = string.Empty;

    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);

    [ObservableProperty]
    public partial MediaKindFilter KindFilter { get; set; } = MediaKindFilter.All;

    [ObservableProperty]
    public partial TaggedFilter TaggedFilter { get; set; } = TaggedFilter.All;

    [ObservableProperty]
    public partial SortKey Sort { get; set; } = SortKey.DateTaken;

    [ObservableProperty]
    public partial SortDir Direction { get; set; } = SortDir.Desc;

    [ObservableProperty]
    public partial GroupKey GroupBy { get; set; } = GroupKey.DateTaken;

    [ObservableProperty]
    public partial GalleryLayoutMode Layout { get; set; } = GalleryLayoutMode.Grid;

    [ObservableProperty]
    public partial LayoutDensity Density { get; set; } = LayoutDensity.Medium;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "0 photos, 0 videos";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool HasLibraries { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewTagText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SuggestionsOpen { get; set; }

    [ObservableProperty]
    public partial double TileSize { get; set; } = 220;

    [ObservableProperty]
    public partial int ImageEpoch { get; set; }

    public bool ShowTaggingFilters => _shell.CurrentSection == RailSection.Tagging;
    public bool ShowGroups => !IsEmpty && Layout == GalleryLayoutMode.Grid && GroupBy != GroupKey.None;
    public bool ShowFlat => !IsEmpty && Layout == GalleryLayoutMode.Grid && GroupBy == GroupKey.None;
    public bool ShowIndexBar => !IsEmpty;
    public bool ShowMasonry => !IsEmpty && Layout == GalleryLayoutMode.Masonry;
    public bool ShowRiver => !IsEmpty && Layout == GalleryLayoutMode.River;
    public bool IsDateSort => Sort is SortKey.DateTaken or SortKey.DateAdded or SortKey.DateModified;
    public bool HasQuery => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowGroupCaption => !IsEmpty && GroupBy != GroupKey.None && !HasQuery;
    public string HeaderGlyph { get; private set; } = "\uE91B";
    public string HeaderTitle { get; private set; } = "Gallery";
    public string HeaderDetail { get; private set; } = string.Empty;
    public bool HasHeaderDetail => !string.IsNullOrEmpty(HeaderDetail);

    [ObservableProperty]
    public partial string CurrentGroupLabel { get; set; } = string.Empty;
    public double MasonryColumnWidth => Density switch
    {
        LayoutDensity.Small => 160,
        LayoutDensity.Large => 300,
        _ => 220
    };
    public double RiverRowHeight => Density switch
    {
        LayoutDensity.Small => 140,
        LayoutDensity.Large => 280,
        _ => 200
    };

    public string KindLabel => KindFilter switch
    {
        MediaKindFilter.Images => "Photos",
        MediaKindFilter.Videos => "Videos",
        _ => "Photos & videos"
    };

    public string SortLabel => $"{Sort} {(Direction == SortDir.Desc ? "↓" : "↑")}";
    public string LayoutLabel => Layout.ToString();
    public bool SortDirAsc => Direction == SortDir.Asc;
    public bool SortDirDesc => Direction == SortDir.Desc;

    public ObservableCollection<FilterTagRow> FilterIncludeTags { get; } = [];
    public ObservableCollection<FilterTagRow> FilterExcludeTags { get; } = [];
    public ObservableCollection<FilterTagRow> FilterCharacterTags { get; } = [];
    public ObservableCollection<FilterChoice> FilterExtensions { get; } = [];
    public ObservableCollection<FilterChoice> FilterFolders { get; } = [];
    public ObservableCollection<FilterChoice> FilterOrientations { get; } =
    [
        new() { Label = "Landscape" },
        new() { Label = "Portrait" },
        new() { Label = "Square" }
    ];
    public ObservableCollection<FilterChoice> FilterAspects { get; } =
    [
        new() { Label = "16:9" },
        new() { Label = "4:3" },
        new() { Label = "3:2" },
        new() { Label = "1:1" },
        new() { Label = "9:16" }
    ];
    public ObservableCollection<FilterChoice> FilterColors { get; } =
        [.. ColorBucket.Names.Select(n => new FilterChoice { Label = n })];
    public string FilterIncludeNeedle { get => field; set { field = value; ApplyFilterNeedles(); } } = string.Empty;
    public string FilterExcludeNeedle { get => field; set { field = value; ApplyFilterNeedles(); } } = string.Empty;
    public string FilterCharacterNeedle { get => field; set { field = value; ApplyFilterNeedles(); } } = string.Empty;
    public string FilterIncludeHeader { get; private set; } = "Include tags";
    public string FilterExcludeHeader { get; private set; } = "Exclude tags";
    public string FilterCharacterHeader { get; private set; } = "Characters";
    public int FilterDateMode { get; set; }
    public int FilterDateField { get; set; }
    public DateTimeOffset? FilterDate { get; set; } = DateTimeOffset.Now;

    public ResettableCollection<TagRecord> TagRiver { get; } = new();
    public ResettableCollection<TagRecord> TagIncludeStaged { get; } = new();
    public ResettableCollection<TagRecord> TagExcludeStaged { get; } = new();
    public bool HasTagStaging => TagIncludeStaged.Count > 0 || TagExcludeStaged.Count > 0;
    public string TagNeedle { get => field; set { field = value; ApplyTagNeedle(); } } = string.Empty;

    private List<FilterTagRow> _includeAll = [];
    private List<FilterTagRow> _excludeAll = [];
    private List<FilterTagRow> _characterAll = [];
    private List<TagRecord> _tagAll = [];

    public async Task RefreshAsync(bool force = false)
    {
        if (!force && _shell.IsTagging && Items.Count > 0)
        {
            return;
        }

        HasLibraries = _shell.Libraries.Count > 0;
        OnPropertyChanged(nameof(ShowTaggingFilters));
        var ownsLoad = !_shell.IsLibraryLoading && Items.Count == 0;
        if (ownsLoad)
        {
            _shell.IsLibraryLoading = true;
            _shell.LoadingMessage = "Loading library…";
        }

        try
        {
        var query = _shell.CreateQuery();
        IReadOnlyList<MediaItem> items;
        try
        {
            items = await Task.Run(() => _shell.QueryAsync(query));
        }
        catch (Exception ex)
        {
            AppLog.Error("Refresh query", ex);
            _shell.ErrorMessage = $"Library query failed: {ex.Message}";
            IsEmpty = Items.Count == 0;
            return;
        }
        IsEmpty = items.Count == 0;
        Items.ReplaceAll(items);
        Entries.ReplaceAll(GalleryGrouping.Build(items, GroupBy, insertHeaders: ShowGroups));
        CurrentGroupLabel = FirstGroupLabel();
        NotifyHeader();

        var photos = 0;
        var videos = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == MediaKind.Image)
            {
                photos++;
            }
            else
            {
                videos++;
            }
        }

        Subtitle = $"{photos} photo{(photos == 1 ? "" : "s")}, {videos} video{(videos == 1 ? "" : "s")}";
        if (_shell.CurrentSection == RailSection.Directory && _shell.CurrentLibraryRoot is not null)
        {
            var lib = _shell.Libraries.FirstOrDefault(l =>
                l.RootPath.Equals(_shell.CurrentLibraryRoot, StringComparison.OrdinalIgnoreCase));
            if (lib?.LastScanAt is { } scan)
            {
                Subtitle += $" · Scanned {FormatAgo(scan)}";
            }
        }

        if (_shell.CurrentSection == RailSection.Tagging)
        {
            var untaggedQuery = query.Clone();
            untaggedQuery.Tagged = TaggedFilter.Untagged;
            untaggedQuery.Kind = MediaKindFilter.Images;
            var untaggedCount = await Task.Run(() => _shell.CountAsync(untaggedQuery));
            var allQuery = query.Clone();
            allQuery.Tagged = TaggedFilter.All;
            var allCount = await Task.Run(() => _shell.CountAsync(allQuery));
            Subtitle = $"{untaggedCount} untagged of {allCount}";
        }

        _shell.Status = HasLibraries ? Subtitle : "Add a folder to start";
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowFlat));
        OnPropertyChanged(nameof(ShowMasonry));
        OnPropertyChanged(nameof(ShowRiver));
        OnPropertyChanged(nameof(ShowIndexBar));
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(ShowGroupCaption));

        if (SelectedItem is not null)
        {
            var match = items.FirstOrDefault(i => i.Id == SelectedItem.Id && i.LibraryRoot == SelectedItem.LibraryRoot);
            SelectedItem = match;
            if (match is not null)
            {
                await LoadTagsAsync(match);
            }
            else
            {
                SelectedTags.Clear();
                NotifyTagGroups();
            }
        }
        }
        catch (Exception ex)
        {
            AppLog.Error("Refresh", ex);
            _shell.ErrorMessage = $"Gallery failed to load: {ex.Message}";
            IsEmpty = Items.Count == 0;
        }
        finally
        {
            if (ownsLoad)
            {
                _shell.IsLibraryLoading = false;
            }
        }
    }

    public void UpdateTileSize(double availableWidth)
    {
        var min = Density switch
        {
            LayoutDensity.Small => 160,
            LayoutDensity.Large => 300,
            _ => 216
        };
        const double gap = 8;
        availableWidth = Math.Max(min, availableWidth);
        var columns = Math.Max(1, (int)Math.Floor((availableWidth + gap) / (min + gap)));
        var tile = (availableWidth - gap * (columns - 1)) / columns;
        if (Math.Abs(tile - TileSize) > 0.5)
        {
            TileSize = tile;
        }
    }

    public async Task ApplySearchAsync(string text)
    {
        _queryRefresh?.Cancel();
        SearchText = text.Trim();
        CloseSuggestions();
        await RefreshAsync();
    }

    public async Task HandleSearchInputAsync(string text)
    {
        await UpdateSuggestionsAsync(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            _queryRefresh?.Cancel();
            await RefreshAsync();
            return;
        }

        if (!_shell.Settings.SearchAsYouType)
        {
            return;
        }

        _queryRefresh?.Cancel();
        var cts = _queryRefresh = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, cts.Token);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task ApplySuggestionAsync(SearchSuggestion suggestion)
    {
        if (suggestion.Kind == "prefix")
        {
            SearchText = suggestion.ApplyText;
            await UpdateSuggestionsAsync(suggestion.ApplyText);
            return;
        }

        if (suggestion.Kind == "action")
        {
            var query = QueryParser.Parse(suggestion.ApplyText);
            var seen = new Dictionary<string, TagClause>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in query.Tags)
            {
                seen[tag.Name] = tag;
            }

            query.Tags = [.. seen.Values];
            await ApplySearchAsync(QueryParser.ToRaw(query));
            return;
        }

        await ApplySearchAsync(suggestion.ApplyText);
    }

    [RelayCommand]
    private async Task SearchTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        await ApplySearchAsync(tag.Contains(':') ? tag : QueryParser.FormatTagToken(tag));
    }

    public void CloseSuggestions()
    {
        Suggestions.Clear();
        SuggestionsOpen = false;
    }

    public async Task UpdateSuggestionsAsync(string text)
    {
        try
        {
            var catalog = await _shell.SuggestTagsAsync(LastNeedle(text));
            var folders = await _shell.ListFolderNamesAsync();
            var list = QueryParser.Suggest(text, catalog, folders);
            Suggestions.Clear();
            foreach (var item in list)
            {
                Suggestions.Add(item);
            }

            SuggestionsOpen = Suggestions.Count > 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("suggestions", ex);
            Suggestions.Clear();
            SuggestionsOpen = false;
        }
    }

    public bool IsChecked(MediaItem? item) =>
        item is not null && _selectedKeys.Contains(KeyOf(item));

    public static string KeyOf(MediaItem item) => $"{item.LibraryRoot}\u001f{item.Id}";

    [RelayCommand]
    private void ToggleSelectMode()
    {
        IsSelectMode = !IsSelectMode;
        if (!IsSelectMode)
        {
            _selectedKeys.Clear();
        }

        SelectionVersion++;
        NotifySelection();
    }

    [RelayCommand]
    private void SelectAll()
    {
        IsSelectMode = true;
        _selectedKeys.Clear();
        foreach (var item in Items)
        {
            _selectedKeys.Add(KeyOf(item));
        }

        SelectionVersion++;
        NotifySelection();
    }

    [RelayCommand]
    private void SelectNone()
    {
        IsSelectMode = false;
        _selectedKeys.Clear();
        SelectionVersion++;
        SelectedItem = null;
        SelectedTags.Clear();
        NotifyTagGroups();
        NotifySelection();
    }

    [RelayCommand]
    private async Task SetKindAsync(string kind)
    {
        KindFilter = Enum.Parse<MediaKindFilter>(kind);
        OnPropertyChanged(nameof(KindLabel));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SetSortAsync(string key)
    {
        Sort = Enum.Parse<SortKey>(key);
        if (Sort == SortKey.Name && Direction == SortDir.Desc)
        {
            Direction = SortDir.Asc;
        }

        OnPropertyChanged(nameof(SortLabel));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SetSortDirAsync(string dir)
    {
        Direction = Enum.Parse<SortDir>(dir);
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(SortDirAsc));
        OnPropertyChanged(nameof(SortDirDesc));
        await RefreshAsync();
    }

    [RelayCommand]
    private void SetGroup(string key)
    {
        GroupBy = Enum.Parse<GroupKey>(key);
        _shell.Settings.GroupBy = GroupBy;
        _shell.PersistSettings();
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowFlat));
        OnPropertyChanged(nameof(ShowIndexBar));
        Entries.ReplaceAll(GalleryGrouping.Build(Items, GroupBy, insertHeaders: ShowGroups));
        CurrentGroupLabel = FirstGroupLabel();
        OnPropertyChanged(nameof(ShowGroupCaption));
    }

    [RelayCommand]
    private void SetLayout(string layout)
    {
        Layout = Enum.Parse<GalleryLayoutMode>(layout);
        _shell.Settings.Layout = Layout;
        _shell.PersistSettings();
        OnPropertyChanged(nameof(LayoutLabel));
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowFlat));
        OnPropertyChanged(nameof(ShowMasonry));
        OnPropertyChanged(nameof(ShowRiver));
        OnPropertyChanged(nameof(ShowIndexBar));
        OnPropertyChanged(nameof(ShowGroupCaption));
        Entries.ReplaceAll(GalleryGrouping.Build(Items, GroupBy, insertHeaders: ShowGroups));
        CurrentGroupLabel = FirstGroupLabel();
    }

    [RelayCommand]
    private void SetDensity(string density)
    {
        Density = Enum.Parse<LayoutDensity>(density);
        _shell.Settings.LayoutDensity = Density;
        _shell.PersistSettings();
        OnPropertyChanged(nameof(MasonryColumnWidth));
        OnPropertyChanged(nameof(RiverRowHeight));
        OnPropertyChanged(nameof(TileSize));
    }

    [RelayCommand]
    private async Task SetTaggedAsync(string tagged)
    {
        TaggedFilter = Enum.Parse<TaggedFilter>(tagged);
        _shell.NotifyTaggingState();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SelectAsync(MediaItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (IsSelectMode)
        {
            var key = KeyOf(item);
            if (!_selectedKeys.Add(key))
            {
                _selectedKeys.Remove(key);
            }

            SelectionVersion++;
            NotifySelection();
        }

        SelectedItem = item;
        if (!_shell.IsPreviewOpen)
        {
            _shell.IsPreviewOpen = true;
        }

        await LoadTagsAsync(item);
    }

    partial void OnSelectedItemChanged(MediaItem? value)
    {
        _shell.NotifyTaggingState();
        OnPropertyChanged(nameof(FavoriteGlyph));
        OnPropertyChanged(nameof(ShowSinglePreview));
    }

    [RelayCommand]
    private async Task CopyImageAsync()
    {
        var item = SelectedItem;
        if (item is null || !File.Exists(item.FullPath))
        {
            return;
        }

        try
        {
        var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems([file]);
        if (item.Kind == MediaKind.Image)
        {
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        }

        Clipboard.SetContent(package);
        _shell.Status = "Copied to clipboard";
        }
        catch (Exception ex)
        {
            AppLog.Error("CopyImage", ex);
            _shell.ErrorMessage = "Could not copy that file.";
        }
    }

    [RelayCommand]
    private async Task TagCurrentAsync()
    {
        if (SelectedItem is not { Kind: MediaKind.Image } item)
        {
            _shell.ErrorMessage = "Select an image to tag.";
            return;
        }

        await _shell.TagItemsAsync([item], overwrite: true, title: "Tag this image", primary: "Tag");
    }

    [RelayCommand]
    private async Task OpenInPhotosAsync(MediaItem? item)
    {
        item ??= SelectedItem;
        if (item is null || !File.Exists(item.FullPath))
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
            await Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenInPhotos", ex);
            _shell.ErrorMessage = "Could not open that file.";
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(MediaItem? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        var next = !item.IsFavorite;
        try
        {
            await _shell.SetFavoriteAsync(item, next);
            item.IsFavorite = next;
            OnPropertyChanged(nameof(FavoriteGlyph));
            SelectionVersion++;
            if (_shell.CurrentSection == RailSection.Favorites)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("ToggleFavorite", ex);
        }
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (SelectedItem is null || string.IsNullOrWhiteSpace(NewTagText))
        {
            return;
        }

        if (!QueryParser.TryParseManualTag(NewTagText, out var name, out var category))
        {
            return;
        }

        var tags = SelectedTags.ToList();
        if (!tags.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(new TagRecord { Name = name, Category = category });
        }

        NewTagText = string.Empty;
        await _shell.SaveTagsAsync(SelectedItem, tags);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string? tag)
    {
        if (SelectedItem is null || string.IsNullOrEmpty(tag))
        {
            return;
        }

        var tags = SelectedTags.Where(t => !t.Name.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
        await _shell.SaveTagsAsync(SelectedItem, tags);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        _queryRefresh?.Cancel();
        SearchText = string.Empty;
        CloseSuggestions();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ChooseSuggestionAsync(SearchSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        await ApplySuggestionAsync(suggestion);
    }

    private async Task LoadTagsAsync(MediaItem item)
    {
        try
        {
            var tags = await _shell.GetTagRecordsAsync(item);
            SelectedTags.Clear();
            foreach (var tag in tags)
            {
                SelectedTags.Add(tag);
            }

            NotifyTagGroups();
        }
        catch (Exception ex)
        {
            AppLog.Error($"LoadTags {item.FileName}", ex);
        }
    }

    private void NotifyTagGroups()
    {
        PreviewRatingTags.ReplaceAll(SelectedTags.Where(t => SidecarFormat.UiGroup(t.Category) == "rating").ToList());
        PreviewCharacterTags.ReplaceAll(SelectedTags.Where(t => SidecarFormat.UiGroup(t.Category) == "character").ToList());
        PreviewCopyrightTags.ReplaceAll(SelectedTags.Where(t => SidecarFormat.UiGroup(t.Category) == "copyright").ToList());
        PreviewGeneralTags.ReplaceAll(SelectedTags.Where(t => SidecarFormat.UiGroup(t.Category) == "general").ToList());
        OnPropertyChanged(nameof(HasPreviewRating));
        OnPropertyChanged(nameof(HasPreviewCharacter));
        OnPropertyChanged(nameof(HasPreviewCopyright));
        OnPropertyChanged(nameof(HasPreviewGeneral));
    }

    public void NotifyImageSourceChanged()
    {
        ImageEpoch++;
    }

    public async Task OpenFilterAsync()
    {
        IReadOnlyList<TagRecord> tags;
        try
        {
            tags = await _shell.ListTagsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenFilter", ex);
            return;
        }
        _includeAll = tags.Select(t => new FilterTagRow { Name = t.Name, Category = t.Category, UseCount = t.UseCount }).ToList();
        _excludeAll = tags.Select(t => new FilterTagRow { Name = t.Name, Category = t.Category, UseCount = t.UseCount }).ToList();
        _characterAll = tags
            .Where(t => t.Category == "character")
            .Select(t => new FilterTagRow { Name = t.Name, Category = t.Category, UseCount = t.UseCount })
            .ToList();
        ApplyFilterNeedles();

        FilterExtensions.Clear();
        foreach (var ext in await _shell.ListExtensionsAsync())
        {
            FilterExtensions.Add(new FilterChoice { Label = ext });
        }

        FilterFolders.Clear();
        foreach (var folder in await _shell.ListFolderNamesAsync())
        {
            FilterFolders.Add(new FilterChoice { Label = folder });
        }

        HydrateFiltersFromSearch();
        RefreshFilterSummaries();
    }

    public async Task ApplyFilterDraftAsync()
    {
        var query = new MediaQuery
        {
            Kind = KindFilter,
            Direction = Direction,
            Sort = Sort
        };
        foreach (var row in _includeAll.Where(r => r.IsSelected))
        {
            query.Tags.Add(new TagClause { Name = row.Name, Category = row.Category == "character" ? "character" : null });
        }

        foreach (var row in _excludeAll.Where(r => r.IsSelected))
        {
            query.Tags.Add(new TagClause { Name = row.Name, Exclude = true, Category = row.Category == "character" ? "character" : null });
        }

        foreach (var row in _characterAll.Where(r => r.IsSelected))
        {
            query.Tags.Add(new TagClause { Name = row.Name, Category = "character" });
        }

        foreach (var ext in FilterExtensions.Where(e => e.IsSelected))
        {
            query.Extensions.Add(ext.Label);
        }

        foreach (var shape in FilterOrientations.Where(s => s.IsSelected))
        {
            query.Orientations.Add(shape.Label.ToLowerInvariant());
        }

        foreach (var aspect in FilterAspects.Where(s => s.IsSelected))
        {
            query.Aspects.Add(aspect.Label);
        }

        foreach (var color in FilterColors.Where(s => s.IsSelected))
        {
            query.Colors.Add(color.Label);
        }

        query.Folders = FilterFolders.Where(f => f.IsSelected).Select(f => f.Label).ToList();
        if (query.Folders.Count == 1)
        {
            query.Folder = query.Folders[0];
        }

        query.DateField = (DateField)Math.Clamp(FilterDateField, 0, 2);
        if (FilterDate is { } picked && FilterDateMode is 1 or 2 or 3)
        {
            var day = picked.Date;
            switch (FilterDateMode)
            {
                case 1:
                    query.DateAfter = day;
                    break;
                case 2:
                    query.DateBefore = day;
                    break;
                default:
                    query.DateExact = day;
                    break;
            }
        }

        await ApplySearchAsync(QueryParser.ToRaw(query));
    }

    public async Task LoadTagBrowserAsync()
    {
        try
        {
            _tagAll = [.. await _shell.ListTagsAsync()];
            ApplyTagNeedle();
            HydrateTagStagingFromSearch();
        }
        catch (Exception ex)
        {
            AppLog.Error("LoadTagBrowser", ex);
        }
    }

    private void HydrateFiltersFromSearch()
    {
        var query = QueryParser.Parse(SearchText);
        foreach (var row in _includeAll)
        {
            row.IsSelected = query.Tags.Any(t => !t.Exclude && NamesMatch(t.Name, row.Name)
                && (string.IsNullOrEmpty(t.Category) || t.Category is "tag" or "general"
                    || t.Category.Equals(row.Category, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var row in _excludeAll)
        {
            row.IsSelected = query.Tags.Any(t => t.Exclude && NamesMatch(t.Name, row.Name));
        }

        foreach (var row in _characterAll)
        {
            row.IsSelected = query.Tags.Any(t => !t.Exclude && NamesMatch(t.Name, row.Name)
                && t.Category == "character");
        }

        foreach (var ext in FilterExtensions)
        {
            ext.IsSelected = query.Extensions.Contains(ext.Label);
        }

        foreach (var folder in FilterFolders)
        {
            folder.IsSelected = query.Folders.Any(f => f.Equals(folder.Label, StringComparison.OrdinalIgnoreCase))
                || (query.Folder?.Equals(folder.Label, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        foreach (var shape in FilterOrientations)
        {
            shape.IsSelected = query.Orientations.Contains(shape.Label);
        }

        foreach (var aspect in FilterAspects)
        {
            aspect.IsSelected = query.Aspects.Contains(aspect.Label);
        }

        foreach (var color in FilterColors)
        {
            color.IsSelected = query.Colors.Contains(color.Label);
        }

        if (query.DateExact is { } exact)
        {
            FilterDateMode = 3;
            FilterDate = new DateTimeOffset(exact, TimeSpan.Zero);
        }
        else if (query.DateAfter is { } after)
        {
            FilterDateMode = 1;
            FilterDate = new DateTimeOffset(after, TimeSpan.Zero);
        }
        else if (query.DateBefore is { } before)
        {
            FilterDateMode = 2;
            FilterDate = new DateTimeOffset(before, TimeSpan.Zero);
        }
        else
        {
            FilterDateMode = 0;
        }

        FilterDateField = (int)query.DateField;
        OnPropertyChanged(nameof(FilterDateMode));
        OnPropertyChanged(nameof(FilterDateField));
        OnPropertyChanged(nameof(FilterDate));
    }

    private void HydrateTagStagingFromSearch()
    {
        var query = QueryParser.Parse(SearchText);
        TagIncludeStaged.Clear();
        TagExcludeStaged.Clear();
        foreach (var tag in query.Tags)
        {
            var record = new TagRecord
            {
                Name = tag.Name,
                Category = string.IsNullOrEmpty(tag.Category) || tag.Category == "tag" ? "general" : tag.Category
            };
            if (tag.Exclude)
            {
                TagExcludeStaged.Add(record);
            }
            else
            {
                TagIncludeStaged.Add(record);
            }
        }

        OnPropertyChanged(nameof(HasTagStaging));
    }

    private static bool NamesMatch(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private void ApplyTagNeedle()
    {
        var needle = TagNeedle?.Trim() ?? string.Empty;
        if (needle.Length == 0)
        {
            TagRiver.ReplaceAll(_tagAll);
            return;
        }

        TagRiver.ReplaceAll(_tagAll.Where(t => TagMatch.Contains(t.Name, needle)).ToList());
    }

    [RelayCommand]
    private void ClearTagStaging()
    {
        TagIncludeStaged.Clear();
        TagExcludeStaged.Clear();
        OnPropertyChanged(nameof(HasTagStaging));
    }

    [RelayCommand]
    private void StageInclude(TagRecord? tag)
    {
        if (tag is null)
        {
            return;
        }

        RemoveStaged(TagExcludeStaged, tag.Name);
        if (TagIncludeStaged.Any(t => t.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase)))
        {
            OnPropertyChanged(nameof(HasTagStaging));
            return;
        }

        TagIncludeStaged.Add(tag);
        OnPropertyChanged(nameof(HasTagStaging));
    }

    [RelayCommand]
    private void StageExclude(TagRecord? tag)
    {
        if (tag is null)
        {
            return;
        }

        RemoveStaged(TagIncludeStaged, tag.Name);
        if (TagExcludeStaged.Any(t => t.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase)))
        {
            OnPropertyChanged(nameof(HasTagStaging));
            return;
        }

        TagExcludeStaged.Add(tag);
        OnPropertyChanged(nameof(HasTagStaging));
    }

    private static void RemoveStaged(ResettableCollection<TagRecord> list, string name)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
            }
        }
    }

    [RelayCommand]
    private void UnstageInclude(TagRecord? tag)
    {
        if (tag is null)
        {
            return;
        }

        RemoveStaged(TagIncludeStaged, tag.Name);
        OnPropertyChanged(nameof(HasTagStaging));
    }

    [RelayCommand]
    private void UnstageExclude(TagRecord? tag)
    {
        if (tag is null)
        {
            return;
        }

        RemoveStaged(TagExcludeStaged, tag.Name);
        OnPropertyChanged(nameof(HasTagStaging));
    }

    [RelayCommand]
    private async Task ApplyTagStagingAsync()
    {
        var query = new MediaQuery();
        foreach (var tag in TagIncludeStaged)
        {
            query.Tags.Add(new TagClause { Name = tag.Name, Category = tag.Category == "general" ? null : tag.Category });
        }

        foreach (var tag in TagExcludeStaged)
        {
            query.Tags.Add(new TagClause { Name = tag.Name, Exclude = true, Category = tag.Category == "general" ? null : tag.Category });
        }

        _shell.Navigate(RailSection.All, null);
        await ApplySearchAsync(QueryParser.ToRaw(query));
    }

    [RelayCommand]
    private async Task SearchRiverTagAsync(TagRecord? tag)
    {
        if (tag is null)
        {
            return;
        }

        _shell.Navigate(RailSection.All, null);
        await ApplySearchAsync(QueryParser.FormatTagToken(tag.Name, category: tag.Category));
    }

    private void ApplyFilterNeedles()
    {
        ReplaceFilterList(FilterIncludeTags, _includeAll, FilterIncludeNeedle);
        ReplaceFilterList(FilterExcludeTags, _excludeAll, FilterExcludeNeedle);
        ReplaceFilterList(FilterCharacterTags, _characterAll, FilterCharacterNeedle);
    }

    private static void ReplaceFilterList(ObservableCollection<FilterTagRow> target, List<FilterTagRow> source, string needle)
    {
        target.Clear();
        var n = needle?.Trim() ?? string.Empty;
        foreach (var row in source)
        {
            if (n.Length == 0 || TagMatch.Contains(row.Name, n))
            {
                target.Add(row);
                if (target.Count >= 400)
                {
                    break;
                }
            }
        }
    }

    public void RefreshFilterSummaries()
    {
        FilterIncludeHeader = HeaderCount("Include tags", _includeAll);
        FilterExcludeHeader = HeaderCount("Exclude tags", _excludeAll);
        FilterCharacterHeader = HeaderCount("Characters", _characterAll);
        OnPropertyChanged(nameof(FilterIncludeHeader));
        OnPropertyChanged(nameof(FilterExcludeHeader));
        OnPropertyChanged(nameof(FilterCharacterHeader));
    }

    private static string HeaderCount(string title, List<FilterTagRow> rows)
    {
        var n = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsSelected)
            {
                n++;
            }
        }

        return n > 0 ? $"{title} ({n})" : title;
    }

    public void UpdateCurrentGroup(double offset, IReadOnlyList<LayoutRect> rects)
    {
        if (!ShowGroupCaption)
        {
            CurrentGroupLabel = string.Empty;
            return;
        }

        CurrentGroupLabel = GalleryGrouping.LabelAt(Entries, rects, offset);
    }

    public void NotifyHeader()
    {
        var section = _shell.CurrentSection;
        var query = QueryParser.Parse(SearchText);
        if (HasQuery)
        {
            HeaderGlyph = "\uE721";
            HeaderTitle = "Search";
            HeaderDetail = SearchSummary(query);
            if (section == RailSection.Directory && _shell.CurrentLibraryRoot is not null)
            {
                var folder = new DirectoryInfo(_shell.CurrentLibraryRoot).Name;
                HeaderDetail = string.IsNullOrEmpty(HeaderDetail) ? folder : HeaderDetail + " · " + folder;
            }
        }
        else if (section == RailSection.Directory && _shell.CurrentLibraryRoot is not null)
        {
            HeaderGlyph = "\uE8B7";
            HeaderTitle = new DirectoryInfo(_shell.CurrentLibraryRoot).Name;
            HeaderDetail = _shell.CurrentLibraryRoot;
        }
        else if (section == RailSection.Favorites)
        {
            HeaderGlyph = "\uE00B";
            HeaderTitle = "Favorites";
            HeaderDetail = string.Empty;
        }
        else if (section == RailSection.Tagging)
        {
            HeaderGlyph = "\uE1CB";
            HeaderTitle = "Tagging";
            HeaderDetail = string.Empty;
        }
        else
        {
            HeaderGlyph = "\uE91B";
            HeaderTitle = "Gallery";
            HeaderDetail = string.Empty;
        }

        OnPropertyChanged(nameof(HeaderGlyph));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderDetail));
        OnPropertyChanged(nameof(HasHeaderDetail));
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(ShowGroupCaption));
        _shell.NotifyPageTitle();
    }

    private string FirstGroupLabel()
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            if (entry.IsHeader)
            {
                return entry.Header;
            }

            if (!string.IsNullOrEmpty(entry.GroupLabel))
            {
                return entry.GroupLabel;
            }
        }

        return string.Empty;
    }

    private static string SearchSummary(MediaQuery query)
    {
        var parts = new List<string>();
        var include = query.Tags.Where(t => !t.Exclude).Select(t => t.Name).ToList();
        var exclude = query.Tags.Where(t => t.Exclude).Select(t => t.Name).ToList();
        if (include.Count > 0)
        {
            parts.Add(string.Join(", ", include));
        }

        if (exclude.Count > 0)
        {
            parts.Add("not " + string.Join(", ", exclude));
        }

        var folders = query.Folders.Count > 0
            ? query.Folders
            : string.IsNullOrWhiteSpace(query.Folder) ? [] : [query.Folder];
        if (folders.Count > 0)
        {
            parts.Add(string.Join(", ", folders));
        }

        return string.Join(" · ", parts);
    }

    partial void OnSearchTextChanged(string value) => NotifyHeader();

    public IReadOnlyList<MediaItem> SelectedMedia()
    {
        if (IsSelectMode && _selectedKeys.Count > 0)
        {
            if (_selectedKeys.Count == Items.Count)
            {
                return Items;
            }

            return Items.Where(i => _selectedKeys.Contains(KeyOf(i))).ToList();
        }

        return SelectedItem is null ? [] : [SelectedItem];
    }

    public void NotifySelection()
    {
        var selected = SelectedMedia();
        CollageItems.ReplaceAll(selected.Take(9).ToList());
        var bytes = selected.Sum(i => i.ByteSize);
        var folders = selected.Select(i => i.LibraryRoot).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        MultiMeta = selected.Count == 0
            ? string.Empty
            : $"{selected.Count} selected · {bytes / (1024.0 * 1024.0):0.0} MB · {folders} folder{(folders == 1 ? "" : "s")}";
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsMultiPreview));
        OnPropertyChanged(nameof(ShowSinglePreview));
        OnPropertyChanged(nameof(ExtraSelectedCount));
        OnPropertyChanged(nameof(MultiMeta));
        OnPropertyChanged(nameof(FavoriteGlyph));
        AppLog.Run(() => LoadCommonTagsAsync(selected), "common tags");
    }

    private async Task LoadCommonTagsAsync(IReadOnlyList<MediaItem> selected)
    {
        if (selected.Count <= 1 || selected.Count > 100)
        {
            CommonTags.ReplaceAll([]);
            OnPropertyChanged(nameof(HasCommonTags));
            return;
        }

        Dictionary<string, TagRecord>? intersection = null;
        try
        {
        foreach (var item in selected)
        {
            var tags = await _shell.GetTagRecordsAsync(item);
            var map = tags.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            if (intersection is null)
            {
                intersection = map;
            }
            else
            {
                foreach (var key in intersection.Keys.ToList())
                {
                    if (!map.ContainsKey(key))
                    {
                        intersection.Remove(key);
                    }
                }
            }
        }

        CommonTags.ReplaceAll(intersection?.Values.OrderBy(t => t.Name).ToList() ?? []);
        OnPropertyChanged(nameof(HasCommonTags));
        }
        catch (Exception ex)
        {
            AppLog.Error("LoadCommonTags", ex);
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(SelectedItem.FullPath);
        Clipboard.SetContent(package);
        _shell.Status = "Path copied";
    }

    [RelayCommand]
    private void CopyTags()
    {
        if (SelectedTags.Count == 0)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(string.Join(", ", SelectedTags.Select(t => t.Name)));
        Clipboard.SetContent(package);
        _shell.Status = "Tags copied";
    }

    [RelayCommand]
    private async Task ClearTagsAsync()
    {
        foreach (var item in SelectedMedia())
        {
            await _shell.SaveTagsAsync(item, Array.Empty<TagRecord>());
        }

        await RefreshAsync();
        NotifySelection();
    }

    [RelayCommand]
    private async Task FavoriteSelectedAsync()
    {
        var items = SelectedMedia();
        if (items.Count == 0)
        {
            return;
        }

        var makeFav = items.Any(i => !i.IsFavorite);
        foreach (var item in items)
        {
            await _shell.SetFavoriteAsync(item, makeFav);
            item.IsFavorite = makeFav;
        }

        OnPropertyChanged(nameof(FavoriteGlyph));
        SelectionVersion++;
        NotifySelection();
        if (_shell.CurrentSection == RailSection.Favorites)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        var items = SelectedMedia().Where(i => File.Exists(i.FullPath)).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var files = new List<StorageFile>();
        foreach (var item in items)
        {
            files.Add(await StorageFile.GetFileFromPathAsync(item.FullPath));
        }

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems(files);
        if (items.Count <= 9 && items[0].Kind == MediaKind.Image)
        {
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(files[0]));
        }

        Clipboard.SetContent(package);
        _shell.Status = items.Count == 1 ? "Copied to clipboard" : $"Copied {items.Count} files";
    }

    [RelayCommand]
    private async Task TagSelectedAsync()
    {
        var images = SelectedMedia().Where(i => i.Kind == MediaKind.Image).ToList();
        await _shell.TagItemsAsync(images, overwrite: true, title: "Tag selected", primary: "Tag");
        NotifySelection();
    }

    [RelayCommand]
    private async Task BulkAddTagAsync()
    {
        if (!QueryParser.TryParseManualTag(BulkTagText, out var name, out var category))
        {
            return;
        }

        BulkTagText = string.Empty;
        _shell.PauseWatcher();
        try
        {
            foreach (var item in SelectedMedia())
            {
                var tags = (await _shell.GetTagRecordsAsync(item)).ToList();
                if (!tags.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    tags.Add(new TagRecord { Name = name, Category = category });
                    await _shell.SaveTagsAsync(item, tags);
                }
            }
        }
        finally
        {
            _shell.ResumeWatcher();
        }

        await RefreshAsync();
        NotifySelection();
    }

    [RelayCommand]
    private async Task BulkRemoveTagAsync(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        _shell.PauseWatcher();
        try
        {
            foreach (var item in SelectedMedia())
            {
                var tags = (await _shell.GetTagRecordsAsync(item))
                    .Where(t => !t.Name.Equals(tag, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                await _shell.SaveTagsAsync(item, tags);
            }
        }
        finally
        {
            _shell.ResumeWatcher();
        }

        await RefreshAsync();
        NotifySelection();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync(bool permanent)
    {
        var items = SelectedMedia();
        if (items.Count == 0)
        {
            return;
        }

        _shell.PauseWatcher();
        try
        {
            foreach (var item in items)
            {
                await DeleteFilesAsync(item, permanent);
                await _shell.DeleteItemAsync(item);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DeleteSelected", ex);
            _shell.ErrorMessage = "Could not delete every selected file.";
        }
        finally
        {
            _shell.ResumeWatcher();
        }

        _selectedKeys.Clear();
        SelectedItem = null;
        SelectionVersion++;
        await RefreshAsync();
        NotifySelection();
    }

    public async Task RecycleItemsAsync(IReadOnlyList<MediaItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        _shell.PauseWatcher();
        try
        {
            foreach (var item in items)
            {
                await DeleteFilesAsync(item, permanent: false);
                await _shell.DeleteItemAsync(item);
            }
        }
        finally
        {
            _shell.ResumeWatcher();
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RenameSelectedAsync(string? stem)
    {
        if (SelectedItem is null || string.IsNullOrWhiteSpace(stem))
        {
            return;
        }

        var item = SelectedItem;
        var plan = MediaPaths.PlanRename(item, stem);
        var dest = GeneratedLayout.ToFullPath(item.LibraryRoot, plan.RelPath);
        if (File.Exists(dest))
        {
            _shell.ErrorMessage = "A file with that name already exists.";
            return;
        }

        _shell.PauseWatcher();
        try
        {
            MoveIfExists(item.FullPath, dest);
            MoveIfExists(
                GeneratedLayout.ToFullPath(item.LibraryRoot, MediaPaths.SidecarRelative(item)),
                GeneratedLayout.ToFullPath(item.LibraryRoot, plan.SidecarRel));
            var oldThumb = item.ThumbFullPath;
            var newThumb = GeneratedLayout.ToFullPath(item.LibraryRoot, plan.ThumbRel);
            if (!string.IsNullOrEmpty(oldThumb))
            {
                MoveIfExists(oldThumb, newThumb);
            }

            await _shell.RenameItemAsync(item, plan);
        }
        catch (Exception ex)
        {
            AppLog.Error("Rename", ex);
            _shell.ErrorMessage = $"Rename failed: {ex.Message}";
        }
        finally
        {
            _shell.ResumeWatcher();
        }

        await RefreshAsync();
    }

    private static void MoveIfExists(string source, string dest)
    {
        if (!File.Exists(source))
        {
            return;
        }

        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.Move(source, dest, overwrite: false);
    }

    private static async Task DeleteFilesAsync(MediaItem item, bool permanent)
    {
        var paths = new List<string> { item.FullPath };
        var sidecar = GeneratedLayout.ToFullPath(item.LibraryRoot, MediaPaths.SidecarRelative(item));
        paths.Add(sidecar);
        if (!string.IsNullOrEmpty(item.ThumbFullPath))
        {
            paths.Add(item.ThumbFullPath);
        }

        foreach (var path in paths.Where(File.Exists))
        {
            if (permanent)
            {
                File.Delete(path);
                continue;
            }

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                await file.DeleteAsync(StorageDeleteOption.Default);
            }
            catch
            {
                File.Delete(path);
            }
        }
    }

    public async Task<IReadOnlyList<string>> SuggestAddTagsAsync(string text)
    {
        var (needle, category) = QueryParser.ManualTagNeedle(text);
        var catalog = await _shell.SuggestTagsAsync(needle);
        return catalog
            .Where(t => category is null or "general" or "tag"
                || t.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(t => TagMatch.Contains(t.Name, needle))
            .OrderBy(t => TagMatch.Rank(t.Name, needle))
            .Take(12)
            .Select(t => category is null or "general" or "tag" ? t.Name : $"{category}:{t.Name}")
            .ToList();
    }

    public IReadOnlyList<IReadOnlyList<MediaItem>> DuplicateGroups { get; private set; } = [];
    public IReadOnlyList<(MediaItem Item, float Score)> SimilarHits { get; private set; } = [];

    [RelayCommand]
    private async Task FindDuplicatesAsync()
    {
        try
        {
            DuplicateGroups = await Task.Run(() => _shell.ListDuplicateGroupsAsync());
        }
        catch (Exception ex)
        {
            AppLog.Error("FindDuplicates", ex);
            _shell.ErrorMessage = $"Duplicate search failed: {ex.Message}";
            DuplicateGroups = [];
        }

        OnPropertyChanged(nameof(DuplicateGroups));
    }

    [RelayCommand]
    private async Task FindSimilarAsync()
    {
        if (SelectedItem is null)
        {
            SimilarHits = [];
            OnPropertyChanged(nameof(SimilarHits));
            return;
        }

        try
        {
            SimilarHits = await _shell.FindSimilarAsync(SelectedItem);
        }
        catch (Exception ex)
        {
            AppLog.Error("Find similar command", ex);
            _shell.ErrorMessage = $"Similar search failed: {ex.Message}";
            SimilarHits = [];
        }

        OnPropertyChanged(nameof(SimilarHits));
    }

    public static string FormatAgo(DateTimeOffset when)
    {
        var ago = DateTimeOffset.UtcNow - when.ToUniversalTime();
        if (ago.TotalMinutes < 1)
        {
            return "just now";
        }

        if (ago.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)ago.TotalMinutes)} min ago";
        }

        if (ago.TotalHours < 24)
        {
            return $"{Math.Max(1, (int)ago.TotalHours)} h ago";
        }

        return when.ToLocalTime().ToString("d");
    }

    public void ExclusiveFilterCheck(FilterTagRow row)
    {
        if (!row.IsSelected)
        {
            RefreshFilterSummaries();
            return;
        }

        foreach (var other in _includeAll.Concat(_excludeAll).Concat(_characterAll))
        {
            if (!ReferenceEquals(other, row)
                && other.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase))
            {
                other.IsSelected = false;
            }
        }

        RefreshFilterSummaries();
    }

    private static string LastNeedle(string text)
    {
        var tokens = QueryParser.Tokenize(text);
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var last = tokens[^1].TrimStart('-');
        var idx = last.IndexOf(':');
        var value = idx >= 0 ? last[(idx + 1)..] : last;
        return value.Trim('"').Replace('_', ' ');
    }
}
