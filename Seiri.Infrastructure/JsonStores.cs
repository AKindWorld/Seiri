using System.Text.Json;
using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Models;

namespace Seiri.Infrastructure;

internal static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed class JsonLibraryRegistry(IAppHome appHome) : ILibraryRegistry
{
    public IReadOnlyList<string> GetRoots()
    {
        try
        {
            if (!File.Exists(appHome.LibrariesPath))
            {
                return [];
            }

            var json = File.ReadAllText(appHome.LibrariesPath);
            var file = JsonSerializer.Deserialize<LibrariesFile>(json, JsonUtil.Options) ?? new LibrariesFile();
            return file.Roots;
        }
        catch (Exception ex)
        {
            AppLog.Error("libraries.json", ex);
            return [];
        }
    }

    public void Save(IEnumerable<string> roots)
    {
        try
        {
            var file = new LibrariesFile { Roots = [.. roots] };
            File.WriteAllText(appHome.LibrariesPath, JsonSerializer.Serialize(file, JsonUtil.Options));
        }
        catch (Exception ex)
        {
            AppLog.Error("save libraries.json", ex);
            throw;
        }
    }
}

public sealed class JsonSettingsStore(IAppHome appHome) : ISettingsStore
{
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(appHome.SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(appHome.SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonUtil.Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error("settings.json", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(appHome.SettingsPath, JsonSerializer.Serialize(settings, JsonUtil.Options));
        }
        catch (Exception ex)
        {
            AppLog.Error("save settings.json", ex);
            throw;
        }
    }
}
