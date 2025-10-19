using System.Text.Json;

namespace LedMatrixOS.Core;

/// <summary>
/// Persists app settings to disk so they remain when switching between apps
/// </summary>
public sealed class AppSettingsStorage
{
    private readonly string _settingsFilePath;
    private readonly Dictionary<string, Dictionary<string, object>> _settingsCache = new();
    private readonly object _lock = new();
    
    public AppSettingsStorage(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
        LoadFromDisk();
    }
    
    /// <summary>
    /// Save settings for a specific app
    /// </summary>
    public void SaveAppSettings(string appId, Dictionary<string, object> settings)
    {
        lock (_lock)
        {
            _settingsCache[appId] = new Dictionary<string, object>(settings);
            PersistToDisk();
        }
    }
    
    /// <summary>
    /// Load settings for a specific app
    /// </summary>
    public Dictionary<string, object>? GetAppSettings(string appId)
    {
        lock (_lock)
        {
            return _settingsCache.TryGetValue(appId, out var settings) 
                ? new Dictionary<string, object>(settings) 
                : null;
        }
    }
    
    /// <summary>
    /// Update a single setting for an app
    /// </summary>
    public void UpdateAppSetting(string appId, string key, object value)
    {
        lock (_lock)
        {
            if (!_settingsCache.ContainsKey(appId))
            {
                _settingsCache[appId] = new Dictionary<string, object>();
            }
            
            _settingsCache[appId][key] = value;
            PersistToDisk();
        }
    }
    
    private void LoadFromDisk()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(json);
                
                if (data != null)
                {
                    foreach (var (appId, settings) in data)
                    {
                        _settingsCache[appId] = new Dictionary<string, object>();
                        foreach (var (key, value) in settings)
                        {
                            _settingsCache[appId][key] = ConvertJsonElement(value);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load app settings: {ex.Message}");
        }
    }
    
    private void PersistToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settingsCache, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to save app settings: {ex.Message}");
        }
    }
    
    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString()
        };
    }
}

