using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.AirQuality;

/// <summary>
/// One reading of the air: the European AQI, the pollutants behind it, UV and (in Europe, in season) pollen counts in grains per cubic
/// metre. <see cref="Hourly"/> is the AQI for the next 24 hours starting this hour. A class, so apps can tell a new reading by reference.
/// </summary>
public sealed class AirQualitySnapshot(string location, int aqi, double pm25, double pm10, double ozone, double no2, double uv,
    double? alder, double? birch, double? grass, IReadOnlyList<float> hourly)
{
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
