using SixLabors.ImageSharp;

namespace LedMatrixOS.Core.Overlays;

/// <summary>
/// A toast overlay is a colored banner that appears at the top/bottom of the screen.
/// It auto-dismisses after a duration. Actual text rendering is delegated to the caller
/// via RenderContent callback to avoid Core depending on Graphics.Text.
/// </summary>
public sealed class ToastOverlay : OverlayBase
{
    private readonly Pixel _bgColor;
    private readonly Action<FrameBuffer, FrameContext> _renderContent;

    /// <summary>
    /// Create a toast that auto-dismisses.
    /// renderContent: called to draw the toast content (typically a message or icon).
    /// </summary>
    public ToastOverlay(
        TimeSpan duration,
        Action<FrameBuffer, FrameContext> renderContent,
        Pixel? bgColor = null,
        Rectangle? bounds = null)
        : base("toast", priority: 100, bounds: bounds ?? new Rectangle(0, 0, 256, 8))
    {
        _bgColor = bgColor ?? Pixel.White;
        _renderContent = renderContent ?? ((_, _) => { });
        Duration = duration;
        TransitionDuration = TimeSpan.FromMilliseconds(150);
    }

    public override string Kind => "toast";

    public override void Render(FrameBuffer frame, FrameContext context)
    {
        // Draw background
        frame.Fill(new Rectangle(0, 0, Bounds.Width, Bounds.Height), _bgColor);

        // Draw custom content
        _renderContent(frame, context);
    }
}
