using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Spotify;

/// <summary>
/// Slow colour waves in the album palette behind everything. Drawn as a few dozen vertical strips (two rect fills each), never per pixel,
/// and kept dim so text stays readable. "Gradient" is the same without the motion.
/// </summary>
internal sealed class BackdropNode : Node
{
    private const int Strip = 4;
    private TimeSpan _time;

    public BackdropNode()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public SpotifyColors Colors { get; set; } = SpotifyColors.Default;
    public string Style { get; set; } = "Waves";

    /// <summary>Strips left of this x are skipped (the album art covers them).</summary>
    public int StartX { get; set; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (Style == "Off") return;
        float t = (float)_time.TotalSeconds;
        bool moving = Style == "Waves";
        int split = bounds.Height * 5 / 8;

        for (int x = bounds.X + StartX / Strip * Strip; x < bounds.Right; x += Strip)
        {
            float u = (x - bounds.X) / (float)bounds.Width;
            float mix = moving
                ? 0.5f + 0.5f * MathF.Sin(x * 0.028f + t * 0.55f) * (0.6f + 0.4f * MathF.Sin(x * 0.011f - t * 0.31f))
                : u;
            var c = Pixel.Lerp(Colors.A, Colors.B, Math.Clamp(mix, 0f, 1f));
            float pulse = moving ? 0.24f + 0.06f * MathF.Sin(x * 0.05f - t * 0.8f) : 0.22f;
            frame.Fill(new Rectangle(x, bounds.Y, Strip, split), c.WithBrightness(pulse));
            frame.Fill(new Rectangle(x, bounds.Y + split, Strip, bounds.Height - split), c.WithBrightness(pulse * 0.55f));
        }
    }
}

/// <summary>
/// Small equaliser. With live audio it follows the frequency bands; otherwise it plays a calm, repeatable sway so the display never looks
/// frozen. Paused, it settles to a row of dim stubs. Heights are smoothed with fast attack and slow release; nothing allocates per frame.
/// </summary>
internal sealed class VisualiserNode : Node
{
    public const int Bars = 9;
    private const int BarWidth = 3, Gap = 1, BarsHeight = 12;

    private static readonly float[] Speed = [1.9f, 2.6f, 1.4f, 3.1f, 2.2f, 1.7f, 2.9f, 2.0f, 2.4f];
    private static readonly float[] Phase = [0.0f, 1.3f, 2.1f, 0.7f, 3.4f, 4.2f, 5.0f, 2.8f, 1.9f];

    private readonly float[] _level = new float[Bars];
    private readonly float[] _bands = new float[AudioDataService.FrequencyBandCount];
    private TimeSpan _time;
    private float _dt;

    public VisualiserNode()
    {
        Width = Bars * (BarWidth + Gap) - Gap;
        Height = BarsHeight;
    }

    public bool Active { get; set; } = true;
    public Pixel Low { get; set; } = SpotifyColors.SpotifyGreen;
    public Pixel High { get; set; } = SpotifyColors.SpotifyGreen;
    public AudioDataService? Audio { get; set; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
        _dt = (float)ctx.Delta.TotalSeconds;
        float t = (float)ctx.Time.TotalSeconds;

        int bandCount = Active && Audio is { } audio && audio.HasRecentData() ? audio.CopyFrequencyBands(_bands) : 0;
        for (int i = 0; i < Bars; i++)
        {
            float target;
            if (!Active) target = 0f;
            else if (bandCount >= Bars)
            {
                int per = bandCount / Bars;
                float sum = 0;
                for (int j = 0; j < per; j++) sum += _bands[i * per + j];
                target = Math.Clamp(sum / per * 3f, 0f, 1f);
            }
            else
            {
                float sway = 0.5f + 0.5f * MathF.Sin(t * Speed[i] + Phase[i]);
                float swell = 0.6f + 0.4f * MathF.Sin(t * 0.7f + i * 0.9f);
                target = 0.15f + 0.85f * sway * swell;
            }

            float rate = target > _level[i] ? 14f : 4f;
            _level[i] += (target - _level[i]) * Math.Min(1f, rate * _dt);
        }
    }

    protected override Size MeasureCore(int availW, int availH) => new(Bars * (BarWidth + Gap) - Gap, BarsHeight);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        for (int i = 0; i < Bars; i++)
        {
            int h = Math.Clamp((int)MathF.Round(_level[i] * bounds.Height), 1, bounds.Height);
            var c = Pixel.Lerp(Low, High, i / (float)(Bars - 1));
            if (!Active) c = c.WithBrightness(0.35f);
            frame.Fill(new Rectangle(bounds.X + i * (BarWidth + Gap), bounds.Bottom - h, BarWidth, h), c);
        }
    }
}

/// <summary>A 7x6 pixel heart, shown when the current track is in the user's library.</summary>
internal sealed class HeartNode : Node
{
    private static readonly string[] Rows = ["0110110", "1111111", "1111111", "0111110", "0011100", "0001000"];

    public HeartNode()
    {
        Width = 7;
        Height = 6;
    }

    public Pixel Color { get; set; } = SpotifyColors.SpotifyGreen;

    protected override Size MeasureCore(int availW, int availH) => new(7, 6);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        for (int y = 0; y < Rows.Length; y++)
            for (int x = 0; x < 7; x++)
                if (Rows[y][x] == '1') frame.SetPixel(bounds.X + x, bounds.Y + y, Color);
    }
}

/// <summary>Stand-in for album art that is missing or failed to decode: a palette gradient with a music note.</summary>
internal sealed class ArtPlaceholder : Node
{
    public ArtPlaceholder()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public SpotifyColors Colors { get; set; } = SpotifyColors.Default;

    /// <summary>True while the artwork is still being fetched: a breathing gradient with three travelling dots instead of the note.</summary>
    public bool Loading { get; set; }

    private TimeSpan _time;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (Loading)
        {
            float t = (float)_time.TotalSeconds;
            float breathe = 0.7f + 0.3f * MathF.Sin(t * 3f);
            frame.FillLinearGradient(bounds, Colors.A.WithBrightness(0.45f * breathe), Colors.B.WithBrightness(0.25f * breathe), vertical: true);
            for (int i = 0; i < 3; i++)
            {
                float k = MathF.Max(0f, MathF.Sin(t * 5f - i * 1.1f));
                frame.FillCircle(bounds.X + bounds.Width / 2 - 10 + i * 10, bounds.Y + bounds.Height / 2, 2, Colors.Accent.WithBrightness(0.25f + 0.75f * k));
            }
            return;
        }

        frame.FillLinearGradient(bounds, Colors.A.WithBrightness(0.55f), Colors.B.WithBrightness(0.3f), vertical: true);
        int cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2;
        var ink = Colors.Accent;
        frame.FillCircle(cx - 6, cy + 9, 6, ink);
        frame.Fill(new Rectangle(cx + 1, cy - 15, 3, 25), ink);
        frame.Fill(new Rectangle(cx + 4, cy - 15, 9, 4), ink);
        frame.Fill(new Rectangle(cx + 8, cy - 11, 4, 6), ink);
    }
}

/// <summary>A small "paused" badge over the corner of the album art.</summary>
internal sealed class PauseBadge : Node
{
    public PauseBadge()
    {
        Width = 16;
        Height = 16;
    }

    protected override Size MeasureCore(int availW, int availH) => new(16, 16);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        SimpleGraphics.FillRoundedRect(frame, bounds, 3, new Pixel(8, 8, 10));
        frame.Fill(new Rectangle(bounds.X + 4, bounds.Y + 3, 3, 10), new Pixel(235, 235, 235));
        frame.Fill(new Rectangle(bounds.X + 9, bounds.Y + 3, 3, 10), new Pixel(235, 235, 235));
    }
}

internal enum SpotifyState { Loading, Idle, Offline, NoLogin, Playing }

/// <summary>
/// Full-screen card for everything that is not a track: a breathing Spotify-style disc on the left, a headline and a hint on the right.
/// </summary>
internal sealed class StateCard : Node
{
    private readonly TextRun _title = new();
    private readonly TextRun _subtitle = new();
    private TimeSpan _time;
    private SpotifyState _state = SpotifyState.Loading;

    public StateCard()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public SpotifyState State
    {
        get => _state;
        set => _state = value;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
        _title.Set(Fonts.Big, _state switch
        {
            SpotifyState.Idle => "NOTHING PLAYING",
            SpotifyState.Offline => "SPOTIFY OFFLINE",
            SpotifyState.NoLogin => "NO SPOTIFY LOGIN",
            _ => "LOADING",
        });
        _subtitle.Set(Fonts.Small, _state switch
        {
            SpotifyState.Idle => "Start a track in Spotify",
            SpotifyState.Offline => "Retrying shortly",
            SpotifyState.NoLogin => "Spotify keys missing in config",
            _ => "Connecting to Spotify",
        });
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float t = (float)_time.TotalSeconds;
        bool offline = _state is SpotifyState.Offline or SpotifyState.NoLogin;
        var color = offline ? new Pixel(215, 60, 50) : SpotifyColors.SpotifyGreen;
        float breathe = 0.6f + 0.4f * (0.5f + 0.5f * MathF.Sin(t * 2.4f));

        int cx = bounds.X + 32, cy = bounds.Y + bounds.Height / 2;
        var disc = color.WithBrightness(breathe);
        frame.FillCircle(cx, cy, 24, disc);

        // Three "sound wave" arcs, flattened to bars; while loading they pulse one after another.
        var ink = new Pixel(10, 12, 10);
        for (int i = 0; i < 3; i++)
        {
            int w = 34 - i * 8;
            var c = _state == SpotifyState.Loading
                ? Pixel.Lerp(ink, disc, 0.55f * MathF.Max(0f, MathF.Sin(t * 5f - i * 1.2f)))
                : ink;
            SimpleGraphics.FillRoundedRect(frame, new Rectangle(cx - w / 2 + i * 2, cy - 11 + i * 8, w, 4), 2, c);
        }

        int x = bounds.X + 68;
        int top = cy - (_title.Height + 3 + _subtitle.Height) / 2;
        _title.Draw(frame, x, top, color, shadow: true);
        _subtitle.Draw(frame, x, top + _title.Height + 3, new Pixel(150, 150, 160));
    }
}

/// <summary>Fonts only the Spotify app needs.</summary>
internal static class SpotifyFonts
{
    private static BdfFontParser.BdfFont? _large;
    private static BdfFontParser.BdfFont? _largeFor;

    /// <summary>The 10x20 font for the large title (falls back to the regular big font when the file is missing).</summary>
    public static BdfFontParser.BdfFont Large
    {
        get
        {
            if (_large is null || !ReferenceEquals(_largeFor, Fonts.Big))
            {
                _largeFor = Fonts.Big;
                var path = Path.Combine(Path.GetDirectoryName(typeof(Fonts).Assembly.Location)!, "Text", "Fonts", "10x20.bdf");
                _large = File.Exists(path) ? new BdfFontParser.BdfFont(path) : Fonts.Big;
            }
            return _large;
        }
    }
}
