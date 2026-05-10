# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

JetOS is a Space Engineers programmable block script providing a fighter jet operating system — HUD, radar, weapons, gun turret auto-tracking, canard AoA damping, and flight control. Built using MDK2 (Malware's Development Kit 2).

## Build System

**Target**: .NET Framework 4.8, C# 6.0 | **Platform**: x64 | **Runs at**: `UpdateFrequency.Update1`

```bash
dotnet build Mdk.PbScript2.sln --configuration Release   # auto-deploys to %APPDATA%/SpaceEngineers/IngameScripts/local/Mdk.PbScript2
dotnet build Mdk.PbScript2.sln --configuration Debug
dotnet clean Mdk.PbScript2.sln
```

No automated tests — verification is in-game. Minification level: `Mdk.PbScript2/Mdk.PbScript2.mdk.ini` (currently `full`). Levels: `none` < `trim` < `stripcomments` < `lite` < `full`.

### Files excluded from the build

The csproj excludes these paths via `<Compile Remove="..." />`. They exist in the repo as experimental/alternate code but do NOT compile:

- `Diagnostics/**` — standalone PB scripts pasted into a separate PB in-game. They have a top-level `Main()` and do NOT use the `partial class Program` pattern.
- `UI/StartupSequence.cs` — disabled boot animation
- `UI/TerrainRenderer.cs`, `UI/StatusPanelRenderer.idle-slides.cs` — disabled visualizations
- `Utilities/TerrainAPI.cs` — older heightmap implementation (superseded by `TerrainData.cs`)
- `Modules/TerrainMapModule.cs` — alternate terrain page

When adding code to any of these files, remember it won't reach the game until un-excluded.

## Architecture

### Partial Class Pattern (Critical)

Every `.cs` file declares `partial class Program` inside `namespace IngameScript`. MDK2 merges them into one compilation unit for Space Engineers. New classes must nest inside `partial class Program`:

```csharp
namespace IngameScript {
    partial class Program {
        public class MyNewClass { ... }
    }
}
```

### Shortcuts (minification aliases)

`Utilities/Shortcuts.cs` defines short-name helpers used pervasively across the codebase to keep minified output small. When reading code, these are NOT obscure — they are the project's conventional spelling:

- `VN` = `Vector3D.Normalize`, `VD` = `Dot`, `VX` = `Cross`, `VDi` = `Distance`, `VTN` = `TransformNormal`, `VZ` = `Vector3D.Zero`
- `Cr(r,g,b[,a])` = `new Color(...)`, `Cl` = `Clamp`, `Mn` = `Min`, `Mx` = `Max`, `Ab` = `Abs`, `Sn/Cs` = `Sin/Cos`, `At2` = `Atan2`, `ToDeg/ToRad`, `V2` = `new Vector2`, `PI`

When writing new code, prefer these over verbose forms — matching project style keeps minified output compact.

### Entry Point and Tick Loop

`Program.cs` does nothing but call `SystemManager.Initialize(this)` in the ctor (+ `UpdateFrequency = Update1`) and `SystemManager.Main(argument, updateSource)` each tick, wrapped in a try/catch:
- `NullReferenceException` → `Echo` the error and call `SystemManager.Initialize(program)` to recover from missing blocks
- Any other exception → `Echo` only, no auto-recovery (so real bugs surface)

**SE Double-Main Call Guard** (`SystemManager.Main`): When a toolbar argument is triggered, SE calls `Main()` twice in the same sim tick — once with `UpdateType.Trigger`, then again with `Update1`. The `Trigger` pass stores the argument in `_pendingArgument` and returns early. The `Update1` pass consumes the pending argument and runs the real tick. Without this, `GameTicks`/`DeltaSeconds` double-advance and modules tick twice per sim tick. This is a foundational invariant — do not remove.

### Tick order (SystemManager.Main)

1. Trigger-vs-Update1 guard (above)
2. Advance `Jet.GameTicks`; update `DeltaSeconds`/`ElapsedSeconds`/`Jet.GameSeconds` from `Runtime.TimeSinceLastRun` (clamped to `[1/60, 1.0]`)
3. Cache gravity: `_myJet.CachedGravity = _cockpit.GetNaturalGravity()`
4. `TerrainData.Tick(Me, cockpitPos)` — pulls heightmap chunks from the `TerrainAPI` mod property (one-time download, then offline lookups)
5. Altitude/speed warning hysteresis: if `velocityKnots > speed_warning && altitude < altitude_warning`, latch `altitudeWarningActive` and request `"Tief"` warning sound; unlatch with 20kts / 40m dead-band
6. Handle input (arg empty → render menu; arg set → `HandleInput`)
7. `currentModule.Tick()`
8. Background-tick modules that are not the current module: `HUDModule`, `RadarControlModule`, `AirtoAir`, `GunControlModule`, `CanardModule` (terrain/config/air-to-ground do NOT background-tick)
9. `HandleSpecialFunctionInputs` → forwards numeric argument to `currentModule.HandleSpecialFunction(key)`
10. `SoundManager.Tick(ElapsedSeconds)` — must run AFTER all module Ticks so their `RequestWarning/RequestWeapon` calls are seen this tick (not deferred by one)
11. Store `Runtime.CurrentInstructionCount` into `Jet.IC`, track peak `IP` and EMA average `IA`

### Module init order (SystemManager.Initialize)

Order matters — earlier modules are referenced by later ones:

1. `CustomDataManager.Initialize(Me)`, `SoundManager.Initialize(GTS)`, `TerrainData.Probe/Init(Me)`
2. `RadarControlModule` — stored on `_myJet.radarControl` so HUD + AirtoAir can use it
3. `AirToGround`
4. `AirtoAir`
5. `HUDModule` (receives `radarControlModule` for radar display)
6. `UIController` (constructed from `lcdMain`/`lcdExtra`)
7. `ConfigurationModule`
8. `GunControlModule`
9. `TerrainModule`
10. `CanardModule`

`mainMenuOptions` auto-populates from each module's `.name` field — no separate array.

### Jet (hardware abstraction)

`Jet` holds all block references gathered once in its constructor via `GridTerminalSystem`:
- `_cockpit` (required — ctor returns early if missing, leaving the jet non-functional)
- Thrusters split into left/right/center by grid X position relative to cockpit (SE convention: looking forward, X+ is LEFT), then further split into engines vs afterburner (`leftAB`/`rightAB`/`centerAB`) by whether subtype contains `"Hydrogen"`. Thrusters named `"Industrial"` are excluded entirely.
- `_bays` (merge blocks named `"Bay N"`, sorted numerically)
- `leftstab`/`rightstab` (blocks containing `"normalstab"`/`"invertedstab"`)
- `tanks` (gas tanks containing `"Jet"` — treated as hydrogen tanks)
- `batteries`, `_gatlings`, `hudBlock` (+ `hud` as `IMyTextSurface`)

`enemyList` is the authoritative enemy database. `UpdateOrAddEnemy` dedupes in 3 tiers: EntityId (O(1) via `_entityIdIndex` dictionary) → Name match → 50m proximity. Acceleration is maintained with an EMA (α=0.4) over successive updates. Each contact carries a 30-bit `TrackHistory` (bit 0 = most recent second) that shifts left each second without an update — used to render the tracking timeline on the weapon screen.

`CONTACT_DECAY_SECONDS = 30`, `SELECTED_DECAY_SECONDS = 60` (longer for the pilot-selected target). Decay check throttled to once per `DECAY_CHECK_SECONDS = 1.0` of wall-clock time.

### Timing

`SystemManager.DeltaSeconds` (per-tick wall-clock delta, clamped) and `SystemManager.ElapsedSeconds` (accumulated) are the lag-resistant time source. `Jet.GameSeconds` mirrors `ElapsedSeconds`. **Use wall-clock seconds for all durations** (decay, cooldowns, intervals). `Jet.GameTicks` is only a raw call counter retained for ordering logic that doesn't depend on wall-clock time (e.g. `SoundManager`'s `FRAME_DELAY`). Pausing SE freezes aging because `TimeSinceLastRun` doesn't advance.

### Module System

All modules inherit from `ProgramModule` (`Modules/ProgramModule.cs`):
- **Required**: `GetOptions()`, `ExecuteOption(int)`
- **Optional overrides**: `Tick()`, `HandleSpecialFunction(int)`, `HandleNavigation(bool)` → return true to consume, `HandleBack()` → return true to consume, `GetHotkeys()`, `HasCustomScreen` + `RenderCustomScreen(frame, area)` (takes over MFD surface 0 when true — used by `TerrainModule` for its map page)
- `name` field is what shows in the main menu

**Adding a new module**:
1. Create file in `Modules/` with class inheriting `ProgramModule` inside `partial class Program`
2. Implement `GetOptions` + `ExecuteOption` (+ whatever else)
3. Instantiate in `SystemManager.Initialize` and `modules.Add(...)`
4. If it needs background ticking, add a null-check + `Tick()` block in `SystemManager.Main()` after the existing air-to-air/gun/canard blocks
5. Menu options populate automatically

### SoundManager (static, dual-channel)

Two independent channels: `warningChannel` ("Sound Block Warning", vol 1.0) and `weaponChannel` ("Canopy Side Plate Sound Block", vol 0.3). Each tick, the highest-priority `RequestWarning(clip, priority)` / `RequestWeapon(...)` wins (ALTITUDE=4 > RWR=3 > LOCK=2 > SEARCH=1).

**3-tick state machine per sound change** (`idle → stop → select → play`, with `FRAME_DELAY=3` between each). Driven by SE's limitation of 1 sound API action per sim tick AND the double-Main quirk: if two block operations land in the same sim tick SE can discard one. Confirmed working — do not collapse the delay. Full trace: `docs/sound-pipeline-debug.md`.

### CustomDataManager (static)

Dictionary cache over the PB's `CustomData` string (key:value lines). Re-parses only when the raw string changes or `MarkDirty()` is called. All reads/writes go through `SystemManager.GetCustomDataValue` / `SetCustomDataValue` / `TryGetCustomDataValue`. If you bypass these (writing directly to `Me.CustomData`), call `MarkCustomDataDirty()`.

### RadarControlModule + RadarTrackingModule

Centralized radar + RWR using `IMyOffensiveCombatBlock` AI blocks. States: `IDLE → SEARCHING → LOCKED` (plus a dedicated `RWR` state for pure-RWR radars). Only 1 pool radar SEARCHING at a time; on lock it promotes and activates the next IDLE as SEARCHING. LOCKED radars stream position; demoted to IDLE after `LOST_TARGET_TIMEOUT_SECONDS = 2.0` without tracking.

**Key SE pitfalls** (verified from decompiled DLLs in `decompiled_dlls/`):
- `ActivateBehavior_On` is NOT a toggle — it always sets `IsActivated=true` (the toggle action is `ActivateBehavior` without suffix)
- `UpdateTargetInterval` is clamped to `[5, 60]` — setting 4 silently becomes 5
- `IsTracking` can be stale/false-positive; use `FoundEnemyId != null` cautiously
- `TrackedObjectName` only reparses `DetailedInfo` when `FoundEnemyId` changes (hot-path optimization in `RadarTrackingModule`)
- `ApplyAction` is unreliable during the `Program()` constructor — defer to runtime `Tick()`
- Only activate the COMBAT block's behavior (radar-only mode). Do NOT activate the flight block — its autopilot would fight the pilot. Rdav's reference missile script DOES activate both because the missile flies itself; our use case is detection-only.

See `memory/se-ai-block-internals.md` for the full decompilation notes.

### GunControlModule — Turret Aiming

Rotor+hinge assemblies (`Gun Rotor Left/Right`, `Gun Hinge Left/Right`) with mirror-mounted configs:
- **Yaw sign**: `SignedAngleBetween(flatGun, flatTarget, rotorUp)`, applied as `-KP * yawDeg`. SE positive RPM = counterclockwise from above (opposite of right-hand rule), so the cross-product-based sign is negated.
- **Pitch sign**: `ElevationSign = Sign(Dot(Cross(rotorUp, gunFwd), hinge.Up))` — handles left vs right hinge mounting without per-frame recalc.
- **Cone check**: uses `cockpit.WorldMatrix.Forward` (ship-fixed), NOT the gun's own forward (that would create a feedback loop).
- **`DetermineMotorSigns()`** runs once in ctor — mounting geometry is static.
- **Control law**: P (heading error) + ship-rotation feedforward (rad/s → RPM via `60/(2π)`, using wall-clock `dt`) + target-LOS-rate D-term (captures lateral target motion pure-P misses).
- **Spawn-delay compensation**: before intercept calc, target pos is advanced by `(V_target − V_ship) * DeltaSeconds` to bridge the one-tick gap between computing lead and bullet spawn. HUD lead pip applies the same shift — guns and pip stay aligned.
- **Muzzle velocity**: read from config `gun_muzzle_velocity` in both gun and HUD — single source of truth.
- Barrel direction is always `Gun.WorldMatrix.Forward` regardless of physical hinge mounting.

### TerrainData (heightmap subsystem)

`Utilities/TerrainData.cs` (the active implementation — `TerrainAPI.cs` is excluded). Downloads entire planet heightmap over the lifetime of a session by calling a `TerrainAPI` property on the PB itself (a mod-provided `ITerminalProperty<string>`). Protocol: `P;cellSize` → grid dimensions, then `C;offset;count` → height chunks (batch-sized to stay under the 50K-instruction tick budget).

`TerrainData.Probe(Me)` sets `_off = true` if the property is missing (no mod installed → terrain features disable silently). `TerrainData.Init(Me)` starts the download. `TerrainData.Tick(Me, pos)` drives chunk streaming and recomputes tangent vectors (`GridFwd`/`GridRight`) each tick once `Ready`.

### UI Theme — NYINAH CORP MFD

All three LCD surfaces use a unified dark-green-phosphor corporate MFD theme. Static `MFDTheme` class (`UI/UIController.cs`) holds the palette and sprite/alignment constants (`TX`/`TT`/`AC`/`AL`/`AR`). `MFDFrame.DrawChrome()` renders the shared frame: "NYINAH CORP" gold header + "TACTICAL SYSTEM" green subtitle, 1px gold accent line, corner brackets, footer with nav hints, 2px screen border, 15px top offset to avoid viewing-angle clipping.

Each surface is composed by an `MfdPage` (see `UI/MfdPage.cs`) — modules return their own page from `ProgramModule.GetPage()` to take over the main surface, otherwise the default `MenuMfdPage` wraps `GetOptions()`.

- **Surface 0** (driven by `UIController.Render` + the current module's `MfdPage`): two-column menu — items left, status sidebar right (propulsion schematic, H2 fuel, battery via `StatusPanelRenderer`). Custom pages (`TerrainModule`'s map) opt out of the menu and call `RenderContent` instead.
- **Surface 1** (`GridMfdPage` → `GridVisualization.Render`): grid outline, vertical fuel bar, flight data, G-meter.
- **Surface 2** (`WeaponMfdPage` → `HUDModule.RenderWeaponContent`): selected target detail, enemy contact list with tracking timelines, missile TOF.

Palette: BG `(5,8,5)`, normal text `(90,154,90)`, accent `(64,160,64)`, corp gold `(138,122,80)`.

### Sprite emission — SpriteBus + JetOS sprite mod

Every `MFDFrame.Rect` / `SpriteHelpers.*` / `Sq` / `SqT` / `Tx` call routes through `SpriteBus.Add` (`UI/SpriteBus.cs`) instead of `frame.Add` directly. The bus optionally tees each sprite into a capture list — that capture is what powers the page-transition replay (`UIController.ReplayWithTransform`, fades over `PAGE_FADE_DURATION = 0.30s`, cut at 85% progress because the tail is invisible). Direct `frame.Add` is reserved for renderers that don't participate in transitions (HUD glass via `HorizonRenderer`, cached grid outline in `GridVisualization`).

**Pre-baked sprite mod** (`Mod/testmod`, all sprite names `JetOS_*`): one textured sprite replaces what used to be N filled rects/lines per frame — bank arc, boresight, target bracket, lock cone, range ring, MFD corner, master caution/warning chiclets, etc. Sprite name constants live in `Utilities/Shortcuts.cs` as `TEX_*`. When adding new chrome, look first for an existing constant before composing from primitives.

**Outline helpers** (`SpriteHelpers.DrawCircleOutline` / `DrawRectangleOutline`) collapse to a single hollow vanilla sprite (`CircleHollow` / `SquareHollow`) when the geometry is square-ish; stretched rects fall back to four 1px filled rects so the border stays uniform.

**Blink cadence** is wall-clock via `Anim.Blink(period)` — never `(tickCounter / N) % 2`. Tick-based blinks drift under sim hitches.

### Throttle System — MIL/AB Gate

Three thrust stages in `HUDModule.UpdateThrottleControl()`:
- **NORMAL** (0–80%): atmospheric thrusters only
- **MIL** (80%): clamped — full atmospheric, no hydrogen, green HUD bar
- **AFTERBURNER** (80–100%): hydrogen enabled, yellow HUD bar

**AB Gate**: to break MIL, either (a) release W at MIL to arm the gate then press again (AB engages immediately), or (b) hold W for `AB_AUTO_ENGAGE_TICKS=40` (~0.67s) continuously. Throttle is clamped at 0.80 until `abGatePassed`. Dropping below MIL resets the gate.

### Target Data Flow

1. **Acquire**: `RadarControlModule` (AI block) detects targets, calls `Jet.UpdateOrAddEnemy`
2. **Enemy list**: `Jet.enemyList` stores pos/vel/accel/name/entity id/source index + 30s tracking history
3. **Select**: `Jet.selectedEnemyEntityId` + `selectedEnemyName`; `FlipGPS()` cycles through `GetEnemiesSortedByDistance()`
4. **Sync**: `SystemManager.UpdateActiveTargetGPS()` writes `Cached:GPS:...` and `CachedSpeed:vx:vy:vz:#FF75C9F1:` to PB CustomData
5. **Consume**: HUD reads for lead pip; AirtoAir reads for missile programming; gun turrets independently track the closest enemy from `enemyList`
6. **Bay fire**: `MissileBayHelper` (`Utilities/MissileBayHelper.cs`) copies GPS into bay-specific CustomData before the merge fires so missile scripts can read it

### AirtoAir module consolidation

`AirtoAir` uses `myJet.radarControl` exclusively — no separate `RadarTrackingModule`. Lock detection is `radarControl.IsTrackLocked`. The "Seeker" toggle controls only the weapon-channel tones, not radar block enablement. Missile firing is delegated to `MissileBayHelper` (`FireSelectedBays`, `ToggleSelectedBays`, `ToggleBaySelection`, `TransferCacheToSlots`, `BuildBayOptionList`).

### Input System

Toolbar arguments (numpad 1–9 mapped on the PB):
- **1 / 2** — navigate up/down (modules can consume via `HandleNavigation`)
- **3** — select/execute
- **4** — back (modules can consume via `HandleBack`; otherwise exits module)
- **5** — no-op at system level (modules may override via `HandleSpecialFunction`)
- **6 / 7** — global AoA trim (`Jet.offset` −1 / +1)
- **8** — cycle target slots (`FlipGPS`)
- **9** — return to main menu

Any numeric argument also forwards to `currentModule.HandleSpecialFunction(int.Parse(arg))`.

### Performance Constraints

SE enforces ~50,000 instructions per tick. Optimizations in place:
- CustomData dictionary cache — re-parses only on raw-string change or explicit dirty
- Thrust override only set when value differs (>0.001 tolerance)
- Enemy decay check throttled to 1s wall-clock
- Thrust-max sums refreshed every 0.5s (atmospheric density changes slowly)
- Gun ammo inventory iteration throttled to 0.5s (display-only)
- `RadarTrackingModule.TrackedObjectName` reparses `DetailedInfo` only when `FoundEnemyId` changes
- `GetClosestNEnemies` reuses pre-allocated sort/result buffers
- TerrainData chunk download batched to stay under the instruction budget

### Block Naming Conventions

**Required** (script will not initialize fully without):
- `"Jet Pilot Seat"` — `IMyCockpit`
- `"JetOS [HFPS]"` — `IMyTextSurfaceProvider` (needs ≥3 surfaces for main UI, extra info, weapons)
- `"Fighter HUD [HFPS]"` — `IMyTextSurface` for flight instruments

**Optional**:
- `"AI Flight"` / `"AI Combat"` — primary radar pair (`AI Flight N` / `AI Combat N`, 2–99, auto-detected)
- `"Bay N"` — missile merge blocks (sorted numerically)
- `"Gun Rotor Left/Right"` + `"Gun Hinge Left/Right"` — gun turrets
- `"Canard L [Ani]"` / `"Canard R [Ani]"` — canard AoA damping surfaces
- `"Sound Block Warning"` — altitude/speed warning channel
- `"Canopy Side Plate Sound Block"` — weapon lock/search tones
- `"invertedstab"` / `"normalstab"` — right/left stabilizer groups
- Thrusters containing `"Industrial"` are excluded; gas tanks containing `"Jet"` are treated as hydrogen tanks

## Debugging

```csharp
ParentProgram.Echo($"Debug: {value}");
ParentProgram.Echo($"Instructions: {ParentProgram.Runtime.CurrentInstructionCount}");
```

`Jet.IC` / `Jet.IP` / `Jet.IA` track current / peak / EMA-average instruction count.

## Known SE Gotchas

1. **Double-Main call** on toolbar trigger — handled via `_pendingArgument` guard (see Tick Loop above).
2. **Sound 3-tick state machine** required — SE allows only 1 sound API action per tick AND batches block ops at end-of-tick.
3. **Direct CustomData writes** must call `MarkCustomDataDirty()`.
4. **SE motor RPM** — positive = counterclockwise from above (opposite of right-hand rule). Cross-product-based angle signs need negation for rotor control.
5. **`MatrixD.Left` setter** — stores `-Left` internally as `Right`; affects `TransformNormal` results vs Whiplash reference code.
6. **`MyDetectedEntityInfo.Velocity`** is `Vector3` (not `Vector3D`) — implicit widening is safe.
7. **`"Trim"` terminal property** is mod-added, not vanilla.
8. **Pausing SE** freezes wall-clock aging (`TimeSinceLastRun` doesn't advance).

## SE API Reference

Full verified API reference in `docs/se-api-reference.md` — 22 SE types, 161 member accesses, 5 `ApplyAction` strings, 3 `SetValue` properties, all cross-referenced against decompiled vanilla DLLs (audit last run 2026-03-08).

Re-audit tooling:
- `collect_se_api.py` — scans all `.cs`, outputs `se_api_usage.json` + `se_api_usage_report.txt`
- `verify_se_api.py` — cross-refs against `decompiled_dlls/`, outputs `se_api_verification.txt`
- `decompiled_dlls/` — full ilspycmd decompilations of Sandbox.Common, SpaceEngineers.Game, VRage.Game, VRage.Math

Supplementary docs: `docs/architecture.md`, `docs/target-tracking.md`, `docs/hud-rendering.md`, `docs/weapons.md`, `docs/sound-system.md`, `docs/sound-pipeline-debug.md`, `docs/se-scripting-oddities.md`.
