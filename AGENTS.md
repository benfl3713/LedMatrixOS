# AGENTS.md

Guidance for AI agents working in this repo. Read this first so you don't waste time exploring the wrong folders.

## What this repo is

LedMatrixOS: software that drives a **256x64 P5 RGB LED matrix** from a **Raspberry Pi**. The core is a .NET 10 app in `src/`. Everything else in the repo is a client of that app's REST API.

## Top-level layout

| Folder | What it is | Priority |
|---|---|---|
| `src/` | **The main project.** The .NET 10 LedMatrixOS code that runs on the Raspberry Pi (and in a simulator on a dev machine). Almost all tasks belong here. | **High** |
| `flutter_app/` | Flutter mobile app that remote-controls the matrix via the REST API (app switching, brightness, live preview, audio streaming). Not part of the Pi runtime. Only touch when the task is about the mobile app. | Low |
| `homeassistant/` | Python Home Assistant custom integration (`custom_components/ledmatrix_controller`) exposing light/select/number/sensor entities over the REST API. Only touch for Home Assistant work. | Low |
| `docs/` | Images used by the README (`preview.png`). Nothing to edit normally. | Ignore |
| `.github/` | `CODEOWNERS` only. | Ignore |

Root files: `LedMatrixOS.sln` (solution, includes only the `src/` projects), `Directory.Build.props` (shared settings: net10.0, nullable, implicit usings, LangVersion preview), `README.md` (user-facing docs, partly stale, see Gotchas).

Do not explore `bin/` or `obj/` folders anywhere; they are build output.

## `src/` projects

All six are in `LedMatrixOS.sln`.

| Project | Role |
|---|---|
| `src/LedMatrixOS` | **Entry point** (ASP.NET Core web app). `Program.cs` wires DI, picks the device (simulator vs Pi), registers apps, starts the render loop, and defines the REST endpoints. `Endpoints/NotificationEndpoints.cs` holds the notification/interrupt endpoints. `AppConfig.cs` binds the `Matrix` config section. `wwwroot/index.html` is the web preview UI. `appsettings*.json` is config. `*.http` files are manual request samples. |
| `src/LedMatrixOS.Core` | Framework/engine, no hardware or app specifics. `IMatrixApp` / `MatrixAppBase` (app lifecycle, `RunInBackground`), `IConfigurableApp` + `AppSetting` + `AppSettingsStorage` (per-app settings, persisted to `app-settings.json`), `AppManager` (register/activate apps), `RenderEngine` (render loop, FPS), `FrameBuffer` and `Pixel`, `IMatrixDevice` (display abstraction), `InterruptService` (notifications drawn over the active app), `AudioDataService` (audio samples to frequency bands for the equalizer). |
| `src/LedMatrixOS.Apps` | **All the display apps**, one class per file (clocks, effects, Spotify, Weather, Tube status/line/departures, Equalizer, Fire, etc.). `Apps.cs` (`BuiltInApps.GetAll()`) is the registry; a new app must be added there. `Services/` has Spotify auth/data. `Common/` has scrolling-text helpers. `Interrupts/` renders interrupt messages. `ColorExtensions.cs` and `ImageSharpExtensions.cs` are shared helpers. |
| `src/LedMatrixOS.Graphics` | Shared drawing helpers: `SimpleGraphics.cs`, `FlipNumberCard.cs`, and `Text/` (`Fonts.cs`, `TextExtensions.cs`, `Fonts/` font assets). `Fonts.Load()` is called at startup. |
| `src/LedMatrixOS.Hardware.RpiLedMatrix` | Raspberry Pi hardware backend: P/Invoke bindings to `librgbmatrix.so` (hzeller/rpi-rgb-led-matrix), options/factory classes, and `RpiLedMatrixDevice` (the `IMatrixDevice` implementation). Only works on a Pi with root. Mostly binding boilerplate; rarely needs changes. |
| `src/LedMatrixOS.Hardware.Simulator` | Simulated device for development. The `SimulatedMatrixDevice` class lives in `Class1.cs` (misleading filename) and renders to PNG for `GET /preview`. |

Dependency direction: `LedMatrixOS` -> `Apps`, `Core`, `Graphics`, both hardware projects. `Apps` -> `Core` + `Graphics`. Hardware projects -> `Core`.

## Where to make common changes

- **New or changed display app:** `src/LedMatrixOS.Apps/<Name>App.cs`, then register in `Apps.cs`. Inherit `MatrixAppBase`; implement `IConfigurableApp` for user-editable settings.
- **New REST endpoint:** `src/LedMatrixOS/Program.cs` (or a new `Endpoints/*.cs` extension like `NotificationEndpoints`).
- **Rendering/lifecycle/engine behaviour:** `src/LedMatrixOS.Core`.
- **Text, fonts, drawing primitives:** `src/LedMatrixOS.Graphics`.
- **Hardware/GPIO/panel config:** `src/LedMatrixOS.Hardware.RpiLedMatrix` and `Matrix` section in `src/LedMatrixOS/appsettings.json`.
- **If you add or change an API endpoint,** `flutter_app/lib/api_service.dart` and `homeassistant/custom_components/ledmatrix_controller/coordinator.py` are the clients that may need matching updates.

## Build and run

```bash
dotnet build                                   # from repo root
cd src/LedMatrixOS && dotnet run --environment Development   # simulator, UI at http://localhost:5005
```

Simulator mode needs `Matrix:UseSimulator = true` (already set in `appsettings.Development.json`). On the Pi, run with `sudo` in Production. There are no .NET test projects. Graphics use SixLabors.ImageSharp. Development is on Windows, so the hardware project can't be exercised locally; use the simulator.

## Gotchas

- Display size is hard-coded in `Program.cs` as `width = 256`, `height = 64`. The `Matrix` config (`Rows`, `Cols`, `ChainLength`) describes the physical panels, not these.
- `README.md` is partly out of date (it mentions `Class1.cs` as core, a leftover `>>>>>>> main` merge marker, and omits newer apps and the interrupt/audio features). Prefer the code over the README.
- Secrets/local config go in `appsettings.local.json` (loaded optionally, should stay out of git). Spotify/Weather apps need API config.
- `PongApp` exists but is commented out of the registry.
