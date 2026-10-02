using SixLabors.ImageSharp;

namespace LedMatrixOS.Core.Overlays;

/// <summary>
/// A badge overlay is a small colored indicator, typically in a corner.
/// Examples: line disruption indicator, app attention badge.
/// Stays visible until manually dismissed (no auto-timeout).
/// </summary>
public sealed class BadgeOverlay : OverlayBase
{
    private readonly Pixel _color;
    private readonly bool _pulsing;
    private float _pulse;

    /// <summary>
    /// Create a badge overlay. If pulsing is true, opacity animates 0.5–1.0 at 2 Hz.
    /// </summary>
    public BadgeOverlay(
        string id,
        Rectangle bounds,
        Pixel color,
        bool pulsing = false,
        int priority = 50)
        : base(id, priority, bounds)
    {
        _color = color;
        _pulsing = pulsing;
        _pulse = 0.5f;
        // No auto-dismiss (Duration stays zero)
    }

    public override bool Update(TimeSpan deltaTime)
    {
        if (_pulsing)
        {
            // Pulse at 2 Hz (0.5 sec per cycle)
            _pulse += (float)(deltaTime.TotalMilliseconds / 500f);
            if (_pulse > 2f) _pulse -= 2f;
            // 0–1 ramp: 0.5 when pulse=0, 1.0 when pulse=1, 0.5 when pulse=2
            float opacity = 0.5f + 0.5f * (1f - Math.Abs(_pulse - 1f));
            // Update our opacity, but don't use the base class's opacity field
            // Instead, we multiply the final color by opacity
        }

        return base.Update(deltaTime);
    }

    public override void Render(FrameBuffer frame, FrameContext context)
    {
        var renderColor = _color;
        if (_pulsing)
        {
            _pulse += (float)(context.Delta.TotalMilliseconds / 500f);
            if (_pulse > 2f) _pulse -= 2f;
            float pulse = 0.5f + 0.5f * (1f - Math.Abs(_pulse - 1f));
            renderColor = _color.WithBrightness(pulse);
        }

        // Draw a small filled rectangle
        frame.Fill(new Rectangle(0, 0, Bounds.Width, Bounds.Height), renderColor);
    }
}
