namespace LedMatrixOS.Core;

/// <summary>
/// Hardware output abstraction used by the MonoGame engine.
/// Receives pixel-readback data from the GPU render target and forwards it to the physical device.
/// Replaces <see cref="IMatrixDevice"/> in the MonoGame-based pipeline.
/// </summary>
public interface IMatrixOutput
{
    int Width { get; }
    int Height { get; }
    byte Brightness { get; set; }
    bool IsEnabled { get; set; }

    /// <summary>
    /// Presents a frame to the output device.
    /// </summary>
    /// <param name="rgbaPixels">
    /// Raw RGBA pixel data read back from the GPU render target.
    /// Length is <c>width * height * 4</c> bytes, row-major order, channels: R, G, B, A.
    /// </param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    void Present(ReadOnlySpan<byte> rgbaPixels, int width, int height);
}
