using System.Globalization;
using System.Text.Json;

namespace LedMatrixOS.Apps.Weather;

/// <summary>Real data from Open-Meteo (free, no key): one geocoding call per place (cached) and one forecast call per poll.</summary>
public sealed class OpenMeteoWeatherSource(HttpClient http) : IWeatherSource
{
    private readonly Dictionary<string, (string Name, double Lat, double Lon)> _places = new(StringComparer.OrdinalIgnoreCase);

    public async Task<WeatherSnapshot> GetAsync(WeatherQuery query, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var place = await ResolveAsync(query.Location, timeout.Token);
        var json = await http.GetStringAsync(ForecastUrl(place.Lat, place.Lon, query.Fahrenheit), timeout.Token);
        return Parse(json, place.Name, query.Fahrenheit);
    }

    public static string ForecastUrl(double lat, double lon, bool fahrenheit) =>
        "https://api.open-meteo.com/v1/forecast" +
        $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
        "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m,is_day" +
        "&hourly=temperature_2m,weather_code,precipitation_probability,is_day" +
        "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max" +
        "&timezone=auto&forecast_days=4" +
        (fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "&wind_speed_unit=kmh");

    private async Task<(string Name, double Lat, double Lon)> ResolveAsync(string location, CancellationToken ct)
    {
        location = location.Trim();
        if (_places.TryGetValue(location, out var cached)) return cached;

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

    /// <summary>Turns an Open-Meteo forecast response into a snapshot. Times are the place's local times (timezone=auto).</summary>
    public static WeatherSnapshot Parse(string json, string name, bool fahrenheit)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var offset = TimeSpan.FromSeconds(root.TryGetProperty("utc_offset_seconds", out var o) ? o.GetInt32() : 0);
        var cur = root.GetProperty("current");
        var nowLocal = ParseLocal(cur.GetProperty("time").GetString()!);

        var hourly = root.GetProperty("hourly");
        var times = hourly.GetProperty("time");
        var hours = new List<HourlyPoint>();
        for (int i = 0; i < times.GetArrayLength() && hours.Count < 13; i++)
        {
            var t = ParseLocal(times[i].GetString()!);
            if (t < nowLocal.Date.AddHours(nowLocal.Hour)) continue;
            hours.Add(new HourlyPoint(t, hourly.GetProperty("temperature_2m")[i].GetDouble(),
                IntAt(hourly, "weather_code", i), IntAt(hourly, "is_day", i) == 1, IntAt(hourly, "precipitation_probability", i)));
        }

        var daily = root.GetProperty("daily");
        var days = new List<DailyPoint>();
        for (int i = 0; i < daily.GetProperty("time").GetArrayLength(); i++)
            days.Add(new DailyPoint(ParseLocal(daily.GetProperty("time")[i].GetString()!), daily.GetProperty("temperature_2m_max")[i].GetDouble(),
                daily.GetProperty("temperature_2m_min")[i].GetDouble(), IntAt(daily, "weather_code", i), IntAt(daily, "precipitation_probability_max", i)));
        if (days.Count == 0) throw new FormatException("Forecast has no daily data");

        var today = days[0];
        return new WeatherSnapshot(name, new DateTimeOffset(nowLocal, offset),
            cur.GetProperty("temperature_2m").GetDouble(), cur.GetProperty("apparent_temperature").GetDouble(),
            cur.GetProperty("weather_code").GetInt32(), cur.GetProperty("is_day").GetInt32() == 1,
            cur.GetProperty("wind_speed_10m").GetDouble(), (int)Math.Round(cur.GetProperty("wind_direction_10m").GetDouble()),
            today.PrecipChance, today.High, today.Low,
            ParseLocal(daily.GetProperty("sunrise")[0].GetString()!).TimeOfDay, ParseLocal(daily.GetProperty("sunset")[0].GetString()!).TimeOfDay,
            fahrenheit, hours, days);
    }

    private static DateTime ParseLocal(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static int IntAt(JsonElement obj, string prop, int i) =>
        obj.TryGetProperty(prop, out var a) && i < a.GetArrayLength() && a[i].ValueKind == JsonValueKind.Number ? (int)Math.Round(a[i].GetDouble()) : 0;
}

/// <summary>Deterministic stand-in: the same input always gives the same forecast. Used by tests and for offline demos (config Weather:Source = Fake).</summary>
public sealed class FakeWeatherSource(int code = 0, bool isDay = true, double tempC = 21, string location = "London", Func<bool>? fail = null) : IWeatherSource
{
    public static readonly DateTime Epoch = new(2026, 1, 2, 13, 0, 0);

    public Task<WeatherSnapshot> GetAsync(WeatherQuery query, CancellationToken ct)
    {
        if (fail?.Invoke() == true) return Task.FromException<WeatherSnapshot>(new HttpRequestException("offline"));

        double Conv(double c) => query.Fahrenheit ? c * 9 / 5 + 32 : c;
        var hours = new List<HourlyPoint>();
        for (int i = 0; i < 13; i++)
        {
            var t = Epoch.AddHours(i);
            int hc = i % 5 == 4 ? 61 : i % 3 == 2 ? 3 : i > 8 ? 2 : code;
            hours.Add(new HourlyPoint(t, Conv(tempC + 3 * Math.Sin(i / 3.0)), hc, t.Hour is >= 7 and < 19, i % 5 == 4 ? 60 : i * 3 % 40));
        }

        int[] dayCodes = [code, 61, 2, 71];
        var days = new List<DailyPoint>();
        for (int d = 0; d < 4; d++)
            days.Add(new DailyPoint(Epoch.Date.AddDays(d), Conv(tempC + 3 - d), Conv(tempC - 6 - d), dayCodes[d], d * 25 % 90));

        return Task.FromResult(new WeatherSnapshot(location, new DateTimeOffset(Epoch, TimeSpan.Zero), Conv(tempC), Conv(tempC - 2), code, isDay,
            query.Fahrenheit ? 9 : 14, 315, 40, days[0].High, days[0].Low, new TimeSpan(7, 52, 0), new TimeSpan(16, 42, 0), query.Fahrenheit, hours, days));
    }
}
