using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Tests;

/// <summary>Drives a generative app with a fixed-step clock and grabs frames; also writes contact sheets when VISUALS_DUMP is set.</summary>
internal sealed class VisualDriver
{
    private long _frame;

    public VisualDriver(IMatrixApp app, int stepMs = 16)
    {
        Fonts.Load();
        App = app;
        StepMs = stepMs;
        app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None).GetAwaiter().GetResult();
    }

    public IMatrixApp App { get; }
    public int StepMs { get; }
    public TimeSpan Time { get; private set; }
    public FrameBuffer Frame { get; } = new(256, 64);

    public void Step(int count = 1, Action? beforeEach = null)
    {
        var delta = TimeSpan.FromMilliseconds(StepMs);
        for (int i = 0; i < count; i++)
        {
            beforeEach?.Invoke();
            Time += delta;
            App.Update(new FrameContext(Time, delta, _frame++), CancellationToken.None);
        }
    }

    /// <summary>Advances to <paramref name="until"/> and returns a copy of the rendered frame.</summary>
    public FrameBuffer RunTo(TimeSpan until, Action? beforeEach = null)
    {
        Step(Math.Max(0, (int)((until - Time).TotalMilliseconds / StepMs)), beforeEach);
        return Render();
    }

    public FrameBuffer Render()
    {
        Frame.Clear(Pixel.Black);
        App.Render(Frame, CancellationToken.None);
        var copy = new FrameBuffer(Frame.Width, Frame.Height);
        copy.CopyFrom(Frame);
        return copy;
    }

    public static bool Same(FrameBuffer a, FrameBuffer b) => a.GetPixelsSpan().SequenceEqual(b.GetPixelsSpan());

    /// <summary>Fraction of pixels that are not black.</summary>
    public static double Coverage(FrameBuffer f)
    {
        int lit = 0;
        foreach (var p in f.GetPixelsSpan()) if (p.R + p.G + p.B > 24) lit++;
        return lit / (double)(f.Width * f.Height);
    }

    public static int Lit(FrameBuffer f)
    {
        int lit = 0;
        foreach (var p in f.GetPixelsSpan()) if (p.R + p.G + p.B > 0) lit++;
        return lit;
    }

    /// <summary>Settings round-trip helper: pushes a persisted-style JsonElement through UpdateSetting.</summary>
    public static System.Text.Json.JsonElement Json(string raw) => System.Text.Json.JsonDocument.Parse(raw).RootElement.Clone();

    public static T Setting<T>(IConfigurableApp app, string key) =>
        (T)Convert.ChangeType(app.GetSettings().Single(s => s.Key == key).CurrentValue!, typeof(T));

    /// <summary>With VISUALS_DUMP=dir, writes a vertical contact sheet (3x nearest scale + an LED-dot render of the last frame).</summary>
    public static void DumpSheet(string name, params FrameBuffer[] frames)
    {
        var dir = Environment.GetEnvironmentVariable("VISUALS_DUMP");
        if (string.IsNullOrEmpty(dir) || frames.Length == 0) return;
        Directory.CreateDirectory(dir);
        const int s = 3;
        int w = frames[0].Width, h = frames[0].Height;
        using var sheet = new Image<Rgba32>(w * s, (h * s + 4) * frames.Length);
        for (int i = 0; i < frames.Length; i++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = frames[i].GetPixel(x, y);
                    var c = new Rgba32(p.R, p.G, p.B, 255);
                    for (int dy = 0; dy < s; dy++)
                        for (int dx = 0; dx < s; dx++)
                            sheet[x * s + dx, i * (h * s + 4) + y * s + dy] = c;
                }
        sheet.SaveAsPng(Path.Combine(dir, name + ".png"));

        // LED look: round lit dot per pixel on a dark panel, with a mild gamma so mid tones read like an LED panel does.
        const int d = 4;
        var last = frames[^1];
        using var led = new Image<Rgba32>(w * d, h * d);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = last.GetPixel(x, y);
                for (int dy = 0; dy < d; dy++)
                    for (int dx = 0; dx < d; dx++)
                    {
                        bool corner = (dx == 0 || dx == d - 1) && (dy == 0 || dy == d - 1);
                        float k = corner ? 0.05f : (dx == 0 || dy == 0) ? 0.55f : 1f;
                        led[x * d + dx, y * d + dy] = new Rgba32((byte)(p.R * k), (byte)(p.G * k), (byte)(p.B * k), 255);
                    }
            }
        led.SaveAsPng(Path.Combine(dir, name + "_led.png"));
    }
}
