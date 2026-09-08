using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seiri.Core;
using Seiri.Core.Models;
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

    public ObservableCollection<MediaItem> Items { get; } = [];
    public ObservableCollection<GalleryGroup> Groups { get; } = [];
    public ObservableCollection<string> SelectedTags { get; } = [];
    public ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    [ObservableProperty]
    public partial MediaItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsSelectMode { get; set; }

    [ObservableProperty]
    public partial int SelectionVersion { get; set; }

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

    public bool ShowTaggingFilters => _shell.CurrentSection == RailSection.Tagging;
    public bool ShowGroups => !IsEmpty && Layout == GalleryLayoutMode.Grid && IsDateSort;
    public bool ShowFlat => !IsEmpty && Layout == GalleryLayoutMode.Grid && !IsDateSort;
    public bool ShowMasonry => !IsEmpty && Layout == GalleryLayoutMode.Masonry;
    public bool ShowRiver => !IsEmpty && Layout == GalleryLayoutMode.River;
    public bool IsDateSort => Sort is SortKey.DateTaken or SortKey.DateAdded or SortKey.DateModified;
    public bool HasQuery => !string.IsNullOrWhiteSpace(SearchText);
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

    public async Task RefreshAsync()
    {
        HasLibraries = _shell.Libraries.Count > 0;
        OnPropertyChanged(nameof(ShowTaggingFilters));

        var query = _shell.CreateQuery();
        var items = await _shell.QueryAsync(query);
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        IsEmpty = items.Count == 0;
        RebuildGroups(items);

        var photos = items.Count(i => i.Kind == MediaKind.Image);
        var videos = items.Count(i => i.Kind == MediaKind.Video);
        Subtitle = $"{photos} photo{(photos == 1 ? "" : "s")}, {videos} video{(videos == 1 ? "" : "s")}";
        if (_shell.CurrentSection == RailSection.Tagging)
        {
            var untaggedQuery = query.Clone();
            untaggedQuery.Tagged = TaggedFilter.Untagged;
            untaggedQuery.Kind = MediaKindFilter.Images;
            var untaggedCount = (await _shell.QueryAsync(untaggedQuery)).Count(i => i.Kind == MediaKind.Image);
            var allQuery = query.Clone();
            allQuery.Tagged = TaggedFilter.All;
            var allCount = (await _shell.QueryAsync(allQuery)).Count;
            Subtitle = $"{untaggedCount} untagged of {allCount}";
        }

        _shell.Status = HasLibraries ? Subtitle : "Add a folder to start";
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowFlat));
        OnPropertyChanged(nameof(ShowMasonry));
        OnPropertyChanged(nameof(ShowRiver));
        OnPropertyChanged(nameof(HasQuery));

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
            }
        }
    }

    public void UpdateTileSize(double availableWidth)
    {
        const double min = 216;
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

        _queryRefresh?.Cancel();
        var cts = _queryRefresh = new CancellationTokenSource();
        try
        {
            await Task.Delay(150, cts.Token);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _queryRefresh?.Cancel();
            _ = RefreshAsync();
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

        await ApplySearchAsync(suggestion.ApplyText);
    }

    [RelayCommand]
    private async Task SearchTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        await ApplySearchAsync(QueryParser.FormatTagToken(tag));
    }

    public void CloseSuggestions()
    {
        Suggestions.Clear();
        SuggestionsOpen = false;
    }

    public async Task UpdateSuggestionsAsync(string text)
    {
        var catalog = await _shell.SuggestTagsAsync(LastNeedle(text));
        var list = QueryParser.Suggest(text, catalog);
        Suggestions.Clear();
        foreach (var item in list)
        {
            Suggestions.Add(item);
        }

        SuggestionsOpen = Suggestions.Count > 0;
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
    }

    [RelayCommand]
    private void SelectNone()
    {
        IsSelectMode = false;
        _selectedKeys.Clear();
        SelectionVersion++;
        SelectedItem = null;
        SelectedTags.Clear();
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
        var parsed = Enum.Parse<SortKey>(key);
        if (Sort == parsed)
        {
            Direction = Direction == SortDir.Desc ? SortDir.Asc : SortDir.Desc;
        }
        else
        {
            Sort = parsed;
            Direction = parsed == SortKey.Name ? SortDir.Asc : SortDir.Desc;
        }

        OnPropertyChanged(nameof(SortLabel));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SetLayoutAsync(string layout)
    {
        Layout = Enum.Parse<GalleryLayoutMode>(layout);
        _shell.Settings.Layout = Layout;
        _shell.PersistSettings();
        OnPropertyChanged(nameof(LayoutLabel));
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowFlat));
        OnPropertyChanged(nameof(ShowMasonry));
        OnPropertyChanged(nameof(ShowRiver));
        await RefreshAsync();
    }

    [RelayCommand]
    private void SetDensity(string density)
    {
        Density = Enum.Parse<LayoutDensity>(density);
        _shell.Settings.LayoutDensity = Density;
        _shell.PersistSettings();
        OnPropertyChanged(nameof(MasonryColumnWidth));
        OnPropertyChanged(nameof(RiverRowHeight));
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
        }

        SelectedItem = item;
        if (!_shell.IsPreviewOpen)
        {
            _shell.IsPreviewOpen = true;
        }

        await LoadTagsAsync(item);
    }

    partial void OnSelectedItemChanged(MediaItem? value) => _shell.NotifyTaggingState();

    [RelayCommand]
    private async Task CopyImageAsync()
    {
        var item = SelectedItem;
        if (item is null || !File.Exists(item.FullPath))
        {
            return;
        }

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

        var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
        await Launcher.LaunchFileAsync(file);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(MediaItem? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        await _shell.SetFavoriteAsync(item, !item.IsFavorite);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (SelectedItem is null || string.IsNullOrWhiteSpace(NewTagText))
        {
            return;
        }

        var tags = SelectedTags.ToList();
        var name = NewTagText.Replace('_', ' ').Trim().ToLowerInvariant();
        if (!tags.Contains(name))
        {
            tags.Add(name);
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

        var tags = SelectedTags.Where(t => !t.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
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
        var tags = await _shell.GetTagsAsync(item);
        SelectedTags.Clear();
        foreach (var tag in tags)
        {
            SelectedTags.Add(tag);
        }
    }

    private void RebuildGroups(IReadOnlyList<MediaItem> items)
    {
        Groups.Clear();
        if (items.Count == 0)
        {
            return;
        }

        if (!ShowGroups)
        {
            var flat = new GalleryGroup { Header = string.Empty };
            foreach (var item in items)
            {
                flat.Items.Add(item);
            }

            Groups.Add(flat);
            return;
        }

        IEnumerable<IGrouping<DateTime, MediaItem>> grouped = Sort switch
        {
            SortKey.DateAdded => items.GroupBy(i => i.AddedAt.LocalDateTime.Date),
            SortKey.DateModified => items.GroupBy(i => i.MtimeUtc.LocalDateTime.Date),
            _ => items.GroupBy(i => i.SortDate.LocalDateTime.Date)
        };

        var ordered = Direction == SortDir.Desc
            ? grouped.OrderByDescending(g => g.Key)
            : grouped.OrderBy(g => g.Key);

        foreach (var day in ordered)
        {
            var group = new GalleryGroup { Header = day.Key.ToString("MMMM d, yyyy") };
            foreach (var item in day)
            {
                group.Items.Add(item);
            }

            Groups.Add(group);
        }
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
