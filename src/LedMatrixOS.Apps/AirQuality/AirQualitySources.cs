using System.Globalization;
using System.Text.Json;
using LedMatrixOS.Apps.Weather;

namespace LedMatrixOS.Apps.AirQuality;

/// <summary>Real data from the Open-Meteo air quality API (free, no key): one geocoding call per place (cached) and one call per poll.</summary>
public sealed class OpenMeteoAirQualitySource(HttpClient http) : IAirQualitySource
{
    public const int HourlyCount = 24;

    private readonly PlaceResolver _places = new(http);

    public async Task<AirQualitySnapshot> GetAsync(AirQualityQuery query, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var place = await _places.ResolveAsync(query.Location, timeout.Token);
        var json = await http.GetStringAsync(Url(place.Lat, place.Lon), timeout.Token);
        return Parse(json, place.Name);
    }

    public static string Url(double lat, double lon) =>
        "https://air-quality-api.open-meteo.com/v1/air-quality" +
        $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
        "&current=european_aqi,us_aqi,pm2_5,pm10,ozone,nitrogen_dioxide,uv_index,alder_pollen,birch_pollen,grass_pollen" +
        "&hourly=european_aqi,us_aqi&timezone=auto&forecast_days=2";

    /// <summary>Turns an Open-Meteo air quality response into a snapshot. Times are the place's local times (timezone=auto).</summary>
    public static AirQualitySnapshot Parse(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cur = root.GetProperty("current");
        var now = DateTime.Parse(cur.GetProperty("time").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.None);
        var thisHour = now.Date.AddHours(now.Hour);

        var series = new List<float>(HourlyCount);
        List<float>? usSeries = null;
        if (root.TryGetProperty("hourly", out var hourly) && hourly.TryGetProperty("time", out var times))
        {
            if (hourly.TryGetProperty("european_aqi", out var values)) series = HourlySeries(times, values, thisHour);
            if (hourly.TryGetProperty("us_aqi", out var usValues)) usSeries = HourlySeries(times, usValues, thisHour);
        }

        double? aqi = Num(cur, "european_aqi") ?? (series.Count > 0 ? series[0] : null);
        if (aqi is null) throw new FormatException("Air quality has no AQI");
        if (series.Count == 0) series.Add((float)aqi.Value);
        int? us = Num(cur, "us_aqi") is { } u ? (int)Math.Round(u) : null;
        if (usSeries is { Count: 0 }) usSeries = null;

        return new AirQualitySnapshot(name, (int)Math.Round(aqi.Value), Num(cur, "pm2_5") ?? 0, Num(cur, "pm10") ?? 0,
            Num(cur, "ozone") ?? 0, Num(cur, "nitrogen_dioxide") ?? 0, Num(cur, "uv_index") ?? 0,
            Num(cur, "alder_pollen"), Num(cur, "birch_pollen"), Num(cur, "grass_pollen"), series.ToArray(), us, usSeries?.ToArray());
    }

    private static List<float> HourlySeries(JsonElement times, JsonElement values, DateTime thisHour)
    {
        var series = new List<float>(HourlyCount);
        for (int i = 0; i < times.GetArrayLength() && i < values.GetArrayLength() && series.Count < HourlyCount; i++)
        {
            if (DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.None) < thisHour) continue;
            if (values[i].ValueKind == JsonValueKind.Number) series.Add((float)values[i].GetDouble());
            else if (series.Count > 0) series.Add(series[^1]);
        }
        return series;
    }

    private static double? Num(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

/// <summary>Deterministic stand-in: the same input always gives the same reading. Used by tests and for offline demos (config AirQuality:Source = Fake).</summary>
public sealed class FakeAirQualitySource(int aqi = 14, double pm25 = 4.2, double pm10 = 9.1, double uv = 5.2, string location = "London",
    bool pollen = true, Func<bool>? fail = null) : IAirQualitySource
{
    public Task<AirQualitySnapshot> GetAsync(AirQualityQuery query, CancellationToken ct)
    {
        if (fail?.Invoke() == true) return Task.FromException<AirQualitySnapshot>(new HttpRequestException("offline"));

        var hourly = new float[OpenMeteoAirQualitySource.HourlyCount];
        for (int i = 0; i < hourly.Length; i++)
            hourly[i] = (float)Math.Max(1, aqi + aqi * 0.35 * Math.Sin(i / 3.5) + (i > 14 ? (i - 14) * aqi * 0.03 : 0));

        return Task.FromResult(new AirQualitySnapshot(location, aqi, pm25, pm10, 58, 21, uv,
            pollen ? 3 : null, pollen ? 42 : null, pollen ? 120 : null, hourly));
    }
}
