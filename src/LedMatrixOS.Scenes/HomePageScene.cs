using LedMatrixOS.Core;
using LedMatrixOS.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LedMatrixOS.Scenes;

/// <summary>
/// Home-page display: animated background particles with time and (optionally) date overlay.
/// </summary>
public sealed class HomePageScene : MatrixSceneBase, IConfigurableScene
{
    public override string Id   => "home";
    public override string Name => "Home";

    private sealed class Particle
    {
        public float X, Y, Vx, Vy, Alpha;
    }

    private readonly List<Particle> _particles = new();
    private readonly Random         _random    = new();
    private double _animTime;

    // Settings
    private bool   _showDate   = true;
    private bool   _show24Hour = true;
    private string _theme      = "Calm Blue";

    public IEnumerable<AppSetting> GetSettings() =>
    [
        new AppSetting("showDate",   "Show Date",     "Display the current date",   AppSettingType.Boolean, true,       _showDate),
        new AppSetting("show24Hour", "24-Hour Format","Use 24-hour time format",    AppSettingType.Boolean, true,       _show24Hour),
        new AppSetting("theme",      "Theme",         "Background colour theme",    AppSettingType.Select,  "Calm Blue", _theme,
            Options: ["Calm Blue", "Warm Sunset", "Forest Green", "Lavender Dreams", "Monochrome"]),
    ];

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "showDate":   _showDate   = Convert.ToBoolean(value.ToString());    break;
            case "show24Hour": _show24Hour = Convert.ToBoolean(value.ToString());    break;
            case "theme":      _theme      = value.ToString() ?? "Calm Blue";        break;
        }
    }

    public override void Initialize(GraphicsDevice graphicsDevice, int width, int height)
    {
        base.Initialize(graphicsDevice, width, height);
        _particles.Clear();
        int count = Math.Min(width * height / 30, 20);
        for (int i = 0; i < count; i++)
        {
            _particles.Add(new Particle
            {
                X     = (float)(_random.NextDouble() * width),
                Y     = (float)(_random.NextDouble() * height),
                Vx    = (float)(_random.NextDouble() - 0.5) * 0.5f,
                Vy    = (float)(_random.NextDouble() - 0.5) * 0.5f,
                Alpha = (float)(_random.NextDouble() * 0.3 + 0.1),
            });
        }
    }

    public override void Update(GameTime gameTime, CancellationToken cancellationToken)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _animTime += dt;

        foreach (var p in _particles)
        {
            p.X += p.Vx * 10 * dt;
            p.Y += p.Vy * 10 * dt;

            if (p.X < -5)              p.X = MatrixWidth  + 5;
            if (p.X > MatrixWidth  + 5) p.X = -5;
            if (p.Y < -5)              p.Y = MatrixHeight + 5;
            if (p.Y > MatrixHeight + 5) p.Y = -5;

            p.Alpha = (float)(0.2 + Math.Sin(_animTime * 2 + p.X) * 0.1);
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        // Ambient particles
        var particleColor = ThemeColor();
        foreach (var p in _particles)
            spriteBatch.DrawFilledCircle((int)p.X, (int)p.Y, 1, particleColor * p.Alpha);

        // Time
        string timeFmt = _show24Hour ? "HH:mm" : "hh:mm tt";
        string time    = DateTime.Now.ToString(timeFmt);
        var    font    = MatrixFonts.Big;
        int    tw      = font.MeasureWidth(time);
        int    ty      = _showDate
                             ? (MatrixHeight / 2)
                             : (MatrixHeight + font.Height) / 2;
        font.DrawString(spriteBatch, time, (MatrixWidth - tw) / 2, ty, Color.White);

        // Optional date
        if (_showDate)
        {
            string date  = DateTime.Now.ToString("ddd dd MMM");
            var    sfont = MatrixFonts.QuiteSmall;
            int    dw    = sfont.MeasureWidth(date);
            int    dy    = ty + font.Height + 2;
            sfont.DrawString(spriteBatch, date, (MatrixWidth - dw) / 2, dy, particleColor);
        }

        spriteBatch.End();
    }

    private Color ThemeColor() => _theme switch
    {
        "Warm Sunset"      => new Color(255, 140,  50),
        "Forest Green"     => new Color( 50, 200,  80),
        "Lavender Dreams"  => new Color(180, 130, 255),
        "Monochrome"       => Color.Gray,
        _                  => new Color( 50, 150, 255), // Calm Blue
    };
}
