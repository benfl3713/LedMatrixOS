using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class FrameBufferTests
{
    [Fact]
    public void SetPixel_InBounds_RoundTrips()
    {
        var f = new FrameBuffer(8, 4);
        f.SetPixel(7, 3, new Pixel(1, 2, 3));
        Assert.Equal(new Pixel(1, 2, 3), f.GetPixel(7, 3));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(8, 0)]
    [InlineData(0, 4)]
    public void SetPixel_OutOfBounds_IsIgnored(int x, int y)
    {
        var f = new FrameBuffer(8, 4);
        f.SetPixel(x, y, new Pixel(9, 9, 9));
        foreach (var p in f.GetPixelsSpan()) Assert.Equal(new Pixel(0, 0, 0), p);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(8, 0)]
    [InlineData(0, 4)]
    public void GetPixel_OutOfRange_ReturnsBlack(int x, int y)
    {
        var f = new FrameBuffer(8, 4);
        f.Clear(new Pixel(5, 5, 5));
        Assert.Equal(Pixel.Black, f.GetPixel(x, y));
    }

    [Fact]
    public void Clear_FillsEverything_AndDefaultsToBlack()
    {
        var f = new FrameBuffer(8, 4);
        f.Clear(new Pixel(10, 20, 30));
        foreach (var p in f.GetPixelsSpan()) Assert.Equal(new Pixel(10, 20, 30), p);
        f.Clear();
        foreach (var p in f.GetPixelsSpan()) Assert.Equal(new Pixel(0, 0, 0), p);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(4, -1));
    }
}
