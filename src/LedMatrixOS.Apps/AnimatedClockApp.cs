using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps;

public sealed class AnimatedClockApp : MatrixAppBase
{
    public override string Id => "animated-clock";
    public override string Name => "Animated Clock";

    private DateTime _lastTime = DateTime.Now;
    private readonly Dictionary<int, float> _digitAnimations = new();
    private readonly Dictionary<int, (int from, int to)> _digitTransitions = new();
    
    // 7-segment display patterns for digits 0-9
    private readonly Dictionary<int, bool[]> _digitPatterns = new()
    {
        { 0, new[] { true, true, true, false, true, true, true } },      // 0
        { 1, new[] { false, false, true, false, false, true, false } }, // 1
        { 2, new[] { true, false, true, true, true, false, true } },    // 2
        { 3, new[] { true, false, true, true, false, true, true } },    // 3
        { 4, new[] { false, true, true, true, false, true, false } },   // 4
        { 5, new[] { true, true, false, true, false, true, true } },    // 5
        { 6, new[] { true, true, false, true, true, true, true } },     // 6
        { 7, new[] { true, false, true, false, false, true, false } },  // 7
        { 8, new[] { true, true, true, true, true, true, true } },      // 8
        { 9, new[] { true, true, true, true, false, true, true } }      // 9
    };

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        var currentTime = DateTime.Now;
        var timeString = currentTime.ToString("HHmmss");
        var lastTimeString = _lastTime.ToString("HHmmss");

        // Check for digit changes and start animations
        for (int i = 0; i < timeString.Length; i++)
        {
            var currentDigit = int.Parse(timeString[i].ToString());
            var lastDigit = int.Parse(lastTimeString[i].ToString());

            if (currentDigit != lastDigit)
            {
                _digitTransitions[i] = (lastDigit, currentDigit);
                _digitAnimations[i] = 0.0f; // Start animation
            }
        }

        // Update animation progress
        var animationSpeed = 3.0f; // Animation duration in seconds
        foreach (var key in _digitAnimations.Keys.ToList())
        {
            _digitAnimations[key] += (float)deltaTime.TotalSeconds * animationSpeed;
            if (_digitAnimations[key] >= 1.0f)
            {
                _digitAnimations.Remove(key);
                _digitTransitions.Remove(key);
            }
        }

        _lastTime = currentTime;
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        var timeString = DateTime.Now.ToString("HHmmss");
        var digitWidth = 24;
        var digitHeight = 40;
        var digitSpacing = 30;
        var colonWidth = 4;
        
        // Calculate starting position to center the clock
        var totalWidth = 6 * digitWidth + 2 * colonWidth + 5 * 8; // 6 digits + 2 colons + spacing
        var startX = (frame.Width - totalWidth) / 2;
        var startY = (frame.Height - digitHeight) / 2;

        image.Mutate(ctx =>
        {
            var currentX = startX;

            for (int i = 0; i < timeString.Length; i++)
            {
                var digit = int.Parse(timeString[i].ToString());
                
                if (_digitAnimations.ContainsKey(i) && _digitTransitions.ContainsKey(i))
                {
                    // Draw morphing digit
                    var transition = _digitTransitions[i];
                    //var progress = EaseInOutCubic(_digitAnimations[i]);
                    var progress = 0;
                    DrawMorphingDigit(ctx, currentX, startY, transition.from, transition.to, progress, digitWidth, digitHeight);
                }
                else
                {
                    // Draw static digit
                    DrawDigit(ctx, currentX, startY, digit, digitWidth, digitHeight, 1.0f);
                }

                currentX += digitSpacing;

                // Draw colons after hours and minutes
                if (i == 1 || i == 3)
                {
                    DrawColon(ctx, currentX, startY, digitHeight);
                    currentX += colonWidth + 2;
                }
            }
        });

        frame.RenderImage(image);
    }

    private void DrawMorphingDigit(IImageProcessingContext ctx, int x, int y, int fromDigit, int toDigit, 
        float progress, int width, int height)
    {
        var fromPattern = _digitPatterns[fromDigit];
        var toPattern = _digitPatterns[toDigit];

        for (int segment = 0; segment < 7; segment++)
        {
            var fromOn = fromPattern[segment];
            var toOn = toPattern[segment];

            float alpha;
            Color color;

            if (fromOn && toOn)
            {
                // Segment stays on - full brightness
                alpha = 1.0f;
                color = ImageSharpExtensions.FromHsv(200, 0.6f, 1.0f); // Green
            }
            else if (!fromOn && !toOn)
            {
                // Segment stays off - skip drawing
                continue;
            }
            else if (fromOn && !toOn)
            {
                // Segment turning off - fade out
                alpha = 1.0f - progress;
                color = ImageSharpExtensions.FromHsv(0, 0.8f, 0.5f); // Red fading out
            }
            else if (!fromOn && toOn)
            {
                // Segment turning off - fade out
                alpha = 1.0f - progress;
                color = ImageSharpExtensions.FromHsv(60, 0.6f, 0.5f); // Yellow
            }
            else
            {
                // Segment turning on - fade in
                alpha = progress;
                color = ImageSharpExtensions.FromHsv(200, 0.6f, 1.0f); // Blue fading in
            }

            if (alpha > 0.05f) // Only draw if visible enough
            {
                var segmentColor = color;
                DrawSegment(ctx, x, y, segment, width, height, segmentColor);
            }
        }
    }

    private void DrawDigit(IImageProcessingContext ctx, int x, int y, int digit, int width, int height, float brightness)
    {
        var pattern = _digitPatterns[digit];
        var color = ImageSharpExtensions.FromHsv(200, 0.6f, brightness); // Cyan

        for (int segment = 0; segment < 7; segment++)
        {
            if (pattern[segment])
            {
                DrawSegment(ctx, x, y, segment, width, height, color);
            }
        }
    }

    private void DrawSegment(IImageProcessingContext ctx, int x, int y, int segment, int width, int height, Color color)
    {
        var segmentThickness = 3;
        var segmentLength = width - 4;
        var halfHeight = height / 2;

        switch (segment)
        {
            case 0: // Top horizontal
                ctx.FillPolygon(color, new PointF[]
                {
                    new(x + 2, y),
                    new(x + width - 2, y),
                    new(x + width - 3, y + segmentThickness),
                    new(x + 3, y + segmentThickness)
                });
                break;
            case 1: // Top left vertical
                ctx.FillPolygon(color, new PointF[]
                {
                    new(x, y + 2),
                    new(x + segmentThickness, y + 3),
                    new(x + segmentThickness, y + halfHeight - 1),
                    new(x, y + halfHeight)
                });
                break;
            case 2: // Top right vertical
                ctx.FillPolygon(color, new PointF[]
                {
                    new(x + width - segmentThickness, y + 3),
                    new(x + width, y + 2),
                    new(x + width, y + halfHeight),
                    new(x + width - segmentThickness, y + halfHeight - 1)
                });
                break;
            case 3: // Middle horizontal
                ctx.FillPolygon(color, new PointF[]
                {
                    new(x + 2, y + halfHeight - 1),
                    new(x + width - 2, y + halfHeight - 1),
                    new(x + width - 3, y + halfHeight + 1),
                    new(x + 3, y + halfHeight + 1)
                });
                break;
            case 4: // Bottom left vertical
                ctx.FillPolygon(color, new PointF[]
                {
                    new(x, y + halfHeight),
                    new(x + segmentThickness, y + halfHeight + 1),
                    new(x + segmentThickness, y + height - 3),
                    new(x, y + height - 2)
                });
                break;
            case 5: // Bottom right vertical
                ctx.FillPolygon(color, new PointF[]
                {
                    new(x + width - segmentThickness, y + halfHeight + 1),
                    new(x + width, y + halfHeight),
                    new(x + width, y + height - 2),
                    new(x + width - segmentThickness, y + height - 3)
                });
                break;
            case 6: // Bottom horizontal
                ctx.FillPolygon(color, new PointF[]
                {
                    new(x + 3, y + height - segmentThickness),
                    new(x + width - 3, y + height - segmentThickness),
                    new(x + width - 2, y + height),
                    new(x + 2, y + height)
                });
                break;
        }
    }

    private void DrawColon(IImageProcessingContext ctx, int x, int y, int height)
    {
        var dotSize = 2;
        var color = ImageSharpExtensions.FromHsv(200, 0.6f, 1.0f);
        
        // Top dot
        var topDot = new EllipsePolygon(x + 1, y + height / 3, dotSize);
        ctx.Fill(color, topDot);
        
        // Bottom dot
        var bottomDot = new EllipsePolygon(x + 1, y + 2 * height / 3, dotSize);
        ctx.Fill(color, bottomDot);
    }

    private float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4 * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 3) / 2;
    }
}
