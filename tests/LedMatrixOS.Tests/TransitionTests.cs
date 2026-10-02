using LedMatrixOS.Core;
using LedMatrixOS.Core.Transitions;
using Xunit;

namespace LedMatrixOS.Tests;

public class TransitionTests
{
    private const int W = 256, H = 64;

    // Distinct, non-uniform content so any misplaced pixel shows up.
    private static FrameBuffer Gradient(byte seed) => SnapshotHelper.Render(f =>
    {
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                f.SetPixel(x, y, new Pixel((byte)(x + seed), (byte)(y * 4), seed));
    });

    private static FrameBuffer Solid(Pixel p) => SnapshotHelper.Render(f => f.Clear(p));

    private static bool Same(FrameBuffer a, FrameBuffer b) => a.GetPixelsSpan().SequenceEqual(b.GetPixelsSpan());

    public static IEnumerable<object[]> AllNames() =>
        new TransitionRegistry().Names.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Endpoints_AreFromAndTo(string name)
    {
        var t = Resolve(name);
        var from = Gradient(10);
        var to = Gradient(200);

        var atStart = new FrameBuffer(W, H);
        var atEnd = new FrameBuffer(W, H);
        t.Render(from, to, atStart, 0f);
        t.Render(from, to, atEnd, 1f);

        Assert.True(Same(from, atStart), $"{name} at 0 should equal from");
        Assert.True(Same(to, atEnd), $"{name} at 1 should equal to");
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Midpoint_IsMixAndDeterministic(string name)
    {
        var t = Resolve(name);
        var from = Solid(new Pixel(255, 0, 0));
        var to = Solid(new Pixel(0, 0, 255));

        var a = new FrameBuffer(W, H);
        var b = new FrameBuffer(W, H);
        t.Render(from, to, a, 0.5f);
        t.Render(from, to, b, 0.5f);
        Assert.True(Same(a, b));

        // Not just one of the inputs: some pixels show each side, or a blend of both
        Assert.False(Same(a, from));
        Assert.False(Same(a, to));
    }

    [Fact]
    public void Dissolve_MidProgress_FlipsRoughlyHalf()
    {
        var from = Solid(new Pixel(255, 0, 0));
        var to = Solid(new Pixel(0, 0, 255));
        var o = new FrameBuffer(W, H);
        new DissolveTransition().Render(from, to, o, 0.5f);

        int blue = o.GetPixelsSpan().ToArray().Count(p => p.B == 255);
        Assert.InRange(blue / (double)(W * H), 0.45, 0.55);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(63)]
    public void SlideUp_MatchesOldVerticalEngine(int offset)
    {
        var from = Gradient(10);
        var to = Gradient(200);
        var expected = new FrameBuffer(W, H);
        OldVertical(from, to, expected, offset);

        var actual = new FrameBuffer(W, H);
        new SlideTransition(MoveDirection.Up).Render(from, to, actual, offset / (float)H);
        Assert.True(Same(expected, actual));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(129)]
    [InlineData(255)]
    public void SlideLeft_MatchesOldHorizontalEngine(int offset)
    {
        var from = Gradient(10);
        var to = Gradient(200);
        var expected = new FrameBuffer(W, H);
        OldHorizontal(from, to, expected, offset);

        var actual = new FrameBuffer(W, H);
        new SlideTransition(MoveDirection.Left).Render(from, to, actual, offset / (float)W);
        Assert.True(Same(expected, actual));
    }

    [Fact]
    public void SlideDownAndRight_MoveOppositeToUpAndLeft()
    {
        var from = Gradient(10);
        var to = Gradient(200);
        var o = new FrameBuffer(W, H);

        new SlideTransition(MoveDirection.Down).Render(from, to, o, 0.25f); // offset 16
        Assert.Equal(from.GetPixel(5, 0), o.GetPixel(5, 16));
        Assert.Equal(to.GetPixel(5, H - 1), o.GetPixel(5, 15));

        new SlideTransition(MoveDirection.Right).Render(from, to, o, 0.25f); // offset 64
        Assert.Equal(from.GetPixel(0, 5), o.GetPixel(64, 5));
        Assert.Equal(to.GetPixel(W - 1, 5), o.GetPixel(63, 5));
    }

    [Fact]
    public void Registry_LooksUpByNameAndPicksRandom()
    {
        var r = new TransitionRegistry();
        Assert.True(r.TryGet("Crossfade", out var t));
        Assert.Equal("crossfade", t.Name);
        Assert.False(r.TryGet("nope", out _));
        Assert.True(r.IsValidName("random"));
        Assert.NotNull(r.Resolve("random"));
        Assert.Null(r.Resolve("nope"));
        Assert.Contains(r.Random().Name, r.Names);
    }

    [Theory]
    [InlineData("wipe", 0.35f)]
    [InlineData("iris", 0.4f)]
    [InlineData("dissolve", 0.5f)]
    public void Snapshot_MidProgress(string kind, float progress)
    {
        var from = Scene(new Pixel(200, 40, 40));
        var to = Scene(new Pixel(40, 80, 220));
        ITransition t = kind switch
        {
            "wipe" => new WipeTransition(MoveDirection.Right),
            "iris" => new IrisTransition(),
            _ => new DissolveTransition()
        };
        var o = new FrameBuffer(W, H);
        t.Render(from, to, o, progress);
        SnapshotHelper.AssertMatchesSnapshot(o, $"transition_{kind}_mid");
    }

    private static FrameBuffer Scene(Pixel c) => SnapshotHelper.Render(f =>
    {
        f.Clear(c);
        for (int y = 0; y < H; y += 8)
            f.Fill(new SixLabors.ImageSharp.Rectangle(0, y, W, 2), Pixel.White);
    });

    private static ITransition Resolve(string name)
    {
        Assert.True(new TransitionRegistry().TryGet(name, out var t));
        return t;
    }

    // Verbatim ports of the pre-ITransition RenderEngine slide code.
    private static void OldVertical(FrameBuffer oldFrame, FrameBuffer frame, FrameBuffer composite, int offset)
    {
        int oldFrameY = -offset;
        int newFrameY = H - offset;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int oldY = y - oldFrameY;
                if (oldY >= 0 && oldY < H) composite.SetPixel(x, y, oldFrame.GetPixel(x, oldY));
            }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int newY = y - newFrameY;
                if (newY >= 0 && newY < H) composite.SetPixel(x, y, frame.GetPixel(x, newY));
            }
    }

    private static void OldHorizontal(FrameBuffer oldFrame, FrameBuffer frame, FrameBuffer composite, int offset)
    {
        int oldFrameX = -offset;
        int newFrameX = W - offset;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int oldX = x - oldFrameX;
                if (oldX >= 0 && oldX < W) composite.SetPixel(x, y, oldFrame.GetPixel(oldX, y));
            }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int newX = x - newFrameX;
                if (newX >= 0 && newX < W) composite.SetPixel(x, y, frame.GetPixel(newX, y));
            }
    }
}
