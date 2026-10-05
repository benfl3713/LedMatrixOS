using System.Text.Json;
using LedMatrixOS.Apps.Screens;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Screens;
using LedMatrixOS.Graphics.UI;
using Xunit;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class ScreenAppTests
{
    // ---- sample screens ----------------------------------------------------------------------------------------------

    private const string DashboardJson = """
    {"id":"dash","name":"Dashboard","root":{"type":"dock",
      "left":{"type":"stack","direction":"vertical","width":120,"padding":4,"gap":2,"children":[
        {"type":"clock","format":"HH:mm","font":"Big","color":"#ffffff"},
        {"type":"clock","format":"ddd d MMM","font":"Small","color":"#8fa3b8"}]},
      "fill":{"type":"panel","background":"#10202e","radius":3,"margin":2,"padding":4,"children":[
        {"type":"stack","direction":"vertical","gap":3,"children":[
          {"type":"stack","direction":"horizontal","gap":4,"children":[
            {"type":"label","text":"{weather.temp}°","font":"Big","color":"#ffd24a"},
            {"type":"label","text":"feels {weather.feels}°","color":"#c8d4e0","valign":"bottom"}]},
          {"type":"divider","color":"#2a4358"},
          {"type":"label","text":"H {weather.high}°  L {weather.low}°  rain {weather.precip}%","color":"#7fc4ff"}]}]}}}
    """;

    private const string StatusJson = """
    {"id":"status","name":"Status","root":{"type":"dock",
      "top":{"type":"label","text":"HOME STATUS","font":"QuiteSmall","color":"#ffffff","margin":1},
      "bottom":{"type":"stack","direction":"horizontal","gap":3,"margin":1,"children":[
        {"type":"pill","text":"BINS","font":"QuiteSmall","background":"#2d6a3e"},
        {"type":"marquee","text":"{bin_day}","font":"QuiteSmall","color":"#c8d4e0","grow":1,"valign":"middle"}]},
      "fill":{"type":"list","source":"Victoria|tube.victoria;Central|tube.central;Lounge|ha:sensor.lounge_temp;Front door|ha:binary_sensor.front_door","max_items":4,"gap":1,"margin":2,
        "item":{"type":"stack","direction":"horizontal","children":[
          {"type":"label","text":"{item.label}","font":"QuiteSmall","color":"#9fb0c2","grow":1,"valign":"middle"},
          {"type":"pill","text":"{item}","font":"QuiteSmall","background":"#243546"}]}}}}
    """;

    private const string ChartsJson = """
    {"id":"charts","name":"Charts","root":{"type":"stack","direction":"vertical","gap":3,"padding":3,"children":[
      {"type":"stack","direction":"horizontal","gap":6,"grow":1,"children":[
        {"type":"sparkline","values":"12,14,13,17,19,18,22,21,24,20,18,19","color":"#4cc9f0","grow":1},
        {"type":"bar_chart","values":"3,5,8,6,9,4,7,10,6","color":"#7bd88f","grow":1}]},
      {"type":"stack","direction":"horizontal","gap":4,"children":[
        {"type":"label","text":"rain {weather.precip}%","color":"#7fc4ff"},
        {"type":"progress","value":{"bind":"weather.precip"},"max":100,"color":"#4cc9f0","background":"#16222e","height":5,"grow":1,"valign":"middle"}]}]}}
    """;

    private static ScreenDefinition Parse(string json)
    {
        var doc = ScreenDocument.TryParse("{\"screens\":[" + json + "]}", out var error);
        Assert.True(doc is not null, error);
        Assert.Empty(doc!.Validate());
        return doc.Screens[0];
    }

    private static ScreenApp Make(string? json, Action<ScreenApp>? data = null)
    {
        var store = new ScreenStore();
        if (json != null) store.Replace(Parse(json));
        var app = new ScreenApp(store, new HttpClient()) { ScreenId = json == null ? "missing" : Parse(json).Id };
        data?.Invoke(app);
        return app;
    }

    private static void Weather(ScreenApp app)
    {
        app.UseValue("weather.temp", "11", 11);
        app.UseValue("weather.feels", "8", 8);
        app.UseValue("weather.high", "13", 13);
        app.UseValue("weather.low", "5", 5);
        app.UseValue("weather.precip", "40", 40);
    }

    private static void Home(ScreenApp app)
    {
        app.UseValue("tube.victoria", "Good Service", 10);
        app.UseValue("tube.central", "Minor Delays", 9);
        app.UseValue("ha:sensor.lounge_temp", "21.5°C", 21.5);
        app.UseValue("ha:binary_sensor.front_door", "closed");
        app.UseValue("bin_day", "Black tomorrow", 1);
    }

    private static readonly Dictionary<string, (string Json, Action<ScreenApp> Data)> Samples = new()
    {
        ["screen_dashboard"] = (DashboardJson, Weather),
        ["screen_status"] = (StatusJson, Home),
        ["screen_charts"] = (ChartsJson, Weather),
    };

    public static IEnumerable<object[]> SampleNames => Samples.Keys.Select(k => new object[] { k });

    private static AmbientRig Rig(string name)
    {
        var (json, data) = Samples[name];
        return new AmbientRig(Make(json, data));
    }

    // ---- goldens and allocations -------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Sample_MatchesGolden(string name)
    {
        var frame = Rig(name).Advance(1500).Copy();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Sample_SteadyState_AllocatesNothing(string name) => Rig(name).AssertNoAllocationsPerFrame();

    // ---- degradation -------------------------------------------------------------------------------------------------

    [Fact]
    public void MissingScreen_ShowsNotFound()
    {
        var rig = new AmbientRig(Make(null));
        rig.Advance(100);
        var label = Assert.IsType<Label>(rig.App.Root);
        Assert.Equal("Screen not found", label.Text);
        Assert.False(SnapshotHelper.IsBlank(rig.Draw()));
    }

    [Fact]
    public void InvalidNodes_AreSkipped_NotThrown()
    {
        // Bypasses ScreenDocument.Validate on purpose: the runtime must cope with anything that reaches it.
        var doc = ScreenDocument.TryParse("""
            {"screens":[{"id":"bad","name":"Bad","root":{"type":"stack","children":[
              {"type":"nonsense"},
              {"type":"label","text":"{nope.key}"},
              {"type":"label","text":"unclosed {time"},
              {"type":"progress"},
              {"type":"sparkline","values":[1,2]},
              {"type":"clock","format":"%%%q"},
              {"type":"label","text":"kept","width":"wide","color":"red","font":"Huge"}]}}]}
            """, out _)!;
        var store = new ScreenStore();
        store.Replace(doc.Screens[0]);
        var rig = new AmbientRig(new ScreenApp(store, new HttpClient()) { ScreenId = "bad" });
        rig.Advance(100);
        var stack = Assert.IsType<Stack>(rig.App.Root);
        Assert.Equal(2, stack.Children.Count);              // the clock (bad format falls back) and "kept"
        Assert.False(SnapshotHelper.IsBlank(rig.Draw()));
    }

    [Fact]
    public void ScreenId_IsAHiddenPreset()
    {
        var app = Make(DashboardJson);
        Assert.Empty(app.GetSettings());
        app.UpdateSetting("screenId", "status");
        Assert.Equal("status", app.ScreenId);
    }

    // ---- bindings ----------------------------------------------------------------------------------------------------

    private sealed class CountingPolls : IPollHost
    {
        public int Count;

        public ILiveData<T> Poll<T>(TimeSpan interval, Func<CancellationToken, Task<T>> fetch)
        {
            Count++;
            return new FakeLive<T>();
        }
    }

    private static BindingResolver Resolver(CountingPolls polls, TimeProvider? time = null, Dictionary<string, ILiveData<BindValue>>? overrides = null) =>
        new(time ?? new FakeTime(), null, new HttpClient(), polls, overrides);

    [Fact]
    public void Template_FormatsOnlyWhenTheValueChanges()
    {
        var temp = new StaticLive("11", 11);
        var text = Resolver(new CountingPolls(), overrides: new() { ["weather.temp"] = temp }).CompileText("{weather.temp}°")!;

        var first = text.Get();
        Assert.Equal("11°", first);
        Assert.Same(first, text.Get());                      // cached: no new string while nothing changed

        temp.Value = new BindValue("12", 12);
        var second = text.Get();
        Assert.Equal("12°", second);
        Assert.Same(second, text.Get());
    }

    [Fact]
    public void Template_MixesLiteralsEscapesAndSeveralKeys()
    {
        var overrides = new Dictionary<string, ILiveData<BindValue>>
        {
            ["weather.high"] = new StaticLive("13", 13),
            ["weather.low"] = new StaticLive("5", 5),
        };
        var r = Resolver(new CountingPolls(), overrides: overrides);
        Assert.Equal("H13/L5 {raw}", r.CompileText("H{weather.high}/L{weather.low} {{raw}}")!.Get());
        Assert.Equal("plain", r.CompileText("plain")!.Constant);
        Assert.Null(r.CompileText("bad {key"));
        Assert.Null(r.CompileText("{unknown.thing}"));
    }

    [Fact]
    public void Template_MissingDataShowsDashes()
    {
        var text = Resolver(new CountingPolls()).CompileText("{weather.temp}°")!;   // the poll has no value yet
        Assert.Equal("--°", text.Get());
    }

    [Fact]
    public void Time_ComesFromTheAppClock()
    {
        var clock = new FakeTime();
        var text = Resolver(new CountingPolls(), clock).CompileText("{time}")!;
        Assert.Equal("13:45", text.Get());
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal("13:46", text.Get());
    }

    [Fact]
    public void DistinctKeys_GetOnePollEach_AndWeatherSharesOne()
    {
        var polls = new CountingPolls();
        var r = Resolver(polls);
        r.CompileText("{weather.temp} {weather.feels} {weather.high} {weather.temp}");
        r.CompileText("{weather.low}");
        Assert.Equal(1, polls.Count);                         // five weather fields, one forecast request

        r.CompileText("{tube.victoria} {tube.victoria}");
        r.CompileText("{tube.central}");
        Assert.Equal(3, polls.Count);                         // one per line

        Assert.Equal(6, r.SourceCount);                       // temp, feels, high, low, victoria, central (each created once)
    }

    [Fact]
    public void Numbers_AcceptLiteralsStringsAndBindings()
    {
        var r = Resolver(new CountingPolls(), overrides: new() { ["weather.precip"] = new StaticLive("40", 40) });
        float Read(string json) => r.CompileNumber(JsonDocument.Parse(json).RootElement)!();

        Assert.Equal(7f, Read("7"));
        Assert.Equal(2.5f, Read("\"2.5\""));
        Assert.Equal(40f, Read("{\"bind\":\"weather.precip\"}"));
        Assert.Equal(40f, Read("\"{weather.precip}\""));
        Assert.True(float.IsNaN(Read("\"abc\"")));
    }

    [Fact]
    public void Series_ParseTemplatesAndRollHistory()
    {
        var low = new StaticLive("5", 5);
        var r = Resolver(new CountingPolls(), overrides: new() { ["weather.low"] = low });

        var series = r.CompileSeries(JsonDocument.Parse("\"{weather.low}, 7 9;x\"").RootElement)!;
        Assert.Equal(new[] { 5f, 7f, 9f }, series().ToArray());

        var history = r.CompileSeries(JsonDocument.Parse("{\"bind\":\"weather.low\"}").RootElement)!;
        Assert.Equal(new[] { 5f }, history().ToArray());
        low.Value = new BindValue("6", 6);
        Assert.Equal(new[] { 5f, 6f }, history().ToArray());
        Assert.Equal(new[] { 5f, 6f }, history().ToArray());  // unchanged value adds nothing
    }
}
