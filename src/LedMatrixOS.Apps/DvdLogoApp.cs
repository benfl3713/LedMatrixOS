using System.Numerics;
using LedMatrixOS.Apps.Toys;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// The classic bouncing DVD logo with all the love: it squashes into walls, sparks, trails and changes colour, and a corner hit
/// triggers a full celebration with a running counter. Gentle aim assist makes corners turn up now and then.
/// </summary>
public sealed class DvdLogoApp : WidgetApp
{
    private DvdLogoField? _field;

    public override string Id => "dvd-logo";
    public override string Name => "DVD Logo";

    [Setting("Speed", Description = "Movement speed (1-10, 5 is normal)", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Trail", Description = "Ghost trail behind the logo")]
    public bool Trail { get; set; } = true;

    [Setting("Corner Assist", Description = "Gently steer so a corner hit happens now and then")]
    public bool CornerAssist { get; set; } = true;

    [Setting("Show Counter", Description = "Show the corner-hit counter after the first corner")]
    public bool ShowCounter { get; set; } = true;

    [Setting("Palette", Description = "Colours the logo cycles through", Options = ["Classic", "Neon", "Sunset", "Ocean", "Candy", "Aurora", "Rainbow", "Mono"])]
    public string Palette { get; set; } = "Classic";

    public DvdLogoField? Field => _field;
    public Pill? CounterPill { get; private set; }

    protected override Node Build()
    {
        var field = new DvdLogoField();
        var pill = new Pill("CORNERS 0", new Pixel(255, 60, 140))
        {
            Style = new TextStyle(Fonts.QuiteSmall, Pixel.White, Shadow: false),
            HAlign = Align.Center,
            VAlign = Align.End,
            Margin = 1,
            Visible = false,
        };
        field.CornerHit += n =>
        {
            pill.Text = $"CORNERS {n}";
            pill.Visible = ShowCounter;
            pill.Position = new Vector2(0, -10);
            pill.AnimatePosition(Vector2.Zero, TimeSpan.FromMilliseconds(600), Easing.OutBounce);
        };
        _field = field;
        CounterPill = pill;
        Apply();
        return new Panel { Children = { field, pill } };
    }

    /// <summary>Old stored speeds were percentages (20-400); they are migrated onto the 1-10 levels.</summary>
    public override void UpdateSetting(string key, object value) =>
        base.UpdateSetting(key, string.Equals(key, "speed", StringComparison.OrdinalIgnoreCase) ? ToySpeed.Migrate(value) : value);

    protected override void OnSettingChanged(string key) => Apply();

    private void Apply()
    {
        if (_field is null) return;
        _field.Speed = ToySpeed.Percent(Speed);
        _field.Trails = Trail;
        _field.Assist = CornerAssist;
        _field.PaletteName = Palette;
        if (CounterPill is { } pill && !ShowCounter) pill.Visible = false;
    }
}
