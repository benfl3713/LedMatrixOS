using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// Shared plumbing for the chart nodes: where the values come from and how they map to a vertical range.
/// Values are read in place every frame, so a source that returns the same list until its data changes costs nothing.
/// </summary>
public abstract class ChartBase : Node
{
    private static readonly IReadOnlyList<float> NoValues = [];

    protected ChartBase()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    /// <summary>Evaluated every frame when set, instead of <see cref="Values"/>. Return a cached list; do not build one per call.</summary>
    public Func<IReadOnlyList<float>>? Source { get; set; }

    public IReadOnlyList<float> Values { get; set; } = NoValues;

    /// <summary>Bottom of the value range; null uses 0 for bars and the smallest value for lines.</summary>
    public float? Min { get; set; }

    /// <summary>Top of the value range; null uses the largest value.</summary>
    public float? Max { get; set; }

    public int NaturalHeight { get; set; } = 12;

    protected IReadOnlyList<float> Current => Source?.Invoke() ?? Values;

    protected override Size MeasureCore(int availW, int availH) => new(Padding.Horizontal, NaturalHeight + Padding.Vertical);

    protected (float lo, float hi) RangeOf(IReadOnlyList<float> values, bool zeroBased)
    {
        float lo = zeroBased ? 0f : float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < values.Count; i++)
        {
            float v = values[i];
            if (!zeroBased && v < lo) lo = v;
            if (v > hi) hi = v;
        }

        if (values.Count == 0) return (0f, 1f);
        lo = Min ?? lo;
        hi = Max ?? hi;
        if (hi - lo < 0.0001f) hi = lo + 1f;
        return (lo, hi);
    }
}

/// <summary>
/// A line chart across the node's width, optionally filled underneath, with a dot on the latest value.
/// Samples are interpolated to the pixel width, so any number of values fits.
/// </summary>
public sealed class Sparkline : ChartBase
{
    public Pixel Line { get; set; } = new(0, 200, 255);

    /// <summary>Fills the area under the line at this fraction of <see cref="Line"/>'s brightness; 0 turns the fill off.</summary>
    public float FillBrightness { get; set; } = 0.25f;

    public bool ShowLatest { get; set; } = true;

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var values = Current;
        var area = ContentOf(bounds);
        if (values.Count == 0 || area.Width <= 0 || area.Height <= 0) return;

        var (lo, hi) = RangeOf(values, zeroBased: false);
        int bottom = area.Bottom - 1;
        var fill = Line.WithBrightness(FillBrightness);
        int prevY = 0;

        for (int i = 0; i < area.Width; i++)
        {
            float v = Sample(values, i, area.Width);
            int y = bottom - (int)MathF.Round((Math.Clamp(v, lo, hi) - lo) / (hi - lo) * (area.Height - 1));
            int x = area.X + i;

            if (FillBrightness > 0f && y < bottom) frame.Fill(new Rectangle(x, y + 1, 1, bottom - y), fill);
            if (i == 0) frame.SetPixel(x, y, Line);
            else frame.DrawLine(x - 1, prevY, x, y, Line);
            prevY = y;
        }

        if (ShowLatest) frame.Fill(new Rectangle(area.Right - 2, Math.Max(area.Y, prevY - 1), 2, 3), Pixel.White);
    }

    private static float Sample(IReadOnlyList<float> values, int column, int width)
    {
        if (values.Count == 1 || width <= 1) return values[Math.Min(column, values.Count - 1)];
        float position = column * (values.Count - 1f) / (width - 1f);
        int i = Math.Min((int)position, values.Count - 2);
        float t = position - i;
        return values[i] + (values[i + 1] - values[i]) * t;
    }
}

/// <summary>
/// Vertical bars, one per value, sharing the width equally. <see cref="ColorOf"/> can colour each bar by its index and value,
/// and <see cref="HighlightIndex"/> marks one (the current half-hour, say).
/// </summary>
public sealed class BarChart : ChartBase
{
    public Pixel Color { get; set; } = new(0, 200, 90);
    public Pixel HighlightColor { get; set; } = Pixel.White;

    /// <summary>Index of the bar drawn in <see cref="HighlightColor"/>, or -1.</summary>
    public int HighlightIndex { get; set; } = -1;

    /// <summary>Picks a bar's colour from its index and value. Use a cached delegate; the result is ignored for the highlighted bar.</summary>
    public Func<int, float, Pixel>? ColorOf { get; set; }

    /// <summary>Pixels between bars; shrinks automatically when there are too many bars to fit.</summary>
    public int Gap { get; set; } = 1;

    /// <summary>Draws a dim 1px line along the bottom.</summary>
    public bool Baseline { get; set; }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var values = Current;
        var area = ContentOf(bounds);
        int n = values.Count;
        if (n == 0 || area.Width <= 0 || area.Height <= 0) return;

        var (lo, hi) = RangeOf(values, zeroBased: true);
        int gap = Math.Min(Gap, (area.Width - n) / Math.Max(1, n - 1));
        gap = Math.Max(0, gap);
        int barWidth = Math.Max(1, (area.Width - gap * (n - 1)) / n);
        int used = barWidth * n + gap * (n - 1);
        int x0 = area.X + (area.Width - used) / 2;
        int bottom = area.Bottom;

        if (Baseline) frame.Fill(new Rectangle(area.X, bottom - 1, area.Width, 1), new Pixel(50, 50, 50));

        for (int i = 0; i < n; i++)
        {
            float v = values[i];
            int h = (int)MathF.Round((Math.Clamp(v, lo, hi) - lo) / (hi - lo) * area.Height);
            if (h <= 0 && v > lo) h = 1;
            if (h <= 0) continue;

            var color = i == HighlightIndex ? HighlightColor : ColorOf?.Invoke(i, v) ?? Color;
            frame.Fill(new Rectangle(x0 + i * (barWidth + gap), bottom - h, barWidth, h), color);
        }
    }
}
