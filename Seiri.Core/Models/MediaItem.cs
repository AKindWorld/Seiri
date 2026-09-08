namespace Seiri.Core.Models;

public sealed class MediaItem
{
    public long Id { get; init; }
    public string LibraryRoot { get; init; } = string.Empty;
    public string RelPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Ext { get; init; } = string.Empty;
    public MediaKind Kind { get; init; }
    public long ByteSize { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public long? DurationMs { get; init; }
    public string? ContentHash { get; init; }
    public DateTimeOffset MtimeUtc { get; init; }
    public DateTimeOffset? TakenAt { get; init; }
    public DateTimeOffset AddedAt { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsMissing { get; init; }
    public string? SidecarRel { get; init; }
    public int TagCount { get; init; }
    public DateTimeOffset? TaggedAt { get; init; }
    public string? ThumbRel { get; init; }
    public string? Rating { get; init; }
    public string? TagError { get; init; }

    public string FullPath => GeneratedLayout.ToFullPath(LibraryRoot, RelPath);

    public string? ThumbFullPath =>
        string.IsNullOrEmpty(ThumbRel) ? null : GeneratedLayout.ToFullPath(LibraryRoot, ThumbRel);

    public DateTimeOffset SortDate => TakenAt ?? MtimeUtc;
}
