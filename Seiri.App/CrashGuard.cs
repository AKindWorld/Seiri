using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Seiri.Core;

namespace Seiri;

/// <summary>
/// Logs native access violations and other fatal SEH codes that never reach
/// managed <c>UnhandledException</c>. The process still dies; the log must not.
/// </summary>
internal static class CrashGuard
{
    private const uint ExceptionAccessViolation = 0xC0000005;
    private const uint ExceptionStackOverflow = 0xC00000FD;
    private const uint ExceptionIllegalInstruction = 0xC000001D;
    private const uint ExceptionIntegerDivideByZero = 0xC0000094;
    private const uint ExceptionStackBufferOverrun = 0xC0000409;
    private const uint ExceptionInPageError = 0xC0000006;
    private const uint ExceptionArrayBounds = 0xC000008C;
    private const int ExceptionContinueSearch = 0;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenAlways = 4;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileEnd = 2;

    private static VectoredHandler? _handler;
    private static IntPtr _crashHandle = new(-1);
    private static IntPtr _logHandle = new(-1);
    private static readonly byte[] NativeBuffer = new byte[1024];

    public static void Install(string crashLogPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(crashLogPath) ?? "logs");
            _crashHandle = OpenLog(crashLogPath);
            var seiri = Path.Combine(Path.GetDirectoryName(crashLogPath) ?? "logs", "seiri.log");
            _logHandle = OpenLog(seiri);
            _handler = OnNativeException;
            AddVectoredExceptionHandler(1, _handler);
            AddVectoredContinueHandler(1, _handler);
            SetUnhandledExceptionFilter(_handler);
        }
        catch (Exception ex)
        {
            AppLog.Error("CrashGuard.Install", ex);
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            AppLog.Write("process exit");
            AppLog.Flush();
        };
    }

    public static void HookManaged(Application app)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Fatal(
                "AppDomain.UnhandledException terminating=" + e.IsTerminating,
                e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)));
            AppLog.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Fatal("UnobservedTaskException", e.Exception);
            AppLog.Flush();
            e.SetObserved();
        };
        app.UnhandledException += (_, e) =>
        {
            AppLog.Fatal("WinUI UnhandledException", e.Exception);
            AppLog.Flush();
            try
            {
                if (App.Shell is not null)
                {
                    App.Shell.ErrorMessage = e.Exception.Message;
                }
            }
            catch
            {
            }

            e.Handled = true;
        };
    }

    private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
    {
        if (e.Exception is AccessViolationException
            or SEHException
            or DllNotFoundException
            or BadImageFormatException
            or OutOfMemoryException)
        {
            AppLog.Fatal("FirstChance " + e.Exception.GetType().Name, e.Exception);
            AppLog.Flush();
        }
    }

    private static int OnNativeException(IntPtr exceptionPointers)
    {
        try
        {
            if (exceptionPointers == IntPtr.Zero)
            {
                return ExceptionContinueSearch;
            }

            var pointers = Marshal.PtrToStructure<ExceptionPointers>(exceptionPointers);
            if (pointers.ExceptionRecord == IntPtr.Zero)
            {
                return ExceptionContinueSearch;
            }

            var record = Marshal.PtrToStructure<ExceptionRecord>(pointers.ExceptionRecord);
            if (!IsFatal(record.ExceptionCode))
            {
                return ExceptionContinueSearch;
            }

            var line = $"{DateTimeOffset.Now:O} FATAL native code=0x{record.ExceptionCode:X8} addr=0x{record.ExceptionAddress:X}{Environment.NewLine}";
            WriteNative(_crashHandle, line);
            WriteNative(_logHandle, line);
        }
        catch
        {
        }

        return ExceptionContinueSearch;
    }

    private static bool IsFatal(uint code)
    {
        if (code is 0x80000003 or 0x80000004 or 0x80000001 or 0x40010006 or 0x4001000A)
        {
            return false;
        }

        if (code is 0xE0434352 or 0xE0434F4D)
        {
            return false;
        }

        return (code & 0xF0000000) == 0xC0000000
            || (code & 0xFFFF0000) == 0x887A0000
            || code is 0x8000FFFF or 0x80004005 or 0x8001010E or 0xC0000374 or 0xC0000417;
    }

    private static IntPtr OpenLog(string path) =>
        CreateFileW(
            path,
            GenericWrite,
            FileShareReadWrite,
            IntPtr.Zero,
            OpenAlways,
            FileAttributeNormal | 0x80000000,
            IntPtr.Zero);

    private static void WriteNative(IntPtr handle, string text)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return;
        }

        SetFilePointerEx(handle, 0, IntPtr.Zero, FileEnd);
        var n = Encoding.UTF8.GetBytes(text, NativeBuffer);
        WriteFile(handle, NativeBuffer, (uint)n, out _, IntPtr.Zero);
        FlushFileBuffers(handle);
    }

    private delegate int VectoredHandler(IntPtr exceptionPointers);

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionPointers
    {
        public IntPtr ExceptionRecord;
        public IntPtr ContextRecord;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionRecord
    {
        public uint ExceptionCode;
        public uint ExceptionFlags;
        public IntPtr ExceptionRecordPtr;
        public IntPtr ExceptionAddress;
        public uint NumberParameters;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr AddVectoredExceptionHandler(uint first, VectoredHandler handler);

    [DllImport("kernel32.dll")]
    private static extern IntPtr AddVectoredContinueHandler(uint first, VectoredHandler handler);

    [DllImport("kernel32.dll")]
    private static extern IntPtr SetUnhandledExceptionFilter(VectoredHandler handler);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(IntPtr hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(IntPtr hFile, long liDistanceToMove, IntPtr lpNewFilePointer, uint dwMoveMethod);
}
