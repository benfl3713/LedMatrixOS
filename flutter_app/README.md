# LED Matrix Controller - Flutter App

A Flutter mobile app to control the LED Matrix display through the REST API.

## Features

- **App Control**: Switch between different display apps (clock, matrix rain, bouncing balls, etc.)
- **Live Preview**: Real-time preview of the LED Matrix display (simulator mode only)
- **Brightness Control**: Adjust display brightness from 0-100%
- **FPS Control**: Set frames per second from 1-120 FPS
- **Real-time Updates**: Auto-refreshing preview and status

## Available Apps

The app can control the following LED Matrix applications:
- **Clock** - Digital clock display
- **Solid Color** - Single color display
- **Rainbow Spiral** - Animated rainbow spiral
- **Bouncing Balls** - Physics simulation with bouncing balls
- **Matrix Rain** - Classic Matrix-style falling characters
- **Geometric Patterns** - Dynamic geometric shapes
- **Animated Clock** - Enhanced animated clock
- **DVD Logo** - Bouncing DVD logo screensaver
- **Weather** - Weather information display
- **Spotify** - Spotify playback visualization

## Setup Instructions

### Prerequisites

1. **Flutter Development Environment**
   ```bash
   # Install Flutter (Linux)
   sudo snap install flutter --classic
   
   # Or download from https://flutter.dev/docs/get-started/install
   ```

2. **Running LED Matrix OS**
   The LED Matrix OS server must be running and accessible. Start it with:
   ```bash
   cd src/LedMatrixOS
   ASPNETCORE_ENVIRONMENT=Development dotnet run
   ```
   
   This will start the server on `http://localhost:5005`

### Installation

1. **Navigate to the Flutter app directory**
   ```bash
   cd flutter_app
   ```

2. **Install dependencies**
   ```bash
   flutter pub get
   ```

3. **Configure API endpoint (if needed)**
   Edit `lib/api_service.dart` and modify the base URL:
   ```dart
   final LedMatrixApi _api = LedMatrixApi(baseUrl: 'http://your-server:5005');
   ```

### Running the App

#### On Android Device/Emulator
```bash
# Connect Android device or start emulator
flutter run
```

#### On iOS Simulator (macOS only)
```bash
flutter run -d simulator
```

#### On Web Browser
```bash
flutter run -d chrome
```

#### On Desktop (Linux/Windows/macOS)
```bash
flutter run -d linux    # Linux
flutter run -d windows  # Windows
flutter run -d macos    # macOS
```

## API Endpoints Used

The Flutter app communicates with these REST endpoints:

- `GET /api/apps` - Get list of available apps and current active app
- `POST /api/apps/{id}` - Activate a specific app
- `GET /api/settings` - Get display settings (resolution, brightness, FPS)
- `POST /api/settings/brightness/{value}` - Set brightness (0-100)
- `POST /api/settings/fps/{value}` - Set FPS (1-120)
- `GET /preview` - Get preview image (simulator mode only)

## Network Configuration

### For Local Testing
The default configuration connects to `localhost:5005`. This works when:
- Testing on desktop/web
- Testing on Android emulator
- Testing on iOS simulator

### For Physical Device Testing
When testing on a physical mobile device, you need to:

1. **Find your computer's IP address**
   ```bash
   # Linux/macOS
   ip addr show | grep inet
   # or
   ifconfig | grep inet
   
   # Windows
   ipconfig
   ```

2. **Update the API base URL in the app**
   Edit `lib/main.dart` and change:
   ```dart
   final LedMatrixApi _api = LedMatrixApi(baseUrl: 'http://localhost:5005');
   ```
   to:
   ```dart
   final LedMatrixApi _api = LedMatrixApi(baseUrl: 'http://YOUR_IP_ADDRESS:5005');
   ```

3. **Ensure firewall allows connections**
   Make sure your firewall allows incoming connections on port 5005.

### CORS Configuration
The LED Matrix OS server is already configured to allow cross-origin requests from any domain, so the Flutter app should work without additional CORS configuration.

## Troubleshooting

### Common Issues

1. **"Failed to load apps" error**
   - Ensure LED Matrix OS server is running
   - Check that the API base URL is correct
   - Verify network connectivity

2. **Preview not showing**
   - Preview only works when LED Matrix OS is running in simulator mode
   - Set `Matrix:UseSimulator = true` in appsettings.json

3. **Connection refused on mobile device**
   - Update the API base URL to use your computer's IP address
   - Check firewall settings
   - Ensure both devices are on the same network

### Development Mode

For development, you can enable additional debugging:

1. **Enable Flutter Inspector**
   ```bash
   flutter run --debug
   ```

2. **View network requests**
   Add debugging to `api_service.dart`:
   ```dart
   print('API Request: $url');
   print('API Response: ${response.body}');
   ```

## Building for Production

### Android APK
```bash
flutter build apk --release
```

### Android App Bundle
```bash
flutter build appbundle --release
```

### iOS (requires macOS and Xcode)
```bash
flutter build ios --release
```

### Web
```bash
flutter build web --release
```

The built files will be in the `build/` directory.

## Contributing

To modify or extend the Flutter app:

1. **Add new features** by editing the main UI in `lib/main.dart`
2. **Extend API functionality** by modifying `lib/api_service.dart`
3. **Update styling** by modifying the Material Design theme
4. **Add new pages** by creating additional widget files in `lib/`

## License

This Flutter app is part of the LED Matrix OS project and follows the same license terms.