using LedMatrixOS.Core.Animation;
using Xunit;

namespace LedMatrixOS.Tests;

public class TimelineTests
{
    [Fact]
    public void Delay_FinishesAtDuration_AndReturnsLeftover()
    {
        var d = Timeline.Delay(1.Seconds());
        Assert.False(d.IsFinished);
        Assert.Equal(TimeSpan.Zero, d.Update(999.Ms()));
        Assert.False(d.IsFinished);
        Assert.Equal(1.Ms(), d.Update(2.Ms()));
        Assert.True(d.IsFinished);
        Assert.Equal(5.Ms(), d.Update(5.Ms()));
    }

    [Fact]
    public void Do_RunsOnceOnFirstUpdate_EvenWithZeroDelta()
    {
        var count = 0;
        var t = Timeline.Do(() => count++);
        Assert.Equal(0, count);
        t.Update(TimeSpan.Zero);
        Assert.Equal(1, count);
        Assert.True(t.IsFinished);
        t.Update(1.Seconds());
        Assert.Equal(1, count);
    }

    [Fact]
    public void ZeroLengthDelay_FinishesImmediately()
    {
        var t = Timeline.Delay(TimeSpan.Zero);
        Assert.Equal(10.Ms(), t.Update(10.Ms()));
        Assert.True(t.IsFinished);
    }

    [Fact]
    public void EmptySequenceAndParallel_FinishImmediately()
    {
        var s = Timeline.Sequence();
        var p = Timeline.Parallel();
        s.Update(1.Ms());
        p.Update(1.Ms());
        Assert.True(s.IsFinished);
        Assert.True(p.IsFinished);
    }

    [Fact]
    public void Sequence_RunsInOrder()
    {
        var a = new Tween<float>(0f);
        var log = new List<string>();
        var t = Timeline.Sequence(
            Timeline.Do(() => log.Add("start")),
            Timeline.To(a, 10f, 1.Seconds()),
            Timeline.Delay(1.Seconds()),
            Timeline.Do(() => log.Add($"mid{a.Value}")),
            Timeline.To(a, 0f, 1.Seconds()),
            Timeline.Do(() => log.Add("end")));

        t.Update(500.Ms());
        Assert.Equal(5f, a.Value, 3);
        Assert.Equal(new[] { "start" }, log);

        t.Update(1.Seconds());
        Assert.Equal(new[] { "start" }, log);
        Assert.Equal(10f, a.Value);

        t.Update(500.Ms());
        Assert.Equal(new[] { "start", "mid10" }, log);
        Assert.False(t.IsFinished);

        t.Update(500.Ms());
        Assert.Equal(5f, a.Value, 3);
        Assert.False(t.IsFinished);
        t.Update(500.Ms());
        Assert.True(t.IsFinished);
        Assert.Equal(new[] { "start", "mid10", "end" }, log);
        Assert.Equal(0f, a.Value);
    }

    [Fact]
    public void Sequence_CarriesLeftoverAcrossSteps()
    {
        var a = new Tween<float>(0f);
        var t = Timeline.Sequence(
            Timeline.To(a, 10f, 1.Seconds()),
            Timeline.To(a, 20f, 1.Seconds()),
            Timeline.To(a, 40f, 2.Seconds()));

        t.Update(2500.Ms());
        Assert.Equal(25f, a.Value, 3);
        t.Update(1500.Ms());
        Assert.True(t.IsFinished);
        Assert.Equal(40f, a.Value);
    }

    [Fact]
    public void Sequence_BigDeltaRunsEverythingAndReturnsLeftover()
    {
        var count = 0;
        var t = Timeline.Sequence(
            Timeline.Do(() => count++),
            Timeline.Delay(1.Seconds()),
            Timeline.Do(() => count++),
            Timeline.Delay(1.Seconds()),
            Timeline.Do(() => count++));

        Assert.Equal(500.Ms(), t.Update(2500.Ms()));
        Assert.Equal(3, count);
        Assert.True(t.IsFinished);
    }

    [Fact]
    public void Parallel_FinishesWithLongest_AndDrivesAllTweens()
    {
        var a = new Tween<float>(0f);
        var b = new Tween<float>(0f);
        var t = Timeline.Parallel(
            Timeline.To(a, 10f, 1.Seconds()),
            Timeline.To(b, 10f, 2.Seconds()));

        t.Update(1.Seconds());
        Assert.Equal(10f, a.Value);
        Assert.Equal(5f, b.Value, 3);
        Assert.False(t.IsFinished);

        Assert.Equal(500.Ms(), t.Update(1500.Ms()));
        Assert.True(t.IsFinished);
        Assert.Equal(10f, b.Value);
    }

    [Fact]
    public void Parallel_LeftoverFollowsLastFinisher()
    {
        var t = Timeline.Sequence(
            Timeline.Parallel(Timeline.Delay(1.Seconds()), Timeline.Delay(2.Seconds())),
            Timeline.Delay(1.Seconds()));

        t.Update(2500.Ms());
        Assert.False(t.IsFinished);
        t.Update(500.Ms());
        Assert.True(t.IsFinished);
    }

    [Fact]
    public void Nested_SequenceOfParallelsAndSequences()
    {
        var x = new Tween<float>(0f);
        var y = new Tween<float>(0f);
        var log = new List<string>();
        var t = Timeline.Sequence(
            Timeline.Parallel(
                Timeline.To(x, 10f, 1.Seconds()),
                Timeline.Sequence(Timeline.Delay(500.Ms()), Timeline.To(y, 10f, 500.Ms()))),
            Timeline.Do(() => log.Add($"{x.Value},{y.Value}")),
            Timeline.Sequence(Timeline.Delay(1.Seconds()), Timeline.Do(() => log.Add("done"))));

        t.Update(1.Seconds());
        Assert.Equal(new[] { "10,10" }, log);
        t.Update(999.Ms());
        Assert.Single(log);
        t.Update(1.Ms());
        Assert.Equal(new[] { "10,10", "done" }, log);
        Assert.True(t.IsFinished);
    }

    [Fact]
    public void Repeat_PlaysExactCount()
    {
        var count = 0;
        var t = Timeline.Sequence(Timeline.Do(() => count++), Timeline.Delay(1.Seconds())).Repeat(3);

        t.Update(2500.Ms());
        Assert.Equal(3, count);
        Assert.False(t.IsFinished);
        t.Update(500.Ms());
        Assert.True(t.IsFinished);
        t.Update(10.Seconds());
        Assert.Equal(3, count);
    }

    [Fact]
    public void Repeat_RejectsNonPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Timeline.Delay(1.Ms()).Repeat(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Timeline.Delay(1.Ms()).Yoyo(0));
    }

    [Fact]
    public void Loop_NeverFinishes()
    {
        var count = 0;
        var t = Timeline.Sequence(Timeline.Do(() => count++), Timeline.Delay(1.Seconds())).Loop();

        for (var i = 0; i < 50; i++) t.Update(100.Ms());
        Assert.Equal(6, count); // fires at 0s, 1s, ... 5s
        Assert.False(t.IsFinished);
    }

    [Fact]
    public void Loop_OfZeroLengthStep_DoesNotSpin()
    {
        var count = 0;
        var t = Timeline.Do(() => count++).Loop();
        t.Update(1.Seconds());
        Assert.Equal(1, count);
        Assert.True(t.IsFinished);
    }

    [Fact]
    public void Loop_HugeDeltaRunsManyIterations()
    {
        var count = 0;
        var t = Timeline.Sequence(Timeline.Do(() => count++), Timeline.Delay(100.Ms())).Loop();
        t.Update(1.Seconds());
        Assert.Equal(11, count); // fires at 0ms, 100ms, ... 1000ms
    }

    [Fact]
    public void Yoyo_TwoPlays_GoesThereAndBack()
    {
        var v = new Tween<float>(0f);
        var t = Timeline.To(v, 10f, 1.Seconds()).Yoyo();

        t.Update(1.Seconds());
        Assert.Equal(10f, v.Value);
        t.Update(500.Ms());
        Assert.Equal(5f, v.Value, 3);
        Assert.False(t.IsFinished);
        t.Update(500.Ms());
        Assert.Equal(0f, v.Value);
        Assert.True(t.IsFinished);
    }

    [Fact]
    public void Yoyo_OddPlaysEndsAtTarget_AndCountsCallbacks()
    {
        var v = new Tween<float>(0f);
        var count = 0;
        var t = Timeline.Sequence(Timeline.To(v, 10f, 1.Seconds()), Timeline.Do(() => count++)).Yoyo(3);

        t.Update(2500.Ms());
        Assert.Equal(2, count);
        Assert.Equal(5f, v.Value, 3);
        t.Update(500.Ms());
        Assert.Equal(3, count);
        Assert.Equal(10f, v.Value);
        Assert.True(t.IsFinished);
    }

    [Fact]
    public void Yoyo_ReversesStepOrder()
    {
        var log = new List<string>();
        var t = Timeline.Sequence(
            Timeline.Do(() => log.Add("a")),
            Timeline.Delay(100.Ms()),
            Timeline.Do(() => log.Add("b"))).Yoyo();

        t.Update(1.Seconds());
        Assert.Equal(new[] { "a", "b", "b", "a" }, log);
    }

    [Fact]
    public void Yoyo_ParallelReverseAlignsFinishAtStart()
    {
        var a = new Tween<float>(0f);
        var b = new Tween<float>(0f);
        var t = Timeline.Parallel(
            Timeline.To(a, 10f, 1.Seconds()),
            Timeline.To(b, 10f, 2.Seconds())).Yoyo();

        t.Update(2.Seconds());
        Assert.Equal(10f, a.Value);
        Assert.Equal(10f, b.Value);

        t.Update(1.Seconds());
        Assert.Equal(10f, a.Value);
        Assert.Equal(5f, b.Value, 3);

        t.Update(1.Seconds());
        Assert.Equal(0f, a.Value);
        Assert.Equal(0f, b.Value);
        Assert.True(t.IsFinished);
    }

    [Fact]
    public void YoyoLoop_KeepsAlternating()
    {
        var v = new Tween<float>(0f);
        var t = Timeline.To(v, 10f, 1.Seconds()).YoyoLoop();

        t.Update(1500.Ms());
        Assert.Equal(5f, v.Value, 3);
        t.Update(1.Seconds());
        Assert.Equal(5f, v.Value, 3);
        t.Update(500.Ms());
        Assert.Equal(10f, v.Value, 3);
        Assert.False(t.IsFinished);
    }

    [Fact]
    public void Cancel_StopsPlaybackAndTween()
    {
        var v = new Tween<float>(0f);
        var count = 0;
        var t = Timeline.Sequence(Timeline.To(v, 10f, 1.Seconds()), Timeline.Do(() => count++));

        t.Update(500.Ms());
        t.Cancel();
        Assert.True(t.IsFinished);
        Assert.False(v.IsRunning);
        t.Update(5.Seconds());
        Assert.Equal(5f, v.Value, 3);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Cancel_Nested_StopsActiveTween()
    {
        var v = new Tween<float>(0f);
        var t = Timeline.Parallel(Timeline.To(v, 10f, 1.Seconds()), Timeline.Delay(2.Seconds())).Loop();
        t.Update(100.Ms());
        t.Cancel();
        Assert.False(v.IsRunning);
    }

    [Fact]
    public void Deterministic_SameResultForAnyStepSize()
    {
        float Run(int stepMs)
        {
            var v = new Tween<float>(0f);
            var t = Timeline.Sequence(
                Timeline.To(v, 100f, 700.Ms(), Easing.OutBack),
                Timeline.Delay(300.Ms()),
                Timeline.To(v, 0f, 1.Seconds(), Easing.InOutSine)).Yoyo(3);

            for (var ms = 0; ms < 6000; ms += stepMs) t.Update(stepMs.Ms());
            return v.Value;
        }

        Assert.Equal(Run(1), Run(10), 2);
        Assert.Equal(Run(1), Run(50), 2);
    }

    [Fact]
    public void Update_DoesNotAllocate()
    {
        var v = new Tween<float>(0f);
        var t = Timeline.Sequence(
            Timeline.Parallel(Timeline.To(v, 10f, 1.Seconds(), Easing.OutCubic), Timeline.Delay(500.Ms())),
            Timeline.To(v, 0f, 1.Seconds())).YoyoLoop();
        for (var i = 0; i < 100; i++) t.Update(16.Ms());

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) t.Update(16.Ms());
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
