using System.Globalization;

namespace LedMatrixOS.Graphics.UI;

public enum Orientation { Horizontal, Vertical }

/// <summary>
/// How a node sits in the slot its parent gives it along one axis. Stretch fills the slot; the others keep the node's own size.
/// </summary>
public enum Align { Start, Center, End, Stretch }

/// <summary>
/// Per-side spacing in pixels. An int converts implicitly to an equal thickness on all sides.
/// </summary>
public readonly record struct Thickness(int Left, int Top, int Right, int Bottom)
{
    public Thickness(int all) : this(all, all, all, all) { }
    public Thickness(int horizontal, int vertical) : this(horizontal, vertical, horizontal, vertical) { }

    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;

    public static implicit operator Thickness(int all) => new(all);
}

public enum GridUnit { Pixel, Star, Auto }

/// <summary>
/// Size of a grid row or column: a fixed number of pixels, a share of the leftover space (<c>*</c>, <c>2*</c>) or as large as its content (<c>auto</c>).
/// </summary>
public readonly record struct GridLength(GridUnit Unit, float Value)
{
    public static GridLength Auto => new(GridUnit.Auto, 0);
    public static GridLength Pixels(int pixels) => new(GridUnit.Pixel, pixels);
    public static GridLength Star(float weight = 1) => new(GridUnit.Star, weight);

    public static implicit operator GridLength(int pixels) => Pixels(pixels);

    /// <summary>Parses a comma separated list such as <c>"16,*,2*,auto"</c>.</summary>
    public static GridLength[] ParseList(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new GridLength[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Equals("auto", StringComparison.OrdinalIgnoreCase)) result[i] = Auto;
            else if (p.EndsWith('*'))
                result[i] = Star(p.Length == 1 ? 1f : float.Parse(p[..^1], CultureInfo.InvariantCulture));
            else result[i] = Pixels(int.Parse(p, CultureInfo.InvariantCulture));
        }
        return result;
    }
}
