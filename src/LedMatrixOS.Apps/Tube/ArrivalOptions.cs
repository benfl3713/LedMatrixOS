using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Tube;

/// <summary>How an arrival is written: the minutes away, the clock time it arrives, or both.</summary>
internal enum ArrivalFormat { Minutes, Clock, Both }

/// <summary>
/// The "Arrival Format", "Show Destination" and "Due Threshold" settings shared by the bus, tube and rail boards. The app owns one instance and
/// updates it from its settings every frame; the rows read it, so a change shows up without rebuilding the board.
/// </summary>
internal sealed class ArrivalOptions
{
    public const string MinutesOption = "Minutes", ClockOption = "Clock time", BothOption = "Both";
    public const int DefaultDueSeconds = 60, MinDueSeconds = 10, MaxDueSeconds = 300;

    public ArrivalFormat Format { get; set; }
    public bool ShowDestination { get; set; } = true;
    public int DueSeconds { get; set; } = DefaultDueSeconds;

    /// <summary>Local wall clock, from the app's time provider; used to turn "3 min away" into "14:07".</summary>
    public Func<DateTime> LocalNow { get; set; } = () => DateTime.Today;

    public static ArrivalFormat Parse(string? value) => value switch
    {
        ClockOption => ArrivalFormat.Clock,
        BothOption => ArrivalFormat.Both,
        _ => ArrivalFormat.Minutes,
    };

    public void Apply(string? format, bool showDestination, int dueSeconds)
    {
        Format = Parse(format);
        ShowDestination = showDestination;
        DueSeconds = Math.Clamp(dueSeconds, MinDueSeconds, MaxDueSeconds);
    }
}

/// <summary>
/// A destination that picks the longest form of its text that fits: the full name, then trailing words shortened to an initial
/// ("Walthamstow C."), then cut with an ellipsis, so a name is never chopped in the middle of a word. Recomputed only when the text or width changes.
/// </summary>
internal sealed class FitLabel : Node
{
    private readonly TextRun _run = new();
    private TextStyle _style;
    private string _text;
    private string _shown = "";
    private int _fitWidth = -1;

    public FitLabel(string text, TextStyle style)
    {
        _style = style;
        _text = text;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            _fitWidth = -1;
            InvalidateLayout();
        }
    }

    public TextStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            _fitWidth = -1;
            InvalidateLayout();
        }
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        _run.Set(_style.Font, _text);
        return new Size(Math.Min(_run.Width + Padding.Horizontal, availW), _run.Height + Padding.Vertical);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var content = ContentOf(bounds);
        if (content.Width != _fitWidth)
        {
            _fitWidth = content.Width;
            _shown = Fit(_style.Font, _text, content.Width);
        }

        _run.Set(_style.Font, _shown);
        if (_run.Width == 0) return;
        frame.PushClip(content);
        _run.Draw(frame, content.X, content.Y, _style.Color, _style.Shadow, _style.ShadowColor);
        frame.PopClip();
    }

    /// <summary>The longest of: the text, the text with trailing words abbreviated, the text cut with an ellipsis, that fits <paramref name="width"/>.</summary>
    internal static string Fit(BdfFontParser.BdfFont font, string text, int width)
    {
        if (font.MeasureText(text) <= width) return text;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Shorten from the last word backwards; the first word is the place name and stays whole.
        for (int i = words.Length - 1; i >= 1; i--)
        {
            words[i] = Abbreviate(words[i]);
            var candidate = string.Join(' ', words);
            if (font.MeasureText(candidate) <= width) return candidate;
        }

        // Still too wide: drop the trailing words ("Walthamstow Central" becomes "Walthamstow") before resorting to an ellipsis.
        var whole = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int keep = whole.Length - 1; keep >= 1; keep--)
        {
            var candidate = string.Join(' ', whole, 0, keep);
            if (font.MeasureText(candidate) <= width) return candidate;
        }

        return font.TruncateWithEllipsis(text, width);
    }

    private static string Abbreviate(string word)
    {
        if (word.Length <= 2 || word[0] == '(') return word;
        return word.Length > 3 && word.EndsWith(')') ? word[0] + ".)" : word[0] + ".";
    }
}
