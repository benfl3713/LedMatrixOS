using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Aquarium;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class AquariumAppTests(ITestOutputHelper output)
{
    private static (AquariumApp App, AppStage Stage) Tank(int fish = 8, string water = "Blue", int seconds = 4)
    {
        Fonts.Load();
        var app = new AquariumApp { Time = new FakeTime(), FishCount = fish, Water = water };
        var stage = new AppStage(app);
        stage.Step(33, seconds * 30);
        return (app, stage);
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
        var app = new AquariumApp();
        Assert.Equal("aquarium", app.Id);
        Assert.Equal(new[] { "fishCount", "water" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.Equal(AquariumField.MaxFish, app.GetSettings().First().MaxValue);
    }

    [Fact]
    public void FishCount_IsClampedBySetting()
    {
        var (app, _) = Tank();
        app.UpdateSetting("fishCount", System.Text.Json.JsonDocument.Parse("500").RootElement);
        Assert.Equal(AquariumField.MaxFish, app.FishCount);
    }

    [Fact]
    public void SameTimeDrawsTheSamePicture()
    {
        var (_, a) = Tank();
        var (_, b) = Tank();
        Assert.True(Stage.Same(a.Snapshot(), b.Snapshot()));
    }

    [Fact]
    public void Fish_Move()
    {
        var (_, stage) = Tank(fish: 4, seconds: 1);
        var before = stage.Snapshot();
        stage.Step(33, 30);
        Assert.False(Stage.Same(before, stage.Snapshot()));
    }

    [Fact]
    public void Golden_DefaultTank() => Golden(Tank().Stage, "aquarium_default");

    [Fact]
    public void Golden_BusyDeepSea() => Golden(Tank(fish: 20, water: "Deep Sea", seconds: 9).Stage, "aquarium_busy_deep_sea");

    [Fact]
    public void Golden_EmptyTealTank() => Golden(Tank(fish: 0, water: "Teal").Stage, "aquarium_empty_teal");

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage) = Tank(fish: 16);
        var run = stage.MeasureSteadyAllocation(windows: 6);
        output.WriteLine($"aquarium: {run.MsPerFrame:F3} ms/frame");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }

    [Fact]
    public void IsRegistered() => Assert.Contains(typeof(AquariumApp), BuiltInApps.GetAll());
}
