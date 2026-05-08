using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

/// <summary>
/// 7-segment clock with smooth per-digit fade transitions.
/// </summary>
public sealed class AnimatedClockScene : MatrixSceneBase
{
    public override string Id   => "animated-clock";
    public override string Name => "Animated Clock";

    // 7-segment patterns: segments [top, top-left, top-right, middle, bot-left, bot-right, bottom]
    private static readonly bool[][] Patterns =
    [
        [true,  true,  true,  false, true,  true,  true ], // 0
        [false, false, true,  false, false, true,  false], // 1
        [true,  false, true,  true,  true,  false, true ], // 2
        [true,  false, true,  true,  false, true,  true ], // 3
        [false, true,  true,  true,  false, true,  false], // 4
        [true,  true,  false, true,  false, true,  true ], // 5
        [true,  true,  false, true,  true,  true,  true ], // 6
        [true,  false, true,  false, false, true,  false], // 7
        [true,  true,  true,  true,  true,  true,  true ], // 8
        [true,  true,  true,  true,  false, true,  true ], // 9
    ];

    // Per-digit fade state
    private readonly float[] _fade   = new float[6]; // 0=old, 1=new
    private readonly int[]   _from   = new int[6];
    private readonly int[]   _to     = new int[6];
    private DateTime _lastTime = DateTime.MinValue;

    // Digit layout
    private const int DW = 12, DH = 20, ColonW = 4, Spacing = 3;

    public override void Initialize(GraphicsDevice graphicsDevice, int width, int height)
    {
        base.Initialize(graphicsDevice, width, height);
        // Seed from/to with current time to avoid spurious transitions on first draw
        var digits = TimeDigits();
        for (int i = 0; i < 6; i++) { _from[i] = digits[i]; _to[i] = digits[i]; _fade[i] = 1f; }
    }

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        if (now.Second == _lastTime.Second && now.Minute == _lastTime.Minute && now.Hour == _lastTime.Hour)
        {
            // Advance active fade animations
            float speed = (float)gameTime.ElapsedGameTime.TotalSeconds * 3f;
            for (int i = 0; i < 6; i++)
                _fade[i] = Math.Min(1f, _fade[i] + speed);
            return;
        }

        var digits = TimeDigits();
        for (int i = 0; i < 6; i++)
        {
            if (digits[i] != _to[i])
            {
                _from[i] = _to[i];
                _to[i]   = digits[i];
                _fade[i] = 0f;
            }
        }
        _lastTime = now;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        // Total width: 6 digits + 2 colons + spacing
        int totalW = 6 * (DW + Spacing) + 2 * (ColonW + Spacing);
        int startX = (MatrixWidth  - totalW) / 2;
        int startY = (MatrixHeight - DH)     / 2;

        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        int x = startX;
        for (int i = 0; i < 6; i++)
        {
            DrawDigit(spriteBatch, x, startY, _from[i], _to[i], _fade[i]);
            x += DW + Spacing;
            if (i == 1 || i == 3)
            {
                DrawColon(spriteBatch, x, startY);
                x += ColonW + Spacing;
            }
        }
        spriteBatch.End();
    }

    private void DrawDigit(SpriteBatch sb, int x, int y, int from, int to, float progress)
    {
        for (int seg = 0; seg < 7; seg++)
        {
            bool wasOn  = Patterns[from][seg];
            bool willOn = Patterns[to][seg];

            float alpha;
            Color baseColor;
            if (wasOn && willOn)       { alpha = 1f;        baseColor = Color.Cyan; }
            else if (!wasOn && !willOn) continue;
            else if (wasOn && !willOn) { alpha = 1f - progress; baseColor = Color.Red; }
            else                        { alpha = progress;     baseColor = Color.Cyan; }

            if (alpha < 0.02f) continue;

            var color = baseColor * alpha;
            DrawSegment(sb, x, y, seg, color);
        }
    }

    private void DrawSegment(SpriteBatch sb, int dx, int dy, int seg, Color color)
    {
        int thick = 2;
        switch (seg)
        {
            case 0: sb.DrawFilledRect(dx + 1,          dy,                  DW - 2,  thick,   color); break; // top
            case 1: sb.DrawFilledRect(dx,               dy + 1,              thick,   DH/2-2,  color); break; // top-left
            case 2: sb.DrawFilledRect(dx + DW - thick,  dy + 1,              thick,   DH/2-2,  color); break; // top-right
            case 3: sb.DrawFilledRect(dx + 1,          dy + DH/2 - 1,       DW - 2,  thick,   color); break; // middle
            case 4: sb.DrawFilledRect(dx,               dy + DH/2 + 1,       thick,   DH/2-2,  color); break; // bot-left
            case 5: sb.DrawFilledRect(dx + DW - thick,  dy + DH/2 + 1,       thick,   DH/2-2,  color); break; // bot-right
            case 6: sb.DrawFilledRect(dx + 1,          dy + DH - thick,     DW - 2,  thick,   color); break; // bottom
        }
    }

    private void DrawColon(SpriteBatch sb, int x, int y)
    {
        var color = Color.Cyan * (DateTime.Now.Millisecond < 500 ? 1f : 0.3f);
        int dotH  = DH / 3;
        sb.DrawFilledRect(x, y + dotH - 2,     2, 2, color);
        sb.DrawFilledRect(x, y + dotH * 2 - 2, 2, 2, color);
    }

    private static int[] TimeDigits()
    {
        var now = DateTime.Now;
        return
        [
            now.Hour   / 10, now.Hour   % 10,
            now.Minute / 10, now.Minute % 10,
            now.Second / 10, now.Second % 10,
        ];
    }
}
