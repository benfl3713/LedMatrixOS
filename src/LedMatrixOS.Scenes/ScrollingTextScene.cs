using LedMatrixOS.Core;
using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

public sealed class ScrollingTextScene : MatrixSceneBase, IConfigurableScene
{
    public override string Id   => "scrolling-text";
    public override string Name => "Scrolling Text";

    private float  _scrollX;
    private int    _textWidth;

    // Settings
    private string _message    = "HELLO WORLD!";
    private int    _speed      = 30;
    private Color  _textColor  = Color.Red;
    private Color  _bgColor    = Color.Black;

    public IEnumerable<AppSetting> GetSettings() =>
    [
        new AppSetting("message",   "Message",    "Text to scroll",              AppSettingType.String,  "HELLO WORLD!", _message),
        new AppSetting("speed",     "Speed",      "Scroll speed (pixels/second)", AppSettingType.Integer, 30, _speed,  5, 120),
        new AppSetting("textColor", "Text Color", "Text colour",                  AppSettingType.Select,  "Red",    ColorName(_textColor),
            Options: ["White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta"]),
        new AppSetting("bgColor",   "Background", "Background colour",            AppSettingType.Select,  "Black",  ColorName(_bgColor),
            Options: ["Black", "DarkBlue", "DarkGreen"]),
    ];

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "message":
                _message   = value.ToString() ?? "HELLO WORLD!";
                _textWidth = MatrixFonts.Small.MeasureWidth(_message);
                break;
            case "speed":
                _speed = Math.Clamp(Convert.ToInt32(value.ToString()), 5, 120);
                break;
            case "textColor":
                _textColor = ParseColor(value.ToString());
                break;
            case "bgColor":
                _bgColor = ParseColor(value.ToString());
                break;
        }
    }

    public override void Initialize(GraphicsDevice graphicsDevice, int width, int height)
    {
        base.Initialize(graphicsDevice, width, height);
        _scrollX   = width;
        _textWidth = MatrixFonts.Small.MeasureWidth(_message);
    }

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _scrollX -= _speed * dt;
        if (_scrollX < -_textWidth)
            _scrollX = MatrixWidth;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(_bgColor);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        int y = (MatrixHeight + MatrixFonts.Small.Height) / 2;
        MatrixFonts.Small.DrawString(spriteBatch, _message, (int)_scrollX, y, _textColor);

        spriteBatch.End();
    }

    private static string ColorName(Color c)
    {
        if (c == Color.White)     return "White";
        if (c == Color.Red)       return "Red";
        if (c == Color.Green)     return "Green";
        if (c == Color.Blue)      return "Blue";
        if (c == Color.Yellow)    return "Yellow";
        if (c == Color.Cyan)      return "Cyan";
        if (c == Color.Magenta)   return "Magenta";
        if (c == Color.DarkBlue)  return "DarkBlue";
        if (c == Color.DarkGreen) return "DarkGreen";
        return "Black";
    }

    private static Color ParseColor(string? name) => name switch
    {
        "White"     => Color.White,
        "Red"       => Color.Red,
        "Green"     => Color.Green,
        "Blue"      => Color.Blue,
        "Yellow"    => Color.Yellow,
        "Cyan"      => Color.Cyan,
        "Magenta"   => Color.Magenta,
        "DarkBlue"  => Color.DarkBlue,
        "DarkGreen" => Color.DarkGreen,
        _           => Color.Black,
    };
}
