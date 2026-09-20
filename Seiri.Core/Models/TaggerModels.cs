namespace Seiri.Core.Models;

public sealed class TaggerImage
{
    public required byte[] Bgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}

public sealed class TaggerOptions
{
    public float GeneralThreshold { get; init; } = 0.35f;
    public float CharacterThreshold { get; init; } = 0.85f;
    public int MaxTags { get; init; } = 128;
}

public sealed class ScoredTag
{
    public required string Name { get; init; }
    public string Category { get; init; } = "general";
    public float Score { get; init; }
    public string? ModelId { get; init; }
}

public sealed class RatingScore
{
    public required string Name { get; init; }
    public float Score { get; init; }
    public string? ModelId { get; init; }
}

public sealed class TagResult
{
    public required string ModelId { get; init; }
    public RatingScore? Rating { get; init; }
    public List<ScoredTag> General { get; init; } = [];
    public List<ScoredTag> Character { get; init; } = [];
    public List<ScoredTag> Other { get; init; } = [];

    public IEnumerable<ScoredTag> AllScored()
    {
        if (Rating is not null)
        {
            yield return new ScoredTag
            {
                Name = Rating.Name,
                Category = "rating",
                Score = Rating.Score,
                ModelId = Rating.ModelId ?? ModelId
            };
        }

        foreach (var tag in General)
        {
            yield return tag;
        }

        foreach (var tag in Character)
        {
            yield return tag;
        }

        foreach (var tag in Other)
        {
            yield return tag;
        }
    }

    public IReadOnlyList<string> Names()
    {
        var names = new List<string>();
        if (Rating is not null)
        {
            names.Add(Rating.Name);
        }

        foreach (var tag in General.Concat(Character).Concat(Other))
        {
            if (!names.Contains(tag.Name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(tag.Name);
            }
        }

        return names;
    }
}

public sealed class ModelTag
{
    public int Index { get; init; }
    public int TagId { get; init; }
    public required string Name { get; init; }
    public int Category { get; init; }
    public int Count { get; init; }

    public string CategoryName => Category switch
    {
        9 => "rating",
        4 => "character",
        3 => "copyright",
        1 => "artist",
        5 => "meta",
        0 => "general",
        _ => "other"
    };
}

public sealed class ModelFile
{
    public string Name { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
}

public sealed class ModelCatalogEntry
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string License { get; set; } = "Apache-2.0";
    public string Description { get; set; } = string.Empty;
    public string Preprocess { get; set; } = "WdV3";
    public long SizeBytes { get; set; }
    public List<ModelFile> Files { get; set; } = [];
    public float BalancedGeneral { get; set; } = 0.35f;
    public float BalancedCharacter { get; set; } = 0.85f;
    public float PreciseGeneral { get; set; } = 0.53f;
    public float PreciseCharacter { get; set; } = 0.85f;
    public float RecallGeneral { get; set; } = 0.25f;
    public float RecallCharacter { get; set; } = 0.70f;

    public (float General, float Character) Thresholds(ThresholdPreset preset) => preset switch
    {
        ThresholdPreset.Precise => (PreciseGeneral, PreciseCharacter),
        ThresholdPreset.Recall => (RecallGeneral, RecallCharacter),
        _ => (BalancedGeneral, BalancedCharacter)
    };
}

public sealed class ModelCatalogFile
{
    public List<ModelCatalogEntry> Models { get; set; } = [];
}

public enum ThresholdPreset
{
    Balanced,
    Precise,
    Recall
}

public enum MergeStrategy
{
    UnionMax,
    UnionAverage,
    Intersection
}

public sealed class ModelDownloadProgress
{
    public required string FileName { get; init; }
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }
    public double Percent => TotalBytes is > 0 ? 100.0 * BytesReceived / TotalBytes.Value : 0;
}

public sealed class TaggingProgress
{
    public int Done { get; init; }
    public int Total { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
    public int Tagged { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public string Phase { get; init; } = "tagging";
    public double Percent => Total > 0 ? 100.0 * Done / Total : 0;

    public bool ShouldPublishUi(ref long lastTickMs, int minIntervalMs = 250)
    {
        if (Phase is "done" or "loading" || Done >= Total)
        {
            lastTickMs = Environment.TickCount64;
            return true;
        }

        var now = Environment.TickCount64;
        if (now - lastTickMs < minIntervalMs)
        {
            return false;
        }

        lastTickMs = now;
        return true;
    }
}

public sealed class TaggingRunResult
{
    public int Tagged { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int Total { get; init; }
}
