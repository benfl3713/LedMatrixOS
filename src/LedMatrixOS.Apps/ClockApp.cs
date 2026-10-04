using LedMatrixOS.Apps.Clocks;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// The one clock app, in three looks chosen by <see cref="Style"/>: Digital (huge gradient digits and a seconds sweep),
/// Animated (neon digits over flowing aurora ribbons with embers and sparks) and Flip (retro split-flap cards).
/// Settings that belong to another style are kept but ignored. The former "animated-clock" and "flip-clock" ids are aliases
/// of this app with the matching Style (see <see cref="BuiltInApps.Aliases"/>).
/// </summary>
public sealed partial class ClockApp : WidgetApp
{
    public override string Id => "clock";
    public override string Name => "Clock";

    [Setting("Style", Description = "Digital: gradient digits; Animated: neon digits over aurora ribbons; Flip: retro flip cards", Options = ["Digital", "Animated", "Flip"])]
    public string Style { get; set; } = "Digital";

    [Setting("Show Seconds", Description = "Display seconds")]
    public bool ShowSeconds { get; set; } = true;

    [Setting("24-Hour Format", Description = "Use 24-hour format instead of 12-hour")]
    public bool Show24Hour { get; set; } = true;

    [Setting("Show Date", Description = "Show the date (weekday, day and month)")]
    public bool ShowDate { get; set; } = true;

    [Setting("Palette", Description = "Colour theme (Digital: Sunset, Ocean, Neon, Aurora, Ember, Mono; Animated: Aurora, Lava, Cyber, Ocean, Rainbow)", Options = ["Sunset", "Ocean", "Neon", "Aurora", "Ember", "Mono", "Lava", "Cyber", "Rainbow"])]
    public string Palette { get; set; } = "Sunset";

    [Setting("Time Color", Description = "Digital style: Palette uses the colour gradient of the chosen palette; a named colour overrides the digits", Options = ["Palette", "White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta"])]
    public string TimeColor { get; set; } = "Palette";

    [Setting("Waves", Description = "Animated style: flowing ribbons that ripple on every second")]
    public bool Waves { get; set; } = true;

    [Setting("Embers And Sparks", Description = "Animated style: drifting embers and the spark burst when a digit lands")]
    public bool Sparks { get; set; } = true;

    [Setting("Text Color", Description = "Flip style: color of the flip cards text", Options = ["White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta", "Amber", "Orange"])]
    public string TextColor { get; set; } = "White";

    [Setting("Background Color", Description = "Flip style: color of the flip cards background", Options = ["Black", "DarkBlue", "DarkGray", "White"])]
    public string BackgroundColor { get; set; } = "Black";

    [Setting("Show AM/PM", Description = "Flip style: show an AM/PM flap in 12-hour mode")]
    public bool ShowAmPm { get; set; } = true;

    private ClockState _state = null!;

    protected override void OnSettingChanged(string key)
    {
        if (Root is null) return;
        if (key == "style") { Host.Root = Build(); return; }
        switch (Style)
        {
            case "Animated": AnimatedSettingChanged(key); break;
            case "Flip": FlipSettingChanged(key); break;
            default: DigitalSettingChanged(key); break;
        }
    }

    protected override Node Build() => Style switch
    {
        "Animated" => BuildAnimated(),
        "Flip" => BuildFlip(),
        _ => BuildDigital(),
    };
}
