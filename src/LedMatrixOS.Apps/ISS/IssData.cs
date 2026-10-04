using System.Globalization;
using System.Text.Json;
using LedMatrixOS.Apps.PlaneSpotter;

namespace LedMatrixOS.Apps.ISS;

public enum IssStatus { Ok, Offline }

/// <summary>
/// Where the station is. <see cref="Visibility"/> is the API's own "daylight", "visible" or "eclipsed". The sub-solar point
/// (<see cref="SolarLat"/>/<see cref="SolarLon"/>) is where the sun is overhead, used to shade the night side of the map; it is NaN when unknown.
/// </summary>
public sealed record IssPosition(
    double Latitude, double Longitude, double AltitudeKm, double VelocityKmh, string Visibility,
    double SolarLat = double.NaN, double SolarLon = double.NaN);

/// <summary>The result of one poll. Sources never throw: a failure comes back as <see cref="IssStatus.Offline"/> with no position.</summary>
public sealed record IssSnapshot(IssStatus Status, IssPosition? Position)
{
    public static readonly IssSnapshot Offline = new(IssStatus.Offline, null);
}

public interface IIssSource
{
    Task<IssSnapshot> GetAsync(CancellationToken ct);
}

/// <summary>The keyless wheretheiss.at API (NORAD id 25544).</summary>
public sealed class WhereTheIssSource(HttpClient http) : IIssSource
{
    public const string Url = "https://api.wheretheiss.at/v1/satellites/25544";

    public async Task<IssSnapshot> GetAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var json = await http.GetStringAsync(Url, timeout.Token).ConfigureAwait(false);
            return Parse(json);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return IssSnapshot.Offline;
        }
    }

    /// <summary>Parses the response; anything without a usable latitude and longitude is <see cref="IssSnapshot.Offline"/>.</summary>
    public static IssSnapshot Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return IssSnapshot.Offline;
            double? lat = Num(root, "latitude"), lon = Num(root, "longitude");
            if (lat is not { } la || lon is not { } lo || !PlaneMath.IsValidCoordinate(la, lo)) return IssSnapshot.Offline;
            var visibility = root.TryGetProperty("visibility", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return new IssSnapshot(IssStatus.Ok, new IssPosition(la, lo, Num(root, "altitude") ?? 0, Num(root, "velocity") ?? 0, visibility,
                Num(root, "solar_lat") ?? double.NaN, Num(root, "solar_lon") ?? double.NaN));
        }
        catch (JsonException)
        {
            return IssSnapshot.Offline;
        }
    }

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
}

/// <summary>Test/demo source: answers with whatever the callback returns, with no network.</summary>
public sealed class FakeIssSource(Func<IssSnapshot>? respond = null) : IIssSource
{
    public int Calls { get; private set; }

    public Task<IssSnapshot> GetAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(respond?.Invoke() ?? IssSnapshot.Offline);
    }

    public static IssPosition At(double lat, double lon, string visibility = "visible", double solarLat = 20, double solarLon = 10) =>
        new(lat, lon, 421.3, 27578.6, visibility, solarLat, solarLon);
}

/// <summary>A home point shared between the polling thread (which may geocode it) and the render thread.</summary>
internal sealed record HomePoint(double Lat, double Lon, string Name)
{
    public static string Format(double lat, double lon) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(lat):0.0}{(lat < 0 ? 'S' : 'N')} {Math.Abs(lon):0.0}{(lon < 0 ? 'W' : 'E')}");
}
