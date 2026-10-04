namespace LedMatrixOS.Apps.PlaneSpotter;

public enum PlaneStatus { Ok, Busy, Offline }

/// <summary>One aircraft state vector, every measurement nullable because OpenSky leaves out whatever it did not receive.</summary>
public sealed record Aircraft(
    string Icao24, string Callsign, string Country, double? Latitude, double? Longitude,
    double? AltitudeMetres, double? VelocityMs, double? TrackDegrees, double? VerticalRateMs, bool OnGround);

/// <summary>A query: aircraft within <paramref name="RadiusKm"/> of a point.</summary>
public readonly record struct PlaneQuery(double Latitude, double Longitude, double RadiusKm);

/// <summary>The result of one poll. Sources never throw: failures come back as <see cref="PlaneStatus.Busy"/> or <see cref="PlaneStatus.Offline"/>.</summary>
public sealed record PlaneSnapshot(PlaneStatus Status, Aircraft[] Aircraft, double HomeLatitude, double HomeLongitude)
{
    public static PlaneSnapshot Failed(PlaneStatus status, PlaneQuery q) => new(status, [], q.Latitude, q.Longitude);
}

public interface IPlaneSource
{
    Task<PlaneSnapshot> GetAsync(PlaneQuery query, CancellationToken ct);
}

/// <summary>Pure geometry and unit helpers.</summary>
public static class PlaneMath
{
    public const double EarthRadiusKm = 6371.0088;

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
        double dp = p2 - p1, dl = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Initial bearing from the first point to the second, in degrees clockwise from north, [0, 360).</summary>
    public static double BearingDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, dl = (lon2 - lon1) * Math.PI / 180;
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    /// <summary>The latitude/longitude rectangle that contains the circle of <paramref name="radiusKm"/> around a point (clamped to the globe).</summary>
    public static (double MinLat, double MinLon, double MaxLat, double MaxLon) BoundingBox(double lat, double lon, double radiusKm)
    {
        double dLat = radiusKm / 111.195;
        double cos = Math.Max(0.01, Math.Cos(lat * Math.PI / 180));
        double dLon = radiusKm / (111.195 * cos);
        return (Math.Max(-90, lat - dLat), Math.Max(-180, lon - dLon), Math.Min(90, lat + dLat), Math.Min(180, lon + dLon));
    }

    public static double MetresToFeet(double metres) => metres * 3.280839895;
    public static double MsToKnots(double ms) => ms * 1.943844492;

    public static bool IsValidCoordinate(double lat, double lon) => lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
}

/// <summary>Maps a callsign prefix to an airline. OpenSky callsigns start with the 3-letter ICAO designator (BAW117); the 2-letter IATA form (BA117) is understood too.</summary>
public static class AirlineLookup
{
    private static readonly Dictionary<string, string> Icao = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BAW"] = "British Airways", ["EZY"] = "easyJet", ["EZS"] = "easyJet Switzerland", ["RYR"] = "Ryanair", ["VIR"] = "Virgin Atlantic",
        ["AFR"] = "Air France", ["DLH"] = "Lufthansa", ["KLM"] = "KLM", ["UAE"] = "Emirates", ["EIN"] = "Aer Lingus",
        ["SAS"] = "SAS", ["IBE"] = "Iberia", ["SWR"] = "Swiss", ["AUA"] = "Austrian", ["FIN"] = "Finnair",
        ["THY"] = "Turkish Airlines", ["QTR"] = "Qatar Airways", ["ETD"] = "Etihad", ["UAL"] = "United", ["AAL"] = "American",
        ["DAL"] = "Delta", ["ACA"] = "Air Canada", ["SIA"] = "Singapore Airlines", ["CPA"] = "Cathay Pacific", ["QFA"] = "Qantas",
        ["TOM"] = "TUI Airways", ["WZZ"] = "Wizz Air", ["NAX"] = "Norwegian", ["TAP"] = "TAP Air Portugal", ["LOT"] = "LOT Polish",
        ["VLG"] = "Vueling", ["EXS"] = "Jet2", ["LOG"] = "Loganair", ["BEL"] = "Brussels Airlines", ["ITY"] = "ITA Airways",
        ["AZA"] = "Alitalia", ["ELY"] = "El Al", ["SVA"] = "Saudia", ["MSR"] = "EgyptAir", ["ETH"] = "Ethiopian", ["KAL"] = "Korean Air",
        ["JAL"] = "Japan Airlines", ["ANA"] = "All Nippon", ["CCA"] = "Air China", ["CES"] = "China Eastern", ["AIC"] = "Air India",
        ["ICE"] = "Icelandair", ["BCS"] = "European Air Transport", ["FDX"] = "FedEx", ["UPS"] = "UPS", ["GEC"] = "Lufthansa Cargo",
        ["CLX"] = "Cargolux", ["RRR"] = "Royal Air Force", ["SHT"] = "BA Shuttle", ["CFE"] = "BA CityFlyer", ["BMR"] = "Blue Islands",
    };

    private static readonly Dictionary<string, string> Iata = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BA"] = "British Airways", ["U2"] = "easyJet", ["FR"] = "Ryanair", ["VS"] = "Virgin Atlantic", ["AF"] = "Air France",
        ["LH"] = "Lufthansa", ["KL"] = "KLM", ["EK"] = "Emirates", ["EI"] = "Aer Lingus", ["SK"] = "SAS", ["IB"] = "Iberia",
        ["LX"] = "Swiss", ["OS"] = "Austrian", ["AY"] = "Finnair", ["TK"] = "Turkish Airlines", ["QR"] = "Qatar Airways",
        ["EY"] = "Etihad", ["UA"] = "United", ["AA"] = "American", ["DL"] = "Delta", ["AC"] = "Air Canada", ["SQ"] = "Singapore Airlines",
        ["CX"] = "Cathay Pacific", ["QF"] = "Qantas", ["BY"] = "TUI Airways", ["W6"] = "Wizz Air", ["DY"] = "Norwegian", ["TP"] = "TAP Air Portugal",
        ["LO"] = "LOT Polish", ["VY"] = "Vueling", ["LS"] = "Jet2", ["BE"] = "Flybe", ["SN"] = "Brussels Airlines",
    };

    /// <summary>The airline for a callsign, or null when the prefix is not in the table.</summary>
    public static string? Find(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return null;
        var cs = callsign.Trim();
        if (cs.Length >= 4 && char.IsLetter(cs[0]) && char.IsLetter(cs[1]) && char.IsLetter(cs[2]) && Icao.TryGetValue(cs[..3], out var name)) return name;
        if (cs.Length >= 3 && char.IsLetterOrDigit(cs[0]) && char.IsLetterOrDigit(cs[1]) && char.IsDigit(cs[2]) && Iata.TryGetValue(cs[..2], out name)) return name;
        return null;
    }

    /// <summary>What to show under the callsign: the airline, else the registration country, else "Unknown operator".</summary>
    public static string Describe(string? callsign, string? country) =>
        Find(callsign) ?? (string.IsNullOrWhiteSpace(country) ? "Unknown operator" : country.Trim());
}
