using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

public sealed class FireScene : MatrixSceneBase
{
    public override string Id   => "fire";
    public override string Name => "Fire";

    private byte[,] _heat    = null!;
    private Color[] _palette = null!;
    private readonly Random _random = new();
    private int _tick;

    public override void Initialize(GraphicsDevice graphicsDevice, int width, int height)
    {
        base.Initialize(graphicsDevice, width, height);
        _heat    = new byte[height, width];
        _palette = BuildPalette();

        // Seed the bottom row
        for (int x = 0; x < width; x++)
            _heat[height - 1, x] = 255;
    }

    private static Color[] BuildPalette()
    {
        var p = new Color[256];
        for (int i = 0; i < 256; i++)
        {
            if      (i < 85)  p[i] = new Color((byte)(i / 85f * 255), (byte)0, (byte)0);
            else if (i < 170) p[i] = new Color((byte)255, (byte)((i - 85)  / 85f * 255), (byte)0);
            else              p[i] = new Color((byte)255, (byte)255,          (byte)((i - 170) / 85f * 255));
        }
        return p;
    }

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
    {
        // Update every 3rd frame for a relaxed flicker speed
        if (++_tick % 3 != 0) return;

        int h = MatrixHeight, w = MatrixWidth;
        for (int y = 0; y < h - 1; y++)
        for (int x = 0; x < w;     x++)
        {
            int heat  = _heat[y + 1, x] * 6;
            int count = 6;
            if (_random.NextDouble() < 0.3)
            {
                if (x > 0)     { heat += _heat[y + 1, x - 1]; count++; }
                if (x < w - 1) { heat += _heat[y + 1, x + 1]; count++; }
            }
            int avg   = heat / count;
            int decay = _random.Next(0, 3);
            _heat[y, x] = (byte)Math.Max(0, avg - decay);
        }

        // Refuel the bottom row
        for (int x = 0; x < w; x++)
        {
            byte cur = _heat[h - 1, x];
            if (cur < 200)
                _heat[h - 1, x] = (byte)Math.Min(255, cur + _random.Next(10, 40));
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        for (int y = 0; y < MatrixHeight; y++)
        for (int x = 0; x < MatrixWidth;  x++)
        {
            byte h = _heat[y, x];
            if (h > 0) spriteBatch.DrawPixel(x, y, _palette[h]);
        }
        spriteBatch.End();
    }
}
