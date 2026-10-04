namespace LedMatrixOS.Core.Screens;

/// <summary>Describes <see cref="ScreenSchema"/> and the binding keys as plain data, so an editor needs no hard-coded knowledge.</summary>
public static class ScreenSchemaInfo
{
    private static object Prop(PropSchema p) => new { name = p.Name, kind = p.Kind.ToString().ToLowerInvariant(), options = p.Options };

    public static object Build() => new
    {
        maxDepth = ScreenSchema.MaxDepth,
        maxNodes = ScreenSchema.MaxNodes,
        fonts = ScreenSchema.Fonts,
        slots = ScreenSchema.AllSlots,
        commonProps = ScreenSchema.CommonProps.Select(Prop),
        nodeTypes = ScreenSchema.Types.Values.Select(t => new { type = t.Type, props = t.Props.Select(Prop), slots = t.Slots }),
        bindings = new
        {
            templateSyntax = "In a binding prop use a string with {key} templates ({{ and }} are literal braces), or an object {\"bind\":\"key\"}.",
            keys = new object[]
            {
                new { key = "time", kind = "time", description = "Current time", insideListOnly = false },
                new { key = "weather.<field>", kind = "weather", description = "Weather value", fields = BindingKey.WeatherFields, insideListOnly = false },
                new { key = "tube.<line-id>", kind = "tube", description = "Tube line status, e.g. tube.victoria", insideListOnly = false },
                new { key = "ha:<entity_id>", kind = "homeassistant", description = "Home Assistant entity state, e.g. ha:sensor.lounge_temp", insideListOnly = false },
                new { key = "bin_day", kind = "binday", description = "Next bin collection", insideListOnly = false },
                new { key = "item", kind = "item", description = "Value of the current list item", insideListOnly = true },
                new { key = "item.label", kind = "item", description = "Label of the current list item", insideListOnly = true },
            },
            listSourceSyntax = "A list source is 'Label|key;Label|key;...' or a comma separated list of values.",
        },
    };
}
