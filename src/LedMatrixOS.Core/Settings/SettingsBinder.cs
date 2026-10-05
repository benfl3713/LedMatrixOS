using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace LedMatrixOS.Core.Settings;

/// <summary>
/// Builds <see cref="AppSetting"/>s from [Setting] properties and applies updates with lenient coercion.
/// Supported property types: bool, int and string.
/// </summary>
public static class SettingsBinder
{
    private sealed record Entry(string Key, PropertyInfo Property, SettingAttribute Attribute, AppSettingType Type);

    private static readonly ConcurrentDictionary<Type, Entry[]> Cache = new();

    // The property values of a freshly constructed instance, per app type (what "reset to default" goes back to).
    private static readonly ConcurrentDictionary<Type, Dictionary<string, object>> Defaults = new();

    /// <summary>Remembers the current values of <paramref name="target"/> as its type's defaults. Call on a new instance; later calls for the type are ignored.</summary>
    public static void CaptureDefaults(object target) => Defaults.GetOrAdd(target.GetType(), _ =>
        Cache.GetOrAdd(target.GetType(), Discover).ToDictionary(e => e.Key, e => e.Property.GetValue(target) ?? ""));

    public static IEnumerable<AppSetting> GetSettings(object target)
    {
        Defaults.TryGetValue(target.GetType(), out var defaults);
        foreach (var e in Cache.GetOrAdd(target.GetType(), Discover))
        {
            var current = e.Property.GetValue(target) ?? "";
            var fallback = defaults != null && defaults.TryGetValue(e.Key, out var d) ? d : current;
            yield return new AppSetting(
                e.Key, e.Attribute.Name, e.Attribute.Description, e.Type,
                fallback, current, e.Attribute.Min, e.Attribute.Max, e.Attribute.Options, Browse: e.Attribute.Browse, Advanced: e.Attribute.Advanced, Editor: e.Attribute.Editor);
        }
    }

    /// <summary>
    /// Applies a value to the setting with the given key (case-insensitive). Returns false if no such setting exists;
    /// <paramref name="canonicalKey"/> is the setting's own camelCase key however the caller spelled it.
    /// </summary>
    public static bool TryUpdate(object target, string key, object value, out string canonicalKey)
    {
        canonicalKey = key;
        var entry = Cache.GetOrAdd(target.GetType(), Discover)
            .FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return TryUpdateLegacy(target, key, value, out canonicalKey);
        canonicalKey = entry.Key;

        var property = entry.Property;
        var current = property.GetValue(target);
        switch (entry.Type)
        {
            case AppSettingType.Boolean:
                property.SetValue(target, CoerceBool(value, (bool)current!));
                break;
            case AppSettingType.Integer:
                var n = CoerceInt(value, (int)current!);
                if (entry.Attribute.Min is int min) n = Math.Max(n, min);
                if (entry.Attribute.Max is int max) n = Math.Min(n, max);
                property.SetValue(target, n);
                break;
            case AppSettingType.MultiSearch:
                var kept = SplitIds(CoerceIds(value, (string?)current ?? ""));
                if (entry.Attribute.Max is int maxItems && maxItems > 0) kept = kept.Take(maxItems).ToArray();
                property.SetValue(target, string.Join(",", kept));
                break;
            default:
                var s = CoerceString(value, (string?)current ?? "");
                if (entry.Type == AppSettingType.Search) s = s.Trim();
                // Ignore values that are not one of the allowed options
                if (entry.Attribute.Options is { } options && !options.Contains(s)) return true;
                property.SetValue(target, s);
                break;
        }

        return true;
    }

    /// <summary>True when <paramref name="key"/> is a retired setting key the target declares with <see cref="LegacySettingKeyAttribute"/>.</summary>
    public static bool IsLegacyKey(object target, string key) => FindLegacy(target, key) != null;

    private static LegacySettingKeyAttribute? FindLegacy(object target, string key) => target.GetType()
        .GetCustomAttributes<LegacySettingKeyAttribute>(inherit: true)
        .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));

    private static bool TryUpdateLegacy(object target, string key, object value, out string canonicalKey)
    {
        canonicalKey = key;
        if (FindLegacy(target, key) is not { } legacy) return false;
        if (legacy.MapsTo == null) return true; // accepted, nothing to apply

        var entry = Cache.GetOrAdd(target.GetType(), Discover).FirstOrDefault(e => e.Key == legacy.MapsTo);
        if (entry == null) return true;
        canonicalKey = entry.Key;

        // Old selects held "id | name"; anything else (the empty value that used to be persisted) selected nothing.
        var picked = CoerceString(value, "");
        var split = picked.IndexOf(" | ", StringComparison.Ordinal);
        if (split <= 0 || picked[..split].Trim() is not { Length: > 0 } id) return true;

        if (entry.Type == AppSettingType.MultiSearch)
        {
            var ids = SplitIds((string?)entry.Property.GetValue(target)).ToList();
            if (ids.Contains(id, StringComparer.OrdinalIgnoreCase)) return true;
            ids.Add(id);
            if (entry.Attribute.Max is int max && max > 0) while (ids.Count > max) ids.RemoveAt(0); // the oldest pick makes room
            entry.Property.SetValue(target, string.Join(",", ids));
        }
        else
        {
            entry.Property.SetValue(target, id);
        }

        return true;
    }

    /// <summary>The ids of a comma separated MultiSearch value: trimmed, without blanks or duplicates.</summary>
    public static string[] SplitIds(string? value) => (value ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    // A list may arrive as a comma separated string, a JSON array string or a JSON array.
    private static string CoerceIds(object value, string fallback)
    {
        try
        {
            if (value is JsonElement { ValueKind: JsonValueKind.Array } array)
                return string.Join(",", array.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()));
            if (value is IEnumerable<object> list && value is not string) return string.Join(",", list.Select(o => o.ToString()));
            var text = CoerceString(value, fallback).Trim();
            if (text.StartsWith('[') && text.EndsWith(']'))
            {
                using var doc = JsonDocument.Parse(text);
                return string.Join(",", doc.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()));
            }
            return text;
        }
        catch
        {
            return fallback;
        }
    }

    private static Entry[] Discover(Type type)
    {
        var entries = new List<Entry>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = p.GetCustomAttribute<SettingAttribute>();
            if (attr == null) continue;
            if (!p.CanRead || !p.CanWrite)
                throw new InvalidOperationException($"Setting property {type.Name}.{p.Name} must have a public getter and setter");

            var settingType = p.PropertyType == typeof(bool) ? AppSettingType.Boolean
                : p.PropertyType == typeof(int) ? AppSettingType.Integer
                : p.PropertyType == typeof(string) ? (attr.Options != null ? AppSettingType.Select
                    : attr.MultiSearch ? AppSettingType.MultiSearch : attr.Search ? AppSettingType.Search : AppSettingType.String)
                : throw new InvalidOperationException($"Setting property {type.Name}.{p.Name} has unsupported type {p.PropertyType.Name}");

            entries.Add(new Entry(char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p, attr, settingType));
        }

        return entries.ToArray();
    }

    public static bool CoerceBool(object value, bool fallback)
    {
        try
        {
            if (value is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.True) return true;
                if (json.ValueKind == JsonValueKind.False) return false;
                if (json.ValueKind == JsonValueKind.String && bool.TryParse(json.GetString(), out var parsed)) return parsed;
                return fallback;
            }

            if (value is bool b) return b;
            if (value is string s) return bool.TryParse(s, out var fromString) ? fromString : fallback;

            return Convert.ToBoolean(value);
        }
        catch
        {
            return fallback;
        }
    }

    public static int CoerceInt(object value, int fallback)
    {
        try
        {
            if (value is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var n)) return n;
                if (json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), out var parsed)) return parsed;
                return fallback;
            }

            if (value is int i) return i;
            if (value is long l) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
            if (value is string s && int.TryParse(s, out var fromString)) return fromString;

            return Convert.ToInt32(value);
        }
        catch
        {
            return fallback;
        }
    }

    public static string CoerceString(object value, string fallback)
    {
        if (value is JsonElement json)
        {
            return json.ValueKind == JsonValueKind.String ? json.GetString() ?? fallback : json.ToString();
        }

        return value.ToString() ?? fallback;
    }
}
