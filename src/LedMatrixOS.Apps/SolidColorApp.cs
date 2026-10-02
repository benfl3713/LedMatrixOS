using LedMatrixOS.Apps.Ambient;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// A mood light. The default "Solid" mode is still exactly one flat colour; the other modes bring that colour to life:
/// breathing, a drifting gradient, a slow colour cycle, twinkling sparkles, candle flicker and a soft aurora.
/// Changing the colour fades smoothly to the new one instead of jumping.
/// </summary>
public class SolidColorApp : WidgetApp
{
    public static readonly string[] Modes = ["Solid", "Breathing", "Gradient Drift", "Color Cycle", "Sparkle", "Candle", "Aurora"];

    public override string Id => "solid_color";
    public override string Name => "Solid Color";
    public override int FrameRate => 30;

    [Setting("Red", Description = "Red color component (0-255)", Min = 0, Max = 255)]
    public int Red { get; set; } = 20;

    [Setting("Green", Description = "Green color component (0-255)", Min = 0, Max = 255)]
    public int Green { get; set; } = 255;

    [Setting("Blue", Description = "Blue color component (0-255)", Min = 0, Max = 255)]
    public int Blue { get; set; } = 0;

    [Setting("Mode", Description = "Solid is a flat colour; the others animate it", Options = ["Solid", "Breathing", "Gradient Drift", "Color Cycle", "Sparkle", "Candle", "Aurora"])]
    public string Mode { get; set; } = "Solid";

    [Setting("Speed", Description = "How fast the animation moves (1-10)", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Spread", Description = "How far the colours wander from your colour, in degrees of hue (0-180)", Min = 0, Max = 180)]
    public int Spread { get; set; } = 60;

    [Setting("Brightness", Description = "Overall brightness in percent (5-100)", Min = 5, Max = 100)]
    public int Brightness { get; set; } = 100;

    protected override Node Build() => new MoodField(this) { HAlign = Align.Stretch, VAlign = Align.Stretch };

    /// <summary>Everything the field needs, read straight from the settings every frame.</summary>
    internal readonly record struct Look(Pixel Color, string Mode, float Speed, float Spread, float Brightness);

    internal Look Current => new(
        new Pixel((byte)Math.Clamp(Red, 0, 255), (byte)Math.Clamp(Green, 0, 255), (byte)Math.Clamp(Blue, 0, 255)),
        Mode, Speed / 5f, Spread, Brightness / 100f);
}
