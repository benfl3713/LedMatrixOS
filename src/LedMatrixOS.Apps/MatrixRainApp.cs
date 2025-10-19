using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace LedMatrixOS.Apps;

public sealed class MatrixRainApp : MatrixAppBase
{
    public override string Id => "matrix-rain";
    public override string Name => "Matrix Rain";

    private (int height, int width) _frameDimensions;
    private readonly List<RainDrop> _drops = new();
    private Random _random = new();
    private Font? _font;
    private readonly string _chars = "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン0123456789";

    private class RainDrop
    {
        public int Column { get; set; }
        public float Y { get; set; }
        public float Speed { get; set; }
        public string Character { get; set; } = "";
        public float Brightness { get; set; }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _frameDimensions = dimensions;
        try
        {
            _font = SystemFonts.CreateFont("Consolas", 8);
        }
        catch
        {
            _font = SystemFonts.CreateFont("Nimbus Sans", 8);
        }

        // Create rain drops for each column
        var columns = dimensions.width / 6; // Approximate character width
        for (int i = 0; i < columns; i++)
        {
            if (_random.NextDouble() < 0.3) // 30% chance of rain in each column
            {
                _drops.Add(new RainDrop
                {
                    Column = i,
                    Y = _random.Next(-dimensions.height, 0),
                    Speed = _random.Next(10, 30),
                    Character = _chars[_random.Next(_chars.Length)].ToString(),
                    Brightness = _random.NextSingle()
                });
            }
        }

        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        var dt = (float)deltaTime.TotalSeconds;

        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var drop = _drops[i];
            drop.Y += drop.Speed * dt;

            // Remove drops that have fallen off screen
            if (drop.Y > _frameDimensions.height + 10)
            {
                _drops.RemoveAt(i);
            }
            else
            {
                // Randomly change character
                if (_random.NextDouble() < 0.1)
                {
                    drop.Character = _chars[_random.Next(_chars.Length)].ToString();
                }
            }
        }

        // Add new drops occasionally
        if (_random.NextDouble() < 0.05 && _drops.Count < 50)
        {
            var columns = _frameDimensions.width / 6;
            _drops.Add(new RainDrop
            {
                Column = _random.Next(columns),
                Y = -10,
                Speed = _random.Next(10, 30),
                Character = _chars[_random.Next(_chars.Length)].ToString(),
                Brightness = _random.NextSingle()
            });
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_font == null) return;

        using var image = new Image<Rgb24>(frame.Width, frame.Height);

        image.Mutate(ctx =>
        {
            foreach (var drop in _drops)
            {
                var x = drop.Column * 6;
                var color = Color.FromRgb(0, (byte)(255 * drop.Brightness), 0);
                
                var textOptions = new RichTextOptions(_font)
                {
                    Origin = new PointF(x, drop.Y)
                };

                ctx.DrawText(textOptions, drop.Character, color);
            }
        });

        frame.RenderImage(image);
    }
}
