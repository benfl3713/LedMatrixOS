using LedMatrixOS.Apps.Ambient;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// A countdown for a duration or to a moment in time. A ring drains around a pulsing core, the digits roll, and the colour slides
/// from your chosen colour through amber to red as time runs out. The last ten seconds throb with a red frame and a kick on every tick;
/// at zero the display flashes, the digits turn rainbow, "TIME'S UP!" waves and confetti bursts from the corners.
/// Set <c>target</c> (a date and time like "2026-12-25 00:00", or a daily time like "18:30") to count to a moment instead of a duration.
/// </summary>
public sealed class CountdownTimerApp : WidgetApp
{
    public override string Id => "countdown-timer";
    public override string Name => "Countdown Timer";
    public override int FrameRate => 30;

    [Setting("Duration (Minutes)", Description = "How long to count down from", Min = 1, Max = 99)]
    public int DurationMinutes { get; set; } = 5;

    [Setting("Auto Restart", Description = "Automatically restart after completion (duration mode)")]
    public bool AutoRestart { get; set; }

    [Setting("Text Color", Description = "Color of the timer display", Options = ["White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta", "Orange"])]
    public string TextColor { get; set; } = "Cyan";

    [Setting("Background Color", Description = "Background color", Options = ["Black", "DarkBlue", "DarkGray"])]
    public string BackgroundColor { get; set; } = "Black";

    [Setting("Target", Description = "Optional moment to count down to, e.g. 2026-12-25 00:00 or a daily time like 18:30. Leave empty to use the duration")]
    public string Target { get; set; } = "";

    [Setting("Label", Description = "Short caption shown above the digits, e.g. NEW YEAR")]
    public string Label { get; set; } = "";

    [Setting("Celebrate", Description = "Confetti and flashing when the countdown reaches zero")]
    public bool Celebrate { get; set; } = true;

    public static bool TryParseTarget(string? text, DateTimeOffset now, out DateTimeOffset target) =>
        CountdownParsing.TryParse(text, now, out target);

    private CountdownView? _view;

    /// <summary>Seconds left (0 once finished); mainly for tests and diagnostics.</summary>
    public double RemainingSeconds => _view?.Remaining ?? 0;

    public bool IsComplete => _view?.IsComplete ?? false;

    public int ConfettiCount => _view?.ConfettiCount ?? 0;

    protected override Node Build()
    {
        var strip = new DigitStrip(Fonts.Big, "dd:dd", 3) { HAlign = Align.Start, VAlign = Align.Center };
        var days = new DigitStrip(Fonts.Big, "dd", 1) { HAlign = Align.Start, VAlign = Align.Center, Visible = false };
        var confetti = new ConfettiLayer();
        var view = _view = new CountdownView(this, strip, days, confetti);
        return new Panel { Children = { view, days, strip, confetti } };
    }
}
