using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Alive;

/// <summary>
/// Hosts the simulations behind <see cref="AliveApp"/>: sizes their buffers, feeds them the settings, watches for stagnation,
/// handles the periodic reseed and the auto-cycle between simulations. Time only comes from the frame deltas.
/// </summary>
internal sealed class AliveVisual : VisualNode
{
    private const float FadeSeconds = 0.6f;

    private readonly AliveApp _app;
    private readonly AliveContext _ctx;
    private readonly IAliveSim[] _sims;
    private int _w, _h, _current = -1;
    private string _palette = "", _variant = "";
    private float _sinceSeed, _sinceSwitch, _fade = 1f;
    private float _accumulator;

    public int Reseeds { get; private set; }
    public string CurrentName => _current < 0 ? "" : _sims[_current].Name;
    public int Ticks => _current < 0 ? 0 : _sims[_current].Ticks;

    public AliveVisual(AliveApp app, int seed)
    {
        _app = app;
        _ctx = new AliveContext(new Random(seed));
        _sims = [new BoidsSim(), new ReactionDiffusionSim(), new FallingSandSim(), new LeniaSim()];
    }

    public void Wipe()
    {
        if (_current >= 0) _sims[_current].Wipe();
    }

    private int IndexFor(string simulation, int autoIndex)
    {
        switch (simulation)
        {
            case AliveApp.BoidsName: return 0;
            case AliveApp.ReactionName: return 1;
            case AliveApp.SandName: return 2;
            case AliveApp.LeniaName: return 3;
            default: return autoIndex;
        }
    }

    protected override void Step(FrameContext ctx)
    {
        var host = Host!;
        var c = _ctx;
        c.Speed = Math.Clamp(_app.Speed, 1, 10);
        c.Population = Math.Clamp(_app.Population, 1, 10);
        c.Trail = Math.Clamp(_app.Trail, 0, 10);
        c.Predator = _app.Predator;
        c.Variant = _app.Variant;
        if (_palette != _app.Palette) { _palette = _app.Palette; c.Palette = AlivePalette.Create(_palette); }

        bool resize = _w != host.Width || _h != host.Height;
        if (resize)
        {
            _w = host.Width; _h = host.Height;
            foreach (var s in _sims) s.Resize(_w, _h);
            _current = -1;
        }

        // Which simulation should be showing?
        int wanted;
        bool auto = _app.Simulation == AliveApp.AutoCycle;
        if (auto)
        {
            wanted = _current < 0 ? 0 : _current;
            if (_current >= 0 && _sinceSwitch >= Math.Max(3, _app.CycleSeconds)) wanted = (_current + 1) % _sims.Length;
        }
        else wanted = IndexFor(_app.Simulation, 0);

        if (wanted != _current)
        {
            bool first = _current < 0;
            _current = wanted;
            _sinceSwitch = 0;
            if (!first) _fade = 0f;
            Reseed(false);
        }
        else if (_variant != c.Variant)
        {
            Reseed(false);
        }
        _variant = c.Variant;

        var sim = _sims[_current];
        _sinceSeed += Dt;
        _sinceSwitch += Dt;
        if (_fade < 1f) _fade = MathF.Min(1f, _fade + Dt / FadeSeconds);

        sim.Step(Dt, c);

        float reseedAfter = _app.ReseedMinutes * 60f;
        if (sim.Stagnant || (reseedAfter > 0f && _sinceSeed >= reseedAfter)) Reseed(true);
    }

    private void Reseed(bool counted)
    {
        if (counted) Reseeds++;
        _sinceSeed = 0;
        _accumulator = 0;
        _sims[_current].Seed(_ctx);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_current < 0) return;
        _sims[_current].Draw(frame, bounds, _ctx);
        if (_fade < 1f)
        {
            float k = _fade * _fade;
            for (int y = 0; y < bounds.Height; y++)
                for (int x = 0; x < bounds.Width; x++)
                {
                    int px = bounds.X + x, py = bounds.Y + y;
                    frame.SetPixel(px, py, Kit.Scale(frame.GetPixel(px, py), k));
                }
        }
    }
}
