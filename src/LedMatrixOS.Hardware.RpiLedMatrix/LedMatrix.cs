namespace LedMatrixOS.Hardware.RpiLedMatrix;

public interface ILedMatrix
{
    int RowLength { get; }
    int ColLength { get; }
    void SetPixel(int x, int y, Color color);
    void DrawLine(int x0, int y0, int x1, int y1, Color color);
    void DrawCircle(int x0, int y0, int radius, Color color);
    void Fill(Color color);
    void Clear();
    void Update();
    void Reset();
}

public class LedMatrix : ILedMatrix, IDisposable
{
    private readonly RgbMatrixFactory _matrixFactory;
    private RGBLedMatrix? _matrix;

    private RGBLedMatrix Matrix
    {
        get
        {
            if (_matrix == null)
            {
                Console.WriteLine("Creating new matrix");
                _matrix = _matrixFactory.CreateLedMatrix();
            }

            return _matrix;
        }
        set => _matrix = value;
    }

    private RGBLedCanvas? _canvas;

    public int RowLength => Canvas.Height;
    public int ColLength => Canvas.Width;
    public int Width { get; }
    public int Height { get; }

    private RGBLedCanvas Canvas
    {
        get
        {
            if (_canvas == null)
            {
                Console.WriteLine("Creating new canvas");
                _canvas = Matrix.CreateOffscreenCanvas();
            }

            return _canvas;
        }
        set => _canvas = value;
    }

    public LedMatrix(RgbMatrixFactory matrixFactory)
    {
        _matrixFactory = matrixFactory;
        Width = Canvas.Width;
        Height = Canvas.Height;
        Clear();
        Update();
    }

    public void SetPixel(int x, int y, Color color)
    {
        Canvas.SetPixel(x, y, color);
    }

    public void DrawTextNative(RGBLedFont font, int x, int y, Color color, string text)
    {
        Canvas.DrawText(font, x, y, color, text);
    }

    public void DrawLine(int x0, int y0, int x1, int y1, Color color)
    {
        Canvas.DrawLine(x0, y0, x1, y1, color);
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = (dx > dy ? dx : -dy) / 2, e2;
    }

    public void DrawCircle(int x0, int y0, int radius, Color color)
    {
        Canvas.DrawCircle(x0, y0, radius, color);
    }

    public void Clear()
    {
        Canvas.Clear();
    }

    public void Fill(Color color)
    {
        Canvas.Fill(color);
    }

    public void Update()
    {
        Matrix.SwapOnVsync(Canvas);
    }

    public void Dispose()
    {
        Canvas.Clear();
        Matrix.SwapOnVsync(Canvas);
        Thread.Sleep(500); // Wait for Vsync to actually happen
        Matrix.Dispose();
    }

    public void Reset()
    {
        Canvas.Clear();
        Matrix.SwapOnVsync(Canvas);
        Matrix.Dispose();
        Canvas = null!;
        Matrix = null!;
        GC.Collect();
    }
}
