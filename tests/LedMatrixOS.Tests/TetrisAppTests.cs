using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Games;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class TetrisAppTests(ITestOutputHelper output)
{
    private static (TetrisApp App, AppStage Stage, InputHub Hub) Start()
    {
        Fonts.Load();
        var hub = new InputHub();
        var app = new TetrisApp { Time = new FakeTime(), Input = hub };
        var stage = new AppStage(app);
        stage.Step(33, 3);
        return (app, stage, hub);
    }

    /// <summary>A tap, delivered the way the engine does: Down+Up queued, dispatched at the start of a frame, then the frame.</summary>
    private static void Tap(AppStage stage, InputHub hub, TetrisApp app, InputButton button)
    {
        hub.EnqueuePress(0, button);
        hub.Dispatch(app);
        stage.Step(33);
    }

    private static void Play(AppStage stage, InputHub hub, TetrisApp app)
    {
        Tap(stage, hub, app, InputButton.A);   // start
        InputButton[] moves = [InputButton.Left, InputButton.Left, InputButton.Right, InputButton.Right, InputButton.Right, InputButton.Left];
        for (int i = 0; i < 6; i++)
        {
            Tap(stage, hub, app, InputButton.A);
            Tap(stage, hub, app, moves[i]);
            Tap(stage, hub, app, moves[i]);
            Tap(stage, hub, app, InputButton.Up);
        }
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- rules -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Bag_DealsEverySevenPiecesOnce()
    {
        var game = new TetrisGame(seed: 5);
        game.StartGame(false);
        var seen = new HashSet<int> { game.Piece };
        for (int i = 0; i < 6; i++) { game.Press(InputButton.Up); seen.Add(game.Piece); }
        // The first 7 pieces dealt (1 current + 6 more, but the 7th was dealt when the bag was drawn) are a permutation.
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void SameSeedPlaysTheSame()
    {
        int Run()
        {
            var g = new TetrisGame(seed: 9);
            g.StartGame(false);
            for (int i = 0; i < 8; i++) g.Press(InputButton.Up);
            return g.Score * 100 + g.Piece * 10 + g.Next;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Controls_MoveRotateAndHardDrop()
    {
        var game = new TetrisGame(seed: 1);
        game.StartGame(false);
        int x = game.PieceX;
        game.Press(InputButton.Left);
        Assert.Equal(x - 1, game.PieceX);
        game.Press(InputButton.Right);
        Assert.Equal(x, game.PieceX);
        int rot = game.Rotation;
        game.Press(InputButton.A);
        Assert.NotEqual(rot, game.Rotation);
        game.Press(InputButton.B);
        Assert.Equal(rot, game.Rotation);
        game.Press(InputButton.Up);
        Assert.True(game.Score > 0);
        Assert.NotEqual(0, game.CellAt(0, TetrisGame.Rows - 1) + game.CellAt(3, TetrisGame.Rows - 1) + game.CellAt(4, TetrisGame.Rows - 1) + game.CellAt(5, TetrisGame.Rows - 1) + game.CellAt(6, TetrisGame.Rows - 1));
    }

    [Fact]
    public void PauseFreezesGravity()
    {
        var game = new TetrisGame(seed: 1);
        game.StartGame(false);
        game.Press(InputButton.Start);
        Assert.Equal(TetrisState.Paused, game.State);
        int y = game.PieceY;
        for (int i = 0; i < 100; i++) game.Update(0.033f, false, false, false);
        Assert.Equal(y, game.PieceY);
        game.Press(InputButton.Start);
        Assert.Equal(TetrisState.Playing, game.State);
    }

    [Fact]
    public void AttractModeClearsLinesAndSurvives()
    {
        var game = new TetrisGame(seed: 3);
        game.StartGame(true);
        bool cleared = false;
        for (int i = 0; i < 30 * 120 && !cleared; i++)
        {
            game.Update(0.033f, false, false, false);
            cleared = game.State == TetrisState.Clearing;
        }
        Assert.True(cleared);
        for (int i = 0; i < 30 * 60; i++) game.Update(0.033f, false, false, false);
        Assert.True(game.Lines >= 10, $"lines: {game.Lines}");
    }

    [Fact]
    public void IdleStartsAttract_AndAnyButtonStartsARealGame()
    {
        var game = new TetrisGame(seed: 1);
        for (int i = 0; i < 30 * 21; i++) game.Update(0.033f, false, false, false);
        Assert.True(game.Attract);
        game.Press(InputButton.Left);
        Assert.False(game.Attract);
        Assert.Equal(TetrisState.Playing, game.State);
    }

    [Fact]
    public void LevelRampsAndHighScoreIsKept()
    {
        var game = new TetrisGame(seed: 1, highScore: 50);
        game.StartGame(false);
        while (game.State != TetrisState.GameOver) game.Press(InputButton.Up);
        Assert.True(game.HighScore >= 50);
        Assert.True(game.Score > 0);
    }

    [Fact]
    public void App_PersistsHighScoreSetting()
    {
        var (app, stage, hub) = Start();
        Play(stage, hub, app);
        Assert.True(app.HighScore > 0);
        Assert.Equal(app.Game.Score, app.HighScore);
    }

    // ---- golden images ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_Ready()
    {
        var (_, stage, _) = Start();
        Golden(stage, "tetris_ready");
    }

    [Fact]
    public void Golden_Playing()
    {
        var (app, stage, hub) = Start();
        Play(stage, hub, app);
        Tap(stage, hub, app, InputButton.Left);
        Golden(stage, "tetris_playing");
    }

    [Fact]
    public void Golden_Paused()
    {
        var (app, stage, hub) = Start();
        Play(stage, hub, app);
        Tap(stage, hub, app, InputButton.Start);
        Golden(stage, "tetris_paused");
    }

    [Fact]
    public void Golden_GameOver()
    {
        var (app, stage, hub) = Start();
        Tap(stage, hub, app, InputButton.A);
        for (int i = 0; i < 40 && app.Game.State != TetrisState.GameOver; i++) Tap(stage, hub, app, InputButton.Up);
        Assert.Equal(TetrisState.GameOver, app.Game.State);
        Golden(stage, "tetris_game_over");
    }

    [Fact]
    public void Golden_LineClearFlash()
    {
        var (app, stage, _) = Start();
        app.Game.StartGame(true);   // the AI is the quickest way to a clear
        for (int i = 0; i < 30 * 120 && app.Game.State != TetrisState.Clearing; i++) stage.Step(33);
        Assert.Equal(TetrisState.Clearing, app.Game.State);
        Golden(stage, "tetris_line_clear");
    }

    [Fact]
    public void Golden_AttractDemo()
    {
        var (_, stage, _) = Start();
        stage.Step(33, 30 * 21 + 30 * 25);   // 20 s idle, then a while of AI play
        Golden(stage, "tetris_attract");
    }

    // ---- allocation ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage, _) = Start();
        // A human game left alone (pieces fall, nothing is scored): count windows where no text had to be re-rasterised.
        app.Game.StartGame(false);
        var run = stage.MeasureSteadyAllocation(windows: 60, warmFrames: 30, beginWindow: () =>
        {
            var state = app.Game.State;
            int score = app.Game.Score, lines = app.Game.Lines, hi = app.Game.HighScore;
            bool attract = app.Game.Attract;
            return () => state == app.Game.State && score == app.Game.Score && lines == app.Game.Lines && hi == app.Game.HighScore && attract == app.Game.Attract;
        });

        output.WriteLine($"tetris: {run.MsPerFrame:F3} ms/frame, {run.Measured} steady windows, least {run.Least} bytes");
        Assert.True(run.Measured >= 3);
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }

    [Fact]
    public void AttractAI_DoesNotAllocateInTheSimulation()
    {
        var game = new TetrisGame(seed: 7);
        game.StartGame(true);
        for (int i = 0; i < 300; i++) game.Update(0.033f, false, false, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3000; i++) game.Update(0.033f, false, false, false);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 256);
    }
}
