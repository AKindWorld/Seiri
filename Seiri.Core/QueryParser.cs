using Seiri.Core.Models;

namespace Seiri.Core;

public static class QueryParser
{
    public static readonly (string Prefix, string Detail)[] Prefixes =
    [
        ("tag:", "Filter by tag"),
        ("character:", "Filter by character"),
        ("copyright:", "Filter by copyright"),
        ("rating:", "Filter by rating"),
        ("type:", "Filter by media type (image or video)"),
        ("ext:", "Filter by file extension"),
        ("taken:", "Date taken (YYYY-MM-DD); falls back to file time if missing"),
        ("added:", "Date added to the library (YYYY-MM-DD)"),
        ("modified:", "Date the file was modified (YYYY-MM-DD)"),
        ("date:", "Same as taken: (YYYY-MM-DD)"),
        ("folder:", "Filter by library or folder name"),
        ("orientation:", "landscape, portrait, or square"),
        ("ratio:", "16:9, 4:3, 3:2, 1:1, 9:16"),
        ("color:", "dominant color bucket (blue, red, …)")
    ];

    public static MediaQuery Parse(string? raw, MediaQuery? seed = null)
    {
        var query = seed?.Clone() ?? new MediaQuery();
        query.Raw = raw;
        query.Tags.Clear();
        query.Extensions.Clear();
        query.Text = null;
        query.Folder = null;
        query.Folders.Clear();
        query.DateField = DateField.Taken;
        query.DateExact = null;
        query.DateAfter = null;
        query.DateBefore = null;
        query.Orientations.Clear();
        query.Aspects.Clear();
        query.Colors.Clear();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return query;
        }

        var tokens = Tokenize(raw);
        for (var i = 0; i < tokens.Count; i++)
        {
            var text = tokens[i];
            var exclude = false;
            if (text.StartsWith('-'))
            {
                exclude = true;
                text = text[1..];
            }

            var (prefix, value) = SplitPrefix(text);
            value = Unquote(value).Replace('_', ' ').Trim().ToLowerInvariant();
            if (value.Length == 0 && prefix is null)
            {
                continue;
            }

            switch (prefix)
            {
                case "type":
                    query.Kind = value is "video" or "videos" ? MediaKindFilter.Videos
                        : value is "image" or "images" or "photo" or "photos" ? MediaKindFilter.Images
                        : MediaKindFilter.All;
                    break;
                case "ext":
                    query.Extensions.Add(value.TrimStart('.'));
                    break;
                case "folder":
                    query.Folder = ConsumeRest(tokens, ref i, value);
                    if (!string.IsNullOrWhiteSpace(query.Folder))
                    {
                        query.Folders.Add(query.Folder);
                    }

                    break;
                case "date" or "taken":
                    query.DateField = DateField.Taken;
                    ApplyDate(query, value);
                    break;
                case "added":
                    query.DateField = DateField.Added;
                    ApplyDate(query, value);
                    break;
                case "modified":
                    query.DateField = DateField.Modified;
                    ApplyDate(query, value);
                    break;
                case "orientation":
                    if (value.Length > 0)
                    {
                        query.Orientations.Add(value);
                    }

                    break;
                case "ratio":
                    if (value.Length > 0)
                    {
                        query.Aspects.Add(value.Replace('x', ':'));
                    }

                    break;
                case "color":
                    if (value.Length > 0)
                    {
                        query.Colors.Add(value);
                    }

                    break;
                case "tag" or "character" or "copyright" or "artist" or "meta" or "rating" or null:
                    if (prefix is "character" or "copyright" or "rating" or "artist" or "meta"
                        || (value.Length == 0 && prefix is not null))
                    {
                        value = ConsumeRest(tokens, ref i, value);
                    }

                    if (value.Length > 0)
                    {
                        query.Tags.Add(new TagClause
                        {
                            Name = value,
                            Exclude = exclude,
                            Category = prefix is null or "tag" ? null : prefix
                        });
                    }

                    break;
            }
        }

        if (query.Tags.Count > 1)
        {
            var seen = new Dictionary<string, TagClause>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in query.Tags)
            {
                seen[tag.Name] = tag;
            }

            query.Tags = [.. seen.Values];
        }

        return query;
    }

    public static string ToRaw(MediaQuery query)
    {
        var parts = new List<string>();
        foreach (var tag in query.Tags)
        {
            parts.Add(FormatTagToken(tag.Name, tag.Exclude, tag.Category));
        }

        if (query.Kind == MediaKindFilter.Images)
        {
            parts.Add("type:image");
        }
        else if (query.Kind == MediaKindFilter.Videos)
        {
            parts.Add("type:video");
        }

        foreach (var ext in query.Extensions)
        {
            parts.Add($"ext:{ext}");
        }

        var folderNames = query.Folders.Count > 0
            ? query.Folders
            : string.IsNullOrWhiteSpace(query.Folder) ? [] : [query.Folder];
        foreach (var folder in folderNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            parts.Add(FormatFolderToken(folder));
        }

        var datePrefix = query.DateField switch
        {
            DateField.Added => "added",
            DateField.Modified => "modified",
            _ => "taken"
        };
        if (query.DateExact is { } exact)
        {
            parts.Add($"{datePrefix}:{exact:yyyy-MM-dd}");
        }

        if (query.DateAfter is { } after)
        {
            parts.Add($"{datePrefix}:>{after:yyyy-MM-dd}");
        }

        if (query.DateBefore is { } before)
        {
            parts.Add($"{datePrefix}:<{before:yyyy-MM-dd}");
        }

        foreach (var o in query.Orientations)
        {
            parts.Add($"orientation:{o}");
        }

        foreach (var a in query.Aspects)
        {
            parts.Add($"ratio:{a}");
        }

        foreach (var c in query.Colors)
        {
            parts.Add($"color:{c}");
        }

        return string.Join(' ', parts);
    }

    public static IReadOnlyList<SearchSuggestion> Suggest(
        string? raw,
        IReadOnlyList<TagRecord> catalog,
        IReadOnlyList<string>? folders = null)
    {
        var text = raw ?? string.Empty;
        var suggestions = new List<SearchSuggestion>();
        var last = LastToken(text, out var before);
        if (last.Length == 0 && before.TrimEnd().EndsWith("folder:", StringComparison.OrdinalIgnoreCase))
        {
            var idx = before.LastIndexOf("folder:", StringComparison.OrdinalIgnoreCase);
            last = "folder:";
            before = idx <= 0 ? string.Empty : before[..idx];
        }

        if (string.IsNullOrWhiteSpace(text) || last.Length == 0)
        {
            foreach (var (prefix, detail) in Prefixes)
            {
                suggestions.Add(new SearchSuggestion
                {
                    Kind = "prefix",
                    Label = prefix,
                    Detail = detail,
                    ApplyText = string.IsNullOrWhiteSpace(before) ? prefix : before + prefix
                });
            }

            return suggestions;
        }

        var working = last.StartsWith('-') ? last[1..] : last;
        var (prefixName, value) = SplitPrefix(working);
        value = Unquote(value).Replace('_', ' ');

        if (prefixName is null && !working.Contains(':'))
        {
            foreach (var (prefix, detail) in Prefixes.Where(p => p.Prefix.StartsWith(working, StringComparison.OrdinalIgnoreCase)))
            {
                suggestions.Add(new SearchSuggestion
                {
                    Kind = "prefix",
                    Label = prefix,
                    Detail = detail,
                    ApplyText = Combine(before, prefix)
                });
            }
        }

        if (prefixName is "folder" && folders is { Count: > 0 })
        {
            var needle = value;
            foreach (var folder in folders.Where(f =>
                         needle.Length == 0 || f.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                suggestions.Add(new SearchSuggestion
                {
                    Kind = "folder",
                    Label = folder,
                    Detail = "Library or folder",
                    ApplyText = Combine(before, FormatFolderToken(folder))
                });
            }
        }

        if (value.Length > 0 || prefixName is "tag" or "character" or "copyright" or "rating" or null)
        {
            var needle = value;
            var hits = catalog
                .Where(t => TagMatch.Contains(t.Name, needle))
                .Where(t => prefixName is null or "tag" || t.Category.Equals(prefixName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => TagMatch.Rank(t.Name, needle))
                .ThenByDescending(t => t.UseCount)
                .ThenBy(t => t.Name)
                .Take(12)
                .ToList();

            var exact = hits.FirstOrDefault(t => t.Name.Equals(needle, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                var category = CategoryForToken(prefixName, exact.Category);
                suggestions.Insert(0, new SearchSuggestion
                {
                    Kind = "action",
                    Label = $"Include {TokenLabel(category)}\"{exact.Name}\"",
                    Detail = $"{exact.UseCount:N0} files",
                    ApplyText = Combine(before, FormatTagToken(exact.Name, exclude: false, category))
                });
                suggestions.Insert(1, new SearchSuggestion
                {
                    Kind = "action",
                    Label = $"Exclude {TokenLabel(category)}\"{exact.Name}\"",
                    Detail = "Must not have this tag",
                    ApplyText = Combine(before, FormatTagToken(exact.Name, exclude: true, category))
                });
            }

            foreach (var hit in hits)
            {
                var category = CategoryForToken(prefixName, hit.Category);
                suggestions.Add(new SearchSuggestion
                {
                    Kind = KindOf(category),
                    Label = hit.Name,
                    Detail = $"{hit.UseCount:N0}",
                    ApplyText = Combine(before, FormatTagToken(hit.Name, exclude: last.StartsWith('-'), category))
                });
            }
        }

        return suggestions;
    }

    public static IReadOnlyList<string> Tokenize(string raw)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in raw)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                current.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string LastToken(string raw, out string before)
    {
        var tokens = Tokenize(raw);
        if (tokens.Count == 0)
        {
            before = string.Empty;
            return string.Empty;
        }

        var last = tokens[^1];
        before = tokens.Count == 1 ? string.Empty : string.Join(' ', tokens.Take(tokens.Count - 1)) + " ";
        if (raw.EndsWith(' ') && !raw.TrimEnd().EndsWith('"'))
        {
            before = string.Join(' ', tokens) + " ";
            return string.Empty;
        }

        var trimmedBefore = before.TrimEnd();
        if (!last.Contains(':') && trimmedBefore.EndsWith(':'))
        {
            var space = trimmedBefore.LastIndexOf(' ');
            var prefixToken = space >= 0 ? trimmedBefore[(space + 1)..] : trimmedBefore;
            last = prefixToken + last;
            before = space >= 0 ? before[..(space + 1)] : string.Empty;
        }

        return last;
    }

    private static (string? Prefix, string Value) SplitPrefix(string token)
    {
        var idx = token.IndexOf(':');
        if (idx <= 0)
        {
            return (null, token);
        }

        return (token[..idx].ToLowerInvariant(), token[(idx + 1)..]);
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            return value[1..^1];
        }

        return value;
    }

    public static string FormatFolderToken(string folder)
    {
        var trimmed = folder.Trim();
        return trimmed.Contains(' ') ? $"folder:\"{trimmed}\"" : $"folder:{trimmed.Replace(' ', '_')}";
    }

    public static string FormatTagToken(string name, bool exclude = false, string? category = null)
    {
        var prefix = category is "character" or "copyright" or "rating" or "artist" or "meta"
            ? category
            : "tag";
        var body = name.Contains(' ') ? $"{prefix}:\"{name}\"" : $"{prefix}:{name.Replace(' ', '_')}";
        return exclude ? "-" + body : body;
    }

    private static string Combine(string before, string token) =>
        string.IsNullOrEmpty(before) ? token : before + token;

    private static string? CategoryForToken(string? prefixName, string? catalogCategory)
    {
        if (prefixName is "character" or "copyright" or "rating" or "artist" or "meta")
        {
            return prefixName;
        }

        return catalogCategory is "character" or "copyright" or "rating" or "artist" or "meta"
            ? catalogCategory
            : null;
    }

    private static string TokenLabel(string? category) =>
        category is "character" or "copyright" or "rating" or "artist" or "meta"
            ? $"{category}:"
            : "tag:";

    private static string KindOf(string? category) =>
        category is "character" or "copyright" or "rating" or "artist" or "meta"
            ? category
            : "tag";

    private static string ConsumeRest(IReadOnlyList<string> tokens, ref int index, string start)
    {
        var folder = start;
        while (index + 1 < tokens.Count)
        {
            var next = tokens[index + 1];
            if (next.StartsWith('-') || next.Contains(':'))
            {
                break;
            }

            index++;
            var piece = Unquote(next).Replace('_', ' ').Trim().ToLowerInvariant();
            if (piece.Length == 0)
            {
                continue;
            }

            folder = string.IsNullOrEmpty(folder) ? piece : folder + " " + piece;
        }

        return folder;
    }

    private static void ApplyDate(MediaQuery query, string value)
    {
        var mode = ' ';
        var text = value;
        if (text.StartsWith('>') || text.StartsWith('<'))
        {
            mode = text[0];
            text = text[1..].Trim();
        }

        if (!DateTime.TryParseExact(
                text,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var day))
        {
            return;
        }

        switch (mode)
        {
            case '>':
                query.DateAfter = day;
                break;
            case '<':
                query.DateBefore = day;
                break;
            default:
                query.DateExact = day;
                break;
        }
    }

    public static bool TryParseManualTag(string? raw, out string name, out string category)
    {
        name = string.Empty;
        category = "general";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var query = Parse(raw.Trim());
        if (query.Tags.Count > 0)
        {
            var tag = query.Tags[0];
            name = tag.Name;
            category = string.IsNullOrWhiteSpace(tag.Category) || tag.Category == "tag" ? "general" : tag.Category;
            return name.Length > 0;
        }

        name = raw.Replace('_', ' ').Trim().ToLowerInvariant();
        return name.Length > 0;
    }

    public static (string Needle, string? Category) ManualTagNeedle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (string.Empty, null);
        }

        var text = raw.Trim();
        var idx = text.IndexOf(':');
        if (idx <= 0)
        {
            return (text.Replace('_', ' ').Trim(), null);
        }

        var prefix = text[..idx].TrimStart('-').ToLowerInvariant();
        var value = text[(idx + 1)..].Trim().Trim('"').Replace('_', ' ');
        return prefix is "character" or "copyright" or "rating" or "artist" or "meta" or "tag"
            ? (value, prefix == "tag" ? null : prefix)
            : (text, null);
    }
}
