using System.Drawing;
using System.Numerics;

namespace LedMatrixOS.Core.Animation;

/// <summary>
/// Linear interpolation for the types a <see cref="Tween{T}"/> can animate out of the box:
/// float, double, int, Pixel, Vector2 and System.Drawing.Point. <see cref="Lerp"/> is null for any other type.
/// t is not clamped (so overshooting easings work), except for Pixel whose own Lerp clamps.
/// </summary>
public static class Lerper<T>
{
    public static readonly Func<T, T, float, T>? Lerp = Create();

    private static Func<T, T, float, T>? Create()
    {
        object? lerp = null;
        if (typeof(T) == typeof(float)) lerp = (Func<float, float, float, float>)((a, b, t) => a + (b - a) * t);
        else if (typeof(T) == typeof(double)) lerp = (Func<double, double, float, double>)((a, b, t) => a + (b - a) * t);
        else if (typeof(T) == typeof(int)) lerp = (Func<int, int, float, int>)((a, b, t) => RoundToInt(a + (b - a) * t));
        else if (typeof(T) == typeof(Pixel)) lerp = (Func<Pixel, Pixel, float, Pixel>)Pixel.Lerp;
        else if (typeof(T) == typeof(Vector2)) lerp = (Func<Vector2, Vector2, float, Vector2>)Vector2.Lerp;
        else if (typeof(T) == typeof(Point)) lerp = (Func<Point, Point, float, Point>)((a, b, t) =>
            new Point(RoundToInt(a.X + (b.X - a.X) * t), RoundToInt(a.Y + (b.Y - a.Y) * t)));
        return (Func<T, T, float, T>?)lerp;
    }

    private static int RoundToInt(float v) => (int)MathF.Round(v, MidpointRounding.AwayFromZero);
}
