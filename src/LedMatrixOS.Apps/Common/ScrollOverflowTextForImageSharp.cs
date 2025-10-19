using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps.Common;

public class ScrollOverflowTextForImageSharp
{
    private readonly int _x, _y, _width;
    private readonly Font _font;
    private double _scrollOffset = 0;

    public ScrollOverflowTextForImageSharp(int x, int y, int width, Font font)
    {
        _x = x;
        _y = y;
        _font = font;
        var sample = new string('W', 1);
        _width = (int)Math.Ceiling(TextMeasurer.MeasureSize(sample, new TextOptions(_font)).Width) * width;
    }

    public void Draw(IImageProcessingContext? ctx, string text)
    {
        if (ctx == null || string.IsNullOrEmpty(text)) return;

        var size = TextMeasurer.MeasureSize(text, new TextOptions(_font));
        int textWidth = (int)Math.Ceiling(size.Width);
        int textHeight = (int)Math.Ceiling(size.Height);
        int visibleWidth = _width;
        int visibleHeight = textHeight;

        // Create a temporary image to render the full text
        using (var tempImg = new Image<Rgba32>(textWidth, textHeight))
        {
            tempImg.Mutate(tempCtx =>
            {
                tempCtx.SetGraphicsOptions(new GraphicsOptions { Antialias = true});
                var textOptions = new RichTextOptions(_font) { Origin = new PointF(0, 0) };
                tempCtx.DrawText(textOptions, text, Color.White);
            });

            if (textWidth > visibleWidth)
            {
                _scrollOffset += 1.0;
                float maxOffset = textWidth - visibleWidth;
                float offset = (float)(_scrollOffset % (maxOffset + 20)); // 20px padding before looping
                int xOffset = (int)Math.Floor(offset);
                // Clamp xOffset to valid range
                if (xOffset < 0) xOffset = 0;
                if (xOffset > maxOffset) xOffset = (int)maxOffset;
                // Blit the visible region from tempImg to ctx
                ctx.DrawImage(tempImg.Clone(i => i.Crop(new Rectangle(xOffset, 0, visibleWidth, visibleHeight))), new Point(_x, _y), 1f);
            }
            else
            {
                // Center or left-align if not scrolling
                ctx.DrawImage(tempImg.Clone(i => i.Crop(new Rectangle(0, 0, textWidth, visibleHeight))), new Point(_x, _y), 1f);
            }
        }
    }
}
