using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Tagging;
using Seiri.Infrastructure;
using Seiri.Services;
using Seiri.ViewModels;

namespace Seiri;

public partial class App : Application
{
    private static Mutex? _instanceMutex;

    public static Window Window { get; private set; } = null!;
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);
    public static ShellViewModel Shell { get; private set; } = null!;

    public App()
    {
        var logs = Path.Combine(AppContext.BaseDirectory, "logs");
        AppLog.Initialize(logs);
        CrashGuard.Install(Path.Combine(logs, "crash.log"));
        CrashGuard.HookManaged(this);
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLog.Fatal("InitializeComponent", ex);
            throw;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private static bool ActivateExistingInstance()
    {
        _instanceMutex = new Mutex(true, @"Local\Seiri.SingleInstance", out var created);
        if (created)
        {
            return false;
        }

        foreach (var process in Process.GetProcessesByName("Seiri"))
        {
            if (process.Id == Environment.ProcessId)
            {
                continue;
            }

            try
            {
                if (!process.Responding)
                {
                    process.Kill();
                    continue;
                }

                var hwnd = process.MainWindowHandle;
                if (hwnd != 0)
                {
                    ShowWindow(hwnd, 9);
                    SetForegroundWindow(hwnd);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("ActivateExistingInstance", ex);
            }
        }

        return false;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            if (ActivateExistingInstance())
            {
                Environment.Exit(0);
                return;
            }

            var appHome = new PortableAppHome();
            appHome.EnsureCreated();
            AppLog.Initialize(appHome.LogsDirectory);
            AppLog.Write("Seiri starting");

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
                try
                {
                    AppLog.Write("Seiri closing");
                    Shell.DisposeWatcher();
                    await libraries.DisposeAsync();
                    _instanceMutex?.ReleaseMutex();
                    _instanceMutex?.Dispose();
                    AppLog.Flush();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Window.Closed", ex);
                }
            };
            Window.Activate();
            AppLog.Write("Seiri started");
            AppLog.Run(() => StartAsync(), "StartAsync");
        }
        catch (Exception ex)
        {
            AppLog.Fatal("OnLaunched", ex);
            throw;
        }
    }

    private static async Task StartAsync()
    {
        try
        {
            await Shell.InitializeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Initialize", ex);
            Shell.ErrorMessage = ex.Message;
            Shell.IsLibraryLoading = false;
            return;
        }

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
                AppLog.Error("AddLibrary --library", ex);
                Shell.ErrorMessage = ex.Message;
            }
        }
    }
}
