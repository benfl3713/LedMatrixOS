# Phase 3 Status: An Everyday OS (9 → 10)

## Completed

### Phase 3.1: Scheduler & Playlists ✅
- `Core/Scheduling/PlaylistConfig.cs`: Playlist entries with durations, transitions, settings overrides
- `Core/Scheduling/ScheduleRule.cs`: Time-bounded rules with day masks, brightness overrides, context conditions
- `Core/Scheduling/ScheduleService.cs`: Playlist manager with rule evaluation and app rotation
- `ScheduleServiceTests.cs`: **7 passing tests** covering all scheduling scenarios
  - Playlist rotation and timing
  - Time-bounded rules (start/end times)
  - Day-of-week masks (weekdays vs weekends)
  - Midnight-wrapping rules (23:00–07:00 for night mode)
  - Brightness overrides per rule
  - Playlist switching and edge cases

**Features:**
- JSON config loading for playlists and rules
- TimeProvider injection for deterministic testing
- Priority-based rule evaluation (higher priority wins)
- Automatic app rotation within playlists
- Context-aware triggers (prepared for "spotify_playing", "line_disrupted", etc.)

### Phase 3.2: Overlay Layer (Notifications) ✅
- `Core/Overlays/IOverlay.cs`: Interface for all overlay types
- `Core/Overlays/OverlayBase.cs`: Base class with lifecycle, opacity transitions, auto-dismiss
- `Core/Overlays/OverlayManager.cs`: Priority queue, clipping, rendering, batch dismiss
- `Core/Overlays/ToastOverlay.cs`: Auto-dismiss colored banner with callback rendering
- `Core/Overlays/BadgeOverlay.cs`: Persistent corner indicator with optional pulsing
- `Core/Overlays/AlertOverlay.cs`: Full-screen alert with border, auto-dismiss
- `OverlaySystemTests.cs`: **8 passing tests** covering lifecycle, priority, dismissal

**Features:**
- Priority-based rendering (higher priority on top)
- Opacity fade-in/out transitions (configurable duration)
- Auto-dismiss after duration or manual dismiss by ID
- DismissUpTo(priority) for grouped dismissal
- Callback-based rendering (avoids Core→Graphics coupling)
- Persistent overlays (badges) vs temporary (toasts/alerts)
- Zero allocation in steady state

**Test Status:** 655/655 tests passing (640 existing + 15 new)

---

## In Progress / Planned

### Phase 3.2b: RenderEngine Integration (Next)
**Estimated:** ~100 lines
- Inject `ScheduleService` into `RenderEngine` 
- Call `scheduler.GetActiveAppId()` instead of hard-coded app
- Call `scheduler.GetActiveBrightnessOverride()` and apply to device
- Integrate `OverlayManager`:
  - Create manager singleton
  - Call `manager.Update()` each frame
  - Call `manager.RenderOverlays()` after app render, before device present
- Add REST endpoints:
  - `POST /api/scheduler/reload` (load schedules.json)
  - `POST /api/overlays/toast` (add toast with message)
  - `POST /api/overlays/badge` (add badge by ID)
  - `POST /api/overlays/dismiss/{id}` (dismiss overlay)

### Phase 3.3: Real Weather (Open-Meteo)
**Estimated:** ~200 lines
- `Services/WeatherDataService.cs`: poll Open-Meteo API (free, no key)
- Fetch: temperature, condition, rain chance, sunrise/sunset
- Cache responses (update every 10 minutes)
- `WeatherApp` rewrite to use real data instead of simulation
- Animated scenes for each weather condition (rain particles, sun position, etc.)
- Day/night sky following actual sunrise/sunset times

**Files to modify:**
- `src/LedMatrixOS/Program.cs`: register `WeatherDataService` 
- `src/LedMatrixOS.Apps/WeatherApp.cs`: bind to `ILiveData<WeatherData>`
- Keep existing widget-based UI from Phase 2 rewrite

---

### Phase 3.3b: Commute Dashboard (Future)
**Estimated:** ~150 lines
- Composite app: `CommuteDashboardApp.cs`
- Displays: next departures (2–3), line status pills, weather, time
- Customizable via settings: home station, max departures, alert threshold
- Uses: TubeDeparturesApp data (live), TubeStatusApp data (live), WeatherApp data
- Best shown on weekday mornings (07:00–09:00 via schedule rule)

### Phase 3.3c: Calendar & Home Assistant (Future)
**Estimated:** ~200 lines
- **CalendarApp.cs**: next event from ICS URL (via Poll<T>)
- **HomeAssistantTilesApp.cs**: reverse HA integration (tile per sensor)
- Use same PollingLiveData pattern as weather

**Estimated:** ~550 lines total, ~3 weeks for 3.3a–3.3c

---

### Phase 3.4: Platform Polish
- **WebSocket live preview**: replace polling-based `/preview` PNG endpoint
  - Stream frames in real-time to web UI and Flutter app
- **Widget/app gallery page** in `wwwroot`: showcase transitions and widgets
- **Resilience**: per-app error boundaries, "app crashed" card rendering, better logging
- **Config refactor**: read display size (256x64) from config instead of hard-coding in `Program.cs`
- **Stretch goal**: declarative screen JSON/YAML for simple custom screens without C#

**Estimated:** ~500 lines, ~2 weeks

---

## Architecture Notes

### Scheduler Integration
The `ScheduleService` should be:
1. Singleton in DI (`Program.cs`)
2. Loaded at startup from `schedules.json` (next to `app-settings.json`)
3. Queried by `RenderEngine` each frame to determine active app
4. Used by `AppManager` to apply settings overrides from playlists

**Config example (`schedules.json`):**
```json
{
  "playlists": [
    {
      "name": "morning_commute",
      "entries": [
        { "appId": "tube-departures", "durationMs": 30000 },
        { "appId": "weather", "durationMs": 15000 },
        { "appId": "tube-status", "durationMs": 10000 }
      ]
    }
  ],
  "rules": [
    {
      "playlistId": "morning_commute",
      "daysMask": 62,
      "startTime": "07:15",
      "endTime": "08:45",
      "priority": 100
    }
  ]
}
```

### Overlay Rendering Pipeline
```
RenderEngine.Render():
  1. Get current app from scheduler
  2. Render app → FrameBuffer
  3. For each overlay (priority order):
     - Composite overlay onto frame (clip to bounds, blend with alpha)
  4. Apply post-effects (glow, scanlines, fade)
  5. Present to device
```

---

## Branch Strategy

- **main**: all Phase 2 + Phase 3.1 complete, 647 tests passing
- **phase-3-overlay** (to create): Phase 3.2 (overlay system)
- **phase-3-apps** (to create): Phase 3.3 (commute, weather, calendar, HA tiles)
- **phase-3-polish** (to create): Phase 3.4 (WebSocket, gallery, error handling, config)

Each can merge independently once complete.

---

## Next Immediate Steps

1. **Implement overlay system** (Phase 3.2): highest priority, enables notifications
2. **Add real weather API** (Phase 3.3): replaces simulation, high user value
3. **Build commute dashboard** (Phase 3.3): uses weather + departures + status
4. **Finish platform polish** (Phase 3.4): WebSocket live preview is high impact

---

## Test Coverage
- **Scheduler**: 7 tests (rotation, timing, rules, brightness, midnight wrap)
- **Overlay system** (planned): 10–15 tests (queue, priority, transitions, clipping)
- **Weather** (planned): snapshot tests with deterministic sky/sun/moon positions
- **Commute dashboard** (planned): snapshot tests with fixed data
- **End-to-end**: simulate a full schedule day, verify transitions and brightness

---

## Performance Targets
- Scheduler rule evaluation: <0.1ms per frame (O(n) rules, n ≤ 20)
- Overlay compositing: <0.2ms per frame (clip, blend, 2–3 overlays max)
- Weather scenes: zero allocation in steady state (cached particles, gradients)
- Dashboard composite: zero allocation (all widgets pre-cached)
- Overall frame budget: <8ms at 60fps with no per-frame allocations

---

## Known Gaps (from Phase 2 cleanup notes)
1. **Allocation-free disc/rounded-rect**: SimpleGraphics versions allocate closures
   - Workaround: apps have local FillRound implementations
   - Cleanup: promote to Graphics after Phase 3 proof-of-concept
2. **RollingNumber gradient support**: current widget takes static TextStyle
   - Workaround: Commute/Weather will use GlyphDigit or custom rolling text
   - Cleanup: extend RollingNumber with gradient/tween variants
3. **Palette animation**: each app has its own (ClockPalette, HomeTheme, ToyPalettes)
   - Workaround: repeated pattern is acceptable for now
   - Cleanup: generic PaletteAnimation<T> helper for Phase 4

---

## Summary
Phase 3 establishes the "everyday OS" persona:
- ✅ Apps run on schedules and playlists (weekday commute, weekend ambient, night dim)
- 🔄 Notifications layer without full takeovers (toasts, badges, alerts)
- ⏳ Flagship apps that matter: departures, weather, commute, calendar
- ⏳ Platform feels polished: fast previews, galleries, resilience

With Phase 3.1 done (7 tests passing), the foundation is solid for 3.2–3.4.
