using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Clocks;

/// <summary>
/// ClockApp backdrop: a slow colour field in the palette's dark tones, brighter in a band behind the digits, with a soft
/// light beam drifting across and a few dim motes floating by. Runs one pass over the frame (no allocation).
/// </summary>
internal sealed class AmbientBackdrop : Node
{
    private const int MoteCount = 18;
    private readonly ClockState _state;
    private readonly LiveTheme _theme;
    private readonly float[] _mx = new float[MoteCount], _my = new float[MoteCount], _mv = new float[MoteCount], _mp = new float[MoteCount];
    private int[] _cr = [], _cg = [], _cb = [];
    private float[] _rowF = [];
    private float _time;

    public AmbientBackdrop(ClockState state, LiveTheme theme)
    {
        _state = state;
        _theme = theme;
        var rng = new Random(11);
        for (int i = 0; i < MoteCount; i++)
        {
            _mx[i] = rng.NextSingle() * 256f;
            _my[i] = rng.NextSingle() * 64f;
            _mv[i] = 2f + rng.NextSingle() * 5f;
            _mp[i] = rng.NextSingle() * 6.28f;
        }
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    /// <summary>Overall brightness of the field (the digits sit on top, so keep it modest).</summary>
    public float Level { get; set; } = 1f;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        float dt = (float)ctx.Delta.TotalSeconds;
        _time = (float)_state.SecondsOfDay;
        for (int i = 0; i < MoteCount; i++)
        {
            _mx[i] -= _mv[i] * dt;
            if (_mx[i] < -2f) { _mx[i] = 258f; }
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int w = bounds.Width, h = bounds.Height;
        if (_cr.Length != w) { _cr = new int[w]; _cg = new int[w]; _cb = new int[w]; }
        if (_rowF.Length != h)
        {
            _rowF = new float[h];
            for (int y = 0; y < h; y++)
            {
                float d = MathF.Abs(y - (h - 1) / 2f) / (h / 2f);
                _rowF[y] = 0.22f + 0.78f * (1f - d * d);
            }
        }

        float t = _time;
        var a = _theme.BackA;
        var b = _theme.BackB;
        float breathe = (0.8f + 0.2f * MathF.Sin(t * 0.5f)) * Level;
        for (int x = 0; x < w; x++)
        {
            float mix = 0.5f + 0.5f * MathF.Sin(t * 0.13f + x * 0.021f);
            _cr[x] = (int)((a.R + (b.R - a.R) * mix) * breathe * 256f);
            _cg[x] = (int)((a.G + (b.G - a.G) * mix) * breathe * 256f);
            _cb[x] = (int)((a.B + (b.B - a.B) * mix) * breathe * 256f);
        }

        float beam = (t * 16f) % (w + 160f) - 80f;
        for (int y = 0; y < h; y++)
        {
            float rf = _rowF[y];
            float slope = (y - h / 2f) * 0.8f;
            for (int x = 0; x < w; x++)
            {
                float d = MathF.Abs(x - beam - slope) * (1f / 46f);
                float boost = d < 1f ? (1f - d) * (1f - d) * 0.55f : 0f;
                float f = rf * (1f + boost) * (1f / 256f);
                frame.SetPixel(bounds.X + x, bounds.Y + y,
                    new Pixel(ClampByte(_cr[x] * f), ClampByte(_cg[x] * f), ClampByte(_cb[x] * f)));
            }
        }

        var accent = _theme.Accent;
        for (int i = 0; i < MoteCount; i++)
        {
            float tw = 0.5f + 0.5f * MathF.Sin(t * 1.7f + _mp[i]);
            float yy = _my[i] + MathF.Sin(t * 0.4f + _mp[i]) * 3f;
            frame.BlendPixel((int)_mx[i], (int)yy, accent, 0.12f + 0.38f * tw * Level);
        }
    }

    private static byte ClampByte(float v) => v <= 0f ? (byte)0 : v >= 255f ? (byte)255 : (byte)v;
}

/// <summary>A full-width seconds sweep along the bottom edge with a glowing head, minute ticks and a flash when the minute turns.</summary>
internal sealed class SecondsSweep : Node
{
    private readonly ClockState _state;
    private readonly LiveTheme _theme;
    private readonly Tween<float> _flash = new(0f);
    private int _lastSecond = -1;

    public SecondsSweep(ClockState state, LiveTheme theme)
    {
        _state = state;
        _theme = theme;
        Height = 3;
        HAlign = Align.Stretch;
        VAlign = Align.End;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        int s = _state.Second;
        if (_lastSecond >= 0 && s < _lastSecond && Host is { } host)
        {
            _flash.Set(1f);
            host.Animator.Animate(_flash, 0f, TimeSpan.FromMilliseconds(900), Easing.OutQuad);
        }
        _lastSecond = s;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int w = bounds.Width;
        float progress = (_state.Second + _state.SecFrac) / 60f;
        float head = progress * w;
        float flash = _flash.Value;
        var accent = _theme.Accent;

        // Track and minute-fraction ticks (every 5 seconds).
        for (int x = 0; x < w; x++)
        {
            for (int row = 1; row < bounds.Height; row++)
                frame.BlendPixel(bounds.X + x, bounds.Y + row, accent, 0.13f);
        }
        for (int i = 0; i <= 12; i++)
        {
            int tx = Math.Min(w - 1, i * w / 12);
            frame.BlendPixel(bounds.X + tx, bounds.Y, accent, i % 3 == 0 ? 0.75f : 0.4f);
        }

        int filled = (int)head;
        for (int x = 0; x < filled; x++)
        {
            float u = x / (float)Math.Max(1, w - 1);
            var c = u < 0.5f ? Pixel.Lerp(_theme.G0, _theme.G1, u * 2f) : Pixel.Lerp(_theme.G1, _theme.G2, (u - 0.5f) * 2f);
            if (flash > 0f) c = Pixel.Lerp(c, Pixel.White, flash * 0.8f);
            frame.SetPixel(bounds.X + x, bounds.Y + 1, c);
            frame.SetPixel(bounds.X + x, bounds.Y + 2, c);
            frame.BlendPixel(bounds.X + x, bounds.Y, c, 0.55f);
        }

        // Glowing head.
        const int glow = 9;
        for (int i = 1; i <= glow; i++)
        {
            float k = 1f - i / (float)(glow + 1);
            int x = filled - i;
            for (int row = 0; row < bounds.Height; row++)
                frame.BlendPixel(bounds.X + x, bounds.Y + row, Pixel.White, k * k * 0.75f);
        }
        for (int row = 0; row < bounds.Height; row++)
        {
            frame.SetPixel(bounds.X + filled, bounds.Y + row, Pixel.White);
            frame.BlendPixel(bounds.X + filled + 1, bounds.Y + row, Pixel.White, 0.4f);
        }
    }
}
