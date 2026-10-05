using LedMatrixOS.Core;
using LedMatrixOS.Core.Input;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

/// <summary>
/// Controller tester: one row per player (P1-P4) with a pill per button that lights up while that button is held.
/// Reads held state through <see cref="MatrixAppBase.Input"/>, so it is also the reference for polling input.
/// </summary>
public sealed class InputTestApp : WidgetApp
{
    private static readonly string[] Labels = ["U", "D", "L", "R", "A", "B", "ST", "SE"];
    private static readonly Pixel Idle = new(28, 30, 42);
    private static readonly Pixel Held = new(40, 200, 90);

    private readonly Pill[] _pills = new Pill[InputEvent.MaxPlayers * InputEvent.ButtonCount];

    public override string Id => "input-test";
    public override string Name => "Input Test";

    protected override Node Build()
    {
        var text = new TextStyle(Fonts.Small, new Pixel(200, 200, 210));
        var pill = new TextStyle(Fonts.Small, Pixel.White, Shadow: false);
        var rows = new Stack(Orientation.Vertical, gap: 2) { Padding = new Thickness(4, 2) };

        for (int p = 0; p < InputEvent.MaxPlayers; p++)
        {
            var row = new Stack(Orientation.Horizontal, gap: 4) { Height = 12, CrossAlign = Align.Center };
            row.Children.Add(new Label($"P{p + 1}") { Style = text, Width = 14 });
            for (int b = 0; b < InputEvent.ButtonCount; b++)
            {
                var node = new Pill(Labels[b], Idle) { Style = pill, Width = 26, Height = 12 };
                _pills[p * InputEvent.ButtonCount + b] = node;
                row.Children.Add(node);
            }
            rows.Children.Add(row);
        }
        return rows;
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        base.Update(context, cancellationToken); // builds the tree on the first frame
        for (int p = 0; p < InputEvent.MaxPlayers; p++)
            for (int b = 0; b < InputEvent.ButtonCount; b++)
                _pills[p * InputEvent.ButtonCount + b].Background = Input.IsDown(p, (InputButton)b) ? Held : Idle;
    }
}
