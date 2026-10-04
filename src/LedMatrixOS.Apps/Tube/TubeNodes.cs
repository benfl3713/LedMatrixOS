using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Tube;

/// <summary>Colours and small drawing helpers shared by the Tube widgets. Nothing here allocates.</summary>
internal static class TubeGfx
{
    public static readonly Pixel Amber = new(255, 176, 0);
    public static readonly Pixel Roundel = new(230, 38, 38);
    public static readonly Pixel RoundelBar = new(30, 90, 255);
    public static readonly Pixel Ink = new(225, 225, 225);
    public static readonly Pixel Muted = new(130, 130, 140);

    /// <summary>Smooth 0..1 wave with the given period.</summary>
    public static float Wave(TimeSpan time, double periodSeconds, double phase = 0)
    {
        double angle = (time.TotalSeconds / periodSeconds + phase) * 2 * Math.PI;
        return 0.5f + 0.5f * MathF.Sin((float)angle);
    }

    public static Pixel Scale(Pixel c, float f) => c.WithBrightness(f);

    /// <summary>1px outline with the corner pixels cut off.</summary>
    public static void OutlineRound(FrameBuffer frame, Rectangle rect, Pixel color)
    {
        if (rect.Width < 3 || rect.Height < 3) return;
        frame.Fill(new Rectangle(rect.X + 1, rect.Y, rect.Width - 2, 1), color);
        frame.Fill(new Rectangle(rect.X + 1, rect.Bottom - 1, rect.Width - 2, 1), color);
        frame.Fill(new Rectangle(rect.X, rect.Y + 1, 1, rect.Height - 2), color);
        frame.Fill(new Rectangle(rect.Right - 1, rect.Y + 1, 1, rect.Height - 2), color);
    }

    /// <summary>Fills a rectangle with a left-to-right ramp between two colours, one column at a time.</summary>
    public static void FillRamp(FrameBuffer frame, Rectangle area, Pixel left, Pixel right, float rampFraction)
    {
        int ramp = Math.Max(1, (int)(area.Width * rampFraction));
        for (int x = 0; x < area.Width; x++)
        {
            float t = Math.Min(1f, x / (float)ramp);
            t *= t;
            frame.Fill(new Rectangle(area.X + x, area.Y, 1, area.Height), Pixel.Lerp(left, right, t));
        }
    }

    /// <summary>
    /// The TfL roundel: a ring with a bar through it. Edges are anti-aliased against whatever is underneath.
    /// <paramref name="chase"/> (0..1, or negative for none) dims the ring except for a bright head that travels round it.
    /// </summary>
    public static void DrawRoundel(FrameBuffer frame, int cx, int cy, int outerRadius, int thickness, Pixel ring, Pixel bar, float chase = -1f)
    {
        float rOut = outerRadius, rIn = outerRadius - thickness;
        int extent = outerRadius + 1;
        for (int y = -extent; y <= extent; y++)
        {
            for (int x = -extent; x <= extent; x++)
            {
                float d = MathF.Sqrt(x * x + y * y);
                float cover = Math.Clamp(rOut - d + 0.5f, 0f, 1f) * Math.Clamp(d - rIn + 0.5f, 0f, 1f);
                if (cover <= 0f) continue;
                float level = 1f;
                if (chase >= 0f)
                {
                    float angle = MathF.Atan2(y, x) / (2f * MathF.PI) + 0.5f;
                    float behind = (chase - angle + 2f) % 1f;
                    level = 0.22f + 0.78f * MathF.Pow(1f - behind, 3f);
                }
                frame.BlendPixel(cx + x, cy + y, ring.WithBrightness(level), cover);
            }
        }

        int half = Math.Max(2, thickness) / 2 + 1;
        frame.Fill(new Rectangle(cx - outerRadius - 3, cy - half, outerRadius * 2 + 7, half * 2 + 1), bar);
    }

    /// <summary>A check mark roughly <paramref name="size"/> px wide, its bottom point at (x, y).</summary>
    public static void DrawTick(FrameBuffer frame, int x, int y, int size, Pixel color)
    {
        int shortArm = Math.Max(2, size / 3);
        for (int i = 0; i <= shortArm; i++)
        {
            frame.SetPixel(x - i, y - i, color);
            frame.SetPixel(x - i - 1, y - i, color);
        }
        for (int i = 1; i <= size - shortArm; i++)
        {
            frame.SetPixel(x + i, y - i, color);
            frame.SetPixel(x + i + 1, y - i, color);
        }
    }

    public static void DrawCross(FrameBuffer frame, int cx, int cy, int radius, Pixel color)
    {
        for (int i = -radius; i <= radius; i++)
        {
            frame.SetPixel(cx + i, cy + i, color);
            frame.SetPixel(cx + i + 1, cy + i, color);
            frame.SetPixel(cx + i, cy - i, color);
            frame.SetPixel(cx + i + 1, cy - i, color);
        }
    }

    /// <summary>An exclamation mark inside a triangle-free, bold form: a bar over a dot.</summary>
    public static void DrawBang(FrameBuffer frame, int cx, int top, int height, Pixel color)
    {
        int bar = Math.Max(2, height - 4);
        frame.Fill(new Rectangle(cx - 1, top, 3, bar), color);
        frame.Fill(new Rectangle(cx - 1, top + height - 2, 3, 2), color);
    }
}

/// <summary>A node that fills its bounds with a colour.</summary>
internal sealed class Block : Node
{
    public Block(Pixel color, int? width = null, int? height = null)
    {
        Color = color;
        Width = width;
        Height = height;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public Pixel Color { get; set; }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds) => frame.Fill(bounds, Color);
}

/// <summary>
/// Wraps a fixed-height row for use in a <see cref="ListView{T}"/>. The row sits at the bottom of a slot that is clipped to the slot, so when the
/// list collapses a leaving row or grows an arriving one the content scrolls up instead of being squashed.
/// </summary>
internal sealed class ScrollSlot : Panel
{
    public ScrollSlot(Node content, int height)
    {
        ClipChildren = true;
        content.Height = height;
        content.HAlign = Align.Stretch;
        content.VAlign = Align.End;
        Add(content);
    }

    public Node Content => Children[0];
}

/// <summary>
/// A flashing "DUE" tag. <see cref="Filled"/> draws it as a solid amber plate with dark text (hero), otherwise as pulsing amber text.
/// </summary>
internal sealed class DueBadge : Node
{
    private readonly TextRun _run = new();
    private TimeSpan _time;

    public DueBadge(BdfFont font, bool filled)
    {
        Font = font;
        Filled = filled;
        _run.Set(font, "DUE");
    }

    public BdfFont Font { get; }
    public bool Filled { get; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override Size MeasureCore(int availW, int availH) =>
        new(_run.Width + (Filled ? 8 : 0), _run.Height + (Filled ? 2 : 0));

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float wave = TubeGfx.Wave(_time, 0.9);
        int tx = bounds.X + (bounds.Width - _run.Width) / 2;
        int ty = bounds.Y + (bounds.Height - _run.Height) / 2;
        if (Filled)
        {
            var plate = Pixel.Lerp(TubeGfx.Amber.WithBrightness(0.55f), new Pixel(255, 214, 80), wave);
            frame.FillRoundedRect(bounds, 2, plate);
            _run.Draw(frame, tx, ty, Pixel.Black);
        }
        else
        {
            _run.Draw(frame, tx, ty, TubeGfx.Amber.WithBrightness(0.55f + 0.45f * wave));
        }
    }
}

/// <summary>
/// The line tile in the bottom strip: a coloured plate with the line's two letter code. A disrupted line pulses, flashes a coloured frame
/// and alternates its code with a bang, so it is the first thing the eye lands on.
/// </summary>
internal sealed class LinePill : Node
{
    private readonly TextRun _code = new();
    private readonly TextRun _bang = new();
    private BdfFont? _font;
    private TimeSpan _time;
    private Pixel _color;
    private Pixel _ink;
    private Health _health;

    public LinePill(LineStatus status, BdfFont font)
    {
        _font = font;
        _bang.Set(font, "!");
        Apply(status);
        Width = 15;
        Height = 10;
    }

    public void Apply(LineStatus status)
    {
        _color = TubeColors.Display(status.LineId);
        _ink = TubeColors.TextOn(_color);
        _health = status.Health;
        _code.Set(_font!, TubeColors.Abbreviation(status.LineId, status.Name));
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        bool alert = _health.NeedsAttention();
        float wave = TubeGfx.Wave(_time, 1.1);
        var fill = _color;
        if (alert) fill = _color.WithBrightness(0.35f + 0.65f * wave);
        else if (_health == Health.Planned) fill = _color.WithBrightness(0.5f);

        frame.FillRoundedRect(bounds, 2, fill);

        if (alert)
        {
            var frameColor = _health.Color();
            if (wave > 0.35f) TubeGfx.OutlineRound(frame, bounds, frameColor);
        }

        bool showBang = alert && (int)(_time.TotalSeconds / 0.9) % 2 == 1;
        var run = showBang ? _bang : _code;
        var ink = alert ? (showBang ? _health.Color() : Pixel.White) : _ink;
        if (alert && !showBang && wave < 0.5f) ink = _ink;
        run.Draw(frame, bounds.X + (bounds.Width - run.Width) / 2, bounds.Y + (bounds.Height - run.Height) / 2, ink);
    }
}

internal enum BoardState { NoStation, Loading, NoTrains, Offline }

/// <summary>
/// Full-board message with the roundel: used while there is nothing to list. The roundel breathes (or chases while loading)
/// so the display never looks frozen.
/// </summary>
internal sealed class StateScreen : Node
{
    private readonly TextRun _title = new();
    private readonly TextRun _subtitle = new();
    private readonly BdfFont _titleFont, _subFont;
    private TimeSpan _time;
    private BoardState _state;

    public StateScreen(BdfFont titleFont, BdfFont subFont)
    {
        _titleFont = titleFont;
        _subFont = subFont;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        State = BoardState.Loading;
    }

    public string Detail { get; set; } = "";

    public BoardState State
    {
        get => _state;
        set
        {
            _state = value;
            _title.Set(_titleFont, value switch
            {
                BoardState.NoStation => "NO STATION",
                BoardState.Loading => "LOADING",
                BoardState.NoTrains => "NO TRAINS",
                _ => "NO DATA",
            });
        }
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
        string sub = _state switch
        {
            BoardState.NoStation => "Choose a station in the app",
            BoardState.Loading => string.IsNullOrEmpty(Detail) ? "Fetching departures" : Detail,
            BoardState.NoTrains => string.IsNullOrEmpty(Detail) ? "Nothing due right now" : Detail,
            _ => "Retrying shortly",
        };
        _subtitle.Set(_subFont, sub);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int cx = bounds.X + 34, cy = bounds.Y + bounds.Height / 2;
        float breathe = TubeGfx.Wave(_time, 2.4);
        float chase = (float)(_time.TotalSeconds / 1.6 % 1.0);

        var ring = _state == BoardState.Offline ? LineHealth.SevereColor : TubeGfx.Roundel;
        TubeGfx.DrawRoundel(frame, cx, cy, 19, 5, _state == BoardState.Loading ? ring : ring.WithBrightness(0.6f + 0.4f * breathe),
            TubeGfx.RoundelBar, _state == BoardState.Loading ? chase : -1f);

        int x = bounds.X + 68;
        int top = cy - (_title.Height + 2 + _subtitle.Height) / 2;
        var titleColor = _state == BoardState.Offline ? LineHealth.SevereColor : TubeGfx.Amber;
        _title.Draw(frame, x, top, titleColor, shadow: true);

        if (_state == BoardState.Loading)
        {
            int dots = 1 + (int)(_time.TotalSeconds * 2.5) % 3;
            int dx = x + _title.Width + 6;
            for (int i = 0; i < 3; i++)
                frame.Fill(new Rectangle(dx + i * 6, top + _title.Height - 5, 3, 3), i < dots ? titleColor : titleColor.WithBrightness(0.2f));
        }

        _subtitle.Draw(frame, x, top + _title.Height + 2, TubeGfx.Muted);
    }
}
