# LED Matrix Controller (Flutter)

Phone app for controlling a LedMatrixOS display (256x64 RGB matrix on a Raspberry Pi) over its REST API.
Targets Android and iOS. The app talks plain HTTP on your LAN (default port 5005).

## Pointing the app at your Pi

1. Make sure LedMatrixOS is running on the Pi and your phone is on the same network.
2. Open **Settings > Connection** and enter the device address, e.g. `192.168.1.50:5005`
   (or a hostname such as `ledmatrix.local:5005`). Use **Test** to verify, or use the scan to find the device.
3. For a dev machine running the simulator, use the machine's LAN IP (not `localhost`) on a physical phone;
   the Android emulator reaches the host at `10.0.2.2`.

Settings also has an **Appearance** switch (System / Dark / Light; default Dark).

## Development

```bash
cd flutter_app
flutter pub get
flutter analyze
flutter test
flutter run
```

## Release builds

Version lives in `pubspec.yaml` (`version: 1.1.0+3`); keep `appVersion` in
`lib/features/settings/settings_page.dart` in step.

### Android

Cleartext HTTP is allowed via `android/app/src/main/res/xml/network_security_config.xml`
(the device address is user-entered, so it cannot be domain-scoped).

Create an upload keystore once and keep it out of git (`*.jks` and `key.properties` are gitignored):

```bash
keytool -genkey -v -keystore ~/ledmatrix-upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias upload
```

Create `android/key.properties`:

```properties
storePassword=...
keyPassword=...
keyAlias=upload
storeFile=/absolute/path/to/ledmatrix-upload.jks
```

Then:

```bash
flutter build appbundle --release   # Play Store
flutter build apk --release         # sideload
```

Without `key.properties`, release builds fall back to the debug key (fine for local testing only).
R8 minification is off; enabling it would need keep rules for the audio/permission plugins.

### iOS (macOS + Xcode)

```bash
cd ios && pod install && cd ..
flutter build ipa --release
```

Set your signing team in Xcode (Runner target > Signing & Capabilities). `Info.plist` already declares
the microphone, camera, photo library and local-network usage strings and allows LAN HTTP.

## Launcher icon

Icons are generated, not hand-drawn: `dart run tool/gen_icons.dart` rewrites the Android mipmaps and the iOS
AppIcon set (needs the `image` dev dependency). Edit the script to change the design.
