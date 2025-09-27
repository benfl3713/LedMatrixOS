using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LedMatrixOS.Apps;

public static class ColorExtensions
{
    /// <summary>
    /// Creates a Color from HSV (Hue, Saturation, Value) values.
    /// </summary>
    /// <param name="hue">Hue in degrees (0-360)</param>
    /// <param name="saturation">Saturation (0.0-1.0)</param>
    /// <param name="value">Value/Brightness (0.0-1.0)</param>
    /// <returns>A Color instance</returns>
    public static Color FromHsv(float hue, float saturation, float value)
    {
        // Normalize hue to 0-360 range
        hue = hue % 360.0f;
        if (hue < 0) hue += 360.0f;

        // Clamp saturation and value to 0-1 range
        saturation = Math.Clamp(saturation, 0.0f, 1.0f);
        value = Math.Clamp(value, 0.0f, 1.0f);

        float c = value * saturation;
        float x = c * (1 - Math.Abs((hue / 60.0f) % 2 - 1));
        float m = value - c;

        float r, g, b;

        if (hue >= 0 && hue < 60)
        {
            r = c; g = x; b = 0;
        }
        else if (hue >= 60 && hue < 120)
        {
            r = x; g = c; b = 0;
        }
        else if (hue >= 120 && hue < 180)
        {
            r = 0; g = c; b = x;
        }
        else if (hue >= 180 && hue < 240)
        {
            r = 0; g = x; b = c;
        }
        else if (hue >= 240 && hue < 300)
        {
            r = x; g = 0; b = c;
        }
        else
        {
            r = c; g = 0; b = x;
        }

        byte red = (byte)Math.Round((r + m) * 255);
        byte green = (byte)Math.Round((g + m) * 255);
        byte blue = (byte)Math.Round((b + m) * 255);

        return Color.FromRgb(red, green, blue);
    }

    /// <summary>
    /// Creates a Color from HSV values with alpha.
    /// </summary>
    /// <param name="hue">Hue in degrees (0-360)</param>
    /// <param name="saturation">Saturation (0.0-1.0)</param>
    /// <param name="value">Value/Brightness (0.0-1.0)</param>
    /// <param name="alpha">Alpha/Opacity (0.0-1.0)</param>
    /// <returns>A Color instance</returns>
    public static Color FromHsva(float hue, float saturation, float value, float alpha)
    {
        var color = FromHsv(hue, saturation, value).ToPixel<Rgb24>();
        byte alphaB = (byte)Math.Round(Math.Clamp(alpha, 0.0f, 1.0f) * 255);
        return Color.FromRgba(color.R, color.G, color.B, alphaB);
    }
}
