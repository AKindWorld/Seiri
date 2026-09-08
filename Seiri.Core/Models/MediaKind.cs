namespace Seiri.Core.Models;

public enum MediaKind
{
    Image,
    Video
}

public enum GalleryLayoutMode
{
    Grid,
    Masonry,
    River
}

public enum LayoutDensity
{
    Small,
    Medium,
    Large
}

public enum SortKey
{
    DateTaken,
    DateAdded,
    DateModified,
    Name,
    Size,
    Type,
    TagCount
}

public enum SortDir
{
    Desc,
    Asc
}

public enum MediaKindFilter
{
    All,
    Images,
    Videos
}

public enum TaggedFilter
{
    All,
    Tagged,
    Untagged,
    Failed
}

public enum RailSection
{
    All,
    Favorites,
    Tagging,
    Directory,
    Settings
}
