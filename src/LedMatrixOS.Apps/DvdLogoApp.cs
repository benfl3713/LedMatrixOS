using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace LedMatrixOS.Apps;

public sealed class DvdLogoApp : MatrixAppBase
{
    public override string Id => "dvd-logo";
    public override string Name => "DVD Logo";

    private float _logoX;
    private float _logoY;
    private float _velocityX;
    private float _velocityY;
    private float _logoWidth = 24;
    private float _logoHeight = 12;
    private Color _currentColor;
    private Font? _font;
    private Random _random = new();
	private (int height, int width) _dimensions;

    // Classic DVD logo colors
	private readonly Color[] _dvdColors =
	{
		ImageSharpExtensions.FromHsv(0, 1.0f, 1.0f),     // Red
        ImageSharpExtensions.FromHsv(60, 1.0f, 1.0f),    // Yellow
        ImageSharpExtensions.FromHsv(120, 1.0f, 1.0f),   // Green
        ImageSharpExtensions.FromHsv(180, 1.0f, 1.0f),   // Cyan
        ImageSharpExtensions.FromHsv(240, 1.0f, 1.0f),   // Blue
        ImageSharpExtensions.FromHsv(300, 1.0f, 1.0f)    // Magenta
    };

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
		_dimensions = dimensions;
        try
		{
			_font = SystemFonts.CreateFont("Nimbus Sans", 8, FontStyle.Bold);
		}
		catch
		{
			_font = SystemFonts.CreateFont("Nimbus Sans", 8);
		}

        // Initialize position and velocity
        _logoX = _random.Next((int)_logoWidth, dimensions.width - (int)_logoWidth);
        _logoY = _random.Next((int)_logoHeight, dimensions.height - (int)_logoHeight);
        
        // Random initial velocity (but not too slow)
        _velocityX = _random.NextSingle() < 0.5f ? -25 : 25;
        _velocityY = _random.NextSingle() < 0.5f ? -15 : 15;
        
        // Start with random color
        _currentColor = _dvdColors[_random.Next(_dvdColors.Length)];

        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        var dt = (float)deltaTime.TotalSeconds;

        // Update position
        _logoX += _velocityX * dt;
        _logoY += _velocityY * dt;

        bool colorChanged = false;

        // Bounce off left and right walls
        if (_logoX <= 0 || _logoX + _logoWidth >= _dimensions.width)
        {
            _velocityX = -_velocityX;
            _logoX = Math.Clamp(_logoX, 0, _dimensions.width - _logoWidth);
            colorChanged = true;
        }

        // Bounce off top and bottom walls
        if (_logoY <= 0 || _logoY + _logoHeight >= _dimensions.height)
        {
            _velocityY = -_velocityY;
            _logoY = Math.Clamp(_logoY, 0, _dimensions.height - _logoHeight);
            colorChanged = true;
        }

        // Change color when hitting a wall (classic DVD behavior)
        if (colorChanged)
        {
            _currentColor = _dvdColors[_random.Next(_dvdColors.Length)];
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_font == null) return;

        using var image = new Image<Rgb24>(frame.Width, frame.Height);

        image.Mutate(ctx =>
        {
            // Draw the DVD logo
            DrawDvdLogo(ctx, _logoX, _logoY, _logoWidth, _logoHeight, _currentColor);
        });

        frame.RenderImage(image);
    }

    private void DrawDvdLogo(IImageProcessingContext ctx, float x, float y, float width, float height, Color color)
    {
        // Draw the outer rectangle (DVD case outline)
        var outerRect = new RectangularPolygon(x, y, width, height);
        ctx.Draw(Pens.Solid(color, 1), outerRect);

        // Draw "DVD" text
        if (_font != null)
        {
            var textOptions = new RichTextOptions(_font) 
            { 
                Origin = new PointF(x + 2, y + 2) 
            };
            ctx.DrawText(textOptions, "DVD", color);
        }

        // Draw some decorative elements to make it look more like the classic logo
        
        // Inner rectangle
        var innerRect = new RectangularPolygon(x + 1, y + 1, width - 2, height - 2);
        ctx.Draw(Pens.Solid(color, 1), innerRect);

        // Small decorative lines
        ctx.DrawLine(Pens.Solid(color, 1), 
            new PointF(x + 2, y + height - 3), 
            new PointF(x + width - 2, y + height - 3));

        // Corner accents
        var cornerSize = 2;
        ctx.DrawLine(Pens.Solid(color, 1), 
            new PointF(x + width - cornerSize, y), 
            new PointF(x + width, y));
        ctx.DrawLine(Pens.Solid(color, 1), 
            new PointF(x + width, y), 
            new PointF(x + width, y + cornerSize));
    }
}
