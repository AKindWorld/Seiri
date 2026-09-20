using Seiri.Core.Models;

namespace Seiri.Core;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height);

public static class GalleryLayoutMath
{
    public const double HeaderHeight = 40;
    public const double MinTile = 40;
    public const double DefaultSpacing = 8;

    public static (LayoutRect[] Rects, double Height) Build(
        IReadOnlyList<GalleryEntry> entries,
        GalleryLayoutMode mode,
        double width,
        double tileSize,
        double spacing,
        double masonryColumnWidth,
        double riverRowHeight)
    {
        if (entries.Count == 0 || double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return ([], 0);
        }

        return mode switch
        {
            GalleryLayoutMode.Masonry => Masonry(entries, width, masonryColumnWidth, spacing),
            GalleryLayoutMode.River => River(entries, width, riverRowHeight, spacing),
            _ => Grid(entries, width, tileSize, spacing)
        };
    }

    public static int CountVisible(LayoutRect[] rects, double top, double bottom) =>
        CollectVisible(rects, top, bottom).Count;

    public static List<int> CollectVisible(LayoutRect[] rects, double top, double bottom)
    {
        var list = new List<int>();
        for (var i = 0; i < rects.Length; i++)
        {
            var r = rects[i];
            if (r.Y < bottom && r.Y + r.Height > top)
            {
                list.Add(i);
            }
        }

        return list;
    }

    private static (LayoutRect[] Rects, double Height) Grid(
        IReadOnlyList<GalleryEntry> entries,
        double width,
        double tileSize,
        double spacing)
    {
        var min = Math.Max(MinTile, tileSize);
        var columns = Math.Max(1, (int)Math.Floor((width + spacing) / (min + spacing)));
        var tile = (width - spacing * (columns - 1)) / columns;
        if (tile <= 0)
        {
            tile = min;
        }

        var rects = new LayoutRect[entries.Count];
        var col = 0;
        var y = 0.0;
        var bottom = 0.0;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.IsHeader)
            {
                if (col > 0)
                {
                    y += tile + spacing;
                    col = 0;
                }

                rects[i] = new LayoutRect(0, y, width, HeaderHeight);
                y += HeaderHeight + spacing;
                bottom = y;
                continue;
            }

            if (col >= columns)
            {
                col = 0;
                y += tile + spacing;
            }

            rects[i] = new LayoutRect(col * (tile + spacing), y, tile, tile);
            col++;
            bottom = y + tile;
        }

        return (rects, bottom);
    }

    private static (LayoutRect[] Rects, double Height) Masonry(
        IReadOnlyList<GalleryEntry> entries,
        double width,
        double columnWidth,
        double spacing)
    {
        var desired = Math.Max(MinTile, columnWidth);
        var columns = Math.Max(1, (int)Math.Floor((width + spacing) / (desired + spacing)));
        var colW = (width - spacing * (columns - 1)) / columns;
        if (colW <= 0)
        {
            colW = desired;
        }

        var heights = new double[columns];
        var rects = new LayoutRect[entries.Count];
        var yCursor = 0.0;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.IsHeader)
            {
                var row = 0.0;
                for (var c = 0; c < columns; c++)
                {
                    row = Math.Max(row, heights[c]);
                }

                if (row > 0)
                {
                    row += spacing;
                }

                rects[i] = new LayoutRect(0, row, width, HeaderHeight);
                yCursor = row + HeaderHeight + spacing;
                Array.Fill(heights, yCursor);
                continue;
            }

            var col = 0;
            var minH = heights[0];
            for (var c = 1; c < columns; c++)
            {
                if (heights[c] < minH)
                {
                    minH = heights[c];
                    col = c;
                }
            }

            var h = Math.Max(MinTile, colW * AspectHeightOverWidth(entry.Item));
            var x = col * (colW + spacing);
            rects[i] = new LayoutRect(x, heights[col], colW, h);
            heights[col] += h + spacing;
        }

        var bottom = 0.0;
        for (var c = 0; c < columns; c++)
        {
            bottom = Math.Max(bottom, heights[c]);
        }

        if (bottom > spacing)
        {
            bottom -= spacing;
        }

        return (rects, Math.Max(bottom, yCursor));
    }

    private static (LayoutRect[] Rects, double Height) River(
        IReadOnlyList<GalleryEntry> entries,
        double width,
        double rowHeight,
        double spacing)
    {
        var height = Math.Max(80, rowHeight);
        var rects = new LayoutRect[entries.Count];
        var rowStart = 0;
        var rowNatural = 0.0;
        var y = 0.0;

        void Flush(int rowEnd, bool stretch)
        {
            var n = rowEnd - rowStart;
            if (n <= 0)
            {
                return;
            }

            var gaps = spacing * (n - 1);
            var scale = stretch && rowNatural > 0 ? (width - gaps) / rowNatural : 1.0;
            var rowH = height * scale;
            var x = 0.0;
            for (var i = rowStart; i < rowEnd; i++)
            {
                var w = NaturalWidth(entries[i], height) * scale;
                rects[i] = new LayoutRect(x, y, w, rowH);
                x += w + spacing;
            }

            y += rowH + spacing;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].IsHeader)
            {
                if (i > rowStart)
                {
                    Flush(i, stretch: true);
                }

                rects[i] = new LayoutRect(0, y, width, HeaderHeight);
                y += HeaderHeight + spacing;
                rowStart = i + 1;
                rowNatural = 0;
                continue;
            }

            var w = NaturalWidth(entries[i], height);
            if (i > rowStart && rowNatural + spacing + w > width)
            {
                Flush(i, stretch: true);
                rowStart = i;
                rowNatural = 0;
            }

            rowNatural += w;
        }

        var lastGaps = spacing * Math.Max(0, entries.Count - rowStart - 1);
        var lastWidth = rowNatural + lastGaps;
        Flush(entries.Count, stretch: lastWidth > width * 0.65);

        var bottom = y > spacing ? y - spacing : y;
        return (rects, bottom);
    }

    private static double NaturalWidth(GalleryEntry entry, double rowHeight)
    {
        var aspect = AspectWidthOverHeight(entry.Item);
        return Math.Max(MinTile, rowHeight * aspect);
    }

    private static double AspectHeightOverWidth(MediaItem? item)
    {
        var w = item?.Width ?? 0;
        var h = item?.Height ?? 0;
        if (w <= 0 || h <= 0)
        {
            return 1;
        }

        return h / (double)w;
    }

    private static double AspectWidthOverHeight(MediaItem? item)
    {
        var w = item?.Width ?? 0;
        var h = item?.Height ?? 0;
        if (w <= 0 || h <= 0)
        {
            return 1;
        }

        return w / (double)h;
    }
}
