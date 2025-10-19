using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LedMatrixOS.Apps;

public sealed class FireApp : MatrixAppBase
{
    public override string Id => "fire";
    public override string Name => "Fire";

    private (int height, int width) _frameDimensions;
    private byte[,] _heatMap = null!;
    private Random _random = new();
    private int _updateCounter;
    private int[] _fuelColumns = null!;
    
    // Fire color palette - from black through red, orange, yellow to white
    private readonly Color[] _firePalette = new Color[256];

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _frameDimensions = dimensions;
        _heatMap = new byte[dimensions.height, dimensions.width];
        _fuelColumns = new int[dimensions.width];
        
        // Initialize fire palette
        InitializeFirePalette();
        
        // Initialize bottom row with hot values
        for (int x = 0; x < dimensions.width; x++)
        {
            _heatMap[dimensions.height - 1, x] = 255;
            _fuelColumns[x] = _random.Next(50, 150);
        }

        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    private void InitializeFirePalette()
    {
        for (int i = 0; i < 256; i++)
        {
            if (i < 85) // Black to dark red
            {
                float r = (i / 85f) * 255;
                _firePalette[i] = Color.FromRgb((byte)r, 0, 0);
            }
            else if (i < 170) // Dark red to orange/yellow
            {
                float g = ((i - 85) / 85f) * 255;
                _firePalette[i] = Color.FromRgb(255, (byte)g, 0);
            }
            else // Orange/yellow to white
            {
                float b = ((i - 170) / 85f) * 255;
                _firePalette[i] = Color.FromRgb(255, 255, (byte)b);
            }
        }
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        // Only update every other frame for slower, more relaxing movement
        _updateCounter++;
        if (_updateCounter % 3 != 0)
        {
            return;
        }
        
        // Update the heat map from bottom to top
        for (int y = 0; y < _frameDimensions.height - 1; y++)
        {
            for (int x = 0; x < _frameDimensions.width; x++)
            {
                // Get heat from pixels below and around
                int heat = 0;
                
                // Sample pixels below - weight center pixel much more heavily to maintain column distinction
                if (y + 1 < _frameDimensions.height)
                {
                    // Center pixel gets dominant weight for upward propagation
                    heat += _heatMap[y + 1, x] * 6;
                    int count = 6;
                    
                    // Minimal horizontal spread - only occasionally for subtle flicker
                    if (_random.NextDouble() < 0.3)
                    {
                        if (x > 0)
                        {
                            heat += _heatMap[y + 1, x - 1];
                            count++;
                        }
                        if (x < _frameDimensions.width - 1)
                        {
                            heat += _heatMap[y + 1, x + 1];
                            count++;
                        }
                    }
                    
                    // Average the heat
                    heat = heat / count;
                }
                
                // Even more reduced cooling for taller, more relaxing flames
                float heightFactor = (float)y / _frameDimensions.height;
                int cooling = _random.Next(0, (int)(5 + heightFactor * 12));
                heat = Math.Max(0, heat - cooling);
                
                _heatMap[y, x] = (byte)heat;
            }
        }
        
        // Update fuel levels for columns to create varying flame heights
        for (int x = 0; x < _frameDimensions.width; x++)
        {
            // Slowly vary fuel levels over time for each column independently
            if (_random.NextDouble() < 0.15)
            {
                _fuelColumns[x] += _random.Next(-30, 30);
                _fuelColumns[x] = Math.Clamp(_fuelColumns[x], 40, 255);
            }
            
            // Occasionally create a large flame burst in specific columns
            if (_random.NextDouble() < 0.015)
            {
                _fuelColumns[x] = 255;
            }
            
            // Sometimes dampen a column for contrast
            if (_random.NextDouble() < 0.01)
            {
                _fuelColumns[x] = _random.Next(30, 80);
            }
        }
        
        // Regenerate bottom rows with column-specific heat based on fuel
        for (int x = 0; x < _frameDimensions.width; x++)
        {
            // Use fuel column value for base heat - more variation
            int baseHeat = Math.Max(100, _fuelColumns[x] - _random.Next(0, 50));
            
            // Add some random variation per column
            if (_random.NextDouble() < 0.2)
            {
                baseHeat = Math.Min(255, baseHeat + _random.Next(10, 40));
            }
            
            _heatMap[_frameDimensions.height - 1, x] = (byte)baseHeat;
        }
        
        // Add second and third rows of heat to create larger, taller flames
        // But maintain column independence
        for (int x = 0; x < _frameDimensions.width; x++)
        {
            if (_frameDimensions.height > 1)
            {
                int secondRowHeat = Math.Max(80, _fuelColumns[x] - _random.Next(20, 60));
                _heatMap[_frameDimensions.height - 2, x] = (byte)Math.Max(_heatMap[_frameDimensions.height - 2, x], secondRowHeat);
            }
            
            if (_frameDimensions.height > 2 && _fuelColumns[x] > 180)
            {
                int thirdRowHeat = Math.Max(60, _fuelColumns[x] - _random.Next(60, 100));
                _heatMap[_frameDimensions.height - 3, x] = (byte)Math.Max(_heatMap[_frameDimensions.height - 3, x], thirdRowHeat);
            }
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);

        // Convert heat map to colors
        for (int y = 0; y < _frameDimensions.height; y++)
        {
            for (int x = 0; x < _frameDimensions.width; x++)
            {
                byte heat = _heatMap[y, x];
                var color = _firePalette[heat];
                image[x, y] = color.ToPixel<Rgb24>();
            }
        }

        frame.RenderImage(image);
    }
}
