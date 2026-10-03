using SixLabors.ImageSharp;

namespace LedMatrixOS.Core.Overlays;

/// <summary>
/// An alert overlay is a full-screen message with a border/glow effect.
/// Used for urgent notifications that need immediate attention.
/// Auto-dismisses after a duration or can be manually dismissed.
/// </summary>
public sealed class AlertOverlay : OverlayBase
{
    private readonly Pixel _bgColor;
    private readonly Pixel _borderColor;
    private readonly bool _drawBorder;
    private readonly Action<FrameBuffer, FrameContext> _renderContent;

    public override string Kind => "alert";

    /// <summary>
    /// Create an alert overlay.
    /// renderContent: callback to draw the alert content (message, icon, etc.).
    /// drawBorder: if true, draws a colored border around the alert. bounds defaults to a 256x64 screen.
    /// </summary>
    public AlertOverlay(
        TimeSpan duration,
        Action<FrameBuffer, FrameContext> renderContent,
        Pixel? bgColor = null,
        Pixel? borderColor = null,
        bool drawBorder = true,
        int priority = 200,
        Rectangle? bounds = null,
        string id = "alert")
        : base(id, priority, bounds ?? new Rectangle(0, 0, 256, 64))
    {
        _bgColor = bgColor ?? Pixel.Black;
        _borderColor = borderColor ?? Pixel.White;
        _drawBorder = drawBorder;
        _renderContent = renderContent ?? ((_, _) => { });
        Duration = duration;
        TransitionDuration = TimeSpan.FromMilliseconds(200);
    }

    public override void Render(FrameBuffer frame, FrameContext context)
    {
        // Draw semi-transparent background
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                frame.BlendPixel(x, y, _bgColor, 0.8f);
            }
        }

        // Draw border if requested
        if (_drawBorder && frame.Width > 2 && frame.Height > 2)
        {
            // Top and bottom
            for (int x = 1; x < frame.Width - 1; x++)
            {
                frame.SetPixel(x, 0, _borderColor);
                frame.SetPixel(x, frame.Height - 1, _borderColor);
            }
            // Left and right
            for (int y = 1; y < frame.Height - 1; y++)
            {
                frame.SetPixel(0, y, _borderColor);
                frame.SetPixel(frame.Width - 1, y, _borderColor);
            }
        }

        // Draw custom content
        _renderContent(frame, context);
    }
}
