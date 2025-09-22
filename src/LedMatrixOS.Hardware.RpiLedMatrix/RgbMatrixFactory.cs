namespace LedMatrixOS.Hardware.RpiLedMatrix;

public class RgbMatrixFactory
{
    private readonly RGBLedMatrixOptions _options;

    public RgbMatrixFactory(RGBLedMatrixOptions options)
    {
        _options = options;
    }
    
    public RGBLedMatrix CreateLedMatrix()
    {
        return new RGBLedMatrix(_options);
    }
}
