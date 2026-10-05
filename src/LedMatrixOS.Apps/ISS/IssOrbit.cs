namespace LedMatrixOS.Apps.ISS;

/// <summary>
/// A circular-orbit model of the station, used to draw the ground track behind and ahead of it from nothing but its current position and
/// whether it is heading north or south. Inclination 51.64 degrees, period 92.7 minutes, over a rotating earth.
/// </summary>
internal static class IssOrbit
{
    public const double InclinationDeg = 51.64;
    public const double PeriodSeconds = 5560;
    private const double EarthDegPerSecond = 360.0 / 86164.0905;

    private static readonly double SinI = Math.Sin(InclinationDeg * Math.PI / 180), CosI = Math.Cos(InclinationDeg * Math.PI / 180);

    /// <summary>The sub-satellite point <paramref name="seconds"/> from now (negative for the past).</summary>
    public static (double Lat, double Lon) Project(double lat, double lon, bool ascending, double seconds)
    {
        double u0 = Math.Asin(Math.Clamp(Math.Sin(lat * Math.PI / 180) / SinI, -1, 1));
        if (!ascending) u0 = Math.PI - u0;
        double u = u0 + 2 * Math.PI * seconds / PeriodSeconds;

        double lat2 = Math.Asin(SinI * Math.Sin(u)) * 180 / Math.PI;
        double along = (Math.Atan2(CosI * Math.Sin(u), Math.Cos(u)) - Math.Atan2(CosI * Math.Sin(u0), Math.Cos(u0))) * 180 / Math.PI;
        along = ((along + 180) % 360 + 360) % 360 - 180;
        double lon2 = lon + along - EarthDegPerSecond * seconds;
        lon2 = ((lon2 + 180) % 360 + 360) % 360 - 180;
        return (lat2, lon2);
    }
}
