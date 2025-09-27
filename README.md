# LED Matrix OS

A comprehensive LED Matrix display system with REST API control and Flutter mobile app.

## Components

### 1. LED Matrix OS (.NET Core Web API)
Located in `src/LedMatrixOS/` - The main server application that controls the LED matrix display.

**Features:**
- Multiple display applications (clock, animations, games, etc.)
- REST API for remote control
- Simulator mode for testing without hardware
- Real-time preview in simulator mode
- Configurable brightness and FPS

**Available Apps:**
- Clock - Digital clock display
- Animated Clock - Enhanced animated clock
- Solid Color - Single color display
- Rainbow Spiral - Animated rainbow spiral
- Bouncing Balls - Physics simulation
- Matrix Rain - Classic Matrix-style falling characters
- Geometric Patterns - Dynamic geometric shapes
- DVD Logo - Bouncing DVD logo screensaver
- Weather - Weather information display
- Spotify - Spotify playback visualization

### 2. Flutter Mobile App
Located in `flutter_app/` - Cross-platform mobile app for controlling the LED Matrix.

**Features:**
- Real-time control of all display apps
- Live preview of the matrix display
- Brightness control (0-100%)
- FPS adjustment (1-120 FPS)
- Material Design UI
- Works on Android, iOS, Web, and Desktop

## Quick Start

### 1. Start the LED Matrix OS Server

```bash
cd src/LedMatrixOS
dotnet run
```

The server will start on `http://localhost:5005` with the simulator enabled by default.

### 2. Use the Web Interface

Open `http://localhost:5005` in your browser for a basic web interface.

### 3. Use the Flutter Mobile App

```bash
cd flutter_app
flutter pub get
flutter run
```

## REST API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/apps` | List available apps and current active app |
| POST | `/api/apps/{id}` | Activate a specific app |
| GET | `/api/settings` | Get display settings |
| POST | `/api/settings/brightness/{value}` | Set brightness (0-100) |
| POST | `/api/settings/fps/{value}` | Set FPS (1-120) |
| GET | `/preview` | Get preview image (simulator only) |

### Example API Usage

```bash
# Get available apps
curl http://localhost:5005/api/apps

# Switch to bouncing balls
curl -X POST http://localhost:5005/api/apps/bouncing-balls

# Set brightness to 75%
curl -X POST http://localhost:5005/api/settings/brightness/75

# Set FPS to 60
curl -X POST http://localhost:5005/api/settings/fps/60

# Get current settings
curl http://localhost:5005/api/settings
```

## Configuration

### LED Matrix Hardware Setup

For real hardware, update `appsettings.json`:

```json
{
  "Matrix": {
    "UseSimulator": false,
    "Width": 64,
    "Height": 32,
    "HardwareMapping": "Regular",
    "ChainLength": 1,
    "Brightness": 100
  }
}
```

### Simulator Mode

For testing without hardware, keep `UseSimulator: true` (default in Development environment).

## Development

### Building the .NET Application

```bash
dotnet build
dotnet run --project src/LedMatrixOS
```

### Building the Flutter App

```bash
cd flutter_app
flutter pub get
flutter build apk    # For Android
flutter build web    # For web
```

### Project Structure

```
├── src/
│   ├── LedMatrixOS/           # Main web API application
│   ├── LedMatrixOS.Core/      # Core engine and interfaces
│   ├── LedMatrixOS.Apps/      # Display applications
│   ├── LedMatrixOS.Hardware.RpiLedMatrix/  # Raspberry Pi hardware driver
│   └── LedMatrixOS.Hardware.Simulator/     # Simulator implementation
├── flutter_app/               # Flutter mobile app
│   ├── lib/
│   │   ├── main.dart         # Main app UI
│   │   └── api_service.dart  # API client
│   ├── android/              # Android-specific files
│   └── README.md             # Flutter app documentation
└── README.md                 # This file
```

## Hardware Requirements

### For Physical LED Matrix
- Raspberry Pi (3B+ or 4 recommended)
- RGB LED Matrix panels
- Hat/HAT for connecting panels to Pi
- Sufficient power supply

### For Development/Testing
- Any system with .NET 9.0+ and Flutter installed
- Uses simulator mode by default

## Contributing

1. **Add new display apps** by implementing `IMatrixApp` in `src/LedMatrixOS.Apps/`
2. **Enhance the API** by modifying `src/LedMatrixOS/Program.cs`
3. **Improve the Flutter app** by editing files in `flutter_app/lib/`
4. **Add hardware support** by implementing `IMatrixDevice`

## License

[Add your license information here]

## Screenshots

### Flutter Mobile App
The Flutter app provides an intuitive interface to control the LED Matrix:
- Live preview of the current display
- App selection with one-touch activation
- Real-time brightness and FPS controls
- Cross-platform compatibility (Android, iOS, Web, Desktop)

### LED Matrix Display
The system supports various engaging applications:
- Real-time clock displays
- Animated patterns and effects  
- Interactive games and simulations
- Data visualizations (weather, music, etc.)

For detailed Flutter app setup and usage instructions, see [`flutter_app/README.md`](flutter_app/README.md).