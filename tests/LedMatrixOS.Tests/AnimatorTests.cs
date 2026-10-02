using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using Xunit;

namespace LedMatrixOS.Tests;

public class AnimatorTests
{
    private static FrameContext Frame(int ms, long index = 0) => new(TimeSpan.Zero, ms.Ms(), index);

    [Fact]
    public void Update_AdvancesTweensAndTimelines()
    {
        var animator = new Animator();
        var a = new Tween<float>(0f);
        var b = new Tween<float>(0f);
        a.To(10f, 1.Seconds());
        animator.Add(a);
        animator.Add(Timeline.To(b, 10f, 1.Seconds()));

        animator.Update(Frame(500));
        Assert.Equal(5f, a.Value, 3);
        Assert.Equal(5f, b.Value, 3);
    }

    [Fact]
    public void FinishedAnimations_AreRemoved()
    {
        var animator = new Animator();
        var a = new Tween<float>(0f);
        animator.Animate(a, 1f, 100.Ms());
        animator.Add(Timeline.Delay(1.Seconds()));
        Assert.Equal(2, animator.Count);

        animator.Update(Frame(100));
        Assert.Equal(1, animator.Count);
        animator.Update(Frame(900));
        Assert.Equal(0, animator.Count);
    }

    [Fact]
    public void Add_SameInstanceTwice_IsIgnored()
    {
        var animator = new Animator();
        var a = new Tween<float>(0f);
        a.To(1f, 1.Seconds());
        animator.Add(a);
        animator.Add(a);
        Assert.Equal(1, animator.Count);
    }

    [Fact]
    public void Animate_RetargetingRunningTween_DoesNotDuplicate()
    {
        var animator = new Animator();
        var a = new Tween<float>(0f);
        animator.Animate(a, 10f, 1.Seconds());
        animator.Update(Frame(500));
        animator.Animate(a, 0f, 1.Seconds(), Easing.OutQuad);
        Assert.Equal(1, animator.Count);
        Assert.Equal(5f, a.Value, 3);
    }

    [Fact]
    public void Animate_ZeroDuration_SetsImmediatelyWithoutTracking()
    {
        var animator = new Animator();
        var a = new Tween<float>(0f);
        animator.Animate(a, 4f, TimeSpan.Zero);
        Assert.Equal(4f, a.Value);
        Assert.Equal(0, animator.Count);
    }

    [Fact]
    public void Clear_CancelsEverything()
    {
        var animator = new Animator();
        var a = new Tween<float>(0f);
        animator.Animate(a, 10f, 1.Seconds());
        animator.Clear();
        Assert.Equal(0, animator.Count);
        Assert.False(a.IsRunning);
        animator.Update(Frame(500));
        Assert.Equal(0f, a.Value);
    }

    [Fact]
    public void CallbackMayAddMoreAnimations()
    {
        var animator = new Animator();
        var b = new Tween<float>(0f);
        var a = new Tween<float>(0f);
        animator.Animate(a, 1f, 100.Ms(), onComplete: () => animator.Animate(b, 1f, 100.Ms()));

        animator.Update(Frame(100));
        Assert.Equal(1, animator.Count);
        animator.Update(Frame(100));
        Assert.Equal(1f, b.Value);
        Assert.Equal(0, animator.Count);
    }

    [Fact]
    public void CallbackMayClear()
    {
        var animator = new Animator();
        var a = new Tween<float>(0f);
        var b = new Tween<float>(0f);
        animator.Animate(a, 1f, 100.Ms(), onComplete: animator.Clear);
        animator.Animate(b, 1f, 1.Seconds());

        animator.Update(Frame(100));
        Assert.Equal(0, animator.Count);
    }

    [Fact]
    public void Update_DoesNotAllocate()
    {
        var animator = new Animator();
        var v = new Tween<float>(0f);
        animator.Add(Timeline.Sequence(Timeline.To(v, 10f, 1.Seconds()), Timeline.To(v, 0f, 1.Seconds())).Loop());
        var ctx = Frame(16);
        for (var i = 0; i < 100; i++) animator.Update(ctx);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) animator.Update(ctx);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
