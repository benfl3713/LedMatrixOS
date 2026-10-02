using System.Reflection;
using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Effects;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Visuals;

/// <summary>Glyph bitmaps cut once from a BDF font into row bit masks, so drawing a glyph never allocates.</summary>
internal sealed class GlyphSet
{
    public int Count { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public uint[] Rows { get; private set; } = [];

    private static readonly Dictionary<string, BdfFont?> FontCache = new();

    /// <summary>Loads any font from the Fonts folder next to the Graphics assembly (not just the four preloaded in <see cref="Fonts"/>).</summary>
    public static BdfFont? LoadFont(string name)
    {
        lock (FontCache)
        {
            if (FontCache.TryGetValue(name, out var cached)) return cached;
            BdfFont? font = null;
            try
            {
                var dir = Path.GetDirectoryName(typeof(Fonts).Assembly.Location)!;
                font = new BdfFont(Path.Combine(dir, "Text", "Fonts", name + ".bdf"));
            }
            catch
            {
                // Missing font file: callers fall back to a preloaded font.
            }

            FontCache[name] = font;
            return font;
        }
    }

    public static GlyphSet Build(BdfFont font, IEnumerable<char> chars, int minPixels)
    {
        var maps = new List<bool[,]>();
        foreach (char ch in chars)
        {
            bool[,] map;
            try { map = font.GetMapOfString(ch.ToString()); }
            catch { continue; }
            int lit = 0;
            foreach (bool b in map) if (b) lit++;
            if (lit >= minPixels && (maps.Count == 0 || (map.GetLength(0) == maps[0].GetLength(0) && map.GetLength(1) == maps[0].GetLength(1))))
                maps.Add(map);
        }

        var set = new GlyphSet();
        if (maps.Count == 0) return set;
        set.Width = Math.Min(maps[0].GetLength(0), 32);
        set.Height = maps[0].GetLength(1);
        set.Count = maps.Count;
        set.Rows = new uint[set.Count * set.Height];
        for (int g = 0; g < maps.Count; g++)
            for (int y = 0; y < set.Height; y++)
            {
                uint mask = 0;
                for (int x = 0; x < set.Width; x++)
                    if (maps[g][x, y]) mask |= 1u << x;
                set.Rows[g * set.Height + y] = mask;
            }
        return set;
    }
}

/// <summary>
/// Digital rain. Three layers share one engine: each layer has a glyph set, a cell pitch, a speed range and a brightness. A stream is a head
/// position falling at constant speed; the cell under the head gets a fresh glyph, cells behind it fade by distance, and random trail cells
/// re-roll their glyph so the column shimmers. Deterministic for a seeded Random and frame times.
/// </summary>
internal sealed class RainVisual : VisualNode
{
    private struct Stream
    {
        public bool Active;
        public float HeadY, Speed, Delay, Mutate;
        public int Len, LastRow, X;
        public Pixel Base;
    }

    private sealed class Layer
    {
        public GlyphSet Glyphs = null!;
        public int PitchX, PitchY, Rows;
        public float SpeedMin, SpeedMax, Bright;
        public int LenMin, LenMax;
        public Stream[] Streams = [];
        public byte[] Cells = [];
    }

    private const string WordFont = "10x20";
    private static readonly string[] WordList = ["WAKE UP", "FOLLOW THE WHITE RABBIT", "KNOCK KNOCK", "LED MATRIX", "THERE IS NO SPOON", "ENTER THE MATRIX", "HELLO"];

    private readonly MatrixRainApp _app;
    private readonly Random _rng;
    private readonly GlowEffect _glow = new() { Threshold = 130f, Radius = 2, Strength = 0.95f };
    private readonly Layer[] _layers = new Layer[3];
    private readonly int[] _bandY = new int[4], _bandH = new int[4], _bandShift = new int[4];
    private Pixel[] _rowTemp = [];
    private bool[][,] _wordMasks = [];
    private int _w, _h;
    private string? _colour;
    private FrameContext _ctx;

    // Word reveal state
    private float _nextWordIn, _wordT = -1f;
    private int _wordIndex;
    // Glitch state
    private float _nextGlitchIn, _glitchLeft, _glitchReroll;

    public RainVisual(MatrixRainApp app, int seed)
    {
        _app = app;
        _rng = new Random(seed);
        _nextWordIn = 5f + (float)_rng.NextDouble() * 4f;
        _nextGlitchIn = 6f + (float)_rng.NextDouble() * 6f;
    }

    protected override void Step(FrameContext ctx)
    {
        _ctx = ctx;
        var host = Host!;
        if (_w != host.Width || _h != host.Height) Resize(host.Width, host.Height);
        if (_colour != _app.Colour) RecolourAll(_app.Colour);

        float speedScale = 0.4f + 0.12f * _app.Speed;
        float dt = Dt;
        foreach (var layer in _layers) StepLayer(layer, dt, speedScale);
        StepWord(dt);
        StepGlitch(dt);
    }

    private void Resize(int w, int h)
    {
        _w = w;
        _h = h;
        _rowTemp = new Pixel[w];
        _colour = null;

        var tiny = GlyphSet.LoadFont("4x6") ?? Fonts.ExtraSmall;
        var small = GlyphSet.LoadFont("5x8") ?? Fonts.QuiteSmall;
        var big = GlyphSet.LoadFont("6x13") ?? Fonts.Small;

        const string latin = "0123456789ABCDEFGHJKLMNPRSTUVXYZ:;<>=+-*/|\\#$%&@";
        var kana = Enumerable.Range(0xFF66, 0xFF9D - 0xFF66 + 1).Select(c => (char)c).Concat("0123456789Z:*+=<>").ToArray();

        _layers[0] = MakeLayer(GlyphSet.Build(tiny, latin, 5), 1, 1, 12f, 20f, 0.24f, 7, 13);
        _layers[1] = MakeLayer(GlyphSet.Build(small, latin, 6), 1, 1, 22f, 34f, 0.46f, 6, 12);
        _layers[2] = MakeLayer(GlyphSet.Build(big, kana, 8), 1, 1, 36f, 58f, 1f, 4, 8);

        foreach (var layer in _layers)
        {
            layer.Rows = (_h + layer.PitchY - 1) / layer.PitchY + 1;
            int columns = Math.Max(1, _w / layer.PitchX);
            layer.Streams = new Stream[columns];
            layer.Cells = new byte[columns * layer.Rows];
            for (int c = 0; c < columns; c++)
            {
                ref var s = ref layer.Streams[c];
                s.X = c * layer.PitchX + (_w - columns * layer.PitchX) / 2;
                for (int r = 0; r < layer.Rows; r++) layer.Cells[c * layer.Rows + r] = (byte)_rng.Next(layer.Glyphs.Count);
                s.Active = false;
                s.Delay = (float)_rng.NextDouble() * 4f;
            }
        }

        BuildWordMasks();
        RecolourAll(_app.Colour);

        // Pre-warm so the first frame already shows a full, developed rain.
        for (int i = 0; i < 140; i++)
            foreach (var layer in _layers) StepLayer(layer, 0.05f, 1f);
    }

    private static Layer MakeLayer(GlyphSet glyphs, int gapX, int gapY, float speedMin, float speedMax, float bright, int lenMin, int lenMax) => new()
    {
        Glyphs = glyphs,
        PitchX = glyphs.Width + gapX,
        PitchY = glyphs.Height + gapY,
        SpeedMin = speedMin, SpeedMax = speedMax, Bright = bright, LenMin = lenMin, LenMax = lenMax,
    };

    private void BuildWordMasks()
    {
        var font = GlyphSet.LoadFont(WordFont) ?? Fonts.Big;
        _wordMasks = new bool[WordList.Length][,];
        for (int i = 0; i < WordList.Length; i++) _wordMasks[i] = font.GetMapOfString(WordList[i]);
    }

    private static Pixel ThemeColour(string name) => name switch
    {
        "Cyan" => new Pixel(40, 215, 255),
        "Red" => new Pixel(255, 45, 60),
        "Purple" => new Pixel(175, 70, 255),
        "Gold" => new Pixel(255, 185, 35),
        _ => new Pixel(35, 255, 85),
    };

    private void RecolourAll(string name)
    {
        _colour = name;
        foreach (var layer in _layers)
            for (int c = 0; c < layer.Streams.Length; c++) layer.Streams[c].Base = StreamColour(name, c, layer.Streams.Length);
    }

    private Pixel StreamColour(string name, int column, int columns) =>
        name == "Rainbow" ? Pixel.FromHsv(column * 360f / columns * 1.5f + (column % 3) * 20f, 0.85f, 1f) : ThemeColour(name);

    private void StepLayer(Layer layer, float dt, float speedScale)
    {
        float spawnEvery = Kit.Lerp(14f, 0.6f, (_app.Density - 1) / 9f);
        var glyphs = layer.Glyphs;
        if (glyphs.Count == 0) return;
        for (int c = 0; c < layer.Streams.Length; c++)
        {
            ref var s = ref layer.Streams[c];
            if (!s.Active)
            {
                s.Delay -= dt;
                if (s.Delay > 0f) continue;
                s.Active = true;
                s.HeadY = -(float)_rng.NextDouble() * layer.PitchY;
                s.Speed = Kit.Lerp(layer.SpeedMin, layer.SpeedMax, (float)_rng.NextDouble());
                s.Len = _rng.Next(layer.LenMin, layer.LenMax + 1);
                s.LastRow = -1;
                s.Mutate = 0f;
                continue;
            }

            s.HeadY += s.Speed * speedScale * dt;
            int headRow = (int)MathF.Floor(s.HeadY / layer.PitchY);
            if (headRow != s.LastRow)
            {
                s.LastRow = headRow;
                if ((uint)headRow < (uint)layer.Rows) layer.Cells[c * layer.Rows + headRow] = (byte)_rng.Next(glyphs.Count);
            }

            s.Mutate += dt * 6f;
            while (s.Mutate >= 1f)
            {
                s.Mutate -= 1f;
                int row = headRow - 1 - _rng.Next(s.Len);
                if ((uint)row < (uint)layer.Rows) layer.Cells[c * layer.Rows + row] = (byte)_rng.Next(glyphs.Count);
            }

            if (s.HeadY - s.Len * layer.PitchY > _h)
            {
                s.Active = false;
                s.Delay = (float)_rng.NextDouble() * spawnEvery * 2f;
            }
        }
    }

    private void StepWord(float dt)
    {
        if (!_app.Words) { _wordT = -1f; return; }
        if (_wordT >= 0f)
        {
            _wordT += dt;
            if (_wordT > ScanIn + Hold + FadeOut)
            {
                _wordT = -1f;
                _nextWordIn = 11f + (float)_rng.NextDouble() * 9f;
            }
            return;
        }

        _nextWordIn -= dt;
        if (_nextWordIn <= 0f)
        {
            _wordT = 0f;
            _wordIndex = _rng.Next(_wordMasks.Length);
        }
    }

    private const float ScanIn = 1.1f, Hold = 2.6f, FadeOut = 1.2f;

    private void StepGlitch(float dt)
    {
        if (!_app.Glitch) { _glitchLeft = 0f; return; }
        if (_glitchLeft > 0f)
        {
            _glitchLeft -= dt;
            _glitchReroll -= dt;
            if (_glitchReroll <= 0f) RollBands();
            return;
        }

        _nextGlitchIn -= dt;
        if (_nextGlitchIn <= 0f)
        {
            _glitchLeft = 0.18f + (float)_rng.NextDouble() * 0.2f;
            _nextGlitchIn = 7f + (float)_rng.NextDouble() * 9f;
            RollBands();
        }
    }

    private void RollBands()
    {
        _glitchReroll = 0.05f;
        for (int i = 0; i < _bandY.Length; i++)
        {
            _bandY[i] = _rng.Next(_h);
            _bandH[i] = 2 + _rng.Next(9);
            _bandShift[i] = (_rng.Next(2) == 0 ? -1 : 1) * (3 + _rng.Next(14));
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_w == 0 || frame.Width != _w || frame.Height != _h) return;

        foreach (var layer in _layers) DrawLayer(frame, layer);
        if (_wordT >= 0f) DrawWord(frame);
        if (_glitchLeft > 0f) ApplyGlitch(frame);
        if (_app.Glow) _glow.Apply(frame, _ctx);
    }

    private static void DrawLayer(FrameBuffer frame, Layer layer)
    {
        var g = layer.Glyphs;
        if (g.Count == 0) return;
        for (int c = 0; c < layer.Streams.Length; c++)
        {
            ref var s = ref layer.Streams[c];
            if (!s.Active) continue;
            int headRow = s.LastRow;
            int r0 = Math.Max(0, headRow - s.Len), r1 = Math.Min(layer.Rows - 1, headRow);
            for (int r = r0; r <= r1; r++)
            {
                float d = (s.HeadY - r * layer.PitchY) / layer.PitchY; // cells behind the head
                if (d < 0f) continue;
                float b = 1f - d / s.Len;
                if (b <= 0.04f) continue;
                b = b * MathF.Sqrt(b);                                  // b^1.5: long soft tail
                float white = d < 1.2f ? (1.2f - d) / 1.2f : 0f;
                white *= white;
                float k = b * layer.Bright;
                float rr = s.Base.R * k * (1f - white) + 255f * white * layer.Bright;
                float gg = s.Base.G * k * (1f - white) + 255f * white * layer.Bright;
                float bb = s.Base.B * k * (1f - white) + 255f * white * layer.Bright;
                var color = new Pixel(Kit.ToByte(rr), Kit.ToByte(gg), Kit.ToByte(bb));

                int gi = layer.Cells[c * layer.Rows + r];
                int y0 = r * layer.PitchY, x0 = s.X;
                int rowBase = gi * g.Height;
                for (int ly = 0; ly < g.Height; ly++)
                {
                    uint mask = g.Rows[rowBase + ly];
                    if (mask == 0) continue;
                    for (int lx = 0; lx < g.Width; lx++)
                        if ((mask & (1u << lx)) != 0) frame.SetPixel(x0 + lx, y0 + ly, color);
                }
            }
        }
    }

    private void DrawWord(FrameBuffer frame)
    {
        var mask = _wordMasks[_wordIndex];
        int mw = mask.GetLength(0), mh = mask.GetLength(1);
        int x0 = (_w - mw) / 2, y0 = (_h - mh) / 2;
        float t = _wordT;
        float env = t < ScanIn ? Kit.Smooth(t / ScanIn)
            : t < ScanIn + Hold ? 1f
            : 1f - Kit.Smooth((t - ScanIn - Hold) / FadeOut);

        // Quiet the rain behind the letters so they read from across the room.
        float keep = 1f - 0.82f * env;
        int kx0 = Math.Max(0, x0 - 5), kx1 = Math.Min(_w, x0 + mw + 5), ky0 = Math.Max(0, y0 - 3), ky1 = Math.Min(_h, y0 + mh + 3);
        for (int y = ky0; y < ky1; y++)
            for (int x = kx0; x < kx1; x++)
            {
                var p = frame.GetPixel(x, y);
                frame.SetPixel(x, y, new Pixel(Kit.ToByte(p.R * keep), Kit.ToByte(p.G * keep), Kit.ToByte(p.B * keep)));
            }

        var tone = StreamColour(_app.Colour, _w / 2, _w);
        var letter = Pixel.Lerp(tone, Pixel.White, 0.5f);
        float scanX = (mw + 12) * Kit.Smooth(t / ScanIn) - 6f;
        float dissolve = t > ScanIn + Hold ? (t - ScanIn - Hold) / FadeOut : -1f;
        int frameSeed = (int)_ctx.FrameIndex;

        for (int my = 0; my < mh; my++)
            for (int mx = 0; mx < mw; mx++)
            {
                if (!mask[mx, my]) continue;
                bool scanning = t < ScanIn;
                if (scanning && mx > scanX) continue;
                if (dissolve >= 0f && Kit.Hash(mx, my, 3) < dissolve) continue;

                Pixel c = letter;
                if (scanning)
                {
                    float near = scanX - mx;                 // distance behind the scan line
                    if (near < 7f) c = Pixel.Lerp(Pixel.White, letter, near / 7f);
                }
                else if (Kit.Hash(mx, my, frameSeed >> 2) < 0.04f)
                {
                    c = Pixel.White;                         // sparkle
                }
                else if (Kit.Hash(mx, my, frameSeed >> 3) < 0.10f)
                {
                    c = Pixel.Lerp(tone, Pixel.White, 0.15f); // shimmer
                }

                frame.SetPixel(x0 + mx, y0 + my, c);
            }

        // The scan line itself.
        if (t < ScanIn)
        {
            int sx = x0 + (int)scanX;
            for (int y = ky0; y < ky1; y++) Kit.Add(frame, sx, y, 120f, 255f, 140f);
        }
    }

    private void ApplyGlitch(FrameBuffer frame)
    {
        for (int i = 0; i < _bandY.Length; i++)
        {
            int shift = _bandShift[i];
            for (int y = _bandY[i]; y < Math.Min(_h, _bandY[i] + _bandH[i]); y++)
            {
                frame.GetPixelRowSpan(y)[.._w].CopyTo(_rowTemp);
                for (int x = 0; x < _w; x++)
                {
                    var pr = _rowTemp[Math.Clamp(x - shift - 2, 0, _w - 1)];
                    var pg = _rowTemp[Math.Clamp(x - shift, 0, _w - 1)];
                    var pb = _rowTemp[Math.Clamp(x - shift + 2, 0, _w - 1)];
                    frame.SetPixel(x, y, new Pixel(pr.R, pg.G, pb.B));
                }
            }
        }
    }
}
