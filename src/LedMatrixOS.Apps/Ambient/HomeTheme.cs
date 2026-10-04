using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Ambient;

/// <summary>The colours of one home-screen mood. A plain struct so a tweened theme change costs nothing per frame.</summary>
internal readonly record struct HomePalette(
    Pixel Sky0, Pixel Sky1, Pixel A1, Pixel A2, Pixel A3, Pixel Star, Pixel Time0, Pixel Time1, Pixel Date, Pixel Accent)
{
    public static HomePalette Lerp(in HomePalette a, in HomePalette b, float t) => new(
        Pixel.Lerp(a.Sky0, b.Sky0, t), Pixel.Lerp(a.Sky1, b.Sky1, t), Pixel.Lerp(a.A1, b.A1, t), Pixel.Lerp(a.A2, b.A2, t),
        Pixel.Lerp(a.A3, b.A3, t), Pixel.Lerp(a.Star, b.Star, t), Pixel.Lerp(a.Time0, b.Time0, t), Pixel.Lerp(a.Time1, b.Time1, t),
        Pixel.Lerp(a.Date, b.Date, t), Pixel.Lerp(a.Accent, b.Accent, t));
}

internal static class HomeThemes
{
    public static readonly string[] Names =
        ["Calm Blue", "Warm Sunset", "Forest Green", "Lavender Dreams", "Monochrome", "Neon Synthwave"];

    private static Pixel P(int r, int g, int b) => new((byte)r, (byte)g, (byte)b);

    // Each theme is a different mood, not a recolour: cool and quiet, golden hour, deep woods, dreamy, stark, and loud.
    public static readonly HomePalette CalmBlue = new(
        Sky0: P(1, 5, 20), Sky1: P(4, 32, 66), A1: P(30, 210, 200), A2: P(70, 120, 255), A3: P(90, 240, 180),
        Star: P(200, 225, 255), Time0: P(240, 250, 255), Time1: P(80, 190, 255), Date: P(130, 205, 255), Accent: P(70, 210, 255));

    public static readonly HomePalette WarmSunset = new(
        Sky0: P(26, 4, 46), Sky1: P(150, 34, 40), A1: P(255, 120, 30), A2: P(255, 50, 110), A3: P(255, 190, 70),
        Star: P(255, 225, 190), Time0: P(255, 246, 218), Time1: P(255, 150, 60), Date: P(255, 190, 120), Accent: P(255, 130, 50));

    public static readonly HomePalette ForestGreen = new(
        Sky0: P(1, 12, 6), Sky1: P(5, 50, 28), A1: P(40, 225, 110), A2: P(160, 255, 60), A3: P(40, 200, 200),
        Star: P(210, 255, 220), Time0: P(232, 255, 226), Time1: P(80, 235, 120), Date: P(140, 235, 150), Accent: P(110, 255, 120));

    public static readonly HomePalette LavenderDreams = new(
        Sky0: P(12, 4, 38), Sky1: P(86, 30, 112), A1: P(175, 110, 255), A2: P(255, 110, 220), A3: P(110, 165, 255),
        Star: P(240, 225, 255), Time0: P(250, 240, 255), Time1: P(205, 135, 255), Date: P(210, 170, 255), Accent: P(200, 130, 255));

    public static readonly HomePalette Monochrome = new(
        Sky0: P(3, 3, 5), Sky1: P(36, 36, 42), A1: P(200, 200, 210), A2: P(120, 120, 132), A3: P(235, 235, 240),
        Star: P(255, 255, 255), Time0: P(255, 255, 255), Time1: P(165, 165, 178), Date: P(175, 175, 185), Accent: P(225, 225, 235));

    public static readonly HomePalette NeonSynthwave = new(
        Sky0: P(8, 0, 22), Sky1: P(78, 0, 84), A1: P(255, 40, 200), A2: P(0, 230, 255), A3: P(150, 80, 255),
        Star: P(255, 200, 255), Time0: P(255, 110, 230), Time1: P(50, 225, 255), Date: P(120, 240, 255), Accent: P(255, 60, 215));

    public static HomePalette Get(string? name) => name switch
    {
        "Warm Sunset" => WarmSunset,
        "Forest Green" => ForestGreen,
        "Lavender Dreams" => LavenderDreams,
        "Monochrome" => Monochrome,
        "Neon Synthwave" => NeonSynthwave,
        _ => CalmBlue,
    };
}

internal enum HomeMode { Aurora, Starfield, Embers, Waves, DaySky, Minimal }

internal static class HomeModes
{
    public static readonly string[] Names = ["Aurora", "Starfield", "Embers", "Waves", "Day Night Sky", "Minimal"];

    /// <summary>Old option values from the previous implementation map onto the closest new scene.</summary>
    public static string Normalize(string? name) => name switch
    {
        "Ambient Particles" => "Embers",
        "Flowing Waves" => "Waves",
        "Geometric Art" => "Aurora",
        "Minimalist" => "Minimal",
        _ => Options.Pick(name, Names, "Aurora"),
    };

    public static HomeMode Parse(string? name) => Normalize(name) switch
    {
        "Starfield" => HomeMode.Starfield,
        "Embers" => HomeMode.Embers,
        "Waves" => HomeMode.Waves,
        "Day Night Sky" => HomeMode.DaySky,
        "Minimal" => HomeMode.Minimal,
        _ => HomeMode.Aurora,
    };
}

/// <summary>What the director computes each frame and the scene nodes read.</summary>
internal sealed class HomeState
{
    public HomePalette Pal = HomeThemes.CalmBlue;
    public HomeMode Mode = HomeMode.Aurora;
    /// <summary>Seconds of animation clock (frame time).</summary>
    public float T;
    public float Dt;
    /// <summary>Ambient animation speed multiplier (1 = default).</summary>
    public float Speed = 1f;
    /// <summary>Local hour of day as a fraction (0-24), from the app's TimeProvider.</summary>
    public float Hour = 12f;
    /// <summary>Position within the current second (0-1).</summary>
    public float SecondFrac;
    /// <summary>Whole seconds of the current minute (0-59).</summary>
    public int Second;
    /// <summary>Bright pulse right after the minute changes, decaying 1 to 0.</summary>
    public float MinuteFlash;
    /// <summary>1 when the clock is left-aligned beside the date, 0 when it is centred; the data chips follow it.</summary>
    public float ChipLeft = 1f;
}
