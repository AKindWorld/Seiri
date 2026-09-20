using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Seiri.Core;
using Seiri.Core.Models;
using Seiri.Services;
using Seiri.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Seiri.Views;

public sealed partial class GalleryPage : Page
{
    public static readonly DependencyProperty TileSizeProperty = DependencyProperty.Register(
        nameof(TileSize),
        typeof(double),
        typeof(GalleryPage),
        new PropertyMetadata(220d));

    public ShellViewModel ViewModel => App.Shell;
    public GalleryViewModel Gallery => ViewModel.Gallery;

    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    public GalleryPage()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            RecalcTileSize();
            SyncGalleryLayout();
        };
        Gallery.Entries.CollectionChanged += (_, _) =>
        {
            if (GalleryLayout is not null)
            {
                GalleryLayout.Entries = Gallery.Entries;
                GalleryLayout.Refresh();
            }

            QueueIndexLabels();
        };
        Gallery.PropertyChanged += OnGalleryChanged;
    }

    private void OnGalleryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.Layout)
            or nameof(GalleryViewModel.Density)
            or nameof(GalleryViewModel.TileSize)
            or nameof(GalleryViewModel.MasonryColumnWidth)
            or nameof(GalleryViewModel.RiverRowHeight)
            or nameof(GalleryViewModel.ShowGroups)
            or nameof(GalleryViewModel.ShowIndexBar)
            or nameof(GalleryViewModel.GroupBy))
        {
            SyncGalleryLayout();
        }
    }

    private void SyncGalleryLayout()
    {
        if (GalleryLayout is null)
        {
            return;
        }

        GalleryLayout.Entries = Gallery.Entries;
        GalleryLayout.Refresh();
        QueueIndexLabels();
        QueueAttachIndexBar();
    }

    private void OnTileHostSizeChanged(object sender, SizeChangedEventArgs e) => RecalcTileSize();

    private double _lastLayoutWidth;
    private bool _indexBarAttached;
    private string _labelSignature = string.Empty;

    private void RecalcTileSize()
    {
        var width = GalleryScroll?.ActualWidth > 0
            ? GalleryScroll.ActualWidth
            : TileHost.ActualWidth;
        if (width <= 0)
        {
            width = Math.Max(0, ActualWidth - 48);
        }

        if (width <= 0)
        {
            return;
        }

        var oldTile = TileSize;
        Gallery.UpdateTileSize(width);
        TileSize = Gallery.TileSize;
        if (Math.Abs(width - _lastLayoutWidth) < 1 && Math.Abs(TileSize - oldTile) < 0.5)
        {
            return;
        }

        _lastLayoutWidth = width;
        SyncGalleryLayout();
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is MediaTile tile)
        {
            tile.Unload();
        }
    }

    public static Visibility EmptyVis(bool isEmpty, bool hasLibraries, bool loading) =>
        isEmpty && !hasLibraries && !loading ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NoMatchVis(bool isEmpty, bool hasLibraries, bool loading) =>
        isEmpty && hasLibraries && !loading ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility BoolVis(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility GalleryVis(bool empty) =>
        empty ? Visibility.Collapsed : Visibility.Visible;

    public static string EmptyMatchTitle(bool tagging, TaggedFilter filter)
    {
        if (tagging && filter == TaggedFilter.Failed)
        {
            return "No failed images";
        }

        if (tagging && filter == TaggedFilter.Untagged)
        {
            return "Everything is tagged";
        }

        return "No items match";
    }

    public static bool Match(MediaKindFilter value, string name) =>
        string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public static bool Match(SortKey value, string name) =>
        string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public static bool Match(SortDir value, string name) =>
        string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public static bool Match(GroupKey value, string name) =>
        string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public static bool Match(GalleryLayoutMode value, string name) =>
        string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public static bool Match(LayoutDensity value, string name) =>
        string.Equals(value.ToString(), name, StringComparison.Ordinal);

    private async void OnTaggedFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag)
        {
            await Gallery.SetTaggedCommand.ExecuteAsync(tag);
        }
    }

    private async void OnFilterOpened(object sender, object e)
    {
        try
        {
            await Gallery.OpenFilterAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenFilter UI", ex);
        }
    }

    private void OnIncludeNeedle(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            Gallery.FilterIncludeNeedle = box.Text;
        }
    }

    private void OnExcludeNeedle(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            Gallery.FilterExcludeNeedle = box.Text;
        }
    }

    private void OnCharacterNeedle(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            Gallery.FilterCharacterNeedle = box.Text;
        }
    }

    private async void OnApplyFilters(object sender, RoutedEventArgs e)
    {
        FilterButton.Flyout?.Hide();
        try
        {
            await Gallery.ApplyFilterDraftAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("ApplyFilters", ex);
        }
    }

    private void OnCancelFilters(object sender, RoutedEventArgs e) => FilterButton.Flyout?.Hide();

    private async void OnClearFilters(object sender, RoutedEventArgs e)
    {
        FilterButton.Flyout?.Hide();
        await Gallery.ClearSearchCommand.ExecuteAsync(null);
    }

    private void OnGalleryScrollLoaded(object sender, RoutedEventArgs e)
    {
        RecalcTileSize();
        QueueAttachIndexBar();
        UpdateGroupCaption();
    }

    private void OnGalleryScrollSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalcTileSize();
        QueueAttachIndexBar();
        UpdateGroupCaption();
    }

    private void OnGalleryViewChanged(ScrollView sender, object args) => UpdateGroupCaption();

    private void UpdateGroupCaption()
    {
        if (GalleryLayout is null || GalleryScroll is null)
        {
            return;
        }

        Gallery.UpdateCurrentGroup(GalleryScroll.VerticalOffset, GalleryLayout.LastRects);
    }

    private void OnFilterCheck(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: FilterTagRow row })
        {
            Gallery.ExclusiveFilterCheck(row);
            return;
        }

        Gallery.RefreshFilterSummaries();
    }

    private void OnGalleryWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
        {
            return;
        }

        var delta = e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender).Properties.MouseWheelDelta;
        var next = Gallery.Density;
        if (delta > 0)
        {
            next = Gallery.Density switch
            {
                LayoutDensity.Large => LayoutDensity.Medium,
                LayoutDensity.Medium => LayoutDensity.Small,
                _ => LayoutDensity.Small
            };
        }
        else if (delta < 0)
        {
            next = Gallery.Density switch
            {
                LayoutDensity.Small => LayoutDensity.Medium,
                LayoutDensity.Medium => LayoutDensity.Large,
                _ => LayoutDensity.Large
            };
        }

        if (next != Gallery.Density)
        {
            Gallery.SetDensityCommand.Execute(next.ToString());
            RecalcTileSize();
        }

        e.Handled = true;
    }

    private void QueueAttachIndexBar()
    {
        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AttachIndexBar);
    }

    private void AttachIndexBar()
    {
        if (_indexBarAttached
            || IndexBar is null
            || GalleryScroll?.ScrollPresenter is not { } presenter
            || GalleryLayout is null)
        {
            return;
        }

        if (GalleryScroll.ActualWidth <= 0 || GalleryLayout.LastExtentHeight <= 0)
        {
            return;
        }

        try
        {
            presenter.VerticalScrollController = IndexBar.ScrollController;
            _indexBarAttached = true;
            IndexBar.SmallChange = Math.Max(48, TileSize);
            UpdateIndexLabels();
        }
        catch (Exception ex)
        {
            AppLog.Error("AttachIndexBar", ex);
        }
    }

    private void QueueIndexLabels()
    {
        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateIndexLabels);
    }

    private void UpdateIndexLabels()
    {
        if (IndexBar is null || GalleryLayout is null)
        {
            return;
        }

        var marks = GalleryGrouping.Marks(Gallery.Entries, GalleryLayout.LastRects);
        var majors = marks.Where(m => m.Major).ToList();
        if (majors.Count == 0)
        {
            majors = [.. marks];
        }

        const int cap = 48;
        if (majors.Count > cap)
        {
            var sampled = new List<IndexMark>(cap);
            for (var i = 0; i < cap; i++)
            {
                sampled.Add(majors[i * (majors.Count - 1) / (cap - 1)]);
            }

            majors = sampled;
        }

        var signature = string.Join('|', majors.Select(m => $"{m.TrackLabel}:{m.Y:0}"));
        if (signature == _labelSignature)
        {
            return;
        }

        _labelSignature = signature;
        IndexBar.Labels.Clear();
        foreach (var mark in majors)
        {
            var text = string.IsNullOrEmpty(mark.TrackLabel) ? mark.Caption : mark.TrackLabel;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            IndexBar.Labels.Add(new AnnotatedScrollBarLabel(text, mark.Y));
        }
    }

    private void OnIndexBarDetail(AnnotatedScrollBar sender, AnnotatedScrollBarDetailLabelRequestedEventArgs args)
    {
        if (GalleryLayout is null)
        {
            return;
        }

        var label = GalleryGrouping.LabelAt(Gallery.Entries, GalleryLayout.LastRects, args.ScrollOffset);
        if (!string.IsNullOrEmpty(label))
        {
            args.Content = label;
        }
    }

    private async void OnEmptyAddFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Window));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            try
            {
                await ViewModel.AddLibraryAsync(folder.Path);
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = ex.Message;
            }
        }
    }

    private async void OnUseDemoFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Window));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "Assets", "demo");
            if (Directory.Exists(source))
            {
                foreach (var file in Directory.EnumerateFiles(source))
                {
                    File.Copy(file, Path.Combine(folder.Path, Path.GetFileName(file)), overwrite: false);
                }
            }

            await ViewModel.AddLibraryAsync(folder.Path);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private async void OnFindDuplicates(object sender, RoutedEventArgs e)
    {
        try
        {
        await Gallery.FindDuplicatesCommand.ExecuteAsync(null);
        var groups = Gallery.DuplicateGroups;
        if (groups.Count == 0)
        {
            var empty = new ContentDialog
            {
                Title = "Duplicates",
                Content = "No exact duplicates yet. Files are hashed in the background (SHA-256). Try again after hashing finishes.",
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            };
            await empty.ShowAsync();
            return;
        }

        var root = new StackPanel { Spacing = 16 };
        var shown = groups.Take(20).ToList();
        foreach (var group in shown)
        {
            var box = new StackPanel { Spacing = 8 };
            box.Children.Add(new TextBlock
            {
                Text = $"{group.Count} copies · {(group[0].ContentHash is { Length: >= 8 } hash ? hash[..8] : "?")}…",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
            });
            foreach (var item in group.Take(8))
            {
                box.Children.Add(CreateCompareRow(item, keepHandler: OnKeepDuplicate));
            }

            if (group.Count > 8)
            {
                box.Children.Add(new TextBlock
                {
                    Text = $"+{group.Count - 8} more in this group",
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
            }

            root.Children.Add(box);
        }

        if (groups.Count > shown.Count)
        {
            root.Children.Add(new TextBlock { Text = $"+{groups.Count - shown.Count} more groups" });
        }

        var dialog = new ContentDialog
        {
            Title = $"Duplicates ({groups.Count} groups)",
            Content = new ScrollViewer
            {
                Content = root,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Duplicates UI", ex);
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private async void OnKeepDuplicate(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaItem keep })
        {
            return;
        }

        var group = Gallery.DuplicateGroups.FirstOrDefault(g =>
            g.Any(i => i.Id == keep.Id && i.LibraryRoot.Equals(keep.LibraryRoot, StringComparison.OrdinalIgnoreCase)));
        if (group is null)
        {
            return;
        }

        var drop = group
            .Where(i => i.Id != keep.Id || !i.LibraryRoot.Equals(keep.LibraryRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await Gallery.RecycleItemsAsync(drop);
        await Gallery.FindDuplicatesCommand.ExecuteAsync(null);
    }

    private async void OnFindSimilar(object sender, RoutedEventArgs e)
    {
        if (Gallery.SelectedItem is null)
        {
            ViewModel.ErrorMessage = "Select an image first.";
            return;
        }

        try
        {
        await Gallery.FindSimilarCommand.ExecuteAsync(null);
        var hits = Gallery.SimilarHits;
        if (hits.Count == 0)
        {
            var empty = new ContentDialog
            {
                Title = "Similar",
                Content = ViewModel.HasSimilarModel
                    ? "No similar embeddings yet. Try again after a few images are encoded."
                    : "Download CLIP ViT-B/32 in Settings → Models, then try Find similar again.",
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            };
            await empty.ShowAsync();
            return;
        }

        var list = new StackPanel { Spacing = 8 };
        foreach (var (item, score) in hits.Take(24))
        {
            list.Children.Add(CreateCompareRow(item, caption: $"{score:0.00} similar"));
        }

        var dialog = new ContentDialog
        {
            Title = "Similar images — click a preview to open in Photos",
            Content = new ScrollViewer
            {
                Content = list,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Find similar UI", ex);
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private FrameworkElement CreateCompareRow(MediaItem item, string? caption = null, RoutedEventHandler? keepHandler = null)
    {
        var preview = new Button
        {
            Padding = new Thickness(0),
            Width = 96,
            Height = 96,
            Tag = item,
            Content = new Image
            {
                Width = 96,
                Height = 96,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                Source = ThumbImage(item)
            }
        };
        ToolTipService.SetToolTip(preview, "Open in Photos");
        preview.Click += OnOpenCompareInPhotos;

        var dim = item.Width is int w && item.Height is int h ? $"{w}×{h}" : "";
        var size = item.ByteSize >= 1_048_576 ? $"{item.ByteSize / 1_048_576d:0.0} MB"
            : item.ByteSize >= 1024 ? $"{item.ByteSize / 1024d:0} KB"
            : $"{item.ByteSize} B";
        var meta = string.Join(" · ", new[] { caption, dim, size, $"{item.TagCount} tags" }.Where(s => !string.IsNullOrEmpty(s)));

        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = item.FileName,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = meta,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        });
        if (keepHandler is not null)
        {
            var keep = new Button { Content = "Keep this, delete others", Tag = item };
            keep.Click += keepHandler;
            text.Children.Add(keep);
        }

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(preview);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private async void OnOpenCompareInPhotos(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MediaItem item })
        {
            await Gallery.OpenInPhotosCommand.ExecuteAsync(item);
        }
    }

    private static BitmapImage? ThumbImage(MediaItem item)
    {
        var path = item.ThumbFullPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            path = item.FullPath;
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage { DecodePixelWidth = 320 };
        image.UriSource = new Uri(path);
        return image;
    }

}
