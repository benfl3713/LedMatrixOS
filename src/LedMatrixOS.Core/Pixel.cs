using System.Runtime.CompilerServices;

namespace LedMatrixOS.Core;

public readonly record struct Pixel(byte R, byte G, byte B)
{
    public static readonly Pixel Black = new(0, 0, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out byte r, out byte g, out byte b)
    {
        r = R; g = G; b = B;
    }

    public static Pixel operator /(Pixel pixel, int divisor) => new Pixel((byte)(pixel.R / divisor), (byte)(pixel.G / divisor), (byte)(pixel.B / divisor));
}

