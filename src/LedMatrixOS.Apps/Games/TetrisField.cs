using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Games;

/// <summary>
/// Draws a <see cref="TetrisGame"/>. Layout on the 256x64 panel: the 10x20 board stands upright in the middle at 3 px per cell
/// (30x60 px, so it fits the 64 px height with a 2 px margin), with SCORE / LEVEL / LINES on its left and NEXT / HI on its right.
/// Text is rasterised once per value change, so steady-state frames do not allocate.
/// </summary>
public sealed class TetrisField : Node
{
    private const int Cell = 3;
    private static readonly Pixel[] Tone = [Pixel.Black, new(0, 220, 230), new(240, 220, 40), new(180, 70, 230), new(60, 210, 80), new(235, 60, 60), new(70, 100, 245), new(250, 150, 40)];
    private static readonly Pixel[] Light = BuildShade(+70), Dark = BuildShade(-80);
    private static readonly Pixel BoardBg = new(10, 10, 16), Frame = new(70, 76, 100), Label = new(130, 135, 160), Value = new(235, 235, 245);
    private static readonly Pixel Flash = new(255, 255, 255);

    private readonly TetrisGame _game;
    private readonly TextRun _scoreLabel = new(), _levelLabel = new(), _linesLabel = new(), _nextLabel = new(), _hiLabel = new();
    private readonly TextRun _score = new(), _level = new(), _lines = new(), _hi = new(), _banner = new(), _sub = new();
    private int _shownScore = -1, _shownLevel = -1, _shownLines = -1, _shownHi = -1;
    private TetrisState _shownState = (TetrisState)(-1);
    private bool _shownAttract;
    private bool _labelsReady;

    public TetrisField(TetrisGame game) => _game = game;

    private static Pixel[] BuildShade(int delta)
    {
        var t = new Pixel[Tone.Length];
        for (int i = 0; i < t.Length; i++)
            t[i] = new Pixel(Clamp(Tone[i].R + delta), Clamp(Tone[i].G + delta), Clamp(Tone[i].B + delta));
        return t;
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    private void Refresh()
    {
        if (!_labelsReady)
        {
            _scoreLabel.Set(Fonts.QuiteSmall, "SCORE");
            _levelLabel.Set(Fonts.QuiteSmall, "LEVEL");
            _linesLabel.Set(Fonts.QuiteSmall, "LINES");
            _nextLabel.Set(Fonts.QuiteSmall, "NEXT");
            _hiLabel.Set(Fonts.QuiteSmall, "HI");
            _labelsReady = true;
        }
        if (_game.Score != _shownScore) { _shownScore = _game.Score; _score.Set(Fonts.Small, _shownScore.ToString()); }
        if (_game.Level != _shownLevel) { _shownLevel = _game.Level; _level.Set(Fonts.Small, (_shownLevel + 1).ToString()); }
        if (_game.Lines != _shownLines) { _shownLines = _game.Lines; _lines.Set(Fonts.Small, _shownLines.ToString()); }
        if (_game.HighScore != _shownHi) { _shownHi = _game.HighScore; _hi.Set(Fonts.Small, _shownHi.ToString()); }
        if (_game.State != _shownState || _game.Attract != _shownAttract)
        {
            _shownState = _game.State;
            _shownAttract = _game.Attract;
            switch (_shownState)
            {
                case TetrisState.Ready: _banner.Set(Fonts.Small, "TETRIS"); _sub.Set(Fonts.QuiteSmall, "PRESS A TO START"); break;
                case TetrisState.Paused: _banner.Set(Fonts.Small, "PAUSED"); _sub.Set(Fonts.QuiteSmall, "START TO RESUME"); break;
                case TetrisState.GameOver: _banner.Set(Fonts.Small, "GAME OVER"); _sub.Set(Fonts.QuiteSmall, _shownAttract ? "DEMO" : "PRESS A"); break;
                default: _banner.Set(Fonts.Small, ""); _sub.Set(Fonts.QuiteSmall, _shownAttract ? "DEMO" : ""); break;
            }
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        Refresh();
        int bx = bounds.X + (bounds.Width - TetrisGame.Cols * Cell) / 2;
        int by = bounds.Y + (bounds.Height - TetrisGame.Rows * Cell) / 2;

        // board
        frame.Fill(new Rectangle(bx - 1, by - 1, TetrisGame.Cols * Cell + 2, TetrisGame.Rows * Cell + 2), Frame);
        frame.Fill(new Rectangle(bx, by, TetrisGame.Cols * Cell, TetrisGame.Rows * Cell), BoardBg);
        bool flashOn = ((int)(_game.Clock * 14f) & 1) == 0;
        for (int y = 0; y < TetrisGame.Rows; y++)
        {
            bool clearing = _game.State == TetrisState.Clearing && _game.IsClearingRow(y);
            for (int x = 0; x < TetrisGame.Cols; x++)
            {
                int c = _game.CellAt(x, y);
                if (clearing) { frame.Fill(new Rectangle(bx + x * Cell, by + y * Cell, Cell, Cell), flashOn ? Flash : Tone[Math.Max(c, 1)].WithBrightness(0.5f)); continue; }
                if (c != 0) Block(frame, bx + x * Cell, by + y * Cell, c);
            }
        }

        if (_game.State == TetrisState.Playing || _game.State == TetrisState.Paused)
        {
            var shape = TetrisGame.Shapes[_game.Piece][_game.Rotation];
            int ghost = _game.GhostY();
            if (ghost != _game.PieceY)
                for (int i = 0; i < 4; i++)
                {
                    int gx = bx + (_game.PieceX + shape[i * 2]) * Cell, gy = by + (ghost + shape[i * 2 + 1]) * Cell;
                    frame.SetPixel(gx + 1, gy + 1, Tone[_game.Piece + 1].WithBrightness(0.45f));
                }
            for (int i = 0; i < 4; i++)
            {
                int py = _game.PieceY + shape[i * 2 + 1];
                if (py >= 0) Block(frame, bx + (_game.PieceX + shape[i * 2]) * Cell, by + py * Cell, _game.Piece + 1);
            }
        }

        // side panels
        int lx = bx - 80, rx = bx + TetrisGame.Cols * Cell + 24;
        _scoreLabel.Draw(frame, lx, by + 1, Label);
        _score.Draw(frame, lx, by + 8, Value);
        _levelLabel.Draw(frame, lx, by + 21, Label);
        _level.Draw(frame, lx, by + 28, Value);
        _linesLabel.Draw(frame, lx, by + 41, Label);
        _lines.Draw(frame, lx, by + 48, Value);

        _nextLabel.Draw(frame, rx, by + 1, Label);
        DrawNext(frame, rx, by + 9);
        _hiLabel.Draw(frame, rx, by + 31, Label);
        _hi.Draw(frame, rx, by + 38, new Pixel(255, 205, 90));

        // messages
        if (_game.State is TetrisState.Ready or TetrisState.Paused or TetrisState.GameOver)
        {
            int h = _banner.Height + _sub.Height + 6;
            int y = bounds.Y + (bounds.Height - h) / 2;
            frame.Fill(new Rectangle(bounds.X, y, bounds.Width, h), new Pixel(0, 0, 0));
            _banner.Draw(frame, bounds.X + (bounds.Width - _banner.Width) / 2, y + 2, Value);
            _sub.Draw(frame, bounds.X + (bounds.Width - _sub.Width) / 2, y + _banner.Height + 4, Label);
        }
        else if (_game.Attract && flashOn)
        {
            _sub.Draw(frame, rx, by + 52, new Pixel(255, 120, 120));
        }
    }

    private void DrawNext(FrameBuffer frame, int x, int y)
    {
        var shape = TetrisGame.Shapes[_game.Next][0];
        int minX = 9, maxX = 0, minY = 9;
        for (int i = 0; i < 4; i++)
        {
            minX = Math.Min(minX, shape[i * 2]); maxX = Math.Max(maxX, shape[i * 2]); minY = Math.Min(minY, shape[i * 2 + 1]);
        }
        int w = (maxX - minX + 1) * 4;
        int ox = x + (16 - w) / 2;
        for (int i = 0; i < 4; i++)
        {
            var rect = new Rectangle(ox + (shape[i * 2] - minX) * 4, y + (shape[i * 2 + 1] - minY) * 4, 4, 4);
            frame.Fill(rect, Tone[_game.Next + 1]);
            frame.Fill(new Rectangle(rect.X, rect.Y, 4, 1), Light[_game.Next + 1]);
            frame.Fill(new Rectangle(rect.X, rect.Bottom - 1, 4, 1), Dark[_game.Next + 1]);
        }
    }

    private static void Block(FrameBuffer frame, int x, int y, int colour)
    {
        frame.Fill(new Rectangle(x, y, Cell, Cell), Tone[colour]);
        frame.SetPixel(x, y, Light[colour]);
        frame.SetPixel(x + 1, y, Light[colour]);
        frame.SetPixel(x, y + 1, Light[colour]);
        frame.SetPixel(x + 2, y + 2, Dark[colour]);
        frame.SetPixel(x + 1, y + 2, Dark[colour]);
        frame.SetPixel(x + 2, y + 1, Dark[colour]);
    }
}
