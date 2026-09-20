using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Seiri.Core;
using Seiri.Core.Models;
using Seiri.Services;
using Windows.Foundation;
using Windows.UI;

namespace Seiri.Views;

public sealed partial class MediaTile : UserControl
{
    private MediaItem? _item;
    private int _loadSerial;

    public MediaTile()
    {
        InitializeComponent();
        PointerPressed += OnPressed;
        DoubleTapped += OnDoubleTapped;
        PointerEntered += OnEntered;
        PointerExited += OnExited;
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

    public void Unload()
    {
        _loadSerial++;
        ThumbImage.Source = null;
    }

    private const long HoverPlayMaxBytes = 20L * 1024 * 1024;

    private bool CanHoverPlay(MediaItem? item)
    {
        if (item is null || item.Kind != MediaKind.Image || item.ByteSize > HoverPlayMaxBytes)
        {
            return false;
        }

        var ext = item.Ext;
        return ext.Equals("gif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals("webp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private async void OnEntered(object sender, PointerRoutedEventArgs e)
    {
        var item = Item;
        if (GpuWork.YieldGpuToOnnx || !CanHoverPlay(item) || item is null || !File.Exists(item.FullPath))
        {
            return;
        }

        var serial = ++_loadSerial;
        try
        {
            var image = new BitmapImage
            {
                AutoPlay = true,
                UriSource = new Uri(item.FullPath)
            };
            if (serial != _loadSerial || !ReferenceEquals(Item, item))
            {
                return;
            }

            ThumbImage.Source = image;
        }
        catch
        {
            if (serial == _loadSerial)
            {
                await LoadThumbAsync();
            }
        }
    }

    private void OnExited(object sender, PointerRoutedEventArgs e)
    {
        if (!CanHoverPlay(Item))
        {
            return;
        }

        _ = LoadThumbAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Shell.Gallery.PropertyChanged += OnGalleryChanged;
        ApplyItem();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.Shell.Gallery.PropertyChanged -= OnGalleryChanged;
        Unload();
    }

    private void OnGalleryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.GalleryViewModel.IsSelectMode)
            or nameof(ViewModels.GalleryViewModel.SelectionVersion))
        {
            ApplySelection();
        }

        if (e.PropertyName is nameof(ViewModels.GalleryViewModel.ImageEpoch))
        {
            ApplyItem();
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
        ApplySelection();
        _ = LoadThumbAsync();
    }

    private async Task LoadThumbAsync()
    {
        var serial = ++_loadSerial;
        var item = Item;
        if (item is null)
        {
            ThumbImage.Source = null;
            return;
        }

        try
        {
            await App.Shell.Thumbs.EnsureAsync(item);
            if (serial != _loadSerial || !ReferenceEquals(Item, item))
            {
                return;
            }

            var path = item.ThumbFullPath;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var source = await TileImageFactory.CreateAsync(
                path,
                DecodeWidth(),
                App.Shell.UseHardwareAcceleration && !GpuWork.YieldGpuToOnnx);
            if (serial != _loadSerial || !ReferenceEquals(Item, item))
            {
                return;
            }

            ThumbImage.Source = source;
        }
        catch
        {
            // Placeholder stays visible.
        }
    }

    private int DecodeWidth()
    {
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var css = ActualWidth > 1 ? ActualWidth : App.Shell.Gallery.TileSize;
        return Math.Clamp((int)Math.Ceiling(css * scale), 64, 512);
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
        var w = Finite(available.Width, App.Shell.Gallery.TileSize);
        var h = Finite(available.Height, w);
        return new Size(w, h);
    }

    private static double Finite(double value, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;

    private async void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Item is { } item)
        {
            try
            {
                await App.Shell.Gallery.SelectCommand.ExecuteAsync(item);
            }
            catch (Exception ex)
            {
                AppLog.Error("tile select", ex);
            }
        }
    }

    private async void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Item is { } item)
        {
            try
            {
                await App.Shell.Gallery.OpenInPhotosCommand.ExecuteAsync(item);
            }
            catch (Exception ex)
            {
                AppLog.Error("tile open", ex);
            }
        }
    }
}
