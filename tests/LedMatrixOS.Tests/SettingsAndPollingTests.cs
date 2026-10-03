using System.Text.Json;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using Xunit;

namespace LedMatrixOS.Tests;

public class SettingsBinderTests
{
    private sealed class TestApp : SettingsAppBase
    {
        public override string Id => "test";
        public override string Name => "Test";

        [Setting("Max Items", Description = "How many", Min = 1, Max = 5)]
        public int MaxItems { get; set; } = 3;

        [Setting("Show Clock")]
        public bool ShowClock { get; set; }

        [Setting("Mode", Options = new[] { "a", "b" })]
        public string Mode { get; set; } = "a";

        [Setting("Label")]
        public string Label { get; set; } = "x";

        public List<string> Changed { get; } = new();
        protected override void OnSettingChanged(string key) => Changed.Add(key);
        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void GetSettings_UsesCamelCaseKeysAndMetadata()
    {
        var app = new TestApp { MaxItems = 4 };
        var settings = app.GetSettings().ToDictionary(s => s.Key);

        Assert.Equal(new[] { "label", "maxItems", "mode", "showClock" }, settings.Keys.OrderBy(k => k).ToArray());
        var max = settings["maxItems"];
        Assert.Equal("Max Items", max.Name);
        Assert.Equal("How many", max.Description);
        Assert.Equal(AppSettingType.Integer, max.Type);
        Assert.Equal(4, max.CurrentValue);
        Assert.Equal(1, max.MinValue);
        Assert.Equal(5, max.MaxValue);
        Assert.Equal(AppSettingType.Boolean, settings["showClock"].Type);
        Assert.Equal(AppSettingType.Select, settings["mode"].Type);
        Assert.Equal(new[] { "a", "b" }, settings["mode"].Options);
        Assert.Equal(AppSettingType.String, settings["label"].Type);
    }

    [Fact]
    public void UpdateSetting_ClampsIntegerToMinMax()
    {
        var app = new TestApp();
        app.UpdateSetting("maxItems", 99);
        Assert.Equal(5, app.MaxItems);
        app.UpdateSetting("maxItems", -4);
        Assert.Equal(1, app.MaxItems);
        app.UpdateSetting("maxItems", 2);
        Assert.Equal(2, app.MaxItems);
    }

    [Fact]
    public void UpdateSetting_KeyIsCaseInsensitive_AndUnknownKeyIsIgnored()
    {
        var app = new TestApp();
        app.UpdateSetting("MAXITEMS", 4);
        Assert.Equal(4, app.MaxItems);
        app.UpdateSetting("nope", 1);
        Assert.Equal(new[] { "maxItems" }, app.Changed); // canonical key, not "MAXITEMS"
    }

    [Fact]
    public void UpdateSetting_SelectIgnoresValuesOutsideOptions()
    {
        var app = new TestApp();
        app.UpdateSetting("mode", "b");
        Assert.Equal("b", app.Mode);
        app.UpdateSetting("mode", "zzz");
        Assert.Equal("b", app.Mode);
        app.UpdateSetting("mode", Json("\"a\""));
        Assert.Equal("a", app.Mode);
    }

    [Theory]
    [InlineData("7", 5)]       // clamped
    [InlineData("4", 4)]
    [InlineData("4.5", 3)]     // unparseable as an int: keeps the current value
    [InlineData("\"2\"", 2)]
    [InlineData("\"abc\"", 3)]
    [InlineData("true", 3)]
    [InlineData("null", 3)]
    public void UpdateSetting_CoercesJsonElementToInt(string json, int expected)
    {
        var app = new TestApp();
        app.UpdateSetting("maxItems", Json(json));
        Assert.Equal(expected, app.MaxItems);
    }

    [Fact]
    public void UpdateSetting_CoercesStringAndLongToInt()
    {
        var app = new TestApp();
        app.UpdateSetting("maxItems", "4");
        Assert.Equal(4, app.MaxItems);
        app.UpdateSetting("maxItems", 2L);
        Assert.Equal(2, app.MaxItems);
        app.UpdateSetting("maxItems", long.MaxValue);
        Assert.Equal(5, app.MaxItems);
        app.UpdateSetting("maxItems", "garbage");
        Assert.Equal(5, app.MaxItems);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"false\"", false)]
    [InlineData("\"maybe\"", true)]  // unparseable: keeps current (true)
    [InlineData("1", true)]
    public void UpdateSetting_CoercesJsonElementToBool(string json, bool expected)
    {
        var app = new TestApp { ShowClock = true };
        app.UpdateSetting("showClock", Json(json));
        Assert.Equal(expected, app.ShowClock);
    }

    [Fact]
    public void UpdateSetting_CoercesStringToBool()
    {
        var app = new TestApp();
        app.UpdateSetting("showClock", "true");
        Assert.True(app.ShowClock);
        app.UpdateSetting("showClock", false);
        Assert.False(app.ShowClock);
        app.UpdateSetting("showClock", "nonsense");
        Assert.False(app.ShowClock);
    }

    [Fact]
    public void UpdateSetting_StringFromJsonElementAndNumbers()
    {
        var app = new TestApp();
        app.UpdateSetting("label", Json("\"hello\""));
        Assert.Equal("hello", app.Label);
        app.UpdateSetting("label", 42);
        Assert.Equal("42", app.Label);
        app.UpdateSetting("label", Json("12"));
        Assert.Equal("12", app.Label);
    }

    [Fact]
    public void OnSettingChanged_FiredWithKeyAfterApply()
    {
        var app = new TestApp();
        app.UpdateSetting("label", "q");
        app.UpdateSetting("mode", "b");
        Assert.Equal(new[] { "label", "mode" }, app.Changed);
    }

    private sealed class BadApp : SettingsAppBase
    {
        public override string Id => "bad";
        public override string Name => "Bad";
        [Setting("X")] public double X { get; set; }
        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    [Fact]
    public void UnsupportedPropertyType_Throws()
        => Assert.Throws<InvalidOperationException>(() => new BadApp().GetSettings().ToList());
}

public class PollingLiveDataTests
{
    private sealed class PollApp : MatrixAppBase
    {
        public override string Id => "poll";
        public override string Name => "Poll";
        public ILiveData<int> Start(TimeSpan interval, Func<CancellationToken, Task<int>> fetch, CancellationToken stop = default) => Poll(interval, fetch, stop);
        public override void Render(FrameBuffer frame, CancellationToken cancellationToken) { }
    }

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task ValueArrives_AndLastUpdatedIsSet()
    {
        var app = new PollApp();
        var data = app.Start(Interval, _ => Task.FromResult(42));
        await WaitFor(() => data.LastUpdated != null);
        Assert.Equal(42, data.Value);
        Assert.Null(data.Error);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Error_IsSurfaced_ThenClearedOnRecovery_KeepingLastGoodValue()
    {
        var app = new PollApp();
        int calls = 0;
        int errorObserved = 0;
        var data = app.Start(Interval, _ =>
        {
            int n = Interlocked.Increment(ref calls);
            if (n == 1) return Task.FromResult(1);
            // Keep failing until the test has seen the error: with a 10 ms interval a fixed number of failures can come and go between two checks under load.
            if (Volatile.Read(ref errorObserved) == 0) throw new InvalidOperationException("boom");
            return Task.FromResult(100 + n);
        });

        await WaitFor(() => data.Error != null);
        Assert.Equal(1, data.Value); // last good value is retained
        Assert.IsType<InvalidOperationException>(data.Error);
        Volatile.Write(ref errorObserved, 1);

        await WaitFor(() => data.Error == null && data.Value > 100);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ChangedEvent_Fires()
    {
        var app = new PollApp();
        int changes = 0;
        var data = app.Start(Interval, _ => Task.FromResult(1));
        data.Changed += (_, _) => Interlocked.Increment(ref changes);
        await WaitFor(() => Volatile.Read(ref changes) > 0);
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopsPollingOnDeactivation()
    {
        var app = new PollApp();
        int calls = 0;
        CancellationToken seen = default;
        var data = app.Start(Interval, ct =>
        {
            seen = ct;
            return Task.FromResult(Interlocked.Increment(ref calls));
        });

        await WaitFor(() => Volatile.Read(ref calls) >= 3);
        await app.OnDeactivatedAsync(CancellationToken.None);
        Assert.True(seen.IsCancellationRequested);

        // Allow any in-flight fetch to finish, then verify nothing further happens
        await Task.Delay(200);
        int after = Volatile.Read(ref calls);
        await Task.Delay(300);
        Assert.Equal(after, Volatile.Read(ref calls));
        Assert.False(data.IsLoading);
    }

    [Fact]
    public async Task StopToken_EndsOnePollWithoutDeactivatingTheApp()
    {
        var app = new PollApp();
        using var cts = new CancellationTokenSource();
        int stoppedCalls = 0, otherCalls = 0;
        app.Start(Interval, _ => Task.FromResult(Interlocked.Increment(ref stoppedCalls)), cts.Token);
        app.Start(Interval, _ => Task.FromResult(Interlocked.Increment(ref otherCalls)));

        await WaitFor(() => Volatile.Read(ref stoppedCalls) >= 2);
        cts.Cancel();
        await Task.Delay(200);
        int stoppedAfter = Volatile.Read(ref stoppedCalls);
        int otherBefore = Volatile.Read(ref otherCalls);

        await WaitFor(() => Volatile.Read(ref otherCalls) >= otherBefore + 3);
        Assert.Equal(stoppedAfter, Volatile.Read(ref stoppedCalls));
        await app.OnDeactivatedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CancellationDuringFetch_DoesNotRecordAnError()
    {
        var app = new PollApp();
        var started = new TaskCompletionSource();
        var data = app.Start(Interval, async ct =>
        {
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return 0;
        });

        await started.Task.WaitAsync(Timeout);
        Assert.True(data.IsLoading);
        await app.OnDeactivatedAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.Null(data.Error);
    }
}
