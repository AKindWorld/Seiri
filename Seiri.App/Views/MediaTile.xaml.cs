using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Seiri.Core.Models;
using Windows.Foundation;
using Windows.UI;

namespace Seiri.Views;

public sealed partial class MediaTile : UserControl
{
    private MediaItem? _item;

    public MediaTile()
    {
        InitializeComponent();
        PointerPressed += OnPressed;
        DoubleTapped += OnDoubleTapped;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public MediaItem? Item
    {
        get => _item;
        set
        {
            _item = value;
            ApplyItem();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Shell.Gallery.PropertyChanged += OnGalleryChanged;
        ApplyItem();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        App.Shell.Gallery.PropertyChanged -= OnGalleryChanged;

    private void OnGalleryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.GalleryViewModel.Layout)
            or nameof(ViewModels.GalleryViewModel.Density)
            or nameof(ViewModels.GalleryViewModel.TileSize)
            or nameof(ViewModels.GalleryViewModel.MasonryColumnWidth)
            or nameof(ViewModels.GalleryViewModel.RiverRowHeight))
        {
            InvalidateMeasure();
        }

        if (e.PropertyName is nameof(ViewModels.GalleryViewModel.IsSelectMode)
            or nameof(ViewModels.GalleryViewModel.SelectionVersion))
        {
            ApplySelection();
        }
    }

    private void ApplyItem()
    {
        var item = Item;
        AutomationProperties.SetName(this, item?.FileName ?? string.Empty);
        Tag = item;
        TagBadge.Value = item?.TagCount ?? 0;
        TagBadge.Visibility = item is { TagCount: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        VideoMark.Visibility = item?.Kind == MediaKind.Video ? Visibility.Visible : Visibility.Collapsed;
        ThumbImage.Source = GalleryPage.Thumb(item?.ThumbFullPath ?? item?.FullPath);
        ApplySelection();
        InvalidateMeasure();
    }

    private void ApplySelection()
    {
        var gallery = App.Shell.Gallery;
        var select = gallery.IsSelectMode;
        SelectMark.Visibility = select ? Visibility.Visible : Visibility.Collapsed;
        var checkedItem = select && gallery.IsChecked(Item);
        SelectChrome.Visibility = checkedItem ? Visibility.Visible : Visibility.Collapsed;
        SelectCheck.Visibility = checkedItem ? Visibility.Visible : Visibility.Collapsed;
        SelectCircle.Fill = checkedItem
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = DesiredTileSize(availableSize);
        if (Content is UIElement child)
        {
            child.Measure(size);
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Content is UIElement child)
        {
            child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }

    private Size DesiredTileSize(Size available)
    {
        var gallery = App.Shell.Gallery;
        var aspect = Aspect();
        return gallery.Layout switch
        {
            GalleryLayoutMode.Masonry => MasonrySize(available, gallery.MasonryColumnWidth, aspect),
            GalleryLayoutMode.River => RiverSize(available, gallery.RiverRowHeight, aspect),
            _ => GridSize(available, gallery.TileSize)
        };
    }

    private static Size GridSize(Size available, double tile)
    {
        var w = Finite(available.Width, tile);
        var h = Finite(available.Height, tile);
        var side = Math.Min(w, h);
        if (side <= 0)
        {
            side = tile;
        }

        return new Size(side, side);
    }

    private static Size MasonrySize(Size available, double column, double aspect)
    {
        var width = Finite(available.Width, column);
        if (width <= 0)
        {
            width = column;
        }

        return new Size(width, Math.Max(40, width * aspect));
    }

    private static Size RiverSize(Size available, double rowHeight, double aspect)
    {
        var height = Finite(available.Height, rowHeight);
        if (height <= 0)
        {
            height = rowHeight;
        }

        var width = aspect <= 0 ? height : height / aspect;
        return new Size(Math.Max(40, width), height);
    }

    private double Aspect()
    {
        var w = Item?.Width ?? 0;
        var h = Item?.Height ?? 0;
        if (w <= 0 || h <= 0)
        {
            return 1;
        }

        return h / (double)w;
    }

    private static double Finite(double value, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;

    private async void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Item is { } item)
        {
            await App.Shell.Gallery.SelectCommand.ExecuteAsync(item);
        }
    }

    private async void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Item is { } item)
        {
            await App.Shell.Gallery.OpenInPhotosCommand.ExecuteAsync(item);
        }
    }
}
