using LedMatrixOS.Core;

namespace LedMatrixOS.Apps;

public class SolidColorApp : MatrixAppBase
{
    public override string Id => "solid_color";
    public override string Name => "Solid Color";
    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        for (int row = 0; row < frame.Height; row++)
        {
            for (int col = 0; col < frame.Width; col++)
            {
                frame.SetPixel(col, row, new Pixel(20, 255, 0)); // Solid green color
            }
        }
    }
}
