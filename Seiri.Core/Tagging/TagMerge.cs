using Seiri.Core.Models;

namespace Seiri.Core.Tagging;

public static class TagMerge
{
    public static TagResult UnionMax(IReadOnlyList<TagResult> results, int maxTags)
    {
        if (results.Count == 0)
        {
            return new TagResult { ModelId = "merge" };
        }

        if (results.Count == 1)
        {
            return Cap(results[0], maxTags);
        }

        RatingScore? rating = null;
        var tags = new Dictionary<string, ScoredTag>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (result.Rating is not null && (rating is null || result.Rating.Score > rating.Score))
            {
                rating = result.Rating;
            }

            foreach (var tag in result.General.Concat(result.Character).Concat(result.Other))
            {
                if (!tags.TryGetValue(tag.Name, out var existing) || tag.Score > existing.Score)
                {
                    tags[tag.Name] = tag;
                }
            }
        }

        var merged = new TagResult
        {
            ModelId = string.Join('+', results.Select(r => r.ModelId)),
            Rating = rating
        };

        foreach (var tag in tags.Values)
        {
            switch (tag.Category)
            {
                case "character":
                    merged.Character.Add(tag);
                    break;
                case "rating":
                    break;
                case "other":
                    merged.Other.Add(tag);
                    break;
                default:
                    merged.General.Add(tag);
                    break;
            }
        }

        return Cap(merged, maxTags);
    }

    public static TagResult Cap(TagResult result, int maxTags)
    {
        maxTags = Math.Clamp(maxTags, 1, 128);
        var keep = result.General
            .Concat(result.Character)
            .Concat(result.Other)
            .OrderByDescending(t => t.Score)
            .Take(maxTags)
            .ToList();

        return new TagResult
        {
            ModelId = result.ModelId,
            Rating = result.Rating,
            General = keep.Where(t => t.Category == "general").ToList(),
            Character = keep.Where(t => t.Category == "character").ToList(),
            Other = keep.Where(t => t.Category is not ("general" or "character" or "rating")).ToList()
        };
    }
}
