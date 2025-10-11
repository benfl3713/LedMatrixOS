# LedMatrixOS

A flexible and extensible LED matrix display system built with .NET 9, designed to run on Raspberry Pi with RGB LED matrices or in a simulated environment for development and testing.

## Features

- 🎨 **Rich Visual Effects** - Multiple built-in apps with stunning animations and effects
- 🖥️ **Simulator Mode** - Develop and test apps without physical hardware
- 🔌 **Hardware Support** - Native support for Raspberry Pi RGB LED matrices via rpi-rgb-led-matrix library
- 🌐 **Web API** - RESTful API for controlling apps, brightness, and settings
- 🎯 **Extensible Architecture** - Easy-to-use base classes for creating custom apps
- 🚀 **High Performance** - Optimized rendering engine with configurable FPS
- 📱 **Web Preview** - Real-time browser preview in simulator mode

## Built-in Apps

The system includes several pre-built applications:

### Clock Apps
- **Clock** (`clock`) - Simple digital clock with decorative stars
- **Animated Clock** (`animated-clock`) - 7-segment style animated digital clock with smooth transitions

### Visual Effects
- **Rainbow Spiral** (`rainbow-spiral`) - Mesmerizing rainbow-colored spiral animation
- **Geometric Patterns** (`geometric-patterns`) - Rotating triangles, pulsating circles, and rotating squares
- **Bouncing Balls** (`bouncing-balls`) - Physics-based bouncing balls with glow effects
- **DVD Logo** (`dvd-logo`) - Classic DVD screensaver bounce effect with color changes
- **Matrix Rain** (`matrix-rain`) - The Matrix-style falling characters effect
- **Solid Color** (`solid_color`) - Simple solid color display

### Information Apps
- **Weather** (`weather`) - Display current weather information (requires API configuration)
- **Spotify** (`spotify`) - Display currently playing track from Spotify (requires API configuration)

## Architecture

The project is organized into several modules:

- **LedMatrixOS** - Main application with ASP.NET Core web server
- **LedMatrixOS.Core** - Core abstractions and rendering engine
  - `IMatrixApp` - Interface for creating apps
  - `MatrixAppBase` - Base class with lifecycle management
  - `RenderEngine` - High-performance rendering loop
  - `FrameBuffer` - Pixel buffer for frame composition
  - `AppManager` - App registration and activation
- **LedMatrixOS.Apps** - Collection of built-in applications
- **LedMatrixOS.Hardware.RpiLedMatrix** - Raspberry Pi hardware adapter (native bindings to librgbmatrix.so)
- **LedMatrixOS.Hardware.Simulator** - Simulated display for development

## Requirements

### For Simulator Mode
- .NET 9.0 SDK or runtime
- Any platform (Windows, Linux, macOS)

### For Hardware Mode (Raspberry Pi)
- Raspberry Pi (tested on Pi 3/4)
- .NET 9.0 runtime (ARM)
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

- `POST /api/settings/fps/{value}` - Set target FPS (1-120)
  ```bash
  curl -X POST http://localhost:5005/api/settings/fps/60
  ```

### Simulator Preview

- `GET /preview` - Get current display as PNG (simulator mode only)
  ```bash
  curl http://localhost:5005/preview -o preview.png
  ```

## Creating Custom Apps

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
│   ├── LedMatrixOS.Core/         # Core engine
│   │   └── Class1.cs             # Core abstractions
│   ├── LedMatrixOS.Apps/         # Built-in apps
│   ├── LedMatrixOS.Hardware.RpiLedMatrix/  # Pi hardware
│   └── LedMatrixOS.Hardware.Simulator/     # Simulator
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

The simulator mode is perfect for testing apps without hardware:

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

- Built with [.NET 9](https://dotnet.microsoft.com/)
- Uses [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) for graphics
- Hardware support via [rpi-rgb-led-matrix](https://github.com/hzeller/rpi-rgb-led-matrix) by Henner Zeller
- Inspired by the LED matrix community

## Support

For issues, questions, or suggestions, please open an issue on GitHub.
