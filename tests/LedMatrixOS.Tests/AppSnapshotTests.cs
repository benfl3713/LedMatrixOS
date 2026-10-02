using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;

namespace LedMatrixOS.Tests;

public class AppSnapshotTests
{
    public AppSnapshotTests()
    {
        // Fonts are copied next to LedMatrixOS.Graphics.dll (Content CopyToOutputDirectory), which is in the test output.
        Fonts.Load();
    }

    [Fact]
    public void SolidColorApp_Snapshot()
    {
        // SolidColorApp is a WidgetApp now, so it needs FrameContext updates; the default "Solid" mode is still one flat colour.
        var frame = AmbientRig.Render(new SolidColorApp(), TimeSpan.FromMilliseconds(100));
        SnapshotHelper.AssertMatchesSnapshot(frame, "solid_color_default");
    }

    [Fact]
    public void Font_DrawText_Snapshot()
    {
        var frame = SnapshotHelper.Render(f =>
        {
            f.DrawText(Fonts.Big, 2, 2, new Pixel(255, 255, 255), "Hello 123");
            f.DrawText(Fonts.Small, 2, 24, new Pixel(255, 0, 0), "Small font ABC");
            f.DrawText(Fonts.QuiteSmall, 2, 40, new Pixel(0, 255, 0), "Quite small abc");
            f.DrawText(Fonts.ExtraSmall, 2, 52, new Pixel(0, 128, 255), "Extra small xyz");
        });
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, "font_drawtext");
    }

    [Fact]
    public void ClockApp_RendersNonBlank()
    {
        // TODO: ClockApp uses DateTime.Now, so it is not deterministic yet. Add a golden snapshot
        // once FrameContext/TimeProvider lands. Until then only assert it does not throw and draws something
        // (the two stars are always drawn, independent of time).
        var frame = SnapshotHelper.RenderApp(new ClockApp());
        Assert.False(SnapshotHelper.IsBlank(frame));
    }
}
