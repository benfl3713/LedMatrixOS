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

    [Setting("Speed", Description = "Movement speed in percent", Min = 20, Max = 400)]
    public int Speed { get; set; } = 100;

    [Setting("Trail", Description = "Ghost trail behind the logo")]
    public bool Trail { get; set; } = true;

    [Setting("CornerAssist", Description = "Gently steer so a corner hit happens now and then")]
    public bool CornerAssist { get; set; } = true;

    [Setting("ShowCounter", Description = "Show the corner-hit counter after the first corner")]
    public bool ShowCounter { get; set; } = true;

    [Setting("Palette", Description = "Colours the logo cycles through", Options = ["classic", "neon", "sunset", "ocean", "candy", "aurora"])]
    public string Palette { get; set; } = "classic";

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

    protected override void OnSettingChanged(string key) => Apply();

    private void Apply()
    {
        if (_field is null) return;
        _field.Speed = Speed;
        _field.Trails = Trail;
        _field.Assist = CornerAssist;
        _field.PaletteName = Palette;
        if (CounterPill is { } pill && !ShowCounter) pill.Visible = false;
    }
}
