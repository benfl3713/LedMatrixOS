using LedMatrixOS.Core;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps;

public enum SnakeMode { Title, Playing, Dead, Attract }

/// <summary>
/// Classic snake on a 128x27 grid of 2 px cells. D-pad turns (180 degree reversals are ignored, one turn is queued), the snake grows and
/// speeds up with every piece of food, and walls either wrap or kill ([Setting]). Start/A begins a game. When nobody has touched a button for
/// 20 seconds a simple pathfinding bot plays so the display is never dead. The high score is an Advanced setting, so it is persisted.
/// Everything is preallocated: the body is a ring buffer, the bot's search uses fixed arrays, text is cached in <see cref="TextRun"/>s.
/// </summary>
public sealed class SnakeApp : SettingsAppBase, IInputConsumer
{
    public const int Cell = 2;
    public const int GridW = 128;
    public const int GridH = 27;
    public const int BoardY = 9;            // top of the board in pixels (HUD above)
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(20);

    private const int Cells = GridW * GridH;
    private static readonly int[] Dx = [0, 1, 0, -1]; // up, right, down, left
    private static readonly int[] Dy = [-1, 0, 1, 0];

    private static readonly Pixel BodyColor = new(40, 200, 90);
    private static readonly Pixel HeadColor = new(170, 255, 120);
    private static readonly Pixel FoodColor = new(255, 70, 70);
    private static readonly Pixel WallColor = new(70, 70, 120);
    private static readonly Pixel Dim = new(120, 120, 140);
    private static readonly Pixel Gold = new(255, 210, 90);

    private readonly int[] _body = new int[Cells];     // ring buffer of cell indexes, tail at _tail
    private readonly bool[] _occupied = new bool[Cells];
    private readonly int[] _queue = new int[Cells];    // BFS frontier
    private readonly int[] _firstStep = new int[Cells]; // first direction taken to reach a cell, -1 = unvisited
    private int _head, _tail, _length;
    private int _dir, _nextDir = -1;
    private int _food;
    private int _score;
    private Random _rng;
    private TimeSpan _now, _lastInput, _stepAcc, _modeSince;
    private bool _everInput;

    private readonly TextRun _hud = new(), _title = new(), _prompt = new(), _result = new(), _demo = new(), _retry = new();
    private readonly TextRun _hudBest = new();
    private NumberRun? _numbers;
    private NumberRun _digits => _numbers ??= new NumberRun(Fonts.QuiteSmall);
    private int _shownResult = -1;

    public override string Id => "snake";
    public override string Name => "Snake";
    public override int FrameRate => 30;

    [Setting("Wrap Walls", Description = "The snake reappears on the opposite side instead of dying at a wall.")]
    public bool WrapWalls { get; set; } = true;

    [Setting("High Score", Description = "Best score so far. Saved between runs.", Min = 0, Max = 99999, Advanced = true)]
    public int HighScore { get; set; }

    public SnakeMode Mode { get; private set; } = SnakeMode.Title;
    public int Score => _score;
    public int Length => _length;

    internal int HeadX => _body[_head] % GridW;
    internal int HeadY => _body[_head] / GridW;
    internal int Direction => _dir;
    internal int FoodX => _food % GridW;
    internal int FoodY => _food / GridW;
    internal void PutFood(int x, int y) => _food = y * GridW + x;

    [ActivatorUtilitiesConstructor]
    public SnakeApp() : this(new Random(Environment.TickCount)) { }

    public SnakeApp(Random rng)
    {
        _rng = rng;
        Reset();
    }

    // ---- input -----------------------------------------------------------------------------------------------------

    public void OnInput(in InputEvent e)
    {
        if (e.State != InputState.Down) return;
        _lastInput = _now;
        _everInput = true;

        switch (Mode)
        {
            case SnakeMode.Title:
            case SnakeMode.Dead:
                if (e.Button is InputButton.Start or InputButton.A) StartGame(SnakeMode.Playing);
                break;
            case SnakeMode.Attract:
                StartGame(SnakeMode.Playing);
                break;
            case SnakeMode.Playing:
                int d = e.Button switch { InputButton.Up => 0, InputButton.Right => 1, InputButton.Down => 2, InputButton.Left => 3, _ => -1 };
                if (d >= 0) Turn(d);
                break;
        }
    }

    private void Turn(int d)
    {
        // Compare against the last direction that will be in effect, so two quick taps can never fold the snake back on itself.
        int basis = _nextDir >= 0 ? _nextDir : _dir;
        if (d == basis || d == (basis + 2) % 4) return;
        if (_nextDir >= 0) return; // one turn queued at a time
        _nextDir = d;
    }

    // ---- game ------------------------------------------------------------------------------------------------------

    private void Reset()
    {
        Array.Clear(_occupied);
        _length = 4;
        _tail = 0;
        int cx = GridW / 2, cy = GridH / 2;
        for (int i = 0; i < _length; i++)
        {
            int idx = cy * GridW + cx - (_length - 1) + i;
            _body[i] = idx;
            _occupied[idx] = true;
        }
        _head = _length - 1;
        _dir = 1;
        _nextDir = -1;
        _score = 0;
        _stepAcc = TimeSpan.Zero;
        PlaceFood();
    }

    private void StartGame(SnakeMode mode)
    {
        Reset();
        SetMode(mode);
    }

    private void SetMode(SnakeMode mode)
    {
        Mode = mode;
        _modeSince = _now;
    }

    private void PlaceFood()
    {
        if (_length >= Cells) { _food = -1; return; }
        int idx = _rng.Next(Cells);
        while (_occupied[idx]) idx = (idx + 1) % Cells;
        _food = idx;
    }

    private TimeSpan StepInterval()
    {
        int ms = Math.Max(45, 115 - (_length - 4) * 2);
        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>Cell index after moving from <paramref name="idx"/> in direction <paramref name="d"/>, or -1 when that hits a wall.</summary>
    private int Neighbour(int idx, int d)
    {
        int x = idx % GridW + Dx[d], y = idx / GridW + Dy[d];
        if (WrapWalls)
        {
            x = (x + GridW) % GridW;
            y = (y + GridH) % GridH;
        }
        else if ((uint)x >= GridW || (uint)y >= GridH) return -1;
        return y * GridW + x;
    }

    private bool Step()
    {
        if (_nextDir >= 0) { _dir = _nextDir; _nextDir = -1; }
        int next = Neighbour(_body[_head], _dir);
        bool eating = next == _food;
        // The tail cell is vacated this step unless the snake is growing, so moving into it is legal.
        if (next < 0 || (_occupied[next] && !(!eating && next == _body[_tail]))) return false;

        if (!eating)
        {
            _occupied[_body[_tail]] = false;
            _tail = (_tail + 1) % Cells;
            _length--;
        }
        _head = (_head + 1) % Cells;
        _body[_head] = next;
        _occupied[next] = true;
        _length++;

        if (eating)
        {
            _score += 10;
            if (Mode == SnakeMode.Playing && _score > HighScore) HighScore = _score;
            PlaceFood();
        }
        return true;
    }

    /// <summary>Bot: breadth first search to the food; otherwise any move that does not die, preferring the one that keeps the most room.</summary>
    private void ChooseBotDirection()
    {
        int start = _body[_head];
        Array.Fill(_firstStep, -1);
        int qh = 0, qt = 0;
        _firstStep[start] = 4; // marker: visited
        for (int d = 0; d < 4; d++)
        {
            if (d == (_dir + 2) % 4) continue;
            int n = Neighbour(start, d);
            if (n < 0 || _occupied[n] || _firstStep[n] >= 0) continue;
            _firstStep[n] = d;
            _queue[qt++] = n;
        }
        while (qh < qt && _food >= 0 && _firstStep[_food] < 0)
        {
            int cur = _queue[qh++];
            for (int d = 0; d < 4; d++)
            {
                int n = Neighbour(cur, d);
                if (n < 0 || _occupied[n] || _firstStep[n] >= 0) continue;
                _firstStep[n] = _firstStep[cur];
                _queue[qt++] = n;
            }
        }

        int choice = -1;
        if (_food >= 0 && _firstStep[_food] is >= 0 and < 4) choice = _firstStep[_food];
        else
        {
            // No path: take the safe neighbour that reaches the most cells (the BFS above flooded everything reachable).
            int best = -1;
            for (int d = 0; d < 4; d++)
            {
                if (d == (_dir + 2) % 4) continue;
                int n = Neighbour(start, d);
                if (n < 0 || _occupied[n]) continue;
                int room = 0;
                for (int i = 0; i < qt; i++) if (_firstStep[_queue[i]] == d) room++;
                if (room > best) { best = room; choice = d; }
            }
        }
        if (choice >= 0) _nextDir = choice;
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _now = context.Time;
        if (!_everInput && _lastInput == TimeSpan.Zero) _lastInput = _now;

        if (Mode is SnakeMode.Title or SnakeMode.Dead or SnakeMode.Playing && _now - _lastInput >= IdleAfter)
        {
            StartGame(SnakeMode.Attract);
        }

        if (Mode is SnakeMode.Playing or SnakeMode.Attract)
        {
            _stepAcc += context.Delta;
            var interval = Mode == SnakeMode.Attract ? StepInterval() / 2 : StepInterval();
            int guard = 0;
            while (_stepAcc >= interval && guard++ < 4)
            {
                _stepAcc -= interval;
                if (Mode == SnakeMode.Attract) ChooseBotDirection();
                if (!Step())
                {
                    if (Mode == SnakeMode.Attract) Reset(); // the demo just starts over
                    else SetMode(SnakeMode.Dead);
                    break;
                }
            }
        }
    }

    // ---- drawing ---------------------------------------------------------------------------------------------------

    private void FillCell(FrameBuffer frame, int idx, Pixel color)
    {
        int x = idx % GridW * Cell, y = idx / GridW * Cell + BoardY;
        frame.Fill(new Rectangle(x, y, Cell, Cell), color);
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        Fonts.Load();
        _hud.Set(Fonts.QuiteSmall, "SCORE");
        _hudBest.Set(Fonts.QuiteSmall, "BEST");
        var main = Mode == SnakeMode.Attract ? Dim : Pixel.White;
        _hud.Draw(frame, 2, 1, Dim);
        int bx = _digits.Draw(frame, 2 + _hud.Width + 4, 1, _score, main);
        _hudBest.Draw(frame, bx + 12, 1, Dim);
        _digits.Draw(frame, bx + 12 + _hudBest.Width + 4, 1, HighScore, Gold);
        frame.Fill(new Rectangle(0, BoardY - 1, frame.Width, 1), WallColor);
        if (!WrapWalls)
        {
            frame.Fill(new Rectangle(0, frame.Height - 1, frame.Width, 1), WallColor);
        }

        if (_food >= 0) FillCell(frame, _food, FoodColor);
        for (int i = 0, idx = _tail; i < _length; i++, idx = (idx + 1) % Cells)
            FillCell(frame, _body[idx], i == _length - 1 ? HeadColor : BodyColor);

        switch (Mode)
        {
            case SnakeMode.Title:
                _title.Set(Fonts.Big, "SNAKE");
                _prompt.Set(Fonts.QuiteSmall, "PRESS START");
                DrawPanel(frame, _title, _prompt, Gold);
                break;
            case SnakeMode.Dead:
                _title.Set(Fonts.Big, "GAME OVER");
                if (_shownResult != _score)
                {
                    _shownResult = _score;
                    _result.Set(Fonts.QuiteSmall, _score >= HighScore && _score > 0 ? $"NEW BEST {_score}" : $"SCORE {_score}");
                }
                DrawPanel(frame, _title, _result, Gold, extra: true);
                break;
            case SnakeMode.Attract:
                _demo.Set(Fonts.QuiteSmall, "DEMO - PRESS START");
                if ((_now.Ticks / (TimeSpan.TicksPerSecond / 2)) % 2 == 0)
                    _demo.Draw(frame, frame.Width - _demo.Width - 2, 1, Gold);
                break;
        }
    }

    private void DrawPanel(FrameBuffer frame, TextRun head, TextRun sub, Pixel color, bool extra = false)
    {
        int w = Math.Max(head.Width, sub.Width) + 16, h = head.Height + sub.Height + 12;
        int x = (frame.Width - w) / 2, y = BoardY + (frame.Height - BoardY - h) / 2;
        frame.Fill(new Rectangle(x, y, w, h), new Pixel(8, 8, 20));
        frame.DrawRect(new Rectangle(x, y, w, h), WallColor);
        head.Draw(frame, (frame.Width - head.Width) / 2, y + 4, color);
        sub.Draw(frame, (frame.Width - sub.Width) / 2, y + 8 + head.Height, Pixel.White);
        if (extra && (_now.Ticks / (TimeSpan.TicksPerSecond)) % 2 == 0)
        {
            _retry.Set(Fonts.QuiteSmall, "START TO RETRY");
            _retry.Draw(frame, (frame.Width - _retry.Width) / 2, y + h + 1, Dim);
        }
    }
}
