using LedMatrixOS.Apps.Games;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Pong for two. Player 0 plays the left paddle and player 1 the right, each with Up/Down (Start pauses). A side nobody has touched
/// for 10 seconds is played by an AI (difficulty is a setting), so one player gets an AI opponent and an idle screen is a demo.
/// First to <see cref="PointsToWin"/> wins, then a new match starts.
/// </summary>
public sealed class PongApp : WidgetApp, IInputConsumer
{
    private PongGame? _game;
    private int _width = 256, _height = 64;

    public override string Id => "pong";
    public override string Name => "Pong";
    public override int FrameRate => 60;

    [Setting("Points To Win", Description = "First side to this many points wins.", Min = 1, Max = 21)]
    public int PointsToWin { get; set; } = 7;

    [Setting("AI Difficulty", Description = "How well the computer plays an unattended side.", Options = ["Easy", "Normal", "Hard"])]
    public string AiDifficulty { get; set; } = "Normal";

    public PongGame Game => _game ??= new PongGame(_width, _height, seed: 4242);

    public override async Task OnActivatedAsync((int height, int width) size, Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(size, configuration, cancellationToken);
        _height = size.height;
        _width = size.width;
        _game = null;
    }

    protected override Node Build()
    {
        ApplySettings();
        return new PongField(Game) { HAlign = Align.Stretch, VAlign = Align.Stretch };
    }

    protected override void OnSettingChanged(string key) => ApplySettings();

    private void ApplySettings()
    {
        Game.PointsToWin = Math.Clamp(PointsToWin, 1, 21);
        Game.Difficulty = AiDifficulty switch { "Easy" => 0, "Hard" => 2, _ => 1 };
    }

    public void OnInput(in InputEvent e)
    {
        if (e.State == InputState.Down && e.Player <= 1) Game.Press(e.Player, e.Button);
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        base.Update(context, cancellationToken);
        Game.Update((float)context.Delta.TotalSeconds,
            Input.IsDown(0, InputButton.Up), Input.IsDown(0, InputButton.Down),
            Input.IsDown(1, InputButton.Up), Input.IsDown(1, InputButton.Down));
    }
}
