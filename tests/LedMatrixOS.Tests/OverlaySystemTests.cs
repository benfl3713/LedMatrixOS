using LedMatrixOS.Core;
using LedMatrixOS.Core.Overlays;
using SixLabors.ImageSharp;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class OverlaySystemTests
{
    [Fact]
    public void Manager_StartsEmpty()
    {
        var mgr = new OverlayManager();
        Assert.Equal(0, mgr.Count);
    }

    [Fact]
    public void Manager_AddsOverlays()
    {
        var mgr = new OverlayManager();
        var toast = new ToastOverlay(TimeSpan.FromSeconds(2), (_, _) => { });

        mgr.Add(toast);
        Assert.Equal(1, mgr.Count);
    }

    [Fact]
    public void Toast_AutoDismisses()
    {
        var toast = new ToastOverlay(TimeSpan.FromSeconds(1), (_, _) => { });

        // Initially not dismissed
        Assert.False(toast.ShouldDismiss);

        // After 1.5 seconds (1 second duration + 0.5s fade out), should be dismissed
        toast.Update(TimeSpan.FromMilliseconds(1500));
        Assert.True(toast.ShouldDismiss);
    }

    [Fact]
    public void Badge_ManualDismiss()
    {
        var badge = new BadgeOverlay("test", new Rectangle(0, 0, 8, 8), Pixel.White);

        // Should not auto-dismiss
        badge.Update(TimeSpan.FromSeconds(10));
        Assert.False(badge.ShouldDismiss);

        // Dismiss it
        badge.Dismiss();
        Assert.True(badge.IsExiting);

        // After transition time, should be dismissed
        badge.Update(TimeSpan.FromMilliseconds(300));
        Assert.True(badge.ShouldDismiss);
    }

    [Fact]
    public void Manager_RemovesDismissedOverlays()
    {
        var mgr = new OverlayManager();
        var toast = new ToastOverlay(TimeSpan.FromMilliseconds(100), (_, _) => { });

        mgr.Add(toast);
        Assert.Equal(1, mgr.Count);

        // Advance past dismissal
        mgr.Update(TimeSpan.FromMilliseconds(300));
        Assert.Equal(0, mgr.Count);
    }

    [Fact]
    public void Manager_DismissByIdWorks()
    {
        var mgr = new OverlayManager();
        var badge = new BadgeOverlay("notification", new Rectangle(0, 0, 8, 8), Pixel.White);
        mgr.Add(badge);

        Assert.True(mgr.Dismiss("notification"));

        // After update and time, should be gone
        mgr.Update(TimeSpan.FromMilliseconds(300));
        Assert.Equal(0, mgr.Count);
    }

    [Fact]
    public void Manager_DismissUpToPriority()
    {
        var mgr = new OverlayManager();

        var lowPri = new BadgeOverlay("low", new Rectangle(0, 0, 8, 8), Pixel.White, priority: 50);
        var midPri = new BadgeOverlay("mid", new Rectangle(8, 0, 8, 8), Pixel.White, priority: 100);
        var highPri = new BadgeOverlay("high", new Rectangle(16, 0, 8, 8), Pixel.White, priority: 200);

        mgr.Add(lowPri);
        mgr.Add(midPri);
        mgr.Add(highPri);

        // Dismiss up to priority 100 (low and mid)
        mgr.DismissUpTo(100);

        // After time, low and mid should be gone, high should remain
        mgr.Update(TimeSpan.FromMilliseconds(300));
        Assert.Equal(1, mgr.Count);
    }

    [Fact]
    public void Toast_OpacityTransition()
    {
        var toast = new ToastOverlay(TimeSpan.FromSeconds(10), (_, _) => { });

        // At t=0, opacity starts at 0 (fading in)
        Assert.True(toast.Opacity < 0.1f);

        // After 150ms (transition duration), should be fully opaque
        toast.Update(TimeSpan.FromMilliseconds(150));
        Assert.True(toast.Opacity > 0.9f);

        // Should stay opaque until duration expires
        toast.Update(TimeSpan.FromMilliseconds(8000));
        Assert.True(toast.Opacity > 0.9f);
        Assert.False(toast.IsExiting);

        // Elapsed 10.05s: past the 10s duration, 50ms into the 150ms fade-out
        toast.Update(TimeSpan.FromMilliseconds(1900));
        Assert.True(toast.IsExiting);
        Assert.InRange(toast.Opacity, 0.5f, 0.9f);

        toast.Update(TimeSpan.FromMilliseconds(200));
        Assert.True(toast.ShouldDismiss);
    }

    [Fact]
    public void Manager_CompositesBadgeOntoFrameAtItsBounds()
    {
        var mgr = new OverlayManager(32, 16);
        var frame = new FrameBuffer(32, 16);
        var red = new Pixel(255, 0, 0);
        mgr.Add(new BadgeOverlay("b", new Rectangle(20, 4, 4, 4), red));
        mgr.Update(TimeSpan.FromSeconds(1)); // past the fade-in

        mgr.RenderOverlays(frame, new FrameContext(TimeSpan.Zero, TimeSpan.Zero, 0));

        Assert.Equal(red, frame.GetPixel(21, 5));
        Assert.Equal(Pixel.Black, frame.GetPixel(0, 0));
        Assert.Equal(Pixel.Black, frame.GetPixel(25, 5));
    }
}
