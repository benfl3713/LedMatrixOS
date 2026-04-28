using BdfFontParser;
using LedMatrixOS.Apps.Common;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps.Interrupts;

public class InterruptMessageRender : IDisposable
{
    private readonly string _message;
    private readonly int _maxWidth;
    private ScrollOverflowTextForFrameBuffer _scrollingText;
    private bool _finishedScroll = false;
    private DateTime? _firstRender;

    public InterruptMessageRender(string message, int maxWidth, BdfFont font, Pixel color)
    {
        _message = message;
        _maxWidth = maxWidth;

        // Center the text vertically on a 64px high display
        // The bottom Y should be: (display height / 2) + (font height / 2)
        var bottomY = (64 / 2) + (font.BoundingBox.Y / 2);

        // For scrolling text, leftX should be the left edge of the text area
        // Center the text area horizontally: (display width / 2) - (text area width / 2)
        var textAreaWidth = maxWidth * font.BoundingBox.X;
        var leftX = 64 + (256 / 2) - (textAreaWidth / 2);

        _scrollingText = new ScrollOverflowTextForFrameBuffer(leftX, bottomY, maxWidth - (int)Math.Round(32d / font.BoundingBox.X, MidpointRounding.AwayFromZero), font, color);
        _scrollingText.OnResetPosition += HandleTextReset;
    }

    public void Render(FrameBuffer frame)
    {
        _firstRender ??= DateTime.Now;
        for (int i = 0; i < 64; i += 4)
        {
            for (int y = 0; y < 64; y+=4)
            {
                frame.SetPixel(i, y, new Pixel(0, 180, 40));
            }
        }

        _scrollingText.Draw(frame, _message);
    }

    public bool HasFinished()
    {
        return _finishedScroll || (_scrollingText.TextFits is true && _firstRender.HasValue && DateTime.Now >  _firstRender.Value.AddSeconds(5));
    }

    public void Dispose()
    {
        _scrollingText.OnResetPosition -= HandleTextReset;
    }

    private void HandleTextReset(object? sender, EventArgs e)
    {
        _finishedScroll = true;
    }
}
