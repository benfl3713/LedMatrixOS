namespace LedMatrixOS.Core;

/// <summary>
/// Timing information for one frame. Apps should use this instead of reading the clock directly.
/// </summary>
/// <param name="Time">Time elapsed since the render engine started.</param>
/// <param name="Delta">Time elapsed since the previous frame.</param>
/// <param name="FrameIndex">Zero-based index of the frame.</param>
public readonly record struct FrameContext(TimeSpan Time, TimeSpan Delta, long FrameIndex);
