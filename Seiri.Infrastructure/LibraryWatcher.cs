using Seiri.Core;

namespace Seiri.Infrastructure;

public sealed class LibraryWatcher : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public event Action<string>? Changed;

    private int _pause;

    public void Watch(string root)
    {
        lock (_gate)
        {
            if (_watchers.ContainsKey(root) || !Directory.Exists(root))
            {
                return;
            }

            FileSystemWatcher watcher;
            try
            {
                watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                    EnableRaisingEvents = true
                };
            }
            catch (Exception ex)
            {
                AppLog.Error($"watch {root}", ex);
                return;
            }

            watcher.Changed += (_, e) => OnEvent(root, e.FullPath);
            watcher.Created += (_, e) => OnEvent(root, e.FullPath);
            watcher.Deleted += (_, e) => OnEvent(root, e.FullPath);
            watcher.Renamed += (_, e) => OnEvent(root, e.FullPath);
            watcher.Error += (_, e) => AppLog.Error($"watcher {root}", e.GetException());
            _watchers[root] = watcher;
        }
    }

    public void Pause() => Interlocked.Increment(ref _pause);

    public void Resume() => Interlocked.Decrement(ref _pause);

    public void Unwatch(string root)
    {
        lock (_gate)
        {
            if (_watchers.Remove(root, out var watcher))
            {
                watcher.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            foreach (var cts in _debounce.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _debounce.Clear();
        }
    }

    private void OnEvent(string root, string fullPath)
    {
        try
        {
            OnEventCore(root, fullPath);
        }
        catch (Exception ex)
        {
            AppLog.Error($"watcher event {root}", ex);
        }
    }

    private void OnEventCore(string root, string fullPath)
    {
        if (Volatile.Read(ref _pause) > 0 || string.IsNullOrEmpty(fullPath))
        {
            return;
        }

        if (GeneratedLayout.IsGeneratedDirectoryName(Path.GetFileName(fullPath)))
        {
            return;
        }

        var generated = GeneratedLayout.GetFolder(root);
        if (fullPath.StartsWith(generated + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(GeneratedLayout.GetFolder(root, GeneratedLayout.FallbackFolderName) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_debounce.TryGetValue(root, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            cts = new CancellationTokenSource();
            _debounce[root] = cts;
        }

        AppLog.Run(() => DebounceAsync(root, cts), $"watcher debounce {root}");
    }

    private async Task DebounceAsync(string root, CancellationTokenSource cts)
    {
        await Task.Delay(400, cts.Token).ConfigureAwait(false);
        Changed?.Invoke(root);
    }
}
