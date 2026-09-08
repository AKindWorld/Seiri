using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Seiri.Core.Models;
using Seiri.ViewModels;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
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

        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var width = (int)(ViewModel.Settings.WindowWidth * scale);
        var height = (int)(ViewModel.Settings.WindowHeight * scale);
        AppWindow.Resize(new SizeInt32(width, height));

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

    public static Visibility PreviewVis(bool previewOpen, bool settingsOpen) =>
        previewOpen && !settingsOpen ? Visibility.Visible : Visibility.Collapsed;

    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    public static Brush NavPill(RailSection current, int item)
    {
        var match = item switch
        {
            0 => RailSection.All,
            1 => RailSection.Favorites,
            2 => RailSection.Tagging,
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

    private void OnPaneToggleRequested(TitleBar sender, object args)
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
        await ViewModel.Gallery.ApplySuggestionAsync(suggestion);
        if (suggestion.Kind == "prefix")
        {
            GallerySearch.FocusQuery();
        }
    }

    private void OnNavAll(object sender, RoutedEventArgs e) => ViewModel.Navigate(RailSection.All, null);

    private void OnNavFavorites(object sender, RoutedEventArgs e) => ViewModel.Navigate(RailSection.Favorites, null);

    private void OnNavTagging(object sender, RoutedEventArgs e) => ViewModel.Navigate(RailSection.Tagging, null);

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
        AddAccel(VirtualKey.F, VirtualKeyModifiers.Control, () => GallerySearch.FocusQuery());
        AddAccel(VirtualKey.F5, VirtualKeyModifiers.None, () => _ = ViewModel.RescanAsync());
        AddAccel(VirtualKey.A, VirtualKeyModifiers.Control, () => ViewModel.Gallery.SelectAllCommand.Execute(null));
        AddAccel(VirtualKey.D, VirtualKeyModifiers.Control, () => ViewModel.Gallery.ToggleFavoriteCommand.Execute(null));
        AddAccel(VirtualKey.I, VirtualKeyModifiers.Control, () => ViewModel.TogglePreviewCommand.Execute(null));
        AddAccel((VirtualKey)0xBC, VirtualKeyModifiers.Control, () => ViewModel.OpenSettingsCommand.Execute(null));
        RootGrid.KeyDown += OnRootKeyDown;
    }

    private void AddAccel(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accel = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accel.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(accel);
    }

    private async void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        ViewModel.Settings.WindowWidth = (int)(AppWindow.Size.Width / scale);
        ViewModel.Settings.WindowHeight = (int)(AppWindow.Size.Height / scale);
        ViewModel.PersistSettings();
    }
}
