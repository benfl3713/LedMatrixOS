using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps;

public sealed class RainbowSpiralApp : MatrixAppBase
{
    public override string Id => "rainbow-spiral";
    public override string Name => "Rainbow Spiral";

    private double _animationTime;
    private readonly int _spiralSegments = 60;

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        _animationTime += deltaTime.TotalSeconds;
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        var centerX = frame.Width / 2f;
        var centerY = frame.Height / 2f;
        var maxRadius = Math.Min(frame.Width, frame.Height) / 2f - 2;

        image.Mutate(ctx =>
        {
            // Create spiral points
            var points = new PointF[_spiralSegments];
            for (int i = 0; i < _spiralSegments; i++)
            {
                var angle = (i / (float)_spiralSegments) * Math.PI * 6 + _animationTime; // 3 full rotations
                var radius = (i / (float)_spiralSegments) * maxRadius;
                
                points[i] = new PointF(
                    centerX + (float)(Math.Cos(angle) * radius),
                    centerY + (float)(Math.Sin(angle) * radius)
                );
            }

            // Draw spiral with rainbow colors
            for (int i = 0; i < points.Length - 1; i++)
            {
                var hue = (i / (float)_spiralSegments + (float)_animationTime * 0.2f) % 1.0f;
                var color = ImageSharpExtensions.FromHsv(hue * 360, 1.0f, 1.0f);
                var pen = Pens.Solid(color, 2);
                
                ctx.DrawLine(pen, points[i], points[i + 1]);
            }
        });

        frame.RenderImage(image);
    }
}
