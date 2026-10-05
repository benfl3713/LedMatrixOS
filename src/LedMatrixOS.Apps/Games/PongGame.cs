using LedMatrixOS.Core.Input;

namespace LedMatrixOS.Apps.Games;

public enum PongState { Serving, Playing, Paused, MatchOver }

/// <summary>
/// Two-sided Pong on a width x height field with no drawing and no clock of its own. Player 0 owns the left paddle and player 1
/// the right; a side with no input in the last <see cref="HumanTimeout"/> seconds is played by an AI, so one player gets an AI
/// opponent and an untouched screen is an AI-vs-AI demo. Seeded RNG, no allocation after construction.
/// </summary>
public sealed class PongGame
{
    public const float HumanTimeout = 10f;
    public const int PaddleW = 3, PaddleH = 12, BallSize = 3, Margin = 4;
    private const float PaddleSpeed = 80f, StartSpeed = 95f, MaxSpeed = 230f, SpeedPerHit = 7f, ServeDelay = 1f, OverRestart = 6f;

    private readonly Random _rng;
    private PongState _resume = PongState.Playing;
    private readonly float[] _sinceInput = [float.MaxValue, float.MaxValue];
    private readonly float[] _aiAim = new float[2];
    private float _stateTimer, _speed;
    private int _serveDir = 1;

    public PongGame(int width, int height, int seed = 1)
    {
        Width = width; Height = height;
        _rng = new Random(seed);
        PaddleY[0] = PaddleY[1] = (height - PaddleH) / 2f;
        ResetBall();
    }

    public int Width { get; }
    public int Height { get; }
    public float[] PaddleY { get; } = new float[2];
    public int[] Score { get; } = new int[2];
    public float BallX { get; private set; }
    public float BallY { get; private set; }
    public float BallVx { get; private set; }
    public float BallVy { get; private set; }
    public PongState State { get; private set; } = PongState.Serving;
    public int Winner { get; private set; } = -1;
    /// <summary>First to this many points wins.</summary>
    public int PointsToWin { get; set; } = 7;
    /// <summary>0 easy, 1 normal, 2 hard.</summary>
    public int Difficulty { get; set; } = 1;

    public int LeftX => Margin;
    public int RightX => Width - Margin - PaddleW;
    public bool IsHuman(int side) => _sinceInput[side] < HumanTimeout;

    // ---- input -----------------------------------------------------------------------------------------------------------------

    public void Press(int player, InputButton button)
    {
        if ((uint)player > 1u) return;
        _sinceInput[player] = 0;
        if (State == PongState.MatchOver)
        {
            if (_stateTimer >= 1f) NewMatch();
            return;
        }
        if (button is InputButton.Up or InputButton.Down)   // a tap (down+up in one frame) is never seen as "held", so nudge
            PaddleY[player] = Math.Clamp(PaddleY[player] + (button == InputButton.Up ? -6f : 6f), 0f, Height - PaddleH);
        else if (button == InputButton.Start)
        {
            if (State == PongState.Paused) { State = _resume; }
            else { _resume = State; State = PongState.Paused; }
        }
    }

    

    /// <summary>Marks a held direction as activity so a player who is only holding a key still counts as present.</summary>
    public void Update(float dt, bool p0Up, bool p0Down, bool p1Up, bool p1Down)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);
        if (p0Up || p0Down) _sinceInput[0] = 0; else _sinceInput[0] = Math.Min(_sinceInput[0] + dt, 1e6f);
        if (p1Up || p1Down) _sinceInput[1] = 0; else _sinceInput[1] = Math.Min(_sinceInput[1] + dt, 1e6f);

        if (State == PongState.Paused) return;
        _stateTimer += dt;

        MovePaddle(0, dt, p0Up, p0Down);
        MovePaddle(1, dt, p1Up, p1Down);

        switch (State)
        {
            case PongState.Serving:
                if (_stateTimer >= ServeDelay) Launch();
                break;
            case PongState.Playing:
                MoveBall(dt);
                break;
            case PongState.MatchOver:
                if (_stateTimer >= OverRestart) NewMatch();
                break;
        }
    }

    // ---- simulation ------------------------------------------------------------------------------------------------------------

    private void MovePaddle(int side, float dt, bool up, bool down)
    {
        float y = PaddleY[side];
        if (IsHuman(side))
        {
            if (up != down) y += (up ? -1 : 1) * PaddleSpeed * dt;
        }
        else
        {
            float speed = Difficulty switch { 0 => 40f, 1 => 62f, _ => 95f };
            float target = Height / 2f - PaddleH / 2f;
            bool incoming = State == PongState.Playing && (side == 0 ? BallVx < 0 : BallVx > 0);
            if (incoming) target = Predict(side) - PaddleH / 2f + _aiAim[side];
            float diff = target - y;
            float step = speed * dt;
            if (Math.Abs(diff) > 1f) y += Math.Clamp(diff, -step, step);
        }
        PaddleY[side] = Math.Clamp(y, 0f, Height - PaddleH);
    }

    /// <summary>Where the ball will cross this side's paddle line, folding wall bounces.</summary>
    private float Predict(int side)
    {
        float targetX = side == 0 ? LeftX + PaddleW : RightX - BallSize;
        float t = (targetX - BallX) / BallVx;
        if (t < 0) return BallY;
        float span = Height - BallSize;
        float y = BallY - BallSize / 2f + BallVy * t;
        float m = y % (2 * span);
        if (m < 0) m += 2 * span;
        return (m > span ? 2 * span - m : m) + BallSize / 2f;
    }

    private void MoveBall(float dt)
    {
        float dx = BallVx * dt, dy = BallVy * dt;
        int steps = Math.Max(1, (int)MathF.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 1.5f));
        float sx = dx / steps, sy = dy / steps;
        for (int i = 0; i < steps; i++)
        {
            BallX += sx; BallY += sy;
            float half = BallSize / 2f;
            if (BallY < half) { BallY = half; BallVy = Math.Abs(BallVy); }
            else if (BallY > Height - half) { BallY = Height - half; BallVy = -Math.Abs(BallVy); }

            if (BallVx < 0 && BallX - half <= LeftX + PaddleW && BallX + half >= LeftX && Hits(0)) Bounce(0);
            else if (BallVx > 0 && BallX + half >= RightX && BallX - half <= RightX + PaddleW && Hits(1)) Bounce(1);

            if (BallX < -half) { Point(1); return; }
            if (BallX > Width + half) { Point(0); return; }
        }
    }

    private bool Hits(int side) => BallY + BallSize / 2f >= PaddleY[side] && BallY - BallSize / 2f <= PaddleY[side] + PaddleH;

    private void Bounce(int side)
    {
        float offset = Math.Clamp((BallY - (PaddleY[side] + PaddleH / 2f)) / (PaddleH / 2f + 1f), -1f, 1f);
        _speed = Math.Min(MaxSpeed, _speed + SpeedPerHit);
        float angle = offset * 0.95f;                       // up to ~55 degrees
        float dir = side == 0 ? 1f : -1f;
        BallVx = dir * _speed * MathF.Cos(angle);
        BallVy = _speed * MathF.Sin(angle);
        BallX = side == 0 ? LeftX + PaddleW + BallSize / 2f + 0.01f : RightX - BallSize / 2f - 0.01f;
        RollAim(1 - side);
    }

    private void RollAim(int side)
    {
        float err = Difficulty switch { 0 => 12f, 1 => 8f, _ => 5f };
        _aiAim[side] = ((float)_rng.NextDouble() * 2 - 1) * err;
    }

    private void Point(int scorer)
    {
        Score[scorer]++;
        _serveDir = scorer == 0 ? -1 : 1;                   // the side that conceded receives the next serve
        if (Score[scorer] >= PointsToWin)
        {
            Winner = scorer;
            State = PongState.MatchOver;
            _stateTimer = 0;
            return;
        }
        ResetBall();
        State = PongState.Serving;
        _stateTimer = 0;
    }

    private void ResetBall()
    {
        BallX = Width / 2f; BallY = Height / 2f;
        BallVx = BallVy = 0;
        _speed = StartSpeed;
    }

    private void Launch()
    {
        float angle = ((float)_rng.NextDouble() * 2 - 1) * 0.5f;
        BallVx = _serveDir * _speed * MathF.Cos(angle);
        BallVy = _speed * MathF.Sin(angle);
        State = PongState.Playing;
        RollAim(_serveDir > 0 ? 1 : 0);
    }

    public void NewMatch()
    {
        Score[0] = Score[1] = 0;
        Winner = -1;
        PaddleY[0] = PaddleY[1] = (Height - PaddleH) / 2f;
        ResetBall();
        State = PongState.Serving;
        _stateTimer = 0;
    }
}
