using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Pet;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class PetAppTests(ITestOutputHelper output)
{
    private sealed class Inputs : ICalendarProgress, IDaylight, IRainSensor
    {
        public int Events;
        public bool Night;
        public bool Rain;
        public int EventsCompletedToday(DateTimeOffset localNow) => Events;
        public bool IsNight(DateTimeOffset localNow) => Night;
        public bool IsRaining => Rain;
    }

    private static (PetApp App, AppStage Stage, Inputs Inputs) Room(int fullness = 80, int events = 0, bool night = false, bool rain = false, int warmFrames = 45)
    {
        Fonts.Load();
        var inputs = new Inputs { Events = events, Night = night, Rain = rain };
        var app = new PetApp(inputs, inputs, inputs) { Time = new FakeTime(), Fullness = fullness };
        var stage = new AppStage(app);
        stage.Step(33, warmFrames);
        return (app, stage, inputs);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- rules -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 0, true, false, PetMood.Sleeping)]     // night beats everything
    [InlineData(5, 0, true, false, PetMood.Sleeping)]
    [InlineData(24, 6, false, false, PetMood.Hungry)]       // hunger beats a good day
    [InlineData(25, 0, false, false, PetMood.Sad)]
    [InlineData(45, 0, false, false, PetMood.Content)]
    [InlineData(80, 0, false, false, PetMood.Content)]
    [InlineData(85, 0, false, false, PetMood.Happy)]
    [InlineData(60, 5, false, false, PetMood.Happy)]        // finished events cheer it up
    [InlineData(100, 0, false, true, PetMood.Content)]      // rain dampens the mood
    [InlineData(60, 0, false, true, PetMood.Sad)]
    public void Mood_FollowsInputs(int fullness, int events, bool night, bool rain, PetMood expected) =>
        Assert.Equal(expected, PetRules.MoodOf(fullness, events, night, rain));

    [Theory]
    [InlineData(21, false)]
    [InlineData(22, true)]
    [InlineData(3, true)]
    [InlineData(6, false)]
    public void ClockDaylight_IsNightBetween22And6(int hour, bool night) =>
        Assert.Equal(night, new ClockDaylight().IsNight(new DateTimeOffset(2026, 1, 2, hour, 30, 0, TimeSpan.Zero)));

    [Fact]
    public void Defaults_AreNoEventsNoRain()
    {
        Assert.Equal(0, new NoCalendarProgress().EventsCompletedToday(DateTimeOffset.UnixEpoch));
        Assert.False(new NoRain().IsRaining);
    }

    // ---- app ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new PetApp();
        Assert.Equal("pet", app.Id);
        Assert.Equal(new[] { "petName", "hoursToEmpty", "feed", "fullness" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.True(app.GetSettings().Single(s => s.Key == "fullness").Advanced);
    }

    [Fact]
    public void Belly_EmptiesOverTime()
    {
        var (app, stage, _) = Room(fullness: 80, warmFrames: 1);
        app.HoursToEmpty = 1;                       // 100 points an hour
        stage.Step(1000, 36 * 6);                   // 216 s
        Assert.InRange(app.Fullness, 73, 75);
    }

    [Fact]
    public void FinishedCalendarEvents_FeedThePet()
    {
        var (app, stage, inputs) = Room(fullness: 50);
        inputs.Events = 2;
        stage.Step(1000, 6);                        // past the input read interval

        Assert.InRange(app.Fullness, 73, 74);
    }

    [Fact]
    public void Feed_SettingFeedsThenResetsItself()
    {
        var (app, _, _) = Room(fullness: 30);
        app.UpdateSetting("feed", true);

        Assert.Equal(70, app.Fullness);
        Assert.False(app.Feed);
    }

    [Fact]
    public void Fullness_IsRestoredFromSettings()
    {
        var (app, stage, _) = Room(fullness: 12);
        app.UpdateSetting("fullness", System.Text.Json.JsonDocument.Parse("93").RootElement);
        stage.Step(33, 3);
        Assert.Equal(93, app.Fullness);
        Assert.Equal(PetMood.Happy, app.Mood);
    }

    [Fact]
    public void Mood_ReactsToNightAndWeatherLive()
    {
        var (app, stage, inputs) = Room(fullness: 100);
        Assert.Equal(PetMood.Happy, app.Mood);

        inputs.Rain = true;
        stage.Step(1000, 6);
        Assert.Equal(PetMood.Content, app.Mood);

        inputs.Night = true;
        stage.Step(1000, 6);
        Assert.Equal(PetMood.Sleeping, app.Mood);
    }

    [Fact]
    public void SameTimeDrawsTheSamePicture()
    {
        Assert.True(Stage.Same(Room().Stage.Snapshot(), Room().Stage.Snapshot()));
    }

    [Theory]
    [InlineData(100, 0, false, false, "pet_happy")]
    [InlineData(60, 0, false, false, "pet_content")]
    [InlineData(35, 0, false, false, "pet_gloomy")]
    [InlineData(10, 0, false, false, "pet_hungry")]
    [InlineData(90, 0, true, false, "pet_sleeping")]
    [InlineData(70, 1, false, true, "pet_raining")]
    public void Golden_Moods(int fullness, int events, bool night, bool rain, string name) =>
        Golden(Room(fullness, events, night, rain).Stage, name);

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage, _) = Room(fullness: 100);
        var run = stage.MeasureSteadyAllocation(windows: 6);
        output.WriteLine($"pet: {run.MsPerFrame:F3} ms/frame");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }

    [Fact]
    public void IsRegistered() => Assert.Contains(typeof(PetApp), BuiltInApps.GetAll());
}
