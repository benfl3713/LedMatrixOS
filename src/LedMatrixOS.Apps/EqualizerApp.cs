using LedMatrixOS.Core;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps;

/// <summary>
/// Animated equalizer/visualizer bars - perfect for music displays
/// Can use real audio data from microphone or auto-generate patterns
/// </summary>
public sealed class EqualizerApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "equalizer";
    public override string Name => "Equalizer Visualizer";

    private float[] _barHeights = Array.Empty<float>();
    private float[] _barTargets = Array.Empty<float>();
    private float[] _barVelocities = Array.Empty<float>();
    private Random _random = new Random();
    private double _beatTimer;
    private AudioDataService? _audioService;
    
    // Settings
    private int _barCount = 32;
    private string _colorMode = "Rainbow";
    private int _smoothness = 5;
    private bool _autoGenerate = true;
    private string _audioSource = "Auto"; // "Auto" or "Microphone"

    public void SetAudioService(AudioDataService audioService)
    {
        _audioService = audioService;
    }

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        InitializeBars();
    }

    private void InitializeBars()
    {
        _barHeights = new float[_barCount];
        _barTargets = new float[_barCount];
        _barVelocities = new float[_barCount];
        
        for (int i = 0; i < _barCount; i++)
        {
            _barHeights[i] = 0;
            _barTargets[i] = 0;
            _barVelocities[i] = 0;
        }
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        _beatTimer += deltaTime.TotalSeconds;
        
        // Check if we should use microphone audio data
        bool useMicrophoneData = _audioSource == "Microphone" && _audioService != null && _audioService.HasRecentData();
        
        if (useMicrophoneData)
        {
            // Use real audio data from microphone
            var frequencyBands = _audioService!.GetFrequencyBands();
            
            // Map frequency bands to our bar count
            for (int i = 0; i < _barCount; i++)
            {
                int bandIndex = (int)((float)i / _barCount * frequencyBands.Length);
                _barTargets[i] = frequencyBands[bandIndex];
            }
        }
        else if (_autoGenerate && _beatTimer > 0.1)
        {
            // Auto-generate random beat patterns
            _beatTimer = 0;
            
            // Create wave patterns
            for (int i = 0; i < _barCount; i++)
            {
                float wave = (float)Math.Sin((DateTime.Now.TimeOfDay.TotalSeconds * 2) + (i * 0.3));
                float randomBass = i < 8 ? (float)_random.NextDouble() * 0.7f : 0;
                float randomMid = i >= 8 && i < 24 ? (float)_random.NextDouble() * 0.5f : 0;
                float randomHigh = i >= 24 ? (float)_random.NextDouble() * 0.3f : 0;
                
                _barTargets[i] = Math.Clamp((wave * 0.3f + 0.5f) + randomBass + randomMid + randomHigh, 0, 1);
            }
        }
        
        // Smooth animation
        float dt = (float)deltaTime.TotalSeconds;
        float springStrength = _smoothness * 5f;
        float damping = 2f;
        
        for (int i = 0; i < _barCount; i++)
        {
            float diff = _barTargets[i] - _barHeights[i];
            _barVelocities[i] += diff * springStrength * dt;
            _barVelocities[i] *= (1 - damping * dt);
            _barHeights[i] += _barVelocities[i] * dt;
            _barHeights[i] = Math.Clamp(_barHeights[i], 0, 1);
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.Black);
            
            float barWidth = (float)frame.Width / _barCount;
            
            for (int i = 0; i < _barCount; i++)
            {
                float x = i * barWidth;
                float height = _barHeights[i] * frame.Height;
                float y = frame.Height - height;
                
                if (height > 0)
                {
                    var color = GetBarColor(i, _barHeights[i]);
                    ctx.Fill(color, new RectangleF(x, y, Math.Max(1, barWidth - 1), height));
                    
                    // Peak indicator
                    if (_barHeights[i] > 0.8f)
                    {
                        ctx.Fill(Color.White, new RectangleF(x, y, Math.Max(1, barWidth - 1), 2));
                    }
                }
            }
        });
        
        frame.RenderImage(image);
    }

    private Color GetBarColor(int barIndex, float intensity)
    {
        return _colorMode switch
        {
            "Rainbow" => GetRainbowColor(barIndex, _barCount),
            "Blue" => Color.FromRgb((byte)(100 * intensity), (byte)(150 * intensity), (byte)(255 * intensity)),
            "Green" => Color.FromRgb(0, (byte)(255 * intensity), (byte)(100 * intensity)),
            "Red" => Color.FromRgb((byte)(255 * intensity), (byte)(50 * intensity), 0),
            "Cyan" => Color.FromRgb(0, (byte)(255 * intensity), (byte)(255 * intensity)),
            "Heat" => GetHeatColor(intensity),
            _ => Color.FromRgb((byte)(255 * intensity), (byte)(255 * intensity), (byte)(255 * intensity))
        };
    }

    private Color GetRainbowColor(int index, int total)
    {
        float hue = (float)index / total;
        return ColorFromHSV(hue * 360, 1.0f, 1.0f);
    }

    private Color GetHeatColor(float intensity)
    {
        if (intensity < 0.33f)
            return Color.FromRgb((byte)(intensity * 3 * 255), 0, 0);
        else if (intensity < 0.66f)
            return Color.FromRgb(255, (byte)((intensity - 0.33f) * 3 * 255), 0);
        else
            return Color.FromRgb(255, 255, (byte)((intensity - 0.66f) * 3 * 255));
    }

    private Color ColorFromHSV(float hue, float saturation, float value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        float f = hue / 60 - (float)Math.Floor(hue / 60);

        value = value * 255;
        byte v = Convert.ToByte(value);
        byte p = Convert.ToByte(value * (1 - saturation));
        byte q = Convert.ToByte(value * (1 - f * saturation));
        byte t = Convert.ToByte(value * (1 - (1 - f) * saturation));

        return hi switch
        {
            0 => Color.FromRgb(v, t, p),
            1 => Color.FromRgb(q, v, p),
            2 => Color.FromRgb(p, v, t),
            3 => Color.FromRgb(p, q, v),
            4 => Color.FromRgb(t, p, v),
            _ => Color.FromRgb(v, p, q)
        };
    }

    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("barCount", "Number of Bars", "How many bars to display", AppSettingType.Integer, 32, _barCount, MinValue: 8, MaxValue: 64),
            new AppSetting("smoothness", "Smoothness", "Animation smoothness (1-10)", AppSettingType.Integer, 5, _smoothness, MinValue: 1, MaxValue: 10),
            new AppSetting("colorMode", "Color Mode", "Color scheme for the bars", AppSettingType.Select, "Rainbow", _colorMode, 
                Options: new[] { "Rainbow", "Blue", "Green", "Red", "Cyan", "Heat", "White" }),
            new AppSetting("autoGenerate", "Auto Generate", "Automatically generate patterns", AppSettingType.Boolean, true, _autoGenerate),
            new AppSetting("audioSource", "Audio Source", "Source of audio data", AppSettingType.Select, "Auto", _audioSource, 
                Options: new[] { "Auto", "Microphone" })
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "barCount":
                _barCount = value is System.Text.Json.JsonElement jsonBarCount 
                    ? jsonBarCount.GetInt32() 
                    : Convert.ToInt32(value);
                InitializeBars();
                break;
            case "smoothness":
                _smoothness = value is System.Text.Json.JsonElement jsonSmoothness 
                    ? jsonSmoothness.GetInt32() 
                    : Convert.ToInt32(value);
                break;
            case "colorMode":
                _colorMode = value?.ToString() ?? "Rainbow";
                break;
            case "autoGenerate":
                _autoGenerate = value is System.Text.Json.JsonElement jsonAuto 
                    ? jsonAuto.GetBoolean() 
                    : Convert.ToBoolean(value);
                break;
            case "audioSource":
                _audioSource = value?.ToString() ?? "Auto";
                break;
        }
    }
}
