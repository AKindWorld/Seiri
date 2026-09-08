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
        ("date:", "Filter by date taken (YYYY-MM-DD)"),
        ("folder:", "Filter by folder name")
    ];

    public static MediaQuery Parse(string? raw, MediaQuery? seed = null)
    {
        var query = seed?.Clone() ?? new MediaQuery();
        query.Raw = raw;
        query.Tags.Clear();
        query.Extensions.Clear();
        query.Text = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return query;
        }

        foreach (var token in Tokenize(raw))
        {
            var text = token;
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
                case "tag" or "character" or "copyright" or "artist" or "meta" or "rating" or null:
                    if (value.Length > 0)
                    {
                        query.Tags.Add(new TagClause { Name = value, Exclude = exclude });
                    }
                    break;
            }
        }

        return query;
    }

    public static string ToRaw(MediaQuery query)
    {
        var parts = new List<string>();
        foreach (var tag in query.Tags)
        {
            var body = tag.Name.Contains(' ') ? $"tag:\"{tag.Name}\"" : $"tag:{tag.Name.Replace(' ', '_')}";
            parts.Add(tag.Exclude ? "-" + body : body);
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

        return string.Join(' ', parts);
    }

    public static IReadOnlyList<SearchSuggestion> Suggest(string? raw, IReadOnlyList<TagRecord> catalog)
    {
        var text = raw ?? string.Empty;
        var suggestions = new List<SearchSuggestion>();
        var last = LastToken(text, out var before);

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

        if (value.Length > 0 || prefixName is "tag" or "character" or null)
        {
            var needle = value;
            var hits = catalog
                .Where(t => t.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.UseCount)
                .ThenBy(t => t.Name)
                .Take(12)
                .ToList();

            var exact = hits.FirstOrDefault(t => t.Name.Equals(needle, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                var include = Combine(before, FormatTagToken(exact.Name, exclude: false));
                var exclude = Combine(before, FormatTagToken(exact.Name, exclude: true));
                suggestions.Insert(0, new SearchSuggestion
                {
                    Kind = "action",
                    Label = $"Include tag:\"{exact.Name}\"",
                    Detail = $"{exact.UseCount:N0} files",
                    ApplyText = include
                });
                suggestions.Insert(1, new SearchSuggestion
                {
                    Kind = "action",
                    Label = $"Exclude tag:\"{exact.Name}\"",
                    Detail = "Must not have this tag",
                    ApplyText = exclude
                });
            }

            foreach (var hit in hits)
            {
                suggestions.Add(new SearchSuggestion
                {
                    Kind = "tag",
                    Label = hit.Name,
                    Detail = $"{hit.UseCount:N0}",
                    ApplyText = Combine(before, FormatTagToken(hit.Name, exclude: last.StartsWith('-')))
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

    public static string FormatTagToken(string name, bool exclude = false)
    {
        var body = name.Contains(' ') ? $"tag:\"{name}\"" : $"tag:{name.Replace(' ', '_')}";
        return exclude ? "-" + body : body;
    }

    private static string Combine(string before, string token) =>
        string.IsNullOrEmpty(before) ? token : before + token;
}
