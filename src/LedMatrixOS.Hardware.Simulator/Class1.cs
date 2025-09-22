using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Hardware.Simulator;

public sealed class SimulatedMatrixDevice : IMatrixDevice
{
    private readonly object _sync = new();
    private readonly Image<Rgba32> _canvas;

    public int Width { get; }
    public int Height { get; }

    private byte _brightness = 100;
    public byte Brightness
    {
        get => _brightness;
        set => _brightness = (byte)Math.Clamp(value, (byte)0b00000000, (byte)0b11111111);
    }

    public SimulatedMatrixDevice(int width, int height)
    {
        Width = width; Height = height;
        _canvas = new Image<Rgba32>(width, height);
    }

    public void Present(FrameBuffer buffer)
    {
        lock (_sync)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var p = buffer.GetPixel(x, y);
                    var r = (byte)(p.R * _brightness / 100);
                    var g = (byte)(p.G * _brightness / 100);
                    var b = (byte)(p.B * _brightness / 100);
                    _canvas[x, y] = new Rgba32(r, g, b, 255);
                }
            }
        }
    }

    public byte[] GetPngBytes()
    {
        using var ms = new MemoryStream();
        lock (_sync)
        {
            _canvas.SaveAsPng(ms);
        }
        return ms.ToArray();
    }
}
