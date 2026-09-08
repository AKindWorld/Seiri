using Seiri.Core.Contracts;

namespace Seiri.Infrastructure;

public sealed class PortableAppHome : IAppHome
{
    public PortableAppHome()
    {
        var env = Environment.GetEnvironmentVariable("SEIRI_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            Root = Path.GetFullPath(env);
        }
        else
        {
            Root = AppContext.BaseDirectory;
        }
    }

    public string Root { get; }
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string LibrariesPath => Path.Combine(Root, "libraries.json");
    public string ModelsDirectory => Path.Combine(Root, "models");
    public string LogsDirectory => Path.Combine(Root, "logs");

    public void EnsureCreated()
    {
        if (!IsWritable(Root))
        {
            throw new InvalidOperationException(
                $"Seiri needs a writable app folder. '{Root}' is not writable. Move Seiri out of Program Files, or set SEIRI_HOME.");
        }

        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".seiri-write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
