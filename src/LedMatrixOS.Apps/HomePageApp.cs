using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace LedMatrixOS.Apps;

public sealed class HomePageApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "home";
    public override string Name => "Home";
    public override int FrameRate => 30;

    private Font? _timeFont;
    private Font? _dateFont;
    private double _animationTime;
    private (int height, int width) _dimensions;
    
    // Configuration settings
    private bool _showDate = true;
    private bool _show24Hour = true;
    private string _theme = "Calm Blue";
    private float _ambientSpeed = 0.3f;
    private string _displayMode = "Ambient Particles";

    // Ambient animation particles
    private readonly List<AmbientParticle> _particles = new();
    private readonly Random _random = new();

    // Wave animation state
    private readonly List<WaveLayer> _waves = new();

    private class AmbientParticle
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float Size { get; set; }
        public float Alpha { get; set; }
    }

    private class WaveLayer
    {
        public float Amplitude { get; set; }
        public float Frequency { get; set; }
        public float Speed { get; set; }
        public float Phase { get; set; }
        public float YOffset { get; set; }
        public float Alpha { get; set; }
    }

    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("displayMode", "Display Mode", "Visual style for the display", AppSettingType.Select, "Ambient Particles", _displayMode,
                Options: new[] { "Ambient Particles", "Flowing Waves", "Starfield", "Geometric Art", "Minimalist" }),
            new AppSetting("showDate", "Show Date", "Display the current date", AppSettingType.Boolean, true, _showDate),
            new AppSetting("show24Hour", "24-Hour Format", "Use 24-hour time format", AppSettingType.Boolean, true, _show24Hour),
            new AppSetting("theme", "Theme", "Color theme for the display", AppSettingType.Select, "Calm Blue", _theme, 
                Options: new[] { "Calm Blue", "Warm Sunset", "Forest Green", "Lavender Dreams", "Monochrome" }),
            new AppSetting("ambientSpeed", "Animation Speed", "Speed of ambient animations (1-10)", AppSettingType.Integer, 3, (int)(_ambientSpeed * 10), MinValue: 1, MaxValue: 10)
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "displayMode":
                var newMode = value.ToString() ?? "Ambient Particles";
                if (newMode != _displayMode)
                {
                    _displayMode = newMode;
                    InitializeDisplayMode();
                }
                break;
            case "showDate":
                _showDate = Convert.ToBoolean(value.ToString());
                break;
            case "show24Hour":
                _show24Hour = Convert.ToBoolean(value.ToString());
                break;
            case "theme":
                _theme = value.ToString() ?? "Calm Blue";
                break;
            case "ambientSpeed":
                _ambientSpeed = Convert.ToInt32(value) / 10f;
                break;
        }
    }

    private void InitializeDisplayMode()
    {
        _particles.Clear();
        _waves.Clear();

        switch (_displayMode)
        {
            case "Ambient Particles":
                InitializeParticles();
                break;
            case "Flowing Waves":
                InitializeWaves();
                break;
            case "Starfield":
                InitializeStarfield();
                break;
            case "Geometric Art":
                InitializeGeometricArt();
                break;
        }
    }

    private void InitializeParticles()
    {
        int particleCount = Math.Min(_dimensions.width * _dimensions.height / 30, 15);
        for (int i = 0; i < particleCount; i++)
        {
            _particles.Add(new AmbientParticle
            {
                X = (float)(_random.NextDouble() * _dimensions.width),
                Y = (float)(_random.NextDouble() * _dimensions.height),
                VelocityX = (float)(_random.NextDouble() - 0.5) * 0.5f,
                VelocityY = (float)(_random.NextDouble() - 0.5) * 0.5f,
                Size = (float)(_random.NextDouble() * 1.5 + 0.5),
                Alpha = (float)(_random.NextDouble() * 0.3 + 0.1)
            });
        }
    }

    private void InitializeWaves()
    {
        for (int i = 0; i < 3; i++)
        {
            _waves.Add(new WaveLayer
            {
                Amplitude = 3 + i * 2,
                Frequency = 0.3f + i * 0.15f,
                Speed = 0.5f + i * 0.3f,
                Phase = 0,
                YOffset = _dimensions.height / 2f + (i - 1) * 8,
                Alpha = 0.3f - i * 0.08f
            });
        }
    }

    private void InitializeStarfield()
    {
        int starCount = Math.Min(_dimensions.width * _dimensions.height / 20, 25);
        for (int i = 0; i < starCount; i++)
        {
            _particles.Add(new AmbientParticle
            {
                X = (float)(_random.NextDouble() * _dimensions.width),
                Y = (float)(_random.NextDouble() * _dimensions.height),
                VelocityX = 0,
                VelocityY = (float)(_random.NextDouble() * 0.1 + 0.02f),
                Size = (float)(_random.NextDouble() * 1.2 + 0.3),
                Alpha = (float)(_random.NextDouble() * 0.6 + 0.2)
            });
        }
    }

    private void InitializeGeometricArt()
    {
        // Use particles to represent geometric shapes
        int shapeCount = 5;
        for (int i = 0; i < shapeCount; i++)
        {
            _particles.Add(new AmbientParticle
            {
                X = (float)(_random.NextDouble() * _dimensions.width),
                Y = (float)(_random.NextDouble() * _dimensions.height),
                VelocityX = (float)(_random.NextDouble() - 0.5) * 0.2f,
                VelocityY = (float)(_random.NextDouble() - 0.5) * 0.2f,
                Size = (float)(_random.NextDouble() * 5 + 3),
                Alpha = (float)(_random.NextDouble() * 0.2 + 0.15)
            });
        }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _dimensions = dimensions;
        
        // Load fonts
        try
        {
            _timeFont = SystemFonts.CreateFont("Nimbus Sans", 14, FontStyle.Bold);
            _dateFont = SystemFonts.CreateFont("Nimbus Sans", 6);
        }
        catch
        {
            _timeFont = SystemFonts.CreateFont("Arial", 14, FontStyle.Bold);
            _dateFont = SystemFonts.CreateFont("Arial", 6);
        }

        // Initialize display mode
        InitializeDisplayMode();

        return Task.CompletedTask;
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        _animationTime += deltaTime.TotalSeconds * _ambientSpeed;

        switch (_displayMode)
        {
            case "Ambient Particles":
                UpdateAmbientParticles(deltaTime);
                break;
            case "Flowing Waves":
                UpdateWaves(deltaTime);
                break;
            case "Starfield":
                UpdateStarfield(deltaTime);
                break;
            case "Geometric Art":
                UpdateGeometricArt(deltaTime);
                break;
        }
    }

    private void UpdateAmbientParticles(TimeSpan deltaTime)
    {
        foreach (var particle in _particles)
        {
            particle.X += particle.VelocityX * (float)deltaTime.TotalSeconds * 10 * _ambientSpeed;
            particle.Y += particle.VelocityY * (float)deltaTime.TotalSeconds * 10 * _ambientSpeed;

            // Wrap around screen
            if (particle.X < -5) particle.X = _dimensions.width + 5;
            if (particle.X > _dimensions.width + 5) particle.X = -5;
            if (particle.Y < -5) particle.Y = _dimensions.height + 5;
            if (particle.Y > _dimensions.height + 5) particle.Y = -5;

            // Gentle alpha pulsing
            particle.Alpha = 0.2f + (float)(Math.Sin(_animationTime * 2 + particle.X) * 0.1);
        }
    }

    private void UpdateWaves(TimeSpan deltaTime)
    {
        foreach (var wave in _waves)
        {
            wave.Phase += wave.Speed * (float)deltaTime.TotalSeconds * _ambientSpeed;
        }
    }

    private void UpdateStarfield(TimeSpan deltaTime)
    {
        foreach (var star in _particles)
        {
            star.Y += star.VelocityY * (float)deltaTime.TotalSeconds * 30 * _ambientSpeed;

            // Wrap around screen
            if (star.Y > _dimensions.height + 5)
            {
                star.Y = -5;
                star.X = (float)(_random.NextDouble() * _dimensions.width);
            }

            // Twinkling effect
            star.Alpha = 0.4f + (float)(Math.Sin(_animationTime * 3 + star.X * 10) * 0.3);
        }
    }

    private void UpdateGeometricArt(TimeSpan deltaTime)
    {
        foreach (var shape in _particles)
        {
            shape.X += shape.VelocityX * (float)deltaTime.TotalSeconds * 5 * _ambientSpeed;
            shape.Y += shape.VelocityY * (float)deltaTime.TotalSeconds * 5 * _ambientSpeed;

            // Bounce off edges
            if (shape.X < 0 || shape.X > _dimensions.width)
                shape.VelocityX *= -1;
            if (shape.Y < 0 || shape.Y > _dimensions.height)
                shape.VelocityY *= -1;

            shape.X = Math.Clamp(shape.X, 0, _dimensions.width);
            shape.Y = Math.Clamp(shape.Y, 0, _dimensions.height);
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);

        image.Mutate(ctx =>
        {
            // Get theme colors
            var (bgColor, accentColor, textColor) = GetThemeColors();

            // Fill background
            ctx.Fill(bgColor);

            // Draw mode-specific art
            switch (_displayMode)
            {
                case "Ambient Particles":
                    DrawAmbientGradient(ctx, frame.Width, frame.Height, accentColor);
                    DrawAmbientParticles(ctx, accentColor);
                    DrawCornerAccents(ctx, frame.Width, frame.Height, accentColor);
                    break;
                case "Flowing Waves":
                    DrawWaves(ctx, frame.Width, frame.Height, accentColor);
                    break;
                case "Starfield":
                    DrawStarfield(ctx, accentColor);
                    break;
                case "Geometric Art":
                    DrawGeometricArt(ctx, frame.Width, frame.Height, accentColor);
                    break;
                case "Minimalist":
                    DrawMinimalist(ctx, frame.Width, frame.Height, accentColor);
                    break;
            }

            // Draw time
            var now = DateTime.Now;
            var timeFormat = _show24Hour ? "HH:mm" : "h:mm tt";
            var timeString = now.ToString(timeFormat);
            
            if (_timeFont != null)
            {
                var timeOptions = new RichTextOptions(_timeFont)
                {
                    Origin = new PointF(frame.Width / 2f, frame.Height / 2f - (_showDate ? 4 : 0)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                ctx.DrawText(timeOptions, timeString, textColor);
            }

            // Draw date if enabled
            if (_showDate && _dateFont != null)
            {
                var dateString = now.ToString("MMM dd");
                var dateOptions = new RichTextOptions(_dateFont)
                {
                    Origin = new PointF(frame.Width / 2f, frame.Height / 2f + 8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var textPixel = textColor.ToPixel<Rgb24>();
                var dateColor = Color.FromRgb(
                    (byte)(textPixel.R * 0.7f),
                    (byte)(textPixel.G * 0.7f),
                    (byte)(textPixel.B * 0.7f)
                );

                ctx.DrawText(dateOptions, dateString, dateColor);
            }
        });

        frame.RenderImage(image);
    }

    private (Color bg, Color accent, Color text) GetThemeColors()
    {
        return _theme switch
        {
            "Calm Blue" => (
                Color.FromRgb(10, 15, 35),           // Deep blue background
                Color.FromRgb(40, 80, 150),          // Soft blue accent
                Color.FromRgb(200, 220, 255)         // Light blue text
            ),
            "Warm Sunset" => (
                Color.FromRgb(30, 20, 25),           // Dark warm background
                Color.FromRgb(180, 80, 50),          // Orange accent
                Color.FromRgb(255, 200, 150)         // Warm text
            ),
            "Forest Green" => (
                Color.FromRgb(15, 25, 20),           // Dark forest background
                Color.FromRgb(50, 120, 80),          // Green accent
                Color.FromRgb(180, 230, 200)         // Light green text
            ),
            "Lavender Dreams" => (
                Color.FromRgb(25, 20, 35),           // Deep purple background
                Color.FromRgb(120, 80, 150),         // Lavender accent
                Color.FromRgb(220, 200, 255)         // Light lavender text
            ),
            "Monochrome" => (
                Color.FromRgb(10, 10, 10),           // Almost black background
                Color.FromRgb(60, 60, 60),           // Gray accent
                Color.FromRgb(220, 220, 220)         // Light gray text
            ),
            _ => (Color.FromRgb(10, 15, 35), Color.FromRgb(40, 80, 150), Color.FromRgb(200, 220, 255))
        };
    }

    private void DrawAmbientGradient(IImageProcessingContext ctx, int width, int height, Color accentColor)
    {
        // Very subtle radial gradient from center
        var centerX = width / 2f;
        var centerY = height / 2f;
        var maxDist = (float)Math.Sqrt(centerX * centerX + centerY * centerY);
        var accentPixel = accentColor.ToPixel<Rgb24>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var dx = x - centerX;
                var dy = y - centerY;
                var dist = (float)Math.Sqrt(dx * dx + dy * dy);
                var factor = Math.Min(1.0f, dist / maxDist) * 0.15f; // Very subtle

                var pulseOffset = (float)(Math.Sin(_animationTime * 0.5) * 0.05);
                factor = Math.Max(0, factor + pulseOffset);

                var color = Color.FromRgb(
                    (byte)(accentPixel.R * factor),
                    (byte)(accentPixel.G * factor),
                    (byte)(accentPixel.B * factor)
                );

                ctx.Fill(color, new RectangleF(x, y, 1, 1));
            }
        }
    }

    private void DrawAmbientParticles(IImageProcessingContext ctx, Color accentColor)
    {
        var accentPixel = accentColor.ToPixel<Rgb24>();
        
        foreach (var particle in _particles)
        {
            var color = Color.FromRgba(
                accentPixel.R,
                accentPixel.G,
                accentPixel.B,
                (byte)(particle.Alpha * 255)
            );

            var ellipse = new EllipsePolygon(particle.X, particle.Y, particle.Size);
            ctx.Fill(color, ellipse);

            // Soft glow effect
            if (particle.Size > 1)
            {
                var glowColor = Color.FromRgba(
                    accentPixel.R,
                    accentPixel.G,
                    accentPixel.B,
                    (byte)(particle.Alpha * 100)
                );
                var glowEllipse = new EllipsePolygon(particle.X, particle.Y, particle.Size + 1);
                ctx.Fill(glowColor, glowEllipse);
            }
        }
    }

    private void DrawWaves(IImageProcessingContext ctx, int width, int height, Color accentColor)
    {
        var accentPixel = accentColor.ToPixel<Rgb24>();

        foreach (var wave in _waves)
        {
            var points = new List<PointF>();
            
            for (int x = 0; x <= width; x++)
            {
                var y = wave.YOffset + (float)Math.Sin((x * wave.Frequency + wave.Phase) * 0.1) * wave.Amplitude;
                points.Add(new PointF(x, y));
            }

            if (points.Count > 1)
            {
                var color = Color.FromRgba(
                    accentPixel.R,
                    accentPixel.G,
                    accentPixel.B,
                    (byte)(wave.Alpha * 255)
                );

                var pen = Pens.Solid(color, 1.5f);
                
                for (int i = 0; i < points.Count - 1; i++)
                {
                    ctx.DrawLine(pen, points[i], points[i + 1]);
                }
            }
        }
    }

    private void DrawStarfield(IImageProcessingContext ctx, Color accentColor)
    {
        var accentPixel = accentColor.ToPixel<Rgb24>();

        foreach (var star in _particles)
        {
            var color = Color.FromRgba(
                accentPixel.R,
                accentPixel.G,
                accentPixel.B,
                (byte)(star.Alpha * 255)
            );

            // Draw star
            ctx.Fill(color, new RectangleF(star.X, star.Y, star.Size, star.Size));

            // Brighter center
            if (star.Size > 0.5f)
            {
                var brightColor = Color.FromRgba(
                    (byte)Math.Min(255, accentPixel.R * 1.5f),
                    (byte)Math.Min(255, accentPixel.G * 1.5f),
                    (byte)Math.Min(255, accentPixel.B * 1.5f),
                    (byte)(star.Alpha * 255)
                );
                ctx.Fill(brightColor, new RectangleF(star.X + star.Size / 4, star.Y + star.Size / 4, star.Size / 2, star.Size / 2));
            }
        }
    }

    private void DrawGeometricArt(IImageProcessingContext ctx, int width, int height, Color accentColor)
    {
        var accentPixel = accentColor.ToPixel<Rgb24>();

        foreach (var shape in _particles)
        {
            var rotation = (float)(_animationTime * 20 + shape.X);
            var color = Color.FromRgba(
                accentPixel.R,
                accentPixel.G,
                accentPixel.B,
                (byte)(shape.Alpha * 255)
            );

            // Draw rotating rectangle
            var halfSize = shape.Size / 2;
            var rad = rotation * (float)Math.PI / 180f;
            var cos = (float)Math.Cos(rad);
            var sin = (float)Math.Sin(rad);

            var points = new PointF[]
            {
                new PointF(shape.X + cos * halfSize - sin * halfSize, shape.Y + sin * halfSize + cos * halfSize),
                new PointF(shape.X + cos * halfSize + sin * halfSize, shape.Y + sin * halfSize - cos * halfSize),
                new PointF(shape.X - cos * halfSize + sin * halfSize, shape.Y - sin * halfSize - cos * halfSize),
                new PointF(shape.X - cos * halfSize - sin * halfSize, shape.Y - sin * halfSize + cos * halfSize)
            };

            var polygon = new Polygon(new LinearLineSegment(points));
            ctx.Draw(Pens.Solid(color, 1), polygon);
        }
    }

    private void DrawMinimalist(IImageProcessingContext ctx, int width, int height, Color accentColor)
    {
        var accentPixel = accentColor.ToPixel<Rgb24>();
        
        // Just a simple horizontal line
        var lineY = height / 2f;
        var breathe = (float)(Math.Sin(_animationTime * 0.5) * 0.3 + 0.7);
        
        var color = Color.FromRgba(
            accentPixel.R,
            accentPixel.G,
            accentPixel.B,
            (byte)(breathe * 120)
        );

        var pen = Pens.Solid(color, 1);
        
        // Thin lines above and below where text will be
        ctx.DrawLine(pen, new PointF(width * 0.2f, lineY - 12), new PointF(width * 0.8f, lineY - 12));
        ctx.DrawLine(pen, new PointF(width * 0.2f, lineY + 12), new PointF(width * 0.8f, lineY + 12));
    }

    private void DrawCornerAccents(IImageProcessingContext ctx, int width, int height, Color accentColor)
    {
        var accentPixel = accentColor.ToPixel<Rgb24>();
        
        // Subtle breathing effect
        var breathe = (float)(Math.Sin(_animationTime * 0.7) * 0.3 + 0.7);
        var cornerColor = Color.FromRgba(
            accentPixel.R,
            accentPixel.G,
            accentPixel.B,
            (byte)(breathe * 80)
        );

        var pen = Pens.Solid(cornerColor, 1);

        // Top left corner
        ctx.DrawLine(pen, new PointF(0, 3), new PointF(3, 3));
        ctx.DrawLine(pen, new PointF(3, 0), new PointF(3, 3));

        // Top right corner
        ctx.DrawLine(pen, new PointF(width - 4, 3), new PointF(width - 1, 3));
        ctx.DrawLine(pen, new PointF(width - 4, 0), new PointF(width - 4, 3));

        // Bottom left corner
        ctx.DrawLine(pen, new PointF(0, height - 4), new PointF(3, height - 4));
        ctx.DrawLine(pen, new PointF(3, height - 4), new PointF(3, height - 1));

        // Bottom right corner
        ctx.DrawLine(pen, new PointF(width - 4, height - 4), new PointF(width - 1, height - 4));
        ctx.DrawLine(pen, new PointF(width - 4, height - 4), new PointF(width - 4, height - 1));
    }

    public override Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _particles.Clear();
        _waves.Clear();
        return Task.CompletedTask;
    }
}
