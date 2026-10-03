using LedMatrixOS.Core;
using LedMatrixOS.Core.Overlays;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>Builds the full-screen alert overlays used by the notification endpoints.</summary>
public static class AlertFactory
{
    private const double ScrollPixelsPerSecond = 60;

    /// <summary>Solid red flash with no text.</summary>
    public static AlertOverlay Flash(int width, int height, TimeSpan duration, Pixel? color = null)
    {
        var fill = color ?? new Pixel(200, 0, 0);
        var alert = new AlertOverlay(
            duration,
            (frame, _) => frame.Fill(new Rectangle(0, 0, width, height), fill),
            drawBorder: false,
            id: NewId());
        alert.Bounds = new Rectangle(0, 0, width, height);
        alert.Text = null;
        return alert;
    }

    /// <summary>
    /// Centred message. Text that fits stays for 5 seconds; wider text scrolls across once, driven by frame time.
    /// </summary>
    public static AlertOverlay Message(string message, Pixel color, int width, int height)
    {
        var font = Fonts.Big.Scale(2);
        var run = new TextRun();
        run.Set(font, message);
        int y = Math.Max(0, (height - run.Height) / 2);
        bool scrolls = run.Width > width - 8;

        var duration = scrolls
            ? TimeSpan.FromSeconds((run.Width + width) / ScrollPixelsPerSecond + 0.5)
            : TimeSpan.FromSeconds(5);

        TimeSpan? start = null;
        var alert = new AlertOverlay(
            duration,
            (frame, ctx) =>
            {
                start ??= ctx.Time;
                int x = scrolls
                    ? width - (int)((ctx.Time - start.Value).TotalSeconds * ScrollPixelsPerSecond)
                    : (width - run.Width) / 2;
                run.Draw(frame, x, y, color);
            },
            borderColor: color,
            id: NewId());
        alert.Bounds = new Rectangle(0, 0, width, height);
        alert.Text = message;
        return alert;
    }

    private static string NewId() => "alert-" + Guid.NewGuid().ToString("N")[..8];
}
