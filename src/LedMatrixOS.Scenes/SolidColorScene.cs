using LedMatrixOS.Core;
using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

/// <summary>
/// Fills the entire matrix with a single configurable colour.
/// This is also the canonical reference implementation for <see cref="MatrixSceneBase"/>
/// showing the minimal scene structure.
/// </summary>
public sealed class SolidColorScene : MatrixSceneBase, IConfigurableScene
{
    public override string Id   => "solid_color";
    public override string Name => "Solid Color";

    private byte _red   = 20;
    private byte _green = 255;
    private byte _blue  = 0;

    // ── IMatrixScene ─────────────────────────────────────────────────────────

    public override void Update(GameTime gameTime, CancellationToken cancellationToken) { }

    public override void Draw(SpriteBatch spriteBatch)
    {
        // GraphicsDevice.Clear sets every pixel — fastest possible fill.
        GraphicsDevice.Clear(new Color(_red, _green, _blue));
        // No SpriteBatch draw calls needed for a solid fill.
    }

    // ── IConfigurableScene ───────────────────────────────────────────────────

    public IEnumerable<AppSetting> GetSettings() => new[]
    {
        new AppSetting("red",   "Red",   "Red component (0-255)",   AppSettingType.Integer, 20,  (int)_red,   0, 255),
        new AppSetting("green", "Green", "Green component (0-255)", AppSettingType.Integer, 255, (int)_green, 0, 255),
        new AppSetting("blue",  "Blue",  "Blue component (0-255)",  AppSettingType.Integer, 0,   (int)_blue,  0, 255),
    };

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "red":   _red   = Clamp(value); break;
            case "green": _green = Clamp(value); break;
            case "blue":  _blue  = Clamp(value); break;
        }
    }

    private static byte Clamp(object value) =>
        (byte)Math.Clamp(Convert.ToInt32(value.ToString()), 0, 255);
}
