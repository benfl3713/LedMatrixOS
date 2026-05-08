using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

public sealed class BouncingBallsScene : MatrixSceneBase
{
    public override string Id   => "bouncing-balls";
    public override string Name => "Bouncing Balls";

    private sealed class Ball
    {
        public float X, Y, VelX, VelY, Radius;
        public Color Color;
    }

    private readonly List<Ball> _balls  = new();
    private readonly Random     _random = new();

    public override void Initialize(GraphicsDevice graphicsDevice, int width, int height)
    {
        base.Initialize(graphicsDevice, width, height);
        _balls.Clear();
        for (int i = 0; i < 10; i++)
        {
            float r = _random.Next(2, 5);
            _balls.Add(new Ball
            {
                X      = _random.Next((int)r, width  - (int)r),
                Y      = _random.Next((int)r, height - (int)r),
                VelX   = _random.Next(-30, 30) == 0 ? 15 : _random.Next(-30, 30),
                VelY   = _random.Next(-30, 30) == 0 ? 15 : _random.Next(-30, 30),
                Radius = r,
                Color  = SpriteBatchExtensions.HsvToColor(_random.Next(0, 360), 1f, 1f)
            });
        }
    }

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        foreach (var b in _balls)
        {
            b.X += b.VelX * dt;
            b.Y += b.VelY * dt;
            if (b.X - b.Radius <= 0 || b.X + b.Radius >= MatrixWidth)
            {
                b.VelX = -b.VelX;
                b.X    = Math.Clamp(b.X, b.Radius, MatrixWidth  - b.Radius);
            }
            if (b.Y - b.Radius <= 0 || b.Y + b.Radius >= MatrixHeight)
            {
                b.VelY = -b.VelY;
                b.Y    = Math.Clamp(b.Y, b.Radius, MatrixHeight - b.Radius);
            }
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        foreach (var b in _balls)
        {
            spriteBatch.DrawFilledCircle((int)b.X, (int)b.Y, (int)b.Radius, b.Color);
            if (b.Radius > 2)
                spriteBatch.DrawFilledCircle((int)b.X, (int)b.Y, Math.Max(1, (int)(b.Radius / 2)), Color.White);
        }
        spriteBatch.End();
    }
}
