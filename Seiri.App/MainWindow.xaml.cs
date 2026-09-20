using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Seiri.Core;
using Seiri.Core.Models;
using Seiri.ViewModels;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Seiri;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public ShellViewModel ViewModel { get; }

    public MainWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        RootGrid.DataContext = ViewModel;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        AddAccelerators();

        RestorePlacement();
        AppWindow.Changed += OnAppWindowChanged;

        RootGrid.SizeChanged += (_, _) => PositionSuggestPanel();
        GallerySearch.SizeChanged += (_, _) => PositionSuggestPanel();
        AppTitleBar.SizeChanged += (_, _) => PositionSuggestPanel();
        SearchSuggestLayer.SizeChanged += (_, _) => PositionSuggestPanel();
        ViewModel.Gallery.PropertyChanged += OnGalleryPropertyChanged;

        Closed += OnClosed;
    }

    private void OnGalleryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.SuggestionsOpen))
        {
            DispatcherQueue.TryEnqueue(PositionSuggestPanel);
        }
    }

    private void PositionSuggestPanel()
    {
        if (SearchSuggestLayer.Visibility != Visibility.Visible || GallerySearch.ActualWidth <= 0)
        {
            return;
        }

        Windows.Foundation.Point origin;
        try
        {
            origin = GallerySearch.TransformToVisual(SearchSuggestLayer)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
        }
        catch (Exception)
        {
            return;
        }

        var width = Math.Max(280, GallerySearch.ActualWidth);
        var maxX = Math.Max(8, SearchSuggestLayer.ActualWidth - width - 8);
        var x = Math.Clamp(origin.X, 8, maxX);

        SearchSuggestPanel.Width = width;
        SearchSuggestPanel.MinWidth = width;
        SearchSuggestPanel.MaxWidth = width;
        SearchSuggestPanel.Margin = new Thickness(x, 4, 0, 0);
    }

    public static Visibility BoolToVis(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NotBoolToVis(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility PreviewVis(bool previewOpen, bool settingsOpen, bool tagsOpen) =>
        previewOpen && !settingsOpen && !tagsOpen ? Visibility.Visible : Visibility.Collapsed;

    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    public static Brush NavPill(RailSection current, int item)
    {
        var match = item switch
        {
            0 => RailSection.All,
            1 => RailSection.Favorites,
            2 => RailSection.Tagging,
            3 => RailSection.Tags,
            _ => (RailSection)(-1)
        };

        if (current != match)
        {
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        return Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var brush) && brush is Brush selected
            ? selected
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public static HorizontalAlignment RailAlign(bool expanded) =>
        expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;

    private void OnRailToggleClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Gallery.CloseSuggestions();
        ViewModel.ToggleRailCommand.Execute(null);
    }

    private void OnBackRequested(TitleBar sender, object args)
    {
        ViewModel.Gallery.CloseSuggestions();
        ViewModel.CloseSettingsCommand.Execute(null);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Gallery.CloseSuggestions();
        ViewModel.OpenSettingsCommand.Execute(null);
    }

    private void OnSearchDismissPointer(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        ViewModel.Gallery.CloseSuggestions();
    }

    private void OnSuggestPanelPointer(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        GallerySearch.CancelPendingDismiss();
    }

    private async void OnSearchSuggestionClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SearchSuggestion suggestion)
        {
            return;
        }

        GallerySearch.CancelPendingDismiss();
        try
        {
            await ViewModel.Gallery.ApplySuggestionAsync(suggestion);
        }
        catch (Exception ex)
        {
            AppLog.Error("search suggestion", ex);
        }
        if (suggestion.Kind == "prefix")
        {
            GallerySearch.FocusQuery();
        }
    }

    private void OnNavAll(object sender, RoutedEventArgs e) => ViewModel.Navigate(RailSection.All, null);

    private void OnNavFavorites(object sender, RoutedEventArgs e) => ViewModel.Navigate(RailSection.Favorites, null);

    private void OnNavTagging(object sender, RoutedEventArgs e) => ViewModel.Navigate(RailSection.Tagging, null);

    private void OnNavTags(object sender, RoutedEventArgs e) => ViewModel.Navigate(RailSection.Tags, null);

    private void OnLibraryButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            ViewModel.Navigate(RailSection.Directory, path);
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        try
        {
            await ViewModel.AddLibraryAsync(folder.Path);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private void AddAccelerators()
    {
        RootGrid.PreviewKeyDown += OnRootPreviewKeyDown;
    }

    private static bool KeyIsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private bool FocusedInTextInput()
    {
        if (FocusManager.GetFocusedElement(RootGrid.XamlRoot) is not DependencyObject focused)
        {
            return false;
        }

        for (DependencyObject? node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or AutoSuggestBox)
            {
                return true;
            }
        }

        return false;
    }

    private async void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = KeyIsDown(VirtualKey.Control);
        var typing = FocusedInTextInput();

        if (ctrl && e.Key == VirtualKey.F)
        {
            GallerySearch.FocusQuery();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.F5)
        {
            AppLog.Run(() => ViewModel.RescanAsync(), "F5 rescan");
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == VirtualKey.I)
        {
            ViewModel.TogglePreviewCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == (VirtualKey)0xBC)
        {
            ViewModel.OpenSettingsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == VirtualKey.A && !typing)
        {
            ViewModel.Gallery.SelectAllCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == VirtualKey.D && !typing)
        {
            ViewModel.Gallery.ToggleFavoriteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        if (ViewModel.Gallery.SuggestionsOpen)
        {
            ViewModel.Gallery.CloseSuggestions();
            e.Handled = true;
            return;
        }

        if (ViewModel.Gallery.IsSelectMode)
        {
            ViewModel.Gallery.SelectNoneCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(ViewModel.Gallery.SearchText))
        {
            await ViewModel.Gallery.ClearSearchCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (ViewModel.IsSettingsOpen)
        {
            ViewModel.CloseSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void RestorePlacement()
    {
        var settings = ViewModel.Settings;
        if (settings.WindowFullScreen)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = Math.Max(1.0, GetDpiForWindow(hwnd) / 96.0);
        var width = Math.Max(640, (int)(settings.WindowWidth * scale));
        var height = Math.Max(480, (int)(settings.WindowHeight * scale));
        var hasPos = settings.WindowX != int.MinValue && settings.WindowY != int.MinValue;
        var point = hasPos
            ? new PointInt32(settings.WindowX, settings.WindowY)
            : AppWindow.Position;
        var display = DisplayArea.GetFromPoint(point, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        if (hasPos)
        {
            var x = Math.Clamp(settings.WindowX, work.X, work.X + Math.Max(0, work.Width - 80));
            var y = Math.Clamp(settings.WindowY, work.Y, work.Y + Math.Max(0, work.Height - 80));
            AppWindow.Move(new PointInt32(x, y));
        }

        AppWindow.Resize(new SizeInt32(width, height));
        if (settings.WindowMaximized && AppWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.Maximize();
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange && !args.DidPresenterChange)
        {
            return;
        }

        CapturePlacement();
    }

    private void CapturePlacement()
    {
        var settings = ViewModel.Settings;
        settings.WindowFullScreen = AppWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        var overlapped = AppWindow.Presenter as OverlappedPresenter;
        settings.WindowMaximized = overlapped?.State == OverlappedPresenterState.Maximized;
        if (settings.WindowFullScreen || settings.WindowMaximized)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = Math.Max(1.0, GetDpiForWindow(hwnd) / 96.0);
        settings.WindowX = AppWindow.Position.X;
        settings.WindowY = AppWindow.Position.Y;
        settings.WindowWidth = Math.Max(640, (int)Math.Round(AppWindow.Size.Width / scale));
        settings.WindowHeight = Math.Max(480, (int)Math.Round(AppWindow.Size.Height / scale));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        CapturePlacement();
        ViewModel.PersistSettings();
    }
}
