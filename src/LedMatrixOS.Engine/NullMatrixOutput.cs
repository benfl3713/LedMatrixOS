using LedMatrixOS.Core;

namespace LedMatrixOS.Engine;

/// <summary>
/// A no-op <see cref="IMatrixOutput"/> used when running the simulator window
/// without any physical LED matrix attached.
/// The MonoGame window itself is the visual output in this mode.
/// </summary>
public sealed class NullMatrixOutput : IMatrixOutput
{
    public int Width { get; }
    public int Height { get; }
    public byte Brightness { get; set; } = 255;
    public bool IsEnabled { get; set; } = true;

    public NullMatrixOutput(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <inheritdoc/>
    /// <remarks>Intentionally does nothing — the MonoGame window is the display.</remarks>
    public void Present(ReadOnlySpan<byte> rgbaPixels, int width, int height) { }
}
