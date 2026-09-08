using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Seiri.Core.Models;
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
        Loaded += async (_, _) =>
        {
            RecalcTileSize();
            SyncRiverLayout();
            await Task.Delay(80);
            RecalcTileSize();
            SyncRiverLayout();
        };
        LayoutUpdated += (_, _) => ApplyGridLayouts(TileHost, TileSize);
        Gallery.PropertyChanged += OnGalleryChanged;
    }

    private void OnGalleryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.ShowRiver)
            or nameof(GalleryViewModel.RiverRowHeight)
            or nameof(GalleryViewModel.Density)
            or nameof(GalleryViewModel.Layout))
        {
            SyncRiverLayout();
        }
    }

    private void SyncRiverLayout()
    {
        if (RiverLayout is null)
        {
            return;
        }

        RiverLayout.Items = Gallery.Items;
        RiverLayout.DesiredRowHeight = Gallery.RiverRowHeight;
        RiverLayout.Refresh();
    }

    private void OnTileHostSizeChanged(object sender, SizeChangedEventArgs e) => RecalcTileSize();

    private void OnRepeaterPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args) =>
        ApplyGridLayouts(args.Element, TileSize);

    private void RecalcTileSize()
    {
        var width = TileHost.ActualWidth;
        if (width <= 0)
        {
            width = Math.Max(0, ActualWidth - 48);
        }

        Gallery.UpdateTileSize(width);
        TileSize = Gallery.TileSize;
        ApplyGridLayouts(TileHost, TileSize);
    }

    private static void ApplyGridLayouts(DependencyObject? root, double tile)
    {
        if (root is null || tile <= 0)
        {
            return;
        }

        if (root is ItemsRepeater { Layout: UniformGridLayout layout }
            && (Math.Abs(layout.MinItemWidth - tile) > 0.5 || layout.ItemsStretch != UniformGridLayoutItemsStretch.None))
        {
            layout.ItemsStretch = UniformGridLayoutItemsStretch.None;
            layout.MinItemWidth = tile;
            layout.MinItemHeight = tile;
        }

        if (root is ScrollViewer { Content: DependencyObject content })
        {
            ApplyGridLayouts(content, tile);
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            ApplyGridLayouts(VisualTreeHelper.GetChild(root, i), tile);
        }
    }

    public static Visibility EmptyVis(bool isEmpty, bool hasLibraries) =>
        isEmpty && !hasLibraries ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NoMatchVis(bool isEmpty, bool hasLibraries) =>
        isEmpty && hasLibraries ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility BoolVis(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility MasonryVis(GalleryLayoutMode layout, bool empty) =>
        !empty && layout == GalleryLayoutMode.Masonry ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility RiverVis(GalleryLayoutMode layout, bool empty) =>
        !empty && layout == GalleryLayoutMode.River ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility GridGroupsVis(GalleryLayoutMode layout, bool empty, SortKey sort) =>
        !empty && layout == GalleryLayoutMode.Grid && sort is SortKey.DateTaken or SortKey.DateAdded or SortKey.DateModified
            ? Visibility.Visible
            : Visibility.Collapsed;

    public static Visibility GridFlatVis(GalleryLayoutMode layout, bool empty, SortKey sort) =>
        !empty && layout == GalleryLayoutMode.Grid && sort is not (SortKey.DateTaken or SortKey.DateAdded or SortKey.DateModified)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public static Visibility FlatVis(bool isEmpty, bool showGroups) =>
        !isEmpty && !showGroups ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility BadgeVis(int count) =>
        count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VideoVis(MediaKind kind) =>
        kind == MediaKind.Video ? Visibility.Visible : Visibility.Collapsed;

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

    public static BitmapImage? Thumb(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.DecodePixelWidth = 512;
        image.UriSource = new Uri(path);
        return image;
    }

    private async void OnTilePressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MediaItem item })
        {
            await Gallery.SelectCommand.ExecuteAsync(item);
        }
    }

    private async void OnTileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MediaItem item })
        {
            await Gallery.OpenInPhotosCommand.ExecuteAsync(item);
        }
    }

    private async void OnTaggedFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag)
        {
            await Gallery.SetTaggedCommand.ExecuteAsync(tag);
        }
    }

    private async void OnApplyFilters(object sender, RoutedEventArgs e)
    {
        var include = FilterIncludeBox.Text;
        var exclude = FilterExcludeBox.Text;
        var parts = new List<string>();
        foreach (var tag in SplitTags(include))
        {
            parts.Add(tag.Contains(' ') ? $"tag:\"{tag}\"" : $"tag:{tag.Replace(' ', '_')}");
        }

        foreach (var tag in SplitTags(exclude))
        {
            parts.Add(tag.Contains(' ') ? $"-tag:\"{tag}\"" : $"-tag:{tag.Replace(' ', '_')}");
        }

        if (FilterKindBox.SelectedItem is ComboBoxItem { Tag: string kind } && kind != "All")
        {
            await Gallery.SetKindCommand.ExecuteAsync(kind);
        }

        FilterButton.Flyout?.Hide();
        await Gallery.ApplySearchAsync(string.Join(' ', parts));
    }

    private void OnCancelFilters(object sender, RoutedEventArgs e) => FilterButton.Flyout?.Hide();

    private async void OnClearFilters(object sender, RoutedEventArgs e)
    {
        FilterIncludeBox.Text = string.Empty;
        FilterExcludeBox.Text = string.Empty;
        FilterKindBox.SelectedIndex = 0;
        FilterButton.Flyout?.Hide();
        await Gallery.ClearSearchCommand.ExecuteAsync(null);
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

    private static IEnumerable<string> SplitTags(string? text) =>
        (text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace('_', ' ').ToLowerInvariant());
}
