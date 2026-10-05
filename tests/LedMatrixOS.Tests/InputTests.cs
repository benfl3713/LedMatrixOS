using LedMatrixOS.Apps;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class InputTests(ITestOutputHelper output)
{
    private sealed class Recorder : IInputConsumer
    {
        public readonly List<InputEvent> Events = [];
        public void OnInput(in InputEvent e) => Events.Add(e);
    }

    private static InputEvent Ev(int player, InputButton b, InputState s) => new(player, b, s);

    // ---- validation --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, "up", "down", 0, InputButton.Up, InputState.Down, false)]
    [InlineData(2, "SELECT", "UP", 2, InputButton.Select, InputState.Up, false)]
    [InlineData(3, " a ", "press", 3, InputButton.A, InputState.Down, true)]
    public void Parse_AcceptsValidRequests(int? player, string button, string state, int p, InputButton b, InputState s, bool press)
    {
        Assert.True(InputParser.TryParse(player, button, state, out var e, out var release, out _));
        Assert.Equal(new InputEvent(p, b, s), e);
        Assert.Equal(press, release);
    }

    [Theory]
    [InlineData(4, "up", "down")]
    [InlineData(-1, "up", "down")]
    [InlineData(0, "jump", "down")]
    [InlineData(0, "1", "down")]
    [InlineData(0, "", "down")]
    [InlineData(0, null, "down")]
    [InlineData(0, "up", "tap")]
    [InlineData(0, "up", null)]
    public void Parse_RejectsBadRequests(int player, string? button, string? state)
    {
        Assert.False(InputParser.TryParse(player, button, state, out _, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Apply_PressQueuesDownThenUp_AndBadRequestQueuesNothing()
    {
        var hub = new InputHub();
        Assert.True(new InputRequest(1, "a", "press").TryApply(hub, null, out _));
        Assert.False(new InputRequest(9, "a", "press").TryApply(hub, null, out var error));
        Assert.Contains("player", error);

        var rec = new Recorder();
        hub.Dispatch(rec);
        Assert.Equal([Ev(1, InputButton.A, InputState.Down), Ev(1, InputButton.A, InputState.Up)], rec.Events);
    }

    // ---- hub ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Dispatch_DeliversInOrder_AndTracksHeldState()
    {
        var hub = new InputHub();
        var rec = new Recorder();
        hub.Enqueue(Ev(0, InputButton.Left, InputState.Down));
        hub.Enqueue(Ev(1, InputButton.B, InputState.Down));
        hub.Enqueue(Ev(0, InputButton.Left, InputState.Up));

        Assert.False(hub.IsDown(0, InputButton.Left)); // nothing applies until the frame starts
        Assert.Equal(3, hub.Dispatch(rec));

        Assert.Equal([Ev(0, InputButton.Left, InputState.Down), Ev(1, InputButton.B, InputState.Down), Ev(0, InputButton.Left, InputState.Up)], rec.Events);
        Assert.False(hub.IsDown(0, InputButton.Left));
        Assert.True(hub.IsDown(1, InputButton.B));
        Assert.True(hub.IsDownAny(InputButton.B));
        Assert.False(hub.IsDown(7, InputButton.B));
    }

    [Fact]
    public void Dispatch_DropsRedundantTransitions()
    {
        var hub = new InputHub();
        var rec = new Recorder();
        hub.Enqueue(Ev(0, InputButton.Up, InputState.Up));   // not held
        hub.Enqueue(Ev(0, InputButton.Up, InputState.Down));
        hub.Enqueue(Ev(0, InputButton.Up, InputState.Down)); // key repeat
        Assert.Equal(1, hub.Dispatch(rec));
        Assert.Single(rec.Events);
    }

    [Fact]
    public void Dispatch_WithoutConsumer_StillTracksHeldState()
    {
        var hub = new InputHub();
        hub.Enqueue(Ev(2, InputButton.Start, InputState.Down));
        hub.Dispatch(null);
        Assert.True(hub.IsDown(2, InputButton.Start));
    }

    [Fact]
    public void ReleaseAll_ClearsHeldAndQueue_AndTellsThePreviousApp()
    {
        var hub = new InputHub();
        var rec = new Recorder();
        hub.Enqueue(Ev(0, InputButton.A, InputState.Down));
        hub.Enqueue(Ev(3, InputButton.Down, InputState.Down));
        hub.Dispatch(null);
        hub.Enqueue(Ev(1, InputButton.B, InputState.Down)); // still queued: dropped by the release

        hub.ReleaseAll(rec);

        Assert.Equal([Ev(0, InputButton.A, InputState.Up), Ev(3, InputButton.Down, InputState.Up)], rec.Events);
        Assert.False(hub.IsDown(0, InputButton.A));
        Assert.Equal(0, hub.Dispatch(rec));
    }

    [Fact]
    public void ClientDispose_ReleasesOnlyWhatThatClientHeld()
    {
        var hub = new InputHub();
        var rec = new Recorder();
        var a = hub.CreateClient();
        var b = hub.CreateClient();
        a.Send(Ev(0, InputButton.Up, InputState.Down));
        a.Send(Ev(0, InputButton.A, InputState.Down));
        a.Send(Ev(0, InputButton.A, InputState.Up));
        b.Send(Ev(1, InputButton.Left, InputState.Down));
        hub.Dispatch(rec);

        a.Dispose();
        rec.Events.Clear();
        hub.Dispatch(rec);

        Assert.Equal([Ev(0, InputButton.Up, InputState.Up)], rec.Events);
        Assert.False(hub.IsDown(0, InputButton.Up));
        Assert.True(hub.IsDown(1, InputButton.Left));
        b.Dispose();
        hub.Dispatch(null);
        Assert.False(hub.IsDown(1, InputButton.Left));
    }

    [Fact]
    public void Queue_IsBounded_ButAlwaysHasRoomForReleases()
    {
        var hub = new InputHub();
        int accepted = 0;
        for (int i = 0; i < InputHub.Capacity * 2; i++)
            if (hub.Enqueue(Ev(0, (InputButton)(i % 8), InputState.Down))) accepted++;

        Assert.True(accepted < InputHub.Capacity);
        Assert.True(hub.Dropped > 0);
        Assert.True(hub.Enqueue(Ev(0, InputButton.Up, InputState.Up))); // an Up still fits
    }

    [Fact]
    public void Hub_IsThreadSafe()
    {
        var hub = new InputHub();
        var rec = new Recorder();
        var writers = Enumerable.Range(0, 4).Select(p => Task.Run(() =>
        {
            for (int i = 0; i < 2000; i++) hub.EnqueuePress(p, InputButton.A);
        })).ToArray();
        while (writers.Any(t => !t.IsCompleted)) hub.Dispatch(rec);
        Task.WaitAll(writers);
        hub.Dispatch(rec);

        Assert.Equal(0, rec.Events.Count % 2);
        Assert.All(Enumerable.Range(0, 4), p => Assert.False(hub.IsDown(p, InputButton.A)));
    }

    [Fact]
    public void Dispatch_DoesNotAllocate()
    {
        var hub = new InputHub();
        var rec = new NullConsumer();
        for (int i = 0; i < 50; i++) { hub.EnqueuePress(0, InputButton.A); hub.Dispatch(rec); }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { hub.EnqueuePress(0, InputButton.A); hub.Dispatch(rec); hub.IsDown(0, InputButton.A); }
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 256);
    }

    private sealed class NullConsumer : IInputConsumer { public void OnInput(in InputEvent e) { } }

    // ---- input-test app ----------------------------------------------------------------------------------------------

    private static (InputTestApp App, AppStage Stage, InputHub Hub) Screen()
    {
        Fonts.Load();
        var hub = new InputHub();
        var app = new InputTestApp { Time = new FakeTime(), Input = hub };
        var stage = new AppStage(app);
        stage.Step(33, 5);
        return (app, stage, hub);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void Golden_Idle()
    {
        var (_, stage, _) = Screen();
        Golden(stage, "input_test_idle");
    }

    [Fact]
    public void Golden_ButtonsHeld()
    {
        var (_, stage, hub) = Screen();
        hub.Enqueue(Ev(0, InputButton.Up, InputState.Down));
        hub.Enqueue(Ev(0, InputButton.A, InputState.Down));
        hub.Enqueue(Ev(1, InputButton.Left, InputState.Down));
        hub.Enqueue(Ev(3, InputButton.Start, InputState.Down));
        hub.Dispatch(null);
        stage.Step(33, 2);
        Golden(stage, "input_test_held");
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage, hub) = Screen();
        hub.Enqueue(Ev(0, InputButton.Right, InputState.Down));
        hub.Dispatch(null);
        var run = stage.MeasureSteadyAllocation(windows: 8);

        output.WriteLine($"input-test: {run.MsPerFrame:F3} ms/frame");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }
}
