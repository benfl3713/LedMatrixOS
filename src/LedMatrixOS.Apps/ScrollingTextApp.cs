using LedMatrixOS.Apps.Ambient;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// A text ticker made to be read from across a room: big pixel type that loops with a separator icon between repeats, painted with
/// gradient, rainbow, fire or per-letter wave effects, over a soft glow or a border of chasing marquee bulbs.
/// Changing the message drops the old text away while the new one bounces up from below.
/// The old font size setting (8-48) maps onto four crisp sizes.
/// </summary>
public sealed class ScrollingTextApp : WidgetApp
{
    public override string Id => "scrolling-text";
    public override string Name => "Scrolling Text";

    [Setting("Message", Description = "Text to scroll across the display")]
    public string Message { get; set; } = "HELLO WORLD!";

    [Setting("Scroll Speed", Description = "How fast the text scrolls (pixels/sec)", Min = 10, Max = 100)]
    public int ScrollSpeed { get; set; } = 30;

    [Setting("Font Size", Description = "Size of the text (8-48): up to 12 small, 20 medium, 32 large, above that huge", Min = 8, Max = 48)]
    public int FontSize { get; set; } = 24;

    [Setting("Font Style", Description = "Bold or Regular", Options = ["Regular", "Bold"])]
    public string FontStyle { get; set; } = "Bold";

    [Setting("Text Color", Description = "Color of the text", Options = ["Red", "Green", "Blue", "Yellow", "Cyan", "Magenta", "Orange", "White"])]
    public string TextColor { get; set; } = "Red";

    [Setting("Background Color", Description = "Background color", Options = ["Black", "DarkBlue", "DarkGray", "Navy"])]
    public string BackgroundColor { get; set; } = "Black";

    [Setting("Text Effect", Description = "How the text is coloured and animated", Options = ["Solid", "Gradient", "Rainbow", "Wave", "Rainbow Wave", "Fire"])]
    public string TextEffect { get; set; } = "Gradient";

    [Setting("Separator", Description = "Icon drawn between repeats of the message", Options = ["None", "Star", "Heart", "Bolt", "Diamond", "Dot", "Arrow"])]
    public string Separator { get; set; } = "Star";

    [Setting("Loop", Description = "Repeat the message continuously (off scrolls it once, then starts again)")]
    public bool Loop { get; set; } = true;

    [Setting("Decoration", Description = "Edge treatment around the text", Options = ["None", "Edge Glow", "Chase Lights"])]
    public string Decor { get; set; } = "Edge Glow";

    protected override Node Build() => new Ticker(this);
}
