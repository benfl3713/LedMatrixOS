namespace LedMatrixOS.Apps.ISS;

/// <summary>
/// "What is it over": a tiny offline lookup of coarse countries, regions and seas as lat/lon boxes (first match wins, so the smaller boxes come first).
/// Deliberately approximate: it names the place well enough for a glance, not for borders.
/// </summary>
internal static class IssRegions
{
    private readonly record struct Box(string Name, double MinLat, double MaxLat, double MinLon, double MaxLon);

    private static readonly Box[] LandBoxes =
    [
        new("Iceland", 63, 66.6, -24.5, -13.5),
        new("Ireland", 51.3, 55.5, -10.7, -6),
        new("United Kingdom", 50, 60, -8, 2),
        new("Japan", 31, 45.7, 129.5, 146),
        new("New Zealand", -47.5, -34, 166, 179),
        new("Madagascar", -25.6, -12, 43, 50.6),
        new("Papua New Guinea", -11, -1, 141, 156),
        new("Philippines", 5, 19, 117, 127),
        new("Indonesia", -11, 6, 95, 141),
        new("Greenland", 59, 84, -74, -11),
        new("Cuba", 19.5, 23.5, -85, -74),
        new("Italy", 36, 47, 6.5, 18.6),
        new("Spain", 36, 43.8, -9.5, 3.4),
        new("France", 42.3, 51.2, -5, 8),
        new("Germany", 47.3, 55, 6, 15),
        new("Turkey", 36, 42, 26, 45),
        new("Ukraine", 44, 52.5, 22, 40),
        new("Scandinavia", 55, 71.5, 4, 31),
        new("Egypt", 22, 31.7, 25, 35.5),
        new("South Africa", -35, -22, 16, 33),
        new("Horn of Africa", 3, 15, 33, 52),
        new("Sahara", 15, 37, -17, 25),
        new("West Africa", 4, 15, -18, 8),
        new("Central Africa", -6, 15, 8, 33),
        new("East Africa", -12, 5, 28, 42),
        new("Southern Africa", -35, -6, 12, 41),
        new("Saudi Arabia", 16, 32, 35, 55),
        new("Iran", 25, 40, 44, 63.5),
        new("Pakistan", 24, 37, 61, 75),
        new("India", 8, 35, 68, 89),
        new("Korea", 33, 43, 124, 131),
        new("Mongolia", 42, 52, 87, 120),
        new("Kazakhstan", 41, 55, 47, 87),
        new("China", 18, 53.5, 73, 135),
        new("Southeast Asia", 5, 24, 92, 110),
        new("Russia", 50, 82, 27, 180),
        new("Alaska", 54, 71.5, -168, -141),
        new("Canada", 49, 84, -141, -52),
        new("United States", 25, 49, -125, -67),
        new("Mexico", 14.5, 32.7, -118, -86.5),
        new("Central America", 7, 18, -93, -77),
        new("Venezuela", 0, 12.5, -73, -59),
        new("Colombia", -4, 12.5, -79, -67),
        new("Peru", -18, 0, -81.5, -69),
        new("Bolivia", -23, -10, -69, -58),
        new("Chile", -56, -18, -76, -70),
        new("Argentina", -55, -22, -73, -53),
        new("Brazil", -34, 5, -74, -34),
        new("Australia", -44, -10, 113, 154),
    ];

    private static readonly Box[] SeaBoxes =
    [
        new("Persian Gulf", 24, 30.5, 48, 56.5),
        new("Red Sea", 12, 28, 32, 43.5),
        new("Black Sea", 41, 47, 27.5, 41.8),
        new("Caspian Sea", 36.5, 47, 46.5, 54.5),
        new("Baltic Sea", 54, 66, 10, 30),
        new("North Sea", 51, 61, -3, 8),
        new("Mediterranean Sea", 30, 45.5, -5.5, 36),
        new("Hudson Bay", 51, 64, -95, -78),
        new("Gulf of Mexico", 18, 30.5, -97.5, -81.5),
        new("Caribbean Sea", 9, 22, -88, -60),
        new("Bay of Bengal", 5, 22, 80, 95),
        new("Arabian Sea", 5, 25, 52, 74),
        new("South China Sea", 0, 23, 105, 120),
        new("Sea of Japan", 34, 52, 127, 142),
        new("Coral Sea", -25, -8, 142, 165),
        new("Tasman Sea", -47, -28, 150, 172),
    ];

    /// <summary>A short name for the place under a latitude and longitude.</summary>
    public static string Describe(double lat, double lon)
    {
        lon = ((lon + 180) % 360 + 360) % 360 - 180;
        return WorldMap.IsLand(lat, lon) ? LandName(lat, lon) : SeaName(lat, lon);
    }

    private static string LandName(double lat, double lon)
    {
        foreach (var b in LandBoxes)
            if (Inside(b, lat, lon)) return b.Name;

        if (lat < -60) return "Antarctica";
        if (lon < -30) return lat > 12 ? "North America" : "South America";
        if (lat > 36 && lon < 60) return "Europe";
        if (lon < 52) return "Africa";
        if (lat < -10 && lon > 110) return "Australia";
        return "Asia";
    }

    private static string SeaName(double lat, double lon)
    {
        foreach (var b in SeaBoxes)
            if (Inside(b, lat, lon)) return b.Name;

        if (lat > 66) return "Arctic Ocean";
        if (lat < -52) return "Southern Ocean";
        if (lon >= 20 && lon < 120) return "Indian Ocean";
        bool north = lat >= 0;
        if (lon >= 120 || lon < -70) return north ? "North Pacific" : "South Pacific";
        return north ? "North Atlantic" : "South Atlantic";
    }

    private static bool Inside(Box b, double lat, double lon) => lat >= b.MinLat && lat <= b.MaxLat && lon >= b.MinLon && lon <= b.MaxLon;
}
