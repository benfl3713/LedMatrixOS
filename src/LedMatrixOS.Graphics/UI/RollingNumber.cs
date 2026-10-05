using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

public enum RollStyle
{
    /// <summary>Each changed digit scrolls vertically out and the new one in, like a mechanical counter.</summary>
    Odometer,

    /// <summary>Each digit sits on a card whose top flap folds down to reveal the new digit, like a split-flap clock.</summary>
    Flip,
}

/// <summary>
/// A non-negative whole number that animates when its value changes: only the digits that changed roll.
/// Set <see cref="Value"/> or bind <see cref="Source"/>. The first value shows without animation.
/// </summary>
public sealed class RollingNumber : Node
{
    private const int MaxDigits = 10;

    private static readonly int[] Powers = [1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000];

    private readonly int[] _current = new int[MaxDigits];
    private readonly int[] _previous = new int[MaxDigits];
    private readonly Tween<float>[] _roll = new Tween<float>[MaxDigits];
    private readonly GlyphRun[] _digits = new GlyphRun[10];
    private BdfFont? _digitFont;
    private TextStyle? _defaultStyle;
    private int _applied = -1;
    private int _count = 1;
    private bool _rollUp = true;
    private int _minDigits = 1;
    private RollStyle _mode;

    public RollingNumber(int value = 0)
    {
        Value = value;
        for (int i = 0; i < MaxDigits; i++)
        {
            _current[i] = -1;
            _previous[i] = -1;
            _roll[i] = new Tween<float>(1f);
        }
        for (int d = 0; d < _digits.Length; d++) _digits[d] = new GlyphRun();
    }

    public RollingNumber(Func<int> source) : this() => Source = source;

    public int Value { get; set; }

    /// <summary>Evaluated every frame when set, instead of <see cref="Value"/>.</summary>
    public Func<int>? Source { get; set; }

    public TextStyle? Style { get; set; }

    public RollStyle Mode { get => _mode; set => SetLayout(ref _mode, value); }

    /// <summary>Pads with leading zeros up to this many digits.</summary>
    public int MinDigits { get => _minDigits; set => SetLayout(ref _minDigits, Math.Clamp(value, 1, MaxDigits)); }

    /// <summary>Gap between digits in pixels.</summary>
    public int Spacing { get; set; }

    /// <summary>Roll time per change. Defaults to 350 ms (odometer) or 500 ms (flip).</summary>
    public TimeSpan? Duration { get; set; }

    public Func<float, float>? Easing { get; set; }

    public Pixel CardColor { get; set; } = new(34, 34, 38);
    public Pixel FlapColor { get; set; } = new(52, 52, 58);
    public Pixel SplitColor { get; set; } = Pixel.Black;

    /// <summary>True while any digit is still rolling.</summary>
    public bool IsRolling
    {
        get
        {
            foreach (var t in _roll) if (t.IsRunning) return true;
            return false;
        }
    }

    /// <summary>Roll progress (0-1, eased) of the digit slot, counting from the rightmost digit; 1 when it is at rest.</summary>
    public float RollProgress(int slot) => _roll[slot].Value;

    /// <summary>Digit shown in the slot (counting from the right), or -1 when blank.</summary>
    public int DigitAt(int slot) => _current[slot];

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        EnsureDigits();

        int value = Math.Max(0, Source?.Invoke() ?? Value);
        if (value != _applied) Apply(value, animate: _applied >= 0);
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        EnsureDigits();
        return new Size(SlotWidth * _count + Spacing * Math.Max(0, _count - 1) + Padding.Horizontal,
            CardHeight + Padding.Vertical);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        EnsureDigits();
        var content = ContentOf(bounds);
        var style = ResolvedStyle;
        int slotWidth = SlotWidth;

        frame.PushClip(content);
        for (int slot = 0; slot < MaxDigits; slot++)
        {
            bool rolling = _roll[slot].IsRunning;
            if (_current[slot] < 0 && !rolling) continue;

            int x = content.Right - (slot + 1) * slotWidth - slot * Spacing;
            if (_mode == RollStyle.Flip) DrawFlip(frame, x, content.Y, slot, rolling, style);
            else DrawOdometer(frame, x, content.Y, slot, rolling, style);
        }
        frame.PopClip();
    }

    private void Apply(int value, bool animate)
    {
        int digits = 1;
        while (digits < MaxDigits && value >= Powers[digits]) digits++;
        int count = Math.Max(_minDigits, digits);
        if (count != _count)
        {
            _count = count;
            InvalidateLayout();
        }

        _rollUp = value >= _applied;
        var duration = Duration ?? TimeSpan.FromMilliseconds(_mode == RollStyle.Flip ? 500 : 350);
        var easing = Easing ?? (_mode == RollStyle.Flip ? Core.Animation.Easing.InOutCubic : Core.Animation.Easing.OutCubic);

        for (int slot = 0; slot < MaxDigits; slot++)
        {
            int digit = slot < count ? value / Powers[slot] % 10 : -1;
            if (digit == _current[slot]) continue;

            _previous[slot] = _current[slot];
            _current[slot] = digit;
            _roll[slot].Set(0f);
            if (animate && Host is { } host) host.Animator.Animate(_roll[slot], 1f, duration, easing);
            else _roll[slot].Set(1f);
        }

        _applied = value;
    }

    private void DrawOdometer(FrameBuffer frame, int x, int y, int slot, bool rolling, TextStyle style)
    {
        int h = LineHeight;
        var cell = new Rectangle(x, y, _digits[0].Width, h);
        frame.PushClip(cell);
        if (!rolling) DrawDigit(frame, _current[slot], x, y, style, style.Shadow);
        else
        {
            int offset = (int)MathF.Round(_roll[slot].Value * h);
            if (_rollUp)
            {
                DrawDigit(frame, _previous[slot], x, y - offset, style, style.Shadow);
                DrawDigit(frame, _current[slot], x, y + h - offset, style, style.Shadow);
            }
            else
            {
                DrawDigit(frame, _previous[slot], x, y + offset, style, style.Shadow);
                DrawDigit(frame, _current[slot], x, y - h + offset, style, style.Shadow);
            }
        }
        frame.PopClip();
    }

    // The top flap of the old digit folds down to the split line, then the bottom flap of the new digit unfolds.
    private void DrawFlip(FrameBuffer frame, int x, int y, int slot, bool rolling, TextStyle style)
    {
        int width = SlotWidth, height = CardHeight, half = height / 2;
        var card = new Rectangle(x, y, width, height);
        int glyphX = x + 1;

        frame.PushClip(card);
        frame.Fill(card, CardColor);

        if (!rolling) DrawDigit(frame, _current[slot], glyphX, y, style, false);
        else
        {
            float p = _roll[slot].Value;
            frame.PushClip(new Rectangle(x, y, width, half));
            DrawDigit(frame, _current[slot], glyphX, y, style, false);
            frame.PopClip();
            frame.PushClip(new Rectangle(x, y + half, width, height - half));
            DrawDigit(frame, _previous[slot], glyphX, y, style, false);
            frame.PopClip();

            if (p < 0.5f)
            {
                int flap = (int)MathF.Round(half * (1f - p * 2f));
                if (flap > 0 && _previous[slot] >= 0)
                {
                    frame.Fill(new Rectangle(x, y + half - flap, width, flap), FlapColor);
                    _digits[_previous[slot]].DrawRows(frame, glyphX, 0, half, y + half - flap, flap, style.Color);
                }
            }
            else
            {
                int flap = (int)MathF.Round(half * (p - 0.5f) * 2f);
                if (flap > 0)
                {
                    frame.Fill(new Rectangle(x, y + half, width, flap), FlapColor);
                    if (_current[slot] >= 0)
                        _digits[_current[slot]].DrawRows(frame, glyphX, half, height, y + half, flap, style.Color);
                }
            }
        }

        frame.Fill(new Rectangle(x, y + half, width, 1), SplitColor);
        frame.PopClip();
    }

    private void DrawDigit(FrameBuffer frame, int digit, int x, int y, TextStyle style, bool shadow)
    {
        if (digit < 0) return;
        _digits[digit].Draw(frame, x, y, style.Color, shadow, style.ShadowColor);
    }

    private TextStyle ResolvedStyle => Style ?? (_defaultStyle ??= new TextStyle(Fonts.Big, Pixel.White));

    private int LineHeight => _digits[0].Height;
    private int CardHeight => _mode == RollStyle.Flip ? LineHeight + LineHeight % 2 : LineHeight;
    private int SlotWidth => _digits[0].Width + (_mode == RollStyle.Flip ? 2 : 0);

    private void EnsureDigits()
    {
        var font = ResolvedStyle.Font;
        if (ReferenceEquals(font, _digitFont)) return;
        _digitFont = font;
        for (int d = 0; d < _digits.Length; d++) _digits[d].Set(font, d.ToString());
        InvalidateLayout();
    }
}
