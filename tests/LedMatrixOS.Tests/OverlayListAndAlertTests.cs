using LedMatrixOS.Core;
using LedMatrixOS.Core.Overlays;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class OverlayListAndAlertTests
{
    public OverlayListAndAlertTests() => Fonts.Load();

    [Fact]
    public void List_ReportsKindTextPriorityAndRemaining_HighestPriorityFirst()
    {
        var mgr = new OverlayManager(256, 64);
        mgr.Add(new ToastOverlay(TimeSpan.FromSeconds(10), (_, _) => { }) { Text = "hello" });
        mgr.Add(new BadgeOverlay("dot", new Rectangle(0, 0, 4, 4), Pixel.White));
        mgr.Add(AlertFactory.Message("fire", Pixel.White, 256, 64));
        mgr.Update(TimeSpan.FromSeconds(2));

        var list = mgr.List();

        Assert.Equal(new[] { "alert", "toast", "badge" }, list.Select(o => o.Kind).ToArray());
        var toast = list.Single(o => o.Kind == "toast");
        Assert.Equal("hello", toast.Text);
        Assert.Equal(100, toast.Priority);
        Assert.Equal(8, toast.RemainingSeconds!.Value, 3);
        Assert.Null(list.Single(o => o.Kind == "badge").RemainingSeconds);
        Assert.Equal("fire", list[0].Text);
    }

    [Fact]
    public void Alerts_GetUniqueIds_SoEachCanBeDismissed()
    {
        var mgr = new OverlayManager(256, 64);
        var a = AlertFactory.Flash(256, 64, TimeSpan.FromSeconds(5));
        var b = AlertFactory.Flash(256, 64, TimeSpan.FromSeconds(5));
        Assert.NotEqual(a.Id, b.Id);
        mgr.Add(a);
        mgr.Add(b);

        Assert.True(mgr.Dismiss(a.Id));
        mgr.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { b.Id }, mgr.List().Select(o => o.Id).ToArray());
    }

    [Fact]
    public void Flash_FillsScreen_AndExpiresAfterDuration()
    {
        var mgr = new OverlayManager(32, 16);
        var frame = new FrameBuffer(32, 16);
        mgr.Add(AlertFactory.Flash(32, 16, TimeSpan.FromSeconds(1)));
        mgr.Update(TimeSpan.FromMilliseconds(500));
        mgr.RenderOverlays(frame, new FrameContext(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500), 1));
        Assert.Equal(new Pixel(200, 0, 0), frame.GetPixel(16, 8));

        mgr.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(0, mgr.Count);
    }

    [Fact]
    public void Message_LongTextScrollsLongerThanFiveSeconds_ShortTextDoesNot()
    {
        var shortAlert = AlertFactory.Message("Hi", Pixel.White, 256, 64);
        var longAlert = AlertFactory.Message("A rather long message that cannot possibly fit", Pixel.White, 256, 64);
        Assert.Equal(TimeSpan.FromSeconds(5), shortAlert.Duration);
        Assert.True(longAlert.Duration > TimeSpan.FromSeconds(5));
    }
}
