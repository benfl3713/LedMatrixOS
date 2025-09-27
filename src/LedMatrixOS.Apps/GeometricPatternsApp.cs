using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps;

public sealed class GeometricPatternsApp : MatrixAppBase
{
    public override string Id => "geometric-patterns";
    public override string Name => "Geometric Patterns";

    private double _animationTime;
    private int _currentPattern = 0;
    private double _patternChangeTime = 0;

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        _animationTime += deltaTime.TotalSeconds;
        _patternChangeTime += deltaTime.TotalSeconds;

        // Change pattern every 5 seconds
        if (_patternChangeTime >= 5.0)
        {
            _currentPattern = (_currentPattern + 1) % 3;
            _patternChangeTime = 0;
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        var centerX = frame.Width / 2f;
        var centerY = frame.Height / 2f;

        image.Mutate(ctx =>
        {
            switch (_currentPattern)
            {
                case 0:
                    DrawRotatingTriangles(ctx, centerX, centerY);
                    break;
                case 1:
                    DrawPulsatingCircles(ctx, centerX, centerY);
                    break;
                case 2:
                    DrawRotatingSquares(ctx, centerX, centerY);
                    break;
            }
        });

        frame.RenderImage(image);
    }

    private void DrawRotatingTriangles(IImageProcessingContext ctx, float centerX, float centerY)
    {
        for (int i = 0; i < 6; i++)
        {
            var angle = i * Math.PI / 3 + _animationTime;
            var radius = 15 + Math.Sin(_animationTime + i) * 5;
            
            var x = centerX + (float)(Math.Cos(angle) * radius);
            var y = centerY + (float)(Math.Sin(angle) * radius);
            
            var triangle = new RegularPolygon(x, y, 3, 5, (float)(_animationTime + i));
            var color = ImageSharpExtensions.FromHsv((i * 60 + (float)_animationTime * 30) % 360, 1.0f, 1.0f);
            
            ctx.Fill(color, triangle);
        }
    }

    private void DrawPulsatingCircles(IImageProcessingContext ctx, float centerX, float centerY)
    {
        for (int i = 1; i <= 5; i++)
        {
            var radius = i * 5 + (float)(Math.Sin(_animationTime * 2 + i) * 3);
            var circle = new EllipsePolygon(centerX, centerY, radius);
            var color = ImageSharpExtensions.FromHsv((i * 72 + (float)_animationTime * 50) % 360, 1.0f, 0.7f);
            
            ctx.Draw(Pens.Solid(color, 1), circle);
        }
    }

    private void DrawRotatingSquares(IImageProcessingContext ctx, float centerX, float centerY)
    {
        for (int i = 0; i < 4; i++)
        {
            var size = 8 + i * 4;
            var rotation = (float)(_animationTime * (i + 1) * 0.5);
            
            var square = new RegularPolygon(centerX, centerY, 4, size, rotation);
            var color = ImageSharpExtensions.FromHsv((i * 90 + (float)_animationTime * 40) % 360, 1.0f, 0.8f);
            
            ctx.Draw(Pens.Solid(color, 1), square);
        }
    }
}
