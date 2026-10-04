using System.Text.RegularExpressions;
using LedMatrixOS.Apps.HomeAssistant;
using LedMatrixOS.Apps.PlaneSpotter;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core.Settings;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// Wires the live pickers (Search/MultiSearch settings) of the built-in apps to their lookups. Providers are stateless and keyed
/// by app id and setting key, so the options endpoint works for apps that are not active.
/// </summary>
public static class BuiltInSettingOptions
{
    public static void Register(SettingOptionsRegistry registry, HttpClient http, IConfiguration configuration)
    {
        var tfl = new TflApi(http) { AppKey = configuration["TFL:AppKey"] };
        var stations = new TflStationOptions(tfl);
        registry.Register("tube-departures", "stationId", stations);
        registry.Register("commute", "stationId", stations);
        registry.Register("morning-briefing", "stationId", stations);

        var routes = new TflRouteOptions(tfl);
        registry.Register("tube-departures", "routes", routes);
        registry.Register("commute", "routes", routes);
        var busStops = new TflBusStopOptions(tfl);
        registry.Register("bus-arrivals", "stopIds", busStops);
        registry.Register("home", "chipStopId", busStops);
        registry.Register("ha-tiles", "entities", new HaEntityOptions(new HaApi(http), configuration));
        registry.Register("cycle-hub", "dockIds", new TflDockOptions(tfl));

        var geocoder = new PlaceGeocoder(http);
        var places = new PlaceOptions(geocoder);
        registry.Register("weather", "location", places);
        registry.Register("commute", "location", places);
        registry.Register("plane-spotter", "location", places);

        var journeyPoints = new JourneyPointOptions(geocoder, tfl);
        registry.Register("journey", "from", journeyPoints);
        registry.Register("journey", "to", journeyPoints);
    }
}

/// <summary>
/// Places by name (Open-Meteo geocoding). A pick is stored as "Name|lat,lon" (see <see cref="PlaceGeocoder.Encode"/>), so the
/// stored value needs no further lookup and cannot be ambiguous.
/// </summary>
internal class PlaceOptions(PlaceGeocoder geocoder) : ISettingOptionsProvider
{
    public virtual async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct) =>
        (await geocoder.SearchAsync(query, 8, ct))
            .Select(p => new SettingOption(PlaceGeocoder.Encode(p.Name, p.Lat, p.Lon), p.Label)).ToList();

    public virtual async Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct)
    {
        // A pick carries its own name; a plain name or "lat,lon" from older settings or configuration is looked up (or shown as is).
        if (PlaceGeocoder.TryParseEncoded(value, out var name, out _)) return name;
        if (PlaceGeocoder.TryParseCoordinates(value, out _)) return value;
        var first = (await geocoder.SearchAsync(value, 1, ct)).FirstOrDefault();
        return first?.Label ?? value;
    }
}

/// <summary>Journey endpoints: places, TfL rail stations (by Naptan id) and UK postcodes (used as typed).</summary>
internal sealed partial class JourneyPointOptions(PlaceGeocoder geocoder, TflApi tfl) : PlaceOptions(geocoder)
{
    public override async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct)
    {
        var options = new List<SettingOption>();
        if (Postcode().IsMatch(query))
        {
            var postcode = query.Trim().ToUpperInvariant();
            options.Add(new SettingOption(postcode, postcode, "Postcode"));
        }

        var stationsTask = tfl.FindStationsAsync(query, ct);
        var placesTask = base.GetOptionsAsync(appId, key, query, ct);
        try { options.AddRange((await stationsTask).Take(5).Select(m => new SettingOption(m.Id, m.Name, "Station"))); }
        catch (Exception) when (!ct.IsCancellationRequested) { /* places may still answer */ }
        try { options.AddRange(await placesTask); }
        catch (Exception) when (!ct.IsCancellationRequested) { /* stations may already have answered */ }
        return options;
    }

    public override async Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct)
    {
        if (NaptanId().IsMatch(value)) return await tfl.GetStationNameAsync(value, ct) is { Length: > 0 } name ? name : value;
        if (Postcode().IsMatch(value)) return value.ToUpperInvariant();
        return await base.GetLabelAsync(appId, key, value, ct);
    }

    [GeneratedRegex(@"^[A-Za-z]{1,2}\d[A-Za-z\d]?\s*\d[A-Za-z]{2}$")]
    private static partial Regex Postcode();

    [GeneratedRegex(@"^(HUB|\d{3,4}G?)[A-Za-z0-9]+$")]
    private static partial Regex NaptanId();
}
