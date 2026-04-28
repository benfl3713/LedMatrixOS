using LedMatrixOS.Core;

namespace LedMatrixOS.Apps;

public class TubeLineApp : MatrixAppBase
{
    public override string Id => "tube-line";
    public override string Name => "Tube Line";
    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        var lineHeight = frame.Height switch
        {
            < 15 => 3,
            < 32 => 6,
            _ => 8
        };

        var startingRow = (frame.Height / 2) - (lineHeight / 2);

        var color = new Pixel(135, 135, 135);
        for (int row = 0; row < lineHeight; row++)
        {
            for (int col = 0; col < frame.Width; col++)
            {
                frame.SetPixel(col, row + startingRow, color);
            }
        }
    }
}
