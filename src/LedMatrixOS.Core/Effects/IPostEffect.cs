namespace LedMatrixOS.Core;

/// <summary>
/// A pass over the finished frame (after the app and any transition rendered, before it is presented).
/// Implementations must not allocate per frame.
/// </summary>
public interface IPostEffect
{
    void Apply(FrameBuffer frame, FrameContext ctx);
}
