using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Seiri.Core.Models;
using Seiri.ViewModels;
using Windows.System;

namespace Seiri.Views;

public sealed partial class PreviewPane : UserControl
{
    public GalleryViewModel Gallery => App.Shell.Gallery;
    public ShellViewModel Shell => App.Shell;

    public PreviewPane()
    {
        InitializeComponent();
        PreviewStack.SizeChanged += (_, _) =>
        {
            if (PreviewStack.ActualWidth > 0)
            {
                PreviewImage.Width = PreviewStack.ActualWidth;
            }
        };
    }

    public static Visibility HasItem(MediaItem? item) =>
        item is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NoItem(MediaItem? item) =>
        item is null ? Visibility.Visible : Visibility.Collapsed;

    public static string NameOf(MediaItem? item) => item?.FileName ?? string.Empty;

    public static string PathOf(MediaItem? item) => item?.FullPath ?? string.Empty;

    public static Visibility ErrorVis(MediaItem? item) =>
        string.IsNullOrEmpty(item?.TagError) ? Visibility.Collapsed : Visibility.Visible;

    public static string ErrorOf(MediaItem? item) =>
        string.IsNullOrEmpty(item?.TagError) ? string.Empty : $"Tagging failed: {item.TagError}";

    public static string MetaOf(MediaItem? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        var dim = item.Width is not null && item.Height is not null ? $"{item.Width}×{item.Height} · " : string.Empty;
        var fav = item.IsFavorite ? " · Favorite" : string.Empty;
        return $"{dim}{item.Ext.ToUpperInvariant()} · {item.ByteSize / 1024.0:0} KB · {item.TagCount} tags{fav}";
    }

    public static BitmapImage? FullImage(MediaItem? item)
    {
        var path = item?.FullPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || item?.Kind != MediaKind.Image)
        {
            return null;
        }

        var image = new BitmapImage();
        image.DecodePixelWidth = 900;
        image.UriSource = new Uri(path);
        return image;
    }

    private async void OnSearchTag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            await Gallery.SearchTagCommand.ExecuteAsync(tag);
        }
    }

    private async void OnRevealPath(object sender, RoutedEventArgs e)
    {
        var item = Gallery.SelectedItem;
        if (item is null)
        {
            return;
        }

        await Windows.System.Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(item.FullPath)!);
    }

    private async void OnRemoveTag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            await Gallery.RemoveTagCommand.ExecuteAsync(tag);
        }
    }

    private async void OnTagKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await Gallery.AddTagCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }
}
