using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;

namespace LedMatrixOS.Apps;

public sealed class ClockApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "clock";
    public override string Name => "Clock";

    private double _timeAccumulator;
    private Font? _font;
    private (float x, float y) _clockCenter;
    
    // Configuration settings
    private bool _showSeconds = true;
    private bool _show24Hour = true;
    private string _timeColor = "White";

    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("showSeconds", "Show Seconds", "Display seconds in the time format", AppSettingType.Boolean, true, _showSeconds),
            new AppSetting("show24Hour", "24-Hour Format", "Use 24-hour format instead of 12-hour", AppSettingType.Boolean, true, _show24Hour),
            new AppSetting("timeColor", "Time Color", "Color of the time display", AppSettingType.Select, "White", _timeColor, Options: new[] { "White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta" })
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "showSeconds":
                _showSeconds = Convert.ToBoolean(value.ToString());
                break;
            case "show24Hour":
                _show24Hour = Convert.ToBoolean(value.ToString());
                break;
            case "timeColor":
                _timeColor = value.ToString() ?? "White";
                break;
        }
    }

    private Color GetTimeColor()
    {
        return _timeColor switch
        {
            "Red" => Color.Red,
            "Green" => Color.Green,
            "Blue" => Color.Blue,
            "Yellow" => Color.Yellow,
            "Cyan" => Color.Cyan,
            "Magenta" => Color.Magenta,
            _ => Color.White
        };
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
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
                _font = SystemFonts.CreateFont("Nimbus Sans", 48);
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
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        _timeAccumulator += deltaTime.TotalSeconds;
        // Update could be used for time-based animations if needed
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_font == null) return;

        // Get current time and format it based on settings
        var now = DateTime.Now;
        var formatString = _show24Hour ? "HH:mm" : "hh:mm tt";
        if (_showSeconds)
        {
            formatString = _show24Hour ? "HH:mm:ss" : "hh:mm:ss tt";
        }
        var timeString = now.ToString(formatString);

        // Create an image to draw on using SixLabors.ImageSharp
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        Star star = new(x: 20.0f, y: 20.0f, prongs: 5, innerRadii: 4.0f, outerRadii:12.0f);
        Star star2 = new(x: frame.Width - 20.0f, y: 20.0f, prongs: 5, innerRadii: 4.0f, outerRadii:12.0f);

        var timeColor = GetTimeColor();
        SolidBrush brush = Brushes.Solid(timeColor);
        Pen pen = Pens.Solid(brush);
        
        // Draw the time text
        image.Mutate(ctx =>
        {
            // Draw main text on top
            ctx.DrawText(new RichTextOptions(_font){Origin = new PointF(_clockCenter.x, _clockCenter.y)}, timeString, brush, pen);
            ctx.Fill(Color.Yellow, star);
            ctx.Fill(Color.Yellow, star2);
        });
        
        frame.RenderImage(image);
    }
}
