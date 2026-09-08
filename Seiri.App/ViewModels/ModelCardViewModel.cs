using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seiri.Core.Models;
using Seiri.Infrastructure;

namespace Seiri.ViewModels;

public partial class ModelCardViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public ModelCardViewModel(ShellViewModel shell, ModelCatalogEntry entry)
    {
        _shell = shell;
        Entry = entry;
        RefreshInstalled();
    }

    public ModelCatalogEntry Entry { get; }

    [ObservableProperty]
    public partial bool IsInstalled { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double DownloadPercent { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not installed";

    public string SizeLabel => FormatBytes(Entry.SizeBytes);

    public string DownloadName => $"Download {Entry.DisplayName}";

    public string DeleteName => $"Delete {Entry.DisplayName}";

    public bool IsEnabled
    {
        get => _shell.Settings.EnabledModelIds.Any(id => id.Equals(Entry.Id, StringComparison.OrdinalIgnoreCase));
        set
        {
            var ids = _shell.Settings.EnabledModelIds
                .Where(id => !id.Equals(Entry.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (value)
            {
                ids.Add(Entry.Id);
            }

            _shell.Settings.EnabledModelIds = ids;
            _shell.PersistSettings();
            OnPropertyChanged();
            _shell.NotifyTaggingState();
        }
    }

    public int PresetIndex
    {
        get => _shell.Settings.ModelPresets.TryGetValue(Entry.Id, out var preset) ? (int)preset : 0;
        set
        {
            _shell.Settings.ModelPresets[Entry.Id] = (ThresholdPreset)Math.Clamp(value, 0, 2);
            _shell.PersistSettings();
            OnPropertyChanged();
        }
    }

    public void RefreshInstalled()
    {
        IsInstalled = ModelDownloader.IsInstalled(_shell.AppHome, Entry);
        StatusText = IsInstalled
            ? $"Installed · {SizeLabel} · {Entry.License}"
            : $"Not installed · {SizeLabel} · {Entry.License}";
        OnPropertyChanged(nameof(IsEnabled));
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        IsDownloading = true;
        DownloadPercent = 0;
        StatusText = "Starting download…";
        try
        {
            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                DownloadPercent = p.Percent;
                StatusText = $"Downloading {p.FileName} · {p.Percent:0}%";
            });
            await _shell.Downloader.DownloadAsync(Entry, progress);
            RefreshInstalled();
            if (IsInstalled && !IsEnabled)
            {
                IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            _shell.ErrorMessage = $"Download failed for {Entry.DisplayName}: {ex.Message}";
            StatusText = "Download failed";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        try
        {
            await _shell.UnloadModelAsync(Entry.Id);
            await _shell.Downloader.DeleteAsync(Entry);
            if (IsEnabled)
            {
                IsEnabled = false;
            }

            RefreshInstalled();
        }
        catch (Exception ex)
        {
            _shell.ErrorMessage = $"Could not delete {Entry.DisplayName}: {ex.Message}";
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000)
        {
            return $"{bytes / 1_000_000_000.0:0.00} GB";
        }

        if (bytes >= 1_000_000)
        {
            return $"{bytes / 1_000_000.0:0} MB";
        }

        return $"{bytes} B";
    }
}
