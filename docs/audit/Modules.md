# Audit — `Mdk.PbScript2/Modules/`

Scope: every `.cs` file in `Mdk.PbScript2/Modules/` plus the build-excluded `TerrainMapModule.cs`. README.md skipped per instructions. Read-only research; no code modified.

The git status in the prompt mentioned an untracked `Modules/SpriteShowModule.cs`, but the file does not exist on disk (`Glob` and `ls` both confirm). Treating it as already-removed.

## 1. Types defined in this folder

### `ProgramModule.cs`
- `ProgramModule` (abstract base) — common contract for every menu-driven module: `GetOptions/ExecuteOption`, optional `Tick`, `HandleSpecialFunction`, `GetHotkeys`, `HandleNavigation`, `HandleBack`, plus `GetPage()` that returns a `MenuMfdPage`. **Holds `ParentProgram` and string `name` (default `"program"`).** All other modules extend this class.

### `AirtoAir.cs`
- `AirtoAir : ProgramModule` — weapons MFD page; manages bay selection, topdown flag, target sync, and missile-bay broadcast. Auto-selects the closest enemy when none is selected.

### `CanardModule.cs`
- `CanardModule : ProgramModule` — finds `Canard L/R [Ani]` blocks, runs an AoA-zeroing PID with sideslip coupling and spillover into the stabilizers.

### `ConfigurationModule.cs`
- `ConfigurationModule : ProgramModule` — three-level menu (Category → Parameter → Adjust) over a dictionary of named runtime knobs. Parses `Config:key:value` lines from `Me.CustomData` directly.
- `ConfigurationModule.ConfigParam` (private nested) — single configurable value with min/max/step/unit, `Adjust(int)`, `Reset()`, `IsModified`/`IsToggle` helpers.

### `GunControlModule.cs`
- `GunControlModule : ProgramModule` — auto-tracks the closest enemy in front of the ship with two rotor+hinge gatling turrets. Implements P+FF+D control law and lock detection.
- `GunControlModule.TurretAssembly` (private nested) — bundles rotor/hinge/gun, mounting sign, last-frame state for the FF/D terms.

### `HUDModule.cs` (partial — also extends `HUD/*.cs`)
- `HUDModule : ProgramModule` — orchestrator for the `Fighter HUD [HFPS]` text surface. Computes flight data each tick, drives throttle / AB-gate / hydrogen tanks / airbrakes / manual-fire toggle, smooths instrument values, dispatches sprite drawing into the partial `HUD/*Renderer` files.
- `HUDModule.MissileTrackingData` (internal struct) — bay index + launch time + estimated TOF + target pos. Consumed by `WeaponScreenRenderer`.
- `HUDModule.LabelValue` (internal struct) — generic `(label, double)` for HUD info boxes.
- `HUDModule.AltitudeTimePoint` (internal struct) — `(TimeSpan, double)` history sample for VVI/altitude charts.

### `RadarControlModule.cs`
- `RadarControlModule : ProgramModule` (public) — central radar/RWR controller. Auto-detects `AI Flight N` / `AI Combat N` pairs (1–99), runs a sequential `IDLE → SEARCHING → LOCKED` chain with a separate `RWR` group, exposes `IsTrackLocked` and `activeThreats`.
- `RadarControlModule.AIBlockPair` (private struct), `.RadarState` (private class), `.RadarRole` (private enum), `.RWRTrackingState` (private class) — internal bookkeeping only.

### `TerrainModule.cs`
- `TerrainModule : ProgramModule` — terrain map MFD page. Renders heading-up marching-squares contour map from `TerrainData`, plus a public static `RenderMinimap` for sidebars.
- `TerrainModule.TerrainMfdPage` (private nested) — wraps `DrawMap` in an `MfdPage` so `GetPage()` can return it.

### `TerrainMapModule.cs` (BUILD-EXCLUDED)
- `TerrainMapModule : ProgramModule` — older terrain page using the deprecated `TerrainAPI` static class. Excluded via `<Compile Remove="Modules\TerrainMapModule.cs" />` (csproj line 38). See section 4.

## 2. Inputs (what this folder reads/calls from elsewhere)

### From root (`Program.cs`, `Jet.cs`, `SystemManager.cs`)

`Program` (passed via ctor):
- `program.GridTerminalSystem` — block lookups in `CanardModule.cs:32,37,38, 107`, `GunControlModule.cs:75-83,98`, `HUDModule.cs:234`, `RadarControlModule.cs:133-134`.
- `ParentProgram.Me.CustomData` (read/write) — `ConfigurationModule.cs:140,167,186`.
- `ParentProgram.Echo` — `RadarControlModule.cs:162,883`.
- `ParentProgram.Runtime.TimeSinceLastRun` — `HUDModule.cs:455,485`, `RadarControlModule.cs:271`.

`Jet` (constructor arg or via ParentProgram.Me):
- `jet._cockpit` — read across `CanardModule.cs:128`, `GunControlModule.cs:69,229,253,396,430,456` (etc.), `HUDModule.cs:196`, `RadarControlModule.cs:406-407`, `TerrainModule.cs:78,121,264-269`.
- `jet._bays` — `AirtoAir.cs:21`.
- `jet._gatlings` — `HUDModule.cs:598-602`.
- `jet._thrustersbackwards`, `tanks`, `leftEngines/rightEngines/centerEngines/leftAB/rightAB/centerAB`, `batteries`, `leftstab`/`rightstab`, `hudBlock`, `manualfire`, `offset`, `enemyList`, `CachedGravity` — all consumed by `HUDModule`/`CanardModule`/`GunControlModule`/`RadarControlModule`/`TerrainModule` (HUDModule.cs:201-208, 596-647, 712-718, 391-413; GunControlModule.cs:258, 399; etc.).
- `jet.GetSelectedEnemy()`, `HasSelectedEnemy()`, `SelectEnemy()`, `GetClosestNEnemies()`, `UpdateEnemyDecay()`, `UpdateOrAddEnemy()`, `GetFuelStatus()`, `GetBatteryStatus()`, `GetEnemyContactColor()`, `GetAltitude()` — `AirtoAir.cs:85-88`, `HUDModule.cs:328,395-406,737`, `RadarControlModule.cs:380,423,451,498-519`, `TerrainMapModule.cs:129` (excluded).
- `Jet.GetGunAmmo(...)` static — `GunControlModule.cs:486`.

`SystemManager` (entirely static):
- `GetCustomDataValue`/`SetCustomDataValue`/`MarkCustomDataDirty` — `AirtoAir.cs:23-25,74`, `RadarControlModule.cs:148-153,250,259`, `ConfigurationModule.cs:187`.
- `GetConfigValue` — `HUDModule.cs:26,304,311,314,317,324,342,364,367,371`, `GunControlModule.cs:54-58`.
- `DeltaSeconds` — `CanardModule.cs` (none; uses HUD-driven smoothed value), `GunControlModule.cs:304,437,472`, `RadarControlModule.cs:333,476,726`, `HUDModule.cs:485` (computes its own `deltaTime` from `Runtime.TimeSinceLastRun` rather than reusing `SystemManager.DeltaSeconds`).
- `GetSmoothedAoA` — `CanardModule.cs:157`.
- `currentMenuIndex` (set) — `ConfigurationModule.cs:312,322,335,342,379,385`.
- `ReturnToMainMenu()` — `CanardModule.cs:108`, `HUDModule.cs:243`, `TerrainModule.cs:35`, `TerrainMapModule.cs:32` (excluded).
- `AltitudeWarningActive` — `HUDModule.cs:410`.
- `UpdateActiveTargetGPS()` — `AirtoAir.cs:90`.

### From `Utilities/`
- `MissileBayHelper` — `AirtoAir.cs:54,66,70,78,92,97,103`.
- `BallisticsCalculator.CalculateInterceptPoint` — `GunControlModule.cs:445`, `HUDModule.cs:348`.
- `RadarTrackingModule` (`Utilities/RadarTrackingModule.cs`) — instantiated and driven by `RadarControlModule.cs:139,281,562-575`. Reads `L_FlightBlock`, `L_CombatBLock`, `IsTracking`, `HasReceivedPosition`, `TargetPosition`, `TargetVelocity`, `TrackedEntityId`, `TrackedObjectName`, `UpdateTracking`.
- `SoundManager.RequestWarning` (+ `PRIORITY_RWR`) — `RadarControlModule.cs:833`.
- `NavigationHelper.CalculateHeading`, `GetAspectAngleDeg` — `HUDModule.cs:732`, `RadarControlModule.cs:805,823`.
- `CustomDataManager` is reached only indirectly via `SystemManager` wrappers.
- `SpriteHelpers.Sp/DrawRectangleOutline/DrawCircleOutline/ProjectToScreen` — `HUDModule.cs:358,427,432`, `TerrainModule.cs:88,90-91,109,132-133`, `TerrainMapModule.cs:109-114,130-132` (excluded).
- `Anim.Blink` — `HUDModule.cs:423,429`.
- `RWRWarning` (`Utilities/CommonTypes.cs`) — `RadarControlModule.cs:96,770,775,856`.
- `Shortcuts.cs` aliases (`VN`, `VD`, `VX`, `Cl`, `Cr`, `Mn`, `Mx`, `Ab`, `At2`, `As`, `Sg`, `LV`, `GP`, `WM`, `WF`, `WR`, `WU`, `SS`, `SX`, `SY`, `V2`, `VTN`, `VDi`, `VZ`, `PI`, `ToDeg`, `ToRad`, `Sn`, `Cs`, `TRIM`, `TEXTURE_TRIANGLE`, `TEX_MASTER_WARNING`, `TEX_MASTER_CAUTION`) — used pervasively. Consistent with CLAUDE.md style.

### From `UI/`
- `MfdPage`, `MenuMfdPage` — `ProgramModule.cs:20`, `TerrainModule.cs:38,101`.
- `MFDFrame.Txt/Rect/DrawChrome/ContentBottom`, `MFDTheme.*` — `TerrainModule.cs:51-97,108-138`, `TerrainMapModule.cs:51-159` (excluded).
- `RectangleF` (VRageMath) used through `RenderContent` virtuals.

### From `HUD/`
- HUD partials extend `HUDModule` — those files are organisationally siblings of `Modules/HUDModule.cs`. The base file declares the fields/structs (`activeMissiles`, `MissileTrackingData`, `viewportMinDim`, `verticalVelocityMps`, `peakGForce`, theme colour properties) consumed by `HUD/InstrumentRenderer.cs`, `HUD/TargetingRenderer.cs`, `HUD/RadarRenderer.cs`, `HUD/WeaponScreenRenderer.cs`, `HUD/HorizonRenderer.cs`. No direct call from this folder INTO `HUD/`; the relationship is through the `partial` keyword.

### SE API (notable)
- `IMyMotorStator.TargetVelocityRPM` — `GunControlModule.cs:235-238,287-288,376-379`. (Per CLAUDE.md: positive RPM = counterclockwise from above; the cross-product yaw sign in `DriveTowardDirection` accounts for this with `-KP * yawDeg`.)
- `IMyTerminalBlock.GetValueFloat("Trim")` / `SetValue<float>("Trim", ...)` — `CanardModule.cs:44,54-55,207,209,213,216,223,225`, `HUDModule.cs:724,726`. **CLAUDE.md flags `"Trim"` as a mod-added property — confirmed used.**
- `IMyOffensiveCombatBlock.ApplyAction("ActivateBehavior_On" / "SetTargetingGroup_Weapons" / "SetTargetPriority_*")` — `RadarControlModule.cs:573,574,581`. Matches the documented gotcha; `_On` is a one-way set, not toggle. `UpdateTargetInterval = 5` (line 568) — at the documented `[5,60]` clamp boundary.
- `SetValue<long>("OffensiveCombatIntercept_GuidanceType", 0)`, `SetValueBool("OffensiveCombatIntercept_OverrideCollisionAvoidance", true)` — `RadarControlModule.cs:571-572`.
- `IMyShipController.MoveIndicator`, `GetShipSpeed`, `GetShipVelocities` (via `LV` shortcut), `TryGetPlanetElevation`, `GetNaturalGravity` — `HUDModule.cs:256-257,475,485,498-502`; CanardModule via `LV(cockpit)`.
- `IMyThrust.ThrustOverridePercentage`, `MaxEffectiveThrust`, `IsFunctional` — `HUDModule.cs:614-655`.
- `IMyDoor.OpenDoor/CloseDoor` (airbrakes) — `HUDModule.cs:573-579`.
- `IMyTextSurface.DrawFrame`, `ContentType.SCRIPT`, `ScriptBackgroundColor`, `SurfaceSize` — `HUDModule.cs:230-231, 290`.
- `Runtime.TimeSinceLastRun.TotalSeconds` — `HUDModule.cs:455,485`. (See odd-code §5: HUDModule maintains its own `deltaTime` instead of using `SystemManager.DeltaSeconds`.)

## 3. Outputs (what this folder exposes to callers)

### `ProgramModule` (base contract)
- `name`, `GetOptions()`, `ExecuteOption(int)`, `Tick()`, `GetPage()`, `HandleSpecialFunction(int)`, `GetHotkeys()`, `HandleNavigation(bool)`, `HandleBack()` — all called from `SystemManager.Main / DisplayMenu / NavigateUp / NavigateDown / DeselectOrGoBack / ExecuteCurrentOption / HandleSpecialFunctionInputs`.

### `AirtoAir` — referenced only by `SystemManager.airtoAirModule` (private static field, ticked in background). No public members beyond the `ProgramModule` overrides. **Internal `BayOffset = 3` const is fine.**

### `CanardModule`
- `internal static bool OwnsStabs { get; private set; }` (line 124) — read by `HUDModule.AdjustStabilizers` (HUDModule.cs:712). Working contract; documented in `docs/canard-system.md`.

### `ConfigurationModule`
- `public float GetValue(string configName)` (line 190) — wrapped by `SystemManager.GetConfigValue` (SystemManager.cs:175). Sole external entry point.

### `GunControlModule`
- `public bool IsControlEnabled` (line 490) — read by `WeaponScreenRenderer.cs:384`.
- `public bool IsLeftTracking` (line 491) — read by `WeaponScreenRenderer.cs:398, 403, 412`.
- `public bool IsRightTracking` (line 492) — read by `WeaponScreenRenderer.cs:401, 403, 412`.

### `HUDModule`
Partial class spread over `Modules/HUDModule.cs` + `HUD/*.cs`. Internals consumed cross-folder:
- `internal RadarControlModule radarControl` (line 60) — used by HUD partials.
- `internal IMyCockpit cockpit`, `IMyTextSurface hud`, `weaponScreen`, `hudBlock`, `myjet`, `tanks`, `leftstab`, `rightstab` (lines 50–60) — used by HUD partials.
- `internal double smoothedVelocity / smoothedAltitude / smoothedGForces / smoothedAoA / throttlePercent / verticalVelocityMps / peakGForce / mach / pitch / roll / velocity / deltaTime` — read by `UI/GridVisualization.cs:235-304` and HUD renderers.
- `internal CircularBuffer<AltitudeTimePoint> altitudeHistory` (line 74) — declared `internal`; **no callers found outside this module**. **UNUSED externally.**
- `internal Vector3D previousVelocity` (line 70) — written internally; read internally; `internal` accessor not consulted externally.
- `internal float currentTrim` (line 64) — written by `AdjustTrim` but value is per-iteration scratch; no external readers found. **Should be a local variable, not a field.**
- `internal TimeSpan totalElapsedTime` (line 118) — read by `WeaponScreenRenderer.cs:343,348` (missile age math).
- `internal Vector2 hudCenter`, `float viewportMinDim` (lines 121-122) — set inside `RenderHUD`, read by HUD partials.
- `internal struct MissileTrackingData` (line 125) and `internal List<MissileTrackingData> activeMissiles` (line 132) — used by HUD partials (WeaponScreenRenderer).
- `internal struct LabelValue`, `internal struct AltitudeTimePoint` — `LabelValue` is used in `DrawLeftInfoBox` call (line 308). `AltitudeTimePoint` only consumed inside the buffer.
- `internal const float COCKPIT_FOV_SCALE_Y = 0.31f` (line 163) — used by `Utilities/SpriteHelpers.cs:101`.
- `internal const float THROTTLE_HYDROGEN_THRESHOLD = 0.8f` (line 40) — local-only use; **no external callers**. **UNUSED externally** (declared `internal` but never read by anyone else).
- `internal const int INTERCEPT_ITERATIONS = 10` (line 137) — local-only. **No external callers.**
- `internal const double MIN_Z_FOR_PROJECTION = 0.1` (line 138) — **no callers anywhere** (grep returns 0 hits). **DEAD.**
- `internal const float RADAR_BOX_SIZE_PX = 100f`, `internal const float RADAR_BORDER_MARGIN = 10f` (lines 139-140) — used by `HUD/RadarRenderer.cs` (consumed via grep — confirmed used).
- `internal const double STALL_AOA = 28.0` and friends (`STALL_CAUTION_PERCENT`, `STALL_WARNING_PERCENT`, `STALL_LEVEL_*`) (lines 143-149) — used by HUD InstrumentRenderer for stall meter; confirmed via grep on `STALL_AOA` in InstrumentRenderer.
- `internal const float TAPE_HEIGHT_PIXELS / ALTITUDE_UNITS_PER_TAPE_HEIGHT / PIXELS_PER_ALTITUDE_UNIT / TICK_INTERVAL / MAJOR_TICK_INTERVAL / SPEED_*` (lines 152-160), `internal const string FONT` (line 157) — used in InstrumentRenderer (HUD partial).
- `internal static void CacheTheme()` (line 24) — called from `Tick()` only. No external callers.
- `internal static Color HUD_PRIMARY/HUD_SECONDARY/HUD_HORIZON/HUD_RADAR_FRIENDLY/HUD_EMPHASIS/HUD_WARNING/HUD_INFO` (lines 30-36) — used by HUD partials.

### `RadarControlModule`
- `public bool IsTrackLocked { get; private set; }` (line 99) — read by `HUD/WeaponScreenRenderer.cs:28,147`, `HUD/RadarRenderer.cs:139`, `HUD/TargetingRenderer.cs:226`. Stored on `Jet.radarControl` for HUD partials; in `Modules/HUDModule.cs:60` directly held as `radarControl` field.
- `public List<RWRWarning> activeThreats` (line 96) — read internally by `UpdateConsoleOutput` (line 856) and `GetOptions` (line 185). **No external readers** — exposed publicly but only consumed in-class. **EFFECTIVELY UNUSED externally** (could be `private`).

### `TerrainModule`
- `public static void RenderMinimap(MySpriteDrawFrame, RectangleF, Jet)` (line 116) — searched repo-wide; **no callers found** (grep produced no hits in `UI/` or anywhere else). **UNUSED.**

### `TerrainMapModule` — entire file is excluded. No public surface reachable.

## 4. Dead code findings

### Files in `<Compile Remove>`
- **`Modules/TerrainMapModule.cs`** — older terrain page using `TerrainAPI.IsAvailable / IsReady / IsLoading / WorldToGrid / ShipAlt / AGL / CellSize` plus `TerrainRenderer.JetAxes/DrawContours`. Both `TerrainAPI` and `UI/TerrainRenderer.cs` are themselves excluded from the build. Functionality is superseded by the active `TerrainModule.cs`. **Action item:** delete `TerrainMapModule.cs` together with `Utilities/TerrainAPI.cs` and `UI/TerrainRenderer.cs` once nobody needs the reference. Also note: it uses `HasCustomScreen` and `RenderCustomScreen` overrides that no longer exist on the current `ProgramModule` base class — confirming the file would not even compile if un-excluded.

### Public/internal members with no readers
- `HUDModule.altitudeHistory` (HUDModule.cs:74) — written each tick via `UpdateSmoothedValues`, never read. The smoothed value `smoothedAltitude` is what callers consume. **Buffer can be removed entirely.**
- `HUDModule.currentTrim` (HUDModule.cs:64) — used as a scratch variable inside `AdjustTrim`. Should be a local; the field-level scope serves no purpose and increases minified size.
- `HUDModule.MIN_Z_FOR_PROJECTION` (HUDModule.cs:138) — zero hits across repo. **Dead constant.**
- `HUDModule.THROTTLE_HYDROGEN_THRESHOLD` declared `internal` (HUDModule.cs:40) — only referenced inside the same partial class; can be `private`.
- `HUDModule.INTERCEPT_ITERATIONS` declared `internal` (HUDModule.cs:137) — same — only used in `RenderHUD`, can be `private`.
- `RadarControlModule.activeThreats` (RadarControlModule.cs:96) — `public` but only read inside the class. Either consumers should be added (e.g. surface threat list on HUD via this property) or it should become `private` to prevent accidental leakage.
- `TerrainModule.RenderMinimap` (TerrainModule.cs:116) — public static helper with no callers. (`StatusPanelRenderer.cs` does NOT call it; sidebars use `StatusPanelRenderer.Render` instead.) **Dead method.**

### Private methods/fields never used
- `HUDModule.previousVelocity` is read each `Tick`, so live. No purely-dead privates found in active modules other than the items above.

### Commented-out blocks > 3 lines
- None — comments in this folder are doc comments.

### TODOs / diagnostic-only code / leftovers
- `RadarControlModule.UpdateConsoleOutput` (line 837) writes to `ParentProgram.Echo` every tick when the string changes. Useful for debugging, but it runs at `Update1`. Could be gated behind a debug flag.
- `RadarControlModule.cs:101 lastConsoleOutput` exists only to dedupe the `Echo` above — same observation.

## 5. Odd code findings

1. **`HUDModule.deltaTime` re-derives wall-clock instead of reusing `SystemManager.DeltaSeconds`** (HUDModule.cs:485-488). CLAUDE.md states `DeltaSeconds` is the canonical lag-resistant time delta; HUDModule pulls `Runtime.TimeSinceLastRun.TotalSeconds` directly and falls back to `0.0167`. Same value is used to drive smoothing, throttle ramp, and AB-gate hold. Pre-existing inconsistency with `GunControlModule` / `RadarControlModule` / `CanardModule` which all read `SystemManager.DeltaSeconds`.

2. **`HUDModule.totalElapsedTime` is a separate clock** from `SystemManager.ElapsedSeconds` (line 118, advanced via `+= ParentProgram.Runtime.TimeSinceLastRun` at line 455). Two different time sources for the same concept. Missile TOF math in `WeaponScreenRenderer` keys off `totalElapsedTime`; everything else uses `SystemManager.ElapsedSeconds` / `Jet.GameSeconds`.

3. **`ConfigurationModule` bypasses `CustomDataManager`** (lines 138-188). It parses `Me.CustomData` directly via `string.Split('\n')` and rewrites the entire string. After writing, it calls `SystemManager.MarkCustomDataDirty()` (line 187), which forces the manager cache to re-parse. CLAUDE.md says all reads/writes should go through `SystemManager.GetCustomDataValue` / `SetCustomDataValue` — this module doesn't. Already documented in `optimisations/12-customdata-config-bypass.md` as a known issue.

4. **`HasCustomScreen` / `RenderCustomScreen` overrides in `TerrainMapModule.cs:28,42`** — not declared on the current `ProgramModule` base. The excluded file would not compile. Mentioned for documentation cleanup.

5. **Hardcoded constants that could come from `ConfigurationModule`**:
   - `HUDModule.AB_AUTO_ENGAGE_SECONDS = 0.667f` (line 98), `THROTTLE_RATE = 0.6f` (line 94), `HYDROGEN_HYSTERESIS = 0.02f` (line 95), `STALL_AOA = 28.0` (line 143), `SMOOTHING_WINDOW_SIZE = 10` (line 47).
   - `GunControlModule.MAX_ANGLE_DEG = 15f` (line 46), `INTERCEPT_ITERATIONS = 6` (line 48), `KD_LOS = 1.0f` (line 51) — pilots may want these tunable, especially `MAX_ANGLE_DEG`.
   - `CanardModule.gain = 1.5f` (line 19) and `coupling = 0.4f` (line 20) — exposed in the menu via `Gain+/-` but stored only as instance fields, lost on PB recompile. **Persistence gap** (no save to CustomData).
   - `RadarControlModule.LOST_TARGET_TIMEOUT_SECONDS = 2.0` (line 116), `RWR_*_SECONDS` constants — durations the pilot might want tunable.

6. **`HUDModule.activeMissiles` is fed externally?** Grep shows the field is read in `WeaponScreenRenderer.cs:82,92,249,338-348`. No code adds to it within `Modules/`. This is supposed to be populated by missile-fire flow. The repo shows `MissileBayHelper.FireSelectedBays` (the only fire path) does NOT push to `activeMissiles`. **Likely dead-end UI feature** — the missile TOF list will never display because the list is never appended to. Worth checking against `MissileBayHelper`.

7. **`AirtoAir` always overrides `AntiAir = true`** (AirtoAir.cs:25) on construction — pilot cannot turn it off. The `Topdown` toggle is the only persistent flag from this module.

8. **`AirtoAir.GetHotkeys()` returns `MissileBayHelper.WEAPON_HOTKEYS = "5: Fire Next Available Bay\n"`** — a single-line hint. `GunControlModule.GetHotkeys()` returns `"5: Toggle Auto-Track\n6: Center Turrets"`. Both modules consume key `5`; that's fine because only `currentModule` receives `HandleSpecialFunction`. Worth flagging that the contract is "active module only", not "any module reachable in background".

9. **`RadarControlModule.GetHotkeys()` returns `"Radar Control is a status display"`** (line 658) — that's not hotkeys text but a description. Inconsistent shape with the other `GetHotkeys()` returns.

10. **`HUDModule.GetOptions()` returns just `{"Back to Main Menu"}`** (line 242) — yet HUDModule still appears in the main menu via `name = "HUD Control"` (line 235). Selecting it lands you on a one-item menu that exits. The module is permanent (always background-ticked), so the menu page is essentially pointless. This is a UX odd, not a bug.

11. **`TerrainModule` static fields `_cl`, `_clMin`, `_clMax`** (lines 30-31) are SHARED between the full-screen `DrawMap` call and the static `RenderMinimap` call. Since `RenderMinimap` has zero callers right now (see §4) the sharing is moot, but if it were ever wired up the two callers would clobber each other's grid buffer in the same tick.

12. **`RadarControlModule.GetOptions()` allocates a fresh `List<string>` every tick** (line 167) — already flagged in `optimisations/09-getoptions-allocations.md`. Lives during the radar-page render loop.

13. **`RadarControlModule.detectedAIPairs` field** (line 17) is populated in the constructor (line 138) but never read again. It's parallel to `allRadars`. **Possibly dead** — the data is also encoded in each `RadarTrackingModule.L_FlightBlock/L_CombatBLock`. Only `Index` is unique to the pair struct.

14. **Naming inconsistency** — `AirtoAir` vs `RadarControlModule` / `GunControlModule` / `CanardModule` / `HUDModule` / `ConfigurationModule` / `TerrainModule`. Other module classes end with `Module`; `AirtoAir` does not. Casing also irregular (`AirtoAir` vs `AirToAir`).

15. **Throttle/AB control belongs in `HUDModule`** — flight control logic (thrust override, hydrogen tank toggling, airbrakes, manualfire) lives inside the HUD rendering module. Not strictly wrong, but the file does HUD + flight-controls + throttle-state-machine + missile tracking list.

16. **`HUDModule._lastTrimOffset` is a `static float`** (line 708) — static fields persist across `Initialize()` recalls. After a re-init the cached value stays, then `myjet.offset` likely starts at 0. This causes the early-return at line 712 to fire on the first post-reinit tick if old `_lastTrimOffset` happened to equal 0. Edge case, unlikely to actually misbehave, but worth noting.

## 6. Notes for the cross-folder consolidation

1. **Dead Terrain stack to delete together**: `Modules/TerrainMapModule.cs` + `Utilities/TerrainAPI.cs` + `UI/TerrainRenderer.cs`. All three are build-excluded and reference each other; only `TerrainModule.cs` is live and uses `Utilities/TerrainData.cs`. Also remove the stale `RenderMinimap` static in `TerrainModule.cs:116` (no callers).

2. **`HUDModule` is an oversized partial class** — 758 lines of hub plus the four `HUD/*Renderer.cs` partials. It owns flight controls, smoothing, throttle/AB gate, missile TOF tracking, master annunciators, AND the HUD glass orchestrator. Splitting flight-control responsibilities (throttle/airbrakes/tanks/manualfire/stabilizer trim) out of `HUDModule` would make the rest of the cross-folder picture cleaner. Several `internal` members exposed for HUD partials (`THROTTLE_HYDROGEN_THRESHOLD`, `INTERCEPT_ITERATIONS`, `currentTrim`, `altitudeHistory`, `MIN_Z_FOR_PROJECTION`) are either unused externally or scope-leaks.

3. **Time-source consistency is the highest-impact correctness item**: HUDModule should switch to `SystemManager.DeltaSeconds`/`ElapsedSeconds` instead of its own `deltaTime`/`totalElapsedTime`, removing two parallel clocks. Also fold `WeaponScreenRenderer`'s `totalElapsedTime` reads onto `Jet.GameSeconds` so missile TOF math stays consistent with everything else.
