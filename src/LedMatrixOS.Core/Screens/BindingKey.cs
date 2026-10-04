using System.Text.RegularExpressions;

namespace LedMatrixOS.Core.Screens;

public enum BindingKind { Time, Weather, Tube, HomeAssistant, BinDay, Item }

/// <summary>
/// A data binding key a screen node can reference. Valid keys: <c>time</c>, <c>weather.temp|feels|high|low|precip</c>,
/// <c>tube.&lt;line-id&gt;</c>, <c>ha:&lt;entity_id&gt;</c>, <c>bin_day</c>, and (inside a list's item template) <c>item</c> and <c>item.label</c>.
/// In a string prop, keys are written as <c>{key}</c> templates (<c>{{</c> and <c>}}</c> are literal braces);
/// a prop may also be an object <c>{"bind":"key"}</c>.
/// </summary>
public readonly record struct BindingKey(BindingKind Kind, string Name)
{
    public static readonly IReadOnlyList<string> WeatherFields = new[] { "temp", "feels", "high", "low", "precip" };

    private static readonly Regex LineId = new("^[a-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex EntityId = new("^[a-z0-9_]+\\.[a-z0-9_]+$", RegexOptions.Compiled);

    public static bool TryParse(string? key, out BindingKey result, out string? error)
    {
        result = default;
        error = null;
        if (string.IsNullOrEmpty(key)) { error = "binding key is empty"; return false; }

        if (key == "time") { result = new(BindingKind.Time, "time"); return true; }
        if (key == "bin_day") { result = new(BindingKind.BinDay, "bin_day"); return true; }
        if (key is "item" or "item.label") { result = new(BindingKind.Item, key); return true; }

        if (key.StartsWith("weather.", StringComparison.Ordinal))
        {
            var field = key["weather.".Length..];
            if (WeatherFields.Contains(field)) { result = new(BindingKind.Weather, field); return true; }
            error = $"unknown weather field '{field}' (expected {string.Join(", ", WeatherFields)})";
            return false;
        }
        if (key.StartsWith("tube.", StringComparison.Ordinal))
        {
            var line = key["tube.".Length..];
            if (LineId.IsMatch(line)) { result = new(BindingKind.Tube, line); return true; }
            error = $"invalid tube line id '{line}'";
            return false;
        }
        if (key.StartsWith("ha:", StringComparison.Ordinal))
        {
            var entity = key["ha:".Length..];
            if (EntityId.IsMatch(entity)) { result = new(BindingKind.HomeAssistant, entity); return true; }
            error = $"invalid Home Assistant entity id '{entity}' (expected domain.object)";
            return false;
        }

        error = $"unknown binding key '{key}'";
        return false;
    }

    /// <summary>Extracts and validates every <c>{key}</c> in a template string. Returns false with the first problem.</summary>
    public static bool TryParseTemplate(string text, out List<BindingKey> keys, out string? error)
    {
        keys = new();
        error = null;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{') { i += 2; continue; }
                int end = text.IndexOf('}', i + 1);
                if (end < 0) { error = "unclosed '{' in template"; return false; }
                if (!TryParse(text.Substring(i + 1, end - i - 1), out var key, out error)) return false;
                keys.Add(key);
                i = end + 1;
            }
            else if (c == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}') { i += 2; continue; }
                error = "unmatched '}' in template (use '}}' for a literal brace)";
                return false;
            }
            else i++;
        }
        return true;
    }
}
