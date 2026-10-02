using LedMatrixOS.Core;

namespace LedMatrixOS.Apps;

/// <summary>
/// Line colours for the TfL network, keyed by TfL line id (case-insensitive).
/// <para>
/// <see cref="Lines"/> holds the authentic brand colours. A few of them are too dark to read on an LED panel (Piccadilly blue is almost black),
/// so <see cref="Display"/> returns a lifted version that keeps the hue but guarantees a minimum luminance; use it for anything small or thin.
/// </para>
/// </summary>
public static class TubeColors
{
    public static readonly IReadOnlyDictionary<string, Pixel> Lines = new Dictionary<string, Pixel>(StringComparer.OrdinalIgnoreCase)
    {
        { "bakerloo",         new Pixel(156, 105, 56)  },
        { "central",          new Pixel(220, 36,  35)  },
        { "circle",           new Pixel(255, 206, 0)   },
        { "district",         new Pixel(0,   114, 41)  },
        { "hammersmith-city", new Pixel(215, 153, 175) },
        { "jubilee",          new Pixel(161, 165, 167) },
        { "metropolitan",     new Pixel(155, 0,   88)  },
        { "northern",         new Pixel(90,  90,  90)  },
        { "piccadilly",       new Pixel(0,   24,  168) },
        { "victoria",         new Pixel(0,   160, 226) },
        { "waterloo-city",    new Pixel(100, 200, 150) },
        { "dlr",              new Pixel(0,   175, 173) },
        { "elizabeth",        new Pixel(126, 91,  198) },
        { "overground",       new Pixel(232, 106, 16)  },
    };

    private static readonly IReadOnlyDictionary<string, string> Abbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "bakerloo",         "BA" },
        { "central",          "CE" },
        { "circle",           "CI" },
        { "district",         "DI" },
        { "hammersmith-city", "HC" },
        { "jubilee",          "JU" },
        { "metropolitan",     "ME" },
        { "northern",         "NO" },
        { "piccadilly",       "PI" },
        { "victoria",         "VI" },
        { "waterloo-city",    "WC" },
        { "dlr",              "DL" },
        { "elizabeth",        "EL" },
        { "overground",       "OV" },
    };

    private static readonly Pixel Unknown = new(120, 120, 120);

    public static bool TryGet(string lineId, out Pixel color) => Lines.TryGetValue(Normalize(lineId), out color);

    /// <summary>Maps the TfL line ids that differ from the keys of <see cref="Lines"/> (london-overground, elizabeth-line) onto them.</summary>
    public static string Normalize(string? lineId)
    {
        if (string.IsNullOrWhiteSpace(lineId)) return string.Empty;
        var id = lineId.Trim().ToLowerInvariant();
        return id switch
        {
            "london-overground" => "overground",
            "elizabeth-line" => "elizabeth",
            "tflrail" => "elizabeth",
            _ => id,
        };
    }

    /// <summary>The brand colour of a line, or a neutral grey for lines TfL adds that are not listed here.</summary>
    public static Pixel Get(string? lineId) => Lines.TryGetValue(Normalize(lineId), out var c) ? c : Unknown;

    /// <summary>The brand colour lifted so it stays readable on an LED panel (see <see cref="Lift"/>).</summary>
    public static Pixel Display(string? lineId) => Lift(Get(lineId), 90f);

    /// <summary>
    /// Blends <paramref name="color"/> toward white just far enough to reach <paramref name="minLuma"/> (0-255); brighter colours are returned untouched.
    /// </summary>
    public static Pixel Lift(Pixel color, float minLuma)
    {
        float luma = Luma(color);
        if (luma >= minLuma) return color;
        float t = (minLuma - luma) / Math.Max(1f, 255f - luma);
        return Pixel.Lerp(color, Pixel.White, t);
    }

    /// <summary>Perceived brightness, 0-255.</summary>
    public static float Luma(Pixel c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

    /// <summary>Two letter code used on small pills, e.g. "VI" for Victoria.</summary>
    public static string Abbreviation(string? lineId, string? fallbackName = null)
    {
        if (Abbreviations.TryGetValue(Normalize(lineId), out var abbreviation)) return abbreviation;
        var source = !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : lineId;
        if (string.IsNullOrWhiteSpace(source)) return "??";
        source = source.Trim();
        return source[..Math.Min(2, source.Length)].ToUpperInvariant();
    }

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    public static Pixel TextOn(Pixel background) => Luma(background) > 125f ? Pixel.Black : Pixel.White;
}
