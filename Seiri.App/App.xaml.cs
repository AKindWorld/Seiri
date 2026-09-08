using Microsoft.UI.Xaml;
using Seiri.Core.Contracts;
using Seiri.Core.Tagging;
using Seiri.Infrastructure;
using Seiri.Services;
using Seiri.ViewModels;

namespace Seiri;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);
    public static ShellViewModel Shell { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var appHome = new PortableAppHome();
        appHome.EnsureCreated();
        AppLog.FilePath = Path.Combine(appHome.LogsDirectory, "seiri.log");

        ILibraryRegistry registry = new JsonLibraryRegistry(appHome);
        ISettingsStore settingsStore = new JsonSettingsStore(appHome);
        var libraries = new LibraryService(registry, new FileMediaScanner());
        var thumbs = new ThumbnailGenerator();
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "Assets", "model-catalog.json");
        var catalog = ModelCatalog.Load(catalogPath);
        var downloader = new ModelDownloader(appHome);
        var tagging = new TaggingService(appHome, libraries);

        Shell = new ShellViewModel(libraries, settingsStore, thumbs, appHome, downloader, tagging, catalog);
        Window = new MainWindow(Shell);
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Closed += async (_, _) =>
        {
            Shell.DisposeWatcher();
            await libraries.DisposeAsync();
        };
        Window.Activate();
        _ = StartAsync();
    }

    private static async Task StartAsync()
    {
        await Shell.InitializeAsync();
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--library", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await Shell.AddLibraryAsync(args[i + 1]);
            }
            catch (InvalidOperationException)
            {
                // Already in the library list.
            }
            catch (Exception ex)
            {
                Shell.ErrorMessage = ex.Message;
            }
        }
    }
}
