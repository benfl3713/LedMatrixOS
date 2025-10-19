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
/// A countdown timer with flip card animation - great for events, workouts, cooking, etc.
/// </summary>
public sealed class CountdownTimerApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "countdown-timer";
    public override string Name => "Countdown Timer";

    private FlipNumberDisplay? _minutesDisplay;
    private FlipNumberDisplay? _secondsDisplay;
    private Font? _font;
    private Font? _labelFont;
    private TimeSpan _remainingTime;
    private TimeSpan _initialTime;
    private bool _isRunning;
    private bool _isPaused;
    private bool _hasCompleted;
    
    // Settings
    private int _durationMinutes = 5;
    private string _textColor = "Cyan";
    private string _backgroundColor = "Black";
    private bool _autoRestart = false;

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        
        _minutesDisplay = new FlipNumberDisplay(2);
        _secondsDisplay = new FlipNumberDisplay(2);

        FontFamily fontFamily;
        try
        {
            fontFamily = SystemFonts.Get("Nimbus Sans");
        }
        catch (Exception e)
        {
            fontFamily = SystemFonts.Get("Arial");
        }
        _font = fontFamily.CreateFont(32, FontStyle.Bold);
        _labelFont = fontFamily.CreateFont(10, FontStyle.Regular);
        
        _initialTime = TimeSpan.FromMinutes(_durationMinutes);
        _remainingTime = _initialTime;
        _isRunning = true;
        _isPaused = false;
        _hasCompleted = false;
        
        UpdateDisplays(false);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        if (_isRunning && !_isPaused && !_hasCompleted)
        {
            _remainingTime -= deltaTime;
            
            if (_remainingTime <= TimeSpan.Zero)
            {
                _remainingTime = TimeSpan.Zero;
                _hasCompleted = true;
                
                if (_autoRestart)
                {
                    Task.Delay(1000).ContinueWith(_ =>
                    {
                        _remainingTime = _initialTime;
                        _hasCompleted = false;
                        UpdateDisplays(true);
                    });
                }
            }
            
            UpdateDisplays(true);
        }
        
        _minutesDisplay?.Update(deltaTime, 0.3f);
        _secondsDisplay?.Update(deltaTime, 0.3f);
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_font == null || _labelFont == null || _minutesDisplay == null || _secondsDisplay == null)
            return;

        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        image.Mutate(ctx =>
        {
            ctx.Fill(GetBackgroundColor());
            
            var textColor = GetTextColor();
            var bgColor = GetBackgroundColor();
            
            // Larger cards for countdown timer
            int cardWidth = 35;
            int cardHeight = 50;
            int spacing = 4;
            int colonWidth = 10;
            
            int totalWidth = 4 * cardWidth + 3 * spacing + colonWidth;
            int startX = (frame.Width - totalWidth) / 2;
            int startY = (frame.Height - cardHeight) / 2 - 2;
            
            // Render minutes
            _minutesDisplay.Render(ctx, startX, startY, cardWidth, cardHeight, spacing, _font, textColor, bgColor);
            
            // Draw colon
            int colonX = startX + 2 * cardWidth + 2 * spacing;
            DrawColon(ctx, colonX, startY, cardHeight, textColor);
            
            // Render seconds
            int secondsX = colonX + colonWidth + spacing;
            _secondsDisplay.Render(ctx, secondsX, startY, cardWidth, cardHeight, spacing, _font, textColor, bgColor);
            
            // Draw labels
            var labelColor = _hasCompleted ? Color.Red : textColor;
            var labelText = _hasCompleted ? "TIME'S UP!" : (_isPaused ? "PAUSED" : "");
            
            if (!string.IsNullOrEmpty(labelText))
            {
                var labelOptions = new RichTextOptions(_labelFont)
                {
                    Origin = new PointF(frame.Width / 2f, 5),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };
                ctx.DrawText(labelOptions, labelText, labelColor);
            }
        });
        
        frame.RenderImage(image);
    }

    private void UpdateDisplays(bool animate)
    {
        int totalSeconds = (int)Math.Ceiling(_remainingTime.TotalSeconds);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        
        _minutesDisplay?.SetNumber(minutes, animate);
        _secondsDisplay?.SetNumber(seconds, animate);
    }

    private void DrawColon(IImageProcessingContext ctx, int x, int y, int height, Color color)
    {
        int dotSize = 6;
        int dotSpacing = height / 3;
        
        ctx.Fill(color, new RectangleF(x + 2, y + dotSpacing - dotSize / 2, dotSize, dotSize));
        ctx.Fill(color, new RectangleF(x + 2, y + 2 * dotSpacing - dotSize / 2, dotSize, dotSize));
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
            "Orange" => Color.Orange,
            _ => Color.White
        };
    }

    private Color GetBackgroundColor()
    {
        return _backgroundColor switch
        {
            "DarkBlue" => Color.DarkBlue,
            "DarkGray" => Color.DarkGray,
            "White" => Color.White,
            _ => Color.Black
        };
    }

    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("durationMinutes", "Duration (Minutes)", "How long to count down from", AppSettingType.Integer, 5, _durationMinutes, MinValue: 1, MaxValue: 99),
            new AppSetting("autoRestart", "Auto Restart", "Automatically restart after completion", AppSettingType.Boolean, false, _autoRestart),
            new AppSetting("textColor", "Text Color", "Color of the timer display", AppSettingType.Select, "Cyan", _textColor, 
                Options: new[] { "White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta", "Orange" }),
            new AppSetting("backgroundColor", "Background Color", "Background color", AppSettingType.Select, "Black", _backgroundColor, 
                Options: new[] { "Black", "DarkBlue", "DarkGray" })
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "durationMinutes":
                _durationMinutes = value is System.Text.Json.JsonElement jsonDuration 
                    ? jsonDuration.GetInt32() 
                    : Convert.ToInt32(value);
                _initialTime = TimeSpan.FromMinutes(_durationMinutes);
                _remainingTime = _initialTime;
                _hasCompleted = false;
                UpdateDisplays(false);
                break;
            case "autoRestart":
                _autoRestart = value is System.Text.Json.JsonElement jsonAuto 
                    ? jsonAuto.GetBoolean() 
                    : Convert.ToBoolean(value);
                break;
            case "textColor":
                _textColor = value?.ToString() ?? "Cyan";
                break;
            case "backgroundColor":
                _backgroundColor = value?.ToString() ?? "Black";
                break;
        }
    }
}
