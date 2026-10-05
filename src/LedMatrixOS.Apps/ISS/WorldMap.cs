using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.ISS;

/// <summary>
/// The world map as a small cell grid, decoded once from <see cref="WorldMapData"/>: land, coast (land next to sea) and a faint graticule on the sea.
/// Equirectangular, so a longitude/latitude is a straight scale to a column/row.
/// </summary>
internal static class WorldMap
{
    public const int Width = WorldMapData.Columns, Height = WorldMapData.Rows;
    public const double TopLat = 80.0;
    public const double DegPerCell = 360.0 / Width;

    public const byte Ocean = 0, Land = 1, Coast = 2, Grid = 3;

    private static readonly byte[] Cells = Decode();
    private static readonly double[] RowSin = new double[Height], RowCos = new double[Height], ColSin = new double[Width], ColCos = new double[Width];

    static WorldMap()
    {
        for (int y = 0; y < Height; y++)
        {
            double lat = (TopLat - (y + 0.5) * DegPerCell) * Math.PI / 180;
            RowSin[y] = Math.Sin(lat);
            RowCos[y] = Math.Cos(lat);
        }
        for (int x = 0; x < Width; x++)
        {
            double lon = (-180 + (x + 0.5) * DegPerCell) * Math.PI / 180;
            ColSin[x] = Math.Sin(lon);
            ColCos[x] = Math.Cos(lon);
        }
    }

    public static byte CellAt(int x, int y) => Cells[y * Width + x];

    public static bool IsLand(double lat, double lon)
    {
        if (lat > TopLat || lat <= TopLat - Height * DegPerCell) return false;
        return Cells[RowOf(lat) * Width + ColumnOf(lon)] is Land or Coast;
    }

    public static int ColumnOf(double lon) => Math.Clamp((int)Math.Floor((lon + 180.0) / DegPerCell), 0, Width - 1);

    /// <summary>The row of a latitude, clamped to the map.</summary>
    public static int RowOf(double lat) => Math.Clamp((int)Math.Floor((TopLat - lat) / DegPerCell), 0, Height - 1);

    /// <summary>
    /// Fills <paramref name="shade"/> (Width x Height) with 0 for day, 1 for twilight and 2 for night, given where the sun is overhead.
    /// Unknown sun (NaN) leaves everything in daylight. No allocation.
    /// </summary>
    public static void ShadeNight(double solarLat, double solarLon, byte[] shade)
    {
        if (double.IsNaN(solarLat) || double.IsNaN(solarLon))
        {
            Array.Clear(shade);
            return;
        }

        double dec = solarLat * Math.PI / 180, lon0 = solarLon * Math.PI / 180;
        double sinD = Math.Sin(dec), cosD = Math.Cos(dec), cosL0 = Math.Cos(lon0), sinL0 = Math.Sin(lon0);
        for (int y = 0; y < Height; y++)
        {
            double a = RowSin[y] * sinD, b = RowCos[y] * cosD;
            int o = y * Width;
            for (int x = 0; x < Width; x++)
            {
                double elevation = a + b * (ColCos[x] * cosL0 + ColSin[x] * sinL0);   // sine of the sun's height
                shade[o + x] = elevation > 0.06 ? (byte)0 : elevation > -0.06 ? (byte)1 : (byte)2;
            }
        }
    }

    private static byte[] Decode()
    {
        var lines = WorldMapData.Mask.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cells = new byte[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                cells[y * Width + x] = lines[y][x] == '#' ? Land : Ocean;

        // coast: land with sea on any of its four sides
        var coast = new byte[cells.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (cells[y * Width + x] != Land) continue;
                bool edge = x == 0 || x == Width - 1 || y == 0 || y == Height - 1 ||
                    cells[y * Width + x - 1] == Ocean || cells[y * Width + x + 1] == Ocean ||
                    cells[(y - 1) * Width + x] == Ocean || cells[(y + 1) * Width + x] == Ocean;
                coast[y * Width + x] = edge ? Coast : Land;
            }

        // a dotted graticule every 30 degrees over the sea
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (coast[y * Width + x] != Ocean) continue;
                double lonA = -180 + x * DegPerCell, lonB = lonA + DegPerCell;
                double latA = TopLat - y * DegPerCell, latB = latA - DegPerCell;
                bool meridian = Math.Floor(lonB / 30) > Math.Floor(lonA / 30) && y % 2 == 0;
                bool parallel = Math.Floor(latA / 30) > Math.Floor(latB / 30) && x % 2 == 0;
                if (meridian || parallel) coast[y * Width + x] = Grid;
            }

        return coast;
    }

    // Palette: [kind * 3 + shade], shade 0 day, 1 twilight, 2 night.
    public static readonly Pixel[] Palette = BuildPalette();

    private static Pixel[] BuildPalette()
    {
        Pixel[] day = [new(8, 30, 70), new(46, 128, 78), new(120, 190, 120), new(18, 52, 100)];
        Pixel[] night = [new(2, 7, 18), new(12, 34, 30), new(34, 76, 62), new(5, 16, 36)];
        var palette = new Pixel[12];
        for (int k = 0; k < 4; k++)
        {
            palette[k * 3] = day[k];
            palette[k * 3 + 1] = Pixel.Lerp(day[k], night[k], 0.5f);
            palette[k * 3 + 2] = night[k];
        }
        return palette;
    }
}
