using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Visuals;

/// <summary>
/// The grid and rules behind <see cref="LifeApp"/>. Conway/HighLife cells store their age (1..255, 0 = dead) so newborns glow and
/// elders settle; Wireworld uses 0 empty, 1 head, 2 tail, 3 conductor; Langton's ant uses 0/1 for the floor and tracks its ants separately.
/// </summary>
internal sealed class LifeVisual : VisualNode
{
    private const int HistorySize = 16;
    private const int StaleLimit = 24;
    private const int AntCount = 3;

    private readonly LifeApp _app;
    private readonly Random _rng;
    private readonly ulong[] _history = new ulong[HistorySize];
    private readonly int[] _antX = new int[AntCount], _antY = new int[AntCount], _antDir = new int[AntCount];
    private readonly ColorRamp _ramp = new();

    private byte[] _cur = [], _next = [];
    private int _w, _h, _cell, _gw, _gh;
    private string _rule = "", _palette = "";
    private float _accumulator;
    private int _historyCount, _stale, _sinceSeed;

    public int Generation { get; private set; }
    public int Population { get; private set; }
    public int Reseeds { get; private set; }

    public LifeVisual(LifeApp app, int seed)
    {
        _app = app;
        _rng = new Random(seed);
    }

    private bool IsAnt => _rule == LifeApp.LangtonsAnt;
    private bool IsWire => _rule == LifeApp.Wireworld;

    protected override void Step(FrameContext ctx)
    {
        var host = Host!;
        int cell = Math.Clamp(_app.CellSize, 1, 4);
        string rule = _app.Rule;
        bool resize = _w != host.Width || _h != host.Height || _cell != cell;
        if (resize)
        {
            _w = host.Width; _h = host.Height; _cell = cell;
            _gw = Math.Max(4, _w / cell); _gh = Math.Max(4, _h / cell);
            _cur = new byte[_gw * _gh]; _next = new byte[_gw * _gh];
        }
        if (_palette != _app.Palette) BuildPalette(_app.Palette);
        if (resize || rule != _rule)
        {
            _rule = rule;
            Reseed(false);
        }

        float gensPerSecond = 3f + _app.Speed * 3f;
        _accumulator += Dt * gensPerSecond;
        int steps = 0;
        while (_accumulator >= 1f && steps < 6)
        {
            _accumulator -= 1f;
            Advance();
            steps++;
        }
        if (_accumulator > 1f) _accumulator = 1f;
    }

    private void Advance()
    {
        Generation++;
        _sinceSeed++;
        if (IsAnt)
        {
            for (int i = 0; i < 12; i++) StepAnts();
            if (_sinceSeed > 5000) Reseed(true);
            return;
        }
        if (IsWire) StepWire(); else StepLife(_rule == LifeApp.HighLife);

        // Stagnation: nothing left, or the live/dead picture has been seen recently (still life or short oscillator).
        ulong hash = Hash();
        bool repeat = false;
        int n = Math.Min(_historyCount, HistorySize);
        for (int i = 0; i < n; i++) if (_history[i] == hash) { repeat = true; break; }
        _history[_historyCount++ % HistorySize] = hash;
        _stale = repeat ? _stale + 1 : 0;

        bool dead = IsWire ? !HasState(1) : Population == 0;
        int cap = IsWire ? 700 : 2500;
        if (dead || _stale >= StaleLimit || _sinceSeed > cap) Reseed(true);
    }

    private bool HasState(byte s)
    {
        foreach (var c in _cur) if (c == s) return true;
        return false;
    }

    private ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        int pop = 0;
        bool wire = IsWire;
        foreach (var c in _cur)
        {
            byte v = wire ? c : (byte)(c > 0 ? 1 : 0);
            if (v != 0 && (!wire || v == 1)) pop++;
            h = (h ^ v) * 1099511628211UL;
        }
        Population = pop;
        return h;
    }

    private void StepLife(bool highLife)
    {
        int gw = _gw, gh = _gh;
        for (int y = 0; y < gh; y++)
        {
            int up = (y == 0 ? gh - 1 : y - 1) * gw, mid = y * gw, down = (y == gh - 1 ? 0 : y + 1) * gw;
            for (int x = 0; x < gw; x++)
            {
                int l = x == 0 ? gw - 1 : x - 1, r = x == gw - 1 ? 0 : x + 1;
                int n = (_cur[up + l] > 0 ? 1 : 0) + (_cur[up + x] > 0 ? 1 : 0) + (_cur[up + r] > 0 ? 1 : 0)
                      + (_cur[mid + l] > 0 ? 1 : 0) + (_cur[mid + r] > 0 ? 1 : 0)
                      + (_cur[down + l] > 0 ? 1 : 0) + (_cur[down + x] > 0 ? 1 : 0) + (_cur[down + r] > 0 ? 1 : 0);
                byte c = _cur[mid + x];
                if (c > 0) _next[mid + x] = n == 2 || n == 3 ? (byte)Math.Min(c + 1, 255) : (byte)0;
                else _next[mid + x] = n == 3 || (highLife && n == 6) ? (byte)1 : (byte)0;
            }
        }
        (_cur, _next) = (_next, _cur);
    }

    private void StepWire()
    {
        int gw = _gw, gh = _gh;
        for (int y = 0; y < gh; y++)
        {
            int up = (y == 0 ? gh - 1 : y - 1) * gw, mid = y * gw, down = (y == gh - 1 ? 0 : y + 1) * gw;
            for (int x = 0; x < gw; x++)
            {
                byte c = _cur[mid + x];
                byte o;
                switch (c)
                {
                    case 1: o = 2; break;
                    case 2: o = 3; break;
                    case 3:
                        int l = x == 0 ? gw - 1 : x - 1, r = x == gw - 1 ? 0 : x + 1;
                        int heads = (_cur[up + l] == 1 ? 1 : 0) + (_cur[up + x] == 1 ? 1 : 0) + (_cur[up + r] == 1 ? 1 : 0)
                                  + (_cur[mid + l] == 1 ? 1 : 0) + (_cur[mid + r] == 1 ? 1 : 0)
                                  + (_cur[down + l] == 1 ? 1 : 0) + (_cur[down + x] == 1 ? 1 : 0) + (_cur[down + r] == 1 ? 1 : 0);
                        o = heads == 1 || heads == 2 ? (byte)1 : (byte)3;
                        break;
                    default: o = 0; break;
                }
                _next[mid + x] = o;
            }
        }
        (_cur, _next) = (_next, _cur);
    }

    private void StepAnts()
    {
        for (int a = 0; a < AntCount; a++)
        {
            int i = _antY[a] * _gw + _antX[a];
            bool on = _cur[i] != 0;
            _antDir[a] = (_antDir[a] + (on ? 3 : 1)) & 3;     // turn left on lit cells, right on dark
            _cur[i] = on ? (byte)0 : (byte)1;
            switch (_antDir[a])
            {
                case 0: _antY[a] = _antY[a] == 0 ? _gh - 1 : _antY[a] - 1; break;
                case 1: _antX[a] = _antX[a] == _gw - 1 ? 0 : _antX[a] + 1; break;
                case 2: _antY[a] = _antY[a] == _gh - 1 ? 0 : _antY[a] + 1; break;
                default: _antX[a] = _antX[a] == 0 ? _gw - 1 : _antX[a] - 1; break;
            }
        }
    }

    private void Reseed(bool counted)
    {
        if (counted) Reseeds++;
        Array.Clear(_cur);
        Array.Clear(_next);
        Generation = 0;
        _sinceSeed = 0;
        _stale = 0;
        _historyCount = 0;
        _accumulator = 0;
        if (IsAnt)
        {
            for (int a = 0; a < AntCount; a++)
            {
                _antX[a] = _gw / 2 + (a - 1) * (_gw / 5);
                _antY[a] = _gh / 2 + (a == 1 ? 0 : (a - 1) * (_gh / 6));
                _antDir[a] = _rng.Next(4);
            }
            Population = 0;
        }
        else if (IsWire) SeedWire();
        else
        {
            for (int i = 0; i < _cur.Length; i++) _cur[i] = _rng.NextDouble() < 0.28 ? (byte)1 : (byte)0;
            Population = _cur.Length;
        }
    }

    /// <summary>Rectangular wire loops with an electron chasing round each, some overlapping so signals cross over.</summary>
    private void SeedWire()
    {
        int loops = 4 + _rng.Next(4);
        for (int k = 0; k < loops; k++)
        {
            int rw = 6 + _rng.Next(Math.Max(1, _gw / 4)), rh = 4 + _rng.Next(Math.Max(1, _gh / 3));
            int x0 = _rng.Next(Math.Max(1, _gw - rw - 1)), y0 = _rng.Next(Math.Max(1, _gh - rh - 1));
            int x1 = Math.Min(_gw - 1, x0 + rw), y1 = Math.Min(_gh - 1, y0 + rh);
            for (int x = x0; x <= x1; x++) { _cur[y0 * _gw + x] = 3; _cur[y1 * _gw + x] = 3; }
            for (int y = y0; y <= y1; y++) { _cur[y * _gw + x0] = 3; _cur[y * _gw + x1] = 3; }
            int ex = x0 + 2;
            _cur[y0 * _gw + ex] = 1;          // head with its tail behind it
            _cur[y0 * _gw + ex - 1] = 2;
        }
        Population = 1;
    }

    public void SetPattern(string[] rows)
    {
        Array.Clear(_cur);
        _historyCount = 0; _stale = 0; _sinceSeed = 0; Generation = 0;
        for (int y = 0; y < rows.Length && y < _gh; y++)
            for (int x = 0; x < rows[y].Length && x < _gw; x++)
                _cur[y * _gw + x] = rows[y][x] switch
                {
                    '#' => IsWire ? (byte)3 : (byte)1,
                    'o' => 1,
                    'x' => 2,
                    _ => 0,
                };
        Hash();
    }

    private void BuildPalette(string name)
    {
        _palette = name;
        // Index 0 is a newborn / bright head; the tail of the ramp is the old, settled colour.
        (float, Pixel)[] stops = name switch
        {
            "Fire" => [(0f, new Pixel(255, 255, 200)), (0.15f, new Pixel(255, 170, 30)), (0.6f, new Pixel(210, 50, 10)), (1f, new Pixel(110, 15, 5))],
            "Ocean" => [(0f, new Pixel(220, 255, 255)), (0.15f, new Pixel(60, 220, 255)), (0.6f, new Pixel(20, 90, 220)), (1f, new Pixel(10, 30, 120))],
            "Mono" => [(0f, new Pixel(255, 255, 255)), (0.2f, new Pixel(190, 190, 190)), (1f, new Pixel(80, 80, 80))],
            _ => [(0f, new Pixel(255, 255, 255)), (0.12f, new Pixel(255, 60, 220)), (0.5f, new Pixel(40, 200, 255)), (1f, new Pixel(30, 255, 120))],
        };
        _ramp.Fill(stops);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_cur.Length == 0) return;
        int cell = _cell;
        for (int y = 0; y < _gh; y++)
        {
            for (int x = 0; x < _gw; x++)
            {
                byte c = _cur[y * _gw + x];
                if (c == 0) continue;
                Pixel p;
                if (IsWire) p = c == 1 ? _ramp.Sample(0f) : c == 2 ? _ramp.Sample(0.35f) : _ramp.Sample(0.75f);
                else if (IsAnt) p = _ramp.Sample(0.55f);
                else p = _ramp.Sample(Math.Min(c - 1, 14) / 14f);
                Put(frame, bounds, x, y, cell, p);
            }
        }
        if (IsAnt)
            for (int a = 0; a < AntCount; a++) Put(frame, bounds, _antX[a], _antY[a], cell, Pixel.White);
    }

    private static void Put(FrameBuffer frame, Rectangle bounds, int gx, int gy, int cell, Pixel p)
    {
        int px = bounds.X + gx * cell, py = bounds.Y + gy * cell;
        if (cell == 1) frame.SetPixel(px, py, p);
        else frame.Fill(new Rectangle(px, py, cell - (cell > 2 ? 1 : 0), cell - (cell > 2 ? 1 : 0)), p);
    }
}
