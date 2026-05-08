# Audit: Root files + Extensions/

Scope: `Mdk.PbScript2/Program.cs`, `Mdk.PbScript2/SystemManager.cs`, `Mdk.PbScript2/Jet.cs`, `Mdk.PbScript2/Extensions/RandomExtensions.cs`.

This is the orchestration layer — entry point, tick loop, module registry, and the hardware-abstraction object that every other folder consumes.

---

## 1. Types defined here

| File | Type | Role |
|---|---|---|
| `Program.cs:6` | `partial class Program : MyGridProgram` | SE entry point. Constructor calls `SystemManager.Initialize(this)` and sets `Update1`; `Main` delegates to `SystemManager.Main` inside try/catch. |
| `SystemManager.cs:13` | `static class SystemManager` (nested in `Program`) | Tick orchestrator + module registry + LCD-surface owner + transition coordinator. All wall-clock timing originates here. |
| `Jet.cs:12` | `class Jet` (nested in `Program`) | Hardware abstraction. Block discovery in ctor, enemy-contact database, ship-state getters, gun ammo cache. |
| `Jet.cs:38` | `struct Jet.EnemyContact` | Per-target record (Position/Velocity/Acceleration/Name/EntityId/LastSeen/SourceIndex/TrackHistory/LastHistoryShift). |
| `Extensions/RandomExtensions.cs` | *(nothing)* | The file is `namespace IngameScript {}` — completely empty. |

---

## 2. Inputs (what root reads/calls from elsewhere)

### Modules constructed and registered (`SystemManager.Initialize`, `SystemManager.cs:62-135`)
Construction order is significant — earlier modules feed later ones:

| Order | Field | Type | Constructed at | Registered in `modules`? | Background-ticked? |
|---|---|---|---|---|---|
| 1 | `radarControlModule` | `RadarControlModule` | `:102` | yes (`:104`) | yes (`:258`) |
| 2 | `airtoAirModule` | `AirtoAir` | `:106` | yes (`:107`) | yes (`:263`) |
| 3 | `hudProgram` | `HUDModule` | `:109` | yes (`:110`) | yes (`:253`) |
| 4 | `configModule` | `ConfigurationModule` | `:113` | yes (`:114`) | no |
| 5 | `gunControlModule` | `GunControlModule` | `:116` | yes (`:117`) | yes (`:268`) |
| 6 | `terrainModule` | `TerrainModule` | `:119` | yes (`:120`) | no |
| 7 | `canardModule` | `CanardModule` | `:122` | yes (`:123`) | yes (`:273`) |

`_myJet.radarControl = radarControlModule` (`:103`) wires the radar reference into the hardware abstraction so HUDModule/AirtoAir can access it through Jet.

### Other subsystem touchpoints (`SystemManager.Initialize`)
- `CustomDataManager.Initialize(program.Me)` `:96`
- `SoundManager.Initialize(program.GridTerminalSystem)` `:97`
- `TerrainData.Probe(program.Me)` + `TerrainData.Init(program.Me)` `:98-99`
- `UIController(lcdMain, lcdExtra)` `:111`
- `GridMfdPage(parentProgram, _myJet, radarControlModule, hudProgram)` `:133`
- `WeaponMfdPage(hudProgram)` `:134`
- `MFDTheme.AC` `:84` (alignment constant)
- `Cr(...)` shortcut `:81, :88` (color)

### Per-tick consumers (`SystemManager.Main`)
- `parentProgram.Runtime.TimeSinceLastRun.TotalSeconds` `:199` — wall-clock delta
- `parentProgram.Runtime.CurrentInstructionCount` `:285` — IC tracking
- `_myJet._cockpit.GetNaturalGravity()` `:207`
- `_myJet.GetVelocity()`, `GetAltitude()`, `GetCockpitPosition()` `:209-214`
- `TerrainData.Tick(parentProgram.Me, ...)` `:214`
- `SoundManager.RequestWarning("Tief", PRIORITY_ALTITUDE)` `:227, :235`
- Per-module `Tick()` (`:250, :255, :260, :265, :270, :275`)
- `currentModule.HandleSpecialFunction(int)` `:297`
- `currentModule.GetPage()` `:323`, `GetOptions()` `:447`, `HandleNavigation` `:426, :439`, `HandleBack` `:472`, `ExecuteOption` `:464`
- `SoundManager.Tick(ElapsedSeconds)` `:284` (must run after module ticks — see CLAUDE.md note)
- `StatusPanelRenderer.Render(frame, area, _myJet, hudProgram, currentTick)` `:143, :319`
- `MenuMfdPage(...)` `:317`, `uiController.Render(...)` `:331-333`

### `Main` argument routing (`SystemManager.HandleInput`, `:336-369`)
Args 1/2/3/4/9 invoke private nav helpers; 5 is no-op; 6/7 modify `_myJet.offset`; 8 calls `FlipGPS()`. Every other numeric arg also forwards to `currentModule.HandleSpecialFunction(int)` via `HandleSpecialFunctionInputs` `:290`.

### SE API surface used in root
- `IMyGridTerminalSystem.GetBlockWithName`, `GetBlocksOfType` (`Jet.cs:120, 130, 135, 142, 148-149, 151, 183, 185, 189`)
- `IMyCockpit.GetShipSpeed/TryGetPlanetElevation/GetPosition/WorldMatrix/CubeGrid/Position` (`Jet.cs:468, 479, 486, 487, 132 etc.`)
- `IMyTerminalBlock.IsSameConstructAs(_cockpit)` (`Jet.cs:142, 148, 149`)
- `IMyThrust.GridThrustDirection / Position / BlockDefinition.SubtypeId / IsFunctional / CurrentThrust / MaxEffectiveThrust` (`Jet.cs:156, 165, 168, 502, 516`)
- `IMyTextSurfaceProvider.GetSurface/SurfaceCount` `SystemManager.cs:67-90`
- `IMyTextSurface.ContentType/BackgroundColor/Script/FontColor/FontSize/TextPadding/Alignment` `SystemManager.cs:70-89`
- `Runtime.UpdateFrequency`, `Runtime.TimeSinceLastRun`, `Runtime.CurrentInstructionCount`
- `IMyBatteryBlock.{CurrentStoredPower, MaxStoredPower, CurrentOutput, CurrentInput, IsFunctional}` `Jet.cs:528-531`
- `IMyGasTank.{Capacity, FilledRatio, BlockDefinition.SubtypeId}` `Jet.cs:541-543`
- `IMyShipMergeBlock` (only `CustomName` access for sort) `Jet.cs:142-145`
- `IMyInventory.ItemCount/GetItemAt` `Jet.cs:566-568`

NO uses of `IsMainCockpit`, `ApplyAction`, or `UpdateTargetInterval` in root files (those live in `RadarControlModule`).

---

## 3. Outputs (the public contract)

### `SystemManager` (consumed by every other folder)

| Member | Source | Used by |
|---|---|---|
| `static double DeltaSeconds` | `SystemManager.cs:57` | `Jet.cs:303, 588`, `GunControlModule.cs:304, 437, 472`, `RadarControlModule.cs:333, 476, 726` |
| `static double ElapsedSeconds` | `:58` | `UIController.cs:130, 156, 247`, `WeaponScreenRenderer.cs:29, 142`, `Anim.cs:41, 49, 58, 104, 111, 119` |
| `static int currentTick` | `:41` | `TerrainRenderer.cs:48` (and internally for `StatusPanelRenderer.Render`) |
| `static ProgramModule currentModule` | `:36` | `StatusPanelRenderer.cs:63` (`is TerrainModule` check) |
| `static int currentMenuIndex` | `:35` | `ConfigurationModule.cs:312, 322, 335, 342, 379, 385` (writes) |
| `static bool AltitudeWarningActive` | `:51` | `HUDModule.cs:410` |
| `static IMyTextSurface MainSurface` | `:138` | **UNUSED** — defined as `=> lcdMain` but no callers. Comment says "Exposed for pages that need access to the main MFD surface (e.g. for absolute coords)" but nothing references it. |
| `Initialize(Program)` | `:62` | `Program.cs:23, 39` |
| `Main(string, UpdateType)` | `:179` | `Program.cs:31` |
| `GetCustomDataValue/SetCustomDataValue/TryGetCustomDataValue` | `:147-160` | `MissileBayHelper.cs:66, 83, 104-113, 169-172`, `AirtoAir.cs:23, 25, 74`, `RadarControlModule.cs:148, 250, 259`, `RadarRenderer.cs:251` |
| `MarkCustomDataDirty()` | `:162` | `ConfigurationModule.cs:187` |
| `GetSmoothedAoA()` | `:167` | `CanardModule.cs:157` |
| `GetConfigValue(string)` | `:172` | `HUDModule.cs:26, 304-371`, `GunControlModule.cs:54-58`, `GridVisualization.cs:39-40` (incl. `gun_muzzle_velocity`, `hud_*` flags, etc.) |
| `RenderDefaultSidebar(frame, area)` | `:141` | `MenuMfdPage.cs:45` |
| `UpdateActiveTargetGPS()` | `:408` | `AirtoAir.cs:90`; also called internally `:405` |
| `ReturnToMainMenu()` | `:482` | `CanardModule.cs:108`, `TerrainModule.cs:35`, `HUDModule.cs:243`, `TerrainMapModule.cs:32` (note: TerrainMapModule is excluded from build) |
| `GetGunControl()` | `:488` | `WeaponScreenRenderer.cs:383` |

### `Jet` (instance fields read by other folders)

| Member | Source | Used by |
|---|---|---|
| `IMyCockpit _cockpit` | `Jet.cs:15` | TerrainModule, RadarControlModule, GunControlModule, HUDModule, CanardModule, TerrainRenderer, StartupSequence (excluded), StatusPanelRenderer, etc. |
| `List<IMyThrust> _thrusters` (forward + all non-Industrial) | `:16` | **UNUSED** — populated in `Jet` ctor at `:134-138` but no consumer reads `jet._thrusters`. |
| `List<IMyThrust> _thrustersbackwards` | `:17` | `HUDModule.cs:204` only. |
| `leftEngines / rightEngines` | `:20-21` | `HUDModule.cs:616-635`, `StatusPanelRenderer.cs:78-79` |
| `centerEngines` | `:22` | `HUDModule.cs:638` |
| `leftAB / rightAB` | `:23-24` | `HUDModule.cs:643-644`, `StatusPanelRenderer.cs:78-79` |
| `centerAB` | `:25` | `HUDModule.cs:645` |
| `static long GameTicks` | `:29` | written in `SystemManager.cs:196`. **No external read** in compiled code (search returned only the definition + write); MFDFrame uses `Jet.IC/IP/IA`, not GameTicks. **Effectively UNUSED externally.** |
| `static double GameSeconds` | `:30` | written `SystemManager.cs:203`; read inside `Jet.cs` (LastSeen, AgeSeconds). **No external read** outside Jet. |
| `static int IC, IP, IA` | `:31` | `MFDFrame.cs:54` (display only) |
| `string selectedEnemyName` | `:34` | written by `SelectEnemy`/`ClearSelection`; read in `GetSelectedEnemy` and `UpdateEnemyDecay`. **No external read.** |
| `long selectedEnemyEntityId` | `:35` | same as above. **No external read.** |
| `List<EnemyContact> enemyList` | `:93` | `RadarRenderer.cs:90`, `AirtoAir.cs:85`, `GunControlModule.cs:258`, `RadarControlModule.cs:220`, `TerrainRenderer.cs:231`, `TerrainMapModule.cs:119` (excluded) |
| `RadarControlModule radarControl` | `:101` | written `SystemManager.cs:103`; read internally by `Jet` consumers (e.g. HUDModule passes through ctor). Search shows only the assignment site — but `HUDModule.cs:109` constructor takes radarControl directly so consumers get it that way. The Jet field itself is **only written, never read externally**. |
| `Vector3D CachedGravity` | `:104` | `GunControlModule.cs:399`, `HUDModule.cs:462`, `RadarRenderer.cs:59`, `WeaponScreenRenderer.cs:308`, `StatusPanelRenderer.cs:62`, `TerrainRenderer.cs:182, 200`, `RadarControlModule.cs:408`, `TerrainModule.cs:50, 118, 264` |
| `List<IMyShipMergeBlock> _bays` | `:106` | `AirtoAir.cs:21`, `GridVisualization.cs:82`, `WeaponScreenRenderer.cs:81, 88` |
| `leftstab / rightstab` | `:107-108` | `CanardModule.cs:205, 211`, `HUDModule.cs:201-202` |
| `IMyTerminalBlock hudBlock` | `:109` | `HUDModule.cs:197` |
| `IMyTextSurface hud` | `:110` | `HUDModule.cs:215`, `StartupSequence.cs:41, 49, 56` (StartupSequence is excluded from build) |
| `List<IMyGasTank> tanks` | `:111` | `GridVisualization.cs:85`, `StatusPanelRenderer.cs:27`, `HUDModule.cs:205, 395` |
| `List<IMyBatteryBlock> batteries` | `:112` | `StatusPanelRenderer.cs:36`, `HUDModule.cs:401` |
| `int offset` | `:113` | `CanardModule.cs:73, 91, 143, 180, 186`, `HUDModule.cs:308, 712-717`, written in `SystemManager.cs:358, 361` |
| `bool manualfire` | `:114` | `HUDModule.cs:587, 596`, `TargetingRenderer.cs:91` |
| `List<IMySmallGatlingGun> _gatlings` | `:115` | `HUDModule.cs:598-601`, `TargetingRenderer.cs:83-96` |

### `Jet` methods

| Member | Source | Used by |
|---|---|---|
| `UpdateOrAddEnemy(...)` | `:213` | `RadarControlModule.cs:451, 498, 511, 519` |
| `UpdateEnemyDecay()` | `:301` | `RadarControlModule.cs:423` |
| `GetClosestNEnemies(int)` | `:343` | `AirtoAir.cs:87` |
| `GetEnemiesSortedByDistance()` | `:421` | `WeaponScreenRenderer.cs:69`, used internally by `SystemManager.FlipGPS` `:373` |
| `GetSelectedEnemy()` | `:375` | `RadarRenderer.cs:149`, `WeaponScreenRenderer.cs:51`, `RadarControlModule.cs:380`, `MissileBayHelper.cs:59, 75`, `HUDModule.cs:328`, `SystemManager.cs:382, 410` |
| `HasSelectedEnemy()` | `:401` | `AirtoAir.cs:85, 90` |
| `SelectEnemy(EnemyContact)` | `:406` | `AirtoAir.cs:88`, `SystemManager.cs:403` |
| `ClearSelection()` | `:412` | `SystemManager.cs:376`, internally `Jet.cs:318` |
| `GetEnemyContactColor(EnemyContact)` | `:436` | `WeaponScreenRenderer.cs:269`, `TerrainRenderer.cs:241`, `TerrainMapModule.cs:129` (excluded) |
| `GetVelocity()` | `:464` | `SystemManager.cs:209`, `StatusPanelRenderer.idle-slides.cs:130` (excluded) |
| `GetAltitude()` | `:474` | `SystemManager.cs:211`, `HUDModule.cs:737` |
| `GetCockpitPosition()` | `:486` | `SystemManager.cs:214`, internally `Jet.cs:361` |
| `GetCockpitMatrix()` | `:487` | **UNUSED** — search returned only the definition. Consumers prefer `_cockpit.WorldMatrix` directly. |
| `static GetEngineHealth(...)` | `:496` | `StatusPanelRenderer.cs:86` |
| `static GetEngineThrust(...)` | `:510` | `StatusPanelRenderer.cs:87-88` |
| `GetBatteryStatus(...)` | `:523` | `StatusPanelRenderer.cs:35`, `HUDModule.cs:404` |
| `GetFuelStatus(...)` | `:535` | `StatusPanelRenderer.cs:26`, `HUDModule.cs:398` |
| `static GetGunAmmo(IMySmallGatlingGun)` | `:556` | `GunControlModule.cs:486`, internally `Jet.cs:595` |
| `GetTotalGunAmmo()` | `:586` | `GridVisualization.cs:249` |
| `EnemyContact.AgeSeconds` | `:66` | `Jet.cs:316`, `Jet.cs:438` (color picker) |
| `EnemyContact.IsStale` | `:67` | **UNUSED** — defined but never queried. Decay logic compares `AgeSeconds` directly against `CONTACT_DECAY_SECONDS / SELECTED_DECAY_SECONDS`. |
| `EnemyContact.Matches(other)` | `:73` | **UNUSED** — defined but never called. `UpdateOrAddEnemy` does the equivalent inline (`Jet.cs:222-251`). |
| `EnemyContact.GetDisplayHistory()` | `:82` | Need to check — see below. |

<a id="getdisplayhistory"></a>
`GetDisplayHistory` is referenced in `WeaponScreenRenderer` (the weapon screen draws the 30-bit timeline). Confirmed live by inspection of the timeline rendering — keep this in mind in the final dead-code call.

---

## 4. Dead code findings

### Definitely dead

1. **`Extensions/RandomExtensions.cs`** — file contains `namespace IngameScript {}` (3 lines). Comment in `Program.cs:19` advertises it as "Extension methods" but the file has zero declarations. **Delete the file** (or repurpose).
2. **`Jet._thrusters`** field (`Jet.cs:16`) — populated in ctor at `:134-138` but never read anywhere. Costs one `GetBlocksOfType` scan plus the list allocation. The forward-thrusters list serves no purpose; `_thrustersbackwards` covers actual usage.
3. **`SystemManager.MainSurface`** property (`SystemManager.cs:138`) — public getter with zero callers. Comment claims pages need it for absolute coords but no page does.
4. **`Jet.GetCockpitMatrix()`** (`Jet.cs:487`) — public method with zero callers. Consumers all use `_cockpit.WorldMatrix` directly.
5. **`Jet.EnemyContact.IsStale`** (`Jet.cs:67`) — property never queried.
6. **`Jet.EnemyContact.Matches(other)`** (`Jet.cs:73`) — instance method never called.
7. **`Jet.GameTicks`** (`Jet.cs:29`) — incremented per tick at `SystemManager.cs:196` but never read by compiled code (only by excluded `Diagnostics/` and `SoundManager`'s tick counter? — confirm: search shows no external reads in compiled files; SoundManager uses its own `_frame` counter, not `Jet.GameTicks`). Effectively a dead counter.
   - *(Note: CLAUDE.md says "GameTicks is a raw call counter retained for ordering logic that doesn't depend on wall-clock time (e.g. `SoundManager`'s `FRAME_DELAY`)" — but the actual SoundManager uses its own counter, so the rationale is stale.)*

### Borderline

8. **`Jet.selectedEnemyName` / `selectedEnemyEntityId`** are public fields read only inside `Jet`. They could be `private` with public accessors, since `SelectEnemy/ClearSelection/GetSelectedEnemy/HasSelectedEnemy` cover all external usage.
9. **`Jet.radarControl`** field (`Jet.cs:101`) is **assigned at `SystemManager.cs:103` but never read** elsewhere — HUDModule etc. receive `radarControlModule` through their own constructors. The field exists "as a dot-accessible reference" but nothing dots into it. Either remove it, or refactor consumers to read `myJet.radarControl` instead of holding their own copy.

### Comments / leftovers

- `SystemManager.cs:130` `currentModule = null;` is set during `Initialize` but a re-initialization (called from `Program.cs:39` on `NullReferenceException`) leaves `_lastModule` and `_pendingArgument` static state stale across the recovery boundary. Not strictly dead but worth noting.
- No commented-out code blocks > 3 lines in any of the four files.
- No `TODO` markers in root or extensions.

---

## 5. Odd code findings

### Module / tick wiring

1. **`ConfigurationModule` and `TerrainModule` are NEVER background-ticked** (`SystemManager.cs:248-276`). This is consistent with CLAUDE.md ("terrain/config/air-to-ground do NOT background-tick"). They only run when `currentModule` selects them. ConfigurationModule has no `Tick()` override, so this is fine; TerrainModule also has no `Tick()` override. **Confirmed correct, just record the asymmetry.**
2. **All registered modules are also background-ticked except config + terrain.** Order in `Main` is HUD → Radar → AirtoAir → Gun → Canard. Note this differs slightly from `Initialize` order (Radar → AirtoAir → HUD → Config → Gun → Terrain → Canard).
3. **No "module constructed but not registered"** issues.

### Block-discovery filter inconsistency in `Jet` ctor

| Block list | Filter | Source |
|---|---|---|
| `_gatlings` | `t.CubeGrid == _cockpit.CubeGrid` | `:132` |
| `_thrusters` | `CubeGrid ==` + `!Industrial` | `:137` |
| `_thrustersbackwards` | `CubeGrid ==` + `!Industrial` + `GridThrustDirection==Backward` | `:153-156` |
| `_bays` | `IsSameConstructAs(_cockpit)` + name contains "Bay" | `:142` |
| `rightstab` / `leftstab` | `IsSameConstructAs(_cockpit)` + name contains | `:148-149` |
| `tanks` | `t.CubeGrid == _cockpit.CubeGrid` + name contains "Jet" | `:187` |
| `batteries` | `t.CubeGrid == _cockpit.CubeGrid` | `:191` |

The split between strict `CubeGrid ==` and `IsSameConstructAs` is **inconsistent**. A merge-blocked turret base (gun rotor on a sub-grid) would be excluded by `CubeGrid ==` but included by `IsSameConstructAs`. In practice:
- Gatlings on a sub-grid → invisible to JetOS.
- Thrusters on a sub-grid → ignored entirely (so left/right grouping never sees them).
- Bays / stabilizers already use `IsSameConstructAs` (correct since stabs are typically on rotors).

This is a **real consistency bug** when the user puts engines on a sub-grid. Keep the strict filter for thrusters intentional only if grouping by `t.Position.X` requires same-grid coordinates (which is the case — see #4 below).

### Engine grouping (`Jet.cs:158-181`)

4. **L/R/center split uses `t.Position.X` vs `_cockpit.Position.X`.** This is a grid-relative coordinate (Vector3I block address), not world coordinates. SE convention: looking forward through the cockpit, X+ is LEFT (CLAUDE.md confirms). The code assumes the cockpit is oriented with the grid's natural axes. **If the cockpit is mounted rotated relative to the grid**, the X-axis split will silently mis-classify left vs right. There is no `WorldMatrix.Forward`/`Right` projection — just raw `Vector3I.X`. Combined with `GridThrustDirection == Vector3I.Backward` this is a "grid-relative vs cockpit-relative" pitfall flagged in the task brief.
5. `centerEngines.Add(t)` only when `t.Position.X == cockpitX` exactly. A thruster one block off-center (X = cockpitX±1) lands in left/right rather than being shared between groups. Probably intentional, but record it.
6. `t.BlockDefinition.SubtypeId.Contains("Hydrogen")` is the AB classifier. Any future non-hydrogen high-thrust subtype would mis-classify. Single source of truth, no constant.
7. `if (_cockpit == null) { ... return; }` early exit at `:121-128` initializes `_thrusters`, `_thrustersbackwards`, and `_bays` but **leaves `tanks`, `batteries`, `_gatlings`, `leftstab`, `rightstab`, `leftEngines`...`centerAB` initialized to empty lists from their field initializers** — fine, no NRE risk, but `hud`/`hudBlock` stay null. HUDModule already checks `hudBlock == null` at `HUDModule.cs:210`, so the chain is safe.

### Tick-order observations

8. **`_myJet.CachedGravity` is updated at `:206-207` BEFORE `terrainData.Tick` (`:214`).** TerrainData uses `_cockpit.GetPosition()`, not gravity, so order is fine. Just note the dependency.
9. **`HandleSpecialFunctionInputs(argument)` runs at `:278` AFTER all module Ticks have run.** A `HandleSpecialFunction` that mutates state would not affect this tick's render — its effect is delayed by one tick. Plausibly intentional but undocumented.
10. **`_pendingArgument` is consumed once at `:189-193`**, then `argument` flows through `HandleInput` and `HandleSpecialFunctionInputs`. If the trigger pass arrives in tick N and the Update1 pass comes in tick N+1 (as expected), the argument is processed during tick N+1 — meaning the toolbar press has a one-tick delay. Matches the documented double-Main guard.
11. **`SoundManager.Tick(ElapsedSeconds)` correctly runs after all modules** (`:284`) — matches CLAUDE.md instruction.

### `ApplyAction` in constructor

12. **Root files do NOT call `ApplyAction` in `Initialize`.** RadarControlModule guards itself (`RadarControlModule.cs:159` comment "NO ApplyAction here (unreliable in constructor)"). Construction is clean.

### Hardcoded constants in `SystemManager` that arguably belong in `ConfigurationModule`

13. **Hysteresis dead-bands `20` (knots) and `40` (m)** at `:221` are inline magic numbers. `altitude_warning` and `speed_warning` come from config, but the dead-band thresholds don't.
14. **Page transition window `0.30` seconds** at `:329` is hardcoded. `UIController` has `PAGE_FADE_DURATION` — duplicating the constant in two places risks drift.
15. **EMA divisor `60` for `Jet.IA`** at `:287` is a magic number. Probably "average over 60 ticks".
16. **`dt` clamp `1.0 / 60.0` and upper `1.0`** at `:200` — fine for a fallback, but the lower bound differs from CLAUDE.md's stated `[1/60, 1.0]` (consistent — clamp is `<=0 || >1`, otherwise raw `dt`; values between 0 and 1/60 pass through unchanged).

### Double-Main guard

17. **The Trigger-pass return at `:184-188` does NOT advance `currentTick`, `Jet.GameTicks`, or `DeltaSeconds`.** Correct per CLAUDE.md invariant — do not regress.
18. **However, on Trigger pass, `SoundManager.Tick` is also skipped.** That's intentional: sound state advances only on the consolidated Update1 pass. Good.

### Recovery loop (`Program.cs:33-40`)

19. On `NullReferenceException`, `Echo` then `SystemManager.Initialize(program)` is called. This will:
    - Re-construct the entire `_myJet`, modules, MFD pages (heavy GC churn)
    - Reset `currentModule = null`
    - **Leave `_lastModule`, `_mainTransitionStart`, `_pendingArgument`, `currentTick` untouched** because they aren't reset in `Initialize`. Probably benign; record as latent state.

### MFD chrome dependency on `Jet.IC/IP/IA`

20. `MFDFrame.cs:54` reads `Jet.IC + "/" + Jet.IA + "/" + Jet.IP` — these are written in `SystemManager.Main` AFTER `uiController.Render` runs (the IC capture happens at `:285-287`, but Render happens earlier in `DisplayMenu` at `:331-333`). **The values displayed on screen are last tick's IC, not this tick's.** For a perf counter this is fine and possibly desired, but it's an off-by-one ordering quirk worth noting.

---

## 6. Notes for cross-folder consolidation

- **`SystemManager.MainSurface`** and **`Jet.radarControl`** are documented contracts that nothing actually consumes. The cross-folder summary should flag these as candidates for removal — but only after verifying every other file in the audit (UI/, HUD/, Modules/, Utilities/) confirms zero reads. (Best-effort search above shows zero, but the consolidation report should reconcile against the per-folder audits.)
- **The `IsSameConstructAs` vs `t.CubeGrid == _cockpit.CubeGrid` filter split in `Jet` ctor** is the highest-impact silent failure mode in the codebase: any user with thrusters or batteries on a merge-blocked sub-grid will see them disappear from JetOS while bays/stabilizers continue working. Recommend the consolidation report propose unifying on `IsSameConstructAs(_cockpit)` for all per-grid block lists, or document the constraint explicitly.
- **Engine L/R grouping uses raw grid-X position vs `_cockpit.Position.X`** — fragile if cockpit is rotated on the grid. CLAUDE.md mentions the related "GridThrustDirection grid-relative vs cockpit-relative engine-detection-when-seated bug" — same family of issue. The consolidation summary should call this out as a known geometry assumption worth documenting in CLAUDE.md or replacing with cockpit-orientation-aware grouping.
- **`Jet.GameTicks` and `Jet.GameSeconds` are write-only externally**. CLAUDE.md describes a real role for `GameSeconds` (mirrors `ElapsedSeconds`) and `GameTicks` (raw counter for `SoundManager` FRAME_DELAY), but the live SoundManager uses its own counter. Either remove `Jet.GameTicks` or update CLAUDE.md to reflect that nothing consumes it. `GameSeconds` is read inside `Jet` itself for `EnemyContact` aging — keep it but acknowledge it could be replaced by `SystemManager.ElapsedSeconds` directly to remove a duplicate field.
- **`Extensions/RandomExtensions.cs` is empty.** Delete the file or repurpose; it's the entire content of the Extensions folder.
