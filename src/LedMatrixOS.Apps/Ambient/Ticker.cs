using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Ambient;

internal static class TickerIcons
{
    public static readonly string[] Names = ["None", "Star", "Heart", "Bolt", "Diamond", "Dot", "Arrow"];

    private static readonly string[] Star = ["...#...", "..###..", "#######", ".#####.", "..###..", ".##.##.", "##...##"];
    private static readonly string[] Heart = [".##.##.", "#######", "#######", "#######", ".#####.", "..###..", "...#..."];
    private static readonly string[] Bolt = ["....##.", "...##..", "..###..", ".######", "...##..", "..##...", ".##...."];
    private static readonly string[] Diamond = ["...#...", "..###..", ".#####.", "#######", ".#####.", "..###..", "...#..."];
    private static readonly string[] Dot = [".......", "..###..", ".#####.", ".#####.", ".#####.", "..###..", "......."];
    private static readonly string[] Arrow = ["##.....", ".##....", "..##...", "...##..", "..##...", ".##....", "##....."];

    public static string[]? Get(string name) => name switch
    {
        "Star" => Star,
        "Heart" => Heart,
        "Bolt" => Bolt,
        "Diamond" => Diamond,
        "Dot" => Dot,
        "Arrow" => Arrow,
        _ => null,
    };

    public static void Draw(FrameBuffer frame, string[] bitmap, int x, int y, int scale, Pixel color, Pixel shadow, float alpha)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            int off = pass == 0 ? Math.Max(1, scale / 2) : 0;
            var c = pass == 0 ? shadow : color;
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 7; col++)
                {
                    if (bitmap[row][col] != '#') continue;
                    int px = x + col * scale + off, py = y + row * scale + off;
                    if (alpha >= 0.999f) frame.Fill(new Rectangle(px, py, scale, scale), c);
                    else
                        for (int j = 0; j < scale; j++)
                            for (int i = 0; i < scale; i++)
                                frame.BlendPixel(px + i, py + j, c, alpha);
                }
        }
    }
}

/// <summary>
/// The scrolling-text ticker: a continuous loop (or single pass) of BDF text with colour effects, separators, a swap animation when the
/// message changes, and optional decoration (soft glow, or chasing marquee bulbs). Scroll position accumulates from frame deltas.
/// </summary>
internal sealed class Ticker : Node
{
    private readonly ScrollingTextApp _app;
    private readonly GlyphLine _line = new(), _outgoing = new();
    private readonly Tween<float> _swap = new(1f), _intro = new(0f);

    private readonly BdfFont _big = Fonts.Big, _small = Fonts.Small;
    private BdfFont _font;
    private int _scale = 1, _outScale = 1;
    private string _message = "\u0001";
    private string _appliedSeparator = "";
    private string[]? _icon;
    private float _offset, _outOffset;
    private float _t;
    private float _pause;
    private bool _first = true;
    private int _period;
    private int _sepPad, _sepScale;
    private bool _swapping;

    public Ticker(ScrollingTextApp app)
    {
        _app = app;
        _font = _big;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public int Scale => _scale;
    public int TextWidth => _line.Width * _scale;
    public float Offset => _offset;
    public bool IsSwapping => _swap.IsRunning;

    private void Pick(int fontSize, out BdfFont font, out int scale)
    {
        if (fontSize <= 12) { font = _small; scale = 1; }
        else if (fontSize <= 20) { font = _big; scale = 1; }
        else if (fontSize <= 32) { font = _big; scale = 2; }
        else { font = _big; scale = 3; }
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        var host = Host!;
        float dt = (float)Math.Min(ctx.Delta.TotalSeconds, 0.1);
        _t = (float)ctx.Time.TotalSeconds;

        Pick(_app.FontSize, out var font, out int scale);
        string message = string.IsNullOrEmpty(_app.Message) ? " " : _app.Message;
        string sep = _app.Separator ?? "None";
        bool changed = !string.Equals(message, _message, StringComparison.Ordinal) || !ReferenceEquals(font, _font) || scale != _scale;

        if (_first)
        {
            _font = font;
            _scale = scale;
            _message = message;
            _line.Set(font, message);
            host.Animator.Animate(_intro, 1f, TimeSpan.FromMilliseconds(700), Easing.OutCubic);
            _offset = -256f;
            _first = false;
        }
        else if (changed)
        {
            // Old text drops away and fades while the new text rises in from just below, with a little overshoot.
            _outgoing.Set(_font, _line.Text);
            _outScale = _scale;
            _outOffset = _offset;
            _font = font;
            _scale = scale;
            _message = message;
            _line.Set(font, message);
            _offset = -12f;
            _pause = 0f;
            _swap.Set(0f);
            host.Animator.Animate(_swap, 1f, TimeSpan.FromMilliseconds(520), Easing.OutBack);
            _swapping = true;
        }
        if (_swapping && !_swap.IsRunning) _swapping = false;

        if (!string.Equals(sep, _appliedSeparator, StringComparison.Ordinal))
        {
            _appliedSeparator = sep;
            _icon = TickerIcons.Get(sep);
        }

        int h = _line.Height * _scale;
        _sepScale = Math.Max(1, h / 9);
        _sepPad = _icon is null ? Math.Max(24, h) : Math.Max(8, h / 2);
        int sepWidth = _icon is null ? 0 : 7 * _sepScale;
        _period = TextWidth + _sepPad * 2 + sepWidth;

        float speed = Math.Clamp(_app.ScrollSpeed, 1, 400);
        if (_app.Loop)
        {
            _offset += speed * dt;
            if (_offset >= _period) _offset -= _period;
        }
        else if (_pause > 0f)
        {
            _pause -= dt;
            if (_pause <= 0f) _offset = -256f;
        }
        else
        {
            _offset += speed * dt;
            if (_offset >= TextWidth) _pause = 1.2f;
        }
    }

    private TextPaint MakePaint(Pixel color, int h, float alpha)
    {
        TextPaint p;
        switch (_app.TextEffect)
        {
            case "Gradient":
            {
                Gfx.ToHsv(color, out var hue, out var s, out var v);
                float drift = 14f * MathF.Sin(_t * 0.6f);
                p = TextPaint.Vertical(Gfx.Mix(color, Pixel.White, 0.4f), Gfx.Hsv(hue + 38f + drift, MathF.Max(s, 0.8f), MathF.Max(v, 0.9f)));
                break;
            }
            case "Rainbow":
            case "Rainbow Wave":
                p = TextPaint.Rainbow(_t * 90f, 1.25f / _scale, 0.9f, 1f);
                break;
            case "Fire":
            {
                float flick = 0.85f + 0.15f * Gfx.Noise1(_t * 14f);
                p = TextPaint.Vertical(Gfx.Dim(new Pixel(255, 235, 130), flick), Gfx.Dim(new Pixel(255, 50, 0), flick));
                break;
            }
            default:
                p = TextPaint.Solid(color);
                break;
        }

        if (_app.TextEffect is "Wave" or "Rainbow Wave")
        {
            p.WaveAmp = Math.Max(2f, h * 0.11f);
            p.WavePhase = _t * 5f;
            p.WaveStep = 0.55f;
        }
        p.Shadow = true;
        p.ShadowColor = new Pixel(0, 0, 0);
        p.Bold = string.Equals(_app.FontStyle, "Bold", StringComparison.Ordinal);
        p.Glow = 0.16f;
        p.Alpha = alpha;
        return p;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var color = NamedColors.Resolve(_app.TextColor, new Pixel(255, 40, 40));
        var bg = NamedColors.Background(_app.BackgroundColor);
        frame.Fill(bounds, bg);
        DrawDecor(frame, color);

        int h = _line.Height * _scale;
        int y = (bounds.Height - h) / 2;

        if (_swapping)
        {
            float p = _swap.Value;
            var op = MakePaint(color, _outgoing.Height * _outScale, Math.Max(0f, 1f - p * 1.6f));
            _outgoing.Draw(frame, (int)MathF.Round(-_outOffset), y + (int)MathF.Round(p * h * 0.8f), _outScale, op);
        }

        float rise = _swapping ? (1f - _swap.Value) * h * 0.9f : 0f;
        float alpha = Gfx.Saturate(_intro.Value) * (_swapping ? Gfx.Saturate(_swap.Value * 2f) : 1f);
        var paint = MakePaint(color, h, alpha);
        int yy = y + (int)MathF.Round(rise);

        if (_offset < 0f || !_app.Loop)
        {
            DrawOne(frame, (int)MathF.Round(-_offset), yy, h, paint, color);
        }
        else
        {
            int x = (int)MathF.Round(-_offset);
            // Copies follow each other, separated by the icon, until the right edge of the panel.
            for (int guard = 0; x < bounds.Width && guard < 40; guard++)
            {
                DrawOne(frame, x, yy, h, paint, color);
                x += _period;
            }
        }
    }

    private void DrawOne(FrameBuffer frame, int x, int y, int h, in TextPaint paint, Pixel color)
    {
        if (x + TextWidth + _sepPad + 8 * _sepScale < 0 || x > frame.Width) return;
        _line.Draw(frame, x, y, _scale, paint);
        if (_icon is not null && _app.Loop)
        {
            int ix = x + TextWidth + _sepPad;
            int iy = (frame.Height - 7 * _sepScale) / 2;
            var c = _app.TextEffect is "Rainbow" or "Rainbow Wave"
                ? Pixel.FromHsv(_t * 90f + ix * 1.25f, 0.9f, 1f)
                : Gfx.Mix(color, Pixel.White, 0.35f);
            TickerIcons.Draw(frame, _icon, ix, iy, _sepScale, c, paint.ShadowColor, paint.Alpha);
        }
    }

    private void DrawDecor(FrameBuffer frame, Pixel color)
    {
        string decor = _app.Decor;
        if (decor == "Edge Glow")
        {
            float pulse = 0.65f + 0.35f * MathF.Sin(_t * 1.4f);
            Gfx.GlowEllipse(frame, 128f, 32f, 170f, 28f, Gfx.Dim(color, 0.55f), 0.30f * pulse);
            for (int x = 0; x < 256; x++)
            {
                float u = (x / 256f + _t * 0.15f) % 1f;
                float k = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(u * MathF.PI * 2f));
                var c = Gfx.Dim(Gfx.Mix(color, Pixel.White, 0.2f), k * 0.55f);
                frame.SetPixel(x, 0, c);
                frame.SetPixel(x, 63, c);
            }
        }
        else if (decor == "Chase Lights")
        {
            int phase = (int)(_t * 9f);
            var lit = Gfx.Mix(color, Pixel.White, 0.5f);
            var dim = Gfx.Dim(color, 0.22f);
            for (int i = 0; i < 64; i++)
            {
                int x = i * 4 + 1;
                bool on = (i + phase) % 3 == 0;
                var c = on ? lit : dim;
                frame.Fill(new Rectangle(x, 0, 2, 2), c);
                frame.Fill(new Rectangle(255 - x - 1, 62, 2, 2), c);
                if (on) Gfx.GlowDisc(frame, x + 0.5f, 0.5f, 4f, color, 0.3f);
            }
            for (int j = 1; j < 15; j++)
            {
                int y = j * 4 + 1;
                bool on = (j + phase) % 3 == 0;
                var c = on ? lit : dim;
                frame.Fill(new Rectangle(0, y, 2, 2), c);
                frame.Fill(new Rectangle(254, 62 - y, 2, 2), c);
            }
        }
    }
}
