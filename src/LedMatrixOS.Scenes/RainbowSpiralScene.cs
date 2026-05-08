using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

public sealed class RainbowSpiralScene : MatrixSceneBase
{
    public override string Id   => "rainbow-spiral";
    public override string Name => "Rainbow Spiral";

    private double _animationTime;
    private const int Segments = 60;

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
        => _animationTime += gameTime.ElapsedGameTime.TotalSeconds;

    public override void Draw(SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);

        float cx = MatrixWidth  / 2f;
        float cy = MatrixHeight / 2f;
        float maxR = Math.Min(MatrixWidth, MatrixHeight) / 2f - 2;

        var pts = new (int x, int y)[Segments];
        for (int i = 0; i < Segments; i++)
        {
            double angle  = i / (double)Segments * Math.PI * 6 + _animationTime;
            float  radius = i / (float)Segments  * maxR;
            pts[i] = ((int)(cx + Math.Cos(angle) * radius), (int)(cy + Math.Sin(angle) * radius));
        }

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        for (int i = 0; i < Segments - 1; i++)
        {
            float hue   = (i / (float)Segments + (float)_animationTime * 0.2f) % 1f;
            var   color = SpriteBatchExtensions.HsvToColor(hue * 360, 1f, 1f);
            spriteBatch.DrawLine(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, color);
        }
        spriteBatch.End();
    }
}
