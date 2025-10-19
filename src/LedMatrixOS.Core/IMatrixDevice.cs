namespace LedMatrixOS.Core;

public interface IMatrixDevice
{
    int Width { get; }
    int Height { get; }
    byte Brightness { get; set; }
    bool IsEnabled { get; set; }

    void Present(FrameBuffer buffer);
}

