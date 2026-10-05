namespace LedMatrixOS.Core.Input;

/// <summary>
/// Opt-in for apps that want input events. <see cref="OnInput"/> is called on the render thread at the start of a frame
/// (before Update), once per queued state change in arrival order, so game logic never races the network threads. Apps that
/// do not implement this ignore input; they can still poll held state through <see cref="MatrixAppBase.Input"/>.
/// </summary>
public interface IInputConsumer
{
    void OnInput(in InputEvent e);
}
