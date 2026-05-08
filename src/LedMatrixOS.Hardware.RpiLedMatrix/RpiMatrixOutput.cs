using LedMatrixOS.Core;

namespace LedMatrixOS.Hardware.RpiLedMatrix;

/// <summary>
/// <see cref="IMatrixOutput"/> implementation for the Raspberry Pi LED matrix.
/// Receives raw RGBA pixel data (read back from the MonoGame GPU render target) and
/// forwards it row-by-row to <c>librgbmatrix</c> via <see cref="ILedMatrix"/>.
///
/// The incoming byte layout matches <c>Microsoft.Xna.Framework.Color[]</c> packed by
/// <c>MemoryMarshal.AsBytes</c>: R, G, B, A per pixel, row-major.
/// </summary>
public sealed class RpiMatrixOutput : IMatrixOutput
{
    private readonly ILedMatrix _matrix;
    private bool _isEnabled = true;

    public RpiMatrixOutput(ILedMatrix matrix)
    {
        _matrix = matrix;
    }

    public int Width  => _matrix.ColLength;
    public int Height => _matrix.RowLength;

    public byte Brightness
    {
        get => _matrix.Brightness;
        set => _matrix.Brightness = value;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (!value)
            {
                // Small delay so the final dimmed frame has time to flush before clearing.
                Task.Run(async () =>
                {
                    await Task.Delay(200);
                    _matrix.Reset();
                });
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each pixel occupies 4 bytes: R, G, B, A (MonoGame <c>Color</c> memory layout).
    /// The alpha channel is ignored because librgbmatrix has no alpha compositing.
    ///
    /// Brightness is applied by the matrix hardware via <see cref="Brightness"/>;
    /// no additional scaling is done here.
    /// </remarks>
    public void Present(ReadOnlySpan<byte> rgbaPixels, int width, int height)
    {
        _matrix.Clear();

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int offset = rowStart + x * 4;
                byte r = rgbaPixels[offset];
                byte g = rgbaPixels[offset + 1];
                byte b = rgbaPixels[offset + 2];
                // offset + 3 = alpha — ignored
                _matrix.SetPixel(x, y, new Color(r, g, b));
            }
        }

        _matrix.Update();
    }
}
