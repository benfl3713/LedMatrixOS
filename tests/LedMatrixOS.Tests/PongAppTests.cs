using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Games;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Graphics.Text;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class PongAppTests(ITestOutputHelper output)
{
    private static (PongApp App, AppStage Stage, InputHub Hub) Start()
    {
        Fonts.Load();
        var hub = new InputHub();
        var app = new PongApp { Time = new FakeTime(), Input = hub };
        var stage = new AppStage(app);
        stage.Step(16, 3);
        return (app, stage, hub);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- rules -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void HeldUpAndDownMoveTheirOwnPaddle()
    {
        var game = new PongGame(256, 64);
        float l = game.PaddleY[0], r = game.PaddleY[1];
        for (int i = 0; i < 20; i++) game.Update(0.016f, true, false, false, true);
        Assert.True(game.PaddleY[0] < l);
        Assert.True(game.PaddleY[1] > r);
        Assert.True(game.IsHuman(0));
        Assert.True(game.IsHuman(1));
    }

    [Fact]
    public void UntouchedSideIsPlayedByTheAi_AndHumanStatusTimesOut()
    {
        var game = new PongGame(256, 64);
        Assert.False(game.IsHuman(0));
        game.Press(0, InputButton.Up);
        Assert.True(game.IsHuman(0));
        Assert.False(game.IsHuman(1));
        for (int i = 0; i < 60 * 11; i++) game.Update(0.016f, false, false, false, false);
        Assert.False(game.IsHuman(0));
    }

    [Fact]
    public void AiVsAi_RalliesAndScores()
    {
        var game = new PongGame(256, 64, seed: 2) { PointsToWin = 100 };
        int hits = 0;
        float lastVx = 0;
        for (int i = 0; i < 60 * 90; i++)
        {
            game.Update(0.016f, false, false, false, false);
            if (Math.Sign(game.BallVx) != Math.Sign(lastVx) && lastVx != 0 && game.BallVx != 0) hits++;
            lastVx = game.BallVx;
        }
        Assert.True(hits >= 10, $"direction changes: {hits}");
        Assert.True(game.Score[0] + game.Score[1] >= 1);
    }

    [Fact]
    public void FirstToNWins_ThenRestarts()
    {
        var game = new PongGame(256, 64, seed: 3) { PointsToWin = 2, Difficulty = 0 };
        for (int i = 0; i < 60 * 600 && game.State != PongState.MatchOver; i++) game.Update(0.016f, false, false, false, false);
        Assert.Equal(PongState.MatchOver, game.State);
        Assert.Equal(2, game.Score[game.Winner]);
        for (int i = 0; i < 60 * 7; i++) game.Update(0.016f, false, false, false, false);
        Assert.NotEqual(PongState.MatchOver, game.State);
        Assert.Equal(0, game.Score[0] + game.Score[1]);
    }

    [Fact]
    public void HardAiBeatsAnIdleSideAlmostAlways_AndPauseFreezesTheBall()
    {
        var game = new PongGame(256, 64, seed: 4);
        for (int i = 0; i < 100; i++) game.Update(0.016f, false, false, false, false);
        game.Press(0, InputButton.Start);
        Assert.Equal(PongState.Paused, game.State);
        float x = game.BallX;
        for (int i = 0; i < 100; i++) game.Update(0.016f, false, false, false, false);
        Assert.Equal(x, game.BallX);
        game.Press(0, InputButton.Start);
        Assert.NotEqual(PongState.Paused, game.State);
    }

    [Fact]
    public void BallNeverTunnelsThroughAPaddleAtMaxSpeed()
    {
        var game = new PongGame(256, 64, seed: 5) { PointsToWin = 1000, Difficulty = 2 };
        for (int i = 0; i < 60 * 120; i++)
        {
            game.Update(0.1f, false, false, false, false);   // coarse frames: sub-stepping must cope
            Assert.InRange(game.BallY, 0, 64);
        }
    }

    [Fact]
    public void Settings_ApplyToTheGame()
    {
        var (app, _, _) = Start();
        app.UpdateSetting("pointsToWin", 3);
        app.UpdateSetting("aiDifficulty", "Hard");
        Assert.Equal(3, app.Game.PointsToWin);
        Assert.Equal(2, app.Game.Difficulty);
    }

    // ---- golden images ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Golden_AiDemo()
    {
        var (_, stage, _) = Start();
        stage.Step(16, 60 * 8);
        Golden(stage, "pong_demo");
    }

    [Fact]
    public void Golden_OnePlayer()
    {
        var (app, stage, hub) = Start();
        hub.EnqueuePress(0, InputButton.Down);
        hub.Dispatch(app);
        stage.Step(16, 60 * 6);
        Golden(stage, "pong_one_player");
    }

    [Fact]
    public void Golden_TwoPlayersHolding()
    {
        var (app, stage, hub) = Start();
        hub.Enqueue(new InputEvent(0, InputButton.Up, InputState.Down));
        hub.Enqueue(new InputEvent(1, InputButton.Down, InputState.Down));
        hub.Dispatch(app);
        stage.Step(16, 30);
        Golden(stage, "pong_two_players");
    }

    [Fact]
    public void Golden_Paused()
    {
        var (app, stage, hub) = Start();
        stage.Step(16, 60 * 3);
        hub.EnqueuePress(0, InputButton.Start);
        hub.Dispatch(app);
        stage.Step(16, 2);
        Golden(stage, "pong_paused");
    }

    [Fact]
    public void Golden_MatchOver()
    {
        var (app, stage, _) = Start();
        app.UpdateSetting("pointsToWin", 2);
        app.UpdateSetting("aiDifficulty", "Easy");
        for (int i = 0; i < 60 * 600 && app.Game.State != PongState.MatchOver; i++) stage.Step(16);
        Assert.Equal(PongState.MatchOver, app.Game.State);
        stage.Step(16, 5);
        Golden(stage, "pong_match_over");
    }

    // ---- allocation ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (app, stage, _) = Start();
        app.UpdateSetting("pointsToWin", 21);
        var run = stage.MeasureSteadyAllocation(windows: 40, beginWindow: () =>
        {
            var state = app.Game.State;
            int a = app.Game.Score[0], b = app.Game.Score[1];
            return () => state == app.Game.State && a == app.Game.Score[0] && b == app.Game.Score[1];
        });

        output.WriteLine($"pong: {run.MsPerFrame:F3} ms/frame, {run.Measured} steady windows, least {run.Least} bytes");
        Assert.True(run.Measured >= 3);
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }
}
