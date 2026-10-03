# LedMatrixOS: Roadmap v2

## Context

The v1 roadmap (`docs/ROADMAP.md`, "7 to 10") is done: widget framework, animation, transitions, scheduler, overlays, real weather, commute, calendar, HA tiles, WebSocket preview. The parts of the platform users see haven't kept up:
- **Display apps:** several look dated or waste the panel. Tube Departures shows only 2 following trains with ~15px unused and ignores direction. Spotify is still the legacy ImageSharp app: it decodes artwork every frame, has no empty state and no goldens. Tube Status overflows on "All London rail". Calendar and HA tiles hand-place pixels and bypass `Pager`. Home has no data. Four clock apps overlap.
- **Flutter app:** one 526-line `HomePage` god-object with errors silently swallowed. The finished live preview widget is never shown. Brightness is 0–100 on a 0–255 API. Colour settings are text boxes, and only the active app can be configured. There is no schedule, transitions or overlay management, and the only test is a broken template test.
- **Server gaps blocking the clients:** no schedule write API, app settings rejected (400) unless the app is active, `/api/notifications*` still take over the full screen, schedule `Condition`s are stubbed.

---

## Phase 0: Housekeeping
- Merge the `cleanup-helpers` and `transport-apps` branches to `main` through PRs.
- Factor the duplicated station-search, line-id and line-status code shared by Departures, Commute and Bus into one `TflStopPicker` helper in `src/LedMatrixOS.Apps/Tube/`.
- Delete the empty `PongApp.cs` and the dead Spotify fields.

## Phase 1: Server API the clients need (`src/LedMatrixOS`, `Core/Scheduling`)
- **Schedule CRUD:**
  - `GET` stays; add `PUT /api/schedule` (validate, write `schedule.json`, reload).
  - Add `GET /api/schedule/status` (active rule, playlist position, next change).
  - Reuse `ScheduleRunner.cs` and the scheduling models.
- **Settings for any app:** allow `GET/POST /api/apps/{id}/settings` on inactive apps by keeping app instances settings-addressable in `AppManager` (persisted via `AppSettingsStorage`).
- **Overlays:**
  - Add `GET /api/overlays`, listing active overlays so the phone can dismiss them.
  - Map `/api/notifications*` onto `Overlays` alerts instead of `InterruptService`, keeping request shapes.
- **Context triggers:**
  - Implement schedule `Condition`s: `spotify_playing`, `line_disrupted:<line>`, `bus_due:<stop>`, `ha_state:<entity>=<v>`.
  - Use an `IAttentionSource` that apps and services can implement, so the Tube and Bus apps can bump themselves to the front.
- Update the HA integration (`coordinator.py`, `services.yaml`) for new endpoints.

## Phase 2: Display refresh (existing apps)
Each item ships with updated or new goldens and a zero-allocation test.
1. **Tube Departures redesign:** a `Board Style` setting.
   - **Split** (default): two direction columns (e.g. Northbound | Southbound), each with its next 3 trains as compact rows. That is 6 trains visible, with no unused rows.
   - **Platform** (classic LU dot-matrix): amber on black, "1 Brixton 3 min" rows and a scrolling bottom line.
   - **Hero** keeps today's look.
   - Direction is grouped from `platformName` in `DepartureBoardModel`.
2. **Spotify rewrite as a `WidgetApp`:**
   - Decode and resize the artwork once per track and cache it as a `Sprite`.
   - Layout: title marquee, artist, progress bar and a palette from the art.
   - Real visualiser from `AudioDataService` when audio is streaming.
   - "Nothing playing" state and an injected `HttpClient`.
   - Goldens via a fake data source.
3. **Tube Status:**
   - Fix the "All London rail" overflow with a two-row tile layout when there are more than 14 lines.
   - Disruption cards show the reason as a marquee.
4. **Smart Home screen:** optional data chips on `HomePageApp`: weather, next event, worst line status and next bus. They reuse the existing data sources and cycle in a corner `Pager`.
5. **Weather:**
   - "Rain next hour" `Sparkline` and an hourly temperature `BarChart` page, using `Graphics/UI/Charts.cs`.
   - Reuse the weather chip in Commute.
6. **Calendar:** move to layout widgets, add a "Today" timeline page (event bars on an hour axis), and support RECURRENCE-ID overrides.
7. **HA Tiles:**
   - Use the shared `Pager`.
   - Add icon and on/off glyph tile kinds.
   - Add an optional 24h history `Sparkline` (HA history API).
8. **Clocks consolidation:** fold Clock, AnimatedClock and FlipClock into one `Clock` app with a `Style` select. The old ids stay as aliases so schedules keep working.

## Phase 3: New apps (transport and daily life)
- **Rail Departures:** National Rail board with a "last train home" alert overlay.
  - **No live integration yet.** Data comes from an `IRailDepartureSource` interface whose only implementation is `HardcodedRailSource`: fixed sample services, with times computed relative to `WidgetApp.Time` so the board looks live.
  - The user will add the real API integration later, behind the same interface. Do not add Huxley, RTT or any other provider.
- **Cycle Hub:** Santander BikePoint docks (bikes and e-bikes free) plus a wind and rain `Sparkline` and a ride-or-Tube verdict.
- **Journey Planner "Leave by":** TfL Journey Planner to saved destinations, showing the best route as line pills, the duration and a leave countdown. Auto-shown by a schedule rule.
- **Plane Spotter:** keyless OpenSky lookup within a radius of the configured lat/lon. Shows callsign, route, altitude `RollingNumber` and heading arrow, with a sweep animation for new aircraft.
- **Reminders and Bin Day:** from the Calendar feed or a rules list. A colour-coded bin and countdown take over the evening before.
- **Morning Briefing:** a composite that pages weather, first event, commute status and bins once, then hands back to the playlist.

## Phase 4: Flutter app rewrite (in place, Android/iOS)
**Architecture:**
- Riverpod for state and go_router with a bottom nav.
- Feature folders (`lib/features/{now,apps,schedule,notify,settings}`) plus a typed `api/` client with timeouts and error results.
- Material 3 dynamic colour with a proper light and dark theme.

**Screens:**
- **Now:**
  - The live `/ws/preview` is the hero (reuse the parsing in `widgets/live_preview_widget.dart`).
  - Current app card, quick-switch strip, power, and brightness as 0–255 shown as a percentage.
  - Transition picker (`/api/transitions`).
- **Apps:**
  - Grid with search, and a settings sheet that works for any app (Phase 1) and refreshes live.
  - Colour picker for Color settings.
  - Station and stop pickers that drive the existing Search/Select settings.
- **Schedule:** week view of rules (day mask, time range, priority, brightness), a playlist editor with reorder, durations and transitions, "active now" status, and a save via `PUT /api/schedule`.
- **Notify:**
  - Toast, badge and alert composer (text, colours, duration, position) with saved presets.
  - Active-overlay list with dismiss.
- **Settings:** device URL and health/uptime (`/api/health`), and the audio-stream card for the equalizer.

**Quality:**
- Surface every API error as a snackbar.
- Widget tests per screen against a fake API, plus api client unit tests.
- Delete the template test.

## Phase 5: Stretch
- Declarative JSON screens (widget tree plus bindings), creatable from the phone.
- `QrCode` node and Party Mode.
- Widget and transition gallery page in `wwwroot`.
- Platform gaps: `Panel` absolute positioning, transparent `Pager` transitions, a writable `FrameBuffer` span API.

## Suggested order

| # | Milestone | Proves it with |
|---|---|---|
| 1 | Phase 0 + schedule/settings/overlay APIs | Flutter can edit an inactive app and the schedule |
| 2 | Tube Departures redesign | 6 trains visible, split by direction |
| 3 | Spotify rewrite | Goldens exist; no per-frame decode |
| 4 | Flutter Now + Apps screens | Live preview on the phone |
| 5 | Flutter Schedule + Notify | Edit a rule, send a toast from the phone |
| 6 | Weather/Calendar/HA/Status/Home refresh | Charts on real data |
| 7 | New apps (Rail, Cycle, Journey, Bins, Planes, Briefing) | Each with goldens + zero-alloc test |
| 8 | Context triggers | Disruption bumps Tube Status automatically |

Milestone 1 unblocks the Flutter work, which then runs in parallel with the display work.

## How to execute

These keep costs low. The main (Opus) session **orchestrates and reviews only**. Cheaper subagents (`Agent` tool with `model`) do the token-heavy reading and writing.

**Who does what:**
| Work | Agent | Model |
|---|---|---|
| Codebase searches, "where is X", API/endpoint inventories | `Explore` | `haiku` |
| Implementing one roadmap item (code, tests, goldens) | `general-purpose`, `isolation: "worktree"` | `sonnet` |
| Mechanical edits: docs, registry lines, renames, HA/Flutter client endpoint stubs, deleting dead code | `general-purpose` | `haiku` |
| Design-heavy layout work (Departures redesign, Flutter shell/architecture) | `general-purpose`, worktree | `sonnet`. The orchestrator reviews the first render before the agent continues. |
| Final review of each milestone diff | orchestrator, or `/code-review low` | — |

**Rules for the orchestrator:**
1. Give each agent **one roadmap item** and a self-contained brief:
   - the goal
   - the exact files to touch and the patterns to copy (e.g. "copy `BusArrivalsApp.cs` + `BusArrivalsAppTests.cs`")
   - the constraints from AGENTS.md: allocation-free rendering, no `DateTime.Now`, secrets config-only, register in `Apps.cs`
   - the done criteria: `dotnet test` green, new goldens written with `UPDATE_SNAPSHOTS=1`
   - "report back only a ≤150-word summary + list of changed files", so results don't flood the main context
   - **a first step to sync the base:** worktrees can fork from an old commit, so tell the agent to run `git merge --ff-only <integration branch>` (or `git merge <integration branch>`) before touching anything, and to check `git log --oneline -5` shows the latest work. The Phase 1 agent skipped this and rebuilt notifications-as-overlays that already existed, which cost a manual merge.
2. **Don't re-read what agents produced** beyond:
   - `git diff --stat`
   - the new golden PNGs, which the orchestrator views itself (visual judgement must not be delegated)
   - spot reads of anything that looks wrong
3. **Parallelism:**
   - Run independent items concurrently, at most 3 agents, each in its own worktree.
   - Items that touch the same hot files (`Apps.cs`, `Tube/TflApi.cs`, `Program.cs`, `flutter_app/lib/api/`) must be sequential, or `Apps.cs` registration is left to the orchestrator at merge time.
4. **Fixes:**
   - Continue the same agent with `SendMessage` instead of spawning a new one; it keeps its context.
   - Escalate to the orchestrator only after two failed attempts.
5. **Delivery:**
   - One branch and PR per milestone (`main` requires PRs).
   - The orchestrator merges the agent worktrees, runs the full `dotnet test` (and `flutter test` for Phase 4) once, then commits.
6. **Flutter:** an agent can run `flutter analyze` and `flutter test` but can't see the UI. The orchestrator checks screens by running the app or screenshotting widget-test goldens before sign-off.

## Critical files

- `src/LedMatrixOS/Program.cs`
- `src/LedMatrixOS/Endpoints/`
- `src/LedMatrixOS/ScheduleRunner.cs`
- `src/LedMatrixOS.Core/Scheduling/`
- `src/LedMatrixOS.Core/Overlays/`
- `src/LedMatrixOS.Core/AppManager.cs`
- `src/LedMatrixOS.Apps/TubeDeparturesApp.cs`
- `src/LedMatrixOS.Apps/Tube/DepartureBoardModel.cs`
- `src/LedMatrixOS.Apps/SpotifyApp.cs`
- `src/LedMatrixOS.Apps/Apps.cs`
- `src/LedMatrixOS.Graphics/UI/Charts.cs`
- `flutter_app/lib/`
- `homeassistant/custom_components/ledmatrix_controller/`

## Verification

- `dotnet build` from the repo root after each milestone.
- `dotnet test` with goldens reviewed visually. Regenerate intentionally with `UPDATE_SNAPSHOTS=1`, and check the PNGs before committing. Use `LED_PREVIEW_DIR` to write enlarged previews.
- Simulator: `cd src/LedMatrixOS && dotnet run --environment Development`, then open http://localhost:5005 and check visually.
- Flutter: `flutter analyze` and `flutter test` for Phase 4.
