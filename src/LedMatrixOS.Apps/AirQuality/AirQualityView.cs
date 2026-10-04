using System.Globalization;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;

namespace LedMatrixOS.Apps.AirQuality;

/// <summary>
/// Everything the screen shows for one reading: strings, colour slots and chart range, worked out once when a new reading arrives
/// so rendering only reads cached values.
/// </summary>
internal sealed class AirQualityView
{
    private static readonly string[] NoPollen = ["--", "--", "--"];

    public string Place { get; }
    public int Aqi { get; }
    public string AqiText { get; }
    public AqiBand Band { get; }
    public string BandLabel { get; }
    public string Pm25Text { get; }
    public string Pm10Text { get; }
    public int Pm25Slot { get; }
    public int Pm10Slot { get; }
    public string GasText { get; }
    public string VerdictText { get; }
    public int VerdictSlot { get; }
    public string UvText { get; }
    public string UvLabel { get; }
    public int UvSlot { get; }
    public string[] PollenLabels { get; }
    public int[] PollenSlots { get; }
    public bool HasPollen { get; }
    public float[] Hourly { get; }
    public float ChartMax { get; }
    public string TrendText { get; }

    public AirQualityView(AirQualitySnapshot s)
    {
        Place = s.Location.ToUpperInvariant();
        Aqi = Math.Max(0, s.Aqi);
        AqiText = s.Aqi.ToString(CultureInfo.InvariantCulture);
        Band = AirQualityScale.BandOf(s.Aqi);
        BandLabel = AirQualityScale.LabelOf(Band);
        Pm25Text = Fmt(s.Pm25);
        Pm10Text = Fmt(s.Pm10);
        Pm25Slot = (int)AirQualityScale.Pm25Band(s.Pm25);
        Pm10Slot = (int)AirQualityScale.Pm10Band(s.Pm10);
        GasText = "O3 " + Math.Round(s.Ozone) + "  NO2 " + Math.Round(s.No2);

        var (verdict, vband) = AirQualityScale.RunVerdict(s.Aqi, s.Pm25);
        VerdictText = verdict;
        VerdictSlot = (int)vband;

        UvText = "UV " + Fmt(s.Uv);
        (UvLabel, UvSlot) = AirQualityScale.UvLevel(s.Uv);

        double?[] pollen = [s.Alder, s.Birch, s.Grass];
        HasPollen = pollen.Any(p => p is not null);
        if (HasPollen)
        {
            PollenLabels = new string[3];
            PollenSlots = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (pollen[i] is { } grains) (PollenLabels[i], PollenSlots[i]) = AirQualityScale.PollenLevel(grains);
                else (PollenLabels[i], PollenSlots[i]) = ("--", AirQualityScale.Neutral);
            }
        }
        else
        {
            PollenLabels = NoPollen;
            PollenSlots = [AirQualityScale.Neutral, AirQualityScale.Neutral, AirQualityScale.Neutral];
        }

        Hourly = s.Hourly.ToArray();
        float peak = Hourly.Length > 0 ? Hourly.Max() : 0;
        ChartMax = MathF.Max(40f, MathF.Ceiling(peak / 10f) * 10f);
        TrendText = "PEAK " + Math.Round(peak);
    }

    private static string Fmt(double v) => v < 10 ? v.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(v).ToString(CultureInfo.InvariantCulture);
}

/// <summary>Text styles of the air quality screen: one per colour slot (the six bands, then neutral). Created in <c>Build</c>, once fonts are loaded.</summary>
internal sealed class AirQualityStyles
{
    public const int Slots = 7;

    public readonly TextStyle[] Digits = new TextStyle[Slots];
    public readonly TextStyle[] Small = new TextStyle[Slots];
    public readonly TextStyle[] Tiny = new TextStyle[Slots];

    public readonly TextStyle Place = new(Fonts.QuiteSmall, new Pixel(200, 215, 245), Shadow: false);
    public readonly TextStyle PlaceStale = new(Fonts.QuiteSmall, new Pixel(255, 176, 0), Shadow: false);
    public readonly TextStyle Muted = new(Fonts.QuiteSmall, new Pixel(150, 150, 160), Shadow: false);
    public readonly TextStyle Message = new(Fonts.Small, new Pixel(255, 176, 0), Shadow: false);
    public readonly TextStyle PillText = new(Fonts.QuiteSmall, Pixel.White, Shadow: false);

    public AirQualityStyles()
    {
        for (int i = 0; i < Slots; i++)
        {
            var color = i < Slots - 1 ? AirQualityScale.ColorOf((AqiBand)i) : AirQualityScale.NeutralColor;
            Digits[i] = new TextStyle(WeatherFonts.Digits, color);
            Small[i] = new TextStyle(Fonts.Small, color, Shadow: false);
            Tiny[i] = new TextStyle(Fonts.QuiteSmall, color, Shadow: false);
        }
    }
}
