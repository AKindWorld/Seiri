using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Seiri.Core;
using Seiri.Core.Models;
using Seiri.Services;
using Seiri.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace Seiri.Views;

public sealed partial class PreviewPane : UserControl
{
    public GalleryViewModel Gallery => App.Shell.Gallery;
    public ShellViewModel Shell => App.Shell;

    private bool _barSync;
    private bool _barDrag;
    private bool _videoSeek;
    private DispatcherTimer? _videoTimer;

    public PreviewPane()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var inner = Math.Max(0, ActualHeight - 32);
            if (inner > 1)
            {
                PreviewScroll.Height = inner;
            }

            SyncPreviewBar();
        };
        PreviewStack.SizeChanged += (_, _) =>
        {
            if (PreviewStack.ActualWidth > 0)
            {
                PreviewImage.Width = PreviewStack.ActualWidth;
                SizePreviewPlayer();
            }

            SyncPreviewBar();
        };
        Gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GalleryViewModel.SelectedItem)
                or nameof(GalleryViewModel.ShowSinglePreview))
            {
                PreviewScroll?.ChangeView(null, 0, null, disableAnimation: true);
                AppLog.Run(() => LoadPreviewMediaAsync(), "preview media");
            }
        };
        Loaded += (_, _) => AppLog.Run(() => LoadPreviewMediaAsync(), "preview media");
        Unloaded += (_, _) => StopPreviewPlayer();
    }

    public static Visibility ImageVis(bool single, MediaItem? item) =>
        single && item?.Kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VideoVis(bool single, MediaItem? item) =>
        single && item?.Kind == MediaKind.Video ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility BoolVis(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility EmptyVis(bool single, bool multi) =>
        single || multi ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility CountVis(int extra) =>
        extra > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static string ExtraLabel(int extra) => extra > 0 ? $"+{extra}" : string.Empty;

    public static string NameOf(MediaItem? item) => item?.FileName ?? string.Empty;

    public static string PathOf(MediaItem? item) => item?.FullPath ?? string.Empty;

    public static Visibility ErrorVis(MediaItem? item) =>
        string.IsNullOrEmpty(item?.TagError) ? Visibility.Collapsed : Visibility.Visible;

    public static string ErrorOf(MediaItem? item) =>
        string.IsNullOrEmpty(item?.TagError) ? string.Empty : $"Tagging failed: {item.TagError}";

    public static string DimOf(MediaItem? item) =>
        item?.Width is int w && item.Height is int h ? $"{w}×{h}" : string.Empty;

    public static Visibility HasDim(MediaItem? item) =>
        item?.Width is > 0 && item.Height is > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static string FormatOf(MediaItem? item) =>
        item is null ? string.Empty : item.Ext.TrimStart('.').ToUpperInvariant();

    public static string SizeOf(MediaItem? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        var bytes = item.ByteSize;
        return bytes >= 1_048_576 ? $"{bytes / 1_048_576d:0.0} MB"
            : bytes >= 1024 ? $"{bytes / 1024d:0} KB"
            : $"{bytes} B";
    }

    public static string TagsOf(MediaItem? item) =>
        item is null ? string.Empty : $"{item.TagCount} tag{(item.TagCount == 1 ? "" : "s")}";

    public static BitmapImage? FullImage(MediaItem? item, bool tagging)
    {
        if (item?.Kind != MediaKind.Image)
        {
            return null;
        }

        var yield = tagging || GpuWork.YieldGpuToOnnx;
        var path = item.FullPath;
        if (yield && !string.IsNullOrEmpty(item.ThumbFullPath) && File.Exists(item.ThumbFullPath))
        {
            path = item.ThumbFullPath;
        }
        else if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            path = item.ThumbFullPath;
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage
        {
            DecodePixelWidth = yield ? 320 : 900,
            CreateOptions = BitmapCreateOptions.IgnoreImageCache
        };
        image.UriSource = new Uri(path);
        return image;
    }

    public static BitmapImage? ThumbFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.DecodePixelWidth = 160;
        image.UriSource = new Uri(path);
        return image;
    }

    private async void OnSearchTag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagRecord record })
        {
            await Gallery.SearchTagCommand.ExecuteAsync(QueryParser.FormatTagToken(record.Name, category: record.Category));
        }
        else if (sender is FrameworkElement { Tag: string tag })
        {
            await Gallery.SearchTagCommand.ExecuteAsync(tag);
        }
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) => SyncPreviewBar();

    private void OnPreviewViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => SyncPreviewBar();

    private void SyncPreviewBar()
    {
        if (PreviewScroll is null || PreviewBarHost is null || PreviewThumb is null || _barSync)
        {
            return;
        }

        _barSync = true;
        try
        {
            var viewport = PreviewScroll.ViewportHeight;
            var extent = PreviewScroll.ExtentHeight;
            var scrollable = Math.Max(0, extent - viewport);
            PreviewBarHost.Visibility = scrollable > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (scrollable <= 1)
            {
                return;
            }

            var host = PreviewBarHost.ActualHeight;
            if (host <= 1)
            {
                host = Math.Max(1, PreviewScroll.ActualHeight);
            }

            var thumb = Math.Clamp(host * viewport / Math.Max(extent, 1), 24, host);
            var top = (host - thumb) * (PreviewScroll.VerticalOffset / scrollable);
            PreviewThumb.Height = thumb;
            PreviewThumb.Margin = new Thickness(2, Math.Clamp(top, 0, host - thumb), 2, 0);
        }
        finally
        {
            _barSync = false;
        }
    }

    private void OnPreviewBarPressed(object sender, PointerRoutedEventArgs e)
    {
        _barDrag = true;
        PreviewBarHost.CapturePointer(e.Pointer);
        SeekPreviewBar(e);
    }

    private void OnPreviewBarMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_barDrag)
        {
            SeekPreviewBar(e);
        }
    }

    private void OnPreviewBarReleased(object sender, PointerRoutedEventArgs e)
    {
        _barDrag = false;
        PreviewBarHost.ReleasePointerCapture(e.Pointer);
    }

    private void SeekPreviewBar(PointerRoutedEventArgs e)
    {
        var viewport = PreviewScroll.ViewportHeight;
        var extent = PreviewScroll.ExtentHeight;
        var scrollable = Math.Max(0, extent - viewport);
        var host = PreviewBarHost.ActualHeight;
        if (scrollable <= 0 || host <= 0)
        {
            return;
        }

        var y = e.GetCurrentPoint(PreviewBarHost).Position.Y;
        var thumb = Math.Clamp(host * viewport / Math.Max(extent, 1), 24, host);
        var t = Math.Clamp((y - thumb / 2) / Math.Max(1, host - thumb), 0, 1);
        PreviewScroll.ChangeView(null, t * scrollable, null, disableAnimation: true);
    }

    private async void OnRevealPath(object sender, RoutedEventArgs e)
    {
        var item = Gallery.SelectedItem;
        if (item is null || !File.Exists(item.FullPath))
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
            var folder = await file.GetParentAsync();
            if (folder is not null)
            {
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(file);
                await Launcher.LaunchFolderAsync(folder, options);
                return;
            }
        }
        catch
        {
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{item.FullPath}\"",
            UseShellExecute = true
        });
    }

    private async void OnPreviewOpen(object sender, RoutedEventArgs e)
    {
        await Gallery.OpenInPhotosCommand.ExecuteAsync(Gallery.SelectedItem);
    }

    private async void OnRemoveTag(object sender, RoutedEventArgs e)
    {
        string? name = sender is FrameworkElement { Tag: TagRecord record }
            ? record.Name
            : sender is FrameworkElement { Tag: string tag } ? tag : null;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (Gallery.IsMultiPreview)
        {
            await Gallery.BulkRemoveTagCommand.ExecuteAsync(name);
        }
        else
        {
            await Gallery.RemoveTagCommand.ExecuteAsync(name);
        }
    }

    private async void OnClearTags(object sender, RoutedEventArgs e)
    {
        var item = Gallery.SelectedItem;
        if (item is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Clear tags",
            Content = $"Remove all tags from {item.FileName}? The sidecar file will be emptied.",
            PrimaryButtonText = "Clear tags",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Gallery.ClearTagsCommand.ExecuteAsync(null);
        }
    }

    private async void OnRename(object sender, RoutedEventArgs e)
    {
        var item = Gallery.SelectedItem;
        if (item is null)
        {
            return;
        }

        var box = new TextBox
        {
            Text = Path.GetFileNameWithoutExtension(item.FileName),
            Header = "New name"
        };
        var dialog = new ContentDialog
        {
            Title = "Rename",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Gallery.RenameSelectedCommand.ExecuteAsync(box.Text);
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var items = Gallery.SelectedMedia();
        if (items.Count == 0)
        {
            return;
        }

        var permanent = new CheckBox { Content = "Permanently delete" };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = items.Count == 1
                ? $"Send {items[0].FileName} to the Recycle Bin?"
                : $"Send {items.Count} items to the Recycle Bin?",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(permanent);
        var dialog = new ContentDialog
        {
            Title = "Delete",
            Content = panel,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Gallery.DeleteSelectedCommand.ExecuteAsync(permanent.IsChecked == true);
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

    private async void OnAddTagTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        sender.ItemsSource = await Gallery.SuggestAddTagsAsync(sender.Text);
    }

    private async void OnAddTagChosen(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is string chosen)
        {
            Gallery.NewTagText = chosen;
        }
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            Gallery.NewTagText = args.QueryText;
        }

        sender.IsSuggestionListOpen = false;
        await Gallery.AddTagCommand.ExecuteAsync(null);
        sender.Text = string.Empty;
    }

    private async void OnBulkTagChosen(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        Gallery.BulkTagText = args.ChosenSuggestion as string ?? args.QueryText;
        sender.IsSuggestionListOpen = false;
        await Gallery.BulkAddTagCommand.ExecuteAsync(null);
        sender.Text = string.Empty;
    }

    private async Task LoadPreviewMediaAsync()
    {
        StopPreviewPlayer();
        var item = Gallery.SelectedItem;
        if (!Gallery.ShowSinglePreview || item is null || item.Kind != MediaKind.Video || !File.Exists(item.FullPath))
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
            var player = new MediaPlayer { AutoPlay = false, IsLoopingEnabled = false };
            PreviewPlayer.SetMediaPlayer(player);
            player.Source = MediaSource.CreateFromStorageFile(file);
            player.PlaybackSession.PlaybackStateChanged += OnVideoStateChanged;
            SizePreviewPlayer();
            StartVideoTimer();
            UpdateVideoChrome();
        }
        catch
        {
            PreviewPlayer.SetMediaPlayer(null);
        }
    }

    private void StopPreviewPlayer()
    {
        _videoTimer?.Stop();
        if (PreviewPlayer is null)
        {
            return;
        }

        var player = PreviewPlayer.MediaPlayer;
        if (player is not null)
        {
            player.PlaybackSession.PlaybackStateChanged -= OnVideoStateChanged;
            player.Pause();
            player.Source = null;
            player.Dispose();
        }

        PreviewPlayer.SetMediaPlayer(null);
        if (VideoSlider is not null)
        {
            VideoSlider.Value = 0;
        }

        if (VideoTime is not null)
        {
            VideoTime.Text = "0:00";
        }

        if (VideoPlayIcon is not null)
        {
            VideoPlayIcon.Glyph = "\uE768";
        }
    }

    private void SizePreviewPlayer()
    {
        if (PreviewPlayer is null || PreviewStack is null)
        {
            return;
        }

        var width = PreviewStack.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        PreviewPlayer.Width = width;
        var item = Gallery.SelectedItem;
        if (item?.Width is > 0 && item.Height is > 0)
        {
            PreviewPlayer.Height = width * item.Height.Value / item.Width.Value;
        }
    }

    private void StartVideoTimer()
    {
        _videoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _videoTimer.Tick -= OnVideoTick;
        _videoTimer.Tick += OnVideoTick;
        _videoTimer.Start();
    }

    private void OnVideoTick(object? sender, object e) => UpdateVideoChrome();

    private void OnVideoStateChanged(MediaPlaybackSession sender, object args)
    {
        DispatcherQueue.TryEnqueue(UpdateVideoChrome);
    }

    private void UpdateVideoChrome()
    {
        if (PreviewPlayer?.MediaPlayer is not { } player || VideoSlider is null || VideoTime is null || VideoPlayIcon is null)
        {
            return;
        }

        var session = player.PlaybackSession;
        var duration = session.NaturalDuration;
        var position = session.Position;
        VideoPlayIcon.Glyph = session.PlaybackState == MediaPlaybackState.Playing ? "\uE769" : "\uE768";
        VideoTime.Text = FormatTime(position);
        if (_videoSeek || duration <= TimeSpan.Zero)
        {
            return;
        }

        _videoSeek = true;
        VideoSlider.Value = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
        _videoSeek = false;
    }

    private void OnVideoPlayPause(object sender, RoutedEventArgs e)
    {
        var player = PreviewPlayer?.MediaPlayer;
        if (player is null)
        {
            return;
        }

        if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }
    }

    private void OnVideoSeek(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_videoSeek || PreviewPlayer?.MediaPlayer is not { } player)
        {
            return;
        }

        var duration = player.PlaybackSession.NaturalDuration;
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        _videoSeek = true;
        player.PlaybackSession.Position = TimeSpan.FromSeconds(e.NewValue * duration.TotalSeconds);
        _videoSeek = false;
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return value.ToString(@"h\:mm\:ss");
        }

        return value.ToString(@"m\:ss");
    }
}
