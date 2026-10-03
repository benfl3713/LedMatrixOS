using LedMatrixOS.Apps.Ambient;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// The screen the device boots into: a calm, oversized clock and date over a living backdrop. Pick a scene (aurora, starfield with
/// shooting stars, drifting embers, slow waves, a day and night sky that follows the real hour, or a minimal glow) and a mood
/// (six palettes from quiet blue to neon). The clock digits roll when they change, the seconds sweep along the bottom edge,
/// and everything eases in on start and cross-fades when you change a setting.
/// </summary>
public sealed partial class HomePageApp : WidgetApp
{
    public override string Id => "home";
    public override string Name => "Home";
    public override int FrameRate => 30;

    [Setting("Display Mode", Description = "Scene behind the clock", Options = ["Aurora", "Starfield", "Embers", "Waves", "Day Night Sky", "Minimal"])]
    public string DisplayMode { get; set; } = "Aurora";

    [Setting("Show Date", Description = "Display the weekday and date")]
    public bool ShowDate { get; set; } = true;

    [Setting("24-Hour Format", Description = "Use 24-hour time format")]
    public bool Show24Hour { get; set; } = true;

    [Setting("Theme", Description = "Mood and colours", Options = ["Calm Blue", "Warm Sunset", "Forest Green", "Lavender Dreams", "Monochrome", "Neon Synthwave"])]
    public string Theme { get; set; } = "Calm Blue";

    [Setting("Animation Speed", Description = "Speed of ambient animations (1-10)", Min = 1, Max = 10)]
    public int AmbientSpeed { get; set; } = 3;

    [Setting("Show Weather Chip", Description = "Show the current temperature and conditions under the clock")]
    public bool ShowWeatherChip { get; set; }

    [Setting("Show Event Chip", Description = "Show your next calendar event (needs Calendar:IcsUrl)")]
    public bool ShowEventChip { get; set; }

    [Setting("Show Line Chip", Description = "Show the worst Tube line status, only while there is disruption")]
    public bool ShowLineChip { get; set; }

    [Setting("Show Bus Chip", Description = "Show the next bus at the stop below")]
    public bool ShowBusChip { get; set; }

    [Setting("Chip Stop ID", Description = "TfL bus stop ID for the bus chip")]
    public string ChipStopId { get; set; } = "";

    [Setting("Chip Seconds", Description = "Seconds each chip stays up (3-30)", Min = 3, Max = 30)]
    public int ChipSeconds { get; set; } = 6;

    // Saved values from the previous version ("Ambient Particles", "Geometric Art", ...) are mapped onto the nearest new scene.
    public override void UpdateSetting(string key, object value)
    {
        if (string.Equals(key, "displayMode", StringComparison.OrdinalIgnoreCase))
            value = HomeModes.Normalize(SettingsBinder.CoerceString(value, DisplayMode));
        else if (string.Equals(key, "theme", StringComparison.OrdinalIgnoreCase))
            value = Options.Pick(SettingsBinder.CoerceString(value, Theme), HomeThemes.Names, Theme);
        base.UpdateSetting(key, value);
    }

    protected override Node Build()
    {
        var state = new HomeState();
        var backdrop = new HomeBackdrop(state);
        var strip = new DigitStrip(Fonts.Big, "dd:dd", 3) { HAlign = Align.Start, VAlign = Align.Center };
        var date = new DateBlock(state);
        var line = new SecondsLine(state);
        var chips = new HomeChips();
        // The band is a 236x12 slot under the clock, set by margins (a pager with a fixed size would skip measuring its pages).
        var band = new ChipPager { Margin = new Thickness(HomeChips.BandX, HomeChips.BandY, 256 - HomeChips.BandX - HomeChips.BandWidth, 64 - HomeChips.BandY - HomeChips.BandHeight) };
        band.Bind(() => chips.Visible, kind => ChipNodes.Build(kind, chips, state));
        var director = new HomeDirector(this, state, strip, date, line, backdrop, chips, band);

        return new Panel
        {
            Children = { director, backdrop, date, strip, line, band },
        };
    }
}
