using System.Text.Json;
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
        if (!File.Exists(appHome.LibrariesPath))
        {
            return [];
        }

        var json = File.ReadAllText(appHome.LibrariesPath);
        var file = JsonSerializer.Deserialize<LibrariesFile>(json, JsonUtil.Options) ?? new LibrariesFile();
        return file.Roots;
    }

    public void Save(IEnumerable<string> roots)
    {
        var file = new LibrariesFile { Roots = [.. roots] };
        File.WriteAllText(appHome.LibrariesPath, JsonSerializer.Serialize(file, JsonUtil.Options));
    }
}

public sealed class JsonSettingsStore(IAppHome appHome) : ISettingsStore
{
    public AppSettings Load()
    {
        if (!File.Exists(appHome.SettingsPath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(appHome.SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonUtil.Options) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        File.WriteAllText(appHome.SettingsPath, JsonSerializer.Serialize(settings, JsonUtil.Options));
    }
}
