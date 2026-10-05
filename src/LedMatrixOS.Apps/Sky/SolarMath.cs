namespace LedMatrixOS.Apps.Sky;

/// <summary>Low-precision sun and moon maths (NOAA solar position formulas); good to a fraction of a degree, which is plenty for a 64 pixel sky.</summary>
public static class SolarMath
{
    private const double Rad = Math.PI / 180;

    /// <param name="Altitude">Degrees above the horizon (negative at night).</param>
    /// <param name="HourAngle">Degrees from solar noon, -180..180 (negative in the morning).</param>
    /// <param name="DayHalfAngle">Hour angle at which the sun rises/sets (0 in polar night, 180 in midnight sun).</param>
    public readonly record struct Sun(double Altitude, double HourAngle, double DayHalfAngle)
    {
        public bool IsDay => DayHalfAngle > 1e-6 && Math.Abs(HourAngle) <= DayHalfAngle;

        /// <summary>How far through the daylight (when <see cref="IsDay"/>) or the night (otherwise) we are, 0..1.</summary>
        public double Progress
        {
            get
            {
                if (IsDay) return Math.Clamp((HourAngle + DayHalfAngle) / (2 * DayHalfAngle), 0, 1);
                double span = 360 - 2 * DayHalfAngle;
                if (span < 1e-6) return 0.5;
                double h = HourAngle >= 0 ? HourAngle : HourAngle + 360;
                return Math.Clamp((h - DayHalfAngle) / span, 0, 1);
            }
        }
    }

    public static Sun SunAt(DateTimeOffset when, double latitude, double longitude)
    {
        var utc = when.UtcDateTime;
        double gamma = 2 * Math.PI / 365 * (utc.DayOfYear - 1 + (utc.Hour - 12) / 24.0 + utc.Minute / 1440.0);
        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma)
            + 0.000907 * Math.Sin(2 * gamma) - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        double minutes = utc.Hour * 60 + utc.Minute + utc.Second / 60.0;
        double solarMinutes = minutes + eqTime + 4 * longitude;
        double hour = solarMinutes / 4 - 180;
        hour -= 360 * Math.Floor((hour + 180) / 360);   // -180..180

        double lat = latitude * Rad;
        double sinAlt = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(hour * Rad);
        double altitude = Math.Asin(Math.Clamp(sinAlt, -1, 1)) / Rad;

        double cosH0 = (Math.Sin(-0.833 * Rad) - Math.Sin(lat) * Math.Sin(decl)) / (Math.Cos(lat) * Math.Cos(decl));
        double h0 = Math.Acos(Math.Clamp(cosH0, -1, 1)) / Rad;
        return new Sun(altitude, hour, h0);
    }

    /// <summary>Moon age as a fraction of the synodic month: 0 new, 0.5 full.</summary>
    public static double MoonAge(DateTimeOffset when)
    {
        double jd = when.UtcDateTime.Subtract(DateTime.UnixEpoch).TotalDays + 2440587.5;
        double age = (jd - 2451550.1) / 29.530588853;
        return age - Math.Floor(age);
    }
}
