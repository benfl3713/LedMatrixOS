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
    /// <summary>Position on the radar as a fraction of its radius (x right, y down), within the unit circle.</summary>
    public float RadarX, RadarY;

    public const double ClimbThresholdMs = 1.0;   // about 200 ft/min

    public void Set(Aircraft a, double homeLat, double homeLon, double distKm, double radiusKm, bool feet)
    {
        Key = a.Icao24;
        Callsign = a.Callsign.Length > 0 ? a.Callsign : a.Icao24.ToUpperInvariant();
        var op = AirlineLookup.Describe(a.Callsign, a.Country);
        if (!string.Equals(op, Operator)) Operator = op;

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
    public string CountText { get; private set; } = "0 in range";

    public void Reset()
    {
        _applied = null;
        _baselined = false;
        _radius = -1;
        _pool.Clear();
        _present.Clear();
        _new.Clear();
        _all = _rest = [];
        _tokens = [];
        _pages = [];
        CountText = "0 in range";
    }

    /// <summary>Applies a snapshot. Failed snapshots are ignored so the last good aircraft survive a blip. Returns true when the rows were rebuilt.</summary>
    public bool Refresh(PlaneSnapshot? snapshot, double radiusKm, bool feet)
    {
        _new.Clear();
        if (snapshot is not { Status: PlaneStatus.Ok }) return false;
        if (ReferenceEquals(snapshot, _applied) && radiusKm == _radius && feet == _feet) return false;

        _applied = snapshot;
        _radius = radiusKm;
        _feet = feet;

        _scratch.Clear();
        foreach (var a in snapshot.Aircraft)
        {
            if (a.OnGround || a.Latitude is not { } lat || a.Longitude is not { } lon) continue;
            double d = PlaneMath.HaversineKm(snapshot.HomeLatitude, snapshot.HomeLongitude, lat, lon);
            if (d > radiusKm) continue;
            if (!_pool.TryGetValue(a.Icao24, out var row)) _pool[a.Icao24] = row = new PlaneRow();
            row.Set(a, snapshot.HomeLatitude, snapshot.HomeLongitude, d, radiusKm, feet);
            _scratch.Add(row);
        }

        _scratch.Sort(static (x, y) => x.DistKm.CompareTo(y.DistKm));
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
