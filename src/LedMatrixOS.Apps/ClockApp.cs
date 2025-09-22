using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;

namespace LedMatrixOS.Apps;

public sealed class ClockApp : MatrixAppBase
{
    public override string Id => "clock";
    public override string Name => "Clock";

    private double _timeAccumulator;
    private Font? _font;
    private (float x, float y) _clockCenter;

    public override Task OnActivatedAsync((int height, int width) dimensions, CancellationToken cancellationToken)
    {
        // Load a default system font for rendering
        try
        {
            // var systemFonts = SystemFonts.Families.ToList();
            // if (systemFonts.Count > 0)
            // {
            //     Console.WriteLine("Using system font: " + systemFonts[0].Name);
            //     _font = systemFonts[0].CreateFont(48); // Small font size for LED matrix
            // }
            // else
            {
                _font = SystemFonts.CreateFont("Nimbus Roman", 48);
            }
        }
        catch
        {
            // Fallback if no system fonts are available
            _font = SystemFonts.CreateFont("Arial", 48);
        }
        
        // Measure the text to calculate centered position
        var textOptions = new TextOptions(_font);
        var textBounds = TextMeasurer.MeasureBounds(DateTime.Now.ToString("HH:mm:ss"), textOptions);
    
        // Calculate centered position
        _clockCenter.x = (dimensions.width - textBounds.Width) / 2;
        _clockCenter.y = (dimensions.height - textBounds.Height) / 2;

        // Example of background loading that should not block frames
        RunInBackground(async ct =>
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
        });
        return base.OnActivatedAsync(dimensions, cancellationToken);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        _timeAccumulator += deltaTime.TotalSeconds;
        // Update could be used for time-based animations if needed
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_font == null) return;

        // Get current time and format it
        var now = DateTime.Now;
        var timeString = now.ToString("HH:mm:ss");

        // Create an image to draw on using SixLabors.ImageSharp
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        Star star = new(x: 20.0f, y: 20.0f, prongs: 5, innerRadii: 4.0f, outerRadii:12.0f);
        Star star2 = new(x: frame.Width - 20.0f, y: 20.0f, prongs: 5, innerRadii: 4.0f, outerRadii:12.0f);

        PatternBrush brush = Brushes.Percent10(Color.Red, Color.Blue);
        Pen pen = Pens.Solid(brush);
        
        // Draw the time text
        image.Mutate(ctx =>
        {
            //ctx.Fill(new Rgb24(40, 40, 40));
            // Draw shadow first (offset by 2 pixels down and right)
            //ctx.DrawText(new RichTextOptions(_font){Origin = new PointF(centerX + 2, centerY + 2)}, timeString, Color.Black);
            
            // Draw main text on top
            ctx.DrawText(new RichTextOptions(_font){Origin = new PointF(_clockCenter.x, _clockCenter.y)}, timeString, brush, pen);
            ctx.Fill(Color.Yellow, star);
            ctx.Fill(Color.Yellow, star2);
        });
        
        frame.RenderImage(image);
    }
}
