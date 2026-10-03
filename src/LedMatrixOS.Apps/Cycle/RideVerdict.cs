using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Cycle;

/// <summary>The answer to "ride or Tube?", from best to worst conditions for cycling.</summary>
public enum Ride { Good, Ok, Wet, Windy, Avoid }

/// <summary>
/// The pure decision behind the Cycle Hub verdict card: rain probability over the next two hours, wind and temperature in, one
/// <see cref="Ride"/> out. All thresholds are named constants so the rule reads (and tests) like a table. Wind is km/h, temperature Celsius.
/// </summary>
public static class RideVerdict
{
    /// <summary>Rain chance (%) from which it is clearly a wet ride.</summary>
    public const int WetRain = 40;

    /// <summary>Rain chance (%) from which the ride is only fair.</summary>
    public const int DampRain = 20;

    /// <summary>Rain chance (%) that on its own makes the Tube the better idea.</summary>
    public const int AvoidRain = 70;

    /// <summary>Wind (km/h) that makes cycling hard work.</summary>
    public const double WindyKmh = 30;

    /// <summary>Breezy: noticeable, but still fine.</summary>
    public const double BreezyKmh = 20;

    /// <summary>Wind (km/h) that is simply unsafe on a heavy hire bike.</summary>
    public const double AvoidWindKmh = 45;

    /// <summary>Rain chance (%) that, together with <see cref="WindyKmh"/>, is miserable enough to avoid.</summary>
    public const int AvoidCombinedRain = 50;

    /// <summary>At or below this (Celsius) roads may be icy.</summary>
    public const double FreezingC = 0;

    /// <summary>Below this (Celsius) it is cold but rideable.</summary>
    public const double ChillyC = 5;

    /// <summary>Above this (Celsius) it is uncomfortably hot but rideable.</summary>
    public const double HotC = 32;

    /// <summary>How many forecast hours ahead the verdict looks (the current hour and the next).</summary>
    public const int VerdictHours = 2;

    /// <summary>How many forecast hours the rain sparkline covers.</summary>
    public const int ChartHours = 6;

    public const double MphToKmh = 1.609344;

    public static Ride Evaluate(int rainChance, double windKmh, double tempC)
    {
        if (rainChance >= AvoidRain || windKmh >= AvoidWindKmh || tempC <= FreezingC) return Ride.Avoid;
        if (rainChance >= AvoidCombinedRain && windKmh >= WindyKmh) return Ride.Avoid;
        if (rainChance >= WetRain) return Ride.Wet;
        if (windKmh >= WindyKmh) return Ride.Windy;
        if (rainChance >= DampRain || windKmh >= BreezyKmh || tempC < ChillyC || tempC > HotC) return Ride.Ok;
        return Ride.Good;
    }

    public static string Label(Ride ride) => ride switch
    {
        Ride.Good => "GOOD",
        Ride.Ok => "OK",
        Ride.Wet => "WET",
        Ride.Windy => "WINDY",
        _ => "AVOID",
    };

    public static Pixel ColorOf(Ride ride) => ride switch
    {
        Ride.Good => new Pixel(0, 200, 90),
        Ride.Ok => new Pixel(210, 200, 40),
        Ride.Wet => new Pixel(70, 150, 255),
        Ride.Windy => new Pixel(0, 190, 200),
        _ => new Pixel(255, 60, 50),
    };
}

/// <summary>What the verdict card shows for one forecast: derived once per snapshot so rendering allocates nothing.</summary>
internal sealed class RideOutlook
{
    private RideOutlook(Ride ride, float[] rain, string windText)
    {
        Ride = ride;
        Rain = rain;
        WindText = windText;
    }

    public Ride Ride { get; }

    /// <summary>Rain probability (%) for the next <see cref="RideVerdict.ChartHours"/> hours.</summary>
    public float[] Rain { get; }

    public string WindText { get; }

    public static RideOutlook From(WeatherSnapshot snap)
    {
        var hours = snap.Hourly;
        int rainNow = 0;
        double wind = snap.WindSpeed;
        for (int i = 0; i < Math.Min(RideVerdict.VerdictHours, hours.Count); i++)
        {
            rainNow = Math.Max(rainNow, hours[i].PrecipChance);
            wind = Math.Max(wind, hours[i].Wind);
        }

        double windKmh = snap.Fahrenheit ? wind * RideVerdict.MphToKmh : wind;
        double tempC = snap.Fahrenheit ? (snap.Temp - 32) * 5 / 9 : snap.Temp;

        var rain = new float[Math.Min(RideVerdict.ChartHours, hours.Count)];
        for (int i = 0; i < rain.Length; i++) rain[i] = hours[i].PrecipChance;

        return new RideOutlook(RideVerdict.Evaluate(rainNow, windKmh, tempC), rain,
            $"Wind {Math.Round(wind)} {(snap.Fahrenheit ? "mph" : "km/h")}");
    }
}
