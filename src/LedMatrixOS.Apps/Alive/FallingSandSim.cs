using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Alive;

/// <summary>
/// Falling-sand cellular world at display resolution: sand, water, stone, fire and plants. Each run is a scripted scene
/// (rain on ledges, hourglasses, a garden that gets struck by lightning, a cascade) that feeds itself with spawners and is
/// reseeded when everything settles, the world fills up, or the scene has run long enough.
/// </summary>
internal sealed class FallingSandSim : IAliveSim
{
    private const byte Empty = 0, Sand = 1, Water = 2, Stone = 3, Fire = 4, Plant = 5;
    private static readonly string[] SceneNames = ["Rain", "Hourglass", "Garden", "Cascade"];

    private const int IdleLimit = 150, SceneTicks = 7000, MaxStepsPerFrame = 8;

    private byte[] _t = [], _s = [], _stamp = [];
    private int _w, _h, _scene = -1, _lastScene = -1;
    private byte _tick;
    private uint _rs = 1;
    private int _changes, _idle, _strikeIn, _fillCheck;
    private float _acc;
    private bool _stagnant;
    private int _population = 5;

    public string Name => "Falling Sand";
    public int Ticks { get; private set; }
    public bool Stagnant => _stagnant;
    public string Scene => _scene < 0 ? "" : SceneNames[_scene];

    public void Resize(int width, int height)
    {
        _w = width; _h = height;
        _t = new byte[width * height]; _s = new byte[width * height]; _stamp = new byte[width * height];
    }

    public void Wipe() { Array.Clear(_t); Array.Clear(_s); _stagnant = true; }

    private uint Rnd()
    {
        uint x = _rs;
        x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        return _rs = x;
    }

    private bool Chance(int percent) => Rnd() % 100u < (uint)percent;

    public void Seed(AliveContext c)
    {
        _rs = (uint)c.Rng.Next() | 1u;
        Array.Clear(_t); Array.Clear(_s); Array.Clear(_stamp);
        Ticks = 0; _idle = 0; _changes = 1; _acc = 0; _stagnant = false; _fillCheck = 0;
        _population = c.Population;
        int pick = Array.IndexOf(SceneNames, c.Variant);
        if (pick < 0)
        {
            pick = c.Rng.Next(SceneNames.Length);
            if (pick == _lastScene) pick = (pick + 1) % SceneNames.Length;
        }
        _scene = _lastScene = pick;
        _strikeIn = 500 + (int)(Rnd() % 500u);
        switch (pick)
        {
            case 0: BuildRain(); break;
            case 1: BuildHourglasses(); break;
            case 2: BuildGarden(); break;
            default: BuildCascade(); break;
        }
    }

    // ---- scene construction -------------------------------------------------

    private void Set(int x, int y, byte type, byte shade = 0)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
        int i = y * _w + x;
        _t[i] = type; _s[i] = shade == 0 ? (byte)Rnd() : shade;
    }

    private void Line(int x0, int y0, int x1, int y1, byte type)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            Set(x0, y0, type);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void BuildRain()
    {
        // A handful of ledges, each with a small plant on top that the rain feeds.
        for (int k = 0; k < 7; k++)
        {
            int len = 24 + (int)(Rnd() % 36u), x = 6 + (int)(Rnd() % (uint)Math.Max(1, _w - len - 12));
            int y = 18 + k * 6 + (int)(Rnd() % 3u);
            if (y >= _h - 3) y = _h - 3 - k;
            Line(x, y, x + len, y + (int)(Rnd() % 3u) - 1, Stone);
            Set(x + len / 2, y - 1, Plant);
            Set(x + len / 2 + 5, y - 1, Plant);
        }
        for (int x = 0; x < _w; x++) Set(x, _h - 1, Stone);
    }

    private void BuildHourglasses()
    {
        int cx0 = _w / 6;
        for (int g = 0; g < 3; g++)
        {
            int cx = cx0 + g * (_w / 3);
            byte material = g == 1 ? Water : Sand;
            int top = 2, bottom = _h - 3, neck = (top + bottom) / 2;
            for (int y = top; y <= bottom; y++)
            {
                int inner = Math.Abs(y - neck) / 2;
                Set(cx - inner - 1, y, Stone); Set(cx - inner - 2, y, Stone);
                Set(cx + inner + 1, y, Stone); Set(cx + inner + 2, y, Stone);
                if (y >= top + 4 && y <= neck - 2)
                    for (int x = -inner; x <= inner; x++) Set(cx + x, y, material);
            }
            int maxInner = (neck - top) / 2;
            for (int x = -maxInner - 2; x <= maxInner + 2; x++) { Set(cx + x, top - 1, Stone); Set(cx + x, bottom + 1, Stone); }
        }
    }

    private void BuildGarden()
    {
        for (int y = _h - 4; y < _h; y++)
            for (int x = 0; x < _w; x++) Set(x, y, y == _h - 1 ? Stone : Sand);
        for (int x = 0; x < _w; x++) Set(x, _h - 1, Stone);
        int seeds = 6 + _population;
        for (int k = 0; k < seeds; k++)
        {
            int x = 6 + (int)(Rnd() % (uint)(_w - 12));
            Set(x, _h - 5, Plant);
        }
    }

    private void BuildCascade()
    {
        for (int k = 0; k < 6; k++)
        {
            int y0 = 10 + k * 8;
            if (k % 2 == 0) Line(0, y0, _w * 55 / 100, y0 + 6, Stone);
            else Line(_w - 1, y0, _w * 45 / 100, y0 + 6, Stone);
        }
        for (int x = 0; x < _w; x++) Set(x, _h - 1, Stone);
        for (int y = _h - 12; y < _h; y++) { Set(0, y, Stone); Set(_w - 1, y, Stone); }
    }

    // ---- stepping -----------------------------------------------------------

    public void Step(float dt, AliveContext c)
    {
        _population = c.Population;
        _acc += dt * c.Speed * (_scene == 1 ? 6f : 16f);   // the hourglasses run slower so they take a while to empty
        int steps = 0;
        while (_acc >= 1f && steps < MaxStepsPerFrame) { _acc -= 1f; Advance(); steps++; }
        if (_acc > 1f) _acc = 1f;
    }

    private void Advance()
    {
        Ticks++;
        _tick++;
        _changes = 0;
        Spawn();
        int w = _w, h = _h;
        for (int y = h - 1; y >= 0; y--)
        {
            bool ltr = ((_tick + y) & 1) == 0;
            for (int xi = 0; xi < w; xi++)
            {
                int x = ltr ? xi : w - 1 - xi;
                int i = y * w + x;
                byte type = _t[i];
                if (type == Empty || type == Stone || _stamp[i] == _tick) continue;
                switch (type)
                {
                    case Sand: StepSand(x, y, i); break;
                    case Water: StepWater(x, y, i); break;
                    case Fire: StepFire(x, y, i); break;
                    case Plant: StepPlant(x, y, i); break;
                }
            }
        }

        if (_scene == 0 || _scene == 2) Lightning();

        _idle = _changes == 0 ? _idle + 1 : 0;
        if (_idle >= IdleLimit || Ticks >= SceneTicks) _stagnant = true;
        if (++_fillCheck >= 64) { _fillCheck = 0; if (FillRatio() > 0.5f) _stagnant = true; }
    }

    private float FillRatio()
    {
        int n = 0;
        for (int i = 0; i < _t.Length; i++) { byte t = _t[i]; if (t != Empty && t != Stone) n++; }
        return n / (float)_t.Length;
    }

    private void Spawn()
    {
        int attempts = 2 + _population / 2;
        int rate = 30 + 5 * _population;
        switch (_scene)
        {
            case 0:
                for (int a = 0; a < attempts; a++)
                    if (Chance(rate)) Drop((int)(Rnd() % (uint)_w), Chance(60) ? Sand : Water);
                break;
            case 2:
                for (int a = 0; a < attempts; a++)
                    if (Chance(rate)) Drop((int)(Rnd() % (uint)_w), Water);
                if (Chance(2)) Drop((int)(Rnd() % (uint)_w), Sand);
                break;
            case 3:
                for (int a = 0; a < attempts; a++)
                {
                    if (Chance(rate + 20)) Drop(_w / 5 + (int)(Rnd() % 5u), Water);
                    if (Chance(rate)) Drop(_w * 4 / 5 + (int)(Rnd() % 5u) - 2, Sand);
                }
                break;
        }
    }

    private void Drop(int x, byte type)
    {
        int i = x;
        if ((uint)x >= (uint)_w || _t[i] != Empty) return;
        _t[i] = type; _s[i] = (byte)Rnd(); _stamp[i] = _tick;
        _changes++;
    }

    private void Lightning()
    {
        if (--_strikeIn > 0) return;
        _strikeIn = 600 + (int)(Rnd() % 700u);
        // Strike a random plant cell (the first one found from a random starting point).
        int start = (int)(Rnd() % (uint)_t.Length);
        for (int k = 0; k < _t.Length; k++)
        {
            int i = start + k;
            if (i >= _t.Length) i -= _t.Length;
            if (_t[i] != Plant) continue;
            _t[i] = Fire; _s[i] = (byte)(45 + Rnd() % 25u);
            _changes++;
            return;
        }
    }

    private void Swap(int a, int b)
    {
        (_t[a], _t[b]) = (_t[b], _t[a]);
        (_s[a], _s[b]) = (_s[b], _s[a]);
        _stamp[a] = _tick; _stamp[b] = _tick;
        _changes++;
    }

    private void StepSand(int x, int y, int i)
    {
        if (y >= _h - 1) return;
        int below = i + _w;
        byte bt = _t[below];
        if (bt == Empty || bt == Water) { Swap(i, below); return; }
        int d = (Rnd() & 1) == 0 ? 1 : -1;
        for (int k = 0; k < 2; k++, d = -d)
        {
            int nx = x + d;
            if ((uint)nx >= (uint)_w) continue;
            byte t = _t[below + d];
            if (t == Empty || t == Water) { Swap(i, below + d); return; }
        }
    }

    private void StepWater(int x, int y, int i)
    {
        int w = _w;
        if (y < _h - 1)
        {
            int below = i + w;
            if (_t[below] == Empty) { Swap(i, below); return; }
            int dd = (Rnd() & 1) == 0 ? 1 : -1;
            for (int k = 0; k < 2; k++, dd = -dd)
            {
                int nx = x + dd;
                if ((uint)nx < (uint)w && _t[below + dd] == Empty) { Swap(i, below + dd); return; }
            }
        }
        int d = (Rnd() & 1) == 0 ? 1 : -1;
        for (int k = 0; k < 2; k++, d = -d)
        {
            int nx = x + d;
            if ((uint)nx >= (uint)w || _t[i + d] != Empty) continue;
            int far = nx + d;
            if ((uint)far < (uint)w && _t[i + 2 * d] == Empty && Chance(50)) { Swap(i, i + 2 * d); return; }
            Swap(i, i + d);
            return;
        }
    }

    private void StepFire(int x, int y, int i)
    {
        int w = _w;
        byte life = _s[i];
        _changes++;                                  // a burning world is never idle
        if (life <= 1) { _t[i] = Empty; return; }
        _s[i] = (byte)(life - 1);

        // Neighbours: water puts it out, plants catch.
        for (int n = 0; n < 4; n++)
        {
            int nx = n == 0 ? x - 1 : n == 1 ? x + 1 : x, ny = n == 2 ? y - 1 : n == 3 ? y + 1 : y;
            if ((uint)nx >= (uint)w || (uint)ny >= (uint)_h) continue;
            int j = ny * w + nx;
            byte nt = _t[j];
            if (nt == Water && Chance(40)) { _t[i] = Empty; return; }
            if (nt == Plant && Chance(12)) { _t[j] = Fire; _s[j] = (byte)(40 + Rnd() % 30u); _stamp[j] = _tick; }
        }

        if (life >= 16)
        {
            // Burning fuel: stays put and now and then throws a spark upwards.
            if (y > 0 && _t[i - w] == Empty && Chance(6))
            {
                _t[i - w] = Fire; _s[i - w] = (byte)(6 + Rnd() % 8u); _stamp[i - w] = _tick;
            }
            return;
        }
        // A spark rises, wobbling.
        if (y > 0 && Chance(70))
        {
            int d = (Rnd() & 1) == 0 ? 1 : -1;
            int target = i - w;
            if (Chance(35) && (uint)(x + d) < (uint)w) target += d;
            if (_t[target] == Empty) { Swap(i, target); return; }
        }
    }

    private void StepPlant(int x, int y, int i)
    {
        if (Rnd() % 12u != 0) return;
        int w = _w;
        int water = -1;
        if (x > 0 && _t[i - 1] == Water) water = i - 1;
        else if (x < w - 1 && _t[i + 1] == Water) water = i + 1;
        else if (y > 0 && _t[i - w] == Water) water = i - w;
        else if (y < _h - 1 && _t[i + w] == Water) water = i + w;
        if (water < 0 || y == 0) return;
        // The water it drank becomes a stem that shoots up a few cells, drifting sideways a little.
        int cx = x, cy = y, len = 3 + (int)(Rnd() % 5u);
        bool grew = false;
        for (int k = 0; k < len; k++)
        {
            int dx = Chance(25) ? ((Rnd() & 1) == 0 ? 1 : -1) : 0;
            int nx = cx + dx, ny = cy - 1;
            if ((uint)nx >= (uint)w || ny < 0) break;
            int j = ny * w + nx;
            if (_t[j] != Empty) break;
            _t[j] = Plant; _s[j] = (byte)Rnd(); _stamp[j] = _tick;
            cx = nx; cy = ny; grew = true;
        }
        if (grew) { _t[water] = Empty; _changes++; }
    }

    // ---- drawing --------------------------------------------------------------

    public void Draw(FrameBuffer frame, Rectangle bounds, AliveContext c)
    {
        var pal = c.Palette;
        int w = _w;
        var hues = pal.Cycle.Colors;
        for (int y = 0; y < _h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                byte type = _t[i];
                if (type == Empty) continue;
                byte s = _s[i];
                Pixel p;
                switch (type)
                {
                    case Sand:
                        p = pal.Rainbow ? Kit.Scale(hues[(x * 255 / w + Ticks / 6) & 255], 0.8f + 0.2f * s / 255f) : Kit.Scale(pal.Sand, 0.75f + 0.25f * s / 255f);
                        break;
                    case Water:
                        p = pal.Rainbow ? Kit.Scale(hues[(x * 255 / w + 128 + Ticks / 6) & 255], 0.6f + 0.3f * s / 255f) : Kit.Scale(pal.Water, 0.65f + 0.35f * s / 255f);
                        break;
                    case Stone: p = Kit.Scale(pal.Stone, 0.8f + 0.2f * s / 255f); break;
                    case Plant: p = Kit.Scale(pal.Plant, 0.65f + 0.35f * s / 255f); break;
                    default: p = Kit.Scale(pal.Fire, 0.45f + 0.55f * Math.Min(s, (byte)20) / 20f); break;
                }
                frame.SetPixel(bounds.X + x, bounds.Y + y, p);
            }
        }
    }
}
