using System.Text.Json.Serialization;

namespace LedMatrixOS.Core.Screens;

public enum PropKind
{
    [JsonStringEnumMemberName("int")] Int,
    [JsonStringEnumMemberName("string")] String,
    [JsonStringEnumMemberName("bool")] Bool,
    [JsonStringEnumMemberName("color")] Color,
    [JsonStringEnumMemberName("enum")] Enum,
    /// <summary>A string with <c>{key}</c> templates, or an object <c>{"bind":"key"}</c>.</summary>
    [JsonStringEnumMemberName("binding")] Binding,
}

public sealed record PropSchema(string Name, PropKind Kind, IReadOnlyList<string>? Options = null);

public sealed record NodeTypeSchema(string Type, IReadOnlyList<PropSchema> Props, IReadOnlyList<string> Slots);

/// <summary>Single source of truth for which node types, props and child slots a screen may use.</summary>
public static class ScreenSchema
{
    public const int MaxDepth = 8;
    public const int MaxNodes = 200;

    public static readonly IReadOnlyList<string> Fonts = new[] { "Big", "Small", "QuiteSmall", "ExtraSmall" };
    public static readonly IReadOnlyList<string> AllSlots = new[] { "children", "top", "bottom", "left", "right", "fill", "item" };

    /// <summary>Props valid on every node type.</summary>
    public static readonly IReadOnlyList<PropSchema> CommonProps = new PropSchema[]
    {
        new("width", PropKind.Int),
        new("height", PropKind.Int),
        new("margin", PropKind.Int),
        new("padding", PropKind.Int),
        new("grow", PropKind.Int),
        new("halign", PropKind.Enum, new[] { "left", "center", "right" }),
        new("valign", PropKind.Enum, new[] { "top", "middle", "bottom" }),
        new("visible", PropKind.Bool),
    };

    private static PropSchema Font() => new("font", PropKind.Enum, Fonts);
    private static PropSchema Color(string name = "color") => new(name, PropKind.Color);
    private static PropSchema Text(string name = "text") => new(name, PropKind.Binding);
    private static PropSchema Shadow() => new("shadow", PropKind.Bool);
    private static PropSchema Int(string name) => new(name, PropKind.Int);

    private static NodeTypeSchema T(string type, PropSchema[] props, params string[] slots) => new(type, props, slots);

    public static readonly IReadOnlyDictionary<string, NodeTypeSchema> Types = new[]
    {
        T("stack", new[] { new PropSchema("direction", PropKind.Enum, new[] { "horizontal", "vertical" }), Int("gap") }, "children"),
        T("grid", new[] { Int("columns"), Int("gap") }, "children"),
        T("dock", new[] { Int("gap") }, "top", "bottom", "left", "right", "fill"),
        T("panel", new[] { Color("background"), Color("border"), Int("radius") }, "children"),
        T("label", new[] { Text(), Font(), Color(), Shadow() }),
        T("marquee", new[] { Text(), Font(), Color(), Shadow(), Int("speed") }),
        T("clock", new[] { new PropSchema("format", PropKind.String), Font(), Color(), Shadow() }),
        T("progress", new[] { new PropSchema("value", PropKind.Binding), Int("max"), Color(), Color("background") }),
        T("pill", new[] { Text(), Font(), Color(), Color("background"), Shadow() }),
        T("divider", new[] { Color(), Int("thickness") }),
        T("sparkline", new[] { new PropSchema("values", PropKind.Binding), Color() }),
        T("bar_chart", new[] { new PropSchema("values", PropKind.Binding), Color() }),
        T("rolling_number", new[] { new PropSchema("value", PropKind.Binding), Font(), Color(), Shadow() }),
        T("pager", new[] { Int("interval_ms") }, "children"),
        T("list", new[] { new PropSchema("source", PropKind.Binding), Int("max_items"), Int("gap") }, "item"),
    }.ToDictionary(t => t.Type, StringComparer.Ordinal);

    /// <summary>Finds a prop (type-specific first, then common) for a node type.</summary>
    public static PropSchema? FindProp(string type, string name)
    {
        if (Types.TryGetValue(type, out var t))
        {
            foreach (var p in t.Props) if (p.Name == name) return p;
        }
        foreach (var p in CommonProps) if (p.Name == name) return p;
        return null;
    }
}
