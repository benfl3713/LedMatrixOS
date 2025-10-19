using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps;

/// <summary>
/// Scrolling text marquee with customizable message and effects
/// </summary>
public sealed class ScrollingTextApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "scrolling-text";
    public override string Name => "Scrolling Text";

    private float _scrollPosition;
    private Font? _font;
    private int _displayWidth;
    
    // Settings
    private string _message = "HELLO WORLD!";
    private int _scrollSpeed = 30;
    private string _textColor = "Red";
    private string _backgroundColor = "Black";
    private int _fontSize = 24;
    private string _fontStyle = "Bold";

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        _displayWidth = dimensions.width;
        LoadFont();
        _scrollPosition = dimensions.width;
    }

    private void LoadFont()
    {
        FontFamily fontFamily;
        try
        {
            fontFamily = SystemFonts.Get("Nimbus Sans");
        }
        catch (Exception e)
        {
            fontFamily = SystemFonts.Get("Arial");
        }
        var style = _fontStyle == "Bold" ? FontStyle.Bold : FontStyle.Regular;
        _font = fontFamily.CreateFont(_fontSize, style);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        _scrollPosition -= _scrollSpeed * (float)deltaTime.TotalSeconds;
        
        // Reset when completely off screen
        if (_font != null)
        {
            var textWidth = TextMeasurer.MeasureSize(_message, new TextOptions(_font)).Width;
            if (_scrollPosition < -textWidth)
            {
                _scrollPosition = _displayWidth;
            }
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_font == null) return;

        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        image.Mutate(ctx =>
        {
            ctx.Fill(GetBackgroundColor());
            
            var textOptions = new RichTextOptions(_font)
            {
                Origin = new PointF(_scrollPosition, frame.Height / 2f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            ctx.DrawText(textOptions, _message, GetTextColor());
        });
        
        frame.RenderImage(image);
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
            "White" => Color.White,
            _ => Color.Red
        };
    }

    private Color GetBackgroundColor()
    {
        return _backgroundColor switch
        {
            "DarkBlue" => Color.DarkBlue,
            "DarkGray" => Color.DarkGray,
            "Navy" => Color.Navy,
            _ => Color.Black
        };
    }

    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("message", "Message", "Text to scroll across the display", AppSettingType.String, "HELLO WORLD!", _message),
            new AppSetting("scrollSpeed", "Scroll Speed", "How fast the text scrolls (pixels/sec)", AppSettingType.Integer, 30, _scrollSpeed, MinValue: 10, MaxValue: 100),
            new AppSetting("fontSize", "Font Size", "Size of the text", AppSettingType.Integer, 24, _fontSize, MinValue: 8, MaxValue: 48),
            new AppSetting("fontStyle", "Font Style", "Bold or Regular", AppSettingType.Select, "Bold", _fontStyle, 
                Options: new[] { "Regular", "Bold" }),
            new AppSetting("textColor", "Text Color", "Color of the text", AppSettingType.Select, "Red", _textColor, 
                Options: new[] { "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta", "Orange", "White" }),
            new AppSetting("backgroundColor", "Background Color", "Background color", AppSettingType.Select, "Black", _backgroundColor, 
                Options: new[] { "Black", "DarkBlue", "DarkGray", "Navy" })
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "message":
                _message = value?.ToString() ?? "HELLO WORLD!";
                break;
            case "scrollSpeed":
                _scrollSpeed = value is System.Text.Json.JsonElement jsonSpeed 
                    ? jsonSpeed.GetInt32() 
                    : Convert.ToInt32(value);
                break;
            case "fontSize":
                _fontSize = value is System.Text.Json.JsonElement jsonSize 
                    ? jsonSize.GetInt32() 
                    : Convert.ToInt32(value);
                LoadFont();
                break;
            case "fontStyle":
                _fontStyle = value?.ToString() ?? "Bold";
                LoadFont();
                break;
            case "textColor":
                _textColor = value?.ToString() ?? "Red";
                break;
            case "backgroundColor":
                _backgroundColor = value?.ToString() ?? "Black";
                break;
        }
    }
}
