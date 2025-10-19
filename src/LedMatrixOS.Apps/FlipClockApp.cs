using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace LedMatrixOS.Apps;

/// <summary>
/// A clock app that uses flip card animations to display time
/// </summary>
public sealed class FlipClockApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "flip-clock";
    public override string Name => "Flip Clock";

    private FlipNumberDisplay? _hoursDisplay;
    private FlipNumberDisplay? _minutesDisplay;
    private FlipNumberDisplay? _secondsDisplay;
    private Font? _font;
    private DateTime _lastTime;
    private bool _showSeconds = true;
    private bool _show24Hour = true;
    private string _textColor = "White";
    private string _backgroundColor = "Black";

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        
        // Initialize flip displays
        _hoursDisplay = new FlipNumberDisplay(2);
        _minutesDisplay = new FlipNumberDisplay(2);
        _secondsDisplay = new FlipNumberDisplay(2);
        
        // Load font - much larger for 256x64 display
        try
        {
            _font = SystemFonts.CreateFont("Nimbus Sans", 32, FontStyle.Bold);
        }
        catch (Exception e)
        {
            var fontFamily = SystemFonts.Get("Arial");
            _font = fontFamily.CreateFont(32, FontStyle.Bold);
        }
        
        _lastTime = DateTime.Now;
        
        // Set initial time without animation
        UpdateTimeDisplays(false);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        var currentTime = DateTime.Now;
        
        // Check if time has changed
        if (_showSeconds)
        {
            if (currentTime.Second != _lastTime.Second)
            {
                UpdateTimeDisplays(true);
                _lastTime = currentTime;
            }
        }
        else
        {
            if (currentTime.Minute != _lastTime.Minute)
            {
                UpdateTimeDisplays(true);
                _lastTime = currentTime;
            }
        }
        
        // Update animations
        _hoursDisplay?.Update(deltaTime, 0.4f);
        _minutesDisplay?.Update(deltaTime, 0.4f);
        _secondsDisplay?.Update(deltaTime, 0.4f);
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_font == null || _hoursDisplay == null || _minutesDisplay == null || _secondsDisplay == null)
            return;

        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        image.Mutate(ctx =>
        {
            // Clear background
            ctx.Fill(GetBackgroundColor());
            
            var textColor = GetTextColor();
            var bgColor = GetBackgroundColor();
            
            // Card dimensions - much larger for 256x64 display
            int cardWidth = _showSeconds ? 28 : 38;  // Smaller cards if showing seconds
            int cardHeight = 50;
            int spacing = 4;
            int colonWidth = 8;
            
            // Calculate positions
            int totalWidth = _showSeconds 
                ? 6 * cardWidth + 5 * spacing + 2 * colonWidth  // HH:MM:SS
                : 4 * cardWidth + 3 * spacing + colonWidth;      // HH:MM
            
            int startX = (frame.Width - totalWidth) / 2;
            int startY = (frame.Height - cardHeight) / 2;
            
            // Render hours
            _hoursDisplay.Render(ctx, startX, startY, cardWidth, cardHeight, spacing, _font, textColor, bgColor);
            
            // Draw colon after hours
            int colonX = startX + 2 * cardWidth + 2 * spacing;
            DrawColon(ctx, colonX, startY, cardHeight, textColor);
            
            // Render minutes
            int minutesX = colonX + colonWidth + spacing;
            _minutesDisplay.Render(ctx, minutesX, startY, cardWidth, cardHeight, spacing, _font, textColor, bgColor);
            
            if (_showSeconds)
            {
                // Draw colon after minutes
                int colonX2 = minutesX + 2 * cardWidth + 2 * spacing;
                DrawColon(ctx, colonX2, startY, cardHeight, textColor);
                
                // Render seconds
                int secondsX = colonX2 + colonWidth + spacing;
                _secondsDisplay.Render(ctx, secondsX, startY, cardWidth, cardHeight, spacing, _font, textColor, bgColor);
            }
        });
        
        frame.RenderImage(image);
    }

    private void UpdateTimeDisplays(bool animate)
    {
        var time = DateTime.Now;
        int hours = _show24Hour ? time.Hour : (time.Hour % 12 == 0 ? 12 : time.Hour % 12);
        
        _hoursDisplay?.SetNumber(hours, animate);
        _minutesDisplay?.SetNumber(time.Minute, animate);
        _secondsDisplay?.SetNumber(time.Second, animate);
    }

    private void DrawColon(IImageProcessingContext ctx, int x, int y, int height, Color color)
    {
        int dotSize = 5;
        int dotSpacing = height / 3;
        
        // Top dot
        ctx.Fill(color, new RectangleF(x, y + dotSpacing - dotSize / 2, dotSize, dotSize));
        
        // Bottom dot
        ctx.Fill(color, new RectangleF(x, y + 2 * dotSpacing - dotSize / 2, dotSize, dotSize));
    }

    private Color GetTextColor()
    {
        return _textColor switch
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

    private Color GetBackgroundColor()
    {
        return _backgroundColor switch
        {
            "Red" => Color.Red,
            "Green" => Color.Green,
            "Blue" => Color.Blue,
            "Yellow" => Color.Yellow,
            "Cyan" => Color.Cyan,
            "Magenta" => Color.Magenta,
            "White" => Color.White,
            "DarkBlue" => Color.DarkBlue,
            "DarkGray" => Color.DarkGray,
            _ => Color.Black
        };
    }

    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("showSeconds", "Show Seconds", "Display seconds with flip animation", AppSettingType.Boolean, true, _showSeconds),
            new AppSetting("show24Hour", "24-Hour Format", "Use 24-hour format instead of 12-hour", AppSettingType.Boolean, true, _show24Hour),
            new AppSetting("textColor", "Text Color", "Color of the flip cards text", AppSettingType.Select, "White", _textColor, 
                Options: new[] { "White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta" }),
            new AppSetting("backgroundColor", "Background Color", "Color of the flip cards background", AppSettingType.Select, "Black", _backgroundColor, 
                Options: new[] { "Black", "DarkBlue", "DarkGray", "White" })
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "showSeconds":
                _showSeconds = Convert.ToBoolean(value);
                break;
            case "show24Hour":
                _show24Hour = Convert.ToBoolean(value);
                UpdateTimeDisplays(false);
                break;
            case "textColor":
                _textColor = value.ToString() ?? "White";
                break;
            case "backgroundColor":
                _backgroundColor = value.ToString() ?? "Black";
                break;
        }
    }
}
