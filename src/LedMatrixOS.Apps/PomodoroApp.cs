using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps;

public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>
/// A pomodoro timer: focus sessions separated by short breaks, with a long break after every few sessions. Big digits count the
/// phase down, pips show progress through the cycle and a breathing bar drains along the bottom. Each phase change raises a toast
/// (or a full-screen alert). Timing comes from <see cref="WidgetApp.Time"/>, and the cycle starts when the app is activated.
/// </summary>
public sealed class PomodoroApp : WidgetApp
{
    public override string Id => "pomodoro";
    public override string Name => "Pomodoro";
    public override int FrameRate => 30;

    [Setting("Focus (Minutes)", Description = "Length of a focus session", Min = 1, Max = 90)]
    public int FocusMinutes { get; set; } = 25;

    [Setting("Short Break (Minutes)", Description = "Length of a short break", Min = 1, Max = 30)]
    public int ShortBreakMinutes { get; set; } = 5;

    [Setting("Long Break (Minutes)", Description = "Length of the long break", Min = 1, Max = 60)]
    public int LongBreakMinutes { get; set; } = 15;

    [Setting("Sessions Before Long Break", Description = "Focus sessions in a cycle", Min = 2, Max = 8)]
    public int SessionsBeforeLongBreak { get; set; } = 4;

    [Setting("Notify", Description = "How a phase change is announced", Options = ["Toast", "Alert", "Off"])]
    public string Notify { get; set; } = "Toast";

    private PomodoroView? _view;

    public PomodoroPhase Phase => _view?.Phase ?? PomodoroPhase.Focus;

    /// <summary>Focus sessions finished in the current cycle.</summary>
    public int CompletedSessions => _view?.Completed ?? 0;

    /// <summary>Seconds left in the current phase.</summary>
    public double RemainingSeconds => _view?.RemainingSeconds ?? 0;

    public static string PhaseLabel(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => "FOCUS",
        PomodoroPhase.ShortBreak => "SHORT BREAK",
        _ => "LONG BREAK",
    };

    public static Pixel PhaseColor(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => new Pixel(255, 90, 60),
        PomodoroPhase.ShortBreak => new Pixel(60, 215, 120),
        _ => new Pixel(80, 150, 255),
    };

    internal int MinutesFor(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => FocusMinutes,
        PomodoroPhase.ShortBreak => ShortBreakMinutes,
        _ => LongBreakMinutes,
    };

    /// <summary>Raises the phase-change overlay chosen by <see cref="Notify"/>.</summary>
    internal void Announce(PomodoroPhase phase)
    {
        var color = PhaseColor(phase);
        string text = phase switch
        {
            PomodoroPhase.Focus => "Focus time",
            PomodoroPhase.ShortBreak => "Short break",
            _ => "Long break",
        };
        if (string.Equals(Notify, "Alert", StringComparison.OrdinalIgnoreCase)) ShowAlert(text, color);
        else if (!string.Equals(Notify, "Off", StringComparison.OrdinalIgnoreCase)) ShowToast(text, Pixel.Black, color, TimeSpan.FromSeconds(5));
    }

    protected override Node Build()
    {
        var view = _view = new PomodoroView(this);
        return new Panel { Children = { view } };
    }
}

internal sealed class PomodoroView : Node
{
    private readonly PomodoroApp _app;
    private readonly TextRun[] _digits = new TextRun[10];
    private readonly TextRun _colon = new();
    private readonly TextRun _label = new();
    private PomodoroPhase _labelPhase = (PomodoroPhase)(-1);
    private DateTimeOffset _phaseStart;
    private bool _started;
    private double _elapsed, _duration = 1;
    private float _time;

    public PomodoroPhase Phase { get; private set; }
    public int Completed { get; private set; }
    public double RemainingSeconds => Math.Max(0, _duration - _elapsed);

    public PomodoroView(PomodoroApp app)
    {
        _app = app;
        for (int i = 0; i < 10; i++)
        {
            _digits[i] = new TextRun();
            _digits[i].Set(Fonts.Big, ((char)('0' + i)).ToString());
        }
        _colon.Set(Fonts.Big, ":");
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = (float)ctx.Time.TotalSeconds;
        var now = _app.Time.GetUtcNow();
        if (!_started) { _started = true; _phaseStart = now; }

        _duration = _app.MinutesFor(Phase) * 60.0;
        _elapsed = Math.Max(0, (now - _phaseStart).TotalSeconds);
        if (_elapsed < _duration) return;

        // Roll into the next phase; carry the overshoot unless the app was suspended for a long time.
        _phaseStart = _elapsed - _duration > _duration ? now : _phaseStart.AddSeconds(_duration);
        int cycle = Math.Max(2, _app.SessionsBeforeLongBreak);
        if (Phase == PomodoroPhase.Focus)
        {
            Completed++;
            Phase = Completed >= cycle ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else
        {
            if (Phase == PomodoroPhase.LongBreak) Completed = 0;
            Phase = PomodoroPhase.Focus;
        }
        _duration = _app.MinutesFor(Phase) * 60.0;
        _elapsed = Math.Max(0, (now - _phaseStart).TotalSeconds);
        _app.Announce(Phase);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var color = PomodoroApp.PhaseColor(Phase);
        if (_labelPhase != Phase)
        {
            _labelPhase = Phase;
            _label.Set(Fonts.Small, PomodoroApp.PhaseLabel(Phase));
        }

        int x0 = bounds.X, y0 = bounds.Y, w = Host!.Width, h = Host.Height;
        int remaining = (int)Math.Ceiling(RemainingSeconds);
        int minutes = Math.Min(99, remaining / 60), seconds = remaining % 60;

        // Label, then the digits.
        _label.Draw(frame, x0 + 6, y0 + 4, color);
        int dx = x0 + 6, dy = y0 + 4 + _label.Height + 3;
        foreach (int d in stackalloc[] { minutes / 10, minutes % 10 }) { _digits[d].Draw(frame, dx, dy, Pixel.White); dx += _digits[d].Width + 1; }
        _colon.Draw(frame, dx, dy, color); dx += _colon.Width + 1;
        foreach (int d in stackalloc[] { seconds / 10, seconds % 10 }) { _digits[d].Draw(frame, dx, dy, Pixel.White); dx += _digits[d].Width + 1; }

        // Cycle pips on the right: one per focus session, filled once done, the current one pulsing.
        int cycle = Math.Max(2, _app.SessionsBeforeLongBreak);
        int pip = 8, gap = 5, px = x0 + w - 6 - (cycle * pip + (cycle - 1) * gap), py = y0 + 8;
        float breath = Breath();
        for (int i = 0; i < cycle; i++)
        {
            var r = new Rectangle(px + i * (pip + gap), py, pip, pip);
            bool done = i < Completed || Phase == PomodoroPhase.LongBreak;
            bool current = Phase == PomodoroPhase.Focus && i == Completed;
            var focus = PomodoroApp.PhaseColor(PomodoroPhase.Focus);
            if (done) frame.Fill(r, focus);
            else if (current) frame.Fill(r, Scale(focus, 0.25f + 0.75f * breath));
            else frame.Fill(r, new Pixel(45, 45, 55));
        }

        // Breathing bar along the bottom: drains as the phase runs out and swells gently while it does.
        int barX = x0 + 6, barW = w - 12, barH = 5, barY = y0 + h - barH - 4;
        frame.Fill(new Rectangle(barX, barY, barW, barH), new Pixel(28, 28, 36));
        double progress = _duration <= 0 ? 0 : Math.Clamp(1 - _elapsed / _duration, 0, 1);
        int fill = (int)Math.Round(barW * progress);
        if (fill > 0)
        {
            var bright = Scale(color, 0.55f + 0.45f * breath);
            frame.Fill(new Rectangle(barX, barY, fill, barH), bright);
            frame.Fill(new Rectangle(barX, barY, fill, 1), Scale(color, 0.8f + 0.2f * breath + 0.3f));     // lit top edge
            // Soft head where the bar ends.
            for (int k = 0; k < 3; k++)
            {
                int gx = barX + fill + k;
                if (gx >= barX + barW) break;
                frame.Fill(new Rectangle(gx, barY, 1, barH), Scale(color, (0.35f - 0.1f * k) * (0.4f + 0.6f * breath)));
            }
        }
    }

    /// <summary>0..1 sine breath; slower on breaks.</summary>
    private float Breath()
    {
        float period = Phase == PomodoroPhase.Focus ? 4f : 6f;
        return 0.5f + 0.5f * MathF.Sin(_time * MathF.PI * 2f / period);
    }

    private static Pixel Scale(Pixel c, float k)
    {
        static byte B(float v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5f));
        return new Pixel(B(c.R * k), B(c.G * k), B(c.B * k));
    }
}
