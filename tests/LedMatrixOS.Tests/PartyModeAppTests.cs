using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Party;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class PartyModeAppTests(ITestOutputHelper output)
{
    private static readonly float[] Silence = new float[AudioDataService.FrequencyBandCount];

    // ---- beat signal --------------------------------------------------------------------------------------------------

    [Fact]
    public void Beat_SyntheticTicksAtTheTempo()
    {
        var beat = new BeatSignal { Bpm = 120 };   // one beat every 0.5 s
        int beats = 0;
        for (int i = 0; i < 1000; i++)           // 10 s at 10 ms
        {
            beat.Update(0.01f, Silence, live: false);
            if (beat.Beat) beats++;
        }
        Assert.InRange(beats, 19, 20);
        Assert.False(beat.IsLive);
        Assert.Equal(beats, beat.Count);
    }

    [Fact]
    public void Beat_PulseJumpsToOneThenDecays()
    {
        var beat = new BeatSignal { Bpm = 60 };
        while (!beat.Beat) beat.Update(0.01f, Silence, live: false);
        Assert.Equal(1f, beat.Pulse);
        beat.Update(0.1f, Silence, live: false);
        Assert.False(beat.Beat);
        Assert.InRange(beat.Pulse, 0.55f, 0.65f);
        for (int i = 0; i < 40; i++) beat.Update(0.01f, Silence, live: false);
        Assert.Equal(0f, beat.Pulse);
    }

    [Fact]
    public void Beat_LiveAudioDetectsBassHitsAndIgnoresSteadyLoudness()
    {
        var beat = new BeatSignal();
        var loud = new float[64];
        Array.Fill(loud, 0.6f);

        // A constant level never beats once the average has caught up.
        for (int i = 0; i < 100; i++) beat.Update(0.016f, loud, live: true);
        int steady = 0;
        for (int i = 0; i < 100; i++) { beat.Update(0.016f, loud, live: true); if (beat.Beat) steady++; }
        Assert.True(beat.IsLive);
        Assert.Equal(0, steady);

        // Kicks every ~0.5 s do.
        int beats = 0;
        var kick = new float[64];
        Array.Fill(kick, 0.1f);
        for (int b = 0; b < 4; b++) kick[b] = 0.95f;
        var quiet = new float[64];
        Array.Fill(quiet, 0.1f);
        for (int i = 0; i < 300; i++)
        {
            beat.Update(0.016f, i % 30 < 2 ? kick : quiet, live: true);
            if (beat.Beat) beats++;
        }
        Assert.InRange(beats, 8, 10);
    }

    [Fact]
    public void Beat_LiveBeatsRespectTheMinimumGap()
    {
        var beat = new BeatSignal();
        var quiet = new float[64]; Array.Fill(quiet, 0.02f);
        var kick = new float[64]; Array.Fill(kick, 0.9f);
        int beats = 0;
        for (int i = 0; i < 100; i++)   // alternating every frame: 1.6 s
        {
            beat.Update(0.016f, i % 2 == 0 ? kick : quiet, live: true);
            if (beat.Beat) beats++;
        }
        Assert.True(beats <= 8, $"{beats} beats in 1.6 s");
    }

    // ---- app -----------------------------------------------------------------------------------------------------------

    private static (PartyModeApp App, AppStage Stage) Party(string mode, AudioDataService? svc = null, int warmMs = 600)
    {
        Fonts.Load();
        var app = new PartyModeApp(svc) { Time = new FakeTime(), Seed = 7, Mode = mode };
        var stage = new AppStage(app);
        stage.Step(33, warmMs / 33);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    private static int Lit(FrameBuffer frame)
    {
        int lit = 0;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                var p = frame.GetPixel(x, y);
                if (p.R + p.G + p.B > 60) lit++;
            }
        return lit;
    }

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new PartyModeApp();
        Assert.Equal("party", app.Id);
        Assert.Equal(new[] { "mode", "secondsPerMode", "tempo" }, app.GetSettings().Select(s => s.Key).ToArray());
    }

    [Fact]
    public void Auto_CyclesBarsFireBursts()
    {
        var (app, stage) = Party("Auto", warmMs: 33);
        app.SecondsPerMode = 4;
        var seen = new List<string> { app.CurrentMode };
        for (int i = 0; i < 600; i++)
        {
            stage.Step(33);
            if (app.CurrentMode != seen[^1]) seen.Add(app.CurrentMode);
        }
        Assert.Equal(new[] { "Bars", "Fire", "Bursts", "Bars", "Fire" }, seen.Take(5).ToArray());
    }

    [Fact]
    public void FixedMode_StaysPut()
    {
        var (app, stage) = Party("Fire");
        stage.Step(33, 600);
        Assert.Equal("Fire", app.CurrentMode);
    }

    [Fact]
    public void WithoutAudio_TheSyntheticBeatKeepsGoing()
    {
        var (app, stage) = Party("Bursts", new AudioDataService(), warmMs: 33);
        int beats = 0;
        for (int i = 0; i < 300; i++)   // 10 s
        {
            stage.Step(33);
            if (app.Beat.Beat) beats++;
        }
        Assert.False(app.Beat.IsLive);
        Assert.InRange(beats, 19, 22);   // 124 bpm
        Assert.True(app.BurstParticles > 0);
    }

    [Fact]
    public void WithAudio_TheBeatFollowsTheBass()
    {
        var svc = new AudioDataService();
        var (app, stage) = Party("Bursts", svc, warmMs: 33);
        int beats = 0;
        for (int i = 0; i < 150; i++)
        {
            // a kick every 0.5 s, in the low bands only
            var samples = new float[64 * 4];
            Array.Fill(samples, i % 15 < 2 ? 0.95f : 0.05f);
            svc.AddAudioSamples(samples);
            stage.Step(33);
            if (app.Beat.Beat) beats++;
        }
        Assert.True(app.Beat.IsLive);
        Assert.InRange(beats, 5, 12);
    }

    [Theory]
    [InlineData("Bars", "party_bars")]
    [InlineData("Fire", "party_fire")]
    [InlineData("Bursts", "party_bursts")]
    public void Golden_Modes(string mode, string name)
    {
        var (app, stage) = Party(mode, warmMs: 1500);
        Assert.True(Lit(stage.Snapshot()) > 100, mode + " must not be blank");
        Golden(stage, name);
    }

    [Theory]
    [InlineData("Bars")]
    [InlineData("Fire")]
    [InlineData("Bursts")]
    [InlineData("Auto")]
    public void SteadyState_DoesNotAllocate(string mode)
    {
        var (app, stage) = Party(mode, warmMs: 3000);
        app.SecondsPerMode = 3;
        stage.Step(33, 200);   // visit every mode once so each one's lazy buffers exist
        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { stage.Step(33); stage.Render(); }
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        output.WriteLine($"party {mode}: " + string.Join(", ", windows));
        Assert.True(windows.Min() == 0, $"{mode} allocated bytes per 100-frame window: " + string.Join(", ", windows));
    }
}
