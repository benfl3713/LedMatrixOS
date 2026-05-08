using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

public sealed class DvdLogoScene : MatrixSceneBase
{
    public override string Id   => "dvd-logo";
    public override string Name => "DVD Logo";

    // Logo bounding box (matched to QuiteSmall "DVD" text size + padding)
    private const float LogoW = 18f, LogoH = 10f;

    private float _x, _y, _vx, _vy;
    private Color _color;
    private readonly Random _random = new();

    private static readonly Color[] Palette =
    {
        SpriteBatchExtensions.HsvToColor(0,   1f, 1f),
        SpriteBatchExtensions.HsvToColor(60,  1f, 1f),
        SpriteBatchExtensions.HsvToColor(120, 1f, 1f),
        SpriteBatchExtensions.HsvToColor(180, 1f, 1f),
        SpriteBatchExtensions.HsvToColor(240, 1f, 1f),
        SpriteBatchExtensions.HsvToColor(300, 1f, 1f),
    };

    public override void Initialize(GraphicsDevice graphicsDevice, int width, int height)
    {
        base.Initialize(graphicsDevice, width, height);
        _x      = _random.Next((int)LogoW, width  - (int)LogoW);
        _y      = _random.Next((int)LogoH, height - (int)LogoH);
        _vx     = _random.NextSingle() < 0.5f ? -25f : 25f;
        _vy     = _random.NextSingle() < 0.5f ? -15f : 15f;
        _color  = Palette[_random.Next(Palette.Length)];
    }

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
    {
        float dt  = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _x += _vx * dt;
        _y += _vy * dt;

        bool hit = false;
        if (_x <= 0 || _x + LogoW >= MatrixWidth)
        {
            _vx = -_vx;
            _x  = Math.Clamp(_x, 0, MatrixWidth  - LogoW);
            hit = true;
        }
        if (_y <= 0 || _y + LogoH >= MatrixHeight)
        {
            _vy = -_vy;
            _y  = Math.Clamp(_y, 0, MatrixHeight - LogoH);
            hit = true;
        }
        if (hit)
            _color = Palette[_random.Next(Palette.Length)];
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        // y + font.Height because DrawString's y is the baseline-adjusted position
        MatrixFonts.QuiteSmall.DrawString(spriteBatch, "DVD", (int)_x, (int)_y + MatrixFonts.QuiteSmall.Height, _color);
        spriteBatch.End();
    }
}
