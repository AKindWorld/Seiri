using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Seiri.Core.Models;
using Windows.Foundation;

namespace Seiri.Layouts;

public sealed class JustifiedLayout : VirtualizingLayout
{
    public static readonly DependencyProperty DesiredRowHeightProperty = DependencyProperty.Register(
        nameof(DesiredRowHeight),
        typeof(double),
        typeof(JustifiedLayout),
        new PropertyMetadata(200d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(JustifiedLayout),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

    public double DesiredRowHeight
    {
        get => (double)GetValue(DesiredRowHeightProperty);
        set => SetValue(DesiredRowHeightProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public IReadOnlyList<MediaItem>? Items { get; set; }

    private int _generation;

    public void Refresh()
    {
        _generation++;
        InvalidateMeasure();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JustifiedLayout layout)
        {
            layout.InvalidateMeasure();
        }
    }

    protected override void InitializeForContextCore(VirtualizingLayoutContext context) =>
        context.LayoutState = new State();

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context) =>
        context.LayoutState = null;

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var state = (State)context.LayoutState;
        var width = availableSize.Width;
        if (double.IsInfinity(width) || width <= 0)
        {
            return new Size(0, 0);
        }

        var count = context.ItemCount;
        if (state.LastWidth != width || state.Rects.Length != count || state.Generation != _generation)
        {
            state.LastWidth = width;
            state.Generation = _generation;
            state.Rects = BuildRects(count, width);
        }

        var visible = context.RealizationRect;
        for (var i = 0; i < count; i++)
        {
            var rect = state.Rects[i];
            if (!Intersects(rect, visible))
            {
                continue;
            }

            var element = context.GetOrCreateElementAt(i);
            element.Measure(new Size(rect.Width, rect.Height));
        }

        return new Size(width, state.Rects.Length == 0 ? 0 : Bottom(state.Rects));
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        var state = (State)context.LayoutState;
        var visible = context.RealizationRect;
        var count = Math.Min(context.ItemCount, state.Rects.Length);
        for (var i = 0; i < count; i++)
        {
            var rect = state.Rects[i];
            if (!Intersects(rect, visible))
            {
                continue;
            }

            var element = context.GetOrCreateElementAt(i);
            element.Arrange(rect);
        }

        return finalSize;
    }

    private Rect[] BuildRects(int count, double availableWidth)
    {
        var rects = new Rect[count];
        if (count == 0)
        {
            return rects;
        }

        var height = Math.Max(80, DesiredRowHeight);
        var gap = Spacing;
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

            var gaps = gap * (n - 1);
            var scale = stretch && rowNatural > 0
                ? (availableWidth - gaps) / rowNatural
                : 1.0;
            var rowHeight = height * scale;
            var x = 0.0;
            for (var i = rowStart; i < rowEnd; i++)
            {
                var w = NaturalWidth(i, height) * scale;
                rects[i] = new Rect(x, y, w, rowHeight);
                x += w + gap;
            }

            y += rowHeight + gap;
        }

        for (var i = 0; i < count; i++)
        {
            var w = NaturalWidth(i, height);
            if (i > rowStart && rowNatural + gap + w > availableWidth)
            {
                Flush(i, stretch: true);
                rowStart = i;
                rowNatural = 0;
            }

            rowNatural += w;
        }

        var lastWidth = rowNatural + gap * Math.Max(0, count - rowStart - 1);
        Flush(count, stretch: lastWidth > availableWidth * 0.65);
        return rects;
    }

    private double NaturalWidth(int index, double rowHeight)
    {
        var aspect = 1.0;
        if (Items is not null && index >= 0 && index < Items.Count)
        {
            var item = Items[index];
            var w = item.Width ?? 0;
            var h = item.Height ?? 0;
            if (w > 0 && h > 0)
            {
                aspect = w / (double)h;
            }
        }

        return Math.Max(40, rowHeight * aspect);
    }

    private static bool Intersects(Rect a, Rect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    private static double Bottom(Rect[] rects)
    {
        var max = 0.0;
        foreach (var rect in rects)
        {
            max = Math.Max(max, rect.Bottom);
        }

        return max;
    }

    private sealed class State
    {
        public double LastWidth;
        public int Generation;
        public Rect[] Rects = [];
    }
}
