using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.AirQuality;

/// <summary>
/// One reading of the air: the European AQI, the pollutants behind it, UV and (in Europe, in season) pollen counts in grains per cubic
/// metre. <see cref="Hourly"/> is the AQI for the next 24 hours starting this hour. A class, so apps can tell a new reading by reference.
/// </summary>
public sealed class AirQualitySnapshot(string location, int aqi, double pm25, double pm10, double ozone, double no2, double uv,
    double? alder, double? birch, double? grass, IReadOnlyList<float> hourly, int? usAqi = null, IReadOnlyList<float>? usHourly = null)
{
    /// <summary>The US EPA AQI now: the API's own value, or worked out from the pollutants when the source has none.</summary>
    public int UsAqi { get; } = usAqi ?? AirQualityScale.UsAqiOf(pm25, pm10, ozone, no2);

    /// <summary>The next 24 hours on the US scale: the API's own series, or the European series converted band by band.</summary>
    public IReadOnlyList<float> UsHourly { get; } = usHourly ?? hourly.Select(AirQualityScale.EuToUs).ToArray();

    public string Location { get; } = location;
    public int Aqi { get; } = aqi;
    public double Pm25 { get; } = pm25;
    public double Pm10 { get; } = pm10;
    public double Ozone { get; } = ozone;
    public double No2 { get; } = no2;
    public double Uv { get; } = uv;
    public double? Alder { get; } = alder;
    public double? Birch { get; } = birch;
    public double? Grass { get; } = grass;
    public IReadOnlyList<float> Hourly { get; } = hourly;
}

public sealed record AirQualityQuery(string Location);

public interface IAirQualitySource
{
    Task<AirQualitySnapshot> GetAsync(AirQualityQuery query, CancellationToken cancellationToken);
}

/// <summary>European AQI bands, in order of worsening.</summary>
public enum AqiBand { Good, Fair, Moderate, Poor, VeryPoor, Extreme }

/// <summary>Band thresholds, colours and wording for the air quality display.</summary>
public static class AirQualityScale
{
    private static readonly Pixel[] Colors =
    [
        new(60, 225, 140), new(170, 225, 60), new(255, 200, 50), new(255, 120, 40), new(255, 55, 70), new(205, 80, 210),
    ];

    private static readonly string[] Labels = ["GOOD", "FAIR", "MODERATE", "POOR", "VERY POOR", "EXTREME"];

    public static AqiBand BandOf(int aqi) => aqi switch { < 20 => AqiBand.Good, < 40 => AqiBand.Fair, < 60 => AqiBand.Moderate, < 80 => AqiBand.Poor, < 100 => AqiBand.VeryPoor, _ => AqiBand.Extreme };

    public static Pixel ColorOf(AqiBand band) => Colors[(int)band];

    public static string LabelOf(AqiBand band) => Labels[(int)band];

    // EAQI per-pollutant breakpoints (ug/m3), the upper bound of Good, Fair, Moderate, Poor and Very poor.
    private static readonly double[] Pm25Limits = [5, 15, 50, 90, 140];
    private static readonly double[] Pm10Limits = [15, 45, 120, 195, 270];

    public static AqiBand Pm25Band(double v) => Banded(v, Pm25Limits);

    public static AqiBand Pm10Band(double v) => Banded(v, Pm10Limits);

    private static AqiBand Banded(double v, double[] limits)
    {
        for (int i = 0; i < limits.Length; i++)
            if (v <= limits[i]) return (AqiBand)i;
        return AqiBand.Extreme;
    }

    // ---- US EPA AQI ----

    private static readonly string[] UsLabels = ["GOOD", "MODERATE", "SENSITIVE", "UNHEALTHY", "V.UNHEALTHY", "HAZARDOUS"];

    // Colour band shown for each US category (the panel has six colours: green, lime, yellow, orange, red, purple).
    private static readonly AqiBand[] UsBands = [AqiBand.Good, AqiBand.Moderate, AqiBand.Poor, AqiBand.VeryPoor, AqiBand.Extreme, AqiBand.Extreme];

    private static readonly int[] UsLimits = [50, 100, 150, 200, 300];

    /// <summary>The US category index (0 Good ... 5 Hazardous) of an AQI value.</summary>
    public static int UsCategoryOf(int aqi)
    {
        for (int i = 0; i < UsLimits.Length; i++)
            if (aqi <= UsLimits[i]) return i;
        return 5;
    }

    public static AqiBand UsBandOf(int aqi) => UsBands[UsCategoryOf(aqi)];

    public static string UsLabelOf(int aqi) => UsLabels[UsCategoryOf(aqi)];

    // Piecewise linear mapping EU band edges (0, 20, 40, 60, 80, 100) to the US edges, for series the API gives only in EU terms.
    private static readonly double[] EuEdges = [0, 20, 40, 60, 80, 100], UsEdges = [0, 50, 100, 150, 200, 300];

    public static float EuToUs(float eu)
    {
        if (eu <= 0) return 0;
        for (int i = 1; i < EuEdges.Length; i++)
            if (eu <= EuEdges[i]) return (float)(UsEdges[i - 1] + (eu - EuEdges[i - 1]) / (EuEdges[i] - EuEdges[i - 1]) * (UsEdges[i] - UsEdges[i - 1]));
        return (float)(300 + (eu - 100) * 2);
    }

    // EPA breakpoints: concentration low/high, index low/high.
    private static readonly (double Lo, double Hi, int ILo, int IHi)[] Pm25Us =
        [(0, 9, 0, 50), (9.1, 35.4, 51, 100), (35.5, 55.4, 101, 150), (55.5, 125.4, 151, 200), (125.5, 225.4, 201, 300), (225.5, 325.4, 301, 500)];
    private static readonly (double Lo, double Hi, int ILo, int IHi)[] Pm10Us =
        [(0, 54, 0, 50), (55, 154, 51, 100), (155, 254, 101, 150), (255, 354, 151, 200), (355, 424, 201, 300), (425, 604, 301, 500)];
    private static readonly (double Lo, double Hi, int ILo, int IHi)[] O3Us =    // ppb, 8 hour
        [(0, 54, 0, 50), (55, 70, 51, 100), (71, 85, 101, 150), (86, 105, 151, 200), (106, 200, 201, 300)];
    private static readonly (double Lo, double Hi, int ILo, int IHi)[] No2Us =   // ppb, 1 hour
        [(0, 53, 0, 50), (54, 100, 51, 100), (101, 360, 101, 150), (361, 649, 151, 200), (650, 1249, 201, 300), (1250, 2049, 301, 500)];

    /// <summary>The US AQI from pollutant concentrations in ug/m3: the worst of the PM2.5, PM10, ozone and NO2 sub-indices.</summary>
    public static int UsAqiOf(double pm25, double pm10, double ozone, double no2)
    {
        double worst = Math.Max(Math.Max(Sub(Pm25Us, Math.Floor(pm25 * 10) / 10), Sub(Pm10Us, Math.Floor(pm10))),
                                Math.Max(Sub(O3Us, Math.Floor(ozone / 1.96)), Sub(No2Us, Math.Floor(no2 / 1.88))));
        return (int)Math.Round(worst);
    }

    private static double Sub((double Lo, double Hi, int ILo, int IHi)[] table, double c)
    {
        if (c <= 0) return 0;
        foreach (var (lo, hi, ilo, ihi) in table)
            if (c <= hi) return (ihi - ilo) / (hi - lo) * (Math.Max(c, lo) - lo) + ilo;
        return table[^1].IHi;
    }

    /// <summary>Colour slot of "nothing to report" (after the six bands), for text styles indexed by band.</summary>
    public const int Neutral = 6;

    public static Pixel NeutralColor => new(120, 120, 135);

    /// <summary>UV index word and the colour slot (a band index, or <see cref="Neutral"/>) of the WHO scale.</summary>
    public static (string Label, int Slot) UvLevel(double uv) => uv switch
    {
        < 3 => ("LOW", 0),
        < 6 => ("MODERATE", 2),
        < 8 => ("HIGH", 3),
        < 11 => ("V.HIGH", 4),
        _ => ("EXTREME", 5),
    };

    /// <summary>Pollen word and colour slot for a grain count (grains/m3).</summary>
    public static (string Label, int Slot) PollenLevel(double grains) => grains switch
    {
        < 1 => ("NONE", Neutral),
        < 20 => ("LOW", 0),
        < 90 => ("MEDIUM", 2),
        < 300 => ("HIGH", 3),
        _ => ("V.HIGH", 4),
    };

    /// <summary>The "good to run outside?" line, from the AQI and fine particles; the band picks its colour.</summary>
    public static (string Text, AqiBand Band) RunVerdict(int aqi, double pm25)
    {
        var band = (AqiBand)Math.Max((int)BandOf(aqi), (int)Pm25Band(pm25));
        return band switch
        {
            AqiBand.Good => ("Good to run outside", band),
            AqiBand.Fair => ("Fine to run outside", band),
            AqiBand.Moderate => ("OK to run, take it easy", band),
            AqiBand.Poor => ("Go easy, short runs only", band),
            _ => ("Skip the run, stay indoors", band),
        };
    }
}
