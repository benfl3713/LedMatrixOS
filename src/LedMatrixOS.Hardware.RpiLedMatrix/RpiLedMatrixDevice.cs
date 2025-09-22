using LedMatrixOS.Core;

namespace LedMatrixOS.Hardware.RpiLedMatrix;

public class RpiLedMatrixDevice : IMatrixDevice
{
    private readonly ILedMatrix _matrix;

    public RpiLedMatrixDevice(ILedMatrix matrix)
    {
        _matrix = matrix;
    }
    
    public int Width => _matrix.ColLength;
    public int Height => _matrix.RowLength;
    public byte Brightness { get; set; }
    public void Present(FrameBuffer buffer)
    {
        _matrix.Clear();
        
        for (int i = 0; i < Height; i++)
        {
            var row = buffer.GetPixelRowSpan(i);
            for (int j = 0; j < Width; j++)
            {
                var (r, g, b) = row[j];
                _matrix.SetPixel(j, i, new Color(r, g, b));
            }
        }
        
        _matrix.Update();
    }
}
