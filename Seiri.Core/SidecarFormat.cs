using Seiri.Core.Models;
using Seiri.Core.Tagging;

namespace Seiri.Core;

public static class SidecarFormat
{
    public static readonly string[] PrefixedCategories =
    [
        "rating",
        "character",
        "copyright",
        "artist",
        "meta"
    ];

    public static IReadOnlyList<string> Parse(string text) =>
        ParseRecords(text).Select(t => t.Name).ToList();

    public static IReadOnlyList<TagRecord> ParseRecords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var hasPrefix = false;
        foreach (var line in lines)
        {
            if (TrySplitPrefix(line, out _, out _))
            {
                hasPrefix = true;
                break;
            }
        }

        if (!hasPrefix)
        {
            return SplitNames(normalized.Replace('\n', ','))
                .Select(name => new TagRecord { Name = name, Category = "general" })
                .ToList();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<TagRecord>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var category = "general";
            var payload = line;
            if (TrySplitPrefix(line, out var prefix, out var rest))
            {
                category = prefix == "general" ? "general" : prefix;
                payload = rest;
            }

            foreach (var name in SplitNames(payload))
            {
                if (seen.Add(name))
                {
                    tags.Add(new TagRecord { Name = name, Category = category });
                }
            }
        }

        return tags;
    }

    public static string Format(IReadOnlyList<string> tags, AppSettings settings) =>
        Format(
            tags.Select(t => new TagRecord { Name = TagDisplay.ToCanonical(t), Category = "general" }).ToList(),
            settings);

    public static string Format(IReadOnlyList<TagRecord> tags, AppSettings settings, string? rating = null)
    {
        var list = new List<TagRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(rating))
        {
            var name = TagDisplay.ToCanonical(rating);
            if (seen.Add(name))
            {
                list.Add(new TagRecord { Name = name, Category = "rating" });
            }
        }

        foreach (var tag in tags)
        {
            var name = TagDisplay.ToCanonical(tag.Name);
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            var category = string.IsNullOrWhiteSpace(tag.Category) ? "general" : tag.Category.ToLowerInvariant();
            list.Add(new TagRecord { Name = name, Category = category });
        }

        var blocks = new List<string>();
        foreach (var category in PrefixedCategories)
        {
            var names = list.Where(t => t.Category == category).Select(t => Display(t.Name, settings)).ToList();
            if (names.Count == 0)
            {
                continue;
            }

            blocks.Add($"{category}:{JoinNames(names, commaStyle: true, settings)}");
        }

        var general = list
            .Where(t => t.Category is not ("rating" or "character" or "copyright" or "artist" or "meta"))
            .Select(t => Display(t.Name, settings))
            .ToList();
        if (general.Count > 0)
        {
            blocks.Add(JoinNames(general, commaStyle: false, settings));
        }

        return string.Join(Environment.NewLine, blocks);
    }

    public static string SidecarRelative(string mediaRelUnix) =>
        Path.ChangeExtension(mediaRelUnix, ".txt")!.Replace('\\', '/');

    public static string UiGroup(string? category) => category switch
    {
        "rating" => "rating",
        "character" => "character",
        "copyright" => "copyright",
        _ => "general"
    };

    private static bool TrySplitPrefix(string line, out string prefix, out string rest)
    {
        prefix = string.Empty;
        rest = string.Empty;
        var trimmed = line.Trim();
        var idx = trimmed.IndexOf(':');
        if (idx <= 0)
        {
            return false;
        }

        var maybe = trimmed[..idx].Trim().ToLowerInvariant();
        if (maybe is not ("rating" or "character" or "copyright" or "artist" or "meta" or "general"))
        {
            return false;
        }

        prefix = maybe;
        rest = trimmed[(idx + 1)..];
        return true;
    }

    private static IEnumerable<string> SplitNames(string payload)
    {
        foreach (var part in payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = TagDisplay.ToCanonical(part);
            if (name.Length > 0)
            {
                yield return name;
            }
        }
    }

    private static string Display(string name, AppSettings settings) =>
        settings.WriteUnderscores && !TagDisplay.IsKaomoji(name) ? name.Replace(' ', '_') : name;

    private static string JoinNames(IReadOnlyList<string> names, bool commaStyle, AppSettings settings)
    {
        if (commaStyle)
        {
            return " " + string.Join(", ", names);
        }

        return settings.SidecarFormat switch
        {
            "newline" => string.Join(Environment.NewLine, names),
            "comma" => string.Join(',', names),
            _ => string.Join(", ", names)
        };
    }
}
