# LedMatrixOS: Roadmap from 7 to 10

## Context

LedMatrixOS works: a solid render loop, a simulator, REST control, per-app settings, interrupts, a Flutter app and Home Assistant. The weak spot is **writing apps**. Each app is hand-built pixel by pixel and re-implements the same plumbing:

- `TubeDeparturesApp.cs` is 938 lines. About half of it is plumbing rather than tube logic: its own paging state machine (7 fields), a cubic-bezier easing solver, clipped text maths, `CoerceInt/BoolSetting`, an `HttpClient` polling loop and magic layout constants (`14 * rowPosition`, `tileStartY = 49`).
- There are **two rendering worlds**. Raw `FrameBuffer` + BDF fonts (crisp, fast) sits beside ImageSharp `Image<Rgb24>` (anti-aliased, alpha). `HomePageApp` creates a new image every frame and calls `ctx.Fill` **once per pixel** (16k calls) for its gradient, which is very slow on a Pi.
- `FrameBuffer` has no alpha blending, clipping or fill helpers. `SimpleGraphics` has just two functions. `TextExtensions` hard-codes `256`/`64` and always draws a shadow.
- Animation is ad hoc. Apps mix `DateTime.Now` with `deltaTime`. The only app-switch transition is a slide, and `ApplyTransition` allocates a new frame on every frame.
- Apps are created with `Activator.CreateInstance`, so they get no DI and no shared `HttpClient`. The Equalizer has to be special-cased in `Program.cs`.
- For everyday use there's no scheduling: nothing shows the commute at 7:30 on weekdays. `WeatherApp` uses **simulated data**. Interrupts take over the whole screen.

**Goal:** make apps quick and fun to write (a declarative, animated, layout-aware toolkit) and make the device useful by itself day to day (schedules, a commute dashboard, overlays). Every phase ships on its own, and existing apps keep working throughout.

---

## Phase 1: Foundations (7 → 8): "stop pushing pixels"

### 1.1 Proper canvas API (`LedMatrixOS.Graphics`)
- **Color**: move HSV out of `Apps/ImageSharpExtensions.cs` and `ColorExtensions.cs` into Core `Pixel`. Add `Pixel.FromHsv`, `Lerp`, `WithBrightness(f)`, `Blend(src, alpha)`, a hex parser and named palettes (with the TfL colours moved out of `TubeDeparturesApp`).
- **FrameBuffer**: add `BlendPixel(x, y, color, alpha)`, `Fill(rect, color)`, `CopyFrom(other, dx, dy)`, `Span` row access and a **clip stack** (`PushClip(rect)` / `PopClip()`) that `SetPixel` respects. This replaces `DrawClippedText`.
- **Primitives** (`SimpleGraphics` → `Canvas` extension methods): Bresenham lines (the current `DrawLine` fills a rectangle, which is wrong for diagonals), rect/rounded-rect outline and fill, circle/ellipse, linear and radial gradients, and progress bars.
- **Text**: `MeasureText`, alignment (left/centre/right), an optional shadow, an ellipsis truncation helper and a `TextStyle` record (font, colour, shadow). Remove the hard-coded 256/64.
- **Sprites/icons**: `Sprite` loaded from small PNGs/GIFs (ImageSharp at load time only), with frame-sequence playback. Add a starter icon pack (weather, tube roundel, music, bell).
- **ImageSharp bridge**: `frame.DrawImage(image, x, y)` with alpha, and a cached reusable `Image` per app so nothing allocates per frame. Fix `HomePageApp` to use it.

### 1.2 App plumbing (`LedMatrixOS.Core`)
- **DI for apps**: `AppManager` creates apps with `ActivatorUtilities.CreateInstance(serviceProvider, type)`. Apps can then inject `IHttpClientFactory`, `AudioDataService`, `ILogger<T>` and options. This removes the Equalizer special case in `Program.cs`.
- **Typed settings**: replace the hand-written `GetSettings` and `UpdateSetting` switches with attribute-driven settings, e.g. `[Setting("Max Departures", Min=1, Max=12)] public int MaxDepartures { get; set; } = 3;`. A single `SettingsBinder` handles reflection and `JsonElement` coercion. `IConfigurableApp` keeps working for old apps.
- **Data polling helper**: `Poll<T>(interval, fetch)` on `MatrixAppBase` returns an `ILiveData<T>` with `Value`, `IsLoading`, `Error`, `LastUpdated` and a `Changed` event. It replaces the `volatile` fields and while-loops copy-pasted across Tube, Weather and Spotify.
- **Engine time**: give apps a `FrameContext` (`Time`, `Delta`, `FrameIndex`) so animation never reads `DateTime.Now` directly. This also makes rendering deterministic for tests.

### 1.3 Test harness
- Add a `tests/LedMatrixOS.Tests` xUnit project with **snapshot tests**: render an app or widget at a fixed `FrameContext` to PNG with the existing simulator encoder and compare it with a golden file. This is the safety net for everything after it.

---

## Phase 2: Animation & UI framework (8 → 9): "make it pop"

### 2.1 Animation core (`LedMatrixOS.Core/Animation`)
- **`Easing`**: the standard set (quad, cubic, back, elastic, bounce, `CubicBezier(p1x, p1y, p2x, p2y)`). The bezier solver moves out of `TubeDeparturesApp.EvaluateBezierProgress`.
- **`Tween<T>`**: a value that animates to a target, with `Lerp` support for float, int, `Pixel` and `Point`. Usage: `_y.To(14, 400.ms(), Easing.OutBack)`.
- **`Timeline`**: sequence, parallel, delay, repeat and yoyo, with callbacks. Usage: `Timeline.Sequence(fadeIn, Delay(8s), slideOut).Loop()`.
- **`Animator`**: one per app, ticked by the engine, so apps never manage timestamps themselves.

### 2.2 Retained-mode widget layer (`LedMatrixOS.Graphics/UI`)
This is the main change: apps describe a **tree of widgets** instead of drawing pixels.
- **`Node`** base: `Position`, `Size`, `Opacity`, `Visible`, `ClipChildren`, and `Animate(...)` on any of them.
- **Layout**: `Stack` (vertical/horizontal, gap, padding), `Grid`, `Dock` (top/bottom/fill) and anchors. This removes the magic Y constants.
- **Widgets**: `Label`, `MarqueeLabel` (replaces both `ScrollOverflowText*` helpers), `RollingNumber` (an odometer/flip digit that animates when the value changes), `Icon`/`AnimatedSprite`, `ProgressBar`, `Badge`/`Pill` (tube line tiles), `Divider`, `Clock`, and `Pager`/`Carousel` (auto-cycles pages with a chosen transition, replacing the departures paging code).
- **`ListView<T>` with keyed diffing**: when bound data changes, rows that moved slide to their new place, new rows fade or slide in and removed rows collapse out. This is what makes a departures board feel alive: a train arriving slides away and the next one moves up.
- **`WidgetApp`** base class: override `Build()` once and bind to `ILiveData`. The framework handles update, layout and render.

Target shape for the rewritten departures app (~150 lines instead of 938):
```csharp
protected override Node Build() =>
  new Dock {
    Fill   = new Pager(pageSize: 3, interval: 8.s(), transition: Transitions.SlideUp(Easing.OutCubic))
               .Bind(_departures, d => new DepartureRow(d)),   // keyed by vehicle id
    Bottom = new Stack(Horizontal, gap: 1) {
               new ListView<LineStatus>(_statuses, s => new Pill(s.Abbrev, s.Color, pulse: !s.Good)),
               new MarqueeLabel(_stationName).Grow(),
               new Clock("HH:mm") }
  };
```

### 2.3 Transitions & effects
- An **`ITransition`** interface used everywhere: app switches, `Pager`, interrupts. It ships with slide (4 directions), fade/crossfade, wipe, dissolve/pixel-scatter, iris, "matrix rain" and "LED split-flap". `RenderEngine` gets a `TransitionRegistry`, keeps preallocated buffers (no per-frame `new FrameBuffer`) and picks a transition per app or at random.
- **Particle system**: emitters with lifetime, velocity, gravity and colour-over-life. Used for confetti on notifications, sparkles and weather (rain/snow on the weather screen). `FireApp`, `MatrixRainApp` and the `HomePageApp` particles can move onto it.
- **Post-effects** as an optional per-app stack: bloom/glow, colour-grade (night tint), CRT scanline and global fade. These run as a pass over the finished frame in `RenderEngine`.

---

## Phase 3: An everyday OS (9 → 10): "it knows what I need"

### 3.1 Scheduler & playlists (`LedMatrixOS.Core/Scheduling`)
- **Playlists**: an ordered list of `(appId, duration, settings override, transition)`. They rotate automatically, e.g. Clock 30s → Weather 15s → Spotify while playing.
- **Schedule rules**: `Mon–Fri 07:15–08:45 → "Commute" playlist`, `23:00–07:00 → dim + clock only`, and `weekends → ambient`. Stored in a JSON file next to `app-settings.json` and editable via REST.
- **Conditional/contextual triggers**: "Spotify is playing → show Spotify", or "line disrupted → bump the Tube status app to the front". Apps can expose `bool WantsAttention`.
- **Brightness schedule / auto-dim**, plus sunrise/sunset from the weather data.

### 3.2 Overlay layer (rework interrupts)
- Interrupts become **layers composited over the running app**, not full takeovers: toasts (slide-in banner), corner badges and full-screen alerts. Each has a priority, a duration and animated enter/exit through `ITransition`.
- Keep the existing `/api/notifications*` endpoints and map them onto the new layer. Add `/api/notifications/toast` with an icon, colour and sound-free "attention" animation (flash border).

### 3.3 Flagship everyday apps
- **Commute dashboard** (composite): next 2–3 departures with a "leave in X min" countdown (configurable walking time; turns amber then red), line status pills, current weather and rain chance, and the time.
- **Real weather**: replace the simulation with Open-Meteo (free, no key). Use animated icons and particle rain/snow.
- **Calendar/next event** (ICS URL), **Home Assistant sensor tiles** (reverse direction: show HA entities on the matrix) and an **upcoming bin day / reminders** tile.

### 3.4 Platform polish
- **Live preview over WebSocket** (pushes frames instead of polling `GET /preview` PNGs) for the web UI and Flutter. Add an app/widget **gallery page** in `wwwroot` that previews every transition and widget.
- **Resilience**: per-app error boundary (render a "⚠ app crashed" card and log, instead of silently swallowing in the `catch` at `RenderEngine.cs:131`), a frame-time HUD toggle and `/api/health`.
- Read the display size from config instead of hard-coding it in `Program.cs`.
- **Stretch goal: declarative screens.** JSON/YAML-defined widget trees with data bindings (`http` JSON path, HA entity) so new simple screens need no C#, and can be created from the phone app.

---

## Suggested order & milestones

| # | Milestone | Proves it with |
|---|---|---|
| 1 | Canvas API + Pixel colour helpers + clip stack | `TubeDeparturesApp` drops `DrawClippedText` and colour maths |
| 2 | DI for apps + `Poll<T>` + typed settings | Tube apps lose the polling loop and Coerce helpers |
| 3 | Snapshot test project | Goldens for Clock and TubeDepartures |
| 4 | Easing/Tween/Timeline | Departures paging uses `Timeline` |
| 5 | Widget tree + layout + `WidgetApp` | Full departures rewrite in ~150 lines |
| 6 | `ListView` keyed diffing + `RollingNumber` | Trains slide up as they depart, minutes "roll" |
| 7 | `ITransition` registry + allocation-free engine | Fade/wipe/dissolve between apps |
| 8 | Scheduler + playlists | Weekday mornings auto-switch to commute |
| 9 | Overlay toasts | HA/phone notifications slide in over any app |
| 10 | Commute dashboard + real weather | The everyday "10" |

Milestones 1–3 are prerequisites. After that, 4–7 (fun) and 8–10 (everyday) can run in parallel.

## Critical files

- `src/LedMatrixOS.Core/`: `FrameBuffer.cs`, `Pixel.cs`, `MatrixAppBase.cs`, `AppManager.cs`, `RenderEngine.cs`, `InterruptService.cs`; new `Animation/`, `Scheduling/`, `Data/`
- `src/LedMatrixOS.Graphics/`: `SimpleGraphics.cs` → `Canvas`, `Text/TextExtensions.cs`; new `UI/`, `Transitions/`, `Particles/`, `Sprites/`
- `src/LedMatrixOS/Program.cs`: DI wiring, scheduler/playlist/overlay endpoints, WebSocket preview
- `src/LedMatrixOS.Apps/TubeDeparturesApp.cs`: the reference migration
- Clients to update when endpoints are added: `flutter_app/lib/api_service.dart`, `homeassistant/custom_components/ledmatrix_controller/coordinator.py`

## Existing code to reuse rather than rewrite

- Bezier easing: `TubeDeparturesApp.EvaluateBezierProgress`/`CubicBezier` → `Easing.CubicBezier`
- HSV: `Apps/ImageSharpExtensions.FromHsv` → `Pixel.FromHsv`
- Scrolling text logic: `Apps/Common/ScrollOverflowTextForFrameBuffer.cs` → `MarqueeLabel`
- Flip digit visuals: `Graphics/FlipNumberCard.cs` → `RollingNumber` flip style
- Background task lifecycle: `MatrixAppBase.RunInBackground` → underpins `Poll<T>`
- Settings persistence: `AppSettingsStorage`, unchanged; the typed binder feeds it

## Verification

- `dotnet build` from the repo root after each milestone.
- `dotnet test tests/LedMatrixOS.Tests` for snapshot goldens. Deterministic `FrameContext` time means animation mid-points can be snapshotted (e.g. a transition at t=0.5).
- Simulator: `cd src/LedMatrixOS && dotnet run --environment Development`, then open http://localhost:5005 and check visually in the browser pane. Switch apps with `POST /api/apps/{id}` to see transitions, and fire `POST /api/notifications/message` to check overlays.
- Performance budget: the frame-time HUD must show the render staying under ~8 ms per frame at 60 fps in the simulator, with no per-frame allocations (check with `dotnet-counters` GC count). Confirm on the Pi before merging the engine changes.
- Scheduler: unit tests with an injected clock (`TimeProvider`) covering weekday/weekend and midnight-crossing rules.
