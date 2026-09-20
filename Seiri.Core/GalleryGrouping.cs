using Seiri.Core.Models;

namespace Seiri.Core;

public readonly record struct GroupBucket(string Label, long Sort);

public readonly record struct IndexMark(string TrackLabel, string Caption, double Y, bool Major);

public static class GalleryGrouping
{
    public static List<GalleryEntry> Build(IReadOnlyList<MediaItem> items, GroupKey key, bool insertHeaders = true)
    {
        if (items.Count == 0)
        {
            return [];
        }

        if (key == GroupKey.None)
        {
            var flat = new List<GalleryEntry>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                flat.Add(GalleryEntry.ForMedia(items[i]));
            }

            return flat;
        }

        var grouped = items
            .Select(i => (Item: i, Bucket: Bucket(i, key)))
            .GroupBy(x => x.Bucket);
        var desc = key is GroupKey.DateTaken or GroupKey.DateAdded or GroupKey.DateModified or GroupKey.Size or GroupKey.Tags;
        var ordered = desc
            ? grouped.OrderByDescending(g => g.Key.Sort)
            : grouped.OrderBy(g => g.Key.Sort);

        var list = new List<GalleryEntry>(items.Count + 32);
        foreach (var group in ordered)
        {
            if (insertHeaders)
            {
                list.Add(GalleryEntry.ForHeader(group.Key.Label));
            }

            foreach (var row in group)
            {
                list.Add(GalleryEntry.ForMedia(row.Item, group.Key.Label));
            }
        }

        return list;
    }

    public static string LabelAt(IReadOnlyList<GalleryEntry> entries, IReadOnlyList<LayoutRect> rects, double offset)
    {
        var count = Math.Min(entries.Count, rects.Count);
        var label = string.Empty;
        for (var i = 0; i < count; i++)
        {
            if (rects[i].Y > offset + 8)
            {
                break;
            }

            var entry = entries[i];
            if (entry.IsHeader)
            {
                label = entry.Header;
            }
            else if (!string.IsNullOrEmpty(entry.GroupLabel))
            {
                label = entry.GroupLabel;
            }
        }

        if (label.Length == 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].GroupLabel) || entries[i].IsHeader)
                {
                    return entries[i].IsHeader ? entries[i].Header : entries[i].GroupLabel;
                }
            }
        }

        return label;
    }

    public static GroupBucket Bucket(MediaItem item, GroupKey key) => key switch
    {
        GroupKey.DateAdded => DateBucket(item.AddedAt),
        GroupKey.DateModified => DateBucket(item.MtimeUtc),
        GroupKey.Name => LetterBucket(item.FileName),
        GroupKey.Type => item.Kind == MediaKind.Video
            ? new GroupBucket("Videos", 1)
            : new GroupBucket("Photos", 0),
        GroupKey.Size => SizeBucket(item.ByteSize),
        GroupKey.Tags => TagBucket(item.TagCount),
        GroupKey.Dimensions => DimensionBucket(item),
        _ => DateBucket(item.SortDate)
    };

    public static IndexMark[] Marks(IReadOnlyList<GalleryEntry> entries, IReadOnlyList<LayoutRect> rects)
    {
        var count = Math.Min(entries.Count, rects.Count);
        var marks = new List<IndexMark>();
        string? lastTrack = null;
        for (var i = 0; i < count; i++)
        {
            if (!entries[i].IsHeader)
            {
                continue;
            }

            var header = entries[i].Header;
            var (track, caption, yearLike) = CaptionOf(header);
            var major = !yearLike || track != lastTrack;
            if (major)
            {
                lastTrack = track;
            }

            marks.Add(new IndexMark(major ? track : string.Empty, caption, rects[i].Y, major));
        }

        return [.. marks];
    }

    public static double YAt(IndexMark[] marks, double normalized, double extent)
    {
        if (marks.Length == 0 || extent <= 0)
        {
            return 0;
        }

        var y = Math.Clamp(normalized, 0, 1) * extent;
        return y;
    }

    private static GroupBucket DateBucket(DateTimeOffset value)
    {
        var local = value.ToLocalTime().Date;
        return new GroupBucket(local.ToString("MMMM d, yyyy"), local.Ticks);
    }

    private static GroupBucket LetterBucket(string fileName)
    {
        var ch = fileName.TrimStart('.', ' ').FirstOrDefault();
        if (ch == 0)
        {
            return new GroupBucket("#", 0);
        }

        ch = char.ToUpperInvariant(ch);
        if (ch is < 'A' or > 'Z')
        {
            return new GroupBucket("#", 0);
        }

        return new GroupBucket(ch.ToString(), ch);
    }

    private static GroupBucket SizeBucket(long bytes) => bytes switch
    {
        < 100 * 1024 => new GroupBucket("Small (under 100 KB)", 0),
        < 1024 * 1024 => new GroupBucket("Medium (100 KB – 1 MB)", 1),
        < 10 * 1024 * 1024 => new GroupBucket("Large (1 – 10 MB)", 2),
        _ => new GroupBucket("Huge (over 10 MB)", 3)
    };

    private static GroupBucket TagBucket(int count) => count switch
    {
        <= 0 => new GroupBucket("Untagged", 0),
        <= 10 => new GroupBucket("1–10 tags", 1),
        <= 50 => new GroupBucket("11–50 tags", 2),
        _ => new GroupBucket("50+ tags", 3)
    };

    private static GroupBucket DimensionBucket(MediaItem item)
    {
        var w = item.Width ?? 0;
        var h = item.Height ?? 0;
        if (w <= 0 || h <= 0)
        {
            return new GroupBucket("Unknown size", 0);
        }

        if (Math.Abs(w - h) < Math.Max(w, h) * 0.05)
        {
            return new GroupBucket("Square", 1);
        }

        return w > h ? new GroupBucket("Landscape", 2) : new GroupBucket("Portrait", 3);
    }

    private static (string Track, string Caption, bool YearLike) CaptionOf(string header)
    {
        if (DateTime.TryParse(header, out var day))
        {
            return (day.Year.ToString(), day.ToString("MMM yyyy"), true);
        }

        return (header, header, false);
    }
}
