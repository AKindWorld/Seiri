namespace Seiri.Core.Models;

public sealed class MediaQuery
{
    public RailSection Section { get; set; } = RailSection.All;
    public string? LibraryRoot { get; set; }
    public MediaKindFilter Kind { get; set; } = MediaKindFilter.All;
    public TaggedFilter Tagged { get; set; } = TaggedFilter.All;
    public SortKey Sort { get; set; } = SortKey.DateTaken;
    public SortDir Direction { get; set; } = SortDir.Desc;
    public string? Text { get; set; }
    public List<TagClause> Tags { get; set; } = [];
    public HashSet<string> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Raw { get; set; }

    public MediaQuery Clone() => new()
    {
        Section = Section,
        LibraryRoot = LibraryRoot,
        Kind = Kind,
        Tagged = Tagged,
        Sort = Sort,
        Direction = Direction,
        Text = Text,
        Tags = [.. Tags],
        Extensions = new HashSet<string>(Extensions, StringComparer.OrdinalIgnoreCase),
        Raw = Raw
    };
}
