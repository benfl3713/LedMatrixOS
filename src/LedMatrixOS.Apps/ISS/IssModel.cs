using System.Globalization;
using LedMatrixOS.Apps.PlaneSpotter;

namespace LedMatrixOS.Apps.ISS;

/// <summary>
/// Everything the ISS Tracker shows, derived from the latest poll: the map cell of the station, its ground track, the night shading, the
/// distance from home and every string. Work and allocation happen only when a new snapshot arrives (every few seconds), never per frame.
/// </summary>
internal sealed class IssModel
{
    public const double RangeKm = 1500, OverheadKm = 500;
    private const double LeaveFactor = 1.1;
    public const int PastPoints = 16, FuturePoints = 8;
    private const double PastStepSeconds = 40, FutureStepSeconds = 120;
    private const double KmToMiles = 0.621371;

    private IssSnapshot? _snapshot;
    private HomePoint? _home;
    private bool _miles, _keyed;
    private double _rangeKm = RangeKm;
    private IssPosition? _previous;
    private bool? _ascending;

    public IssPosition? Position { get; private set; }
    public bool Stale { get; private set; }
    public bool HasPosition => Position is not null;
    public bool InRange { get; private set; }
    public bool Overhead { get; private set; }
    public double DistanceKm { get; private set; }

    public int DotX { get; private set; }
    public int DotY { get; private set; }
    public int HomeX { get; private set; } = -1;
    public int HomeY { get; private set; } = -1;

    public readonly byte[] Shade = new byte[WorldMap.Width * WorldMap.Height];
    public readonly int[] PastX = new int[PastPoints], PastY = new int[PastPoints], FutureX = new int[FuturePoints], FutureY = new int[FuturePoints];
    public int PastCount { get; private set; }
    public int FutureCount { get; private set; }

    public string RegionText { get; private set; } = "";
    public string LatLonText { get; private set; } = "";
    public string AltText { get; private set; } = "";
    public string DistText { get; private set; } = "";
    public string SpeedUnit { get; private set; } = "km/h";
    public string Headline { get; private set; } = "";
    public int Speed { get; private set; }

    /// <summary>Takes the latest poll. Returns true on the call where the station has just come within range of home.</summary>
    public bool Refresh(IssSnapshot? snapshot, HomePoint? home, bool miles, double alertKm = RangeKm)
    {
        if (_keyed && ReferenceEquals(snapshot, _snapshot) && ReferenceEquals(home, _home) && miles == _miles && alertKm == _rangeKm) return false;
        _rangeKm = alertKm;
        bool sameSnapshot = _keyed && ReferenceEquals(snapshot, _snapshot);
        _keyed = true;
        _snapshot = snapshot;
        _home = home;
        _miles = miles;

        if (snapshot?.Position is { } position)
        {
            if (!sameSnapshot) TrackDirection(position);
            Position = position;
            Stale = false;
        }
        else if (snapshot is not null && Position is not null)
        {
            Stale = true;
        }

        if (Position is not { } p) return false;
        return Derive(p, home, miles);
    }

    private void TrackDirection(IssPosition position)
    {
        if (_previous is { } prev && !ReferenceEquals(prev, position))
        {
            double dlat = position.Latitude - prev.Latitude;
            if (Math.Abs(dlat) > 0.002) _ascending = dlat > 0;
        }
        _previous = position;
    }

    private bool Derive(IssPosition p, HomePoint? home, bool miles)
    {
        DotX = WorldMap.ColumnOf(p.Longitude);
        DotY = WorldMap.RowOf(p.Latitude);
        WorldMap.ShadeNight(p.SolarLat, p.SolarLon, Shade);
        BuildTrack(p);

        if (home is not null)
        {
            HomeX = WorldMap.ColumnOf(home.Lon);
            HomeY = WorldMap.RowOf(home.Lat);
            DistanceKm = PlaneMath.HaversineKm(p.Latitude, p.Longitude, home.Lat, home.Lon);
        }
        else
        {
            HomeX = HomeY = -1;
            DistanceKm = double.NaN;
        }

        bool wasInRange = InRange;
        InRange = home is not null && (wasInRange ? DistanceKm <= _rangeKm * LeaveFactor : DistanceKm <= _rangeKm);
        Overhead = InRange && DistanceKm <= Math.Min(OverheadKm, _rangeKm / 3);

        var inv = CultureInfo.InvariantCulture;
        RegionText = Stale ? "signal lost, retrying" : "over " + IssRegions.Describe(p.Latitude, p.Longitude);
        LatLonText = HomePoint.Format(p.Latitude, p.Longitude);
        double unit = miles ? KmToMiles : 1;
        AltText = "ALT " + Math.Round(p.AltitudeKm * unit).ToString("N0", inv) + (miles ? " mi" : " km");
        Speed = (int)Math.Round(p.VelocityKmh * unit);
        SpeedUnit = miles ? "mph" : "km/h";
        DistText = home is null ? "Set a location" : Math.Round(DistanceKm * unit).ToString("N0", inv) + (miles ? " mi from you" : " km from you");
        Headline = !InRange ? ""
            : Overhead ? "OVERHEAD NOW"
            : string.Equals(p.Visibility, "visible", StringComparison.OrdinalIgnoreCase) ? "VISIBLE TONIGHT"
            : "PASSING NEARBY";
        return InRange && !wasInRange;
    }

    private void BuildTrack(IssPosition p)
    {
        PastCount = FutureCount = 0;
        if (_ascending is not { } up) return;

        for (int i = 0; i < PastPoints; i++)
        {
            var (lat, lon) = IssOrbit.Project(p.Latitude, p.Longitude, up, -(i + 1) * PastStepSeconds);
            PastX[i] = WorldMap.ColumnOf(lon);
            PastY[i] = WorldMap.RowOf(lat);
        }
        PastCount = PastPoints;

        for (int i = 0; i < FuturePoints; i++)
        {
            var (lat, lon) = IssOrbit.Project(p.Latitude, p.Longitude, up, (i + 1) * FutureStepSeconds);
            FutureX[i] = WorldMap.ColumnOf(lon);
            FutureY[i] = WorldMap.RowOf(lat);
        }
        FutureCount = FuturePoints;
    }
}
