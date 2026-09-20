using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Seiri.Core;
using Seiri.Core.Models;
using Windows.Foundation;

namespace Seiri.Layouts;

/// <summary>
/// Sizes every cell from media metadata, realizes the viewport, and recycles
/// the rest. Skipping RecycleElement on a 50k repeater is a native WinUI death
/// with no managed exception.
/// </summary>
public sealed class GalleryVirtualizingLayout : VirtualizingLayout
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(GalleryLayoutMode), typeof(GalleryVirtualizingLayout),
        new PropertyMetadata(GalleryLayoutMode.Grid, OnLayoutPropertyChanged));

    public static readonly DependencyProperty TileSizeProperty = DependencyProperty.Register(
        nameof(TileSize), typeof(double), typeof(GalleryVirtualizingLayout),
        new PropertyMetadata(220d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(GalleryVirtualizingLayout),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MasonryColumnWidthProperty = DependencyProperty.Register(
        nameof(MasonryColumnWidth), typeof(double), typeof(GalleryVirtualizingLayout),
        new PropertyMetadata(220d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty RiverRowHeightProperty = DependencyProperty.Register(
        nameof(RiverRowHeight), typeof(double), typeof(GalleryVirtualizingLayout),
        new PropertyMetadata(200d, OnLayoutPropertyChanged));

    public GalleryLayoutMode Mode
    {
        get => (GalleryLayoutMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double MasonryColumnWidth
    {
        get => (double)GetValue(MasonryColumnWidthProperty);
        set => SetValue(MasonryColumnWidthProperty, value);
    }

    public double RiverRowHeight
    {
        get => (double)GetValue(RiverRowHeightProperty);
        set => SetValue(RiverRowHeightProperty, value);
    }

    public IReadOnlyList<GalleryEntry>? Entries { get; set; }
    internal LayoutRect[] LastRects { get; private set; } = [];
    internal double LastExtentHeight { get; private set; }

    private int _generation;

    public void Refresh()
    {
        _generation++;
        InvalidateMeasure();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GalleryVirtualizingLayout layout)
        {
            layout.InvalidateMeasure();
        }
    }

    protected override void InitializeForContextCore(VirtualizingLayoutContext context) =>
        context.LayoutState = new State();

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        if (context.LayoutState is State state)
        {
            RecycleAll(context, state);
        }

        context.LayoutState = null;
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        try
        {
            var state = context.LayoutState as State;
            if (state is null)
            {
                return new Size(0, 0);
            }

            var width = availableSize.Width;
            if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0)
            {
                // ScrollView may measure unconstrained. A 0×0 return leaves
                // the gallery blank even after the viewport has a real size.
                width = state.LastWidth > 1 ? state.LastWidth : 960;
            }

            var count = context.ItemCount;
            var version = Entries is ResettableCollection<GalleryEntry> reset ? reset.Version : _generation;
            if (state.LastWidth != width
                || state.Count != count
                || state.Generation != _generation
                || state.Version != version
                || state.Mode != Mode
                || state.TileSize != TileSize
                || state.Masonry != MasonryColumnWidth
                || state.River != RiverRowHeight)
            {
                if (state.Count != count || state.Version != version)
                {
                    RecycleAll(context, state);
                }

                var source = Entries;
                if (source is null || source.Count != count)
                {
                    source = Snapshot(context, count);
                }

                var built = GalleryLayoutMath.Build(
                    source, Mode, width, TileSize, Spacing, MasonryColumnWidth, RiverRowHeight);
                state.LastWidth = width;
                state.Count = count;
                state.Generation = _generation;
                state.Version = version;
                state.Mode = Mode;
                state.TileSize = TileSize;
                state.Masonry = MasonryColumnWidth;
                state.River = RiverRowHeight;
                state.Rects = built.Rects;
                state.Height = built.Height;
                LastRects = built.Rects;
                LastExtentHeight = built.Height;
            }

            Realize(context, state, width, measure: true);
            return new Size(width, state.Height);
        }
        catch (Exception ex)
        {
            AppLog.Error("gallery measure", ex);
            var fallbackWidth = double.IsFinite(availableSize.Width) && availableSize.Width > 0
                ? availableSize.Width
                : 0;
            return new Size(fallbackWidth, LastExtentHeight);
        }
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        try
        {
            var state = context.LayoutState as State;
            if (state is null)
            {
                return finalSize;
            }

            if (finalSize.Width <= 0)
            {
                return finalSize;
            }

            Realize(context, state, finalSize.Width, measure: false);
        }
        catch (Exception ex)
        {
            AppLog.Error("gallery arrange", ex);
        }

        return finalSize;
    }

    private static void Realize(VirtualizingLayoutContext context, State state, double width, bool measure)
    {
        var visible = context.RealizationRect;
        if (visible.Width <= 0 || double.IsInfinity(visible.Width) || double.IsNaN(visible.Width))
        {
            visible.X = 0;
            visible.Width = width;
        }

        if (visible.Height <= 0 || double.IsInfinity(visible.Height) || double.IsNaN(visible.Height))
        {
            if (state.LastVisible.Height > 0)
            {
                visible = state.LastVisible;
            }
            else
            {
                visible.Y = 0;
                visible.Height = 900;
            }
        }
        else
        {
            var pad = Math.Min(visible.Height * 0.5, 600);
            visible.Y -= pad;
            visible.Height += pad * 2;
            state.LastVisible = visible;
        }

        var keep = GalleryLayoutMath.CollectVisible(state.Rects, visible.Y, visible.Y + visible.Height);
        var count = Math.Min(context.ItemCount, state.Rects.Length);
        if (keep.Count > 0 && keep[^1] >= count)
        {
            keep.RemoveAll(i => i >= count);
        }

        RecycleNotIn(context, state, keep);

        foreach (var i in keep)
        {
            if (i < 0 || i >= count)
            {
                continue;
            }

            if (!state.Realized.TryGetValue(i, out var element))
            {
                element = context.GetOrCreateElementAt(
                    i,
                    ElementRealizationOptions.ForceCreate | ElementRealizationOptions.SuppressAutoRecycle);
                state.Realized[i] = element;
            }

            var rect = ToWinRect(state.Rects[i]);
            if (measure)
            {
                element.Measure(new Size(rect.Width, rect.Height));
            }
            else
            {
                element.Arrange(rect);
            }
        }
    }

    private static void RecycleNotIn(VirtualizingLayoutContext context, State state, List<int> keep)
    {
        if (state.Realized.Count == 0)
        {
            return;
        }

        HashSet<int>? set = null;
        List<int>? drop = null;
        foreach (var index in state.Realized.Keys)
        {
            set ??= [.. keep];
            if (set.Contains(index))
            {
                continue;
            }

            drop ??= [];
            drop.Add(index);
        }

        if (drop is null)
        {
            return;
        }

        foreach (var index in drop)
        {
            if (state.Realized.Remove(index, out var element))
            {
                try
                {
                    context.RecycleElement(element);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"recycle {index}", ex);
                }
            }
        }
    }

    private static void RecycleAll(VirtualizingLayoutContext context, State state)
    {
        foreach (var element in state.Realized.Values)
        {
            try
            {
                context.RecycleElement(element);
            }
            catch (Exception ex)
            {
                AppLog.Error("recycle all", ex);
            }
        }

        state.Realized.Clear();
    }

    private static IReadOnlyList<GalleryEntry> Snapshot(VirtualizingLayoutContext context, int count)
    {
        var list = new GalleryEntry[count];
        for (var i = 0; i < count; i++)
        {
            list[i] = context.GetItemAt(i) as GalleryEntry ?? GalleryEntry.ForHeader(string.Empty);
        }

        return list;
    }

    private static Rect ToWinRect(LayoutRect r) => new(r.X, r.Y, r.Width, r.Height);

    private sealed class State
    {
        public double LastWidth;
        public int Count;
        public int Generation;
        public int Version;
        public GalleryLayoutMode Mode;
        public double TileSize;
        public double Masonry;
        public double River;
        public LayoutRect[] Rects = [];
        public double Height;
        public Rect LastVisible;
        public Dictionary<int, UIElement> Realized { get; } = [];
    }
}
