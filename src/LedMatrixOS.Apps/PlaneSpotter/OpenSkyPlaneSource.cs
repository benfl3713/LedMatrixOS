using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LedMatrixOS.Apps.PlaneSpotter;

/// <summary>
/// Aircraft from the keyless OpenSky Network REST API. Anonymous access is rate limited, so on HTTP 429 the source backs off
/// (honouring Retry-After, otherwise doubling from 60s) and reports <see cref="PlaneStatus.Busy"/> without calling the API until the back-off ends.
/// Every failure becomes a status; nothing is thrown (except cancellation).
/// </summary>
public sealed class OpenSkyPlaneSource(HttpClient http, TimeProvider? time = null) : IPlaneSource
{
    public static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
    private TimeSpan _backoff = MinBackoff;

    public async Task<PlaneSnapshot> GetAsync(PlaneQuery query, CancellationToken ct)
    {
        if (_time.GetUtcNow() < _blockedUntil) return PlaneSnapshot.Failed(PlaneStatus.Busy, query);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(Url(query), timeout.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.TryGetValues("X-Rate-Limit-Retry-After-Seconds", out var v) && double.TryParse(v.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
                        ? TimeSpan.FromSeconds(s) : _backoff);
                wait = wait < MinBackoff ? MinBackoff : wait > MaxBackoff ? MaxBackoff : wait;
                _blockedUntil = _time.GetUtcNow() + wait;
                _backoff = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, _backoff.Ticks * 2));
                return PlaneSnapshot.Failed(PlaneStatus.Busy, query);
            }

            if (!response.IsSuccessStatusCode) return PlaneSnapshot.Failed(PlaneStatus.Offline, query);

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var aircraft = Parse(json);
            _backoff = MinBackoff;
            return new PlaneSnapshot(PlaneStatus.Ok, aircraft, query.Latitude, query.Longitude);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return PlaneSnapshot.Failed(PlaneStatus.Offline, query);
        }
    }

    public static string Url(PlaneQuery q)
    {
        var (minLat, minLon, maxLat, maxLon) = PlaneMath.BoundingBox(q.Latitude, q.Longitude, q.RadiusKm);
        static string F(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
        return $"https://opensky-network.org/api/states/all?lamin={F(minLat)}&lomin={F(minLon)}&lamax={F(maxLat)}&lomax={F(maxLon)}";
    }

    /// <summary>Parses the <c>states</c> array (an array of arrays; every field may be null or missing). Rows without an icao24 are dropped.</summary>
    public static Aircraft[] Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("states", out var states) || states.ValueKind != JsonValueKind.Array) return [];

        var list = new List<Aircraft>(states.GetArrayLength());
        foreach (var s in states.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Array) continue;
            var icao = Str(s, 0).ToLowerInvariant();
            if (icao.Length == 0) continue;
            list.Add(new Aircraft(icao, Str(s, 1), Str(s, 2), Num(s, 6), Num(s, 5), Num(s, 7) ?? Num(s, 13), Num(s, 9), Num(s, 10), Num(s, 11),
                s.GetArrayLength() > 8 && s[8].ValueKind == JsonValueKind.True));
        }

        return list.ToArray();
    }

    private static string Str(JsonElement row, int i) =>
        i < row.GetArrayLength() && row[i].ValueKind == JsonValueKind.String ? (row[i].GetString() ?? "").Trim() : "";

    private static double? Num(JsonElement row, int i) =>
        i < row.GetArrayLength() && row[i].ValueKind == JsonValueKind.Number && row[i].TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
}

/// <summary>Resolves a Weather-style location (a place name, or "lat,lon") to coordinates with the keyless Open-Meteo geocoder. Results are cached.</summary>
public sealed class PlaceGeocoder(HttpClient http)
{
    private readonly Dictionary<string, (double Lat, double Lon)?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<(double Lat, double Lon)?> ResolveAsync(string place, CancellationToken ct)
    {
        place = place.Trim();
        if (place.Length == 0) return null;
        if (TryParseCoordinates(place, out var direct)) return direct;
        if (_cache.TryGetValue(place, out var cached)) return cached;

        var url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(place);
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return _cache[place] = null;
        var first = results[0];
        return _cache[place] = (first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble());
    }

    public static bool TryParseCoordinates(string text, out (double Lat, double Lon) value)
    {
        value = default;
        var parts = text.Split(',');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var la) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) && PlaneMath.IsValidCoordinate(la, lo))
        {
            value = (la, lo);
            return true;
        }
        return false;
    }
}

/// <summary>Test/demo source: answers with whatever the callback returns, with no network.</summary>
public sealed class FakePlaneSource(Func<PlaneQuery, PlaneSnapshot>? respond = null) : IPlaneSource
{
    public int Calls { get; private set; }

    public Task<PlaneSnapshot> GetAsync(PlaneQuery query, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(respond?.Invoke(query) ?? new PlaneSnapshot(PlaneStatus.Ok, [], query.Latitude, query.Longitude));
    }
}
