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
    public string? Folder { get; set; }
    public List<string> Folders { get; set; } = [];
    public DateField DateField { get; set; } = DateField.Taken;
    public DateTime? DateExact { get; set; }
    public DateTime? DateAfter { get; set; }
    public DateTime? DateBefore { get; set; }
    public HashSet<string> Orientations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Aspects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
        Tags = [.. Tags.Select(t => new TagClause
        {
            Name = t.Name,
            Exclude = t.Exclude,
            Category = t.Category,
            Aliases = t.Aliases.Count == 0 ? [] : [.. t.Aliases]
        })],
        Extensions = new HashSet<string>(Extensions, StringComparer.OrdinalIgnoreCase),
        Folder = Folder,
        Folders = [.. Folders],
        DateField = DateField,
        DateExact = DateExact,
        DateAfter = DateAfter,
        DateBefore = DateBefore,
        Orientations = new HashSet<string>(Orientations, StringComparer.OrdinalIgnoreCase),
        Aspects = new HashSet<string>(Aspects, StringComparer.OrdinalIgnoreCase),
        Colors = new HashSet<string>(Colors, StringComparer.OrdinalIgnoreCase),
        Raw = Raw
    };
}
