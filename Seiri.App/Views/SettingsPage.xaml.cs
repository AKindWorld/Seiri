using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Seiri.Core;
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

    public bool HardwareAcceleration
    {
        get => ViewModel.UseHardwareAcceleration;
        set => ViewModel.UseHardwareAcceleration = value;
    }

    public bool SearchAsYouType
    {
        get => ViewModel.Settings.SearchAsYouType;
        set
        {
            ViewModel.Settings.SearchAsYouType = value;
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

    public static Visibility NotVis(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowDownload(bool installed, bool downloading) =>
        !installed && !downloading ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowDelete(bool installed, bool downloading) =>
        installed && !downloading ? Visibility.Visible : Visibility.Collapsed;

    public bool UseDanbooruAliases
    {
        get => ViewModel.Settings.UseDanbooruAliases;
        set
        {
            ViewModel.Settings.UseDanbooruAliases = value;
            ViewModel.ApplyAliasSetting();
        }
    }

    private string CustomPreprocess()
    {
        if (CustomPreprocessBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            return CustomModelSpec.SanitizePreprocess(tag);
        }

        return "WdV3";
    }

    private async void OnDownloadCustomHf(object sender, RoutedEventArgs e)
    {
        if (!CustomModelSpec.TryParseRepo(HfRepoBox.Text, out var repo))
        {
            ViewModel.ErrorMessage = "Use a Hugging Face repo like org/name.";
            return;
        }

        var preprocess = CustomPreprocess();
        var clip = preprocess.Equals("Clip224", StringComparison.OrdinalIgnoreCase);
        var entry = new ModelCatalogEntry
        {
            Id = CustomModelSpec.IdFromRepo(repo),
            DisplayName = repo,
            Repo = repo,
            License = "Custom",
            Description = clip
                ? "Custom CLIP-style encoder for similar images. Large download."
                : "Custom Hugging Face tagger. Must ship model.onnx and selected_tags.csv on main.",
            Preprocess = preprocess,
            Files = clip
                ? [new ModelFile { Name = "model.onnx" }]
                : [new ModelFile { Name = "model.onnx" }, new ModelFile { Name = "selected_tags.csv" }]
        };

        try
        {
            await ViewModel.AddCustomModelAsync(entry);
            var card = ViewModel.Models.FirstOrDefault(m => m.Entry.Id == entry.Id);
            if (card is not null)
            {
                await card.DownloadCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DownloadCustomHf", ex);
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private async void OnInstallLocalModel(object sender, RoutedEventArgs e)
    {
        var onnxPicker = new FileOpenPicker();
        onnxPicker.FileTypeFilter.Add(".onnx");
        InitializeWithWindow.Initialize(onnxPicker, WindowNative.GetWindowHandle(App.Window));
        var onnx = await onnxPicker.PickSingleFileAsync();
        if (onnx is null)
        {
            return;
        }

        var preprocess = CustomPreprocess();
        string? csvPath = null;
        if (!preprocess.Equals("Clip224", StringComparison.OrdinalIgnoreCase))
        {
            var csvPicker = new FileOpenPicker();
            csvPicker.FileTypeFilter.Add(".csv");
            InitializeWithWindow.Initialize(csvPicker, WindowNative.GetWindowHandle(App.Window));
            var csv = await csvPicker.PickSingleFileAsync();
            if (csv is null)
            {
                ViewModel.ErrorMessage = "Tagger models need a selected_tags.csv with a name column.";
                return;
            }

            csvPath = csv.Path;
        }

        var entry = new ModelCatalogEntry
        {
            Id = CustomModelSpec.IdFromFile(onnx.Path),
            DisplayName = Path.GetFileNameWithoutExtension(onnx.Path),
            Repo = "local",
            License = "Custom",
            Description = preprocess.Equals("Clip224", StringComparison.OrdinalIgnoreCase)
                ? "Local CLIP-style encoder for similar images."
                : "Local ONNX tagger with selected_tags.csv.",
            Preprocess = preprocess,
            Files = csvPath is null
                ? [new ModelFile { Name = "model.onnx" }]
                : [new ModelFile { Name = "model.onnx" }, new ModelFile { Name = "selected_tags.csv" }]
        };

        try
        {
            await ViewModel.Downloader.InstallLocalAsync(entry, onnx.Path, csvPath);
            await ViewModel.AddCustomModelAsync(entry);
            var card = ViewModel.Models.FirstOrDefault(m => m.Entry.Id == entry.Id);
            card?.RefreshInstalled();
            if (card is not null && !card.IsEnabled && !preprocess.Equals("Clip224", StringComparison.OrdinalIgnoreCase))
            {
                card.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private async void OnDownloadAliases(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.DownloadAliasesAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("DownloadAliases", ex);
            ViewModel.ErrorMessage = ex.Message;
        }
    }

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
            try
            {
                await ViewModel.RemoveLibraryAsync(path, deleteGenerated: false);
            }
            catch (Exception ex)
            {
                AppLog.Error("RemoveLibrary", ex);
                ViewModel.ErrorMessage = ex.Message;
            }
        }
    }

    private async void OnRescan(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            try
            {
                await ViewModel.RescanAsync(path);
            }
            catch (Exception ex)
            {
                AppLog.Error("Rescan library", ex);
                ViewModel.ErrorMessage = ex.Message;
            }
        }
    }
}
