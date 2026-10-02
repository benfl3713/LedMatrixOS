using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class PixelTests
{
    [Theory]
    [InlineData(0f, 1f, 1f, 255, 0, 0)]
    [InlineData(60f, 1f, 1f, 255, 255, 0)]
    [InlineData(120f, 1f, 1f, 0, 255, 0)]
    [InlineData(180f, 1f, 1f, 0, 255, 255)]
    [InlineData(240f, 1f, 1f, 0, 0, 255)]
    [InlineData(300f, 1f, 1f, 255, 0, 255)]
    [InlineData(360f, 1f, 1f, 255, 0, 0)]
    [InlineData(-120f, 1f, 1f, 0, 0, 255)]
    [InlineData(0f, 0f, 1f, 255, 255, 255)]
    [InlineData(123f, 1f, 0f, 0, 0, 0)]
    [InlineData(0f, 1f, 0.5f, 128, 0, 0)]
    public void FromHsv_KnownValues(float h, float s, float v, int r, int g, int b)
        => Assert.Equal(new Pixel((byte)r, (byte)g, (byte)b), Pixel.FromHsv(h, s, v));

    [Fact]
    public void FromHsv_ClampsSaturationAndValue()
        => Assert.Equal(new Pixel(255, 0, 0), Pixel.FromHsv(0f, 5f, 5f));

    [Theory]
    [InlineData("#FF8000", 255, 128, 0)]
    [InlineData("ff8000", 255, 128, 0)]
    [InlineData("#F80", 255, 136, 0)]
    [InlineData("  #abc ", 170, 187, 204)]
    public void FromHex_Parses(string hex, int r, int g, int b)
        => Assert.Equal(new Pixel((byte)r, (byte)g, (byte)b), Pixel.FromHex(hex));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData("#+F0")]
    public void TryParseHex_RejectsInvalid(string? hex)
    {
        Assert.False(Pixel.TryParseHex(hex, out var p));
        Assert.Equal(Pixel.Black, p);
        if (hex != null) Assert.Throws<FormatException>(() => Pixel.FromHex(hex));
    }

    [Fact]
    public void Lerp_EndpointsMidpointAndClamp()
    {
        var a = new Pixel(0, 100, 255);
        var b = new Pixel(255, 100, 0);
        Assert.Equal(a, Pixel.Lerp(a, b, 0f));
        Assert.Equal(b, Pixel.Lerp(a, b, 1f));
        Assert.Equal(a, Pixel.Lerp(a, b, -3f));
        Assert.Equal(b, Pixel.Lerp(a, b, 7f));
        Assert.Equal(new Pixel(128, 100, 128), Pixel.Lerp(a, b, 0.5f));
    }

    [Fact]
    public void WithBrightness_ScalesAndClamps()
    {
        var p = new Pixel(100, 200, 50);
        Assert.Equal(Pixel.Black, p.WithBrightness(0f));
        Assert.Equal(Pixel.Black, p.WithBrightness(-1f));
        Assert.Equal(p, p.WithBrightness(1f));
        Assert.Equal(new Pixel(50, 100, 25), p.WithBrightness(0.5f));
        Assert.Equal(new Pixel(200, 255, 100), p.WithBrightness(2f));
    }

    [Fact]
    public void Blend_DrawsSourceOverDestination()
    {
        var dst = new Pixel(0, 0, 0);
        var src = new Pixel(200, 100, 50);
        Assert.Equal(dst, dst.Blend(src, 0f));
        Assert.Equal(src, dst.Blend(src, 1f));
        Assert.Equal(new Pixel(100, 50, 25), dst.Blend(src, 0.5f));
    }
}
