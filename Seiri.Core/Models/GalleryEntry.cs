namespace Seiri.Core.Models;

public sealed class GalleryEntry
{
    public bool IsHeader { get; init; }
    public string Header { get; init; } = string.Empty;
    public string GroupLabel { get; init; } = string.Empty;
    public MediaItem? Item { get; init; }

    public static GalleryEntry ForHeader(string header) =>
        new() { IsHeader = true, Header = header, GroupLabel = header };

    public static GalleryEntry ForMedia(MediaItem item, string groupLabel = "") =>
        new() { Item = item, GroupLabel = groupLabel };
}
