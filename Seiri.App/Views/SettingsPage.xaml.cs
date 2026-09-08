using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Seiri.Core.Models;
using Seiri.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Seiri.Views;

public sealed partial class SettingsPage : Page
{
    public ShellViewModel ViewModel => App.Shell;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public string Theme
    {
        get => ViewModel.Settings.Theme;
        set
        {
            ViewModel.Settings.Theme = value;
            ViewModel.PersistSettings();
        }
    }

    public int ThemeIndex
    {
        get => ViewModel.Settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        set => Theme = value switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System"
        };
    }

    public int DensityIndex
    {
        get => ViewModel.Gallery.Density switch
        {
            LayoutDensity.Small => 0,
            LayoutDensity.Large => 2,
            _ => 1
        };
        set
        {
            var density = value switch
            {
                0 => "Small",
                2 => "Large",
                _ => "Medium"
            };
            ViewModel.Gallery.SetDensityCommand.Execute(density);
        }
    }

    public double MaxTags
    {
        get => ViewModel.Settings.MaxTags;
        set
        {
            ViewModel.Settings.MaxTags = (int)Math.Clamp(value, 1, 128);
            ViewModel.PersistSettings();
        }
    }

    public int ExecutionIndex
    {
        get => ViewModel.Settings.ExecutionProvider switch
        {
            "GPU" => 1,
            "CPU" => 2,
            _ => 0
        };
        set
        {
            ViewModel.Settings.ExecutionProvider = value switch
            {
                1 => "GPU",
                2 => "CPU",
                _ => "Auto"
            };
            ViewModel.PersistSettings();
        }
    }

    public static Visibility BoolVis(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowDownload(bool installed, bool downloading) =>
        !installed && !downloading ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowDelete(bool installed, bool downloading) =>
        installed && !downloading ? Visibility.Visible : Visibility.Collapsed;

    private async void OnAddFolder(object sender, RoutedEventArgs e)
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

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            await ViewModel.RemoveLibraryAsync(path, deleteGenerated: false);
        }
    }

    private async void OnRescan(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            await ViewModel.RescanAsync(path);
        }
    }
}
