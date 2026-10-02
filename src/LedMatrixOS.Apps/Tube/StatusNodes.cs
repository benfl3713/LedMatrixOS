using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Tube;

/// <summary>What the lower half of the status board shows: one disruption, or the all-clear (<see cref="Status"/> is null).</summary>
internal sealed record StatusCard(LineStatus? Status)
{
    public static readonly StatusCard AllGood = new((LineStatus?)null);
    public string Key => Status?.LineId ?? "ok";
}

/// <summary>
/// One line as a tall coloured tile. Good service is a calm, slightly dimmed block with a tick; anything else brightens to full colour,
/// breathes, wears a pulsing frame in the severity colour and swaps its tick for a bang or cross.
/// </summary>
internal sealed class StatusTile : Node
{
    private readonly TextRun _code = new();
    private readonly BdfFontParser.BdfFont _font;
    private LineStatus _status;
    private Pixel _color, _ink;
    private TimeSpan _time;

    public StatusTile(LineStatus status, BdfFontParser.BdfFont font, int width, int height)
    {
        _font = font;
        _status = status;
        Width = width;
        Height = height;
        Apply(status);
    }

    public void Apply(LineStatus status)
    {
        _status = status;
        _color = TubeColors.Display(status.LineId);
        _ink = TubeColors.TextOn(_color);
        _code.Set(_font, TubeColors.Abbreviation(status.LineId, status.Name));
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var health = _status.Health;
        bool alert = health.NeedsAttention();
        float wave = TubeGfx.Wave(_time, 1.2, bounds.X * 0.004);
        float level = alert ? 0.55f + 0.45f * wave : health == Health.Planned ? 0.45f : 0.62f;
        frame.Fill(bounds, _color.WithBrightness(level));

        // The code sits on a solid plate so it stays legible at any brightness.
        var plate = new Rectangle(bounds.X, bounds.Y, bounds.Width, 11);
        frame.Fill(plate, _color);
        _code.Draw(frame, bounds.X + (bounds.Width - _code.Width) / 2, bounds.Y + 2, _ink);

        int cx = bounds.X + bounds.Width / 2, cy = bounds.Y + 11 + (bounds.Height - 11) / 2;
        var white = Pixel.White;
        switch (health)
        {
            case Health.Good: TubeGfx.DrawTick(frame, cx - 1, cy + 5, 9, white); break;
            case Health.Planned: for (int i = -1; i <= 1; i++) frame.Fill(new Rectangle(cx + i * 5 - 1, cy, 2, 2), white); break;
            case Health.Closed: TubeGfx.DrawCross(frame, cx - 1, cy, 4, white); break;
            default:
                TubeGfx.DrawBang(frame, cx, cy - 6, 13, wave > 0.4f ? white : health.Color());
                break;
        }

        if (alert && wave > 0.3f)
        {
            var edge = health.Color();
            frame.Fill(new Rectangle(bounds.X, bounds.Y + 11, bounds.Width, 1), edge);
            frame.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), edge);
            frame.Fill(new Rectangle(bounds.X, bounds.Y + 11, 1, bounds.Height - 11), edge);
            frame.Fill(new Rectangle(bounds.Right - 1, bounds.Y + 11, 1, bounds.Height - 11), edge);
        }
    }
}

/// <summary>The TfL roundel as a small icon with an optional tick underneath.</summary>
internal sealed class RoundelIcon : Node
{
    public RoundelIcon(int size)
    {
        Width = size;
        Height = size;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int r = bounds.Width / 2 - 2;
        TubeGfx.DrawRoundel(frame, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, r, 3, TubeGfx.Roundel, TubeGfx.RoundelBar);
    }
}

internal static class StatusCards
{
    /// <summary>First clause of TfL's reason text, without the "NORTHERN LINE:" prefix, short enough to read in a couple of seconds.</summary>
    public static string ShortReason(string reason, int max = 90)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "";
        var text = reason.Trim();
        int colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon is > 0 and < 30 && text[..colon].All(c => char.IsUpper(c) || c == ' ' || c == '&' || c == '-')) text = text[(colon + 2)..];
        int stop = text.IndexOfAny(['.', ';']);
        if (stop > 10) text = text[..stop];
        return text.Length <= max ? text : text[..(max - 3)].TrimEnd() + "...";
    }

    public static Node Build(StatusCard card, BoardStyles styles)
    {
        if (card.Status is not { } s)
        {
            return new Stack(Orientation.Horizontal, gap: 6)
            {
                CrossAlign = Align.Center,
                Padding = new Thickness(6, 0),
                Children =
                {
                    new RoundelIcon(22),
                    new Label("GOOD SERVICE") { Style = new TextStyle(Fonts.Big, LineHealth.GoodColor) },
                    new Label("on all lines") { Style = styles.Tiny, Grow = 1, TextAlignment = TextAlign.Right },
                },
            };
        }

        var color = TubeColors.Display(s.LineId);
        return new Stack(Orientation.Vertical, gap: 0)
        {
            Padding = new Thickness(4, 1),
            Children =
            {
                new Stack(Orientation.Horizontal, gap: 5)
                {
                    CrossAlign = Align.Center,
                    Height = 13,
                    Children =
                    {
                        new Pill(s.Name.ToUpperInvariant(), color) { Style = new TextStyle(Fonts.Small, TubeColors.TextOn(color), Shadow: false), Radius = 2, Height = 13 },
                        new Label(s.Description.ToUpperInvariant()) { Style = new TextStyle(Fonts.Small, s.Health.Color(), Shadow: false) },
                    },
                },
                new MarqueeLabel(ShortReason(s.Reason)) { Style = styles.Strip, Height = 8, Margin = new Thickness(0, 1, 0, 0), Speed = 40 },
            },
        };
    }
}
