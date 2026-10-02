namespace LedMatrixOS.Core;

/// <summary>
/// A pass over a finished frame, applied in place by the render engine.
/// </summary>
public interface IPostEffect
{
    void Apply(FrameBuffer frame, FrameContext ctx);
}
