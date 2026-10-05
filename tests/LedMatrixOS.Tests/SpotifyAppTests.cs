using System.Diagnostics;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Spotify;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class SpotifyAppTests(ITestOutputHelper output)
{
    /// <summary>A procedural 64x64 "album cover": diagonal gradient with a bright disc, so the golden shows the art placement.</summary>
    private static Sprite FakeArt()
    {
        var pixels = new Pixel[64 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = MathF.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32));
                pixels[y * 64 + x] = d < 14 ? new Pixel(250, 220, 90) : new Pixel((byte)(30 + x * 2), (byte)(20 + y), (byte)(150 + y));
            }
        return new Sprite(64, 64, [new Sprite.SpriteFrame(pixels, Enumerable.Repeat((byte)255, pixels.Length).ToArray(), TimeSpan.FromMilliseconds(100))]);
    }

    private static readonly Pixel[] Palette = [new(40, 40, 170), new(250, 220, 90), new(60, 90, 220), new(20, 20, 90)];

    private static NowPlaying Track(string title = "Midnight City", string artist = "M83", bool playing = true, bool saved = true,
        string? next = "Wait", int progress = 83000, bool art = true, bool artLoading = false) => new()
    {
        Title = title,
        Artist = artist,
        ProgressMs = progress,
        DurationMs = 243000,
        IsPlaying = playing,
        IsSaved = saved,
        NextTitle = next,
        Art = art ? FakeArt() : null,
        ArtLoading = artLoading,
        Palette = Palette,
    };

    private static (SpotifyApp App, AppStage Stage, MutableLive<NowPlaying> Data) Make(NowPlaying? value, bool authMissing = false, Exception? error = null, int steps = 45)
    {
        Fonts.Load();
        var app = new SpotifyApp(new HttpClient()) { Time = new FakeTime() };
        var data = new MutableLive<NowPlaying> { Value = value, Error = error };
        app.UseData(data, authMissing);
        var stage = new AppStage(app);
        stage.Step(33, steps);
        return (app, stage, data);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void Identity_AndSettings()
    {
        Fonts.Load();
        var app = new SpotifyApp(new HttpClient());
        Assert.Equal("spotify", app.Id);
        Assert.Equal(new[] { "showNextTrack", "showVisualiser", "background", "textSize", "albumArt", "showProgress", "scrollSpeed", "dimWhenPaused" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Equal(new[] { "Waves", "Gradient", "Off" }, app.GetSettings().Single(s => s.Key == "background").Options);
    }

    [Fact]
    public void Settings_RoundTripThroughJson()
    {
        var (app, _, _) = Make(Track());
        app.UpdateSetting("showNextTrack", System.Text.Json.JsonDocument.Parse("false").RootElement);
        app.UpdateSetting("background", System.Text.Json.JsonDocument.Parse("\"Off\"").RootElement);
        Assert.False(app.ShowNextTrack);
        Assert.Equal("Off", app.Background);
    }

    [Fact]
    public void Settings_ControlWhatIsDrawn()
    {
        var (app, stage, _) = Make(Track());
        var full = stage.Snapshot();

        app.ShowNextTrack = false;
        app.ShowVisualiser = false;
        app.Background = "Off";
        stage.Step(33);
        Assert.False(Stage.Same(full, stage.Snapshot()));
        // With the backdrop off, the area behind the text (right of the art, top row) is black
        Assert.Equal(Pixel.Black, stage.Snapshot().GetPixel(250, 1));
    }

    [Fact]
    public void States_AreChosenFromTheData()
    {
        Assert.Equal(SpotifyState.Playing, Make(Track()).App.State);
        Assert.Equal(SpotifyState.Playing, Make(Track(playing: false)).App.State);
        Assert.Equal(SpotifyState.Idle, Make(NowPlaying.Nothing).App.State);
        Assert.Equal(SpotifyState.Loading, Make(null).App.State);
        Assert.Equal(SpotifyState.Offline, Make(null, error: new HttpRequestException("down")).App.State);
        Assert.Equal(SpotifyState.NoLogin, Make(null, authMissing: true).App.State);
    }

    [Fact]
    public void Progress_InterpolatesFromTheAppClock()
    {
        var (app, stage, _) = Make(Track(progress: 10000), steps: 1);
        int start = app.PositionMs;
        stage.Step(1000, 5);
        Assert.InRange(app.PositionMs - start, 4900, 5100);
    }

    [Fact]
    public void Progress_StandsStillWhenPausedAndStopsAtTheEnd()
    {
        var (paused, pausedStage, _) = Make(Track(playing: false, progress: 10000), steps: 1);
        int at = paused.PositionMs;
        pausedStage.Step(1000, 5);
        Assert.Equal(at, paused.PositionMs);

        var (ending, endStage, _) = Make(Track(progress: 242000), steps: 1);
        endStage.Step(1000, 5);
        Assert.Equal(243000, ending.PositionMs);
    }

    [Fact]
    public void Progress_FollowsAFreshSnapshotThatJumped()
    {
        var (app, stage, data) = Make(Track(progress: 10000), steps: 1);
        stage.Step(1000, 3);
        data.Value = Track(progress: 120000);   // user seeked
        stage.Step(33);
        Assert.InRange(app.PositionMs, 119900, 121000);
    }

    [Fact]
    public void ArtDecoding_ReturnsNullForGarbage_AndResizesRealImages()
    {
        Assert.Null(NowPlaying.DecodeArt(null));
        Assert.Null(NowPlaying.DecodeArt([1, 2, 3, 4]));

        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(300, 300);
        using var ms = new MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, ms);
        var sprite = NowPlaying.DecodeArt(ms.ToArray());
        Assert.NotNull(sprite);
        Assert.Equal(64, sprite!.Width);
        Assert.Equal(64, sprite.Height);
    }

    [Fact]
    public void Colours_FallBackToSpotifyGreenForADarkPalette()
    {
        var colors = SpotifyColors.From([Pixel.Black]);
        Assert.Equal(SpotifyColors.SpotifyGreen, colors.Accent);
        Assert.Equal(SpotifyColors.Default, SpotifyColors.From([]));
    }

    // ---- goldens ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_Playing()
    {
        var (_, stage, _) = Make(Track());
        Golden(stage, "spotify_playing");
    }

    [Fact]
    public void Golden_Paused()
    {
        var (_, stage, _) = Make(Track(playing: false));
        Golden(stage, "spotify_paused");
    }

    [Fact]
    public void Golden_LongTitleMarquee()
    {
        var (_, stage, _) = Make(Track(title: "Bohemian Rhapsody (Remastered 2011) - Live at Wembley", artist: "Queen with a very long list of guest artists", next: null), steps: 1);
        stage.Step(33, 150);   // 2s pause, then well into the scroll
        Golden(stage, "spotify_long_title");
    }

    [Fact]
    public void Golden_NoArtPlaceholder()
    {
        var (_, stage, _) = Make(Track(art: false, saved: false));
        Golden(stage, "spotify_no_art");
    }

    [Fact]
    public void Golden_NothingPlaying()
    {
        var (_, stage, _) = Make(NowPlaying.Nothing);
        Golden(stage, "spotify_idle");
    }

    [Fact]
    public void Golden_Offline()
    {
        var (_, stage, _) = Make(null, error: new HttpRequestException("down"));
        Golden(stage, "spotify_offline");
    }

    [Fact]
    public void Golden_Loading()
    {
        var (_, stage, _) = Make(null);
        Golden(stage, "spotify_loading");
    }

    [Fact]
    public void Golden_NoLogin()
    {
        var (_, stage, _) = Make(null, authMissing: true);
        Golden(stage, "spotify_no_login");
    }

    [Fact]
    public void Golden_LargeText()
    {
        var (_, stage, _) = Make(Track(title: "Midnight City", artist: "M83"), steps: 1);
        stage.Step(33, 1);
        ((SpotifyApp)stage.App).UpdateSetting("textSize", "Large");
        stage.Step(33, 45);
        Golden(stage, "spotify_large_text");
    }

    [Fact]
    public void Golden_ArtHidden_NoProgress()
    {
        var (app, stage, _) = Make(Track(title: "Midnight City", artist: "M83"));
        app.UpdateSetting("albumArt", "Hide");
        app.UpdateSetting("showProgress", false);
        stage.Step(33, 45);
        Golden(stage, "spotify_art_hidden_no_progress");
    }

    [Fact]
    public void Golden_ArtLoading_ThenFailed()
    {
        var (_, stage, _) = Make(Track(art: false, artLoading: true), steps: 20);
        Golden(stage, "spotify_art_loading");
        stage.Step(33, 240);   // past the 6s grace: treated as failed, the note placeholder
        Golden(stage, "spotify_art_failed_after_loading");
    }

    [Fact]
    public void Golden_PausedDimSetting()
    {
        var (app, stage, _) = Make(Track(playing: false), steps: 1);
        app.UpdateSetting("dimWhenPaused", 0);
        stage.Step(33, 45);
        Golden(stage, "spotify_paused_no_dim");
    }

    [Fact]
    public void PausedAndLarge_DoNotAllocate()
    {
        var (app, stage, _) = Make(Track(title: "A rather long title that has to scroll along the screen", playing: false, progress: 10000));
        app.UpdateSetting("textSize", "Large");
        for (int i = 0; i < 150; i++) { stage.Step(33); stage.Render(); }
        long least = long.MaxValue;
        for (int window = 0; window < 8; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) { stage.Step(33); stage.Render(); }
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }

    // ---- allocation -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage, _) = Make(Track(title: "A rather long title that has to scroll along the screen", progress: 10000));
        for (int i = 0; i < 150; i++) { stage.Step(33); stage.Render(); }

        // The time label is rebuilt once a second, so take the quietest of several windows.
        long least = long.MaxValue;
        double ms = 0;
        for (int window = 0; window < 8; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { stage.Step(33); stage.Render(); }
            sw.Stop();
            ms = sw.Elapsed.TotalMilliseconds / 20;
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        output.WriteLine($"spotify: {ms:F3} ms/frame, least window {least} bytes");
        Assert.True(least < 256, $"least allocation in a steady window: {least} bytes");
    }
}
