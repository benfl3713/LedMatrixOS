using System.Globalization;
using System.Text.RegularExpressions;

namespace LedMatrixOS.Apps.TrainSim;

public enum SpeedAspect { Unknown, Stop, Yellow, DoubleYellow, Green }

public enum SpeedState { Normal, Amber, Red }

/// <summary>Pure formatting and unit helpers for the Train Sim display.</summary>
public static partial class TrainSimFormat
{
    public const double MsToMph = 2.2369362921, MsToKmh = 3.6, MetresToYards = 1.0936133, MetresPerMile = 1609.344;

    /// <summary>How far over the limit still counts as "just over" (amber): 2 mph.</summary>
    public const double AmberOverMs = 2 / MsToMph;

    public static int Speed(double ms, bool mph) => (int)Math.Round(Math.Max(0, ms) * (mph ? MsToMph : MsToKmh), MidpointRounding.AwayFromZero);

    public static SpeedState Classify(double speedMs, double? limitMs)
    {
        if (limitMs is not { } limit) return SpeedState.Normal;
        double over = speedMs - limit;
        return over <= 0.1 ? SpeedState.Normal : over <= AmberOverMs ? SpeedState.Amber : SpeedState.Red;
    }

    /// <summary>"220y" or "1.2mi" (mph), "850m" or "1.2km" (km/h). Close distances are rounded so the text does not flicker.</summary>
    public static string Distance(double metres, bool mph)
    {
        metres = Math.Max(0, metres);
        if (mph)
        {
            double yards = metres * MetresToYards;
            if (yards < 1000) return RoundTo(yards).ToString(CultureInfo.InvariantCulture) + "y";
            return (metres / MetresPerMile).ToString("0.0", CultureInfo.InvariantCulture) + "mi";
        }

        if (metres < 1000) return RoundTo(metres).ToString(CultureInfo.InvariantCulture) + "m";
        return (metres / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "km";
    }

    private static int RoundTo(double value) => value < 100 ? (int)Math.Round(value / 5) * 5 : (int)Math.Round(value / 10) * 10;

    /// <summary>Dwell time as "m:ss".</summary>
    public static string Countdown(double seconds)
    {
        int s = (int)Math.Ceiling(Math.Max(0, seconds));
        return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>The game clock as "HH:mm": the game's UTC time plus its GMT offset. Empty when the game gave no time.</summary>
    /// <summary>HH:mm of the game clock. <c>localTimeIso</c> is already route-local (its offset matches gmtOffset), so this is its wall-clock time.</summary>
    public static string Clock(DateTimeOffset? worldTime) =>
        worldTime is { } t ? t.DateTime.ToString("HH:mm", CultureInfo.InvariantCulture) : "";

    public static SpeedAspect Aspect(string? aspect)
    {
        if (string.IsNullOrWhiteSpace(aspect)) return SpeedAspect.Unknown;
        var a = aspect.Trim();
        if (a.Equals("Stop", StringComparison.OrdinalIgnoreCase) || a.Equals("Danger", StringComparison.OrdinalIgnoreCase)) return SpeedAspect.Stop;
        if (a.Equals("Approach_1", StringComparison.OrdinalIgnoreCase)) return SpeedAspect.Yellow;
        if (a.Equals("Approach_2", StringComparison.OrdinalIgnoreCase)) return SpeedAspect.DoubleYellow;
        if (a.Equals("Clear", StringComparison.OrdinalIgnoreCase)) return SpeedAspect.Green;
        return SpeedAspect.Unknown;
    }

    public static string AspectWord(SpeedAspect aspect) => aspect switch
    {
        SpeedAspect.Stop => "STOP",
        SpeedAspect.Yellow => "CAUTION",
        SpeedAspect.DoubleYellow => "PREPARE",
        SpeedAspect.Green => "CLEAR",
        _ => "SIGNAL",
    };

    /// <summary>"RVM_DEMO_Class805_C" gives "Class 805"; other names lose the prefix and the "_C" suffix and have underscores turned into spaces.</summary>
    public static string LocoName(string? objectClass)
    {
        if (string.IsNullOrWhiteSpace(objectClass)) return "";
        var m = ClassNumber().Match(objectClass);
        if (m.Success) return "Class " + m.Groups[1].Value;

        var name = objectClass.Trim();
        if (name.EndsWith("_C", StringComparison.Ordinal)) name = name[..^2];
        var parts = new List<string>(name.Split('_', StringSplitOptions.RemoveEmptyEntries));
        while (parts.Count > 1 && parts[0].Length <= 4 && parts[0].All(c => char.IsUpper(c) || char.IsDigit(c))) parts.RemoveAt(0);
        return string.Join(' ', parts);
    }

    [GeneratedRegex(@"Class[_ ]?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ClassNumber();
}
