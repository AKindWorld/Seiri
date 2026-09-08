using Seiri.Core.Models;

namespace Seiri.Core;

public static class SidecarFormat
{
    public static IReadOnlyList<string> Parse(string text)
    {
        var parts = text.Replace('\n', ',').Replace('\r', ',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = new List<string>();
        foreach (var part in parts)
        {
            var name = part.Replace('_', ' ').Trim().ToLowerInvariant();
            if (name.Length > 0 && !tags.Contains(name))
            {
                tags.Add(name);
            }
        }

        return tags;
    }

    public static string Format(IReadOnlyList<string> tags, AppSettings settings)
    {
        var names = tags.Select(t => settings.WriteUnderscores ? t.Replace(' ', '_') : t);
        return settings.SidecarFormat switch
        {
            "newline" => string.Join(Environment.NewLine, names),
            "comma" => string.Join(',', names),
            _ => string.Join(", ", names)
        };
    }

    public static string SidecarRelative(string mediaRelUnix) =>
        Path.ChangeExtension(mediaRelUnix, ".txt")!.Replace('\\', '/');
}
