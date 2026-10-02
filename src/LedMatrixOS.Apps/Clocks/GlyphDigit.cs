using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Clocks;

internal enum DigitRoll
{
    /// <summary>The old digit is pushed up and out while the new one rises in from below.</summary>
    Slide,
    /// <summary>The new digit drops in from above and bounces; the old one fades.</summary>
    Drop,
}

/// <summary>
/// One big anti-aliased glyph with a soft halo. Changing the value rolls it (see <see cref="DigitRoll"/>), and an optional
/// entrance makes it rise into place after a delay. Colours come from a vertical two-colour gradient. No per-frame allocations.
/// </summary>
internal class GlyphDigit : Node
{
    private readonly GlyphAtlas _atlas;
    private readonly Func<int> _source;
    private readonly Tween<float> _roll = new(1f);
    private readonly Tween<float> _enter = new(1f);
    private readonly Pixel[] _rowColors;
    private int _applied = -2;
    private int _previous = -1;
    private float _enterDelay;
    private bool _enterPending;

    public GlyphDigit(GlyphAtlas atlas, Func<int> source)
    {
        _atlas = atlas;
        _source = source;
        _rowColors = new Pixel[atlas.Rows];
        Width = atlas.Stride;
        Height = atlas.Rows;
        ClipChildren = true;
    }

    public LiveTheme? Theme { get; set; }
    public Pixel Top { get; set; } = Pixel.White;
    public Pixel Bottom { get; set; } = new(200, 200, 200);
    public Pixel Halo { get; set; } = Pixel.White;
    public float HaloAlpha { get; set; } = 0.35f;
    /// <summary>Dark underlay (the halo mask in black) that keeps the digit readable over busy backgrounds.</summary>
    public float BackingAlpha { get; set; }
    public DigitRoll Roll { get; set; } = DigitRoll.Slide;
    public TimeSpan RollDuration { get; set; } = TimeSpan.FromMilliseconds(480);
    public bool IsRolling => _roll.IsRunning;
    public int Value => _applied;

    /// <summary>Raised the moment a digit starts changing (not for the first value).</summary>
    public Action<GlyphDigit>? Changed { get; set; }

    /// <summary>Hidden until <paramref name="delay"/> has passed, then rises into place.</summary>
    public void StartEntrance(TimeSpan delay)
    {
        _enter.Set(0f);
        _enterDelay = (float)delay.TotalSeconds;
        _enterPending = true;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        if (_enterPending)
        {
            _enterDelay -= (float)ctx.Delta.TotalSeconds;
            if (_enterDelay <= 0f)
            {
                _enterPending = false;
                if (Host is { } host) host.Animator.Animate(_enter, 1f, TimeSpan.FromMilliseconds(700), Easing.OutBack);
                else _enter.Set(1f);
            }
        }

        int value = _source();
        if (value != _applied)
        {
            bool first = _applied == -2;
            _previous = first ? -1 : _applied;
            _applied = value;
            _roll.Set(0f);
            if (!first && Host is { } host)
            {
                host.Animator.Animate(_roll, 1f, RollDuration, Roll == DigitRoll.Slide ? Easing.InOutCubic : Easing.Linear);
                Changed?.Invoke(this);
            }
            else _roll.Set(1f);
        }

        ResolveColors();
    }

    protected virtual void ResolveColors()
    {
        Pixel top = Top, bottom = Bottom;
        if (Theme is { } t)
        {
            top = t.Top;
            bottom = t.Bottom;
            Halo = Pixel.Lerp(top, bottom, 0.5f);
        }
        FillGradient(top, bottom);
    }

    protected void FillGradient(Pixel top, Pixel bottom)
    {
        int rows = _rowColors.Length;
        for (int y = 0; y < rows; y++)
            _rowColors[y] = Pixel.Lerp(top, bottom, rows <= 1 ? 0f : y / (float)(rows - 1));
    }

    protected Pixel[] RowColors => _rowColors;

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float enter = _enter.Value;
        if (enter <= 0.001f && (_enterPending || _enter.IsRunning)) return;

        int ox = bounds.X + (bounds.Width - _atlas.Stride) / 2;
        int oy = bounds.Y + (bounds.Height - _atlas.Rows) / 2;
        // OutBack overshoots past 1, which lifts the digit slightly above its final spot and settles.
        oy += (int)MathF.Round((1f - enter) * (_atlas.Rows + 6));
        float baseAlpha = Math.Clamp(enter * 2f, 0f, 1f);
        int rows = _atlas.Rows, stride = _atlas.Stride;

        if (!_roll.IsRunning || _previous < 0)
        {
            DrawGlyph(frame, _applied, ox, oy, baseAlpha, 0f);
            return;
        }

        float p = _roll.Value;
        if (Roll == DigitRoll.Slide)
        {
            int dist = rows;
            DrawGlyph(frame, _previous, ox, oy - (int)MathF.Round(p * dist), baseAlpha * (1f - p), 0f);
            DrawGlyph(frame, _applied, ox, oy + (int)MathF.Round((1f - p) * dist), baseAlpha * MathF.Min(1f, p * 1.8f), 0f);
        }
        else
        {
            DrawGlyph(frame, _previous, ox, oy, baseAlpha * MathF.Max(0f, 1f - p * 5f), 0f);
            float fall = 1f - Easing.OutBounce(p);
            int y = oy - (int)MathF.Round(fall * (rows + 4));
            DrawGlyph(frame, _applied, ox, y, baseAlpha * MathF.Min(1f, p * 6f), (1f - p) * 0.7f);
        }
    }

    private void DrawGlyph(FrameBuffer frame, int digit, int x, int y, float alpha, float whiten)
    {
        if (digit < 0 || digit > 9 || alpha <= 0f) return;
        int stride = _atlas.Stride, rows = _atlas.Rows;
        if (BackingAlpha > 0f) MaskDraw.Blit(frame, _atlas.Halo[digit], stride, rows, x, y, Pixel.Black, Math.Min(1f, BackingAlpha * 1.4f) * alpha);
        if (HaloAlpha > 0f) MaskDraw.Blit(frame, _atlas.Halo[digit], stride, rows, x, y, Halo, HaloAlpha * alpha);

        var colors = _rowColors;
        if (whiten > 0f)
        {
            // Landing flash: a brief white-hot tint, applied through a second pass of the same mask.
            MaskDraw.BlitRows(frame, _atlas.Ink[digit], stride, rows, x, y, colors, alpha);
            MaskDraw.Blit(frame, _atlas.Ink[digit], stride, rows, x, y, Pixel.White, alpha * whiten);
        }
        else MaskDraw.BlitRows(frame, _atlas.Ink[digit], stride, rows, x, y, colors, alpha);
    }
}

/// <summary>Two round dots with a soft halo, pulsing with the second fraction.</summary>
internal sealed class ColonNode : Node
{
    private readonly ClockState _state;
    private readonly Func<Pixel> _color;
    private readonly byte[] _mask, _glow;
    private readonly int _size, _glowSize;
    private readonly int _gapHalf;

    public ColonNode(ClockState state, Func<Pixel> color, int height, float radius, int pad = 3)
    {
        _state = state;
        _color = color;
        (_mask, _size) = DotMask.Get(radius, pad);
        (_glow, _glowSize) = DotMask.GetSoft(radius, 3);
        _gapHalf = height / 4;
        Width = _size + 2;
        Height = height;
    }

    /// <summary>0 = steady, 1 = heartbeat pulse on the second.</summary>
    public float Pulse { get; set; } = 1f;
    public bool Blink { get; set; }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float f = _state.SecFrac;
        // Bright on the tick, easing down to a dimmer glow over the second.
        float beat = 1f - Easing.OutCubic(Math.Min(1f, f * 1.4f));
        float level = 1f - Pulse * 0.4f * (1f - beat);
        if (Blink && f > 0.5f) level *= 0.25f;

        int cx = bounds.X + (bounds.Width - _size) / 2;
        int cy = bounds.Y + bounds.Height / 2;
        var colour = _color();
        var core = Pixel.Lerp(colour, Pixel.White, 0.35f);
        for (int i = 0; i < 2; i++)
        {
            int y = cy + (i == 0 ? -_gapHalf : _gapHalf) - _size / 2;
            MaskDraw.Blit(frame, _glow, _glowSize, _glowSize, cx - (_glowSize - _size) / 2, y - (_glowSize - _size) / 2, colour, 0.3f * level);
            MaskDraw.Blit(frame, _mask, _size, _size, cx, y, i == 0 ? core : colour, level);
        }
    }
}
