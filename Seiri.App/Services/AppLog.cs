namespace Seiri.Services;

public static class AppLog
{
    public static string? FilePath { get; set; }

    public static void Write(string message)
    {
        try
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                return;
            }

            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never throw into the UI.
        }
    }

    public static void Error(string context, Exception ex) => Write($"{context}: {ex}");
}
