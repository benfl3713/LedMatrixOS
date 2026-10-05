using LedMatrixOS.Core.Screens;
using Xunit;

namespace LedMatrixOS.Tests;

public class ScreenDocumentTests
{
    private const string Good = """
    {
      "screens": [
        { "id": "home", "name": "Home",
          "root": { "type": "dock", "gap": 1,
            "top": { "type": "label", "text": "{time} {weather.temp}", "font": "Big", "color": "#fff" },
            "fill": { "type": "stack", "direction": "horizontal", "children": [
              { "type": "progress", "value": { "bind": "weather.precip" }, "max": 100 },
              { "type": "pill", "text": "{tube.victoria}", "background": "#ff0000" },
              { "type": "label", "text": "{ha:sensor.temp}", "visible": true }
            ] } } }
      ]
    }
    """;

    private static List<ScreenError> Check(string json)
    {
        var doc = ScreenDocument.TryParse(json, out var err);
        Assert.True(doc != null, err);
        return doc!.Validate();
    }

    private static string One(string root, string id = "a") => "{\"screens\":[{\"id\":\"" + id + "\",\"name\":\"A\",\"root\":" + root + "}]}";

    [Fact]
    public void RoundTrip_PreservesStructureAndProps()
    {
        var doc = ScreenDocument.TryParse(Good, out _)!;
        Assert.Empty(doc.Validate());
        var again = ScreenDocument.TryParse(doc.ToJson(), out var err);
        Assert.Null(err);
        Assert.Empty(again!.Validate());
        Assert.Equal(doc.ToJson(), again.ToJson());
        Assert.Equal(3, again.Screens[0].Root.Fill!.Children!.Count);
        Assert.Equal("Big", again.Screens[0].Root.Top!.Props!["font"].GetString());
    }

    [Fact]
    public void MalformedJson_ReturnsError()
    {
        Assert.Null(ScreenDocument.TryParse("{nope", out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void UnknownType() =>
        Assert.Contains(Check(One("""{"type":"blob"}""")), e => e.Path == "screens[0].root.type");

    [Fact]
    public void UnknownProp() =>
        Assert.Contains(Check(One("""{"type":"label","wat":1}""")), e => e.Path == "screens[0].root.wat");

    [Fact]
    public void WrongKinds()
    {
        var errs = Check(One("""{"type":"label","width":"x","visible":3,"font":"Huge","halign":"up","text":[1]}"""));
        foreach (var p in new[] { "width", "visible", "font", "halign", "text" })
            Assert.Contains(errs, e => e.Path == $"screens[0].root.{p}");
    }

    [Fact]
    public void BadColor() =>
        Assert.Contains(Check(One("""{"type":"dock","fill":{"type":"stack","children":[{"type":"label"},{"type":"label"},{"type":"label","color":"red"}]}}""")),
            e => e.Path == "screens[0].root.fill.children[2].color");

    [Theory]
    [InlineData("{nope}")]
    [InlineData("{weather.wind}")]
    [InlineData("{tube.}")]
    [InlineData("{ha:light}")]
    [InlineData("{time")]
    public void BadBindingTemplates(string text) =>
        Assert.Contains(Check(One("{\"type\":\"label\",\"text\":\"" + text + "\"}")), e => e.Path == "screens[0].root.text");

    [Fact]
    public void BadBindingObject()
    {
        Assert.Contains(Check(One("""{"type":"label","text":{"bind":"nope"}}""")), e => e.Path == "screens[0].root.text.bind");
        Assert.Contains(Check(One("""{"type":"label","text":{"bind":"time","x":1}}""")), e => e.Path == "screens[0].root.text");
    }

    [Fact]
    public void BindingKeyTryParse()
    {
        Assert.True(BindingKey.TryParse("tube.northern", out var k, out _));
        Assert.Equal(BindingKind.Tube, k.Kind);
        Assert.True(BindingKey.TryParse("ha:light.kitchen", out k, out _));
        Assert.Equal("light.kitchen", k.Name);
        Assert.True(BindingKey.TryParse("bin_day", out _, out _));
        Assert.False(BindingKey.TryParse("", out _, out _));
    }

    [Fact]
    public void BadSlots()
    {
        Assert.Contains(Check(One("""{"type":"label","fill":{"type":"label"}}""")), e => e.Path == "screens[0].root.fill");
        Assert.Contains(Check(One("""{"type":"dock","children":[{"type":"label"}]}""")), e => e.Path == "screens[0].root.children");
        Assert.Contains(Check(One("""{"type":"list"}""")), e => e.Path == "screens[0].root.item");
    }

    [Fact]
    public void DuplicateAndInvalidIds()
    {
        var dup = ScreenDocument.TryParse("""{"screens":[{"id":"a","name":"A","root":{"type":"label"}},{"id":"a","name":"B","root":{"type":"label"}}]}""", out _)!;
        Assert.Contains(dup.Validate(), e => e.Path == "screens[1].id" && e.Message.Contains("duplicated"));
        Assert.Contains(Check(One("""{"type":"label"}""", "Bad Id")), e => e.Path == "screens[0].id");
    }

    private static string Nest(int levels) =>
        levels == 1 ? "{\"type\":\"label\"}" : "{\"type\":\"stack\",\"children\":[" + Nest(levels - 1) + "]}";

    [Fact]
    public void DepthLimit()
    {
        Assert.Empty(Check(One(Nest(8))));
        Assert.Contains(Check(One(Nest(9))), e => e.Message.Contains("deeper"));
    }

    private static string Stack(int n) =>
        "{\"type\":\"stack\",\"children\":[" + string.Join(",", Enumerable.Repeat("{\"type\":\"label\"}", n)) + "]}";

    [Fact]
    public void NodeCountLimit()
    {
        Assert.Empty(Check(One(Stack(199))));
        Assert.Contains(Check(One(Stack(200))), e => e.Message.Contains("more than 200"));
    }

    [Fact]
    public void WriteAtomic_WritesAndReplaces()
    {
        var dir = Path.Combine(Path.GetTempPath(), "screens-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "screens.json");
        try
        {
            ScreenDocument.WriteAtomic(path, "one");
            ScreenDocument.WriteAtomic(path, "two");
            Assert.Equal("two", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Schema_CoversAllTypes()
    {
        foreach (var t in new[] { "stack", "grid", "dock", "panel", "label", "marquee", "clock", "progress", "pill", "divider", "sparkline", "bar_chart", "rolling_number", "pager", "list" })
            Assert.True(ScreenSchema.Types.ContainsKey(t), t);
    }
}
