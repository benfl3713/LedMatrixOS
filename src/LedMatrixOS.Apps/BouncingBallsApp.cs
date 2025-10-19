using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps;

public sealed class BouncingBallsApp : MatrixAppBase
{
    public override string Id => "bouncing-balls";
    public override string Name => "Bouncing Balls";

    private readonly List<Ball> _balls = new();
    private Random _random = new();
    private (int height, int width) _frameDimensions;

    private class Ball
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VelX { get; set; }
        public float VelY { get; set; }
        public float Radius { get; set; }
        public Color Color { get; set; }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _frameDimensions = dimensions;
        // Create 5 random balls
        for (int i = 0; i < 10; i++)
        {
            _balls.Add(new Ball
            {
                X = _random.Next(5, dimensions.width - 5),
                Y = _random.Next(5, dimensions.height - 5),
                VelX = _random.Next(-30, 30),
                VelY = _random.Next(-30, 30),
                Radius = _random.Next(2, 5),
                Color = ImageSharpExtensions.FromHsv(_random.Next(0, 360), 1.0f, 1.0f)
            });
        }

        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        var dt = (float)deltaTime.TotalSeconds;

        foreach (var ball in _balls)
        {
            // Update position
            ball.X += ball.VelX * dt;
            ball.Y += ball.VelY * dt;

            // Bounce off walls
            if (ball.X - ball.Radius <= 0 || ball.X + ball.Radius >= _frameDimensions.width)
            {
                ball.VelX = -ball.VelX;
                ball.X = Math.Clamp(ball.X, ball.Radius, _frameDimensions.width - ball.Radius);
            }

            if (ball.Y - ball.Radius <= 0 || ball.Y + ball.Radius >= _frameDimensions.height)
            {
                ball.VelY = -ball.VelY;
                ball.Y = Math.Clamp(ball.Y, ball.Radius, _frameDimensions.height - ball.Radius);
            }
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);

        image.Mutate(ctx =>
        {
            // Draw each ball
            foreach (var ball in _balls)
            {
                var circle = new EllipsePolygon(ball.X, ball.Y, ball.Radius);
                ctx.Fill(ball.Color, circle);
                
                // Add a bright center for glow effect
                var innerCircle = new EllipsePolygon(ball.X, ball.Y, ball.Radius * 0.5f);
                ctx.Fill(Color.White, innerCircle);
            }
        });

        frame.RenderImage(image);
    }
}
