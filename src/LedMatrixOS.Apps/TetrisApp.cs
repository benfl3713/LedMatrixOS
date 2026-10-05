using LedMatrixOS.Apps.Games;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Tetris. Left/Right move (hold to repeat), Down soft drops, Up hard drops, A rotates clockwise, B counter-clockwise, Start pauses.
/// Idle for 20 seconds and a simple AI plays a demo game until a button is pressed. The board is drawn upright and centred
/// (3 px cells, see <see cref="TetrisField"/>). The best human score is an Advanced setting so it is kept in normal settings storage.
/// </summary>
public sealed class TetrisApp : WidgetApp, IInputConsumer
{
    private readonly TetrisGame _game = new(seed: 20240607);

    public override string Id => "tetris";
    public override string Name => "Tetris";
    public override int FrameRate => 30;

    [Setting("High Score", Description = "Best score so far. Saved between runs.", Min = 0, Max = 99999999, Advanced = true)]
    public int HighScore { get; set; }

    public TetrisGame Game => _game;

    protected override Node Build()
    {
        _game.HighScore = HighScore;
        return new TetrisField(_game) { HAlign = Align.Stretch, VAlign = Align.Stretch };
    }

    protected override void OnSettingChanged(string key)
    {
        _game.HighScore = HighScore;   // settings are applied from the API (e.g. resetting the score to 0)
    }

    public void OnInput(in InputEvent e)
    {
        if (e.State == InputState.Down) _game.Press(e.Button);
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        base.Update(context, cancellationToken);
        _game.Update((float)context.Delta.TotalSeconds, Input.IsDownAny(InputButton.Left), Input.IsDownAny(InputButton.Right), Input.IsDownAny(InputButton.Down));
        if (_game.HighScore > HighScore) HighScore = _game.HighScore;
    }
}
