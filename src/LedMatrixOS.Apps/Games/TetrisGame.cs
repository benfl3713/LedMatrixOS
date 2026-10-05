using LedMatrixOS.Core.Input;

namespace LedMatrixOS.Apps.Games;

public enum TetrisState { Ready, Playing, Paused, Clearing, GameOver }

/// <summary>
/// Tetris rules with no drawing and no clock of its own: <see cref="Update"/> is fed the frame delta and the held directions, and
/// <see cref="Press"/> the button presses, so a given seed and input sequence always plays out the same. The 7-bag, the board and
/// the AI's scratch board are allocated once; nothing allocates while playing.
/// </summary>
public sealed class TetrisGame
{
    public const int Cols = 10, Rows = 20;
    public const float IdleBeforeAttract = 20f;
    public const float ClearTime = 0.45f;

    private const float DasDelay = 0.17f, DasRepeat = 0.05f, AiStep = 0.06f, SoftDropInterval = 0.05f, AttractRestart = 3f;

    // Piece shapes: cells (x, y) in a size x size box, rotated clockwise (x, y) -> (size - 1 - y, x).
    private static readonly int[] BoxSize = [4, 2, 3, 3, 3, 3, 3];
    private static readonly int[][] Base =
    [
        [0, 1, 1, 1, 2, 1, 3, 1],   // I
        [0, 0, 1, 0, 0, 1, 1, 1],   // O
        [1, 0, 0, 1, 1, 1, 2, 1],   // T
        [1, 0, 2, 0, 0, 1, 1, 1],   // S
        [0, 0, 1, 0, 1, 1, 2, 1],   // Z
        [0, 0, 0, 1, 1, 1, 2, 1],   // J
        [2, 0, 0, 1, 1, 1, 2, 1],   // L
    ];
    /// <summary>Shapes[piece][rotation] = 4 (x, y) pairs.</summary>
    public static readonly int[][][] Shapes = BuildShapes();
    private static readonly float[] Intervals = BuildIntervals();
    private static readonly int[] KickX = [0, -1, 1, 0, -2, 2];
    private static readonly int[] KickY = [0, 0, 0, -1, 0, 0];

    private readonly byte[] _board = new byte[Cols * Rows];          // 0 empty, 1..7 piece colour
    private readonly byte[] _scratch = new byte[Cols * Rows];
    private readonly int[] _bag = new int[7];
    private readonly int[] _clearRows = new int[4];
    private readonly Random _rng;
    private int _bagIndex = 7;
    private float _gravity, _das, _clearTimer, _idle, _aiTimer, _overTimer, _clock;
    private int _heldDir;
    private int _aiRot, _aiX;
    private bool _aiPlanned;

    public TetrisGame(int seed = 1, int highScore = 0)
    {
        _rng = new Random(seed);
        HighScore = highScore;
        Next = DrawFromBag();
    }

    public TetrisState State { get; private set; } = TetrisState.Ready;
    public bool Attract { get; private set; }
    public int Score { get; private set; }
    public int Lines { get; private set; }
    public int Level => Lines / 10;
    public int HighScore { get; set; }
    public int Piece { get; private set; }      // 0-6
    public int Rotation { get; private set; }
    public int PieceX { get; private set; }
    public int PieceY { get; private set; }
    public int Next { get; private set; }
    public int ClearRowCount { get; private set; }
    public float Clock => _clock;

    public int CellAt(int x, int y) => _board[y * Cols + x];
    public bool IsClearingRow(int y)
    {
        for (int i = 0; i < ClearRowCount; i++) if (_clearRows[i] == y) return true;
        return false;
    }

    // ---- flow ------------------------------------------------------------------------------------------------------------------

    public void StartGame(bool attract)
    {
        Array.Clear(_board);
        Score = 0; Lines = 0; _gravity = 0; _das = 0; _heldDir = 0; ClearRowCount = 0;
        Attract = attract;
        _aiPlanned = false;
        Spawn();
    }

    private void Spawn()
    {
        Piece = Next;
        Next = DrawFromBag();
        Rotation = 0;
        PieceX = (Cols - BoxSize[Piece]) / 2;
        PieceY = 0;
        _gravity = 0;
        _aiPlanned = false;
        if (!Fits(Piece, Rotation, PieceX, PieceY, _board)) EndGame();
        else State = TetrisState.Playing;
    }

    private void EndGame()
    {
        State = TetrisState.GameOver;
        _overTimer = 0;
        if (!Attract && Score > HighScore) HighScore = Score;
    }

    private int DrawFromBag()
    {
        if (_bagIndex >= 7)
        {
            for (int i = 0; i < 7; i++) _bag[i] = i;
            for (int i = 6; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
            }
            _bagIndex = 0;
        }
        return _bag[_bagIndex++];
    }

    // ---- input -----------------------------------------------------------------------------------------------------------------

    /// <summary>A button went down (a tap arrives as down then up in one frame, so react here rather than polling).</summary>
    public void Press(InputButton button)
    {
        PressCore(button);
        if (!Attract && Score > HighScore) HighScore = Score;
    }

    private void PressCore(InputButton button)
    {
        _idle = 0;
        if (Attract) { StartGame(false); return; }

        switch (State)
        {
            case TetrisState.Ready:
                StartGame(false);
                return;
            case TetrisState.GameOver:
                if (_overTimer >= 1f) StartGame(false);
                return;
            case TetrisState.Paused:
                if (button == InputButton.Start) State = TetrisState.Playing;
                return;
            case TetrisState.Clearing:
                return;
        }

        switch (button)
        {
            case InputButton.Left: TryMove(-1, 0); break;
            case InputButton.Right: TryMove(1, 0); break;
            case InputButton.Down: if (TryMove(0, 1)) Score++; _gravity = 0; break;
            case InputButton.A: TryRotate(1); break;
            case InputButton.B: TryRotate(3); break;
            case InputButton.Up: HardDrop(); break;
            case InputButton.Start: State = TetrisState.Paused; break;
        }
    }

    // ---- simulation ------------------------------------------------------------------------------------------------------------

    public void Update(float dt, bool left, bool right, bool down)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);
        _clock += dt;
        if (left || right || down) _idle = 0; else _idle += dt;

        switch (State)
        {
            case TetrisState.Ready:
                if (_idle >= IdleBeforeAttract) StartGame(true);
                break;
            case TetrisState.GameOver:
                _overTimer += dt;
                if (Attract && _overTimer >= AttractRestart) StartGame(true);
                else if (!Attract && _idle >= IdleBeforeAttract) StartGame(true);
                break;
            case TetrisState.Clearing:
                _clearTimer -= dt;
                if (_clearTimer <= 0) FinishClear();
                break;
            case TetrisState.Playing:
                if (Attract) RunAi(dt);
                else HandleHeld(dt, left, right);
                Gravity(dt, down && !Attract);
                break;
        }
        if (!Attract && Score > HighScore) HighScore = Score;
    }

    private void HandleHeld(float dt, bool left, bool right)
    {
        int dir = left == right ? 0 : left ? -1 : 1;
        if (dir != _heldDir) { _heldDir = dir; _das = 0; return; }
        if (dir == 0) return;
        _das += dt;
        while (_das >= DasDelay)
        {
            _das -= DasRepeat;
            TryMove(dir, 0);
        }
    }

    private void Gravity(float dt, bool softDrop)
    {
        if (State != TetrisState.Playing) return;
        float interval = Intervals[Math.Min(Level, Intervals.Length - 1)];
        if (softDrop) interval = Math.Min(interval, SoftDropInterval);
        _gravity += dt;
        for (int guard = 0; _gravity >= interval && guard < 8 && State == TetrisState.Playing; guard++)
        {
            _gravity -= interval;
            if (TryMove(0, 1)) { if (softDrop) Score++; }
            else { Lock(); break; }
        }
    }

    private void HardDrop()
    {
        int cells = 0;
        while (TryMove(0, 1)) cells++;
        Score += cells * 2;
        Lock();
    }

    private void Lock()
    {
        var shape = Shapes[Piece][Rotation];
        for (int i = 0; i < 4; i++)
        {
            int x = PieceX + shape[i * 2], y = PieceY + shape[i * 2 + 1];
            if (y >= 0) _board[y * Cols + x] = (byte)(Piece + 1);
        }

        ClearRowCount = 0;
        for (int y = 0; y < Rows; y++)
        {
            bool full = true;
            for (int x = 0; x < Cols; x++) if (_board[y * Cols + x] == 0) { full = false; break; }
            if (full && ClearRowCount < 4) _clearRows[ClearRowCount++] = y;
        }

        if (ClearRowCount > 0) { State = TetrisState.Clearing; _clearTimer = ClearTime; }
        else Spawn();
    }

    private void FinishClear()
    {
        int n = ClearRowCount;
        int dst = Rows - 1;
        for (int src = Rows - 1; src >= 0; src--)
        {
            if (IsClearingRow(src)) continue;
            if (dst != src) Array.Copy(_board, src * Cols, _board, dst * Cols, Cols);
            dst--;
        }
        for (; dst >= 0; dst--) Array.Clear(_board, dst * Cols, Cols);

        ClearRowCount = 0;
        int before = Level;
        Lines += n;
        Score += n switch { 1 => 100, 2 => 300, 3 => 500, _ => 800 } * (before + 1);
        if (!Attract && Score > HighScore) HighScore = Score;
        Spawn();
    }

    // ---- movement --------------------------------------------------------------------------------------------------------------

    private bool Fits(int piece, int rot, int px, int py, byte[] board)
    {
        var shape = Shapes[piece][rot];
        for (int i = 0; i < 4; i++)
        {
            int x = px + shape[i * 2], y = py + shape[i * 2 + 1];
            if (x < 0 || x >= Cols || y >= Rows) return false;
            if (y >= 0 && board[y * Cols + x] != 0) return false;
        }
        return true;
    }

    private bool TryMove(int dx, int dy)
    {
        if (!Fits(Piece, Rotation, PieceX + dx, PieceY + dy, _board)) return false;
        PieceX += dx; PieceY += dy;
        return true;
    }

    private bool TryRotate(int turns)
    {
        int rot = (Rotation + turns) & 3;
        for (int k = 0; k < KickX.Length; k++)
        {
            if (!Fits(Piece, rot, PieceX + KickX[k], PieceY + KickY[k], _board)) continue;
            Rotation = rot; PieceX += KickX[k]; PieceY += KickY[k];
            return true;
        }
        return false;
    }

    /// <summary>Row where the current piece would land (for the ghost).</summary>
    public int GhostY()
    {
        int y = PieceY;
        while (Fits(Piece, Rotation, PieceX, y + 1, _board)) y++;
        return y;
    }

    // ---- attract-mode AI -------------------------------------------------------------------------------------------------------

    private void RunAi(float dt)
    {
        if (!_aiPlanned) { PlanMove(); _aiPlanned = true; _aiTimer = 0; }
        _aiTimer += dt;
        while (_aiTimer >= AiStep && State == TetrisState.Playing)
        {
            _aiTimer -= AiStep;
            if (Rotation != _aiRot) { if (!TryRotate(1)) _aiRot = Rotation; }
            else if (PieceX < _aiX) { if (!TryMove(1, 0)) _aiX = PieceX; }
            else if (PieceX > _aiX) { if (!TryMove(-1, 0)) _aiX = PieceX; }
            else HardDrop();
        }
    }

    private void PlanMove()
    {
        float best = float.MinValue;
        _aiRot = Rotation; _aiX = PieceX;
        int rots = Piece == 1 ? 1 : 4;
        for (int rot = 0; rot < rots; rot++)
            for (int x = -2; x < Cols; x++)
            {
                if (!Fits(Piece, rot, x, 0, _board)) continue;
                int y = 0;
                while (Fits(Piece, rot, x, y + 1, _board)) y++;
                float score = Evaluate(rot, x, y);
                if (score > best) { best = score; _aiRot = rot; _aiX = x; }
            }
    }

    private float Evaluate(int rot, int px, int py)
    {
        Array.Copy(_board, _scratch, _board.Length);
        var shape = Shapes[Piece][rot];
        for (int i = 0; i < 4; i++)
        {
            int x = px + shape[i * 2], y = py + shape[i * 2 + 1];
            if (y >= 0) _scratch[y * Cols + x] = 1;
        }

        int lines = 0;
        for (int y = 0; y < Rows; y++)
        {
            bool full = true;
            for (int x = 0; x < Cols; x++) if (_scratch[y * Cols + x] == 0) { full = false; break; }
            if (full) lines++;
        }

        int aggregate = 0, holes = 0, bumpiness = 0, prev = -1;
        for (int x = 0; x < Cols; x++)
        {
            int height = 0;
            bool seen = false;
            for (int y = 0; y < Rows; y++)
            {
                if (_scratch[y * Cols + x] != 0) { if (!seen) { height = Rows - y; seen = true; } }
                else if (seen) holes++;
            }
            aggregate += height;
            if (prev >= 0) bumpiness += Math.Abs(height - prev);
            prev = height;
        }
        return -0.51f * aggregate + 0.76f * lines - 0.36f * holes - 0.18f * bumpiness;
    }

    // ---- tables ----------------------------------------------------------------------------------------------------------------

    private static int[][][] BuildShapes()
    {
        var all = new int[7][][];
        for (int p = 0; p < 7; p++)
        {
            all[p] = new int[4][];
            all[p][0] = (int[])Base[p].Clone();
            for (int r = 1; r < 4; r++)
            {
                var prev = all[p][r - 1];
                var cur = new int[8];
                for (int i = 0; i < 4; i++)
                {
                    cur[i * 2] = BoxSize[p] - 1 - prev[i * 2 + 1];
                    cur[i * 2 + 1] = prev[i * 2];
                }
                all[p][r] = cur;
            }
        }
        return all;
    }

    private static float[] BuildIntervals()
    {
        var t = new float[30];
        for (int i = 0; i < t.Length; i++) t[i] = Math.Max(0.06f, 0.8f * MathF.Pow(0.82f, i));
        return t;
    }
}
