using LedMatrixOS.Core;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps;

public enum BreakoutMode { Title, Serving, Playing, Dead, Attract }

/// <summary>
/// Breakout. A stats column on the left, a 208x64 playfield on the right with six coloured brick rows. Left/Right move the paddle (held, with
/// acceleration), A or Start launches the ball, three lives, and every cleared wall starts a faster level with a narrower paddle. After 20 seconds
/// without input a bot plays so the display is never dead. The high score is an Advanced setting, so it is persisted.
/// Time comes only from <see cref="FrameContext"/>; the ball is moved in sub-steps of at most one pixel so it cannot tunnel through bricks.
/// </summary>
public sealed class BreakoutApp : SettingsAppBase, IInputConsumer
{
    public const int FieldX = 48, FieldW = 208, FieldH = 64;
    public const int Cols = 13, Rows = 6, BrickW = 16, BrickH = 5, BrickTop = 5;
    public const int PaddleY = 60, PaddleH = 2;
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(20);

    private static readonly Pixel[] RowColors =
    [
        new(235, 60, 60), new(240, 140, 40), new(235, 210, 60), new(70, 200, 90), new(60, 190, 220), new(110, 100, 240),
    ];
    private static readonly int[] RowPoints = [7, 6, 5, 3, 2, 1];
    private static readonly Pixel Dim = new(120, 120, 140);
    private static readonly Pixel Gold = new(255, 210, 90);
    private static readonly Pixel Edge = new(70, 70, 120);

    private readonly bool[] _bricks = new bool[Cols * Rows];
    private int _brickCount;
    private float _px, _pv;                  // paddle centre x (field coordinates) and velocity
    private int _paddleW = 28;
    private float _bx, _by, _vx, _vy;
    private int _score, _lives = 3, _level = 1;
    private readonly Random _rng;
    private TimeSpan _now, _lastInput, _modeSince;
    private bool _everInput;
    private bool _launchRequested;

    private readonly TextRun _scoreLabel = new(), _bestLabel = new(), _levelRun = new();
    private readonly TextRun _title = new(), _sub = new(), _retry = new(), _demo = new();
    private int _shownResult = -1;
    private NumberRun? _numbers;
    private NumberRun _digits => _numbers ??= new NumberRun(Fonts.QuiteSmall);

    public override string Id => "breakout";
    public override string Name => "Breakout";
    public override int FrameRate => 60;

    [Setting("High Score", Description = "Best score so far. Saved between runs.", Min = 0, Max = 99999, Advanced = true)]
    public int HighScore { get; set; }

    public BreakoutMode Mode { get; private set; } = BreakoutMode.Title;
    public int Score => _score;
    public int Lives => _lives;
    public int Level => _level;
    public int BricksLeft => _brickCount;
    public float PaddleX => _px;

    internal float BallX => _bx;
    internal float BallY => _by;
    internal float BallVx => _vx;
    internal float BallVy => _vy;
    internal void PutBall(float x, float y, float vx, float vy) { _bx = x; _by = y; _vx = vx; _vy = vy; }
    internal void ClearBricksExcept(int row, int col)
    {
        Array.Clear(_bricks);
        _bricks[row * Cols + col] = true;
        _brickCount = 1;
    }

    [ActivatorUtilitiesConstructor]
    public BreakoutApp() : this(new Random(Environment.TickCount)) { }

    public BreakoutApp(Random rng)
    {
        _rng = rng;
        NewGame();
    }

    // ---- input -----------------------------------------------------------------------------------------------------

    public void OnInput(in InputEvent e)
    {
        if (e.State != InputState.Down) return;
        _lastInput = _now;
        _everInput = true;

        switch (Mode)
        {
            case BreakoutMode.Title:
            case BreakoutMode.Dead:
                if (e.Button is InputButton.Start or InputButton.A) { NewGame(); SetMode(BreakoutMode.Serving); }
                break;
            case BreakoutMode.Attract:
                NewGame();
                SetMode(BreakoutMode.Serving);
                break;
            case BreakoutMode.Serving:
                if (e.Button is InputButton.A or InputButton.Start) _launchRequested = true;
                break;
        }
    }

    // ---- game ------------------------------------------------------------------------------------------------------

    private void SetMode(BreakoutMode mode)
    {
        Mode = mode;
        _modeSince = _now;
    }

    private void NewGame()
    {
        _score = 0;
        _lives = 3;
        _level = 1;
        BuildLevel();
    }

    private void BuildLevel()
    {
        int pattern = _level % 3;
        _brickCount = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                bool present = pattern switch
                {
                    2 => (r + c) % 2 == 0 || r == 0,          // checkerboard under a solid top row
                    0 => c >= r && c < Cols - r,              // inverted pyramid
                    _ => true,
                };
                _bricks[r * Cols + c] = present;
                if (present) _brickCount++;
            }
        _paddleW = Math.Max(16, 28 - (_level - 1) * 3);
        ResetBall();
    }

    private void ResetBall()
    {
        _px = FieldW / 2f;
        _pv = 0;
        _bx = _px;
        _by = PaddleY - 2;
        _vx = _vy = 0;
        _launchRequested = false;
    }

    private float BallSpeed => Math.Min(190f, 105f + (_level - 1) * 14f);

    private void Launch()
    {
        float angle = (float)(_rng.NextDouble() * 0.6 - 0.3); // radians from straight up
        _vx = MathF.Sin(angle) * BallSpeed;
        _vy = -MathF.Cos(angle) * BallSpeed;
        if (Mode == BreakoutMode.Serving) SetMode(BreakoutMode.Playing);
    }

    private bool BrickAt(float x, float y, out int index)
    {
        index = -1;
        int row = (int)MathF.Floor((y - BrickTop) / BrickH);
        int col = (int)MathF.Floor(x / BrickW);
        if ((uint)row >= Rows || (uint)col >= Cols) return false;
        index = row * Cols + col;
        return _bricks[index];
    }

    private void Hit(int index)
    {
        _bricks[index] = false;
        _brickCount--;
        _score += RowPoints[index / Cols] * 10;
        if (Mode == BreakoutMode.Playing && _score > HighScore) HighScore = _score;
    }

    private void MoveBall(float dt)
    {
        float dist = MathF.Max(MathF.Abs(_vx), MathF.Abs(_vy)) * dt;
        int steps = Math.Max(1, (int)MathF.Ceiling(dist)); // at most one pixel per sub-step
        float sdt = dt / steps;
        for (int i = 0; i < steps && Mode is BreakoutMode.Playing or BreakoutMode.Attract; i++)
        {
            // x axis
            float nx = _bx + _vx * sdt;
            if (nx < 1) { nx = 1; _vx = MathF.Abs(_vx); }
            else if (nx > FieldW - 2) { nx = FieldW - 2; _vx = -MathF.Abs(_vx); }
            else if (BrickAt(nx, _by, out int hx)) { Hit(hx); _vx = -_vx; nx = _bx; }
            _bx = nx;

            // y axis
            float ny = _by + _vy * sdt;
            if (ny < 1) { ny = 1; _vy = MathF.Abs(_vy); }
            else if (BrickAt(_bx, ny, out int hy)) { Hit(hy); _vy = -_vy; ny = _by; }
            else if (_vy > 0 && ny >= PaddleY - 1 && _by < PaddleY - 1 && MathF.Abs(_bx - _px) <= _paddleW / 2f + 1)
            {
                float offset = Math.Clamp((_bx - _px) / (_paddleW / 2f + 1), -1f, 1f);
                float angle = offset * 1.0f; // up to ~57 degrees from vertical
                float speed = BallSpeed;
                _vx = MathF.Sin(angle) * speed;
                _vy = -MathF.Cos(angle) * speed;
                ny = PaddleY - 2;
            }
            else if (ny > FieldH)
            {
                LoseBall();
                return;
            }
            _by = ny;

            if (_brickCount == 0)
            {
                _level++;
                BuildLevel();
                SetMode(Mode == BreakoutMode.Attract ? BreakoutMode.Attract : BreakoutMode.Serving);
                if (Mode == BreakoutMode.Attract) _launchRequested = true;
                return;
            }
        }
    }

    private void LoseBall()
    {
        _lives--;
        if (_lives <= 0)
        {
            if (Mode == BreakoutMode.Attract) { NewGame(); _launchRequested = true; return; } // the demo just starts over
            SetMode(BreakoutMode.Dead);
            return;
        }
        ResetBall();
        if (Mode == BreakoutMode.Attract) _launchRequested = true;
        else SetMode(BreakoutMode.Serving);
    }

    private void MovePaddle(float dt, int direction)
    {
        const float accel = 1500f, maxSpeed = 240f, friction = 1800f;
        if (direction != 0) _pv += direction * accel * dt;
        else
        {
            float drag = friction * dt;
            _pv = MathF.Abs(_pv) <= drag ? 0 : _pv - MathF.Sign(_pv) * drag;
        }
        _pv = Math.Clamp(_pv, -maxSpeed, maxSpeed);
        _px += _pv * dt;
        float half = _paddleW / 2f;
        if (_px < half) { _px = half; _pv = 0; }
        else if (_px > FieldW - half) { _px = FieldW - half; _pv = 0; }
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _now = context.Time;
        float dt = Math.Min((float)context.Delta.TotalSeconds, 0.05f);
        if (!_everInput && _lastInput == TimeSpan.Zero) _lastInput = _now;

        if (Mode is BreakoutMode.Title or BreakoutMode.Dead or BreakoutMode.Serving or BreakoutMode.Playing && _now - _lastInput >= IdleAfter)
        {
            NewGame();
            SetMode(BreakoutMode.Attract);
            _launchRequested = true;
        }

        int dir = 0;
        if (Mode == BreakoutMode.Attract)
        {
            // Bot: follow the ball, aiming a little off-centre so it varies its shots.
            float diff = _bx - _px;
            dir = diff > 3 ? 1 : diff < -3 ? -1 : 0;
        }
        else if (Mode is BreakoutMode.Serving or BreakoutMode.Playing)
        {
            if (Input.IsDown(0, InputButton.Left)) dir--;
            if (Input.IsDown(0, InputButton.Right)) dir++;
        }
        if (Mode is BreakoutMode.Serving or BreakoutMode.Playing or BreakoutMode.Attract) MovePaddle(dt, dir);

        if (Mode == BreakoutMode.Serving || Mode == BreakoutMode.Attract && _vy == 0)
        {
            _bx = _px;
            _by = PaddleY - 2;
            if (_launchRequested && (Mode == BreakoutMode.Serving || _now - _modeSince > TimeSpan.FromMilliseconds(600)))
            {
                _launchRequested = false;
                Launch();
            }
        }
        else if (Mode is BreakoutMode.Playing or BreakoutMode.Attract)
        {
            MoveBall(dt);
        }
    }

    // ---- drawing ---------------------------------------------------------------------------------------------------

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        Fonts.Load();
        DrawHud(frame);
        frame.Fill(new Rectangle(FieldX - 2, 0, 1, frame.Height), Edge);

        for (int r = 0; r < Rows; r++)
        {
            var color = RowColors[r];
            for (int c = 0; c < Cols; c++)
            {
                if (!_bricks[r * Cols + c]) continue;
                frame.Fill(new Rectangle(FieldX + c * BrickW, BrickTop + r * BrickH, BrickW - 1, BrickH - 1), color);
            }
        }

        int pw = _paddleW;
        frame.Fill(new Rectangle(FieldX + (int)MathF.Round(_px - pw / 2f), PaddleY, pw, PaddleH), Pixel.White);
        frame.Fill(new Rectangle(FieldX + (int)MathF.Round(_bx) - 1, (int)MathF.Round(_by) - 1, 2, 2), Gold);

        switch (Mode)
        {
            case BreakoutMode.Title:
                _title.Set(Fonts.Big, "BREAKOUT");
                _sub.Set(Fonts.QuiteSmall, "PRESS START");
                DrawPanel(frame, Gold, retry: false);
                break;
            case BreakoutMode.Serving:
                _sub.Set(Fonts.QuiteSmall, "PRESS A TO LAUNCH");
                if ((_now.Ticks / TimeSpan.TicksPerSecond) % 2 == 0)
                    _sub.Draw(frame, FieldX + (FieldW - _sub.Width) / 2, 44, Dim);
                break;
            case BreakoutMode.Dead:
                _title.Set(Fonts.Big, "GAME OVER");
                if (_shownResult != _score)
                {
                    _shownResult = _score;
                    _sub.Set(Fonts.QuiteSmall, _score >= HighScore && _score > 0 ? $"NEW BEST {_score}" : $"SCORE {_score}");
                }
                DrawPanel(frame, Gold, retry: true);
                break;
            case BreakoutMode.Attract:
                _demo.Set(Fonts.QuiteSmall, "DEMO - PRESS START");
                if ((_now.Ticks / (TimeSpan.TicksPerSecond / 2)) % 2 == 0)
                    _demo.Draw(frame, FieldX + (FieldW - _demo.Width) / 2, 40, Gold);
                break;
        }
    }

    private void DrawHud(FrameBuffer frame)
    {
        _scoreLabel.Set(Fonts.QuiteSmall, "SCORE");
        _bestLabel.Set(Fonts.QuiteSmall, "BEST");
        _levelRun.Set(Fonts.QuiteSmall, "LEVEL");
        var main = Mode == BreakoutMode.Attract ? Dim : Pixel.White;
        _scoreLabel.Draw(frame, 2, 2, Dim);
        _digits.Draw(frame, 2, 10, _score, main);
        _bestLabel.Draw(frame, 2, 21, Dim);
        _digits.Draw(frame, 2, 29, HighScore, Gold);
        _levelRun.Draw(frame, 2, 41, Dim);
        _digits.Draw(frame, 2 + _levelRun.Width + 4, 41, _level, main);
        for (int i = 0; i < _lives; i++)
            frame.Fill(new Rectangle(2 + i * 6, 54, 4, 4), Gold);
    }

    private void DrawPanel(FrameBuffer frame, Pixel color, bool retry)
    {
        int w = Math.Max(_title.Width, _sub.Width) + 16, h = _title.Height + _sub.Height + 12;
        int x = FieldX + (FieldW - w) / 2, y = 28 - h / 2;
        frame.Fill(new Rectangle(x, y, w, h), new Pixel(8, 8, 20));
        frame.DrawRect(new Rectangle(x, y, w, h), Edge);
        _title.Draw(frame, FieldX + (FieldW - _title.Width) / 2, y + 4, color);
        _sub.Draw(frame, FieldX + (FieldW - _sub.Width) / 2, y + 8 + _title.Height, Pixel.White);
        if (retry && (_now.Ticks / TimeSpan.TicksPerSecond) % 2 == 0)
        {
            _retry.Set(Fonts.QuiteSmall, "START TO RETRY");
            _retry.Draw(frame, FieldX + (FieldW - _retry.Width) / 2, y + h + 2, Dim);
        }
    }
}
