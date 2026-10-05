using System.Runtime.CompilerServices;
using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Alive;

/// <summary>Per-frame settings and shared services handed to every simulation. One instance, mutated in place.</summary>
internal sealed class AliveContext(Random rng)
{
    public Random Rng { get; } = rng;
    public AlivePalette Palette { get; set; } = AlivePalette.Create("Neon");
    public int Speed { get; set; } = 5;
    public int Population { get; set; } = 5;
    public int Trail { get; set; } = 5;
    public bool Predator { get; set; } = true;
    /// <summary>Preset / scene picked in the app settings; "Auto" lets the simulation choose.</summary>
    public string Variant { get; set; } = "Auto";
}

/// <summary>A self-running toy. Buffers are allocated in <see cref="Resize"/> and never again.</summary>
internal interface IAliveSim
{
    string Name { get; }
    /// <summary>Total simulation steps since the last seed.</summary>
    int Ticks { get; }
    /// <summary>True once the picture is empty, frozen or has run its course; the host then reseeds.</summary>
    bool Stagnant { get; }
    void Resize(int width, int height);
    void Seed(AliveContext c);
    /// <summary>Test hook: empties the world as if everything died.</summary>
    void Wipe();
    void Step(float dt, AliveContext c);
    void Draw(FrameBuffer frame, Rectangle bounds, AliveContext c);
}

internal sealed class AlivePalette
{
    public ColorRamp Ramp { get; } = new();
    public ColorRamp Cycle { get; } = new();
    public Pixel Sand, Water, Stone, Fire, Plant;
    public bool Rainbow { get; private set; }

    public static AlivePalette Create(string name)
    {
        var p = new AlivePalette();
        switch (name)
        {
            case "Fire":
                p.Ramp.Fill([(0f, Pixel.Black), (0.3f, new Pixel(120, 10, 0)), (0.6f, new Pixel(255, 110, 0)), (0.85f, new Pixel(255, 220, 60)), (1f, new Pixel(255, 255, 220))]);
                p.Cycle.Fill([(0f, new Pixel(255, 60, 0)), (0.5f, new Pixel(255, 200, 40)), (1f, new Pixel(255, 60, 0))]);
                p.Sand = new Pixel(240, 140, 30); p.Water = new Pixel(210, 50, 20); p.Stone = new Pixel(100, 70, 60);
                p.Fire = new Pixel(255, 240, 150); p.Plant = new Pixel(170, 60, 10);
                break;
            case "Ocean":
                p.Ramp.Fill([(0f, Pixel.Black), (0.3f, new Pixel(0, 20, 110)), (0.6f, new Pixel(0, 120, 220)), (0.85f, new Pixel(80, 230, 255)), (1f, Pixel.White)]);
                p.Cycle.Fill([(0f, new Pixel(30, 100, 255)), (0.33f, new Pixel(0, 230, 230)), (0.66f, new Pixel(120, 160, 255)), (1f, new Pixel(30, 100, 255))]);
                p.Sand = new Pixel(190, 220, 170); p.Water = new Pixel(20, 110, 230); p.Stone = new Pixel(70, 90, 120);
                p.Fire = new Pixel(255, 200, 90); p.Plant = new Pixel(30, 200, 170);
                break;
            case "Forest":
                p.Ramp.Fill([(0f, Pixel.Black), (0.3f, new Pixel(0, 50, 10)), (0.6f, new Pixel(30, 170, 50)), (0.85f, new Pixel(170, 255, 60)), (1f, new Pixel(255, 255, 190))]);
                p.Cycle.Fill([(0f, new Pixel(40, 200, 60)), (0.33f, new Pixel(190, 255, 60)), (0.66f, new Pixel(0, 170, 130)), (1f, new Pixel(40, 200, 60))]);
                p.Sand = new Pixel(200, 170, 90); p.Water = new Pixel(50, 140, 230); p.Stone = new Pixel(110, 110, 100);
                p.Fire = new Pixel(255, 150, 30); p.Plant = new Pixel(40, 200, 60);
                break;
            case "Mono":
                p.Ramp.Fill([(0f, Pixel.Black), (1f, Pixel.White)]);
                p.Cycle.Fill([(0f, new Pixel(110, 110, 110)), (0.5f, Pixel.White), (1f, new Pixel(110, 110, 110))]);
                p.Sand = new Pixel(220, 220, 220); p.Water = new Pixel(110, 110, 110); p.Stone = new Pixel(60, 60, 60);
                p.Fire = Pixel.White; p.Plant = new Pixel(170, 170, 170);
                break;
            case "Rainbow":
                for (int i = 0; i < ColorRamp.Size; i++)
                {
                    float t = i / (float)(ColorRamp.Size - 1);
                    p.Ramp.Colors[i] = Pixel.FromHsv(t * 300f, 1f - MathF.Max(0f, (t - 0.9f) * 8f), MathF.Min(1f, t * 3.5f));
                    p.Cycle.Colors[i] = Pixel.FromHsv(i * 360f / ColorRamp.Size, 1f, 1f);
                }
                p.Sand = new Pixel(255, 200, 0); p.Water = new Pixel(0, 150, 255); p.Stone = new Pixel(130, 130, 150);
                p.Fire = new Pixel(255, 80, 0); p.Plant = new Pixel(60, 255, 80);
                p.Rainbow = true;
                break;
            default: // Neon
                p.Ramp.Fill([(0f, Pixel.Black), (0.25f, new Pixel(60, 0, 110)), (0.5f, new Pixel(255, 40, 200)), (0.78f, new Pixel(40, 220, 255)), (1f, Pixel.White)]);
                p.Cycle.Fill([(0f, new Pixel(255, 40, 200)), (0.33f, new Pixel(40, 220, 255)), (0.66f, new Pixel(60, 255, 140)), (1f, new Pixel(255, 40, 200))]);
                p.Sand = new Pixel(255, 60, 200); p.Water = new Pixel(40, 200, 255); p.Stone = new Pixel(110, 90, 150);
                p.Fire = new Pixel(255, 230, 120); p.Plant = new Pixel(60, 255, 130);
                break;
        }
        return p;
    }
}

/// <summary>Draws a low-resolution float field (value 0..1 through a colour ramp) onto the display with bilinear upscaling.</summary>
internal sealed class FieldUpscaler
{
    private int[] _x0 = [], _x1 = [], _y0 = [], _y1 = [];
    private float[] _fx = [], _fy = [];
    private int _gw;

    public void Resize(int w, int h, int gw, int gh)
    {
        _gw = gw;
        Build(w, gw, out _x0, out _x1, out _fx);
        Build(h, gh, out _y0, out _y1, out _fy);
    }

    private static void Build(int n, int g, out int[] i0, out int[] i1, out float[] f)
    {
        i0 = new int[n]; i1 = new int[n]; f = new float[n];
        for (int i = 0; i < n; i++)
        {
            float s = (i + 0.5f) * g / n - 0.5f;
            int a = (int)MathF.Floor(s);
            f[i] = s - a;
            i0[i] = Math.Clamp(a, 0, g - 1);
            i1[i] = Math.Clamp(a + 1, 0, g - 1);
        }
    }

    /// <summary>Samples <paramref name="src"/> (gw x gh, row major) times <paramref name="gain"/> through <paramref name="ramp"/>.</summary>
    public void Draw(FrameBuffer frame, Rectangle bounds, float[] src, ColorRamp ramp, float gain)
    {
        var colors = ramp.Colors;
        int w = _x0.Length, h = _y0.Length, gw = _gw;
        float scale = gain * (ColorRamp.Size - 1);
        for (int y = 0; y < h; y++)
        {
            int r0 = _y0[y] * gw, r1 = _y1[y] * gw;
            float fy = _fy[y];
            for (int x = 0; x < w; x++)
            {
                int xa = _x0[x], xb = _x1[x];
                float fx = _fx[x];
                float top = src[r0 + xa] + (src[r0 + xb] - src[r0 + xa]) * fx;
                float bot = src[r1 + xa] + (src[r1 + xb] - src[r1 + xa]) * fx;
                int idx = (int)((top + (bot - top) * fy) * scale + 0.5f);
                if (idx <= 0) continue;
                frame.SetPixel(bounds.X + x, bounds.Y + y, colors[idx >= ColorRamp.Size ? ColorRamp.Size - 1 : idx]);
            }
        }
    }
}

internal static class AliveMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Wrap(float v, float size) => v < 0f ? v + size : v >= size ? v - size : v;

    /// <summary>Shortest signed distance on a torus.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Delta(float d, float size, float half) => d > half ? d - size : d < -half ? d + size : d;
}
