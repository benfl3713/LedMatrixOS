using LedMatrixOS.Core;

namespace LedMatrixOS.Apps;

/// <summary>
/// Line colours for the TfL network, keyed by TfL line id (case-insensitive).
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

    public static bool TryGet(string lineId, out Pixel color) => Lines.TryGetValue(lineId, out color);
}
