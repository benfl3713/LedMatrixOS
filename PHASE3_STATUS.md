# Phase 3 Status: An Everyday OS (9 → 10)

## Completed

### Phase 3.1: Scheduler & Playlists ✅
- `Core/Scheduling/PlaylistConfig.cs`: Playlist entries with durations, transitions, settings overrides
- `Core/Scheduling/ScheduleRule.cs`: Time-bounded rules with day masks, brightness overrides, context conditions
- `Core/Scheduling/ScheduleService.cs`: Playlist manager with rule evaluation and app rotation
- `ScheduleServiceTests.cs`: 7 comprehensive tests covering all scheduling scenarios
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

**Test Status:** 647/647 tests passing (640 existing + 7 new)

---

## In Progress / Planned

### Phase 3.2: Overlay Layer (Notifications)
The interrupt system needs to be reworked to support:
- **Toast notifications**: slide-in banner with icon, text, duration
- **Corner badges**: compact alert indicators (line disruption, app attention)
- **Full-screen alerts**: urgent messages with priority/duration
- **Composited rendering**: layers over the active app, not full takeovers
- **Priority queue**: resolve conflicts when multiple overlays want attention
- **Transition animations**: ITransition for enter/exit of overlays

**Planned files:**
- `Core/Overlays/IOverlay.cs`: base interface (position, duration, priority, transition)
- `Core/Overlays/ToastOverlay.cs`, `BadgeOverlay.cs`, `AlertOverlay.cs`
- `Core/Overlays/OverlayManager.cs`: queue, priority, rendering
- Integration into `RenderEngine.cs` (render active app, then composite overlays)
- Remap existing `/api/notifications/*` REST endpoints to overlay system

**Estimated:** ~300–400 lines, ~2 weeks for full implementation + testing

---

### Phase 3.3: Flagship Everyday Apps
- **Commute Dashboard** (composite): next departures + line status + weather + time
  - Requires: TubeDeparturesApp output, Weather live data, TimeWidget
  - Planned: ~150 lines as a WidgetApp
- **Real Weather** (replace simulation)
  - API: Open-Meteo (free, no key) for hourly/daily forecast
  - Features: animated scenes per weather condition, temperature roll, day/night sky
  - Planned: ~200 lines (API client + animated scenes)
- **Calendar/Events** (ICS feed reader)
  - Next event display with time/location
  - Planned: ~100 lines
- **Home Assistant Tiles** (reverse HA integration)
  - Show HA sensor values (temperature, humidity, etc.) on matrix
  - Planned: ~100 lines

**Estimated:** ~550 lines total, ~3 weeks

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
