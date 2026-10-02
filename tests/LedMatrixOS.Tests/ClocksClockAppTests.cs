using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class ClocksClockAppTests
{
    private static ClocksHarness Make(DateTimeOffset start, Action<ClockApp>? configure = null)
    {
        var app = new ClockApp();
        configure?.Invoke(app);
        var h = new ClocksHarness(app, start);
        h.Step(16);
        return h;
    }

    [Fact]
    public void Snapshots()
    {
        // Entrance: digits rising in.
        var h = Make(ClocksHarness.At(9, 30, 0));
        h.Step(16, 20);
        ClocksHarness.Verify(h.Snapshot(), "clock_entrance");
        h.Step(16, 160);
        ClocksHarness.Verify(h.Snapshot(), "clock_0930_00");

        // Mid digit roll: 09:30:07 -> :08.
        var rolling = Make(ClocksHarness.At(9, 30, 7, 500));
        rolling.Step(16, 150); // entrance done, wall clock just before :08
        rolling.Step(16, 12);
        ClocksHarness.Verify(rolling.Snapshot(), "clock_roll_seconds");

        // Midnight: every digit changes at once.
        var mid = Make(ClocksHarness.At(23, 59, 50, 0));
        mid.Step(16, 190); // entrance done
        mid.SetWall(ClocksHarness.At(23, 59, 59, 990));
        mid.Step(16, 10); // crosses midnight, every digit starts rolling
        mid.Step(16, 6);
        ClocksHarness.Verify(mid.Snapshot(), "clock_midnight_roll");
        mid.Step(16, 60);
        ClocksHarness.Verify(mid.Snapshot(), "clock_midnight_settled");

        // 12-hour, PM, different palette.
        h = Make(ClocksHarness.At(15, 4, 41, 500), a => { a.Show24Hour = false; a.Palette = "Ocean"; });
        h.Step(16, 200);
        ClocksHarness.Verify(h.Snapshot(), "clock_12h_ocean");

        // No seconds.
        h = Make(ClocksHarness.At(21, 47, 3), a => { a.ShowSeconds = false; a.Palette = "Neon"; });
        h.Step(16, 200);
        ClocksHarness.Verify(h.Snapshot(), "clock_noseconds_neon");
    }
}
