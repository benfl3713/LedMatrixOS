using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>
/// Hosts the effects: owns the palettes and the two scratch pixel buffers, runs the auto-cycle / crossfade state machine, then
/// applies mirror and intensity while copying into the frame. All time comes from the frame delta.
/// </summary>
internal sealed class DemosceneVisual : VisualNode
{
    public const float FadeSeconds = 1.5f;

    private readonly DemosceneApp _app;
    private readonly IDemoVisual[] _effects;
    private readonly ColorRamp[] _ramps;
    private Pixel[] _a = [], _b = [];
    private int _w, _h;
    private float _clock;      // speed-scaled animation time
    private float _cycleTimer; // real seconds on the current effect (auto-cycle)
    private int _cur;
    private int _next = -1;
    private float _fade;

    public DemosceneVisual(DemosceneApp app, int seed)
    {
        _app = app;
        _effects =
        [
            new PlasmaVisual(), new TunnelVisual(), new MetaballsVisual(seed + 1), new StarfieldWarpVisual(seed + 2),
            new LavaLampVisual(seed + 3), new CubeVisual(), new MandelbrotVisual(),
        ];
        _ramps = BuildRamps();
        _cur = Math.Max(0, Array.IndexOf(DemosceneApp.EffectNames, _app.Effect) - 1);
    }

    /// <summary>Index into <see cref="DemosceneApp.EffectNames"/> minus the auto entry, i.e. which effect is on screen.</summary>
    public int CurrentEffect => _cur;
    public bool Fading => _next >= 0;

    private static ColorRamp[] BuildRamps()
    {
        static ColorRamp R(params (float, Pixel)[] s) => ColorRamp.FromStops(s);
        var rainbow = new ColorRamp();
        for (int i = 0; i < ColorRamp.Size; i++) rainbow.Colors[i] = Pixel.FromHsv(i * 300f / (ColorRamp.Size - 1), 1f, 1f);
        return
        [
            R((0f, new Pixel(10, 0, 40)), (0.25f, new Pixel(110, 0, 230)), (0.5f, new Pixel(255, 20, 180)), (0.75f, new Pixel(0, 220, 255)), (1f, new Pixel(230, 255, 255))),
            R((0f, new Pixel(0, 0, 0)), (0.12f, new Pixel(80, 0, 0)), (0.32f, new Pixel(205, 22, 0)), (0.54f, new Pixel(255, 90, 0)), (0.74f, new Pixel(255, 165, 10)), (0.9f, new Pixel(255, 225, 90)), (1f, new Pixel(255, 252, 215))),
            R((0f, new Pixel(0, 5, 30)), (0.25f, new Pixel(0, 40, 120)), (0.5f, new Pixel(0, 120, 200)), (0.75f, new Pixel(40, 210, 230)), (1f, new Pixel(210, 250, 255))),
            R((0f, new Pixel(0, 12, 4)), (0.25f, new Pixel(5, 60, 20)), (0.5f, new Pixel(30, 140, 40)), (0.75f, new Pixel(150, 210, 60)), (1f, new Pixel(235, 250, 170))),
            R((0f, new Pixel(0, 0, 0)), (1f, new Pixel(255, 255, 255))),
            rainbow,
            R((0f, new Pixel(20, 0, 50)), (0.25f, new Pixel(110, 10, 90)), (0.5f, new Pixel(230, 50, 70)), (0.75f, new Pixel(255, 140, 30)), (1f, new Pixel(255, 235, 150))),
        ];
    }

    protected override void Step(FrameContext ctx)
    {
        var host = Host!;
        if (_w != host.Width || _h != host.Height) Resize(host.Width, host.Height);

        _clock += Dt * (0.2f + _app.Speed * 0.16f);

        int wanted = Array.IndexOf(DemosceneApp.EffectNames, _app.Effect) - 1; // -1 = auto-cycle
        if (_next >= 0)
        {
            _fade += Dt / FadeSeconds;
            if (_fade >= 1f) { _cur = _next; _next = -1; _fade = 0f; _cycleTimer = 0f; }
        }
        else if (wanted < 0)
        {
            _cycleTimer += Dt;
            if (_cycleTimer >= _app.SecondsPerEffect) { _next = (_cur + 1) % _effects.Length; _fade = 0f; }
        }
        else if (wanted != _cur) { _next = wanted; _fade = 0f; }
    }

    private void Resize(int w, int h)
    {
        _w = w; _h = h;
        _a = new Pixel[w * h];
        _b = new Pixel[w * h];
        foreach (var e in _effects) e.Resize(w, h);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_w == 0 || frame.Width != _w || frame.Height != _h) return;
        int w = _w, h = _h;
        int paletteIndex = Math.Max(0, Array.IndexOf(DemosceneApp.PaletteNames, _app.Palette));
        float speedScale = 0.2f + _app.Speed * 0.16f;
        float scale = MathF.Pow(1.25f, _app.Scale - 5);
        var f = new DemoFrame { T = _clock, Dt = Dt * speedScale, Scale = scale, Ramp = _ramps[paletteIndex] };

        _effects[_cur].Draw(_a, f);
        var px = _a;
        float mixB = 0f;
        if (_next >= 0)
        {
            _effects[_next].Draw(_b, f);
            mixB = Kit.Smooth(_fade);
        }

        int mirror = Array.IndexOf(DemosceneApp.MirrorNames, _app.Mirror);
        int k256 = (int)(Math.Min(_app.Intensity / 8f, 1.25f) * 256f);
        int mb = (int)(mixB * 256f), ma = 256 - mb;
        var b = _b;
        for (int y = 0; y < h; y++)
        {
            int sy = mirror == 2 && y >= h / 2 ? h - 1 - y : y;
            int row = y * w, srow = sy * w;
            for (int x = 0; x < w; x++)
            {
                int sx = mirror >= 1 && x >= w / 2 ? w - 1 - x : x;
                var p = px[srow + sx];
                int r = p.R, g = p.G, bl = p.B;
                if (mb > 0)
                {
                    var q = b[srow + sx];
                    r = (r * ma + q.R * mb) >> 8; g = (g * ma + q.G * mb) >> 8; bl = (bl * ma + q.B * mb) >> 8;
                }
                r = r * k256 >> 8; g = g * k256 >> 8; bl = bl * k256 >> 8;
                frame.SetPixel(x, y, new Pixel((byte)(r > 255 ? 255 : r), (byte)(g > 255 ? 255 : g), (byte)(bl > 255 ? 255 : bl)));
            }
        }
    }
}
