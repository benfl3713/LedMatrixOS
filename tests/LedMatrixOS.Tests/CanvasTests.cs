using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LedMatrixOS.Tests;

public class CanvasTests
{
    private static readonly Pixel Red = new(255, 0, 0);
    private static readonly Pixel Green = new(0, 255, 0);
    private static readonly Pixel Blue = new(0, 0, 255);

    public CanvasTests()
    {
        Fonts.Load();
    }

    private static int Count(FrameBuffer f, Pixel color)
    {
        int n = 0;
        foreach (var p in f.GetPixelsSpan()) if (p == color) n++;
        return n;
    }

    // ---- clip stack ----

    [Fact]
    public void Clip_RestrictsSetPixelBlendFillAndCopy()
    {
        var f = new FrameBuffer(10, 10);
        f.PushClip(new Rectangle(2, 2, 3, 3));

        f.SetPixel(1, 2, Red);
        f.SetPixel(2, 2, Red);
        f.BlendPixel(5, 2, Red, 1f);
        f.BlendPixel(4, 4, Red, 1f);
        Assert.Equal(Pixel.Black, f.GetPixel(1, 2));
        Assert.Equal(Red, f.GetPixel(2, 2));
        Assert.Equal(Pixel.Black, f.GetPixel(5, 2));
        Assert.Equal(Red, f.GetPixel(4, 4));

        f.Fill(new Rectangle(0, 0, 10, 10), Green);
        Assert.Equal(9, Count(f, Green));
        Assert.Equal(Pixel.Black, f.GetPixel(1, 1));

        var src = new FrameBuffer(10, 10);
        src.Clear(Blue);
        f.CopyFrom(src);
        Assert.Equal(9, Count(f, Blue));
        Assert.Equal(Blue, f.GetPixel(3, 3));
        Assert.Equal(Pixel.Black, f.GetPixel(1, 1));
        Assert.Equal(Pixel.Black, f.GetPixel(5, 5));
    }

    [Fact]
    public void Clip_CopyFromWithOffsetRespectsClip()
    {
        var f = new FrameBuffer(10, 10);
        var src = new FrameBuffer(4, 4);
        src.Clear(Blue);
        f.PushClip(new Rectangle(0, 0, 5, 5));
        f.CopyFrom(src, 3, 3);
        Assert.Equal(4, Count(f, Blue)); // x,y in 3..4
        Assert.Equal(Pixel.Black, f.GetPixel(5, 5));
    }

    [Fact]
    public void Clip_NestsByIntersectionAndPopRestores()
    {
        var f = new FrameBuffer(10, 10);
        f.PushClip(new Rectangle(0, 0, 6, 6));
        f.PushClip(new Rectangle(4, 4, 6, 6)); // intersection is 4..5
        f.Fill(new Rectangle(0, 0, 10, 10), Red);
        Assert.Equal(4, Count(f, Red));

        f.PopClip();
        f.Fill(new Rectangle(0, 0, 10, 10), Green);
        Assert.Equal(36, Count(f, Green));
        Assert.Equal(Green, f.GetPixel(0, 0));
        Assert.Equal(Pixel.Black, f.GetPixel(6, 6));

        f.PopClip();
        f.SetPixel(9, 9, Blue);
        Assert.Equal(Blue, f.GetPixel(9, 9));
    }

    [Fact]
    public void Clip_DisjointRectDrawsNothing_AndStillPops()
    {
        var f = new FrameBuffer(10, 10);
        f.PushClip(new Rectangle(0, 0, 2, 2));
        f.PushClip(new Rectangle(5, 5, 2, 2));
        f.Fill(new Rectangle(0, 0, 10, 10), Red);
        f.SetPixel(5, 5, Red);
        Assert.Equal(0, Count(f, Red));
        f.PopClip();
        f.Fill(new Rectangle(0, 0, 10, 10), Red);
        Assert.Equal(4, Count(f, Red));
    }

    [Fact]
    public void Clip_RectOutsideBufferIsClampedToBuffer()
    {
        var f = new FrameBuffer(4, 4);
        f.PushClip(new Rectangle(-5, -5, 100, 100));
        f.Fill(new Rectangle(-5, -5, 100, 100), Red);
        Assert.Equal(16, Count(f, Red));
    }

    [Fact]
    public void PopClip_OnEmptyStack_Throws()
    {
        var f = new FrameBuffer(4, 4);
        Assert.Throws<InvalidOperationException>(() => f.PopClip());
        f.PushClip(new Rectangle(0, 0, 1, 1));
        f.PopClip();
        Assert.Throws<InvalidOperationException>(() => f.PopClip());
    }

    [Fact]
    public void Clip_AppliesToDrawText()
    {
        var f = new FrameBuffer(64, 20);
        f.PushClip(new Rectangle(0, 0, 8, 20));
        f.DrawText(Fonts.Small, 0, 2, Red, "WWWWWWWW", shadow: false);
        for (int y = 0; y < 20; y++)
            for (int x = 8; x < 64; x++)
                Assert.Equal(Pixel.Black, f.GetPixel(x, y));
        Assert.True(Count(f, Red) > 0);
    }

    // ---- lines ----

    [Fact]
    public void DrawLine_Diagonal_IncludesBothEndpoints()
    {
        var f = new FrameBuffer(8, 8);
        f.DrawLine(1, 1, 6, 6, Red);
        for (int i = 1; i <= 6; i++) Assert.Equal(Red, f.GetPixel(i, i));
        Assert.Equal(6, Count(f, Red));
    }

    [Fact]
    public void DrawLine_ReversedAndSinglePointAndAxisAligned()
    {
        var f = new FrameBuffer(8, 8);
        f.DrawLine(6, 1, 1, 1, Red);
        Assert.Equal(6, Count(f, Red));
        Assert.Equal(Red, f.GetPixel(1, 1));
        Assert.Equal(Red, f.GetPixel(6, 1));

        var g = new FrameBuffer(8, 8);
        g.DrawLine(3, 3, 3, 3, Red);
        Assert.Equal(1, Count(g, Red));

        var h = new FrameBuffer(8, 8);
        h.DrawLine(2, 6, 2, 1, Red);
        Assert.Equal(6, Count(h, Red));
    }

    [Fact]
    public void DrawLine_Shallow_HasOnePixelPerColumn()
    {
        var f = new FrameBuffer(16, 8);
        f.DrawLine(0, 0, 10, 4, Red);
        Assert.Equal(11, Count(f, Red));
        Assert.Equal(Red, f.GetPixel(0, 0));
        Assert.Equal(Red, f.GetPixel(10, 4));
    }

    // ---- shapes ----

    [Fact]
    public void FillRect_And_DrawRect()
    {
        var f = new FrameBuffer(10, 10);
        f.FillRect(new Rectangle(1, 1, 4, 3), Red);
        Assert.Equal(12, Count(f, Red));

        var g = new FrameBuffer(10, 10);
        g.DrawRect(new Rectangle(1, 1, 5, 5), Red);
        Assert.Equal(16, Count(g, Red));
        Assert.Equal(Pixel.Black, g.GetPixel(3, 3));
    }

    [Fact]
    public void FillCircle_IsSymmetricAndCoversCentreAndAxisExtremes()
    {
        var f = new FrameBuffer(21, 21);
        f.FillCircle(10, 10, 5, Red);
        Assert.Equal(Red, f.GetPixel(10, 10));
        Assert.Equal(Red, f.GetPixel(5, 10));
        Assert.Equal(Red, f.GetPixel(15, 10));
        Assert.Equal(Red, f.GetPixel(10, 5));
        Assert.Equal(Red, f.GetPixel(10, 15));
        Assert.Equal(Pixel.Black, f.GetPixel(4, 10));
        Assert.Equal(Pixel.Black, f.GetPixel(5, 5)); // corner of the bounding box
        for (int y = 0; y < 21; y++)
            for (int x = 0; x < 21; x++)
            {
                Assert.Equal(f.GetPixel(x, y), f.GetPixel(20 - x, y));
                Assert.Equal(f.GetPixel(x, y), f.GetPixel(x, 20 - y));
            }
    }

    [Fact]
    public void DrawCircle_IsHollow()
    {
        var f = new FrameBuffer(21, 21);
        f.DrawCircle(10, 10, 6, Red);
        Assert.Equal(Pixel.Black, f.GetPixel(10, 10));
        Assert.Equal(Red, f.GetPixel(4, 10));
        Assert.Equal(Red, f.GetPixel(16, 10));
        var filled = new FrameBuffer(21, 21);
        filled.FillCircle(10, 10, 6, Red);
        Assert.True(Count(f, Red) < Count(filled, Red));
    }

    [Fact]
    public void FillRoundedRect_CutsCornersButKeepsEdgeMidpoints()
    {
        var f = new FrameBuffer(20, 12);
        f.FillRoundedRect(new Rectangle(0, 0, 20, 12), 4, Red);
        Assert.Equal(Pixel.Black, f.GetPixel(0, 0));
        Assert.Equal(Pixel.Black, f.GetPixel(19, 0));
        Assert.Equal(Pixel.Black, f.GetPixel(0, 11));
        Assert.Equal(Pixel.Black, f.GetPixel(19, 11));
        Assert.Equal(Red, f.GetPixel(10, 0));
        Assert.Equal(Red, f.GetPixel(0, 6));
        Assert.Equal(Red, f.GetPixel(10, 6));
    }

    [Fact]
    public void FillRoundedRect_ZeroRadiusEqualsFillRect()
    {
        var a = new FrameBuffer(10, 10);
        var b = new FrameBuffer(10, 10);
        a.FillRoundedRect(new Rectangle(1, 2, 6, 5), 0, Red);
        b.FillRect(new Rectangle(1, 2, 6, 5), Red);
        Assert.Equal(b.GetPixelsSpan().ToArray(), a.GetPixelsSpan().ToArray());
    }

    [Fact]
    public void DrawRoundedRect_IsHollow()
    {
        var f = new FrameBuffer(20, 12);
        f.DrawRoundedRect(new Rectangle(0, 0, 20, 12), 3, Red);
        Assert.Equal(Pixel.Black, f.GetPixel(10, 6));
        Assert.Equal(Red, f.GetPixel(10, 0));
        Assert.Equal(Red, f.GetPixel(0, 6));
    }

    // ---- gradients ----

    [Fact]
    public void LinearGradient_HorizontalEndpointsAndMonotonic()
    {
        var f = new FrameBuffer(10, 3);
        f.FillLinearGradient(new Rectangle(0, 0, 10, 3), Pixel.Black, Pixel.White);
        Assert.Equal(Pixel.Black, f.GetPixel(0, 1));
        Assert.Equal(Pixel.White, f.GetPixel(9, 1));
        for (int x = 1; x < 10; x++) Assert.True(f.GetPixel(x, 1).R >= f.GetPixel(x - 1, 1).R);
        Assert.Equal(f.GetPixel(4, 0), f.GetPixel(4, 2));
    }

    [Fact]
    public void LinearGradient_VerticalEndpoints_AndSingleStep()
    {
        var f = new FrameBuffer(3, 10);
        f.FillLinearGradient(new Rectangle(0, 0, 3, 10), Red, Blue, vertical: true);
        Assert.Equal(Red, f.GetPixel(1, 0));
        Assert.Equal(Blue, f.GetPixel(1, 9));

        var g = new FrameBuffer(1, 1);
        g.FillLinearGradient(new Rectangle(0, 0, 1, 1), Red, Blue);
        Assert.Equal(Red, g.GetPixel(0, 0));
    }

    [Fact]
    public void RadialGradient_CentreIsCentreColourAndOutsideUntouched()
    {
        var f = new FrameBuffer(21, 21);
        f.FillRadialGradient(10, 10, 8, Pixel.White, Blue);
        Assert.Equal(Pixel.White, f.GetPixel(10, 10));
        Assert.NotEqual(Pixel.Black, f.GetPixel(18, 10));
        Assert.Equal(Pixel.Black, f.GetPixel(10, 1));
        Assert.Equal(Pixel.Black, f.GetPixel(0, 0));
    }

    // ---- progress bar ----

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 5)]
    [InlineData(1f, 10)]
    [InlineData(2f, 10)]
    [InlineData(-1f, 0)]
    public void ProgressBar_FillsProportionally(float progress, int filled)
    {
        var f = new FrameBuffer(10, 2);
        f.DrawProgressBar(new Rectangle(0, 0, 10, 2), progress, Green, Red);
        for (int x = 0; x < 10; x++)
            Assert.Equal(x < filled ? Green : Red, f.GetPixel(x, 0));
    }

    [Fact]
    public void ProgressBar_WithBorder_DrawsBorderAndInsetFill()
    {
        var f = new FrameBuffer(12, 5);
        f.DrawProgressBar(new Rectangle(0, 0, 12, 5), 0.5f, Green, Blue, border: Red);
        Assert.Equal(Red, f.GetPixel(0, 0));
        Assert.Equal(Red, f.GetPixel(11, 4));
        Assert.Equal(Green, f.GetPixel(1, 1));
        Assert.Equal(Green, f.GetPixel(5, 3));
        Assert.Equal(Blue, f.GetPixel(6, 1));
        Assert.Equal(Blue, f.GetPixel(10, 3));
    }

    // ---- text ----

    [Fact]
    public void MeasureText_EmptyIsZero_AndGrowsWithLength()
    {
        Assert.Equal(0, Fonts.Small.MeasureText(""));
        var one = Fonts.Small.MeasureText("A");
        Assert.True(one > 0);
        Assert.Equal(one * 6, Fonts.Small.MeasureText("ABCDEF"));
        Assert.Equal(Fonts.Big.MeasureText("a") * 3, Fonts.Big.MeasureText("abc"));
    }

    [Fact]
    public void TruncateWithEllipsis_FitsOrShortens()
    {
        var font = Fonts.Small;
        Assert.Equal("Hello", font.TruncateWithEllipsis("Hello", 100));
        var t = font.TruncateWithEllipsis("Hello world, long text", 60);
        Assert.EndsWith("...", t);
        Assert.True(font.MeasureText(t) <= 60);
        Assert.StartsWith("Hello", t);
        Assert.Equal("", font.TruncateWithEllipsis("Hello", 5)); // ellipsis alone does not fit
        Assert.Equal("...", font.TruncateWithEllipsis("Hello world", font.MeasureText("...")));
    }

    [Fact]
    public void DrawText_AlignmentMovesTheAnchor()
    {
        var style = new TextStyle(Fonts.Small, Red, Shadow: false);
        int w = Fonts.Small.MeasureText("ABCD");

        static (int min, int max) Extents(FrameBuffer f)
        {
            int min = int.MaxValue, max = -1;
            for (int y = 0; y < f.Height; y++)
                for (int x = 0; x < f.Width; x++)
                    if (f.GetPixel(x, y) != Pixel.Black) { min = Math.Min(min, x); max = Math.Max(max, x); }
            return (min, max);
        }

        FrameBuffer Draw(TextAlign a)
        {
            var f = new FrameBuffer(100, 20);
            f.DrawText(style, 50, 2, "ABCD", a);
            return f;
        }

        var left = Extents(Draw(TextAlign.Left));
        var center = Extents(Draw(TextAlign.Center));
        var right = Extents(Draw(TextAlign.Right));

        Assert.InRange(left.min, 50, 50 + w);
        Assert.Equal(left.min - w / 2, center.min);
        Assert.Equal(left.min - w, right.min);
        Assert.True(right.max < 50);
    }

    [Fact]
    public void DrawText_ShadowDrawsOffsetCopyInShadowColour()
    {
        var f = new FrameBuffer(40, 20);
        f.DrawText(new TextStyle(Fonts.Small, Red, true, Blue), 2, 2, "I");
        Assert.True(Count(f, Red) > 0);
        Assert.True(Count(f, Blue) > 0);
        var g = new FrameBuffer(40, 20);
        g.DrawText(new TextStyle(Fonts.Small, Red, false), 2, 2, "I");
        Assert.Equal(0, Count(g, Blue));
        Assert.Equal(Count(f, Red), Count(g, Red));
    }

    [Fact]
    public void DrawText_EmptyStringDrawsNothing()
    {
        var f = new FrameBuffer(40, 20);
        f.DrawText(new TextStyle(Fonts.Small, Red), 2, 2, "");
        Assert.True(SnapshotHelper.IsBlank(f));
    }

    // ---- sprite ----

    [Fact]
    public void Sprite_FromImage_DecodesPixelsAlphaAndSize()
    {
        using var image = new Image<Rgba32>(3, 2);
        image[0, 0] = new Rgba32(255, 0, 0, 255);
        image[2, 1] = new Rgba32(1, 2, 3, 128);
        var sprite = Sprite.FromImage(image);
        Assert.Equal(3, sprite.Width);
        Assert.Equal(2, sprite.Height);
        Assert.Single(sprite.Frames);
        Assert.Equal(Red, sprite.Frames[0].Pixels[0]);
        Assert.Equal(255, sprite.Frames[0].Alpha[0]);
        Assert.Equal(new Pixel(1, 2, 3), sprite.Frames[0].Pixels[1 * 3 + 2]);
        Assert.Equal(128, sprite.Frames[0].Alpha[1 * 3 + 2]);
        Assert.Equal(0, sprite.FrameIndexAt(TimeSpan.FromHours(5)));
    }

    [Fact]
    public void Sprite_FrameIndexAt_LoopsThroughFrameDurations()
    {
        var one = new Pixel[1];
        var a = new byte[1];
        var sprite = new Sprite(1, 1, new[]
        {
            new Sprite.SpriteFrame(one, a, TimeSpan.FromMilliseconds(100)),
            new Sprite.SpriteFrame(one, a, TimeSpan.FromMilliseconds(200)),
            new Sprite.SpriteFrame(one, a, TimeSpan.FromMilliseconds(100)),
        });
        Assert.Equal(0, sprite.FrameIndexAt(TimeSpan.Zero));
        Assert.Equal(0, sprite.FrameIndexAt(TimeSpan.FromMilliseconds(99)));
        Assert.Equal(1, sprite.FrameIndexAt(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(1, sprite.FrameIndexAt(TimeSpan.FromMilliseconds(299)));
        Assert.Equal(2, sprite.FrameIndexAt(TimeSpan.FromMilliseconds(300)));
        Assert.Equal(0, sprite.FrameIndexAt(TimeSpan.FromMilliseconds(400)));
        Assert.Equal(1, sprite.FrameIndexAt(TimeSpan.FromMilliseconds(550)));
    }

    [Fact]
    public void Sprite_FromImage_MultiFrameUsesDefaultDuration()
    {
        using var image = new Image<Rgba32>(2, 2);
        image.Frames.AddFrame(image.Frames.RootFrame);
        var sprite = Sprite.FromImage(image);
        Assert.Equal(2, sprite.Frames.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(100), sprite.Frames[0].Duration);
        Assert.Equal(1, sprite.FrameIndexAt(TimeSpan.FromMilliseconds(150)));
        Assert.Equal(0, sprite.FrameIndexAt(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void Sprite_RequiresAtLeastOneFrame()
        => Assert.Throws<ArgumentException>(() => new Sprite(1, 1, Array.Empty<Sprite.SpriteFrame>()));

    // ---- golden scenes ----

    [Fact]
    public void ComposedScene_Snapshot()
    {
        var frame = SnapshotHelper.Render(f =>
        {
            f.FillLinearGradient(new Rectangle(0, 0, 256, 64), new Pixel(10, 0, 40), new Pixel(0, 40, 60));
            f.FillRoundedRect(new Rectangle(4, 4, 80, 28), 6, new Pixel(200, 40, 40));
            f.DrawRoundedRect(new Rectangle(4, 4, 80, 28), 6, Pixel.White);
            f.FillCircle(110, 18, 12, new Pixel(255, 200, 0));
            f.DrawCircle(110, 18, 14, Pixel.White);
            f.FillRadialGradient(150, 18, 12, Pixel.White, new Pixel(0, 0, 255));
            f.DrawLine(170, 4, 250, 32, new Pixel(0, 255, 0));
            f.DrawRect(new Rectangle(172, 38, 40, 10), Pixel.White);
            f.DrawProgressBar(new Rectangle(4, 38, 120, 8), 0.65f, new Pixel(0, 200, 0), new Pixel(40, 40, 40), Pixel.White);
            f.PushClip(new Rectangle(130, 36, 40, 14));
            f.FillRect(new Rectangle(120, 30, 60, 30), new Pixel(90, 0, 90));
            f.DrawText(Fonts.Small, 128, 38, Pixel.White, "Clipped text overflow");
            f.PopClip();
            var left = new TextStyle(Fonts.Small, Pixel.White);
            f.DrawText(left, 4, 52, "Left");
            f.DrawText(left with { Color = new Pixel(255, 255, 0) }, 128, 52, "Center", TextAlign.Center);
            f.DrawText(left with { Color = new Pixel(0, 255, 255) }, 252, 52, "Right", TextAlign.Right);
        });
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, "canvas_scene");
    }

    [Fact]
    public void ColourScene_Snapshot()
    {
        var frame = SnapshotHelper.Render(f =>
        {
            for (int x = 0; x < 256; x++)
                f.FillRect(new Rectangle(x, 0, 1, 16), Pixel.FromHsv(x / 256f * 360f, 1f, 1f));
            f.FillLinearGradient(new Rectangle(0, 20, 256, 12), Pixel.FromHex("#F00"), Pixel.FromHex("#00F"));
            f.FillLinearGradient(new Rectangle(0, 34, 256, 12), Pixel.Black, Pixel.White.WithBrightness(0.5f));
            f.FillRect(new Rectangle(0, 48, 256, 16), Pixel.Black.Blend(Pixel.FromHex("#FF8000"), 0.5f));
        });
        SnapshotHelper.AssertMatchesSnapshot(frame, "colour_scene");
    }
}
