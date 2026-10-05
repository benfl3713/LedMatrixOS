using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class GameAppTests(ITestOutputHelper output)
{
    private static void Press(IInputConsumer app, InputHub hub, InputButton b)
    {
        hub.EnqueuePress(0, b);
        hub.Dispatch(app);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- snake -----------------------------------------------------------------------------------------------------

    private static (SnakeApp App, AppStage Stage, InputHub Hub) Snake(bool started = true, int seed = 7)
    {
        Fonts.Load();
        var hub = new InputHub();
        var app = new SnakeApp(new Random(seed)) { Input = hub };
        var stage = new AppStage(app);
        stage.Step(33, 2);
        if (started) Press(app, hub, InputButton.Start);
        return (app, stage, hub);
    }

    [Fact]
    public void Snake_IdentityAndSettings()
    {
        var app = new SnakeApp();
        Assert.Equal("snake", app.Id);
        Assert.Equal(new[] { "wrapWalls", "highScore" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.True(app.GetSettings().Single(s => s.Key == "highScore").Advanced);
        Assert.Contains(typeof(SnakeApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void Snake_StartBeginsAGame_AndSnakeMoves()
    {
        var (app, stage, _) = Snake(started: false);
        Assert.Equal(SnakeMode.Title, app.Mode);
        var (_, _, hub) = (app, stage, app.Input);
        Press(app, hub, InputButton.Start);
        Assert.Equal(SnakeMode.Playing, app.Mode);
        int x = app.HeadX;
        stage.Step(33, 10);
        Assert.True(app.HeadX > x);
    }

    [Fact]
    public void Snake_TurnsButNeverReverses()
    {
        var (app, stage, hub) = Snake();
        Press(app, hub, InputButton.Left);          // 180: ignored
        stage.Step(33, 6);
        Assert.Equal(1, app.Direction);
        Press(app, hub, InputButton.Up);
        stage.Step(33, 6);
        Assert.Equal(0, app.Direction);
        Press(app, hub, InputButton.Down);          // 180 of the new heading: ignored
        stage.Step(33, 6);
        Assert.Equal(0, app.Direction);
    }

    [Fact]
    public void Snake_EatingGrowsAndScores()
    {
        var (app, stage, _) = Snake();
        int len = app.Length;
        app.PutFood(app.HeadX + 3, app.HeadY);
        stage.Step(33, 20);
        Assert.Equal(len + 1, app.Length);
        Assert.Equal(10, app.Score);
        Assert.Equal(10, app.HighScore);
    }

    [Fact]
    public void Snake_WallKillsWhenNotWrapping_AndWrapsWhenWrapping()
    {
        var (wrap, ws, _) = Snake();
        ws.Step(33, 400);
        Assert.Equal(SnakeMode.Playing, wrap.Mode);

        var (solid, ss, _) = Snake();
        solid.WrapWalls = false;
        ss.Step(33, 400);
        Assert.Equal(SnakeMode.Dead, solid.Mode);
    }

    [Fact]
    public void Snake_DeadThenStartRestarts()
    {
        var (app, stage, hub) = Snake();
        app.WrapWalls = false;
        stage.Step(33, 400);
        Assert.Equal(SnakeMode.Dead, app.Mode);
        Press(app, hub, InputButton.Start);
        Assert.Equal(SnakeMode.Playing, app.Mode);
        Assert.Equal(0, app.Score);
    }

    [Fact]
    public void Snake_PlaysItselfWhenIdle_AndAnyPressTakesOver()
    {
        var (app, stage, hub) = Snake(started: false);
        stage.Step(500, 41); // > 20 s
        Assert.Equal(SnakeMode.Attract, app.Mode);
        stage.Step(100, 600);
        Assert.Equal(SnakeMode.Attract, app.Mode);
        Assert.True(app.Score > 0, "the bot should eat something in a minute");
        Assert.Equal(0, app.HighScore); // demo scores do not count

        Press(app, hub, InputButton.Left);
        Assert.Equal(SnakeMode.Playing, app.Mode);
    }

    [Fact]
    public void Snake_SeededRunsAreIdentical()
    {
        var a = Snake(seed: 3);
        var b = Snake(seed: 3);
        a.Stage.Step(100, 100);
        b.Stage.Step(100, 100);
        Assert.True(Stage.Same(a.Stage.Snapshot(), b.Stage.Snapshot()));
    }

    [Fact]
    public void Golden_Snake()
    {
        var (app, stage, _) = Snake(started: false);
        Golden(stage, "snake_title");

        var (play, ps, hub) = Snake();
        for (int i = 0; i < 6; i++)
        {
            play.PutFood(play.HeadX + 2, play.HeadY);
            ps.Step(33, 6);
        }
        play.PutFood(play.HeadX + 6, play.HeadY - 5);
        ps.Step(33, 30);
        Press(play, hub, InputButton.Up);
        ps.Step(33, 20);
        Golden(ps, "snake_playing");

        var (dead, ds, _) = Snake();
        dead.WrapWalls = false;
        ds.Step(33, 400);
        Golden(ds, "snake_gameover");

        var (demo, dstage, _) = Snake(started: false);
        dstage.Step(500, 41);
        dstage.Step(100, 50);
        Golden(dstage, "snake_demo");
    }

    [Fact]
    public void Snake_SteadyState_DoesNotAllocate()
    {
        var (_, stage, _) = Snake();
        var run = stage.MeasureSteadyAllocation(windows: 6);
        output.WriteLine($"snake: {run.MsPerFrame:F3} ms/frame");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");

        var (_, demo, _) = Snake(started: false);
        demo.Step(500, 41);
        var run2 = demo.MeasureSteadyAllocation(windows: 8);
        output.WriteLine($"snake demo: {run2.MsPerFrame:F3} ms/frame");
        Assert.True(run2.Least < 256, $"demo least allocation: {run2.Least} bytes");
    }

    // ---- breakout --------------------------------------------------------------------------------------------------

    private static (BreakoutApp App, AppStage Stage, InputHub Hub) Brick(bool started = true, int seed = 11)
    {
        Fonts.Load();
        var hub = new InputHub();
        var app = new BreakoutApp(new Random(seed)) { Input = hub };
        var stage = new AppStage(app);
        stage.Step(16, 2);
        if (started) Press(app, hub, InputButton.Start);
        return (app, stage, hub);
    }

    [Fact]
    public void Breakout_IdentityAndSettings()
    {
        var app = new BreakoutApp();
        Assert.Equal("breakout", app.Id);
        Assert.Equal(new[] { "highScore" }, app.GetSettings().Select(s => s.Key).ToArray());
        Assert.True(app.GetSettings().Single(s => s.Key == "highScore").Advanced);
        Assert.Contains(typeof(BreakoutApp), BuiltInApps.GetAll());
    }

    [Fact]
    public void Breakout_PaddleAcceleratesWhileHeld_AndStopsAtWalls()
    {
        var (app, stage, hub) = Brick();
        float start = app.PaddleX;
        hub.Enqueue(new InputEvent(0, InputButton.Left, InputState.Down));
        hub.Dispatch(app);
        stage.Step(16, 10);
        float d1 = start - app.PaddleX;
        stage.Step(16, 10);
        float d2 = start - app.PaddleX - d1;
        Assert.True(d1 > 0 && d2 > d1, "second interval should cover more distance (acceleration)");
        stage.Step(16, 120);
        Assert.True(app.PaddleX <= 15);
        hub.Enqueue(new InputEvent(0, InputButton.Left, InputState.Up));
        hub.Dispatch(app);
        float held = app.PaddleX;
        stage.Step(16, 30);
        Assert.Equal(held, app.PaddleX); // at the wall
    }

    [Fact]
    public void Breakout_LaunchOnA_AndBallMoves()
    {
        var (app, stage, hub) = Brick();
        Assert.Equal(BreakoutMode.Serving, app.Mode);
        Press(app, hub, InputButton.A);
        stage.Step(16, 1);
        Assert.Equal(BreakoutMode.Playing, app.Mode);
        float y = app.BallY;
        stage.Step(16, 5);
        Assert.True(app.BallY < y);
    }

    [Fact]
    public void Breakout_BallBreaksBrickAndScores()
    {
        var (app, stage, hub) = Brick();
        Press(app, hub, InputButton.A);
        stage.Step(16, 1);
        app.ClearBricksExcept(row: 0, col: 6);
        app.PutBall(6 * BreakoutApp.BrickW + 8, 30, 0, -100);
        stage.Step(16, 40);
        Assert.True(app.Score >= 70);
    }

    [Fact]
    public void Breakout_ClearingTheWallStartsALevel()
    {
        var (app, stage, hub) = Brick();
        Press(app, hub, InputButton.A);
        stage.Step(16, 1);
        app.ClearBricksExcept(row: 5, col: 6);
        app.PutBall(6 * BreakoutApp.BrickW + 8, 40, 0, -100);
        stage.Step(16, 20);
        Assert.Equal(2, app.Level);
        Assert.Equal(BreakoutMode.Serving, app.Mode);
    }

    [Fact]
    public void Breakout_MissingLosesLives_ThenGameOver()
    {
        var (app, stage, hub) = Brick();
        for (int life = 3; life >= 1; life--)
        {
            Assert.Equal(life, app.Lives);
            Press(app, hub, InputButton.A);
            stage.Step(16, 1);
            app.PutBall(200, 55, 0, 150);          // far from the paddle at the centre
            hub.Enqueue(new InputEvent(0, InputButton.Left, InputState.Down));
            hub.Dispatch(app);
            stage.Step(16, 40);
        }
        Assert.Equal(BreakoutMode.Dead, app.Mode);
        Press(app, hub, InputButton.Start);
        Assert.Equal(BreakoutMode.Serving, app.Mode);
        Assert.Equal(3, app.Lives);
    }

    [Fact]
    public void Breakout_PlaysItselfWhenIdle()
    {
        var (app, stage, hub) = Brick(started: false);
        stage.Step(500, 41);
        Assert.Equal(BreakoutMode.Attract, app.Mode);
        stage.Step(33, 1800);
        Assert.True(app.Score > 0);
        Assert.Equal(0, app.HighScore);
        Press(app, hub, InputButton.A);
        Assert.Equal(BreakoutMode.Serving, app.Mode);
    }

    [Fact]
    public void Breakout_SeededRunsAreIdentical()
    {
        var a = Brick(started: false, seed: 5);
        var b = Brick(started: false, seed: 5);
        a.Stage.Step(500, 41); b.Stage.Step(500, 41);
        a.Stage.Step(33, 300); b.Stage.Step(33, 300);
        Assert.True(Stage.Same(a.Stage.Snapshot(), b.Stage.Snapshot()));
    }

    [Fact]
    public void Golden_Breakout()
    {
        var (_, title, _) = Brick(started: false);
        Golden(title, "breakout_title");

        var (app, ps, hub) = Brick();
        Golden(ps, "breakout_serve");
        Press(app, hub, InputButton.A);
        ps.Step(16, 220);
        Golden(ps, "breakout_playing");

        var (dead, ds, dhub) = Brick();
        for (int life = 0; life < 3; life++)
        {
            Press(dead, dhub, InputButton.A);
            ds.Step(16, 1);
            dead.PutBall(200, 55, 0, 150);
            dhub.Enqueue(new InputEvent(0, InputButton.Left, InputState.Down));
            dhub.Dispatch(dead);
            ds.Step(16, 40);
        }
        Golden(ds, "breakout_gameover");

        var (_, demo, _) = Brick(started: false);
        demo.Step(500, 41);
        demo.Step(33, 400);
        Golden(demo, "breakout_demo");
    }

    [Fact]
    public void Breakout_SteadyState_DoesNotAllocate()
    {
        var (app, stage, hub) = Brick();
        Press(app, hub, InputButton.A);
        var run = stage.MeasureSteadyAllocation(windows: 6);
        output.WriteLine($"breakout: {run.MsPerFrame:F3} ms/frame");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");

        var (_, demo, _) = Brick(started: false);
        demo.Step(500, 41);
        var run2 = demo.MeasureSteadyAllocation(windows: 8);
        Assert.True(run2.Least < 256, $"demo least allocation: {run2.Least} bytes");
    }
}
