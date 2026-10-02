using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using Xunit;

namespace LedMatrixOS.Tests;

public class FrameBroadcasterTests
{
    private static FrameBuffer Frame(Pixel p)
    {
        var f = new FrameBuffer(4, 2);
        f.Fill(new Rectangle(0, 0, 4, 2), p);
        f.SetPixel(1, 0, new Pixel(9, 8, 7));
        return f;
    }

    [Fact]
    public void WithoutSubscribers_NothingIsCopied()
    {
        var b = new FrameBroadcaster();
        b.Publish(Frame(new Pixel(1, 2, 3)));
        long last = 0; var msg = new byte[0];
        Assert.Equal(0, b.TryRead(ref last, ref msg));
    }

    [Fact]
    public void Subscriber_ReceivesPackedFrameOnce_ThenOnlyNewerOnes()
    {
        var b = new FrameBroadcaster();
        using var sub = b.Subscribe();
        b.Publish(Frame(new Pixel(1, 2, 3)));

        long last = 0; var msg = new byte[0];
        int length = b.TryRead(ref last, ref msg);

        Assert.Equal(4 + 4 * 2 * 3, length);
        Assert.Equal([4, 0, 2, 0], msg[..4]);
        Assert.Equal([1, 2, 3], msg[4..7]);
        Assert.Equal([9, 8, 7], msg[7..10]);          // pixel (1,0)
        Assert.Equal(0, b.TryRead(ref last, ref msg)); // nothing new

        b.Publish(Frame(new Pixel(5, 5, 5)));
        Assert.True(b.TryRead(ref last, ref msg) > 0);
        Assert.Equal([5, 5, 5], msg[4..7]);
    }

    [Fact]
    public void Subscribers_AreCountedAndDisposeIsIdempotent()
    {
        var b = new FrameBroadcaster();
        var a = b.Subscribe();
        var c = b.Subscribe();
        Assert.Equal(2, b.Subscribers);
        a.Dispose(); a.Dispose();
        Assert.Equal(1, b.Subscribers);
        c.Dispose();
        Assert.Equal(0, b.Subscribers);
    }
}
