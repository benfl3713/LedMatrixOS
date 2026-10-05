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
[LegacySettingKey("red")]
[LegacySettingKey("green")]
[LegacySettingKey("blue")]
public class SolidColorApp : WidgetApp
{
    public static readonly string[] Modes = ["Solid", "Breathing", "Gradient Drift", "Color Cycle", "Sparkle", "Candle", "Aurora"];

    public override string Id => "solid_color";
    public override string Name => "Solid Color";
    public override int FrameRate => 30;

    [Setting("Colour", Description = "Colour as hex, e.g. #14FF00 (or #RGB). Invalid text keeps the previous colour")]
    public string Colour { get; set; } = "#14FF00";

    [Setting("Mode", Description = "Solid is a flat colour; the others animate it", Options = ["Solid", "Breathing", "Gradient Drift", "Color Cycle", "Sparkle", "Candle", "Aurora"])]
    public string Mode { get; set; } = "Solid";

    [Setting("Speed", Description = "How fast the animation moves (1-10)", Min = 1, Max = 10)]
    public int Speed { get; set; } = 5;

    [Setting("Spread", Description = "How far the colours wander from your colour, in degrees of hue (0-180)", Min = 0, Max = 180)]
    public int Spread { get; set; } = 60;

    [Setting("Brightness", Description = "Overall brightness in percent (5-100)", Min = 5, Max = 100)]
    public int Brightness { get; set; } = 100;

    [Setting("Fade Duration", Description = "How long a colour change takes to fade, in milliseconds (0 = instant)", Min = 0, Max = 3000)]
    public int FadeDuration { get; set; } = 450;

    // Parsed colour cache: re-parsed only when the Colour string changes, so steady-state frames never allocate.
    private string? _parsedFor;
    private Pixel _parsed = new(20, 255, 0);

    /// <summary>The colour as parsed from <see cref="Colour"/>; the last valid colour when the text is not a hex colour.</summary>
    public Pixel Parsed
    {
        get
        {
            if (!ReferenceEquals(_parsedFor, Colour))
            {
                _parsedFor = Colour;
                if (TryParseHex(Colour, out var p)) _parsed = p;
            }
            return _parsed;
        }
    }

    public int Red { get => Parsed.R; set => SetChannel(0, value); }
    public int Green { get => Parsed.G; set => SetChannel(1, value); }
    public int Blue { get => Parsed.B; set => SetChannel(2, value); }

    private void SetChannel(int channel, int value)
    {
        var c = Parsed;
        byte v = (byte)Math.Clamp(value, 0, 255);
        c = channel switch { 0 => new Pixel(v, c.G, c.B), 1 => new Pixel(c.R, v, c.B), _ => new Pixel(c.R, c.G, v) };
        Colour = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    /// <summary>Accepts #RRGGBB, RRGGBB, #RGB and RGB.</summary>
    public static bool TryParseHex(string? text, out Pixel color)
    {
        color = default;
        var t = (text ?? "").Trim().TrimStart('#');
        if (t.Length == 3)
        {
            if (!int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var s)) return false;
            color = new Pixel((byte)(((s >> 8) & 0xF) * 17), (byte)(((s >> 4) & 0xF) * 17), (byte)((s & 0xF) * 17));
            return true;
        }
        if (t.Length != 6 || !int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        color = new Pixel((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>Old persisted red/green/blue values (0-255) are migrated into <see cref="Colour"/>.</summary>
    public override void UpdateSetting(string key, object value)
    {
        int channel = key.ToLowerInvariant() switch { "red" => 0, "green" => 1, "blue" => 2, _ => -1 };
        if (channel < 0) { base.UpdateSetting(key, value); return; }
        var current = channel switch { 0 => Red, 1 => Green, _ => Blue };
        var n = Math.Clamp(Core.Settings.SettingsBinder.CoerceInt(value, current), 0, 255);
        SetChannel(channel, n);
    }

    protected override Node Build() => new MoodField(this) { HAlign = Align.Stretch, VAlign = Align.Stretch };

    /// <summary>Everything the field needs, read straight from the settings every frame.</summary>
    internal readonly record struct Look(Pixel Color, string Mode, float Speed, float Spread, float Brightness, float FadeSeconds);

    internal Look Current => new(Parsed, Mode, Speed / 5f, Spread, Brightness / 100f, FadeDuration / 1000f);
}
