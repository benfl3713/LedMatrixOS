using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedMatrixOS.Core.Screens;

/// <summary>
/// One node of a declarative screen. On the wire props sit flat next to <c>type</c>:
/// <c>{"type":"label","text":"{time}","color":"#fff","font":"Big"}</c>. Everything that is not a known structural
/// member lands in <see cref="Props"/> (via JSON extension data) and is checked against <see cref="ScreenSchema"/>.
/// Children live in slots: <see cref="Children"/> (stack/grid/panel/pager), the named dock slots
/// <see cref="Top"/>/<see cref="Bottom"/>/<see cref="Left"/>/<see cref="Right"/>/<see cref="Fill"/>, and
/// <see cref="Item"/> (the per-row template of a list).
/// </summary>
public sealed class ScreenNode
{
    public string Type { get; set; } = "";

    public List<ScreenNode>? Children { get; set; }
    public ScreenNode? Top { get; set; }
    public ScreenNode? Bottom { get; set; }
    public ScreenNode? Left { get; set; }
    public ScreenNode? Right { get; set; }
    public ScreenNode? Fill { get; set; }
    public ScreenNode? Item { get; set; }

    /// <summary>Free property bag: width, color, text, ... (flat on the wire).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Props { get; set; }

    /// <summary>Named single-node slots that are set, as (slot name, node) pairs.</summary>
    internal IEnumerable<(string Slot, ScreenNode? Node)> SingleSlots()
    {
        if (Top != null) yield return ("top", Top);
        if (Bottom != null) yield return ("bottom", Bottom);
        if (Left != null) yield return ("left", Left);
        if (Right != null) yield return ("right", Right);
        if (Fill != null) yield return ("fill", Fill);
        if (Item != null) yield return ("item", Item);
    }
}
