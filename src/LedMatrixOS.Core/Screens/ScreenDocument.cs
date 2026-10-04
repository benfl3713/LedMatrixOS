using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LedMatrixOS.Core.Screens;

/// <summary>A validation problem with a JSON-path-like location, e.g. <c>screens[0].root.fill.children[2].color</c>.</summary>
public sealed record ScreenError(string Path, string Message);

public sealed class ScreenDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ScreenNode Root { get; set; } = new();
}

/// <summary>Wire/file shape of screens.json.</summary>
public sealed class ScreenDocument
{
    public List<ScreenDefinition> Screens { get; set; } = new();

    private static readonly Regex Slug = new("^[a-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex HexColor = new("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Parses a document; returns null and an error message when the JSON is malformed.</summary>
    public static ScreenDocument? TryParse(string json, out string? error)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<ScreenDocument>(json, Options);
            if (doc == null) { error = "Body must be a JSON object"; return null; }
            doc.Screens ??= new();
            error = null;
            return doc;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Validates the document; empty list means valid.</summary>
    public List<ScreenError> Validate()
    {
        var errors = new List<ScreenError>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Screens.Count; i++)
        {
            var s = Screens[i];
            var where = $"screens[{i}]";
            if (s == null) { errors.Add(new(where, "screen is null")); continue; }
            if (string.IsNullOrEmpty(s.Id)) errors.Add(new($"{where}.id", "id is required"));
            else if (!Slug.IsMatch(s.Id)) errors.Add(new($"{where}.id", $"id '{s.Id}' must match [a-z0-9-_]+"));
            else if (!ids.Add(s.Id)) errors.Add(new($"{where}.id", $"id '{s.Id}' is duplicated"));
            if (string.IsNullOrWhiteSpace(s.Name)) errors.Add(new($"{where}.name", "name is required"));

            if (s.Root == null) { errors.Add(new($"{where}.root", "root is required")); continue; }
            int count = 0;
            CountNodes(s.Root, 1, ref count);
            if (count > ScreenSchema.MaxNodes)
                errors.Add(new($"{where}.root", $"screen has more than {ScreenSchema.MaxNodes} nodes"));
            ValidateNode(s.Root, $"{where}.root", 1, errors);
        }
        return errors;
    }

    private static IEnumerable<ScreenNode?> ChildNodes(ScreenNode n)
    {
        if (n.Children != null) foreach (var c in n.Children) yield return c;
        foreach (var (_, node) in n.SingleSlots()) yield return node;
    }

    private static void CountNodes(ScreenNode n, int depth, ref int count)
    {
        count++;
        if (count > ScreenSchema.MaxNodes || depth > ScreenSchema.MaxDepth) return;
        foreach (var c in ChildNodes(n))
        {
            if (c != null) CountNodes(c, depth + 1, ref count);
            if (count > ScreenSchema.MaxNodes) return;
        }
    }

    private static void ValidateNode(ScreenNode node, string path, int depth, List<ScreenError> errors)
    {
        if (depth > ScreenSchema.MaxDepth)
        {
            errors.Add(new(path, $"nesting is deeper than {ScreenSchema.MaxDepth} levels"));
            return;
        }

        if (string.IsNullOrEmpty(node.Type)) { errors.Add(new($"{path}.type", "type is required")); return; }
        if (!ScreenSchema.Types.TryGetValue(node.Type, out var schema))
        {
            errors.Add(new($"{path}.type", $"unknown type '{node.Type}'"));
            return;
        }

        if (node.Props != null)
        {
            foreach (var (name, value) in node.Props)
            {
                var prop = ScreenSchema.FindProp(node.Type, name);
                if (prop == null) { errors.Add(new($"{path}.{name}", $"unknown property '{name}' for type '{node.Type}'")); continue; }
                ValidateProp(prop, value, $"{path}.{name}", errors);
            }
        }

        if (node.Children is { Count: > 0 } && !schema.Slots.Contains("children"))
            errors.Add(new($"{path}.children", $"type '{node.Type}' does not accept children"));
        foreach (var (slot, _) in node.SingleSlots())
            if (!schema.Slots.Contains(slot))
                errors.Add(new($"{path}.{slot}", $"type '{node.Type}' does not have a '{slot}' slot"));
        if (node.Type == "list" && node.Item == null)
            errors.Add(new($"{path}.item", "list requires an item template"));

        if (node.Children != null && schema.Slots.Contains("children"))
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                var c = node.Children[i];
                if (c == null) errors.Add(new($"{path}.children[{i}]", "child is null"));
                else ValidateNode(c, $"{path}.children[{i}]", depth + 1, errors);
            }
        }
        foreach (var (slot, child) in node.SingleSlots())
            if (child != null && schema.Slots.Contains(slot))
                ValidateNode(child, $"{path}.{slot}", depth + 1, errors);
    }

    private static void ValidateProp(PropSchema prop, JsonElement v, string path, List<ScreenError> errors)
    {
        switch (prop.Kind)
        {
            case PropKind.Int:
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out _))
                    errors.Add(new(path, "must be an integer"));
                break;
            case PropKind.Bool:
                if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    errors.Add(new(path, "must be true or false"));
                break;
            case PropKind.String:
                if (v.ValueKind != JsonValueKind.String) errors.Add(new(path, "must be a string"));
                break;
            case PropKind.Color:
                if (v.ValueKind != JsonValueKind.String || !HexColor.IsMatch(v.GetString()!))
                    errors.Add(new(path, "must be a hex color (#RGB, #RRGGBB or #RRGGBBAA)"));
                break;
            case PropKind.Enum:
                if (v.ValueKind != JsonValueKind.String || !prop.Options!.Contains(v.GetString()!, StringComparer.OrdinalIgnoreCase))
                    errors.Add(new(path, $"must be one of: {string.Join(", ", prop.Options!)}"));
                break;
            case PropKind.Binding:
                if (v.ValueKind == JsonValueKind.String)
                {
                    if (!BindingKey.TryParseTemplate(v.GetString()!, out _, out var terr)) errors.Add(new(path, terr!));
                }
                else if (v.ValueKind == JsonValueKind.Object)
                {
                    int n = 0;
                    string? key = null;
                    bool bad = false;
                    foreach (var p in v.EnumerateObject())
                    {
                        n++;
                        if (p.Name == "bind" && p.Value.ValueKind == JsonValueKind.String) key = p.Value.GetString();
                        else bad = true;
                    }
                    if (bad || n != 1 || key == null) errors.Add(new(path, "binding object must be exactly {\"bind\":\"key\"}"));
                    else if (!BindingKey.TryParse(key, out _, out var kerr)) errors.Add(new($"{path}.bind", kerr!));
                }
                else if (v.ValueKind != JsonValueKind.Number)
                {
                    errors.Add(new(path, "must be a string, number, or {\"bind\":\"key\"} object"));
                }
                break;
        }
    }

    /// <summary>Writes the file atomically: temp file in the same directory, then replace.</summary>
    public static void WriteAtomic(string path, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
