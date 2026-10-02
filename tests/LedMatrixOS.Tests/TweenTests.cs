using System.Drawing;
using System.Numerics;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using Xunit;

namespace LedMatrixOS.Tests;

public class TweenTests
{
    [Fact]
    public void DurationHelpers()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), 250.Ms());
        Assert.Equal(TimeSpan.FromMilliseconds(1.5), 1.5.Ms());
        Assert.Equal(TimeSpan.FromSeconds(8), 8.Seconds());
        Assert.Equal(TimeSpan.FromSeconds(0.5), 0.5.Seconds());
    }

    [Fact]
    public void Idle_HoldsInitialValue()
    {
        var t = new Tween<float>(3f);
        Assert.Equal(3f, t.Value);
        Assert.False(t.IsRunning);
        Assert.True(t.IsFinished);
        Assert.Equal(1.Seconds(), t.Update(1.Seconds()));
        Assert.Equal(3f, t.Value);
    }

    [Fact]
    public void LinearProgress_AndBoundaries()
    {
        var t = new Tween<float>(0f);
        t.To(10f, 1.Seconds());
        Assert.True(t.IsRunning);
        Assert.Equal(0f, t.Value);

        t.Update(0.Ms());
        Assert.Equal(0f, t.Value);
        t.Update(250.Ms());
        Assert.Equal(2.5f, t.Value, 4);
        t.Update(750.Ms());
        Assert.Equal(10f, t.Value);
        Assert.False(t.IsRunning);
    }

    [Fact]
    public void Update_ReturnsLeftoverOnCompletion()
    {
        var t = new Tween<float>(0f);
        t.To(1f, 100.Ms());
        Assert.Equal(TimeSpan.Zero, t.Update(40.Ms()));
        Assert.Equal(160.Ms(), t.Update(220.Ms()));
    }

    [Fact]
    public void Easing_IsApplied()
    {
        var t = new Tween<float>(0f);
        t.To(100f, 1.Seconds(), Easing.InQuad);
        t.Update(500.Ms());
        Assert.Equal(25f, t.Value, 3);
    }

    [Fact]
    public void OvershootEasing_GoesPastTargetThenLandsExactly()
    {
        var t = new Tween<float>(0f);
        t.To(100f, 1.Seconds(), Easing.OutBack);
        var max = 0f;
        for (var i = 0; i < 10; i++)
        {
            t.Update(100.Ms());
            max = Math.Max(max, t.Value);
        }

        Assert.True(max > 100f);
        Assert.Equal(100f, t.Value);
    }

    [Fact]
    public void Retarget_ContinuesFromCurrentValue()
    {
        var t = new Tween<float>(0f);
        t.To(100f, 1.Seconds());
        t.Update(500.Ms());
        Assert.Equal(50f, t.Value, 3);

        t.To(0f, 1.Seconds());
        Assert.Equal(50f, t.Value, 3);
        t.Update(500.Ms());
        Assert.Equal(25f, t.Value, 3);
        t.Update(500.Ms());
        Assert.Equal(0f, t.Value);
    }

    [Fact]
    public void Retarget_DiscardsOldCallback()
    {
        var fired = 0;
        var t = new Tween<float>(0f);
        t.To(1f, 1.Seconds(), onComplete: () => fired += 100);
        t.Update(500.Ms());
        t.To(2f, 1.Seconds(), onComplete: () => fired += 1);
        t.Update(1.Seconds());
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ZeroDuration_JumpsAndFiresCallback()
    {
        var fired = 0;
        var t = new Tween<int>(0);
        t.To(7, TimeSpan.Zero, onComplete: () => fired++);
        Assert.Equal(7, t.Value);
        Assert.False(t.IsRunning);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Callback_FiresOnceAndMayRetarget()
    {
        var fired = 0;
        var t = new Tween<float>(0f);
        t.To(1f, 100.Ms(), onComplete: () =>
        {
            fired++;
            t!.To(2f, 100.Ms());
        });
        t.Update(100.Ms());
        Assert.Equal(1, fired);
        Assert.True(t.IsRunning);
        t.Update(100.Ms());
        Assert.Equal(1, fired);
        Assert.Equal(2f, t.Value);
    }

    [Fact]
    public void SetAndCancel()
    {
        var fired = false;
        var t = new Tween<float>(0f);
        t.To(10f, 1.Seconds(), onComplete: () => fired = true);
        t.Update(500.Ms());
        t.Cancel();
        Assert.False(t.IsRunning);
        Assert.Equal(5f, t.Value, 3);

        t.To(10f, 1.Seconds());
        t.Set(1f);
        Assert.Equal(1f, t.Value);
        Assert.Equal(1f, t.Target);
        Assert.False(t.IsRunning);
        Assert.False(fired);
    }

    [Fact]
    public void Deterministic_RegardlessOfStepSize()
    {
        var a = new Tween<float>(0f);
        var b = new Tween<float>(0f);
        a.To(50f, 1.Seconds(), Easing.InOutCubic);
        b.To(50f, 1.Seconds(), Easing.InOutCubic);

        a.Update(600.Ms());
        for (var i = 0; i < 60; i++) b.Update(10.Ms());
        Assert.Equal(a.Value, b.Value, 3);

        var c = new Tween<float>(0f);
        c.To(50f, 1.Seconds(), Easing.InOutCubic);
        c.Update(600.Ms());
        Assert.Equal(a.Value, c.Value);
    }

    [Fact]
    public void Lerp_Int_RoundsToNearest()
    {
        var t = new Tween<int>(0);
        t.To(10, 1.Seconds());
        t.Update(250.Ms());
        Assert.Equal(3, t.Value);
        t.Update(750.Ms());
        Assert.Equal(10, t.Value);
    }

    [Fact]
    public void Lerp_Double()
    {
        var t = new Tween<double>(0.0);
        t.To(1.0, 1.Seconds());
        t.Update(250.Ms());
        Assert.Equal(0.25, t.Value, 4);
    }

    [Fact]
    public void Lerp_Pixel()
    {
        var t = new Tween<Pixel>(Pixel.Black);
        t.To(new Pixel(200, 100, 0), 1.Seconds());
        t.Update(500.Ms());
        Assert.Equal(new Pixel(100, 50, 0), t.Value);
        t.Update(500.Ms());
        Assert.Equal(new Pixel(200, 100, 0), t.Value);
    }

    [Fact]
    public void Lerp_Vector2()
    {
        var t = new Tween<Vector2>(Vector2.Zero);
        t.To(new Vector2(10, -20), 1.Seconds());
        t.Update(500.Ms());
        Assert.Equal(new Vector2(5, -10), t.Value);
    }

    [Fact]
    public void Lerp_Point()
    {
        var t = new Tween<Point>(new Point(0, 0));
        t.To(new Point(10, 20), 1.Seconds());
        t.Update(500.Ms());
        Assert.Equal(new Point(5, 10), t.Value);
        t.Update(500.Ms());
        Assert.Equal(new Point(10, 20), t.Value);
    }

    [Fact]
    public void UnsupportedType_ThrowsUnlessLerpSupplied()
    {
        Assert.Throws<NotSupportedException>(() => new Tween<string>("a"));

        var t = new Tween<string>("a", (a, b, p) => p < 0.5f ? a : b);
        t.To("b", 1.Seconds());
        t.Update(250.Ms());
        Assert.Equal("a", t.Value);
        t.Update(750.Ms());
        Assert.Equal("b", t.Value);
    }

    [Fact]
    public void Update_DoesNotAllocate()
    {
        var t = new Tween<Pixel>(Pixel.Black);
        t.To(Pixel.White, 1.Seconds(), Easing.OutCubic);
        t.Update(1.Ms());

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) t.Update(1.Ms());
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
