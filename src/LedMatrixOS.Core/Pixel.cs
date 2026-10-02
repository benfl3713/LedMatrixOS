using System.Globalization;
using System.Runtime.CompilerServices;

namespace LedMatrixOS.Core;

public readonly record struct Pixel(byte R, byte G, byte B)
{
    public static readonly Pixel Black = new(0, 0, 0);
    public static readonly Pixel White = new(255, 255, 255);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out byte r, out byte g, out byte b)
    {
        r = R; g = G; b = B;
    }

    public static Pixel operator /(Pixel pixel, int divisor) => new Pixel((byte)(pixel.R / divisor), (byte)(pixel.G / divisor), (byte)(pixel.B / divisor));

    /// <summary>
    /// Creates a Pixel from HSV. Hue in degrees (wraps), saturation and value 0-1.
    /// </summary>
    public static Pixel FromHsv(float hue, float saturation, float value)
    {
        hue %= 360f;
        if (hue < 0) hue += 360f;
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);

        float c = value * saturation;
        float x = c * (1 - Math.Abs((hue / 60f) % 2 - 1));
        float m = value - c;

        (float r, float g, float b) = (int)(hue / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return new Pixel(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    /// <summary>
    /// Parses "#RGB" or "#RRGGBB" (the leading # is optional).
    /// </summary>
    public static Pixel FromHex(string hex)
    {
        if (!TryParseHex(hex, out var pixel)) throw new FormatException($"Invalid hex colour '{hex}'");
        return pixel;
    }

    public static bool TryParseHex(string? hex, out Pixel pixel)
    {
        pixel = Black;
        if (hex is null) return false;
        var s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#') s = s[1..];

        const NumberStyles hexStyle = NumberStyles.AllowHexSpecifier;
        if (s.Length == 3 &&
            byte.TryParse(s.Slice(0, 1), hexStyle, null, out var r1) &&
            byte.TryParse(s.Slice(1, 1), hexStyle, null, out var g1) &&
            byte.TryParse(s.Slice(2, 1), hexStyle, null, out var b1))
        {
            pixel = new Pixel((byte)(r1 * 17), (byte)(g1 * 17), (byte)(b1 * 17));
            return true;
        }

        if (s.Length == 6 &&
            byte.TryParse(s.Slice(0, 2), hexStyle, null, out var r) &&
            byte.TryParse(s.Slice(2, 2), hexStyle, null, out var g) &&
            byte.TryParse(s.Slice(4, 2), hexStyle, null, out var b))
        {
            pixel = new Pixel(r, g, b);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Linear interpolation between two colours; t is clamped to 0-1.
    /// </summary>
    public static Pixel Lerp(Pixel a, Pixel b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Pixel(
            (byte)(a.R + (b.R - a.R) * t + 0.5f),
            (byte)(a.G + (b.G - a.G) * t + 0.5f),
            (byte)(a.B + (b.B - a.B) * t + 0.5f));
    }

    /// <summary>
    /// Scales the colour by a factor (0 = black, 1 = unchanged, above 1 brightens, clamped).
    /// </summary>
    public Pixel WithBrightness(float factor)
    {
        if (factor < 0f) factor = 0f;
        return new Pixel(ToByte(R / 255f * factor), ToByte(G / 255f * factor), ToByte(B / 255f * factor));
    }

    /// <summary>
    /// Draws <paramref name="src"/> over this colour with the given opacity (0-1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pixel Blend(Pixel src, float alpha) => Lerp(this, src, alpha);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);
}
