using System.Text;

namespace Seiri.Core;

/// <summary>
/// Process-wide log. Writes through to disk so a native crash still leaves a trail.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static StreamWriter? _log;
    private static StreamWriter? _crash;
    private static bool _fallbackTried;

    public static string? FilePath { get; private set; }
    public static string? CrashFilePath { get; private set; }

    public static void Initialize(string logsDirectory)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory))
        {
            return;
        }

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(logsDirectory);
                var logPath = Path.Combine(logsDirectory, "seiri.log");
                var crashPath = Path.Combine(logsDirectory, "crash.log");
                if (string.Equals(FilePath, logPath, StringComparison.OrdinalIgnoreCase) && _log is not null)
                {
                    return;
                }

                CloseWriters();
                FilePath = logPath;
                CrashFilePath = crashPath;
                _log = OpenWriter(logPath);
                _crash = OpenWriter(crashPath);
                WriteLine(_log, $"{DateTimeOffset.Now:O} log opened pid={Environment.ProcessId} {Environment.OSVersion}");
            }
            catch
            {
                CloseWriters();
            }
        }
    }

    public static void Write(string message) => WriteCore(message, crash: false);

    /// <summary>Write and flush so a native crash still leaves the last line on disk.</summary>
    public static void Heartbeat(string message)
    {
        WriteCore(message, crash: false);
        Flush();
    }

    public static void Error(string context, Exception ex) =>
        WriteCore($"{context}: {Format(ex)}", crash: false);

    public static void Fatal(string context, Exception? ex)
    {
        var text = ex is null ? context : $"{context}: {Format(ex)}";
        WriteCore($"FATAL {text}", crash: true);
    }

    public static void Flush()
    {
        lock (Gate)
        {
            try
            {
                _log?.Flush();
                _log?.BaseStream.Flush();
                _crash?.Flush();
                _crash?.BaseStream.Flush();
            }
            catch
            {
            }
        }
    }

    public static async Task RunAsync(Func<Task> work, string context)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error(context, ex);
        }
    }

    public static void Run(Func<Task> work, string context) => _ = RunAsync(work, context);

    private static void WriteCore(string message, bool crash)
    {
        lock (Gate)
        {
            EnsureFallback();
            var line = $"{DateTimeOffset.Now:O} {message}";
            WriteLine(_log, line);
            if (crash)
            {
                WriteLine(_crash, line);
            }
        }
    }

    private static void EnsureFallback()
    {
        if (_log is not null || _fallbackTried)
        {
            return;
        }

        _fallbackTried = true;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            FilePath ??= Path.Combine(dir, "seiri.log");
            CrashFilePath ??= Path.Combine(dir, "crash.log");
            _log = OpenWriter(FilePath);
            _crash = OpenWriter(CrashFilePath);
        }
        catch
        {
        }
    }

    private static StreamWriter OpenWriter(string path)
    {
        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            4096,
            FileOptions.WriteThrough);
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    private static void WriteLine(StreamWriter? writer, string line)
    {
        if (writer is null)
        {
            return;
        }

        try
        {
            writer.WriteLine(line);
            writer.Flush();
            writer.BaseStream.Flush();
        }
        catch
        {
        }
    }

    private static void CloseWriters()
    {
        try
        {
            _log?.Dispose();
        }
        catch
        {
        }

        try
        {
            _crash?.Dispose();
        }
        catch
        {
        }

        _log = null;
        _crash = null;
    }

    private static string Format(Exception ex)
    {
        var hr = $"hr=0x{ex.HResult:X8}";
        return $"{hr} {ex}";
    }
}
