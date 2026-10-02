using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Ambient;

/// <summary>
/// A row of big digits and colons ("dd:dd", "d:dd:dd") built from the BDF digits scaled by whole pixels. Unlike RollingNumber it is
/// painted with a <see cref="TextPaint"/> (gradients, tweened colour, alpha), which the hero clock and countdown need. A digit that
/// changes slides out upward while the new one rises in underneath it, fading as it goes.
/// Pattern characters: 'd' digit, ':' colon, ' ' narrow gap.
/// </summary>
internal sealed class DigitStrip : Node
{
    private readonly GlyphLine[] _glyphs = new GlyphLine[10];
    private BdfFont? _font;
    private string _pattern = "";
    private int[] _values = [];
    private int[] _previous = [];
    private Tween<float>[] _roll = [];
    private int[] _slotOf = [];
    private int _scale = 3;
    private int _gap = 3;

    public DigitStrip(BdfFont font, string pattern = "dd:dd", int scale = 3)
    {
        for (int i = 0; i < _glyphs.Length; i++) _glyphs[i] = new GlyphLine();
        Font = font;
        Pattern = pattern;
        Scale = scale;
    }

    public BdfFont Font
    {
        get => _font!;
        set
        {
            if (ReferenceEquals(_font, value)) return;
            _font = value;
            for (int d = 0; d < 10; d++) _glyphs[d].Set(value, d.ToString());
            InvalidateLayout();
        }
    }

    public string Pattern
    {
        get => _pattern;
        set
        {
            if (value == _pattern) return;
            _pattern = value;
            int n = 0;
            foreach (var c in value) if (c == 'd') n++;
            _values = new int[n];
            _previous = new int[n];
            _roll = new Tween<float>[n];
            _slotOf = new int[value.Length];
            for (int i = 0; i < n; i++)
            {
                _values[i] = -1;
                _previous[i] = -1;
                _roll[i] = new Tween<float>(1f);
            }
            int s = 0;
            for (int i = 0; i < value.Length; i++) _slotOf[i] = value[i] == 'd' ? s++ : -1;
            InvalidateLayout();
        }
    }

    public int Scale { get => _scale; set => SetLayout(ref _scale, Math.Max(1, value)); }
    public int Gap { get => _gap; set => SetLayout(ref _gap, value); }

    /// <summary>Colour and effects for this frame. Set it from the owner every frame; it is a plain struct.</summary>
    public TextPaint Paint = TextPaint.Solid(Pixel.White);

    /// <summary>Brightness of the colons, 0-1 (they pulse).</summary>
    public float ColonAlpha = 1f;

    /// <summary>Pixels the whole row is lifted by (a quick pop on each tick).</summary>
    public float Lift;

    public Func<float, float> Easing { get; set; } = Core.Animation.Easing.OutCubic;
    public TimeSpan RollTime { get; set; } = TimeSpan.FromMilliseconds(380);

    public int SlotCount => _values.Length;

    public int Value(int slot) => _values[slot];

    /// <summary>Shows <paramref name="digit"/> (0-9, or -1 to blank) in the slot; animates when it changes.</summary>
    public void SetDigit(int slot, int digit, bool animate = true)
    {
        if ((uint)slot >= (uint)_values.Length || _values[slot] == digit) return;
        _previous[slot] = _values[slot];
        _values[slot] = digit;
        if (animate && _previous[slot] >= 0 && digit >= 0 && Host is { } host)
        {
            _roll[slot].Set(0f);
            host.Animator.Animate(_roll[slot], 1f, RollTime, Easing);
        }
        else _roll[slot].Set(1f);
    }

    /// <summary>Convenience: writes a 0-99 number into two consecutive slots starting at <paramref name="firstSlot"/>.</summary>
    public void SetTwo(int firstSlot, int value, bool animate = true)
    {
        value = Math.Clamp(value, 0, 99);
        SetDigit(firstSlot, value / 10, animate);
        SetDigit(firstSlot + 1, value % 10, animate);
    }

    public int DigitWidth => _glyphs[0].Width * _scale;
    public int ColonWidth => 2 * _scale + 2;
    public int GapWidth => 2 * _scale;
    public int LineHeight => _glyphs[0].Height * _scale;

    public int TotalWidth
    {
        get
        {
            int w = 0, items = 0;
            foreach (var c in _pattern)
            {
                w += c == 'd' ? DigitWidth : c == ':' ? ColonWidth : GapWidth;
                items++;
            }
            return w + Math.Max(0, items - 1) * _gap;
        }
    }

    protected override Size MeasureCore(int availW, int availH) => new(TotalWidth + Padding.Horizontal, LineHeight + Padding.Vertical);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var content = ContentOf(bounds);
        int x = content.X + (content.Width - TotalWidth) / 2;
        if (HAlign == Align.Start) x = content.X;
        int y = content.Y + (content.Height - LineHeight) / 2 - (int)MathF.Round(Lift);
        int h = LineHeight;

        for (int i = 0; i < _pattern.Length; i++)
        {
            char c = _pattern[i];
            if (c == 'd')
            {
                DrawSlot(frame, x, y, h, _slotOf[i]);
                x += DigitWidth + _gap;
            }
            else if (c == ':')
            {
                DrawColon(frame, x, y, h);
                x += ColonWidth + _gap;
            }
            else x += GapWidth + _gap;
        }
    }

    private void DrawSlot(FrameBuffer frame, int x, int y, int h, int slot)
    {
        int digit = _values[slot];
        var cell = new Rectangle(x - 2, y - 1, DigitWidth + 4 + _scale, h + 2 + _scale);
        float p = _roll[slot].IsRunning ? _roll[slot].Value : 1f;
        if (p >= 1f)
        {
            if (digit >= 0) _glyphs[digit].Draw(frame, x, y, _scale, Paint);
            return;
        }

        frame.PushClip(cell);
        var paint = Paint;
        int travel = h / 2 + 2;
        if (_previous[slot] >= 0)
        {
            paint.Alpha = Paint.Alpha * (1f - p);
            _glyphs[_previous[slot]].Draw(frame, x, y - (int)MathF.Round(p * travel), _scale, paint);
        }
        paint.Alpha = Paint.Alpha * p;
        _glyphs[digit].Draw(frame, x, y + (int)MathF.Round((1f - p) * travel), _scale, paint);
        frame.PopClip();
    }

    private void DrawColon(FrameBuffer frame, int x, int y, int h)
    {
        int s = 2 * _scale;
        int bx = x + (ColonWidth - s) / 2;
        var top = Paint.Mode == PaintMode.Vertical ? Pixel.Lerp(Paint.A, Paint.B, 0.33f) : Paint.A;
        var bottom = Paint.Mode == PaintMode.Vertical ? Pixel.Lerp(Paint.A, Paint.B, 0.67f) : Paint.A;
        if (Paint.Mode == PaintMode.Rainbow) top = bottom = Pixel.FromHsv(Paint.HueBase + x * Paint.HueSpan, Paint.Sat, Paint.Val);
        float a = Paint.Alpha * ColonAlpha;
        if (Paint.Shadow) FillBlock(frame, bx + _scale, y + h / 3 - s / 2 + _scale, s, Paint.ShadowColor, a * 0.85f);
        if (Paint.Shadow) FillBlock(frame, bx + _scale, y + 2 * h / 3 - s / 2 + _scale, s, Paint.ShadowColor, a * 0.85f);
        FillBlock(frame, bx, y + h / 3 - s / 2, s, top, a);
        FillBlock(frame, bx, y + 2 * h / 3 - s / 2, s, bottom, a);
    }

    private static void FillBlock(FrameBuffer frame, int x, int y, int s, Pixel color, float alpha)
    {
        if (alpha >= 0.999f) frame.Fill(new Rectangle(x, y, s, s), color);
        else
            for (int j = 0; j < s; j++)
                for (int i = 0; i < s; i++)
                    frame.BlendPixel(x + i, y + j, color, alpha);
    }
}
