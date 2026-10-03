using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class ClocksFlipClockAppTests
{
    private static ClocksHarness Make(DateTimeOffset start, Action<ClockApp>? configure = null)
    {
        var app = new ClockApp { Style = "Flip" };
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
        ClocksHarness.Verify(h.Snapshot(), "flipclock_entrance");
        h.Step(16, 160);
        ClocksHarness.Verify(h.Snapshot(), "flipclock_0930_00");

        // Flip frames of the seconds (and minutes) at 09:30:59 -> 09:31:00.
        var f = Make(ClocksHarness.At(9, 30, 58, 900));
        f.Step(16, 150);
        f.SetWall(ClocksHarness.At(9, 30, 59, 990));
        f.Step(16, 2);   // flip starts
        f.Step(16, 8);   // ~ 0.13 s: top flap falling
        ClocksHarness.Verify(f.Snapshot(), "flipclock_flap_falling");
        f.Step(16, 6);   // ~ 0.25 s: flap edge-on
        ClocksHarness.Verify(f.Snapshot(), "flipclock_flap_edge");
        f.Step(16, 3);   // lower flap swinging down
        ClocksHarness.Verify(f.Snapshot(), "flipclock_flap_unfolding");
        f.Step(16, 30);
        ClocksHarness.Verify(f.Snapshot(), "flipclock_settled");

        var mid = Make(ClocksHarness.At(23, 59, 50, 0));
        mid.Step(16, 190);
        mid.SetWall(ClocksHarness.At(23, 59, 59, 990));
        mid.Step(16, 2);
        mid.Step(16, 15);
        ClocksHarness.Verify(mid.Snapshot(), "flipclock_midnight");

        h = Make(ClocksHarness.At(15, 4, 41, 500), a => { a.Show24Hour = false; a.TextColor = "Amber"; });
        h.Step(16, 200);
        ClocksHarness.Verify(h.Snapshot(), "flipclock_12h_pm_amber");

        h = Make(ClocksHarness.At(8, 12, 0), a => { a.ShowSeconds = false; a.BackgroundColor = "DarkBlue"; a.TextColor = "Cyan"; });
        h.Step(16, 200);
        ClocksHarness.Verify(h.Snapshot(), "flipclock_noseconds_blue");
    }
}
