using LedMatrixOS.Core;
using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

/// <summary>
/// Animated equalizer / audio-visualiser bars.
/// Pass an <see cref="AudioDataService"/> via the constructor for microphone input;
/// otherwise bars are auto-generated.
/// </summary>
public sealed class EqualizerScene : MatrixSceneBase, IConfigurableScene
{
    public override string Id   => "equalizer";
    public override string Name => "Equalizer Visualizer";

    private readonly AudioDataService? _audioService;

    private float[] _heights    = Array.Empty<float>();
    private float[] _targets    = Array.Empty<float>();
    private float[] _velocities = Array.Empty<float>();
    private readonly Random _random = new();
    private double _beatTimer;

    // Settings
    private int    _barCount    = 32;
    private string _colorMode   = "Rainbow";
    private int    _smoothness  = 5;

    public EqualizerScene() { }
    public EqualizerScene(AudioDataService audioService) => _audioService = audioService;

    public IEnumerable<AppSetting> GetSettings() =>
    [
        new AppSetting("barCount",   "Bar Count",       "Number of equalizer bars",     AppSettingType.Integer, 32, _barCount,   4,  64),
        new AppSetting("colorMode",  "Color Mode",      "Bar color scheme",             AppSettingType.Select,  "Rainbow", _colorMode,
            Options: ["Rainbow", "Blue", "Green", "Red", "Cyan", "Heat"]),
        new AppSetting("smoothness", "Smoothness",      "Spring smoothness (1-10)",     AppSettingType.Integer, 5, _smoothness, 1, 10),
    ];

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "barCount":
                _barCount = Math.Clamp(Convert.ToInt32(value.ToString()), 4, 64);
                InitBars();
                break;
            case "colorMode":
                _colorMode = value.ToString() ?? "Rainbow";
                break;
            case "smoothness":
                _smoothness = Math.Clamp(Convert.ToInt32(value.ToString()), 1, 10);
                break;
        }
    }

    public override void Initialize(GraphicsDevice graphicsDevice, int width, int height)
    {
        base.Initialize(graphicsDevice, width, height);
        InitBars();
    }

    private void InitBars()
    {
        _heights    = new float[_barCount];
        _targets    = new float[_barCount];
        _velocities = new float[_barCount];
    }

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _beatTimer += dt;

        if (_audioService?.HasRecentData() == true)
        {
            var bands = _audioService.GetFrequencyBands();
            for (int i = 0; i < _barCount; i++)
                _targets[i] = bands[(int)((float)i / _barCount * bands.Length)];
        }
        else if (_beatTimer > 0.1)
        {
            _beatTimer = 0;
            double t = gameTime.TotalGameTime.TotalSeconds;
            for (int i = 0; i < _barCount; i++)
            {
                float wave   = (float)Math.Sin(t * 2 + i * 0.3);
                float extra  = i < 8 ? (float)_random.NextDouble() * 0.7f
                             : i < 24 ? (float)_random.NextDouble() * 0.5f
                             : (float)_random.NextDouble() * 0.3f;
                _targets[i] = Math.Clamp(wave * 0.3f + 0.5f + extra, 0f, 1f);
            }
        }

        float spring  = _smoothness * 5f;
        float damping = 2f;
        for (int i = 0; i < _barCount; i++)
        {
            _velocities[i] += (_targets[i] - _heights[i]) * spring  * dt;
            _velocities[i] *= 1f - damping * dt;
            _heights[i]    += _velocities[i] * dt;
            _heights[i]     = Math.Clamp(_heights[i], 0f, 1f);
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        float barW = (float)MatrixWidth / _barCount;
        for (int i = 0; i < _barCount; i++)
        {
            float h = _heights[i] * MatrixHeight;
            if (h < 1f) continue;

            int x    = (int)(i * barW);
            int y    = MatrixHeight - (int)h;
            int w    = Math.Max(1, (int)barW - 1);
            var col  = GetBarColor(i, _heights[i]);
            spriteBatch.DrawFilledRect(x, y, w, (int)h, col);

            if (_heights[i] > 0.8f)
                spriteBatch.DrawFilledRect(x, y, w, 1, Color.White);
        }
        spriteBatch.End();
    }

    private Color GetBarColor(int index, float intensity)
    {
        return _colorMode switch
        {
            "Rainbow" => SpriteBatchExtensions.HsvToColor((float)index / _barCount * 360, 1f, 1f),
            "Blue"    => new Color((byte)(100 * intensity), (byte)(150 * intensity), (byte)(255 * intensity)),
            "Green"   => new Color((byte)0, (byte)(255 * intensity), (byte)(100 * intensity)),
            "Red"     => new Color((byte)(255 * intensity), (byte)(50  * intensity), (byte)0),
            "Cyan"    => new Color((byte)0, (byte)(255 * intensity), (byte)(255 * intensity)),
            "Heat"    => intensity < 0.5f
                             ? new Color((byte)(255 * intensity * 2), (byte)0, (byte)0)
                             : new Color((byte)255, (byte)(255 * (intensity - 0.5f) * 2), (byte)0),
            _         => new Color((byte)(255 * intensity), (byte)(255 * intensity), (byte)(255 * intensity)),
        };
    }
}
