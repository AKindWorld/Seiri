namespace Seiri.Core.Models;

public sealed class TagRecord
{
    public required string Name { get; init; }
    public string Category { get; init; } = "general";
    public int UseCount { get; init; }
}

public sealed class TagClause
{
    public required string Name { get; init; }
    public bool Exclude { get; init; }
}

public sealed class SearchSuggestion
{
    public required string Kind { get; init; }
    public required string Label { get; init; }
    public required string Detail { get; init; }
    public required string ApplyText { get; init; }

    public override string ToString() => Label;
}
