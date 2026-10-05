using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Games;

/// <summary>Draws a <see cref="PongGame"/>: dashed centre line, large scores either side of it, paddles and ball.</summary>
public sealed class PongField : Node
{
    private static readonly Pixel Net = new(45, 48, 62), ScoreColor = new(95, 100, 130), Text = new(235, 235, 245), Muted = new(150, 155, 180);
    private static readonly Pixel[] Human = [new(60, 220, 240), new(255, 160, 50)];
    private static readonly Pixel[] Cpu = [new(40, 120, 135), new(140, 90, 40)];

    private readonly PongGame _game;
    private readonly TextRun[] _score = [new(), new()];
    private readonly int[] _shown = [-1, -1];
    private readonly TextRun _banner = new(), _sub = new();
    private PongState _shownState = (PongState)(-1);
    private int _shownWinner = -2;

    public PongField(PongGame game) => _game = game;

    private void Refresh()
    {
        for (int i = 0; i < 2; i++)
            if (_game.Score[i] != _shown[i]) { _shown[i] = _game.Score[i]; _score[i].Set(Fonts.Big, _shown[i].ToString()); }

        if (_game.State == _shownState && _game.Winner == _shownWinner) return;
        _shownState = _game.State;
        _shownWinner = _game.Winner;
        switch (_shownState)
        {
            case PongState.Paused: _banner.Set(Fonts.Small, "PAUSED"); _sub.Set(Fonts.QuiteSmall, "START TO RESUME"); break;
            case PongState.MatchOver:
                _banner.Set(Fonts.Small, _shownWinner == 0 ? "LEFT WINS" : "RIGHT WINS");
                _sub.Set(Fonts.QuiteSmall, "PRESS ANY BUTTON"); break;
            default: _banner.Set(Fonts.Small, ""); _sub.Set(Fonts.QuiteSmall, ""); break;
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        Refresh();
        int cx = bounds.X + bounds.Width / 2;
        for (int y = bounds.Y + 1; y < bounds.Bottom; y += 6) frame.Fill(new Rectangle(cx - 1, y, 2, 3), Net);

        _score[0].Draw(frame, cx - 24 - _score[0].Width, bounds.Y + 4, ScoreColor);
        _score[1].Draw(frame, cx + 24, bounds.Y + 4, ScoreColor);

        for (int side = 0; side < 2; side++)
        {
            int x = bounds.X + (side == 0 ? _game.LeftX : _game.RightX);
            frame.Fill(new Rectangle(x, bounds.Y + (int)MathF.Round(_game.PaddleY[side]), PongGame.PaddleW, PongGame.PaddleH),
                _game.IsHuman(side) ? Human[side] : Cpu[side]);
        }

        int bx = bounds.X + (int)MathF.Round(_game.BallX - PongGame.BallSize / 2f);
        int by = bounds.Y + (int)MathF.Round(_game.BallY - PongGame.BallSize / 2f);
        frame.Fill(new Rectangle(bx, by, PongGame.BallSize, PongGame.BallSize), Pixel.White);

        if (_game.State is PongState.Paused or PongState.MatchOver)
        {
            int h = _banner.Height + _sub.Height + 6;
            int y = bounds.Y + (bounds.Height - h) / 2;
            frame.Fill(new Rectangle(cx - 50, y, 100, h), Pixel.Black);
            _banner.Draw(frame, cx - _banner.Width / 2, y + 2, Text);
            _sub.Draw(frame, cx - _sub.Width / 2, y + _banner.Height + 4, Muted);
        }
    }
}
