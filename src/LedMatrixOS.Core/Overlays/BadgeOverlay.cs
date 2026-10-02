using SixLabors.ImageSharp;

namespace LedMatrixOS.Core.Overlays;

/// <summary>
/// A small solid-colour indicator (e.g. a disruption marker in a corner). Stays until dismissed.
/// </summary>
public sealed class BadgeOverlay : OverlayBase
{
    private readonly Pixel _color;
    private readonly bool _pulsing;

    public BadgeOverlay(string id, Rectangle bounds, Pixel color, bool pulsing = false, int priority = 50)
        : base(id, priority, bounds)
    {
        _color = color;
        _pulsing = pulsing;
    }

    public override void Render(FrameBuffer frame, FrameContext context)
    {
        var color = _color;
        if (_pulsing)
        {
            // 1 Hz triangle wave between 40% and 100% brightness, driven by frame time so it is deterministic
            float phase = (float)(context.Time.TotalSeconds % 1.0);
            float tri = 1f - Math.Abs(phase * 2f - 1f);
            color = _color.WithBrightness(0.4f + 0.6f * tri);
        }

        frame.Fill(new Rectangle(0, 0, Bounds.Width, Bounds.Height), color);
    }
}
