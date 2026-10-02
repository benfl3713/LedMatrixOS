using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Effects;
using LedMatrixOS.Graphics.Particles;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Visuals;

/// <summary>
/// Heat-diffusion fire. Each 60 Hz tick every cell takes the (blurred, wind-shifted) heat of the row below and loses some to cooling;
/// the cooling and the wind both come from a tileable noise texture that scrolls slightly slower than the heat rises, which shears the
/// pattern into licking tongues. The bottom rows are re-fuelled from the same noise so flames vary in height along the wall.
/// </summary>
internal sealed class FireVisual : VisualNode
{
    private const int NoiseSize = 256;
    private const float TickSeconds = 1f / 60f;
    private const float HeatRange = 1.15f; // heat that maps to the last palette entry

    private readonly FireApp _app;
    private readonly Random _rng;
    private readonly float[] _noise;
    private readonly GlowEffect _glow = new() { Threshold = 120f, Radius = 3, Strength = 0.85f };
    private readonly Emitter _emberEmitter;
    private readonly Emitter _sparkEmitter;

    private ParticleSystem? _embers;
    private float[] _heat = [];
    private int _w, _h, _tick;
    private uint _jitter = 2463534242;
    private float _accumulator;
    private ColorRamp _ramp = new();
    private string? _rampName;
    private FrameContext _ctx;

    public FireVisual(FireApp app, int seed)
    {
        _app = app;
        _rng = new Random(seed);
        _noise = Kit.TileableNoise(NoiseSize, 8, _rng);
        _emberEmitter = new Emitter
        {
            Rate = 0f,
            LifetimeMin = 1.4f, LifetimeMax = 3.2f,
            SpeedMin = 10f, SpeedMax = 34f,
            Angle = -90f, Spread = 70f,
            GravityX = 4f, GravityY = -5f,
            AlphaStart = 1f, AlphaEnd = 0f,
        };
        // Fewer, bigger, brighter sparks that climb above the flames; they are what the eye follows from across a room.
        _sparkEmitter = new Emitter
        {
            Rate = 0f, Size = 2f,
            LifetimeMin = 2f, LifetimeMax = 3.8f,
            SpeedMin = 18f, SpeedMax = 46f,
            Angle = -90f, Spread = 50f,
            GravityX = 5f, GravityY = -3f,
            AlphaStart = 1f, AlphaEnd = 0f,
        };
    }

    protected override void Step(FrameContext ctx)
    {
        _ctx = ctx;
        var host = Host!;
        if (_w != host.Width || _h != host.Height) Resize(host.Width, host.Height);
        if (_rampName != _app.Palette) BuildPalette(_app.Palette);

        _accumulator += Dt;
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < 3)
        {
            _accumulator -= TickSeconds;
            Tick();
            steps++;
        }

        if (_embers is not null)
        {
            _emberEmitter.Enabled = _app.Embers;
            _emberEmitter.Rate = _w * 0.22f * (0.5f + _app.Intensity / 8f);
            _sparkEmitter.Enabled = _app.Embers;
            _sparkEmitter.Rate = _w * 0.035f * (0.5f + _app.Intensity / 8f);
            _embers.Update(Dt);
        }
    }

    private void Resize(int w, int h)
    {
        _w = w;
        _h = h;
        _heat = new float[w * h];
        _tick = 0;
        _accumulator = 0f;
        _embers = new ParticleSystem(w, h, 600, new Random(_rng.Next()));
        _emberEmitter.X = 0;
        _emberEmitter.Width = w - 1;
        _emberEmitter.Y = h - 10;
        _emberEmitter.Height = 6;
        _embers.Add(_emberEmitter);
        _sparkEmitter.X = 0;
        _sparkEmitter.Width = w - 1;
        _sparkEmitter.Y = h - 14;
        _sparkEmitter.Height = 6;
        _embers.Add(_sparkEmitter);
        for (int i = 0; i < 160; i++) Tick(); // pre-warm: the first frame already shows a developed fire
        _embers.Update(0.0001f);
        _rampName = null;
    }

    private void BuildPalette(string name)
    {
        _rampName = name;
        Span<(float, Pixel)> stops = name switch
        {
            "Blue" =>
            [
                (0.00f, new Pixel(0, 0, 0)), (0.12f, new Pixel(0, 0, 70)), (0.32f, new Pixel(0, 40, 220)),
                (0.55f, new Pixel(20, 140, 255)), (0.78f, new Pixel(130, 215, 255)), (1.00f, new Pixel(235, 250, 255)),
            ],
            "Green" =>
            [
                (0.00f, new Pixel(0, 0, 0)), (0.12f, new Pixel(0, 50, 0)), (0.32f, new Pixel(0, 170, 20)),
                (0.55f, new Pixel(120, 255, 20)), (0.78f, new Pixel(210, 255, 120)), (1.00f, new Pixel(245, 255, 220)),
            ],
            "Purple" =>
            [
                (0.00f, new Pixel(0, 0, 0)), (0.12f, new Pixel(55, 0, 90)), (0.32f, new Pixel(170, 10, 190)),
                (0.55f, new Pixel(255, 60, 150)), (0.78f, new Pixel(255, 170, 190)), (1.00f, new Pixel(255, 240, 245)),
            ],
            _ =>
            [
                (0.00f, new Pixel(0, 0, 0)), (0.10f, new Pixel(80, 0, 0)), (0.30f, new Pixel(205, 22, 0)),
                (0.52f, new Pixel(255, 90, 0)), (0.72f, new Pixel(255, 165, 10)), (0.88f, new Pixel(255, 225, 90)),
                (1.00f, new Pixel(255, 252, 215)),
            ],
        };
        _ramp.Fill(stops);
        _emberEmitter.Gradient = [_ramp.Sample(0.95f), _ramp.Sample(0.62f), _ramp.Sample(0.3f)];
        _sparkEmitter.Gradient = [_ramp.Sample(1f), _ramp.Sample(0.7f), _ramp.Sample(0.35f)];
    }

    private void Tick()
    {
        int w = _w, h = _h;
        var heat = _heat;
        var noise = _noise;
        _tick++;

        float intensity = _app.Intensity;
        float coolBase = Kit.Lerp(0.030f, 0.0115f, (intensity - 1f) / 9f);
        float fuelGain = Kit.Lerp(0.85f, 1.2f, (intensity - 1f) / 9f);

        // Fuel: smooth in x and in time, with deep dips and hot spikes so the wall of flame has an uneven skyline.
        int fuelRow = ((_tick >> 1) & (NoiseSize - 1)) * NoiseSize;
        int bottom = (h - 1) * w;
        for (int x = 0; x < w; x++)
        {
            float n = noise[fuelRow + ((x + 97) & (NoiseSize - 1))];
            float fuel = (0.42f + n * 0.85f) * fuelGain;
            heat[bottom + x] = fuel;
        }

        int scroll = (int)(_tick * 0.85f);
        for (int y = 0; y < h - 1; y++)
        {
            int below = (y + 1) * w;
            int nrow = (((y + scroll) >> 2) & (NoiseSize - 1)) * NoiseSize;
            int wrow = ((((y + scroll) >> 1) + 101) & (NoiseSize - 1)) * NoiseSize;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float wind = noise[wrow + ((x + 50) & (NoiseSize - 1))];
                int xc = wind < 0.30f ? x - 1 : wind > 0.70f ? x + 1 : x;
                int xl = xc - 1 < 0 ? 0 : xc - 1;
                int xr = xc + 1 >= w ? w - 1 : xc + 1;
                if (xc < 0) xc = 0; else if (xc >= w) xc = w - 1;
                float s = heat[below + xl] * 0.16f + heat[below + xc] * 0.68f + heat[below + xr] * 0.16f;
                _jitter ^= _jitter << 13; _jitter ^= _jitter >> 17; _jitter ^= _jitter << 5;
                float grain = 0.8f + (_jitter & 0xFF) * (0.4f / 255f);
                float cool = coolBase * (0.05f + 2.2f * noise[nrow + x]) * grain;
                float v = s - cool;
                heat[row + x] = v < 0f ? 0f : v;
            }
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_w == 0 || frame.Width != _w || frame.Height != _h) return;
        var heat = _heat;
        var colors = _ramp.Colors;
        float scale = 255f / HeatRange;
        for (int y = 0; y < _h; y++)
        {
            int row = y * _w;
            for (int x = 0; x < _w; x++)
            {
                int i = (int)(heat[row + x] * scale);
                frame.SetPixel(x, y, colors[i > 255 ? 255 : i]);
            }
        }

        if (_app.Embers) _embers?.Render(frame);
        if (_app.Glow) _glow.Apply(frame, _ctx);
    }
}
