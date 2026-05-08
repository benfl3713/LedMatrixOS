using LedMatrixOS.Core;
using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

public sealed class ClockScene : MatrixSceneBase, IConfigurableScene
{
    public override string Id   => "clock";
    public override string Name => "Clock";

    // Settings
    private bool   _showSeconds = true;
    private bool   _show24Hour  = true;
    private Color  _timeColor   = Color.White;

    public IEnumerable<AppSetting> GetSettings() =>
    [
        new AppSetting("showSeconds", "Show Seconds", "Display seconds",         AppSettingType.Boolean, true,    _showSeconds),
        new AppSetting("show24Hour",  "24-Hour",      "Use 24-hour format",      AppSettingType.Boolean, true,    _show24Hour),
        new AppSetting("timeColor",   "Colour",       "Colour of the time text", AppSettingType.Select,  "White", ColorName(_timeColor),
            Options: ["White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta"]),
    ];

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "showSeconds": _showSeconds = Convert.ToBoolean(value.ToString()); break;
            case "show24Hour":  _show24Hour  = Convert.ToBoolean(value.ToString()); break;
            case "timeColor":   _timeColor   = ParseColor(value.ToString());        break;
        }
    }

    public override void Update(GameTime gameTime, CancellationToken cancellationToken) { }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var now    = DateTime.Now;
        string fmt = _show24Hour ? (_showSeconds ? "HH:mm:ss" : "HH:mm")
                                 : (_showSeconds ? "hh:mm:ss" : "hh:mm");
        string text = now.ToString(fmt);

        var font = MatrixFonts.Big;
        int w    = font.MeasureWidth(text);
        int x    = (MatrixWidth  - w) / 2;
        int y    = (MatrixHeight + font.Height) / 2;

        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        font.DrawString(spriteBatch, text, x, y, _timeColor);
        spriteBatch.End();
    }

    private static string ColorName(Color c)
    {
        if (c == Color.Red)     return "Red";
        if (c == Color.Green)   return "Green";
        if (c == Color.Blue)    return "Blue";
        if (c == Color.Yellow)  return "Yellow";
        if (c == Color.Cyan)    return "Cyan";
        if (c == Color.Magenta) return "Magenta";
        return "White";
    }

    private static Color ParseColor(string? name) => name switch
    {
        "Red"     => Color.Red,
        "Green"   => Color.Green,
        "Blue"    => Color.Blue,
        "Yellow"  => Color.Yellow,
        "Cyan"    => Color.Cyan,
        "Magenta" => Color.Magenta,
        _         => Color.White,
    };
}
