using LedMatrixOS.Core;

namespace LedMatrixOS.Apps;

public class SolidColorApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "solid_color";
    public override string Name => "Solid Color";
	public override int FrameRate => 1;
    
    private byte _red = 20;
    private byte _green = 255;
    private byte _blue = 0;
    
    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("red", "Red", "Red color component (0-255)", AppSettingType.Integer, 20, (int)_red, 0, 255),
            new AppSetting("green", "Green", "Green color component (0-255)", AppSettingType.Integer, 255, (int)_green, 0, 255),
            new AppSetting("blue", "Blue", "Blue color component (0-255)", AppSettingType.Integer, 0, (int)_blue, 0, 255)
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "red":
                _red = (byte)Math.Clamp(Convert.ToInt32(value.ToString()), 0, 255);
                break;
            case "green":
                _green = (byte)Math.Clamp(Convert.ToInt32(value.ToString()), 0, 255);
                break;
            case "blue":
                _blue = (byte)Math.Clamp(Convert.ToInt32(value.ToString()), 0, 255);
                break;
        }
    }
    
    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        for (int row = 0; row < frame.Height; row++)
        {
            for (int col = 0; col < frame.Width; col++)
            {
                frame.SetPixel(col, row, new Pixel(_red, _green, _blue));
            }
        }
    }
}
