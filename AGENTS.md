# AGENTS.md

Guidance for AI agents working in this repo. Read this first so you don't waste time exploring the wrong folders.

## What this repo is

LedMatrixOS: software that drives a **256x64 P5 RGB LED matrix** from a **Raspberry Pi**. The core is a .NET 10 app in `src/`. Everything else in the repo is a client of that app's REST API.

## Top-level layout

| Folder | What it is | Priority |
|---|---|---|
| `src/` | **The main project.** The .NET 10 LedMatrixOS code that runs on the Raspberry Pi (and in a simulator on a dev machine). Almost all tasks belong here. | **High** |
| `tests/LedMatrixOS.Tests/` | xUnit tests, including golden-image snapshots (`Snapshots/*.png`) and zero-allocation checks per app. Run `dotnet test`. | **High** |
| `flutter_app/`
| `homeassistant/` | Python Home Assistant custom integration (`custom_components/ledmatrix_controller`) exposing light/select/number/sensor entities over the REST API. Only touch for Home Assistant work. | Low |
| `docs/` | Roadmaps: `ROADMAP.md` (current v2 roadmap) and `ROADMAP_v1.md` (archived v1). Images used by the README (`preview.png`). | Low |
| `.github/` | `CODEOWNERS` only. | Ignore |

Root files: `LedMatrixOS.sln` (solution, includes only the `src/` projects), `Directory.Build.props` (shared settings: net10.0, nullable, implicit usings, LangVersion preview), `README.md` (user-facing docs, partly stale, see Gotchas).

Do not explore `bin/` or `obj/` folders anywhere; they are build output.

## `src/` projects

All six are in `LedMatrixOS.sln`.

| Project | Role |
|---|---|
| `src/LedMatrixOS` | **Entry point** (ASP.NET Core web app). `Program.cs` wires DI, picks the device (simulator vs Pi), registers apps, starts the render loop, and defines the REST endpoints. `Endpoints/` holds `NotificationEndpoints` (alert overlays) and `OverlayEndpoints` (toast/badge/schedule). `ScheduleRunner.cs` applies `schedule.json`. `AppConfig.cs` binds the `Matrix` config section. `wwwroot/index.html` is the web preview UI (WebSocket). `appsettings*.json` is config. `*.http` files are manual request samples. |
| `src/LedMatrixOS.Core` | Engine, no hardware or app specifics. `RenderEngine` (render loop, transitions, overlays, crash card), `AppManager`, `FrameBuffer`/`Pixel`/`FrameContext`, `Animation/`, `Transitions/`, `Data/` (`Poll<T>`), `Settings/` (`[Setting]` attributes), `Scheduling/` (playlists and rules), `Overlays/` (toast/badge/alert), `CrashGuard`, `FrameBroadcaster` (WebSocket preview), `InterruptService` (legacy full-screen takeovers), `AudioDataService`. | **High** |
| `src/LedMatrixOS.Apps` | **All the display apps**. Most are `WidgetApp`s with their own `*Kit`/subfolder of nodes (`Commute/`, `Calendar/`, `HomeAssistant/`, `Tube/`, `Weather/`, ...). `Apps.cs` (`BuiltInApps.GetAll()`) is the registry; a new app must be added there. | **High** |
| `src/LedMatrixOS.Graphics` | Drawing and UI: `SimpleGraphics`, `Text/` (fonts, `TextStyle`), `UI/` (widget tree: `Node`, `Stack`, `Dock`, `Label`, `ListView`, `Pager`, `WidgetApp`, `TextRun`, `CrashCard`), `Particles/`, `Effects/`. `Fonts.Load()` is called at startup. | **High** |
| `src/LedMatrixOS.Hardware.RpiLedMatrix` | Raspberry Pi hardware backend: P/Invoke bindings to `librgbmatrix.so` (hzeller/rpi-rgb-led-matrix), options/factory classes, and `RpiLedMatrixDevice` (the `IMatrixDevice` implementation). Only works on a Pi with root. Mostly binding boilerplate; rarely needs changes. |
| `src/LedMatrixOS.Hardware.Simulator` | Simulated device for development. The `SimulatedMatrixDevice` class lives in `Class1.cs` (misleading filename) and renders to PNG for `GET /preview`. |

Dependency direction: `LedMatrixOS` -> `Apps`, `Core`, `Graphics`, both hardware projects. `Apps` -> `Core` + `Graphics`. Hardware projects -> `Core`.

## Where to make common changes

- **New or changed display app:** `src/LedMatrixOS.Apps/<Name>App.cs`, then register in `Apps.cs`. Prefer `WidgetApp` (override `Build()`, use `[Setting]` properties and `Poll(...)`); add a test file with goldens and a zero-allocation check (copy `CommuteAppTests.cs`). Use `MatrixAppBase` only for low-level pixel apps.
- **User-defined JSON screens:** schema and validation in `src/LedMatrixOS.Core/Screens/` (`ScreenSchema`, `BindingKey`), runtime in `src/LedMatrixOS.Apps/Screens/` (`ScreenApp`, `BindingResolver`, `ScreenNodeFactory`), API in `Endpoints/ScreenEndpoints.cs`, stored in `screens.json`; each screen is an alias `screen:<id>`. A new node type or binding key must be added to the schema, the factory/resolver and `flutter_app/lib/features/screens/`.
- **New REST endpoint:** `src/LedMatrixOS/Program.cs` (or a new `Endpoints/*.cs` extension like `NotificationEndpoints`).
- **Rendering/lifecycle/engine behaviour:** `src/LedMatrixOS.Core`.
- **Text, fonts, drawing primitives:** `src/LedMatrixOS.Graphics`.
- **Hardware/GPIO/panel config:** `src/LedMatrixOS.Hardware.RpiLedMatrix` and `Matrix` section in `src/LedMatrixOS/appsettings.json`.
- **Scheduling / overlays / crash handling:** `src/LedMatrixOS.Core/Scheduling`, `Overlays`, `CrashGuard.cs`.
- **If you add or change an API endpoint,** `flutter_app/lib/api_service.dart` and `homeassistant/custom_components/ledmatrix_controller/` (`coordinator.py`, `__init__.py` services, `services.yaml`) are the clients that may need matching updates.

## Build and run

```bash
dotnet build                                   # from repo root
cd src/LedMatrixOS && dotnet run --environment Development   # simulator, UI at http://localhost:5005
```

Simulator mode needs `Matrix:UseSimulator = true` (already set in `appsettings.Development.json`). On the Pi, run with `sudo` in Production. Run `dotnet test` (golden-image tests compare against `tests/LedMatrixOS.Tests/Snapshots`; regenerate intentionally with `UPDATE_SNAPSHOTS=1`, and look at the PNGs before committing them; `LED_PREVIEW_DIR` writes enlarged previews). Graphics use SixLabors.ImageSharp. Development is on Windows, so the hardware project can't be exercised locally; use the simulator.

## Gotchas

- Display size comes from `Display:Width`/`Display:Height` (default 256x64). The `Matrix` config (`Rows`, `Cols`, `ChainLength`) describes the physical panels, not these.
- Keep steady-state rendering allocation-free (cache `TextRun`s, rebuild strings only when data changes); tests enforce it. Never read `DateTime.Now` in render code; use `FrameContext`/`WidgetApp.Time`.
- Private feed URLs and tokens (`Calendar:IcsUrl`, `HomeAssistant:Token`) are config only, never `[Setting]`s, so they can't be read back through the API.
- `main` requires pull requests; work on a branch.
- Secrets/local config go in `appsettings.local.json` (loaded optionally, should stay out of git). Spotify/Weather apps need API config.
- `PongApp` exists but is commented out of the registry.
