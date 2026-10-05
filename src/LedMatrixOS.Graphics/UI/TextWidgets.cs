using System.Globalization;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// Shared text handling for <see cref="Label"/>, <see cref="MarqueeLabel"/> and <see cref="Clock"/>: a style, fixed or bound text,
/// and a cached glyph map that is rebuilt only when the text or font changes.
/// </summary>
public abstract class TextNode : Node
{
    private string _text = "";
    private TextStyle? _style;
    private TextStyle? _defaultStyle;
    private TextAlign _textAlignment;
    internal readonly GlyphRun Run = new();

    /// <summary>Evaluated every frame; return a cached string rather than formatting a new one each time. Setting <see cref="Text"/> clears it.</summary>
    public Func<string>? TextSource { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            TextSource = null;
            _text = value ?? "";
            InvalidateLayout();
        }
    }

    /// <summary>Font and colour. Defaults to the small font in white.</summary>
    public TextStyle? Style
    {
        get => _style;
        set
        {
            _style = value;
            InvalidateLayout();
        }
    }

    /// <summary>Horizontal placement of the text inside the node.</summary>
    public TextAlign TextAlignment
    {
        get => _textAlignment;
        set => _textAlignment = value;
    }

    protected TextStyle ResolvedStyle => _style ?? (_defaultStyle ??= new TextStyle(Fonts.Small, Pixel.White));

    protected void EnsureRun()
    {
        if (Run.Set(ResolvedStyle.Font, _text)) InvalidateLayout();
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        if (TextSource is { } source) _text = source() ?? "";
        EnsureRun();
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        EnsureRun();
        return new Size(Run.Width + Padding.Horizontal, Run.Height + Padding.Vertical);
    }

    protected int AlignedX(Rectangle content, int textWidth) => _textAlignment switch
    {
        TextAlign.Center => content.X + (content.Width - textWidth) / 2,
        TextAlign.Right => content.Right - textWidth,
        _ => content.X,
    };
}

/// <summary>A line of text, clipped to its bounds. Text is fixed, or bound through <see cref="TextNode.TextSource"/>.</summary>
public class Label : TextNode
{
    public Label(string text = "") => Text = text;

    public Label(Func<string> source) => TextSource = source;


    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        EnsureRun();
        if (Run.Width == 0) return;

        var content = ContentOf(bounds);
        var style = ResolvedStyle;
        frame.PushClip(content);
        Run.Draw(frame, AlignedX(content, Run.Width), content.Y, style.Color, style.Shadow, style.ShadowColor);
        frame.PopClip();
    }
}

/// <summary>
/// A label that scrolls text wider than its bounds: pause, scroll to the end, pause, jump back and repeat.
/// Time comes from the frame context, so playback is deterministic. Text that fits is just drawn aligned.
/// Replaces the old <c>ScrollOverflowText*</c> helpers.
/// </summary>
public sealed class MarqueeLabel : TextNode
{
    private enum Phase { PauseStart, Scroll, PauseEnd }

    private Phase _phase;
    private TimeSpan _phaseTime;
    private int _lastTextWidth = -1, _lastViewWidth = -1;

    public MarqueeLabel(string text = "") => Text = text;

    public MarqueeLabel(Func<string> source) => TextSource = source;


    /// <summary>How long the text rests at the start and at the end.</summary>
    public TimeSpan PauseDuration { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Scroll speed in pixels per second.</summary>
    public float Speed { get; set; } = 30f;

    /// <summary>Current horizontal scroll in whole pixels (0 when the text fits or is resting at the start).</summary>
    public int Offset { get; private set; }

    // Takes whatever width the parent offers: the point is to show long text in a short space.
    protected override Size MeasureCore(int availW, int availH)
    {
        EnsureRun();
        return new Size(Math.Min(Run.Width + Padding.Horizontal, availW), Run.Height + Padding.Vertical);
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        int view = Content.Width;
        int overflow = Run.Width - view;
        if (Run.Width != _lastTextWidth || view != _lastViewWidth)
        {
            _lastTextWidth = Run.Width;
            _lastViewWidth = view;
            Reset();
        }

        if (overflow <= 0 || Speed <= 0f)
        {
            Offset = 0;
            return;
        }

        _phaseTime += ctx.Delta;
        // A long frame can cross several phases, so loop until the time is used up.
        for (int guard = 0; guard < 4; guard++)
        {
            if (_phase == Phase.Scroll)
            {
                var scrollTime = TimeSpan.FromSeconds(overflow / (double)Speed);
                if (_phaseTime >= scrollTime)
                {
                    _phaseTime -= scrollTime;
                    _phase = Phase.PauseEnd;
                    Offset = overflow;
                    continue;
                }

                Offset = Math.Min(overflow, (int)(Speed * _phaseTime.TotalSeconds));
                return;
            }

            if (_phaseTime < PauseDuration) return;

            _phaseTime -= PauseDuration;
            if (_phase == Phase.PauseStart) _phase = Phase.Scroll;
            else
            {
                _phase = Phase.PauseStart;
                Offset = 0;
            }
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        EnsureRun();
        if (Run.Width == 0) return;

        var content = ContentOf(bounds);
        var style = ResolvedStyle;
        int x = Run.Width <= content.Width ? AlignedX(content, Run.Width) : content.X - Offset;
        frame.PushClip(content);
        Run.Draw(frame, x, content.Y, style.Color, style.Shadow, style.ShadowColor);
        frame.PopClip();
    }

    private void Reset()
    {
        _phase = Phase.PauseStart;
        _phaseTime = TimeSpan.Zero;
        Offset = 0;
    }
}

/// <summary>
/// Shows the current time, formatted with a .NET date format string. The time comes from the injected <see cref="TimeProvider"/>
/// (or the host's), never from DateTime.Now, and the text is re-formatted at most once a second.
/// </summary>
public sealed class Clock : TextNode
{
    private readonly string _format;
    private readonly TimeProvider? _time;
    private long _lastSecond = long.MinValue;
    private string _cached = "";

    public Clock(string format = "HH:mm", TimeProvider? time = null)
    {
        _format = format;
        _time = time;
        TextSource = FormatNow;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        EnsureRun();
        if (Run.Width == 0) return;

        var content = ContentOf(bounds);
        var style = ResolvedStyle;
        frame.PushClip(content);
        Run.Draw(frame, AlignedX(content, Run.Width), content.Y, style.Color, style.Shadow, style.ShadowColor);
        frame.PopClip();
    }

    private string FormatNow()
    {
        var now = (_time ?? Host?.Time ?? TimeProvider.System).GetLocalNow();
        long second = now.Ticks / TimeSpan.TicksPerSecond;
        if (second != _lastSecond)
        {
            _lastSecond = second;
            _cached = now.ToString(_format, CultureInfo.InvariantCulture);
        }
        return _cached;
    }
}
