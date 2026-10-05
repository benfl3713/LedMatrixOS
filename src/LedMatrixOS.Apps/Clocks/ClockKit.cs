using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps.Clocks;

/// <summary>Colour set shared by the clock apps. G0..G2 are the gradient stops of the digits, Accent is for small details.</summary>
internal sealed record ClockPalette(string Name, Pixel G0, Pixel G1, Pixel G2, Pixel Accent, Pixel BackA, Pixel BackB)
{
    public static readonly ClockPalette Sunset = new("Sunset", new(255, 226, 120), new(255, 120, 80), new(255, 60, 140),
        new(255, 150, 190), new(54, 10, 60), new(70, 18, 14));
    public static readonly ClockPalette Ocean = new("Ocean", new(170, 255, 240), new(60, 190, 255), new(70, 110, 255),
        new(0, 230, 255), new(0, 22, 58), new(0, 52, 66));
    public static readonly ClockPalette Neon = new("Neon", new(255, 90, 230), new(150, 120, 255), new(60, 210, 255),
        new(255, 250, 90), new(40, 0, 56), new(0, 28, 62));
    public static readonly ClockPalette Aurora = new("Aurora", new(150, 255, 170), new(80, 220, 220), new(150, 130, 255),
        new(210, 130, 255), new(0, 40, 36), new(34, 8, 60));
    public static readonly ClockPalette Ember = new("Ember", new(255, 236, 130), new(255, 140, 20), new(255, 60, 20),
        new(255, 200, 60), new(48, 10, 0), new(60, 20, 0));
    public static readonly ClockPalette Mono = new("Mono", new(255, 255, 255), new(225, 232, 250), new(170, 185, 225),
        new(140, 170, 255), new(16, 18, 34), new(8, 10, 22));

    public static readonly string[] Names = ["Sunset", "Ocean", "Neon", "Aurora", "Ember", "Mono"];

    public static ClockPalette ByName(string? name) => name switch
    {
        "Ocean" => Ocean,
        "Neon" => Neon,
        "Aurora" => Aurora,
        "Ember" => Ember,
        "Mono" => Mono,
        _ => Sunset,
    };

    public static Pixel ByColorName(string? name) => name switch
    {
        "White" => new Pixel(255, 255, 255),
        "Red" => new Pixel(255, 40, 40),
        "Green" => new Pixel(40, 255, 70),
        "Blue" => new Pixel(70, 120, 255),
        "Yellow" => new Pixel(255, 225, 40),
        "Cyan" => new Pixel(30, 235, 255),
        "Magenta" => new Pixel(255, 50, 230),
        "Amber" => new Pixel(255, 176, 30),
        "Orange" => new Pixel(255, 110, 20),
        _ => new Pixel(255, 255, 255),
    };
}

/// <summary>
/// The palette in use, tweened when it changes, plus a slow "shift" that slides the digit gradient along the palette stops
/// a little more every minute. Nodes read the resolved colours (updated by <see cref="ThemeNode"/>).
/// </summary>
internal sealed class LiveTheme
{
    private ClockPalette _from, _to;
    private readonly Tween<float> _blend = new(1f);
    private readonly Tween<float> _shift = new(0f);
    private bool _shiftInit;

    public LiveTheme(ClockPalette palette) => _from = _to = palette;

    public ClockPalette Target => _to;

    /// <summary>Optional override of the digit colour (the legacy "timeColor" setting). Null uses the palette gradient.</summary>
    public Pixel? DigitOverride { get; set; }

    public Pixel Top { get; private set; }
    public Pixel Bottom { get; private set; }
    public Pixel Accent { get; private set; }
    public Pixel BackA { get; private set; }
    public Pixel BackB { get; private set; }
    public Pixel G0 { get; private set; }
    public Pixel G1 { get; private set; }
    public Pixel G2 { get; private set; }

    public void SetPalette(ClockPalette palette, Animator? animator)
    {
        if (ReferenceEquals(palette, _to)) return;
        _from = Current();
        _to = palette;
        _blend.Set(0f);
        if (animator is null) _blend.Set(1f);
        else animator.Animate(_blend, 1f, TimeSpan.FromMilliseconds(900), Easing.InOutQuad);
        Refresh();
    }

    /// <summary>Slides the gradient toward <paramref name="target"/> (0..1) with an eased tween (instant on first call).</summary>
    public void ShiftTo(float target, Animator animator)
    {
        if (!_shiftInit)
        {
            _shiftInit = true;
            _shift.Set(target);
        }
        else animator.Animate(_shift, target, TimeSpan.FromMilliseconds(2500), Easing.InOutSine);
    }

    private ClockPalette Current() => _blend.Value >= 1f ? _to : Mix(_from, _to, _blend.Value);

    private static ClockPalette Mix(ClockPalette a, ClockPalette b, float t) => new("mix",
        Pixel.Lerp(a.G0, b.G0, t), Pixel.Lerp(a.G1, b.G1, t), Pixel.Lerp(a.G2, b.G2, t),
        Pixel.Lerp(a.Accent, b.Accent, t), Pixel.Lerp(a.BackA, b.BackA, t), Pixel.Lerp(a.BackB, b.BackB, t));

    public void Refresh()
    {
        float t = _blend.Value;
        Pixel g0 = Pixel.Lerp(_from.G0, _to.G0, t), g1 = Pixel.Lerp(_from.G1, _to.G1, t), g2 = Pixel.Lerp(_from.G2, _to.G2, t);
        G0 = g0; G1 = g1; G2 = g2;
        Accent = Pixel.Lerp(_from.Accent, _to.Accent, t);
        BackA = Pixel.Lerp(_from.BackA, _to.BackA, t);
        BackB = Pixel.Lerp(_from.BackB, _to.BackB, t);
        float s = _shift.Value;
        Top = Pixel.Lerp(g0, g1, s);
        Bottom = Pixel.Lerp(g1, g2, s);
        if (DigitOverride is { } o)
        {
            Top = o;
            Bottom = Pixel.Lerp(o, Pixel.Black, 0.28f);
        }
    }
}

/// <summary>Keeps <see cref="LiveTheme"/> current each frame. Put it first in the tree.</summary>
internal sealed class ThemeNode : Node
{
    private readonly LiveTheme _theme;
    public ThemeNode(LiveTheme theme)
    {
        _theme = theme;
        Visible = true;
        Width = 0;
        Height = 0;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _theme.Refresh();
    }
}

/// <summary>Wall-clock fields for the frame, read from the injected TimeProvider (never DateTime.Now).</summary>
internal sealed class ClockState
{
    private static readonly string[] Two = Enumerable.Range(0, 100).Select(i => i.ToString("00")).ToArray();
    public static readonly string[] DayNames = ["SUNDAY", "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY"];
    public static readonly string[] DayShort = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];
    public static readonly string[] MonthShort = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    private readonly TimeProvider _time;
    private int _dateKey = -1;

    public ClockState(TimeProvider time) => _time = time;

    public int Hour { get; private set; }
    public int Minute { get; private set; }
    public int Second { get; private set; }
    public int Day { get; private set; }
    public int Month { get; private set; }
    public int Year { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    /// <summary>Fraction (0..1) of the current second.</summary>
    public float SecFrac { get; private set; }
    /// <summary>Seconds since midnight including the fraction.</summary>
    public double SecondsOfDay { get; private set; }

    public int Hour12 => Hour % 12 == 0 ? 12 : Hour % 12;
    public bool IsPm => Hour >= 12;
    public string DayName => DayNames[(int)DayOfWeek];
    public string DayShortName => DayShort[(int)DayOfWeek];
    public string MonthName => MonthShort[Month - 1];
    /// <summary>"02 OCT"; rebuilt only when the date changes.</summary>
    public string DateText { get; private set; } = "";
    /// <summary>"THU 02 OCT"; rebuilt only when the date changes.</summary>
    public string LongDateText { get; private set; } = "";

    public static string TwoDigits(int n) => Two[Math.Clamp(n, 0, 99)];

    private TimeZoneInfo? _zone;
    private string _dateFormat = "Weekday Day Month";

    public static readonly string[] DateFormats = ["Weekday Day Month", "DD/MM", "MM/DD"];

    /// <summary>
    /// Picks the time zone (an IANA or Windows id; empty, unknown or invalid ids fall back to the device's local zone) and the date layout.
    /// </summary>
    public void Configure(string? timeZoneId, string? dateFormat)
    {
        _zone = null;
        var id = (timeZoneId ?? "").Trim();
        if (id.Length > 0)
        {
            try { _zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception) { _zone = null; }
        }
        _dateFormat = dateFormat is "DD/MM" or "MM/DD" ? dateFormat : "Weekday Day Month";
        _dateKey = -1;
    }

    public void Refresh()
    {
        var now = _zone is null ? _time.GetLocalNow() : TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone);
        Hour = now.Hour;
        Minute = now.Minute;
        Second = now.Second;
        Day = now.Day;
        Month = now.Month;
        Year = now.Year;
        DayOfWeek = now.DayOfWeek;
        long subTicks = now.Ticks % TimeSpan.TicksPerSecond;
        SecFrac = (float)(subTicks / (double)TimeSpan.TicksPerSecond);
        SecondsOfDay = Hour * 3600 + Minute * 60 + Second + SecFrac;

        int key = Year * 400 + Month * 32 + Day;
        if (key != _dateKey)
        {
            _dateKey = key;
            DateText = _dateFormat switch
            {
                "DD/MM" => $"{TwoDigits(Day)}/{TwoDigits(Month)}",
                "MM/DD" => $"{TwoDigits(Month)}/{TwoDigits(Day)}",
                _ => $"{TwoDigits(Day)} {MonthName}",
            };
            LongDateText = $"{DayShortName} {DateText}";
        }
    }
}

internal sealed class ClockStateNode : Node
{
    private readonly ClockState _state;
    public ClockStateNode(ClockState state)
    {
        _state = state;
        Width = 0;
        Height = 0;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _state.Refresh();
    }
}

/// <summary>Pre-rasterised anti-aliased stroke glyphs (digits 0-9) with a soft halo mask each. Cached per size.</summary>
internal sealed class GlyphAtlas
{
    private static readonly Dictionary<(int, int, float, int), GlyphAtlas> Cache = new();

    public int W { get; }
    public int H { get; }
    public int Pad { get; }
    /// <summary>Row stride of the padded masks.</summary>
    public int Stride => W + Pad * 2;
    public int Rows => H + Pad * 2;
    public byte[][] Ink { get; } = new byte[10][];
    public byte[][] Halo { get; } = new byte[10][];

    private GlyphAtlas(int w, int h, float thickness, int pad)
    {
        W = w;
        H = h;
        Pad = pad;
        for (int d = 0; d < 10; d++)
        {
            var paths = DigitPaths(d, w, h, thickness);
            Ink[d] = Rasterise(paths, thickness / 2f);
            Halo[d] = Blur(Ink[d], Stride, Rows, Math.Max(1, pad));
        }
    }

    public static GlyphAtlas Get(int w, int h, float thickness, int pad)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue((w, h, thickness, pad), out var atlas))
                Cache[(w, h, thickness, pad)] = atlas = new GlyphAtlas(w, h, thickness, pad);
            return atlas;
        }
    }

    private byte[] Rasterise(List<List<(float x, float y)>> paths, float halfWidth)
    {
        int stride = Stride, rows = Rows;
        var mask = new byte[stride * rows];
        var segs = new List<(float x0, float y0, float x1, float y1)>();
        foreach (var p in paths)
            for (int i = 0; i + 1 < p.Count; i++) segs.Add((p[i].x, p[i].y, p[i + 1].x, p[i + 1].y));

        float reach = halfWidth + 1f;
        foreach (var s in segs)
        {
            int xa = (int)MathF.Floor(Math.Min(s.x0, s.x1) - reach) + Pad, xb = (int)MathF.Ceiling(Math.Max(s.x0, s.x1) + reach) + Pad;
            int ya = (int)MathF.Floor(Math.Min(s.y0, s.y1) - reach) + Pad, yb = (int)MathF.Ceiling(Math.Max(s.y0, s.y1) + reach) + Pad;
            for (int py = Math.Max(0, ya); py <= Math.Min(rows - 1, yb); py++)
            {
                for (int px = Math.Max(0, xa); px <= Math.Min(stride - 1, xb); px++)
                {
                    float d = DistToSegment(px - Pad + 0.5f, py - Pad + 0.5f, s.x0, s.y0, s.x1, s.y1);
                    float cov = Math.Clamp(halfWidth + 0.5f - d, 0f, 1f);
                    byte v = (byte)(cov * 255f + 0.5f);
                    int idx = py * stride + px;
                    if (v > mask[idx]) mask[idx] = v;
                }
            }
        }
        return mask;
    }

    private static float DistToSegment(float px, float py, float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len2 = dx * dx + dy * dy;
        float t = len2 <= 1e-6f ? 0f : Math.Clamp(((px - x0) * dx + (py - y0) * dy) / len2, 0f, 1f);
        float cx = x0 + dx * t - px, cy = y0 + dy * t - py;
        return MathF.Sqrt(cx * cx + cy * cy);
    }

    internal static byte[] Blur(byte[] src, int stride, int rows, int radius)
    {
        var a = new float[src.Length];
        for (int i = 0; i < src.Length; i++) a[i] = src[i] / 255f;
        var b = new float[src.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            // horizontal
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < stride; x++)
                {
                    float sum = 0;
                    int n = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int xx = x + k;
                        if (xx < 0 || xx >= stride) { n++; continue; }
                        sum += a[y * stride + xx];
                        n++;
                    }
                    b[y * stride + x] = sum / n;
                }
            // vertical
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < stride; x++)
                {
                    float sum = 0;
                    int n = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int yy = y + k;
                        if (yy < 0 || yy >= rows) { n++; continue; }
                        sum += b[yy * stride + x];
                        n++;
                    }
                    a[y * stride + x] = sum / n;
                }
        }
        var result = new byte[src.Length];
        for (int i = 0; i < result.Length; i++) result[i] = (byte)Math.Clamp(a[i] * 255f * 1.6f + 0.5f, 0f, 255f);
        return result;
    }

    private static void Arc(List<(float x, float y)> p, float cx, float cy, float rx, float ry, float a0, float a1)
    {
        int steps = Math.Max(8, (int)(MathF.Abs(a1 - a0) / 7f));
        for (int i = 0; i <= steps; i++)
        {
            float a = (a0 + (a1 - a0) * i / steps) * MathF.PI / 180f;
            p.Add((cx + rx * MathF.Cos(a), cy + ry * MathF.Sin(a)));
        }
    }

    private static void Bezier(List<(float x, float y)> p, (float x, float y) p0, (float x, float y) p1, (float x, float y) p2, (float x, float y) p3)
    {
        const int steps = 14;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1 - t;
            float x = u * u * u * p0.x + 3 * u * u * t * p1.x + 3 * u * t * t * p2.x + t * t * t * p3.x;
            float y = u * u * u * p0.y + 3 * u * u * t * p1.y + 3 * u * t * t * p2.y + t * t * t * p3.y;
            p.Add((x, y));
        }
    }

    private static List<List<(float x, float y)>> DigitPaths(int digit, int w, int h, float thickness)
    {
        float a = thickness / 2f;
        float x0 = a, x1 = w - a, y0 = a, y1 = h - a;
        float dx = x1 - x0, dy = y1 - y0, r = dx / 2f, cx = w / 2f;
        var paths = new List<List<(float x, float y)>>();
        var p = new List<(float x, float y)>();
        paths.Add(p);

        switch (digit)
        {
            case 0:
                Arc(p, cx, y0 + r, r, r, 180, 360);
                Arc(p, cx, y1 - r, r, r, 0, 180);
                p.Add(p[0]);
                break;
            case 1:
                p.Add((x0 + dx * 0.08f, y0 + dy * 0.17f));
                p.Add((cx + dx * 0.14f, y0));
                p.Add((cx + dx * 0.14f, y1));
                break;
            case 2:
                Arc(p, cx, y0 + r, r, r, 180, 400);
                p.Add((x0, y1));
                p.Add((x1, y1));
                break;
            case 3:
            {
                float ry1 = dy * 0.255f, ry2 = dy * 0.275f;
                Arc(p, cx, y0 + ry1, r * 0.92f, ry1, 205, 450);
                var q = new List<(float x, float y)>();
                paths.Add(q);
                Arc(q, cx, y1 - ry2, r, ry2, 270, 515);
                break;
            }
            case 4:
                p.Add((x0 + dx * 0.74f, y1));
                p.Add((x0 + dx * 0.74f, y0));
                p.Add((x0, y0 + dy * 0.66f));
                p.Add((x1, y0 + dy * 0.66f));
                break;
            case 5:
            {
                float ys = y0 + dy * 0.44f, ry = (y1 - ys) / 2f;
                p.Add((x1 - dx * 0.02f, y0));
                p.Add((x0 + dx * 0.1f, y0));
                p.Add((x0 + dx * 0.06f, ys + ry * 0.25f));
                Arc(p, cx, ys + ry, r, ry, 222, 508);
                break;
            }
            case 6:
            case 9:
            {
                float ry = dy * 0.3f, yc = y1 - ry;
                Bezier(p, (x1 - dx * 0.03f, y0 + dy * 0.1f), (x0 + dx * 0.4f, y0 - dy * 0.0f), (x0, y0 + dy * 0.28f), (x0, yc));
                Arc(p, cx, yc, r, ry, 180, 540);
                if (digit == 9)
                {
                    for (int i = 0; i < p.Count; i++) p[i] = (w - p[i].x, h - p[i].y);
                }
                break;
            }
            case 7:
                p.Add((x0, y0));
                p.Add((x1, y0));
                p.Add((x0 + dx * 0.32f, y1));
                break;
            case 8:
            {
                float ryt = dy * 0.255f, ryb = dy * 0.285f;
                Arc(p, cx, y0 + ryt, r * 0.86f, ryt, 90, 450);
                var q = new List<(float x, float y)>();
                paths.Add(q);
                Arc(q, cx, y1 - ryb, r, ryb, 270, 630);
                break;
            }
        }
        return paths;
    }
}

/// <summary>Soft round dot mask (for colons and sparkles), cached per radius.</summary>
internal static class DotMask
{
    private static readonly Dictionary<(float, int), (byte[] mask, int size)> Cache = new();

    /// <summary>A blurred disc: radius is the core radius, the glow reaches <paramref name="spread"/> pixels beyond it.</summary>
    public static (byte[] mask, int size) GetSoft(float radius, int spread)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((-radius, spread), out var hit)) return hit;
            var (hard, size) = Get(radius, spread + 1);
            var soft = GlyphAtlas.Blur(hard, size, size, Math.Max(1, spread));
            return Cache[(-radius, spread)] = (soft, size);
        }
    }

    public static (byte[] mask, int size) Get(float radius, int pad)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((radius, pad), out var hit)) return hit;
            int size = (int)MathF.Ceiling(radius * 2) + pad * 2;
            var mask = new byte[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    mask[y * size + x] = (byte)(Math.Clamp(radius + 0.5f - d, 0f, 1f) * 255f + 0.5f);
                }
            return Cache[(radius, pad)] = (mask, size);
        }
    }
}

internal static class MaskDraw
{
    /// <summary>Blends <paramref name="color"/> through <paramref name="mask"/> (0..255 coverage) at (x, y).</summary>
    public static void Blit(FrameBuffer frame, byte[] mask, int stride, int rows, int x, int y, Pixel color, float alpha)
    {
        if (alpha <= 0f) return;
        for (int row = 0; row < rows; row++)
        {
            int py = y + row;
            if ((uint)py >= (uint)frame.Height) continue;
            int o = row * stride;
            for (int col = 0; col < stride; col++)
            {
                byte m = mask[o + col];
                if (m == 0) continue;
                float a = m * (1f / 255f) * alpha;
                if (a >= 0.999f) frame.SetPixel(x + col, py, color);
                else frame.BlendPixel(x + col, py, color, a);
            }
        }
    }

    /// <summary>Like <see cref="Blit"/> with a per-row colour, clipping the rows to [clipTop, clipBottom) in screen space.</summary>
    public static void BlitRows(FrameBuffer frame, byte[] mask, int stride, int rows, int x, int y, Pixel[] rowColors, float alpha)
    {
        if (alpha <= 0f) return;
        for (int row = 0; row < rows; row++)
        {
            int py = y + row;
            if ((uint)py >= (uint)frame.Height) continue;
            var color = rowColors[row];
            int o = row * stride;
            for (int col = 0; col < stride; col++)
            {
                byte m = mask[o + col];
                if (m == 0) continue;
                float a = m * (1f / 255f) * alpha;
                if (a >= 0.999f) frame.SetPixel(x + col, py, color);
                else frame.BlendPixel(x + col, py, color, a);
            }
        }
    }
}
