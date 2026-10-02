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

    public static IEnumerable<AppSetting> GetSettings(object target)
    {
        foreach (var e in Cache.GetOrAdd(target.GetType(), Discover))
        {
            var current = e.Property.GetValue(target) ?? "";
            yield return new AppSetting(
                e.Key, e.Attribute.Name, e.Attribute.Description, e.Type,
                current, current, e.Attribute.Min, e.Attribute.Max, e.Attribute.Options);
        }
    }

    /// <summary>Applies a value to the setting with the given key. Returns false if no such setting exists.</summary>
    public static bool TryUpdate(object target, string key, object value)
    {
        var entry = Cache.GetOrAdd(target.GetType(), Discover)
            .FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return false;

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
            default:
                var s = CoerceString(value, (string?)current ?? "");
                // Ignore values that are not one of the allowed options
                if (entry.Attribute.Options is { } options && !options.Contains(s)) return true;
                property.SetValue(target, s);
                break;
        }

        return true;
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
                : p.PropertyType == typeof(string) ? (attr.Options != null ? AppSettingType.Select : AppSettingType.String)
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
