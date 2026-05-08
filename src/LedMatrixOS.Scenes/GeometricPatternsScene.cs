using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

public sealed class GeometricPatternsScene : MatrixSceneBase
{
    public override string Id   => "geometric-patterns";
    public override string Name => "Geometric Patterns";

    private double _animTime;
    private int    _pattern;
    private double _patternTimer;

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
    {
        double dt = gameTime.ElapsedGameTime.TotalSeconds;
        _animTime     += dt;
        _patternTimer += dt;
        if (_patternTimer >= 5.0)
        {
            _pattern      = (_pattern + 1) % 3;
            _patternTimer = 0;
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        switch (_pattern)
        {
            case 0: DrawRotatingTriangles(spriteBatch); break;
            case 1: DrawPulsatingCircles(spriteBatch);  break;
            case 2: DrawRotatingSquares(spriteBatch);   break;
        }
        spriteBatch.End();
    }

    private void DrawRotatingTriangles(SpriteBatch sb)
    {
        float cx = MatrixWidth / 2f, cy = MatrixHeight / 2f;
        for (int i = 0; i < 6; i++)
        {
            double angle  = i * Math.PI / 3 + _animTime;
            float  radius = (float)(15 + Math.Sin(_animTime + i) * 5);
            float  x      = cx + (float)(Math.Cos(angle) * radius);
            float  y      = cy + (float)(Math.Sin(angle) * radius);
            float  hue    = (i * 60 + (float)_animTime * 30) % 360;
            var    color  = SpriteBatchExtensions.HsvToColor(hue, 1f, 1f);
            sb.FillPolygon(x, y, 3, 5f, (float)(_animTime + i), color);
        }
    }

    private void DrawPulsatingCircles(SpriteBatch sb)
    {
        int cx = MatrixWidth / 2, cy = MatrixHeight / 2;
        for (int i = 1; i <= 5; i++)
        {
            float  radius = (float)(i * 5 + Math.Sin(_animTime * 2 + i) * 3);
            float  hue    = (float)((_animTime * 50 + i * 30) % 360);
            var    color  = SpriteBatchExtensions.HsvToColor(hue, 1f, 0.8f);
            sb.DrawFilledCircle(cx, cy, (int)radius, color);
        }
        // Overwrite centre with inner colour for a ring effect
        sb.DrawFilledCircle(cx, cy, 3, Color.Black);
    }

    private void DrawRotatingSquares(SpriteBatch sb)
    {
        float cx = MatrixWidth / 2f, cy = MatrixHeight / 2f;
        for (int i = 0; i < 4; i++)
        {
            float  size  = 8 + i * 5;
            float  rot   = (float)(_animTime * (0.5 + i * 0.3));
            float  hue   = (i * 90 + (float)_animTime * 40) % 360;
            var    color = SpriteBatchExtensions.HsvToColor(hue, 1f, 1f);
            sb.DrawPolygon(cx, cy, 4, size, rot, color);
        }
    }
}
