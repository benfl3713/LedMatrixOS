# LedMatrixOS

A flexible and extensible LED matrix display system built with .NET 10, designed to run on Raspberry Pi with RGB LED matrices or in a simulated environment for development and testing.

> Example Spotify app on a 256x64 RGB LED matrix <br/>
>![LedMatrixOS Demo](docs/preview.png)

## Features

- 🎨 **Animated apps** - Clocks, ambient scenes, visual effects and everyday boards, all built on a widget and animation toolkit
- 🖥️ **Simulator mode** - Develop and test apps without hardware, with a live browser preview (WebSocket)
- 🔌 **Hardware support** - Raspberry Pi RGB LED matrices via the rpi-rgb-led-matrix library
- 🌐 **REST API** - Control apps, brightness, settings, schedules and notifications
- 🗓️ **Scheduler** - Playlists that rotate apps, and rules such as "weekdays 07:15-08:45: commute" or "23:00-07:00: dim"
- 🔔 **Overlays** - Toasts, corner badges and alerts drawn over whatever is running
- 🛡️ **Crash card** - An app that throws shows its error on the panel instead of freezing
- 📱 **Clients** - Flutter mobile app and a Home Assistant integration

## Built-in Apps

| Group | Apps (id) |
|---|---|
| Clocks | Clock (`clock`), Animated Clock (`animated-clock`), Flip Clock (`flip-clock`), Countdown Timer (`countdown-timer`) |
| Ambient | Home (`home`), Solid Color / mood light (`solid_color`), Scrolling Text (`scrolling-text`) |
| Visuals | Rainbow Spiral (`rainbow-spiral`), Geometric Patterns (`geometric-patterns`), Bouncing Balls (`bouncing-balls`), DVD Logo (`dvd-logo`), Matrix Rain (`matrix-rain`), Fire (`fire`), Equalizer (`equalizer`) |
| Everyday | Weather (`weather`, Open-Meteo, no key), Commute (`commute`), Calendar (`calendar`), Home Assistant tiles (`ha-tiles`), Spotify (`spotify`, needs API config) |
| Tube | Tube Departures (`tube-departures`), Tube Status (`tube-status`), Tube Line (`tube-line`) |
| Dev | Widget Demo (`widget-demo`) |

Apps with settings expose them through `GET /api/apps/{id}/settings`.

### Config keys used by the everyday apps

| Key | Used by |
|---|---|
| `Weather:Location` | Weather, Commute (place name or `lat,lon`) |
| `TFL:AppKey`, `Commute:StationId`, `Commute:WalkMinutes`, `TubeDeparturesApp:StationId` | Tube apps, Commute |
| `Calendar:IcsUrl` | Calendar (an `.ics` feed; `webcal://` works) |
| `HomeAssistant:BaseUrl`, `HomeAssistant:Token`, `HomeAssistant:Entities` | Home Assistant tiles (long-lived access token) |
| `Display:Width`, `Display:Height` | Display size (default 256x64) |

Put secrets in `appsettings.local.json`, which is not committed.

## Architecture

- **LedMatrixOS** - ASP.NET Core host: DI, device selection, REST endpoints, `ScheduleRunner`, WebSocket preview
- **LedMatrixOS.Core** - Engine with no hardware or app specifics: `RenderEngine`, `FrameBuffer`, `AppManager`, `FrameContext`, animation (`Easing`, `Tween`, `Timeline`), transitions, `Poll<T>` data, attribute settings, `Scheduling/` (playlists and rules), `Overlays/`, `CrashGuard`, `FrameBroadcaster`
- **LedMatrixOS.Graphics** - Canvas helpers, fonts and text, the widget layer (`Node`, `Stack`, `Dock`, `Label`, `ListView`, `Pager`, `RollingNumber`, `WidgetApp`), particles and post-effects
- **LedMatrixOS.Apps** - The built-in apps (register new ones in `Apps.cs`)
- **LedMatrixOS.Hardware.RpiLedMatrix** - Raspberry Pi adapter (bindings to librgbmatrix.so)
- **LedMatrixOS.Hardware.Simulator** - Simulated display for development
- **tests/LedMatrixOS.Tests** - Unit and snapshot (golden PNG) tests
- **flutter_app/**, **homeassistant/** - Clients of the REST API

## Requirements

### For Simulator Mode
- .NET 10.0 SDK or runtime
- Any platform (Windows, Linux, macOS)

### For Hardware Mode (Raspberry Pi)
- Raspberry Pi (tested on Pi 3/4)
- .NET 10.0 runtime (ARM)
- RGB LED Matrix panels
- [rpi-rgb-led-matrix library](https://github.com/hzeller/rpi-rgb-led-matrix) installed
- Root privileges (for GPIO access)

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/benfl3713/LedMatrixOS.git
cd LedMatrixOS
```

### 2. Build the Project

```bash
dotnet build
```

### 3. Configure Settings

Edit `src/LedMatrixOS/appsettings.json` to configure your matrix:

```json
{
    "Urls": "http://*:5005",
    "Matrix": {
        "Rows": 64,
        "Cols": 64,
        "HardwareMapping": "adafruit-hat-pwm",
        "GpioSlowdown": 4,
        "ChainLength": 4,
        "PwmBits": 7,
        "ShowRefreshRate": true
    }
}
```

For simulator mode, add `appsettings.Development.json`:

```json
{
    "Matrix": {
        "UseSimulator": true
    }
}
```

## Usage

### Running in Simulator Mode (Development)

```bash
cd src/LedMatrixOS
dotnet run --environment Development
```

Then open your browser to `http://localhost:5005` to see the web preview interface.

### Running on Raspberry Pi (Hardware)

```bash
cd src/LedMatrixOS
sudo dotnet run --environment Production
```

> **Note:** Root privileges are required for GPIO access on Raspberry Pi.

## Web API

The application exposes a RESTful API for control:

### App Management

- `POST /api/apps/{id}` - Activate an app by ID
  ```bash
  curl -X POST http://localhost:5005/api/apps/animated-clock
  ```

### Settings

- `GET /api/settings` - Get current settings (width, height, brightness)
  ```bash
  curl http://localhost:5005/api/settings
  ```

- `POST /api/settings/brightness/{value}` - Set brightness (0-100)
  ```bash
  curl -X POST http://localhost:5005/api/settings/brightness/50
  ```

### Overlays and notifications

- `POST /api/overlays/toast` - banner over the running app: `{"message":"Dinner","seconds":4,"color":"#000000","background":"#ffffff"}`
- `POST /api/overlays/badge` - corner indicator until dismissed: `{"id":"door","color":"#ff3c3c","pulsing":true}`
- `DELETE /api/overlays/{id}` and `DELETE /api/overlays` - dismiss one / all
- `POST /api/notifications/message` - full-screen alert, scrolls if long: `{"message":"Washing done","color":{"r":255,"g":160,"b":0}}`
- `POST /api/notifications` - red flash for 5 seconds

### Schedule

- `POST /api/schedule/reload` - re-read `schedule.json` from the app directory
- `GET /api/schedule` - what the schedule currently selects

`schedule.json` holds `playlists` (apps with durations, optional per-entry `settings`) and `rules` (time window, `daysMask` where Sun=1, Mon=2, ... Sat=64, optional `brightnessOverride`, `priority`). Times are local. See `src/LedMatrixOS/schedule.example.json`. A manual app switch sticks until the playlist next rotates.

### Health

- `GET /api/health` - status, active app, display size, uptime

### Preview

- `GET /ws/preview` - WebSocket of binary frames: `[width u16 LE][height u16 LE][RGB bytes]`, up to 30 fps, only when the picture changed
- `GET /preview` - Get current display as PNG (simulator mode only)
  ```bash
  curl http://localhost:5005/preview -o preview.png
  ```

## Creating Custom Apps

### Widget apps (recommended)

Describe a tree of widgets once; the framework lays it out, animates and draws it. Settings come from attributes and data from `Poll`:

```csharp
public class HelloApp : WidgetApp
{
    public override string Id => "hello";
    public override string Name => "Hello";

    [Setting("Greeting", Description = "Text to show")]
    public string Greeting { get; set; } = "HELLO";

    protected override Node Build() => new Stack(Orientation.Vertical)
    {
        Children =
        {
            new Label(() => Greeting) { Style = new TextStyle(Fonts.Big, new Pixel(120, 220, 240)) },
            new Clock("HH:mm", Time),
        },
    };
}
```

Use `Time` and the frame context rather than `DateTime.Now`, so tests can drive the clock. Look at `CommuteApp` or `HomeAssistantTilesApp` for complete examples with polled data, and `tests/LedMatrixOS.Tests/CommuteAppTests.cs` for golden-image and allocation tests. Steady-state rendering should allocate nothing (cache `TextRun`s; rebuild strings only when data changes).

### Low-level apps

To create your own app, inherit from `MatrixAppBase`:

```csharp
using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps;

public sealed class MyCustomApp : MatrixAppBase
{
    public override string Id => "my-custom-app";
    public override string Name => "My Custom App";

    private double _animationTime;

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        // Update animation state
        _animationTime += deltaTime.TotalSeconds;
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        // Option 1: Direct pixel manipulation
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                frame.SetPixel(x, y, new Pixel(255, 0, 0));
            }
        }

        // Option 2: Using ImageSharp (recommended for complex graphics)
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        image.Mutate(ctx =>
        {
            // Draw your graphics here
            ctx.Fill(Color.Blue);
        });
        frame.RenderImage(image);
    }
}
```

### App Lifecycle

Apps can override these methods for lifecycle management:

- `OnActivatedAsync()` - Called when app becomes active (initialize resources)
- `Update()` - Called every frame to update state
- `Render()` - Called every frame to render output
- `OnDeactivatedAsync()` - Called when app is deactivated (cleanup resources)

### Background Tasks

For apps that need background work (like fetching data):

```csharp
public override Task OnActivatedAsync((int height, int width) dimensions, CancellationToken cancellationToken)
{
    // Start a background task
    RunInBackground(async (ct) =>
    {
        while (!ct.IsCancellationRequested)
        {
            // Fetch data, etc.
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    });

    return base.OnActivatedAsync(dimensions, cancellationToken);
}
```

### Register Your App

Add your app to `BuiltInApps.GetAll()` in `src/LedMatrixOS.Apps/Apps.cs`:

```csharp
public static IEnumerable<Type> GetAll()
{
    yield return typeof(MyCustomApp);
    // ... other apps
}
>>>>>>> main
```

## Configuration

### Matrix Hardware Settings

Configure in `appsettings.json`:

| Setting | Description | Default |
|---------|-------------|---------|
| `Rows` | Number of rows per panel | 64 |
| `Cols` | Number of columns per panel | 64 |
| `HardwareMapping` | Hardware mapping type | adafruit-hat-pwm |
| `GpioSlowdown` | GPIO slowdown factor (1-4) | 4 |
| `ChainLength` | Number of chained panels | 4 |
| `PwmBits` | PWM bits (1-11, lower = faster) | 7 |
| `ShowRefreshRate` | Show refresh rate on console | true |

### Environment Variables

- `Matrix:UseSimulator` - Set to `true` for simulator mode
- `Urls` - HTTP endpoint URL (default: `http://*:5005`)

## Development

### Project Structure

```
LedMatrixOS/
├── src/
│   ├── LedMatrixOS/              # Main web application
│   │   ├── Program.cs            # Entry point
│   │   ├── appsettings.json      # Configuration
│   │   └── wwwroot/
│   │       └── index.html        # Web preview UI
│   ├── LedMatrixOS.Core/         # Engine, scheduling, overlays, animation
│   ├── LedMatrixOS.Graphics/     # Canvas, fonts, widget layer, particles
│   ├── LedMatrixOS.Apps/         # Built-in apps
│   ├── LedMatrixOS.Hardware.RpiLedMatrix/  # Pi hardware
│   └── LedMatrixOS.Hardware.Simulator/     # Simulator
├── tests/LedMatrixOS.Tests/      # Unit and golden-image tests
├── flutter_app/                  # Mobile client
├── homeassistant/                # Home Assistant integration
├── LedMatrixOS.sln               # Solution file
└── Directory.Build.props         # Common build settings
```

### Building

```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build src/LedMatrixOS/LedMatrixOS.csproj

# Build for release
dotnet build -c Release
```

### Testing

Run the automated tests (including golden-image snapshots) with `dotnet test`. If a visual change is intended, review the images and regenerate with `UPDATE_SNAPSHOTS=1 dotnet test`; set `LED_PREVIEW_DIR` to also write enlarged PNGs you can look at.

The simulator mode is perfect for trying apps without hardware:

1. Set `Matrix:UseSimulator` to `true` in configuration
2. Run the application
3. Open `http://localhost:5005` in your browser
4. Use the web UI to switch between apps and adjust settings

## Troubleshooting

### "librgbmatrix.so not found" error

Make sure the rpi-rgb-led-matrix library is installed and accessible:

```bash
sudo apt-get update
sudo apt-get install librgbmatrix-dev
```

### Permission denied on Raspberry Pi

LED matrix control requires root privileges:

```bash
sudo dotnet run
```

### Flickering or artifacts on display

Try adjusting these settings in `appsettings.json`:
- Increase `GpioSlowdown` (values 1-4)
- Decrease `PwmBits` for faster refresh
- Check power supply (LED matrices need significant power)

### Simulator not showing preview

Make sure you're running in Development mode:

```bash
dotnet run --environment Development
```

## Contributing

Contributions are welcome! To contribute:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-app`)
3. Commit your changes (`git commit -m 'Add amazing app'`)
4. Push to the branch (`git push origin feature/amazing-app`)
5. Open a Pull Request

## License

This project is provided as-is for educational and personal use.

## Acknowledgments

- Built with [.NET 10](https://dotnet.microsoft.com/)
- Uses [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) for graphics
- Hardware support via [rpi-rgb-led-matrix](https://github.com/hzeller/rpi-rgb-led-matrix) by Henner Zeller
- Inspired by the LED matrix community

## Support

For issues, questions, or suggestions, please open an issue on GitHub.
