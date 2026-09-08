using Seiri.Core.Models;

namespace Seiri.Core.Tagging;

public static class TagCsvParser
{
    public static IReadOnlyList<ModelTag> Parse(string csv)
    {
        using var reader = new StringReader(csv);
        var header = reader.ReadLine();
        if (header is null)
        {
            return [];
        }

        var cols = Split(header);
        var nameIdx = IndexOf(cols, "name");
        var catIdx = IndexOf(cols, "category");
        var countIdx = IndexOf(cols, "count");
        var tagIdIdx = IndexOf(cols, "tag_id");
        if (nameIdx < 0)
        {
            throw new InvalidDataException("selected_tags.csv is missing a name column.");
        }

        var list = new List<ModelTag>();
        var index = 0;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = Split(line);
            var rawName = Get(parts, nameIdx);
            if (string.IsNullOrWhiteSpace(rawName))
            {
                index++;
                continue;
            }

            list.Add(new ModelTag
            {
                Index = index,
                TagId = tagIdIdx >= 0 ? ParseInt(Get(parts, tagIdIdx)) : index,
                Name = TagDisplay.ToCanonical(rawName),
                Category = catIdx >= 0 ? ParseInt(Get(parts, catIdx)) : 0,
                Count = countIdx >= 0 ? ParseInt(Get(parts, countIdx)) : 0
            });
            index++;
        }

        return list;
    }

    public static TagResult ToResult(string modelId, ReadOnlySpan<float> scores, IReadOnlyList<ModelTag> tags, TaggerOptions options)
    {
        var general = new List<ScoredTag>();
        var character = new List<ScoredTag>();
        var other = new List<ScoredTag>();
        RatingScore? rating = null;
        foreach (var tag in tags)
        {
            if ((uint)tag.Index >= (uint)scores.Length)
            {
                continue;
            }

            var score = scores[tag.Index];
            if (tag.Category == 9)
            {
                if (rating is null || score > rating.Score)
                {
                    rating = new RatingScore { Name = tag.Name, Score = score, ModelId = modelId };
                }

                continue;
            }

            var threshold = tag.Category == 4 ? options.CharacterThreshold : options.GeneralThreshold;
            if (score < threshold)
            {
                continue;
            }

            var scored = new ScoredTag
            {
                Name = tag.Name,
                Category = tag.CategoryName,
                Score = score,
                ModelId = modelId
            };

            if (tag.Category == 4)
            {
                character.Add(scored);
            }
            else if (tag.Category == 0)
            {
                general.Add(scored);
            }
            else
            {
                other.Add(scored);
            }
        }

        return TagMerge.Cap(new TagResult
        {
            ModelId = modelId,
            Rating = rating,
            General = general,
            Character = character,
            Other = other
        }, options.MaxTags);
    }

    private static string[] Split(string line)
    {
        var parts = new List<string>();
        var start = 0;
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                parts.Add(Unquote(line[start..i]));
                start = i + 1;
            }
        }

        parts.Add(Unquote(line[start..]));
        return [.. parts];
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\"\"", "\"");
        }

        return value;
    }

    private static int IndexOf(IReadOnlyList<string> cols, string name)
    {
        for (var i = 0; i < cols.Count; i++)
        {
            if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Get(IReadOnlyList<string> parts, int index) =>
        index >= 0 && index < parts.Count ? parts[index] : string.Empty;

    private static int ParseInt(string value) => int.TryParse(value, out var n) ? n : 0;
}
