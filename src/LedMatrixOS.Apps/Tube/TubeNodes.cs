using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Tube;

/// <summary>Line colours tuned for an LED panel (the dark TfL blues and greys muddy out, so they are lifted a little).</summary>
internal static class LineColors
{
    private static readonly Dictionary<string, Pixel> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bakerloo"] = new(190, 105, 20), ["central"] = new(228, 38, 33), ["circle"] = new(255, 211, 41), ["district"] = new(0, 140, 60),
        ["hammersmith-city"] = new(244, 169, 190), ["jubilee"] = new(170, 175, 180), ["metropolitan"] = new(170, 0, 100),
        ["northern"] = new(105, 105, 115), ["piccadilly"] = new(25, 55, 235), ["victoria"] = new(0, 160, 225), ["waterloo-city"] = new(147, 206, 186),
        ["dlr"] = new(0, 185, 183), ["elizabeth"] = new(140, 100, 215), ["london-overground"] = new(238, 112, 20), ["liberty"] = new(150, 154, 158),
        ["lioness"] = new(255, 201, 47), ["mildmay"] = new(20, 120, 205), ["suffragette"] = new(0, 170, 85), ["weaver"] = new(165, 80, 120),
        ["windrush"] = new(228, 38, 33), ["tram"] = new(132, 189, 0),
    };

    public static Pixel Of(string lineId) => Map.TryGetValue(lineId, out var c) ? c : new Pixel(30, 60, 220);

    /// <summary>Black or white, whichever reads better on the colour.</summary>
    public static Pixel TextOn(Pixel c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 140 ? new Pixel(8, 8, 16) : Pixel.White;

    public static string DisplayName(string lineId) => lineId switch
    {
        "dlr" => "DLR",
        "hammersmith-city" => "HAMMERSMITH & CITY",
        "waterloo-city" => "WATERLOO & CITY",
        _ => lineId.Replace('-', ' ').ToUpperInvariant(),
    };

    public static Pixel HealthColor(LineHealth h) => h switch
    {
        LineHealth.Good => new Pixel(60, 220, 100),
        LineHealth.Minor => new Pixel(255, 190, 20),
        LineHealth.Severe => new Pixel(255, 60, 50),
        _ => new Pixel(150, 160, 175),
    };
}

/// <summary>The line's colour as a banner, with a slow light sweep so it never looks dead.</summary>
internal sealed class LineBand : Node
{
    private readonly Func<Pixel> _color;
    private float _t;

    public LineBand(Func<Pixel> color)
    {
        _color = color;
        HAlign = VAlign = Align.Stretch;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _t = (float)ctx.Time.TotalSeconds;
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        var c = _color();
        for (int y = 0; y < b.Height; y++)
            f.Fill(new Rectangle(b.X, b.Y + y, b.Width, 1), c.WithBrightness(1.08f - 0.38f * y / b.Height));
        f.Fill(new Rectangle(b.X, b.Bottom - 1, b.Width, 1), Pixel.Lerp(Pixel.Black, c, 0.35f));

        float sweep = (_t % 7f) / 7f * (b.Width + 80) - 40;
        for (int x = 0; x < 30; x++)
        {
            float a = 0.28f * MathF.Sin(x / 30f * MathF.PI);
            for (int y = 0; y < b.Height - 1; y++) f.BlendPixel(b.X + (int)sweep + x - y / 3, b.Y + y, Pixel.White, a);
        }
    }
}

/// <summary>Black capsule with a status dot and the status text in the status colour: readable on any line colour.</summary>
internal sealed class StatusBadge : Node
{
    private readonly Func<(LineHealth Health, string Text)> _source;
    private readonly StaticText _text = new();
    private readonly BdfFontParser.BdfFont _font = Fonts.Small;
    private float _t;

    public StatusBadge(Func<(LineHealth, string)> source)
    {
        _source = source;
        Height = 16;
        VAlign = Align.Center;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _t = (float)ctx.Time.TotalSeconds;
        if (_text.Set(_font, _source().Text)) InvalidateLayout();
    }

    protected override Size MeasureCore(int availW, int availH) => new(_text.Width + 20, 16);

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        var (health, _) = _source();
        var color = LineColors.HealthColor(health);
        var ink = new Pixel(6, 6, 12);
        f.Fill(new Rectangle(b.X + 2, b.Y, b.Width - 4, b.Height), ink);
        f.Fill(new Rectangle(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), ink);
        f.Fill(new Rectangle(b.X, b.Y + 2, b.Width, b.Height - 4), ink);
        float pulse = health is LineHealth.Minor or LineHealth.Severe ? 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(_t * 5f)) : 1f;
        f.FillRect(new Rectangle(b.X + 6, b.Y + 5, 6, 6), color.WithBrightness(pulse));
        _text.Draw(f, b.X + 15, b.Y + 2, color, shadow: false);
    }
}

/// <summary>
/// The line itself, laid across the panel with stations as ticks and live trains gliding along it. A hazard stripe runs along the track
/// while service is disrupted. One train at a time is spotlighted with its next station and arrival time.
/// </summary>
internal sealed class TrackMap : Node
{
    private sealed class TrainView
    {
        public string Id = "";
        public float Pos, Target, Alpha;
        public bool Alive = true, Rightward = true;
        public string Next = "";
        public int Seconds;
    }

    private readonly Func<LineSnapshot?> _snap;
    private readonly Func<Pixel> _color;
    private readonly Func<LineHealth> _health;
    private readonly List<TrainView> _views = new();
    private readonly StaticText _focusText = new();
    private readonly BdfFontParser.BdfFont _font = Fonts.QuiteSmall;
    private LineSnapshot? _seen;
    private TrainView? _focusView;
    private int _focusMinutes = -1;
    private float _t, _focusT;

    private const int Left = 12, LineY = 13, Thick = 6;

    public TrackMap(Func<LineSnapshot?> snap, Func<Pixel> color, Func<LineHealth> health)
    {
        _snap = snap;
        _color = color;
        _health = health;
        HAlign = VAlign = Align.Stretch;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _t = (float)ctx.Time.TotalSeconds;
        float dt = (float)ctx.Delta.TotalSeconds;
        var snap = _snap();
        if (!ReferenceEquals(snap, _seen)) Reconcile(snap);

        float k = 1f - MathF.Exp(-dt * 3f);
        for (int i = _views.Count - 1; i >= 0; i--)
        {
            var v = _views[i];
            v.Pos += (v.Target - v.Pos) * k;
            v.Alpha = Math.Clamp(v.Alpha + (v.Alive ? dt * 2f : -dt * 2f), 0f, 1f);
            if (!v.Alive && v.Alpha <= 0f) _views.RemoveAt(i);
        }

        int alive = 0;
        foreach (var v in _views) if (v.Alive) alive++;
        TrainView? focus = null;
        if (alive > 0)
        {
            int pick = (int)(_t / 4f) % alive;
            foreach (var v in _views) if (v.Alive && pick-- == 0) { focus = v; break; }
        }
        int minutes = focus is null ? -1 : Math.Max(0, (focus.Seconds + 30) / 60);
        if (!ReferenceEquals(focus, _focusView) || minutes != _focusMinutes)
        {
            if (!ReferenceEquals(focus, _focusView)) _focusT = _t;
            _focusView = focus;
            _focusMinutes = minutes;
            _focusText.Set(_font, focus is null ? "" : minutes == 0 ? focus.Next + " now" : focus.Next + " " + minutes + "m");
        }
    }

    private void Reconcile(LineSnapshot? snap)
    {
        _seen = snap;
        if (snap is null) return;
        foreach (var v in _views) v.Alive = false;
        int n = Math.Max(1, snap.Stops.Count - 1);
        foreach (var t in snap.Trains)
        {
            var view = _views.Find(v => v.Id == t.Id);
            bool fresh = view is null;
            if (view is null) _views.Add(view = new TrainView { Id = t.Id });
            view.Alive = true;
            view.Target = t.StopIndex / (float)n;
            if (fresh) view.Pos = view.Target;
            view.Rightward = !t.Direction.Equals("inbound", StringComparison.OrdinalIgnoreCase);
            view.Next = t.NextStation;
            view.Seconds = t.Seconds;
        }
    }

    protected override void OnRender(FrameBuffer f, Rectangle b)
    {
        var line = _color();
        var health = _health();
        int right = b.Width - Left, width = right - Left, y0 = b.Y + LineY - Thick / 2;

        // Dark wash of the line colour that fades out downwards.
        for (int y = 0; y < b.Height; y++)
            f.Fill(new Rectangle(b.X, b.Y + y, b.Width, 1), line.WithBrightness(0.14f * (1f - y / (float)b.Height)));

        var snap = _seen;
        bool hasData = snap is { Stops.Count: > 1 };

        f.Fill(new Rectangle(b.X + Left, y0, width, Thick), hasData ? line : line.WithBrightness(0.25f));
        // Highlight gliding along the track.
        float sweep = (_t * 45f) % (width + 80) - 40;
        for (int x = 0; x < 36; x++)
        {
            int px = (int)sweep + x;
            if (px < 0 || px >= width) continue;
            float a = (hasData ? 0.33f : 0.5f) * MathF.Sin(x / 36f * MathF.PI);
            for (int y = 0; y < Thick; y++) f.BlendPixel(b.X + Left + px, y0 + y, Pixel.White, a);
        }

        if (health is LineHealth.Minor or LineHealth.Severe)
        {
            var warn = health == LineHealth.Severe ? new Pixel(255, 40, 40) : new Pixel(255, 190, 0);
            int shift = (int)(_t * 14f);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < Thick; y++)
                    if (((x + y + shift) / 4) % 2 == 0) f.BlendPixel(b.X + Left + x, y0 + y, warn, 0.8f);
        }

        if (!hasData) return;

        int n = snap!.Stops.Count;
        for (int i = 1; i < n - 1; i++)
        {
            int x = b.X + Left + (int)MathF.Round(i / (float)(n - 1) * (width - 1));
            f.Fill(new Rectangle(x, y0 - 3, 1, Thick + 6), Pixel.White);
        }
        Terminus(f, b.X + Left, b.Y + LineY, line);
        Terminus(f, b.X + right, b.Y + LineY, line);

        foreach (var v in _views) DrawTrain(f, b, v, width, ReferenceEquals(v, _focusView), line);

        if (_focusView is { } fv && _focusText.Width > 0)
        {
            float appear = Math.Clamp((_t - _focusT) * 3f, 0f, 1f);
            int tx = b.X + Left + (int)(fv.Pos * (width - 1));
            int lx = Math.Clamp(tx - _focusText.Width / 2, b.X + 2, b.Right - _focusText.Width - 2), ly = b.Y + LineY + 11 + (int)((1f - appear) * 4);
            f.Fill(new Rectangle(tx, b.Y + LineY + 9, 1, 2), Pixel.White);
            if (appear > 0.5f) _focusText.Draw(f, lx, ly, Pixel.White);
        }
    }

    private static void Terminus(FrameBuffer f, int cx, int cy, Pixel line)
    {
        for (int y = -7; y <= 7; y++)
            for (int x = -7; x <= 7; x++)
            {
                int d = x * x + y * y;
                if (d <= 49) f.SetPixel(cx + x, cy + y, d <= 12 ? line : Pixel.White);
                if (d > 49 && d <= 64) f.SetPixel(cx + x, cy + y, Pixel.Black);
            }
    }

    private void DrawTrain(FrameBuffer f, Rectangle b, TrainView v, int width, bool focus, Pixel line)
    {
        float a = v.Alpha;
        int x = b.X + Left + (int)MathF.Round(v.Pos * (width - 1)), cy = b.Y + LineY;
        int dir = v.Rightward ? 1 : -1;
        var body = new Pixel(255, 255, 255);

        float pulse = 0.5f + 0.5f * MathF.Sin(_t * 4f + v.Pos * 20f);
        bool dense = _views.Count > 12 && !focus; // busy lines: plain markers, no glow or tail
        if (!dense)
        for (int gy = -9; gy <= 9; gy++)
            for (int gx = -9; gx <= 9; gx++)
            {
                float d = MathF.Sqrt(gx * gx + gy * gy) / 9f;
                if (d < 1f) f.BlendPixel(x + gx, cy + gy, body, (1f - d) * (1f - d) * 0.3f * a * (focus ? 1.4f : 0.7f + 0.3f * pulse));
            }
        if (!dense)
        for (int i = 1; i <= 9; i++) f.BlendPixel(x - dir * (i + 2), cy, body, 0.8f * (1f - i / 10f) * a);
        if (!dense)
        for (int i = 1; i <= 9; i++)
            for (int y = -1; y <= 1; y++) f.BlendPixel(x - dir * (i + 2), cy + y, body, 0.35f * (1f - i / 10f) * a);

        f.Fill(new Rectangle(x - 4, cy - 8, 9, 17), Pixel.Lerp(f.GetPixel(x, cy - 9), Pixel.Black, a));
        f.Fill(new Rectangle(x - 3, cy - 7, 7, 15), Pixel.Lerp(f.GetPixel(x, cy), body, a));
        f.Fill(new Rectangle(x - 3, cy - 7, 7, 3), Pixel.Lerp(Pixel.Black, line, a));
        f.Fill(new Rectangle(x - 3, cy + 5, 7, 3), Pixel.Lerp(Pixel.Black, line, a));
        // Direction chevron on the white body.
        var arrow = Pixel.Lerp(Pixel.Black, new Pixel(10, 10, 20), a);
        for (int i = 0; i < 3; i++) f.Fill(new Rectangle(x + dir * (i - 1), cy - 2 + i, 1, 5 - 2 * i), arrow);
        if (focus && ((int)(_t * 3f) & 1) == 0)
            f.DrawRect(new Rectangle(x - 6, cy - 9, 13, 19), Pixel.White);
    }
}
