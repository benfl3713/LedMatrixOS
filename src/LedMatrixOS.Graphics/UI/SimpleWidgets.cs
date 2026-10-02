using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// Draws a <see cref="Sprite"/>. Multi-frame sprites (GIFs) play on a clock that advances with the frame time, looping.
/// </summary>
public sealed class Icon : Node
{
    private TimeSpan _elapsed;

    public Icon(Sprite? sprite = null) => Sprite = sprite;

    public Sprite? Sprite { get; set; }

    /// <summary>Pauses playback of a multi-frame sprite when false.</summary>
    public bool Playing { get; set; } = true;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (Playing) _elapsed += ctx.Delta;
    }

    protected override Size MeasureCore(int availW, int availH) =>
        new((Sprite?.Width ?? 0) + Padding.Horizontal, (Sprite?.Height ?? 0) + Padding.Vertical);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (Sprite is null) return;
        var content = ContentOf(bounds);
        frame.DrawSprite(Sprite, content.X, content.Y, _elapsed);
    }
}

/// <summary>
/// A horizontal bar filled to <see cref="Value"/> (0-1). It has no natural width, so give it a Width or let a Stack/Dock stretch it.
/// </summary>
public sealed class ProgressBar : Node
{
    public ProgressBar(float value = 0f)
    {
        Value = value;
        HAlign = Align.Stretch;
    }

    public ProgressBar(Func<float> source) : this() => ValueSource = source;

    public float Value { get; set; }

    /// <summary>Evaluated every frame when set, instead of <see cref="Value"/>.</summary>
    public Func<float>? ValueSource { get; set; }

    public Pixel Fill { get; set; } = new(0, 200, 90);
    public Pixel Background { get; set; } = new(25, 25, 25);
    public Pixel? Border { get; set; }
    public int Thickness { get; set; } = 4;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (ValueSource is { } source) Value = source();
    }

    protected override Size MeasureCore(int availW, int availH) => new(Padding.Horizontal, Thickness + Padding.Vertical);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds) =>
        frame.DrawProgressBar(ContentOf(bounds), Value, Fill, Background, Border);
}

/// <summary>
/// A rounded tile with centred text, such as a Tube line badge. With <see cref="Pulse"/> the tile breathes between
/// 65% and 100% brightness, driven by the frame time.
/// </summary>
public sealed class Pill : Node
{
    private readonly GlyphRun _run = new();
    private TextStyle? _defaultStyle;
    private TimeSpan _time;

    public Pill(string text = "", Pixel? background = null, bool pulse = false)
    {
        Text = text;
        Background = background ?? new Pixel(0, 90, 200);
        Pulse = pulse;
        Padding = new Thickness(3, 1);
    }

    public string Text { get; set; }
    public Pixel Background { get; set; }

    /// <summary>Text colour and font; defaults to white on the small font.</summary>
    public TextStyle? Style { get; set; }

    public int Radius { get; set; } = 2;
    public bool Pulse { get; set; }
    public TimeSpan PulsePeriod { get; set; } = TimeSpan.FromSeconds(1.2);

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
        if (_run.Set(Font, Text ?? "")) InvalidateLayout();
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        _run.Set(Font, Text ?? "");
        return new Size(_run.Width + Padding.Horizontal, _run.Height + Padding.Vertical);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var bg = Background;
        if (Pulse && PulsePeriod > TimeSpan.Zero)
        {
            float phase = (float)(_time.TotalSeconds / PulsePeriod.TotalSeconds * 2 * Math.PI);
            bg = bg.WithBrightness(0.65f + 0.35f * (0.5f + 0.5f * MathF.Sin(phase)));
        }

        UiDraw.FillRoundedRect(frame, bounds, Radius, bg);

        _run.Set(Font, Text ?? "");
        var content = ContentOf(bounds);
        var style = ResolvedStyle;
        int x = content.X + (content.Width - _run.Width) / 2;
        int y = content.Y + (content.Height - _run.Height) / 2;
        _run.Draw(frame, x, y, style.Color, style.Shadow, style.ShadowColor);
    }

    private TextStyle ResolvedStyle => Style ?? (_defaultStyle ??= new TextStyle(Fonts.Small, Pixel.White, Shadow: false));
    private BdfFont Font => ResolvedStyle.Font;
}

/// <summary>A 1px line across (<see cref="Orientation.Horizontal"/>) or down (<see cref="Orientation.Vertical"/>) its slot.</summary>
public sealed class Divider : Node
{
    public Divider(Orientation orientation = Orientation.Horizontal)
    {
        Orientation = orientation;
        if (orientation == Orientation.Horizontal) HAlign = Align.Stretch;
        else VAlign = Align.Stretch;
    }

    public Orientation Orientation { get; }
    public Pixel Color { get; set; } = new(60, 60, 60);

    protected override Size MeasureCore(int availW, int availH) =>
        Orientation == Orientation.Horizontal ? new Size(0, 1) : new Size(1, 0);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var line = Orientation == Orientation.Horizontal
            ? new Rectangle(bounds.X, bounds.Y, bounds.Width, 1)
            : new Rectangle(bounds.X, bounds.Y, 1, bounds.Height);
        frame.Fill(line, Color);
    }
}
