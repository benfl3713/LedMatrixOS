using System.Globalization;

namespace LedMatrixOS.Apps.PlaneSpotter;

/// <summary>
/// One aircraft as the display shows it. The instance lives across polls (same icao24), so the list keeps its row and only the
/// text that changed is rebuilt. Strings are formatted when the data changes, never per frame.
/// </summary>
internal sealed class PlaneRow
{
    public string Key = "", Callsign = "", Operator = "", AltText = "--", SpeedText = "-- kt", DistText = "";
    public int Altitude;
    public bool HasAltitude;
    /// <summary>-1 descending, 0 level or unknown, 1 climbing.</summary>
    public int Climb;
    public bool HasTrack;
    public float Track;
    public double DistKm, BearingDeg;
    /// <summary>Raw altitude (metres) and speed (m/s) for sorting; unknown sorts last.</summary>
    public double AltitudeMetres, VelocityMs;
    /// <summary>Position on the radar as a fraction of its radius (x right, y down), within the unit circle.</summary>
    public float RadarX, RadarY;

    public const double ClimbThresholdMs = 1.0;   // about 200 ft/min

    public void Set(Aircraft a, double homeLat, double homeLon, double distKm, double radiusKm, bool feet)
    {
        Key = a.Icao24;
        Callsign = a.Callsign.Length > 0 ? a.Callsign : a.Icao24.ToUpperInvariant();
        var op = AirlineLookup.Describe(a.Callsign, a.Country);
        if (!string.Equals(op, Operator)) Operator = op;

        AltitudeMetres = a.AltitudeMetres ?? double.NegativeInfinity;
        VelocityMs = a.VelocityMs ?? double.NegativeInfinity;
        HasAltitude = a.AltitudeMetres is not null;
        if (a.AltitudeMetres is { } m)
        {
            double v = feet ? PlaneMath.MetresToFeet(m) : m;
            Altitude = Math.Max(0, (int)(Math.Round(v / 100.0) * 100));
            AltText = Altitude.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            Altitude = 0;
            AltText = "--";
        }

        Climb = a.VerticalRateMs is { } vr ? (vr > ClimbThresholdMs ? 1 : vr < -ClimbThresholdMs ? -1 : 0) : 0;
        SpeedText = a.VelocityMs is { } ms ? ((int)Math.Round(PlaneMath.MsToKnots(ms))).ToString(CultureInfo.InvariantCulture) + " kt" : "-- kt";
        HasTrack = a.TrackDegrees is not null;
        Track = (float)(a.TrackDegrees ?? 0);

        DistKm = distKm;
        DistText = distKm < 10 ? distKm.ToString("0.0", CultureInfo.InvariantCulture) + "km" : ((int)Math.Round(distKm)).ToString(CultureInfo.InvariantCulture) + "km";
        BearingDeg = PlaneMath.BearingDegrees(homeLat, homeLon, a.Latitude!.Value, a.Longitude!.Value);
        double f = Math.Min(1.0, distKm / Math.Max(0.1, radiusKm));
        double rad = BearingDeg * Math.PI / 180;
        RadarX = (float)(Math.Sin(rad) * f);
        RadarY = (float)(-Math.Cos(rad) * f);
    }
}

internal readonly record struct PlanePageToken(int Index);

internal enum PlaneSort { Nearest, Highest, Fastest }

/// <summary>
/// Which aircraft the board keeps and how it orders them. The default value is the original behaviour: airborne aircraft only, any altitude,
/// nearest first. Altitudes are in the display unit (feet or metres); 0 means no limit.
/// </summary>
internal readonly record struct PlaneFilter(int MinAltitude = 0, int MaxAltitude = 0, bool ShowGround = false, PlaneSort Sort = PlaneSort.Nearest);

/// <summary>
/// Turns polled aircraft into what the board shows: aircraft in range sorted nearest first (the hero, then the rest in pages),
/// and the set of aircraft that have just entered range. Published arrays only change when a new snapshot (or a setting) arrives,
/// so a per-frame <see cref="Refresh"/> with unchanged inputs allocates nothing.
/// </summary>
internal sealed class PlaneBoardModel
{
    public const int RowsPerPage = 5;

    private readonly Dictionary<string, PlaneRow> _pool = new();
    private readonly HashSet<string> _present = new();
    private readonly List<PlaneRow> _scratch = new();
    private readonly List<PlaneRow> _new = new();
    private PlaneSnapshot? _applied;
    private double _radius = -1;
    private bool _feet, _baselined;
    private PlaneFilter _filter;
    private PlaneRow[] _all = [];
    private PlaneRow[] _rest = [];
    private PlanePageToken[] _tokens = [];
    private PlaneRow[][] _pages = [];

    public IReadOnlyList<PlaneRow> All => _all;
    public int Count => _all.Length;
    public PlaneRow? Hero => _all.Length > 0 ? _all[0] : null;
    public IReadOnlyList<PlanePageToken> Pages => _tokens;
    public IReadOnlyList<PlaneRow> Page(int index) => index >= 0 && index < _pages.Length ? _pages[index] : [];
    public IReadOnlyList<PlaneRow> Rest => _rest;

    /// <summary>Rows that entered range in the latest refresh (always empty on the first one, which only sets the baseline).</summary>
    public IReadOnlyList<PlaneRow> NewRows => _new;

    /// <summary>Text for the status strip, rebuilt when the count changes.</summary>
    private static bool InAltitudeBand(Aircraft a, PlaneFilter filter, bool feet)
    {
        if (filter.MinAltitude <= 0 && filter.MaxAltitude <= 0) return true;
        // With a limit set, an aircraft that reports no altitude cannot be placed in the band.
        if (a.AltitudeMetres is not { } m) return false;
        double v = feet ? PlaneMath.MetresToFeet(m) : m;
        return (filter.MinAltitude <= 0 || v >= filter.MinAltitude) && (filter.MaxAltitude <= 0 || v <= filter.MaxAltitude);
    }

    public string CountText { get; private set; } = "0 in range";

    public void Reset()
    {
        _applied = null;
        _baselined = false;
        _radius = -1;
        _filter = default;
        _pool.Clear();
        _present.Clear();
        _new.Clear();
        _all = _rest = [];
        _tokens = [];
        _pages = [];
        CountText = "0 in range";
    }

    /// <summary>Applies a snapshot. Failed snapshots are ignored so the last good aircraft survive a blip. Returns true when the rows were rebuilt.</summary>
    public bool Refresh(PlaneSnapshot? snapshot, double radiusKm, bool feet, PlaneFilter filter = default)
    {
        _new.Clear();
        if (snapshot is not { Status: PlaneStatus.Ok }) return false;
        if (ReferenceEquals(snapshot, _applied) && radiusKm == _radius && feet == _feet && filter == _filter) return false;

        _applied = snapshot;
        _radius = radiusKm;
        _feet = feet;
        _filter = filter;

        _scratch.Clear();
        foreach (var a in snapshot.Aircraft)
        {
            if ((a.OnGround && !filter.ShowGround) || a.Latitude is not { } lat || a.Longitude is not { } lon) continue;
            double d = PlaneMath.HaversineKm(snapshot.HomeLatitude, snapshot.HomeLongitude, lat, lon);
            if (d > radiusKm) continue;
            if (!InAltitudeBand(a, filter, feet)) continue;
            if (!_pool.TryGetValue(a.Icao24, out var row)) _pool[a.Icao24] = row = new PlaneRow();
            row.Set(a, snapshot.HomeLatitude, snapshot.HomeLongitude, d, radiusKm, feet);
            _scratch.Add(row);
        }

        _scratch.Sort(filter.Sort switch
        {
            PlaneSort.Highest => static (x, y) => y.AltitudeMetres.CompareTo(x.AltitudeMetres) is var c and not 0 ? c : x.DistKm.CompareTo(y.DistKm),
            PlaneSort.Fastest => static (x, y) => y.VelocityMs.CompareTo(x.VelocityMs) is var c and not 0 ? c : x.DistKm.CompareTo(y.DistKm),
            _ => static (x, y) => x.DistKm.CompareTo(y.DistKm),
        });
        // Duplicate icao24 in one response would share a row; keep the first.
        for (int i = _scratch.Count - 1; i > 0; i--)
            if (ReferenceEquals(_scratch[i], _scratch[i - 1])) _scratch.RemoveAt(i);

        foreach (var row in _scratch)
            if (_baselined && !_present.Contains(row.Key)) _new.Add(row);

        _present.Clear();
        foreach (var row in _scratch) _present.Add(row.Key);
        foreach (var key in _pool.Keys.Where(k => !_present.Contains(k)).ToList()) _pool.Remove(key);
        _baselined = true;

        _all = _scratch.ToArray();
        _rest = _all.Length > 1 ? _all[1..] : [];
        int pageCount = (_rest.Length + RowsPerPage - 1) / RowsPerPage;
        var pages = new PlaneRow[pageCount][];
        for (int p = 0; p < pageCount; p++) pages[p] = _rest.Skip(p * RowsPerPage).Take(RowsPerPage).ToArray();
        _pages = pages;
        if (_tokens.Length != pageCount) _tokens = Enumerable.Range(0, pageCount).Select(i => new PlanePageToken(i)).ToArray();
        CountText = _all.Length == 1 ? "1 in range" : _all.Length.ToString(CultureInfo.InvariantCulture) + " in range";
        return true;
    }
}
