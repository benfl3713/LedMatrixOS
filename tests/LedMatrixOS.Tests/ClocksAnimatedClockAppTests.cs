using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class ClocksAnimatedClockAppTests
{
    private static ClocksHarness Make(DateTimeOffset start, Action<ClockApp>? configure = null)
    {
        var app = new ClockApp { Style = "Animated" };
        configure?.Invoke(app);
        var h = new ClocksHarness(app, start);
        h.Step(16);
        return h;
    }

    [Fact]
    public void Snapshots()
    {
        var h = Make(ClocksHarness.At(9, 30, 0));
        h.Step(16, 25);
        ClocksHarness.Verify(h.Snapshot(), "animclock_entrance");
        h.Step(16, 200);
        ClocksHarness.Verify(h.Snapshot(), "animclock_0930_settled");

        // Second tick: ripple front a little way out (09:30:03.0 -> 03.3).
        var tick = Make(ClocksHarness.At(9, 30, 2, 800));
        tick.Step(16, 190);
        tick.SetWall(ClocksHarness.At(9, 30, 5, 0));
        tick.Step(16, 16);
        ClocksHarness.Verify(tick.Snapshot(), "animclock_second_ripple");

        // Minute change: the units digit drops in and sparks fly.
        var minute = Make(ClocksHarness.At(9, 30, 40, 0));
        minute.Step(16, 200);
        minute.SetWall(ClocksHarness.At(9, 30, 59, 960));
        minute.Step(16, 6); // 09:31:00.0x, the digit starts dropping
        minute.Step(16, 14);
        ClocksHarness.Verify(minute.Snapshot(), "animclock_minute_drop");

        var mid = Make(ClocksHarness.At(23, 59, 50, 0), a => a.Palette = "Lava");
        mid.Step(16, 200);
        mid.SetWall(ClocksHarness.At(23, 59, 59, 990));
        mid.Step(16, 10);
        mid.Step(16, 14);
        ClocksHarness.Verify(mid.Snapshot(), "animclock_midnight_lava");

        h = Make(ClocksHarness.At(17, 8, 15, 400), a => { a.Palette = "Rainbow"; a.Show24Hour = false; });
        h.Step(16, 220);
        ClocksHarness.Verify(h.Snapshot(), "animclock_rainbow_12h");

        h = Make(ClocksHarness.At(6, 45, 31, 0), a => { a.Palette = "Cyber"; a.ShowSeconds = false; });
        h.Step(16, 220);
        ClocksHarness.Verify(h.Snapshot(), "animclock_cyber_noseconds");
    }
}
