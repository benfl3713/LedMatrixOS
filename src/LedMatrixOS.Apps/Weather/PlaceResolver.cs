using System.Globalization;
using System.Text.Json;

namespace LedMatrixOS.Apps.Weather;

/// <summary>
/// Turns a location setting into coordinates for the Open-Meteo APIs: a place picked in the app ("London|51.5085,-0.1257") or a plain
/// "lat,lon" skips geocoding, anything else is looked up once and cached.
/// </summary>
public sealed class PlaceResolver(HttpClient http)
{
    private readonly Dictionary<string, (string Name, double Lat, double Lon)> _places = new(StringComparer.OrdinalIgnoreCase);

    public async Task<(string Name, double Lat, double Lon)> ResolveAsync(string location, CancellationToken ct)
    {
        location = location.Trim();
        if (_places.TryGetValue(location, out var cached)) return cached;

        // A place picked in the app ("London|51.5085,-0.1257") carries its coordinates.
        if (PlaneSpotter.PlaceGeocoder.TryParseEncoded(location, out var pickedName, out var picked))
            return _places[location] = (pickedName, picked.Lat, picked.Lon);

        // "51.5,-0.12" skips geocoding.
        var parts = location.Split(',');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var la) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
            return _places[location] = (location, la, lo);

        var url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(location);
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new LocationNotFoundException(location);
        var first = results[0];
        return _places[location] = (first.GetProperty("name").GetString() ?? location,
            first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble());
    }
}
