using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class QrAppTests(ITestOutputHelper output)
{
    private static (QrApp App, AppStage Stage) Screen(Action<QrApp>? configure = null, int warmFrames = 10)
    {
        Fonts.Load();
        var app = new QrApp { Time = new FakeTime() };
        configure?.Invoke(app);
        var stage = new AppStage(app);
        stage.Step(33, warmFrames);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void Identity_AndSettings_NeverExposeThePassword()
    {
        var app = new QrApp();
        Assert.Equal("qr", app.Id);
        var keys = app.GetSettings().Select(s => s.Key).ToArray();
        Assert.Equal(new[] { "mode", "text", "label", "ssid", "security", "correction", "invert" }, keys);
        Assert.DoesNotContain(keys, k => k.Contains("pass", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AppSettingType.Select, app.GetSettings().Single(s => s.Key == "mode").Type);
    }

    [Theory]
    [InlineData("Home", "hunter2", "WPA", "WIFI:T:WPA;S:Home;P:hunter2;;")]
    [InlineData("Home", "hunter2", "WEP", "WIFI:T:WEP;S:Home;P:hunter2;;")]
    [InlineData("Guest", "", "WPA", "WIFI:T:nopass;S:Guest;;")]
    [InlineData("Guest", "ignored", "None", "WIFI:T:nopass;S:Guest;;")]
    [InlineData("A;B:C,D", "p\\w\"d;", "WPA", "WIFI:T:WPA;S:A\\;B\\:C\\,D;P:p\\\\w\\\"d\\;;;")]
    public void WifiPayload_FollowsTheStandardFormat(string ssid, string password, string security, string expected) =>
        Assert.Equal(expected, QrApp.WifiPayload(ssid, password, security));

    [Fact]
    public void TextMode_EncodesTheTextSetting()
    {
        var (app, _) = Screen(a => a.Text = "https://ledmatrix.local/hello");
        Assert.Equal("https://ledmatrix.local/hello", app.Payload);
    }

    [Fact]
    public void WifiMode_UsesTheConfigurationPassword()
    {
        Fonts.Load();
        var app = new QrApp { Time = new FakeTime(), Mode = "WiFi", Ssid = "Home" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Qr:WifiPassword"] = "s3cret" }).Build();
        app.OnActivatedAsync((64, 256), config, CancellationToken.None).GetAwaiter().GetResult();
        var stage = new AppStage(app);
        stage.Step(33, 5);

        Assert.Equal("WIFI:T:WPA;S:Home;P:s3cret;;", app.Payload);
        Assert.DoesNotContain("s3cret", app.GetSettings().Select(s => s.CurrentValue?.ToString() ?? "").ToArray());
    }

    [Fact]
    public void WifiMode_WithoutANetworkNameShowsNothingToScan()
    {
        var (app, _) = Screen(a => a.Mode = "WiFi");
        Assert.Equal("", app.Payload);
    }

    [Fact]
    public void WifiAlias_SelectsTheWifiMode()
    {
        var alias = BuiltInApps.Aliases().Single(a => a.Alias == "wifi");
        Assert.Equal("qr", alias.TargetId);
        Assert.Equal("WiFi", alias.Preset["mode"]);
    }

    [Fact]
    public void RegisteredInTheBuiltInList()
    {
        Assert.Contains(typeof(QrApp), BuiltInApps.GetAll());
        Assert.Contains(typeof(PartyModeApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void SettingChanges_ReEncode()
    {
        var (app, stage) = Screen(a => a.Text = "one");
        Assert.Equal("one", app.Payload);
        app.UpdateSetting("text", System.Text.Json.JsonDocument.Parse("\"two\"").RootElement);
        stage.Step(33);
        Assert.Equal("two", app.Payload);
    }

    [Fact]
    public void Golden_Url()
    {
        var (_, stage) = Screen(a => { a.Text = "https://github.com/ledmatrix"; a.Label = "Scan me"; });
        Golden(stage, "qr_url");
    }

    [Fact]
    public void Golden_Wifi()
    {
        var (app, stage) = Screen(a => { a.Mode = "WiFi"; a.Ssid = "HomeNet"; a.Label = "Join Wi-Fi"; a.UseWifiPassword("correct-horse"); });
        Assert.StartsWith("WIFI:T:WPA;S:HomeNet;P:", app.Payload);
        Golden(stage, "qr_wifi");
    }

    [Fact]
    public void Golden_Inverted()
    {
        var (_, stage) = Screen(a => { a.Text = "HELLO"; a.Label = "Hi"; a.Invert = true; });
        Golden(stage, "qr_inverted");
    }

    [Fact]
    public void Golden_TooLong()
    {
        var (_, stage) = Screen(a => { a.Text = new string('x', 400); a.Label = "Too much"; });
        Golden(stage, "qr_too_long");
    }

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage) = Screen(a => { a.Text = "https://github.com/ledmatrix/some/long/path"; a.Label = "Scan me"; }, warmFrames: 60);
        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { stage.Step(33); stage.Render(); }
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        output.WriteLine("qr allocation per 100 frames: " + string.Join(", ", windows));
        Assert.True(windows.Min() == 0, "allocated bytes per 100-frame window: " + string.Join(", ", windows));
    }
}
