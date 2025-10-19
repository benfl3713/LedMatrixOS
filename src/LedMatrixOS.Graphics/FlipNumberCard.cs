using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;

namespace LedMatrixOS.Graphics;

/// <summary>
/// Represents a single flip card that can animate between two numbers with a flip display style animation
/// </summary>
public class FlipNumberCard
{
    private int _currentValue;
    private int _targetValue;
    private float _animationProgress; // 0.0 to 1.0
    private bool _isAnimating;
    
    public int CurrentValue => _currentValue;
    public int TargetValue => _targetValue;
    public bool IsAnimating => _isAnimating;
    public float AnimationProgress => _animationProgress;

    public FlipNumberCard(int initialValue = 0)
    {
        _currentValue = initialValue;
        _targetValue = initialValue;
        _animationProgress = 0f;
        _isAnimating = false;
    }

    /// <summary>
    /// Starts an animation to flip to a new value
    /// </summary>
    public void FlipTo(int newValue)
    {
        if (newValue < 0 || newValue > 9)
            throw new ArgumentOutOfRangeException(nameof(newValue), "Value must be between 0 and 9");
            
        if (newValue != _targetValue)
        {
            _currentValue = _targetValue;
            _targetValue = newValue;
            _animationProgress = 0f;
            _isAnimating = true;
        }
    }

    /// <summary>
    /// Sets the value immediately without animation
    /// </summary>
    public void SetValue(int value)
    {
        if (value < 0 || value > 9)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0 and 9");
            
        _currentValue = value;
        _targetValue = value;
        _animationProgress = 0f;
        _isAnimating = false;
    }

    /// <summary>
    /// Updates the animation progress
    /// </summary>
    /// <param name="deltaTime">Time elapsed since last update</param>
    /// <param name="animationDuration">Total duration of the flip animation in seconds</param>
    public void Update(TimeSpan deltaTime, float animationDuration = 0.5f)
    {
        if (!_isAnimating) return;

        _animationProgress += (float)(deltaTime.TotalSeconds / animationDuration);
        
        if (_animationProgress >= 1.0f)
        {
            _animationProgress = 1.0f;
            _currentValue = _targetValue;
            _isAnimating = false;
        }
    }

    /// <summary>
    /// Renders the flip card at the specified position
    /// </summary>
    public void Render(IImageProcessingContext ctx, int x, int y, int width, int height, Font font, Color textColor, Color backgroundColor)
    {
        if (!_isAnimating)
        {
            // Draw static card
            DrawCard(ctx, x, y, width, height, _currentValue, font, textColor, backgroundColor, 1.0f);
        }
        else
        {
            // Apply easing to the animation for a more natural flip
            float easedProgress = EaseInOutCubic(_animationProgress);
            
            if (easedProgress < 0.5f)
            {
                // First half: old number's top half is static, bottom half flips up (shrinks with perspective)
                float flipProgress = easedProgress * 2.0f; // 0 to 1 for first half
                
                // Draw static top half of old number
                DrawStaticTopHalf(ctx, x, y, width, height, _currentValue, font, textColor, backgroundColor);
                
                // Draw bottom half flipping up with perspective
                DrawFlippingBottomHalf(ctx, x, y, width, height, _currentValue, font, textColor, backgroundColor, flipProgress, true);
            }
            else
            {
                // Second half: new number's bottom half is static, top half flips down (grows with perspective)
                float flipProgress = (easedProgress - 0.5f) * 2.0f; // 0 to 1 for second half
                
                // Draw static bottom half of new number
                DrawStaticBottomHalf(ctx, x, y, width, height, _targetValue, font, textColor, backgroundColor);
                
                // Draw top half flipping down with perspective
                DrawFlippingTopHalf(ctx, x, y, width, height, _targetValue, font, textColor, backgroundColor, flipProgress, false);
            }
            
            // Draw center separator line
            var line = new PathBuilder().AddLine(new PointF(x, y + height / 2), new PointF(x + width, y + height / 2)).Build();
            ctx.Draw(Color.Black, 2, line);
        }
    }

    private void DrawCard(IImageProcessingContext ctx, int x, int y, int width, int height, int digit, Font font, Color textColor, Color backgroundColor, float alpha)
    {
        var bgColor = new Color(new Rgba32(backgroundColor.ToPixel<Rgba32>().R, backgroundColor.ToPixel<Rgba32>().G, backgroundColor.ToPixel<Rgba32>().B, (byte)(255 * alpha)));
        
        // Draw background
        ctx.Fill(bgColor, new RectangleF(x, y, width, height));
        
        // Draw border for depth effect
        ctx.Draw(Color.DarkGray, 1, new RectangleF(x, y, width, height));
        
        // Draw center line
        var line = new PathBuilder().AddLine(new PointF(x, y + height / 2), new PointF(x + width, y + height / 2)).Build();
        ctx.Draw(Color.Black, 2, line);
        
        // Draw digit centered
        var text = digit.ToString();
        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(x + width / 2f, y + height / 2f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        ctx.DrawText(textOptions, text, textColor);
    }

    private void DrawStaticTopHalf(IImageProcessingContext ctx, int x, int y, int width, int height, int digit, Font font, Color textColor, Color backgroundColor)
    {
        int halfHeight = height / 2;
        
        // Create full card image
        using var fullCardImage = new Image<Rgba32>(width, height);
        fullCardImage.Mutate(tempCtx =>
        {
            tempCtx.Fill(backgroundColor, new RectangleF(0, 0, width, height));
            
            var text = digit.ToString();
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(width / 2f, height / 2f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            tempCtx.DrawText(textOptions, text, textColor);
        });

        // Crop the top half
        fullCardImage.Mutate(tempCtx => tempCtx.Crop(new Rectangle(0, 0, width, halfHeight)));
        
        // Draw it
        ctx.DrawImage(fullCardImage, new Point(x, y), 1f);
    }

    private void DrawStaticBottomHalf(IImageProcessingContext ctx, int x, int y, int width, int height, int digit, Font font, Color textColor, Color backgroundColor)
    {
        int halfHeight = height / 2;
        
        // Create full card image
        using var fullCardImage = new Image<Rgba32>(width, height);
        fullCardImage.Mutate(tempCtx =>
        {
            tempCtx.Fill(backgroundColor, new RectangleF(0, 0, width, height));
            
            var text = digit.ToString();
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(width / 2f, height / 2f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            tempCtx.DrawText(textOptions, text, textColor);
        });

        // Crop the bottom half
        fullCardImage.Mutate(tempCtx => tempCtx.Crop(new Rectangle(0, halfHeight, width, halfHeight)));
        
        // Draw it
        ctx.DrawImage(fullCardImage, new Point(x, y + halfHeight), 1f);
    }

    private void DrawFlippingBottomHalf(IImageProcessingContext ctx, int x, int y, int width, int height, int digit, Font font, Color textColor, Color backgroundColor, float progress, bool flippingUp)
    {
        int halfHeight = height / 2;
        
        // Calculate perspective scale (1.0 at start, 0.0 at 90 degrees)
        float perspectiveScale = (float)Math.Cos(progress * Math.PI / 2.0);
        int scaledHeight = Math.Max(1, (int)(halfHeight * perspectiveScale));
        
        if (scaledHeight <= 0) return;
        
        // Create full card image
        using var fullCardImage = new Image<Rgba32>(width, height);
        fullCardImage.Mutate(tempCtx =>
        {
            tempCtx.Fill(backgroundColor, new RectangleF(0, 0, width, height));
            
            var text = digit.ToString();
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(width / 2f, height / 2f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            tempCtx.DrawText(textOptions, text, textColor);
        });

        // Crop the bottom half
        fullCardImage.Mutate(tempCtx => tempCtx.Crop(new Rectangle(0, halfHeight, width, halfHeight)));
        
        // Apply perspective scaling
        fullCardImage.Mutate(tempCtx => tempCtx.Resize(width, scaledHeight));
        
        // Apply brightness/darkness for depth effect
        float brightness = 1.0f - (progress * 0.5f); // Darken as it flips
        fullCardImage.Mutate(tempCtx => tempCtx.Brightness(brightness));
        
        // Draw at correct position (top edge of bottom half, growing upward)
        int drawY = y + halfHeight;
        ctx.DrawImage(fullCardImage, new Point(x, drawY), 1f);
    }

    private void DrawFlippingTopHalf(IImageProcessingContext ctx, int x, int y, int width, int height, int digit, Font font, Color textColor, Color backgroundColor, float progress, bool flippingDown)
    {
        int halfHeight = height / 2;
        
        // Calculate perspective scale (0.0 at start, 1.0 at end)
        float perspectiveScale = (float)Math.Cos((1.0f - progress) * Math.PI / 2.0);
        int scaledHeight = Math.Max(1, (int)(halfHeight * perspectiveScale));
        
        if (scaledHeight <= 0) return;
        
        // Create full card image
        using var fullCardImage = new Image<Rgba32>(width, height);
        fullCardImage.Mutate(tempCtx =>
        {
            tempCtx.Fill(backgroundColor, new RectangleF(0, 0, width, height));
            
            var text = digit.ToString();
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(width / 2f, height / 2f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            tempCtx.DrawText(textOptions, text, textColor);
        });

        // Crop the top half
        fullCardImage.Mutate(tempCtx => tempCtx.Crop(new Rectangle(0, 0, width, halfHeight)));
        
        // Apply perspective scaling
        fullCardImage.Mutate(tempCtx => tempCtx.Resize(width, scaledHeight));
        
        // Apply brightness/darkness for depth effect
        float brightness = 1.0f - ((1.0f - progress) * 0.5f); // Brighten as it unfolds
        fullCardImage.Mutate(tempCtx => tempCtx.Brightness(brightness));
        
        // Draw at correct position (bottom edge of top half, growing downward)
        int drawY = y + (halfHeight - scaledHeight);
        ctx.DrawImage(fullCardImage, new Point(x, drawY), 1f);
    }

    private static float EaseInOutCubic(float t)
    {
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - (float)Math.Pow(-2f * t + 2f, 3f) / 2f;
    }
}

/// <summary>
/// Manages multiple flip cards to display multi-digit numbers with animations
/// </summary>
public class FlipNumberDisplay
{
    private readonly List<FlipNumberCard> _cards;
    private readonly int _digitCount;

    public int DigitCount => _digitCount;
    public bool IsAnimating => _cards.Any(card => card.IsAnimating);

    public FlipNumberDisplay(int digitCount)
    {
        if (digitCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(digitCount), "Digit count must be positive");
            
        _digitCount = digitCount;
        _cards = new List<FlipNumberCard>(digitCount);
        
        for (int i = 0; i < digitCount; i++)
        {
            _cards.Add(new FlipNumberCard(0));
        }
    }

    /// <summary>
    /// Sets the displayed number with animation
    /// </summary>
    public void SetNumber(int number, bool animate = true)
    {
        var digits = GetDigits(number, _digitCount);
        
        for (int i = 0; i < _digitCount; i++)
        {
            if (animate)
                _cards[i].FlipTo(digits[i]);
            else
                _cards[i].SetValue(digits[i]);
        }
    }

    /// <summary>
    /// Sets the displayed number from a string (useful for times like "12:34")
    /// </summary>
    public void SetNumberFromString(string numberString, bool animate = true)
    {
        var digits = numberString.Where(char.IsDigit).Select(c => c - '0').ToArray();
        
        for (int i = 0; i < Math.Min(_digitCount, digits.Length); i++)
        {
            if (animate)
                _cards[i].FlipTo(digits[i]);
            else
                _cards[i].SetValue(digits[i]);
        }
    }

    /// <summary>
    /// Updates all card animations
    /// </summary>
    public void Update(TimeSpan deltaTime, float animationDuration = 0.5f)
    {
        foreach (var card in _cards)
        {
            card.Update(deltaTime, animationDuration);
        }
    }

    /// <summary>
    /// Renders all cards horizontally with spacing
    /// </summary>
    public void Render(IImageProcessingContext ctx, int x, int y, int cardWidth, int cardHeight, int spacing, Font font, Color textColor, Color backgroundColor)
    {
        int currentX = x;
        
        foreach (var card in _cards)
        {
            card.Render(ctx, currentX, y, cardWidth, cardHeight, font, textColor, backgroundColor);
            currentX += cardWidth + spacing;
        }
    }

    /// <summary>
    /// Renders cards with custom positions (useful for adding colons in time displays)
    /// </summary>
    public void RenderAtPositions(IImageProcessingContext ctx, int[] xPositions, int y, int cardWidth, int cardHeight, Font font, Color textColor, Color backgroundColor)
    {
        for (int i = 0; i < Math.Min(_digitCount, xPositions.Length); i++)
        {
            _cards[i].Render(ctx, xPositions[i], y, cardWidth, cardHeight, font, textColor, backgroundColor);
        }
    }

    private static int[] GetDigits(int number, int digitCount)
    {
        var digits = new int[digitCount];
        int absNumber = Math.Abs(number);
        
        for (int i = digitCount - 1; i >= 0; i--)
        {
            digits[i] = absNumber % 10;
            absNumber /= 10;
        }
        
        return digits;
    }

    /// <summary>
    /// Gets a specific card by index
    /// </summary>
    public FlipNumberCard GetCard(int index)
    {
        if (index < 0 || index >= _digitCount)
            throw new ArgumentOutOfRangeException(nameof(index));
            
        return _cards[index];
    }
}
