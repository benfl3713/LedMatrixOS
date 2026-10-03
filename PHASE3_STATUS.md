> Historical: Phase 3 of the v1 roadmap is complete. Open work now lives in [docs/ROADMAP.md](docs/ROADMAP.md).

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

**Test Status:** 731/731 passing

---

## In Progress / Planned

### Phase 3.2b: RenderEngine Integration ✅
- `RenderEngine.Overlays` is updated and composited each frame, after post-effects.
- `ScheduleRunner` (hosted service) applies the schedule once a second. It is edge-triggered, so a manual app switch sticks until the playlist next rotates. It also applies per-entry settings overrides and brightness overrides (the previous brightness is restored when the rule ends).
- Rules match against local time. `schedule.json` is read from the app directory.
- Endpoints: `POST /api/overlays/toast`, `POST /api/overlays/badge`, `DELETE /api/overlays/{id}`, `DELETE /api/overlays`, `POST /api/schedule/reload`, `GET /api/schedule`.
- Verified in the simulator: toast renders over the running app, and a two-app playlist rotates with a brightness override.
- Not done: Flutter and Home Assistant clients do not know the new endpoints yet, and the old `/api/notifications*` endpoints still use the full-screen interrupt path.

### Phase 3.3a: Real Weather ✅
Already delivered by the Phase 2 `WeatherApp` rewrite (Open-Meteo, location and units settings, animated scenes). Nothing further to do.

### Phase 3.3b: Commute Dashboard ✅
- `CommuteApp` (`id: commute`): the hero is "LEAVE IN n MIN" for the first train you can still walk to (green, then amber, then a flashing GO plate). It sits beside current weather, with line status pills and the clock along the bottom.
- `Commute/CommutePlanner.cs`: pure logic with unit tests (walk time, urgency thresholds, all-missed).
- Reuses the Tube departures data (`TflApi`, `DepartureBoardModel`, `LinePill`) and the Open-Meteo `IWeatherSource`.
- Settings: Station ID (falls back to `Commute:StationId`, then `TubeDeparturesApp:StationId`), Platform Filter, Walk Minutes, Location, Units.
- 25 tests including 7 goldens (reviewed by eye) and a zero-allocation steady-state test.
- `src/LedMatrixOS/schedule.example.json` shows a weekday-morning commute playlist plus a night-dim rule; copy it next to the executable as `schedule.json`.

### Phase 3.3c: Home Assistant tiles ✅
- `HomeAssistantTilesApp` (`id: ha-tiles`): up to four labelled tiles per page (numbers, on/off, N/A), paged with a fade. Entities are set as `sensor.x|Label, light.y`.
- URL and long-lived token come from configuration only (`HomeAssistant:BaseUrl`, `HomeAssistant:Token`), never from a setting.
- 17 tests (parsing, API client with a stub handler, goldens, zero-allocation).

### Calendar app ✅
`CalendarApp` (`id: calendar`) shows the next event from an `.ics` feed (`Calendar:IcsUrl`, config only) with the next three beside it. The parser handles UTC/TZID/floating times, all-day events, folding, and simple recurrence (daily, weekly with BYDAY, monthly, yearly, INTERVAL/COUNT/UNTIL/EXDATE). Overridden instances (RECURRENCE-ID) are not handled.

### WebSocket live preview ✅
`GET /ws/preview` streams binary frames (`[width u16][height u16][RGB...]`, up to 30 fps, only when the picture changed) from `FrameBroadcaster`, which copies frames only while someone is subscribed. The web UI uses it with a fallback to polling `/preview`; checked in the browser. The Flutter app still polls the PNG.

### Client updates ✅
- Home Assistant integration: services `show_toast`, `set_badge`, `dismiss_overlay`, `reload_schedule` (see `services.yaml`). The new apps appear in the existing app select automatically.
- Flutter: `showToast`, `setBadge`, `dismissOverlay`, `clearOverlays`, `reloadSchedule`, `getHealth`, a "Send message" button and icons for the new apps. `flutter analyze` reports no errors or warnings.

### Crash card ✅
Any app that throws shows its name and exception on the panel instead of freezing (`CrashGuard` + `CrashCard`), retries after 5s, and clears on app switch.

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

**Config example (`schedule.json`):**
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
- ✅ Notifications layer without full takeovers (toasts, badges, alerts)
- ✅ Flagship apps that matter: departures, weather, commute, calendar
- ⏳ Platform feels polished: fast previews, galleries, resilience

With Phases 3.1–3.3 done, Phase 3.4 (platform polish) remains.
