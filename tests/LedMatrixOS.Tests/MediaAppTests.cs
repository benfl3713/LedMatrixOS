using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Media;
using LedMatrixOS.Core.Settings;
using Xunit;
using Xunit.Abstractions;

namespace LedMatrixOS.Tests;

public class MediaAppTests(ITestOutputHelper output)
{
    /// <summary>Starts the app on the library and plays until the first item is on screen (frame 0 at time 0 of the item).</summary>
    private static AppStage Play(MediaApp app)
    {
        var stage = new AppStage(app);
        Settle(app, stage);
        return stage;
    }

    /// <summary>Runs the background prepare to completion and lets the app pick it up.</summary>
    private static void Settle(MediaApp app, AppStage stage)
    {
        stage.Step(33);
        for (int i = 0; i < 20 && app.IsPreparing; i++)
        {
            app.PendingLoad!.Wait();
            stage.Step(33);
        }
        app.PendingLoad?.Wait();   // a prefetch of the next item, so reaching the end of this one is deterministic
    }

    private static MediaApp App(MediaFixture fx, string mode = "Fit", Action<MediaApp>? configure = null)
    {
        var app = new MediaApp(fx.Library) { Mode = mode };
        configure?.Invoke(app);
        return app;
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        TubeFixtures.Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- identity and settings -------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        using var fx = new MediaFixture();
        var app = new MediaApp(fx.Library);
        Assert.Equal("media", app.Id);
        Assert.Equal(new[] { "items", "mode", "seconds", "loops", "speed", "background", "shuffle", "brightness" }, app.GetSettings().Select(s => s.Key).ToArray());

        var items = app.GetSettings().Single(s => s.Key == "items");
        Assert.Equal(AppSettingType.MultiSearch, items.Type);
        Assert.True(items.Browse);
        Assert.Equal(MediaApp.Modes, app.GetSettings().Single(s => s.Key == "mode").Options);
        var speed = app.GetSettings().Single(s => s.Key == "speed");
        Assert.Equal((100, 25, 400), (speed.CurrentValue, speed.MinValue, speed.MaxValue));
        Assert.Contains(typeof(MediaApp), BuiltInApps.GetAll());
    }

    [Fact]
    public async Task ItemsPicker_ListsTheLibrary()
    {
        using var fx = new MediaFixture();
        var card = fx.Add(MediaImages.TestCard(), "Card");
        var gif = fx.Add(MediaImages.Gif(), "Spinner");
        var registry = new SettingOptionsRegistry();
        registry.Register("media", "items", new MediaItemOptions(fx.Library));

        Assert.True(registry.IsBrowse("media", "items"));
        var all = await registry.SearchAsync("media", "items", "", default);
        Assert.Equal([card.Id, gif.Id], all.Select(o => o.Value));
        Assert.Equal(["Spinner"], (await registry.SearchAsync("media", "items", "spin", default)).Select(o => o.Label));
        Assert.Equal("Card", await registry.LabelAsync("media", "items", card.Id, default));
        Assert.Contains("GIF", all[1].Subtitle);
    }

    [Fact]
    public void Items_AcceptsAnIdListAndKeepsIt()
    {
        using var fx = new MediaFixture();
        var app = new MediaApp(fx.Library);
        app.UpdateSetting("items", "a1, b2,a1");
        Assert.Equal("a1,b2", app.Items);
    }

    // ---- golden snapshots ------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_Empty_ShowsTheUploadCard()
    {
        using var fx = new MediaFixture();
        var stage = Play(App(fx));
        Assert.False(SnapshotHelper.IsBlank(stage.Snapshot()));
        Golden(stage, "media_empty");
    }

    [Theory]
    [InlineData("Fit")]
    [InlineData("Fill")]
    [InlineData("Stretch")]
    public void Golden_ScalingModes(string mode)
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.TestCard(160, 80));
        var stage = Play(App(fx, mode, a => a.Background = "#101040"));
        Golden(stage, "media_" + mode.ToLowerInvariant());
    }

    [Fact]
    public void Golden_Center_PixelPerfect()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.TestCard(96, 40));
        var stage = Play(App(fx, "Center", a => a.Background = "#101040"));
        Golden(stage, "media_center");
    }

    [Fact]
    public void Golden_Scroll_HoldsThenPans()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.Panorama(512, 64));
        var app = App(fx, "Scroll");
        var stage = Play(app);
        Golden(stage, "media_scroll_start");

        stage.Step(6000);   // 1 s hold, then 5 s at 24 px/s
        Golden(stage, "media_scroll_mid");

        stage.Step(8000);   // the pan has ended: the right edge is showing
        var end = stage.Snapshot();
        Assert.Equal(Pixel.White, end.GetPixel(0, 0));   // the tick at x = 256 of the picture
    }

    [Fact]
    public void Golden_Gif_TwoTimestamps()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.Gif(frames: 3, delayCs: 50, width: 128, height: 64));
        var stage = Play(App(fx, "Fit"));
        var first = stage.Snapshot();
        Golden(stage, "media_gif_t0");

        stage.Step(600);   // 600 ms in: the second frame
        var second = stage.Snapshot();
        Golden(stage, "media_gif_t600");

        Assert.NotEqual(first.GetPixel(100, 5), second.GetPixel(100, 5));
    }

    // ---- playback behaviour ----------------------------------------------------------------------------------------

    private static int GifFrame(FrameBuffer f) => f.GetPixel(128, 2) switch
    {
        { R: > 150, G: < 80 } => 0,
        { G: > 130, R: < 80 } => 1,
        { B: > 150, R: < 80 } => 2,
        _ => -1,
    };

    [Fact]
    public void Gif_AdvancesWithTheFrameClock()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.Gif(frames: 3, delayCs: 50, width: 256, height: 64));
        var stage = Play(App(fx, "Stretch"));

        Assert.Equal(0, GifFrame(stage.Snapshot()));
        stage.Step(520);
        Assert.Equal(1, GifFrame(stage.Snapshot()));
        stage.Step(500);
        Assert.Equal(2, GifFrame(stage.Snapshot()));
        stage.Step(500);   // wrapped around
        Assert.Equal(0, GifFrame(stage.Snapshot()));
    }

    [Fact]
    public void Speed_ScalesFrameStepping()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.Gif(frames: 3, delayCs: 50, width: 256, height: 64));
        var fast = Play(App(fx, "Stretch", a => a.Speed = 200));
        fast.Step(260);   // 520 ms of animation
        Assert.Equal(1, GifFrame(fast.Snapshot()));

        var slow = Play(App(fx, "Stretch", a => a.Speed = 25));
        slow.Step(1900);  // 475 ms of animation: still the first frame
        Assert.Equal(0, GifFrame(slow.Snapshot()));
    }

    [Fact]
    public void Brightness_DimsThePlayback()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.Png(256, 64, (_, _) => new(200, 100, 50)));
        var stage = Play(App(fx, "Stretch", a => a.Brightness = 50));
        Assert.Equal(new Pixel(100, 50, 25), stage.Snapshot().GetPixel(10, 10));
    }

    [Fact]
    public void ChangingTheModeReprepares_WithoutAGapInTheImage()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.TestCard(160, 80));
        var app = App(fx, "Fit");
        var stage = Play(app);
        Assert.Equal(Pixel.Black, stage.Snapshot().GetPixel(5, 32));   // bar

        app.UpdateSetting("mode", "Fill");
        stage.Step(33);
        Assert.False(SnapshotHelper.IsBlank(stage.Snapshot()));        // the old picture keeps showing while the new one is prepared
        Settle(app, stage);
        Assert.NotEqual(Pixel.Black, stage.Snapshot().GetPixel(5, 10)); // no bar any more
    }

    [Fact]
    public void Playlist_MovesOnAfterTheSecondsPerItem()
    {
        using var fx = new MediaFixture();
        var a = fx.Add(MediaImages.Png(256, 64, (_, _) => new(255, 0, 0)), "red");
        var b = fx.Add(MediaImages.Png(256, 64, (_, _) => new(0, 0, 255)), "blue");
        var app = App(fx, "Stretch", x => x.Seconds = 2);
        var stage = Play(app);

        Assert.Equal(a.Id, app.CurrentId);
        stage.Step(1000);
        Assert.Equal(a.Id, app.CurrentId);
        stage.Step(1200);
        Settle(app, stage);
        Assert.Equal(b.Id, app.CurrentId);
        Assert.Equal(new Pixel(0, 0, 255), stage.Snapshot().GetPixel(100, 30));

        stage.Step(2200);
        Settle(app, stage);
        Assert.Equal(a.Id, app.CurrentId);   // and around again
    }

    [Fact]
    public void Playlist_LoopCountDecidesHowLongAGifPlays()
    {
        using var fx = new MediaFixture();
        var gif = fx.Add(MediaImages.Gif(frames: 2, delayCs: 50, width: 256, height: 64));   // 1 s per loop
        var still = fx.Add(MediaImages.Png(256, 64, (_, _) => new(0, 255, 0)));
        var app = App(fx, "Stretch", x => { x.Loops = 2; x.Seconds = 60; });
        var stage = Play(app);

        Assert.Equal(gif.Id, app.CurrentId);
        stage.Step(1900);
        Assert.Equal(gif.Id, app.CurrentId);   // second loop still running
        stage.Step(300);
        Settle(app, stage);
        Assert.Equal(still.Id, app.CurrentId);
    }

    [Fact]
    public void Items_PicksAndOrdersTheLibrarySubset()
    {
        using var fx = new MediaFixture();
        var a = fx.Add(MediaImages.TestCard(), "a");
        var b = fx.Add(MediaImages.TestCard(), "b");
        var c = fx.Add(MediaImages.TestCard(), "c");
        var app = App(fx, "Fit", x => { x.Items = $"{c.Id},{a.Id}"; x.Seconds = 1; });
        var stage = Play(app);

        Assert.Equal(c.Id, app.CurrentId);
        stage.Step(1100);
        Settle(app, stage);
        Assert.Equal(a.Id, app.CurrentId);
        stage.Step(1100);
        Settle(app, stage);
        Assert.Equal(c.Id, app.CurrentId);   // b was never picked
        Assert.NotEqual(b.Id, app.CurrentId);
    }

    [Fact]
    public void Shuffle_NeverRepeatsTheSameItemBackToBack()
    {
        using var fx = new MediaFixture();
        for (int i = 0; i < 3; i++) fx.Add(MediaImages.Png(256, 64, (_, _) => new(10, 10, 10)), "x" + i);
        var app = App(fx, "Stretch", x => { x.Shuffle = true; x.Seconds = 1; });
        var stage = Play(app);

        string? previous = app.CurrentId;
        for (int i = 0; i < 8; i++)
        {
            stage.Step(1100);
            Settle(app, stage);
            Assert.NotEqual(previous, app.CurrentId);
            previous = app.CurrentId;
        }
    }

    [Fact]
    public void Library_ChangesArePickedUpWhilePlaying()
    {
        using var fx = new MediaFixture();
        var app = App(fx);
        var stage = Play(app);
        Assert.Null(app.CurrentId);   // empty card

        var added = fx.Add(MediaImages.TestCard());
        Settle(app, stage);
        Assert.Equal(added.Id, app.CurrentId);

        fx.Library.Delete(added.Id);
        Settle(app, stage);
        Assert.Null(app.CurrentId);
        Assert.False(SnapshotHelper.IsBlank(stage.Snapshot()));   // back to the empty card
    }

    [Fact]
    public void DeletedItem_IsSkipped()
    {
        using var fx = new MediaFixture();
        var a = fx.Add(MediaImages.TestCard(), "a");
        var b = fx.Add(MediaImages.TestCard(), "b");
        var app = App(fx, "Fit", x => x.Seconds = 1);
        var stage = Play(app);
        Assert.Equal(a.Id, app.CurrentId);

        fx.Library.Delete(a.Id);
        Settle(app, stage);
        Assert.Equal(b.Id, app.CurrentId);
    }

    [Fact]
    public void Video_PlaysFromTheCache()
    {
        using var fx = new MediaFixture(new FakeTranscoder());
        fx.Add(MediaImages.Mp4Header());
        var stage = Play(App(fx, "Fit"));

        Assert.Equal(new Pixel(20, 40, 90), stage.Snapshot().GetPixel(100, 30));
        stage.Step(520);   // the clip is 10 frames at 10 fps
        Assert.Equal(new Pixel(120, 40, 90), stage.Snapshot().GetPixel(100, 30));
    }

    [Fact]
    public void OnADifferentDisplaySize_ThePictureIsPreparedForIt()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.TestCard(160, 80));
        var app = App(fx, "Fit");
        app.OnActivatedAsync((32, 128), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), default).GetAwaiter().GetResult();
        var stage = new AppStage(app, width: 128, height: 32);
        Settle(app, stage);

        var shot = stage.Snapshot();   // 64x32 picture centred: red quadrant at the top left of it, bars around
        Assert.Equal(Pixel.Black, shot.GetPixel(10, 16));
        Assert.True(shot.GetPixel(50, 8) is { R: > 150, G: < 100 });
    }

    // ---- allocation ------------------------------------------------------------------------------------------------

    private void AssertNoAllocation(MediaApp app, string label)
    {
        var stage = Play(app);
        var run = stage.MeasureSteadyAllocation(windows: 8);
        output.WriteLine($"{label}: {run.MsPerFrame:F3} ms/frame, {run.Measured} windows, least {run.Least} bytes");
        Assert.True(run.Measured >= 3);
        Assert.True(run.Least < 256, $"{label}: least allocation in a steady window was {run.Least} bytes");
    }

    [Fact]
    public void SteadyState_Still_DoesNotAllocate()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.TestCard());
        AssertNoAllocation(App(fx, "Fit"), "still");
    }

    [Fact]
    public void SteadyState_Gif_DoesNotAllocate()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.Gif(frames: 4, delayCs: 10, width: 128, height: 64));
        AssertNoAllocation(App(fx, "Fit"), "gif");
    }

    [Fact]
    public void SteadyState_Scroll_DoesNotAllocate()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.Panorama(512, 64));
        AssertNoAllocation(App(fx, "Scroll"), "scroll");
    }

    [Fact]
    public void SteadyState_Video_DoesNotAllocate()
    {
        using var fx = new MediaFixture(new FakeTranscoder());
        fx.Add(MediaImages.Mp4Header());
        AssertNoAllocation(App(fx, "Fit"), "video");
    }

    [Fact]
    public void SteadyState_EmptyCard_DoesNotAllocate()
    {
        using var fx = new MediaFixture();
        AssertNoAllocation(App(fx), "empty");
    }
}
