# Utilities folder audit

Folder: `Mdk.PbScript2/Utilities/`. Leaf library — static helpers and shared utility classes consumed by `Modules/`, `HUD/`, `UI/`, and root files (`SystemManager`, `Jet`, `Program`).

## 1. Types defined in this folder

### `Anim.cs`
- `Anim` (static) — wall-clock-driven animation primitives (`Lerp`, `EaseOut`, `EaseInOut`, `Pulse`, `Saw`, `Blink`, `LerpColor`, `WithAlpha`, `WarnAlpha`).
- `AnimatedValue` (instance) — eased scalar; calls `SetTarget` each tick, reads `Value` for the eased current value.

### `BallisticsCalculator.cs`
- `BallisticsCalculator` (static) — single method `CalculateInterceptPoint` (gravity-free quartic intercept solver with Newton refinement, falls back to pure-quadratic on root drift).

### `CircularBuffer.cs`
- `CircularBuffer<T>` (instance, `public`) — fixed-capacity FIFO wrapper around `Queue<T>`.

### `CommonTypes.cs`
- `RWRWarning` (instance, `public`) — POD threat record (position, velocity, name, incoming flag, RWR index).

### `CustomDataManager.cs`
- `CustomDataManager` (static) — dictionary cache over the PB's `CustomData` string with throttled re-parse.

### `MissileBayHelper.cs`
- `MissileBayHelper` (static) — bay selection, missile launch CustomData write, IGC broadcast, and bay option list rendering.

### `NavigationHelper.cs`
- `NavigationHelper` (static, `public`) — `CalculateHeading`, `TryParseGps`, `FormatGps`, `GetAspectAngleDeg`.

### `RadarTrackingModule.cs`
- `RadarTrackingModule` (instance, `public`) — wraps an AI Flight + Combat block pair. Despite the "Module" name, it is **not** a `ProgramModule`; it is a passive tracking adapter consumed by `RadarControlModule`. Nested struct `TrackingPoint` (position+timestamp pair).

### `Shortcuts.cs`
- (no type — just file-scope `static` helpers and `const` strings on `partial class Program`). Math/vector/sprite shortcuts plus all `TEXTURE_*` and `TEX_*` sprite mod IDs.

### `SoundManager.cs`
- `SoundManager` (static) — dual-channel sound dispatcher (warning + weapon) with priority arbitration and 3-frame block-op state machine. Nested private `SoundChannel` class.

### `SpriteHelpers.cs`
- `SpriteHelpers` (static) — sprite emission wrappers (`Bx`, `Sp`, `Tt`, `FBx`, `FTt`, line/rect/circle outlines), screen projection, range formatter, precomputed circle sin/cos tables.

### `TerrainAPI.cs` — **EXCLUDED FROM BUILD** (`<Compile Remove="Utilities\TerrainAPI.cs" />` in `Mdk.PbScript2.csproj:37`)
- `TerrainAPI` (static) — older async heightmap implementation around a 200×200×50m local window. Superseded by `TerrainData.cs`.

### `TerrainData.cs`
- `TerrainData` (static) — full-planet heightmap downloader (one-shot via the `TerrainAPI` mod property), with grid lat/lon lookup, AGL, tile min/max ranges, and tangent vectors at ship position.

---

## 2. Inputs (what this folder reads/calls from elsewhere)

### Cross-utility dependencies
- `Anim.cs:41,49,58,104,111,119` → reads `SystemManager.ElapsedSeconds` (the wall-clock time source).
- `MissileBayHelper.cs:66,83,104-106,113,169-172` → reads/writes via `SystemManager.GetCustomDataValue` / `SetCustomDataValue` (which delegate to `CustomDataManager`).
- `MissileBayHelper.cs:66,83,102,166` → calls `NavigationHelper.TryParseGps` / `FormatGps`.
- `MissileBayHelper.cs:59,75` → reads `Jet.GetSelectedEnemy()`.
- `MissileBayHelper.cs:207,231-233` → calls `program.IGC.SendBroadcastMessage` and `Rd` (from `Shortcuts`).
- `Anim.cs:42,66,77,83`, `BallisticsCalculator.cs:22-58`, `NavigationHelper.cs:22-66,82-127`, `TerrainData.cs:144-205,216-244,265`, `SpriteHelpers.cs:21-23,63,73-82,96,101-104,109-114` → all build on `Shortcuts.cs` helpers (`VN`/`VD`/`VX`/`Cs`/`Sn`/`At2`/`As`/`Mn`/`Mx`/`Cl`/`Ab`/`Cr`/`V2`/`PI`/`VZ`/`ToDeg`).
- `SpriteHelpers.cs:29,33,39,44` → forwards to `Sq`/`SqT`/`Tx` shortcut emitters (which route through `SpriteBus`).
- `SpriteHelpers.cs:101` → reads `HUDModule.COCKPIT_FOV_SCALE_Y` (cross-folder constant) for `ProjectToScreen`.

### Root-type reach-backs
- `Anim` reads `SystemManager.ElapsedSeconds` directly — fine.
- `MissileBayHelper.WriteLaunchSetup`, `FireNextAvailableBay` write `SystemManager` CustomData and read from `Jet`. This couples the helper to the project — acceptable.

### SE API notable calls
- `RadarTrackingModule.cs:76` — `IMyFlightMovementBlock.GetWaypoints(buf)`. **Memory note**: this works around the fact that `CurrentWaypoint` is never set when the flight block's behavior is not activated (radar-only mode). Decompilation note re-confirmed in `memory/se-ai-block-internals.md`.
- `RadarTrackingModule.cs:166,177` — reads `L_CombatBLock.SearchEnemyComponent.FoundEnemyId`. **Pitfall**: `IsTracking` is false-positive prone (can be stale even when behavior is disabled). Note acknowledged in CLAUDE.md.
- `RadarTrackingModule.cs:205` — `L_CombatBLock.DetailedInfo` parsing for `"Status: Attacking ..."` prefix. Cached on `FoundEnemyId` change to avoid re-parse cost.
- `MissileBayHelper.cs:139,176` — `IMyShipMergeBlock.ApplyAction("Fire")`. Action string verified against decompiled DLLs (`docs/se-api-reference.md`).
- `SoundManager.cs:48-55` — `GridTerminalSystem.GetBlocksOfType(...)` with name-substring filter for `"Sound Block Warning"` and `"Canopy Side Plate Sound Block"`.
- `SoundManager.cs:62-63` — `IMySoundBlock.Stop()/Play()/SelectedSound/Volume/Enabled` driven through the 3-state delay machine to survive the SE double-`Main()` quirk.
- `TerrainData.cs:69,83,84,134,136` — `me.GetProperty("TerrainAPI")` + `me.GetValue<StringBuilder>("TerrainAPI")` / `me.SetValue<StringBuilder>("TerrainAPI", ...)`. Mod-only property — `Probe` falls back to `_off=true` when missing.
- `CustomDataManager.cs:30,89` — direct `IMyProgrammableBlock.CustomData` read/write. `MarkDirty()` exists for callers that bypass the manager.
- `NavigationHelper.cs:21` — `IMyCockpit.GetNaturalGravity()` for compass north reference.

---

## 3. Outputs (what this folder exposes to callers)

### `Anim` (`Anim.cs`)
- `Lerp(a,b,t)` — used at `Anim.cs:120`, `UI/UIController.cs:268,296`.
- `EaseOut(t)` — used at `Anim.cs:120`, `HUD/WeaponScreenRenderer.cs:143`, `UI/UIController.cs:173,268,296`.
- `EaseInOut(t)` — **UNUSED** (only definition site).
- `Pulse(period)` — used internally by `WarnAlpha` (`Anim.cs:83`); no external callers.
- `Saw(period)` — **UNUSED**.
- `Blink(period)` — used at `HUD/InstrumentRenderer.cs:339,345`, `HUD/WeaponScreenRenderer.cs:407`, `Modules/HUDModule.cs:423,429`, `HUD/TargetingRenderer.cs:226,313`.
- `LerpColor(a,b,t)` — used at `HUD/WeaponScreenRenderer.cs:143`, `UI/UIController.cs:190`.
- `WithAlpha(c, alpha)` — used at `UI/UIController.cs:191`, `UI/GridVisualization.cs:237,290`.
- `WarnAlpha(period=1.0)` — used at `UI/GridVisualization.cs:237,290`.
- `AnimatedValue` — used at `UI/GridVisualization.cs:43,44`, `UI/StatusPanelRenderer.cs:12,13`.

### `BallisticsCalculator` (`BallisticsCalculator.cs`)
- `CalculateInterceptPoint(...)` — used at `Modules/GunControlModule.cs:445`, `Modules/HUDModule.cs:348` (single source of truth for both gun aim and HUD lead pip).

### `CircularBuffer<T>` (`CircularBuffer.cs`)
- `Enqueue(item)`, `Dequeue()`, `Count`, ctor — used at `Modules/HUDModule.cs:73-76,675,683,691,699` for velocity / altitude / G-force / AoA smoothing windows.

### `RWRWarning` (`CommonTypes.cs`)
- All fields and ctor — used at `Modules/RadarControlModule.cs:96,770,775` (the `activeThreats` list).

### `CustomDataManager` (`CustomDataManager.cs`)
- `Initialize`, `GetValue`, `SetValue`, `TryGetValue`, `MarkDirty` — all four delegated through `SystemManager` (`SystemManager.cs:96,149,154,159,164`).
- Private `RebuildCustomData` and `ParseCustomData` are internal-only.

### `MissileBayHelper` (`MissileBayHelper.cs`)
- `IsBayReady(bay)` — used at `HUD/WeaponScreenRenderer.cs:373` (loaded indicator) plus internally.
- `ToggleBaySelection(sel,i)` — used at `Modules/AirtoAir.cs:78`.
- `ToggleSelectedBays(bays,sel)` — used at `Modules/AirtoAir.cs:70`.
- `ExtractBayNumber(bay, fallback)` — only internal callers (l. 112,167,205). **External: UNUSED.** Note: `Jet.cs:194` defines its own `ExtractBayNumber(string)` separately — see odd findings.
- `TryGetTargetPosition(jet, out pos)` — only internal callers (l. 99,125,159). **External: UNUSED.**
- `TryGetTargetData(jet, out pos, out vel)` — only internal caller (l. 200). **External: UNUSED.**
- `WriteLaunchSetup(...)` — only internal caller `FireSelectedBays` (l. 128). **External: UNUSED** (could be private).
- `FireSelectedBays(...)` — used at `Modules/AirtoAir.cs:66`.
- `FireNextAvailableBay(...)` — only internal caller `HandleWeaponHotkey` (l. 222). **External: UNUSED.**
- `BroadcastTargetUpdates(program, jet, bays)` — used at `Modules/AirtoAir.cs:92`.
- `WEAPON_HOTKEYS` const — used at `Modules/AirtoAir.cs:103`.
- `HandleWeaponHotkey(key, ...)` — used at `Modules/AirtoAir.cs:97`.
- `ColorToChar(r,g,b)` — only internal caller (l. 245). **External: UNUSED.**
- `BuildBayOptionList(options, bays, sel)` — used at `Modules/AirtoAir.cs:54`.
- `IGC_CHANNEL_PREFIX` const — used internally at l. 207. **External: UNUSED**, but it's a contract string the missile script depends on so should remain `public const`.

### `NavigationHelper` (`NavigationHelper.cs`)
- `CalculateHeading(cockpit)` — used at `Modules/HUDModule.cs:732`.
- `TryParseGps(s, out v)` — used at `HUD/RadarRenderer.cs:254`, `Utilities/MissileBayHelper.cs:66,83`.
- `FormatGps(v)` — used at `SystemManager.cs:419`, `Utilities/MissileBayHelper.cs:102,166`.
- `GetAspectAngleDeg(vel, rel)` — used at `Modules/RadarControlModule.cs:805,823`.

### `RadarTrackingModule` (`RadarTrackingModule.cs`)
Active class — consumed by `RadarControlModule`.
- ctor `(flightBlock, combatBlock)` — used at `Modules/RadarControlModule.cs:139`.
- `L_FlightBlock` field — used at `Modules/RadarControlModule.cs:563,564`.
- `L_CombatBLock` field — used at `Modules/RadarControlModule.cs:567,568,...` (typo in name preserved across both files).
- `UpdateTracking(currentTimeTicks)` — used at `Modules/RadarControlModule.cs:281`.
- `TargetVelocity` — used at `RadarControlModule.cs:451,498,511,519`.
- `TargetPosition` — used at `RadarControlModule.cs:438,479`.
- `IsTracking` — used at `RadarControlModule.cs:435,477`.
- `TrackedEntityId` — used at `RadarControlModule.cs:442,491`.
- `TrackedObjectName` — used at `RadarControlModule.cs:443,492`.
- `HasReceivedPosition` — used at `RadarControlModule.cs:435,477`.
- `CurrentTime`, `CurrentTick` public fields — **UNUSED externally**, only mutated inside `UpdateTracking`. Could be private.

### `Shortcuts.cs` (file-scope helpers on `Program`)
**All sprite emission shortcuts:**
- `Sq(cx,cy,w,h,c)` / `Sq(...,r)` — used by `SpriteHelpers.Bx` (`SpriteHelpers.cs:29,33`) and many callers via `Bx`.
- `SqT(tex, ...)` — used by `SpriteHelpers.Sp` and direct callers (l. 76 in `SpriteHelpers.cs`, plus `MFDFrame.cs`, `UIController.cs`, etc.).
- `Tx(d,x,y,s,c,a,fn)` — used by `SpriteHelpers.Tt` and direct callers across UI/HUD.

**Block accessors:** `LV` (used in 7 files), `GP` (7), `WM` (4), `WF` (19 files), `WR` / `WU` / `SS` / `SX` / `SY` (all used across HUD/Modules/UI).

**Math/vector:** `VN`, `VD`, `VX`, `VTN`, `VDi`, `VZ` — all used 142× across 19 files. `Sn`, `Cs`, `At2` widely used. `As(double)` used at `TerrainData.cs:191,217,235`, `Modules/HUDModule.cs:468`, `Modules/CanardModule.cs:134`. `Rd(double)` used at `MissileBayHelper.cs:231-233`, `HUD/RadarRenderer.cs:222-225`. `Sg(double)` and `Sg(float)` both used (Sg(float) at `HUD/TargetingRenderer.cs:151,155`). `Cl`, `ToDeg`, `ToRad`, `V2`, `Cr`, `Ab`, `Mn`, `Mx`, `PI` widely used.

**`TRIM` const string** — used at `Modules/HUDModule.cs`, `Modules/CanardModule.cs` (mod-added terminal property).

**Built-in sprite IDs (vanilla):**
- `TEXTURE_SQUARE` — used at `SpriteHelpers.cs:73-82` (referenced via `MFDTheme.SQ` indirection actually — confirm: only used internally by `Shortcuts`/`SpriteHelpers`).
- `TEXTURE_SQUARE_HOLLOW` — used at `SpriteHelpers.cs:76`.
- `TEXTURE_CIRCLE` — used at `SpriteHelpers.cs:91` (inside `DrawCircleOutline`).
- `TEXTURE_TRIANGLE` — only defined in `Shortcuts.cs`. **UNUSED.**
- `TEXTURE_CIRCLE_SOLID` — used at `HUD/HorizonRenderer.cs`, `HUD/InstrumentRenderer.cs`, `Modules/TerrainModule.cs`, `HUD/WeaponScreenRenderer.cs`.
- `TEXTURE_FPM` — used at `Modules/HUDModule.cs`, `Modules/CanardModule.cs`.

**Mod sprite IDs (`JetOS_*`)** — see Section 4 for complete unused list. Quick summary of the ones that **are** used:
- `TEX_PITCH_POS`, `TEX_PITCH_NEG`, `TEX_ROLL_POINTER`, `TEX_BANK_ARC` → `HUD/HorizonRenderer.cs`.
- `TEX_AOA_BRACKET`, `TEX_BORESIGHT`, `TEX_TAPE_INDEX` → `HUD/InstrumentRenderer.cs`.
- `TEX_HDG_CHEVRON`, `TEX_TGT_BRACKET`, `TEX_LEAD_PIP`, `TEX_NAV_ARROW`, `TEX_LOCK_DIAMOND` → `HUD/TargetingRenderer.cs`.
- `TEX_C_HOSTILE`, `TEX_C_FRIENDLY`, `TEX_C_UNKNOWN`, `TEX_RANGE_RING`, `TEX_OWN_SHIP`, `TEX_LOCK_CONE` → `HUD/RadarRenderer.cs`.
- `TEX_MISSILE`, `TEX_BAY_EMPTY`, `TEX_BAY_LOADED` → `HUD/WeaponScreenRenderer.cs`.
- `TEX_FUEL_TANK`, `TEX_BATTERY`, `TEX_STATUS_DOT` → `UI/StatusPanelRenderer.cs`.
- `TEX_MFD_CORNER` → `UI/MFDFrame.cs`.
- `TEX_MASTER_CAUTION`, `TEX_MASTER_WARNING` → `Modules/HUDModule.cs`.
- `TEX_NO_SIGNAL` → `UI/StatusPanelRenderer.cs`.
- `TEX_GLYPH_CROSS` → `HUD/TargetingRenderer.cs`.
- `TEX_HATCH` → `HUD/HorizonRenderer.cs`.

### `SoundManager` (`SoundManager.cs`)
- `Initialize(grid)` — used at `SystemManager.cs:97`.
- `RequestWarning(sound, prio, loopSeconds=5)` — used at `SystemManager.cs:227,235` (altitude warning), `Modules/RadarControlModule.cs:833` (RWR alert).
- `RequestWeapon(sound, prio, loopSeconds=5)` — **UNUSED** (no callers for the weapon channel ever — see odd findings).
- `Tick(currentSeconds)` — used at `SystemManager.cs:284`.
- `PRIORITY_NONE = 0` — used internally only.
- `PRIORITY_SEARCH = 1` — **UNUSED** (no `RequestWeapon` callers means no priority constant for AIM9 search).
- `PRIORITY_LOCK = 2` — **UNUSED** (same reason).
- `PRIORITY_RWR = 3` — used at `RadarControlModule.cs:833`.
- `PRIORITY_ALTITUDE = 4` — used at `SystemManager.cs:227,235`.

### `SpriteHelpers` (`SpriteHelpers.cs`)
- `CIRC_SEGS` (internal const), `CSin` / `CCos` (internal arrays) — used at `HUD/RadarRenderer.cs:231-234` (radar arc rendering).
- `Bx(frame, x, y, w, h, c)` and overload — used 74× across HUD/UI/Modules.
- `Sp(frame, tex, x, y, w, h, c, r=0)` — used widely (sprite forwarder).
- `Tt(frame, str, ...)` — used widely (text forwarder).
- `FBx(x, y, w, h, c)` — used at `HUD/HorizonRenderer.cs:62,63`, `UI/GridVisualization.cs:193` (returns `MySprite` for direct list-add — bypasses `SpriteBus`; see odd findings).
- `FTt(...)` — used at `HUD/HorizonRenderer.cs:57,58` (same pattern).
- `AddLineSprite(frame, p1, p2, thickness, c)` — used at `HUD/RadarRenderer.cs:202,235`, `HUD/TargetingRenderer.cs:124-126,278-281`, `HUD/HorizonRenderer.cs:156`, `UI/StatusPanelRenderer.idle-slides.cs:267,283`.
- `DrawRectangleOutline(frame, x, y, w, h, lineW, c)` — used at `HUD/WeaponScreenRenderer.cs:49,155`.
- `DrawCircleOutline(frame, center, radius, c, thickness)` — used at `HUD/WeaponScreenRenderer.cs:393` only.
- `FormatRange(meters)` — used at `HUD/WeaponScreenRenderer.cs:280`.
- `ProjectToScreen(localDir, center, surfaceSize)` (internal) — used at `Modules/HUDModule.cs:358`, `HUD/TargetingRenderer.cs:71,122,209,272`, `HUD/HorizonRenderer.cs:147`, `HUD/RadarRenderer.cs:277`.
- `RotatePoint(point, pivot, angle)` — **UNUSED** (only definition site at l. 107).

### `TerrainAPI` (`TerrainAPI.cs`) — EXCLUDED FROM BUILD
All public members are unreachable from compiled code. The remaining references (`UI/TerrainRenderer.cs`, `Modules/TerrainMapModule.cs`) are themselves excluded from the build (`Mdk.PbScript2.csproj:38,39`). See Section 4.

### `TerrainData` (`TerrainData.cs`)
- `Probe(me)` — used at `SystemManager.cs:98`.
- `Init(me)` — used at `SystemManager.cs:99`.
- `Tick(me, shipPos)` — used at `SystemManager.cs:214`.
- `Available` — used at `Modules/TerrainModule.cs:52`.
- `Ready` — used at `Modules/TerrainModule.cs:60,118`, `UI/StatusPanelRenderer.cs:62`.
- `Loading` — used at `Modules/TerrainModule.cs:62`.
- `DownloadProgress` — used at `Modules/TerrainModule.cs:64`.
- `CellSize` — used at `Modules/TerrainModule.cs:96,137`.
- `GridFwd`, `GridRight` — used at `Modules/TerrainModule.cs:150`.
- `W2GF(wp, out r, c, fr, fc)` — used at `Modules/TerrainModule.cs:80,123`.
- `Surf(r,c)` — used at `Modules/TerrainModule.cs:161`.
- `Alt(wp)` — used at `Modules/TerrainModule.cs:82,125`.
- `AGL(wp)` — used at `Modules/TerrainModule.cs:93,134`.
- `W2G(wp, out r, c)` (the non-frac version) — **UNUSED**.
- `SurfRaw(r, c)` — **UNUSED**.
- `TileRange(r, c, out mn, mx)` — **UNUSED**.
- `TilesReady` — **UNUSED**.
- `Gen` — **UNUSED**.
- `MeanR` — **UNUSED**.
- `Rows`, `Cols` — **UNUSED**.

---

## 4. Dead code findings

### `TerrainAPI.cs` (excluded from build via `<Compile Remove>`)
Whole file is dead code on disk. Types/methods that exist but never reach the compiler:
- enum `LoadState { IDLE, POLLING, LOADING, READY, UNAVAILABLE }`
- `Probe`, `Request`, `Tick`, `TickPoll`, `TickLoad`, `NeedsRefresh`, `WorldToGrid`, `SurfaceAlt`, `AGL`, `ShipAlt`, `QueryPoint`, `Reset`
- public accessors `IsReady`, `IsLoading`, `IsAvailable`, `Width`, `Height`, `CellSize`, `BaseAltitude`, `PlanetCenter`, `GridForward`, `GridRight`, `DebugStatus`
- constants `MAP_W=200`, `MAP_H=200`, `CELL_M=50`, `CHUNK_ROWS=10`

What `TerrainData.cs` superseded:
- The 200×200×50m local heightmap window with re-request-on-move logic (`NeedsRefresh`, `_refreshDistSq`) is replaced by `TerrainData`'s one-time full-planet download (lat/lon grid → height table). After download, all lookups are offline and never need re-issuing.
- `WorldToGrid`/`SurfaceAlt`/`AGL`/`ShipAlt` are replaced by `W2G`/`W2GF`/`Surf`/`AGL`/`Alt`.
- The `H;...` request and `S;READY/BUSY` polling protocol is collapsed into the simpler `P;cellSize` then `C;offset;count` protocol.
- `QueryPoint` (synchronous single-point query) has no equivalent in `TerrainData` — was used for ad-hoc surface lookups that are now never needed.
- `Reset` and `IsAvailable`/`IsLoading`/`IsReady` are still mirrored in `TerrainData` as `_off`/`Ready`/`Loading`.

### Unused `TEX_*` / `TEXTURE_*` sprite constants (`Shortcuts.cs`)
Defined and referenced only in `Shortcuts.cs`:
- `TEXTURE_TRIANGLE` (l. 73)
- `TEX_PITCH_ZERO` (l. 83)
- `TEX_PITCH_INV` (l. 84)
- `TEX_TAPE_BUG` (l. 92)
- `TEX_GMETER_FACE` (l. 96)
- `TEX_GAUGE_NEEDLE` (l. 97)
- `TEX_WARNING` (l. 107)
- `TEX_RADAR_SWEEP` (l. 113)
- `TEX_STATUS_RING` (l. 124)
- `TEX_ICON_HUD` (l. 131)
- `TEX_ICON_RADAR` (l. 132)
- `TEX_ICON_WEAPONS` (l. 133)
- `TEX_ICON_TERRAIN` (l. 134)
- `TEX_ICON_CONFIG` (l. 135)
- `TEX_ICON_CANARD` (l. 136)
- `TEX_ICON_GUN` (l. 137)
- `TEX_ICON_FUEL` (l. 140)
- `TEX_ICON_POWER` (l. 141)
- `TEX_ICON_AMMO` (l. 142)
- `TEX_BG_SCANLINE` (l. 145)
- `TEX_BG_GRIDDOT` (l. 146)
- `TEX_KEY_HINT_BOX` (l. 149)
- `TEX_GLYPH_CHECK` (l. 150)
- `TEX_GLYPH_BACK` (l. 152)
- `TEX_AIRCRAFT_SYM` (l. 155)
- `TEX_MISSILE_HEAT` (l. 160)
- `TEX_MISSILE_RADAR` (l. 161)

Total: **27 unused mod sprite IDs**. Many are "designed-for-future" entries (e.g. all 7 module icons + 3 status label icons, plus all background patterns and key-hint glyphs).

### Unused shortcut helpers (`Shortcuts.cs`)
None of the math/vector/sprite shortcuts themselves are unused — even `As`, `Rd`, `Sg(float)`, `Sg(double)`, `VTN`, `VDi`, `LV`, `GP`, `WM` all have callers.

### Unused public methods/properties in utilities
- `Anim.EaseInOut` (`Anim.cs:30`)
- `Anim.Saw` (`Anim.cs:46`)
- `Anim.Pulse` (`Anim.cs:38`) — only called by `Anim.WarnAlpha` internally; could be made `private`.
- `SpriteHelpers.RotatePoint` (`SpriteHelpers.cs:107`)
- `SoundManager.RequestWeapon` (`SoundManager.cs:97`) — entire weapon channel API is unused
- `SoundManager.PRIORITY_SEARCH`, `SoundManager.PRIORITY_LOCK` (`SoundManager.cs:13,14`) — orphan priority constants for the unused weapon channel
- `TerrainData.W2G` (non-frac), `TerrainData.SurfRaw`, `TerrainData.TileRange`, `TerrainData.TilesReady`, `TerrainData.Gen`, `TerrainData.MeanR`, `TerrainData.Rows`, `TerrainData.Cols` — entire tile-min/max culling subsystem (`_tileMin`/`_tileMax`/`BuildTileChunk`/`TILE_BATCH`/`_tileOfs`/`_tileRows`/`_tileCols`/`_tilesReady`) builds and exposes data nobody reads
- `MissileBayHelper.WriteLaunchSetup`, `MissileBayHelper.TryGetTargetPosition`, `MissileBayHelper.TryGetTargetData`, `MissileBayHelper.ExtractBayNumber`, `MissileBayHelper.ColorToChar`, `MissileBayHelper.FireNextAvailableBay` — public but only called internally; could be `private`
- `RadarTrackingModule.CurrentTime`, `RadarTrackingModule.CurrentTick` — public fields with no external readers

### Private fields/methods never used
- `SoundChannel.activeSound`, `playStartSeconds`, `activeLoopSeconds` — all read inside `TickChannel`, fine.
- `TerrainData._tileMin`, `_tileMax`, `_tileRows`, `_tileCols`, `_tileOfs`, `_tilesReady`, `BuildTileChunk` — built every tick after download completes (l. 124) but the resulting data is never consumed externally (`TileRange` and `TilesReady` are unused). Wasted work each tick.

### Commented-out blocks > 3 lines
None found.

### TODOs / debug leftovers
- `RadarTrackingModule.cs:13-19` — `// monkaspeed`, `// keen ree` style comments preserved from upstream reference code. Cosmetic.
- `RadarTrackingModule.cs:32-33` — `L_FlightBlock` / `L_CombatBLock` (typo — `Block` missing the `c`) public fields. Used externally by `RadarControlModule.cs` so the typo is propagated.
- `Utilities/README.md` describes types `PIDController`, `Player`, `Obstacle`, `Vector2I` that **do not exist** anywhere in the folder. The README is stale.

---

## 5. Odd code findings

### Sprite emission split between `Shortcuts.Sq/SqT/Tx` and `SpriteHelpers.Bx/Sp/Tt/FBx/FTt`
Three layers exist:
1. `Shortcuts.cs` `Sq`/`SqT`/`Tx` — actual `new MySprite { ... }` plus `SpriteBus.Add(...)`. Single source of truth (per file comment l. 12-15).
2. `SpriteHelpers.Bx`/`Sp`/`Tt` — frame-taking wrappers that ignore the frame and forward to `Sq`/`SqT`/`Tx`. The `frame` parameter is dead weight kept for API uniformity.
3. `SpriteHelpers.FBx`/`FTt` — return a `MySprite` instead of forwarding to the bus. **These bypass `SpriteBus`** (used at `HorizonRenderer.cs:62-63` for direct horizon-line sprites and at `GridVisualization.cs:193`/`StatusPanelRenderer.idle-slides.cs` to fill caches). If the SpriteBus transition system is supposed to capture all UI sprites, these are escape hatches that may not be tee'd by the transition recorder. Worth verifying whether that's intentional (likely yes for caches) or an oversight.

### `Bx` vs `Sq` — what's the purpose?
`SpriteHelpers.Bx` simply delegates to `Sq`. `Bx` adds nothing functional; the only reason to keep it is uniform `(frame, ...)` argument shape for callers that already pass `frame` in. Over 70 callsites use `Bx`; switching them to `Sq` would shorten output further but is a minify-only win.

### `MissileBayHelper.ExtractBayNumber` vs `Jet.ExtractBayNumber`
Two different `ExtractBayNumber` methods exist:
- `MissileBayHelper.cs:44` — `ExtractBayNumber(IMyShipMergeBlock, int fallback)` — returns int.
- `Jet.cs:194` — `ExtractBayNumber(string)` — returns int (private to Jet, used to sort `_bays`).

Both parse `"Bay N"` style names. They are independent implementations and don't share code. Not strictly wrong, but a single helper would avoid the duplication.

### `RadarTrackingModule` lives in `Utilities/` but the name suggests it's a `ProgramModule`
It is **not** a `ProgramModule` subclass — it is a passive tracker class consumed by `RadarControlModule` (the actual module). The `README.md` even calls it "Deprecated", but it is in fact the active per-radar tracking adapter. The comment "Replaced by centralized `RadarControlModule`" is misleading — `RadarControlModule` *uses* `RadarTrackingModule` (one per AI block pair). README needs correcting.

### `RadarTrackingModule` exposes mutable state as public fields
- `L_FlightBlock`, `L_CombatBLock` (with typo) — public fields.
- `CurrentTime`, `CurrentTick` — public fields with no external reader. Consumers shouldn't be poking them — they should be private (or properties).

### Duplicated path names in `TerrainData` and `TerrainAPI`
Both speak to the same `"TerrainAPI"` mod property. Only `TerrainData` is in the build, but `TerrainAPI.cs` retains a fully-implemented async pipeline that would conflict if ever re-enabled. Worth deleting `TerrainAPI.cs` outright once superseding is confirmed final.

### Numeric constants that look configurable
- `SoundManager.FRAME_DELAY = 3` (`SoundManager.cs:124`) — inline rationale is solid (PB-call count for SE block-op ordering); fine as a magic constant but worth promoting to a named const if it ever needs tuning.
- `SoundManager` weapon-channel volume `0.3f` (`SoundManager.cs:57`) — hard-coded; warning channel uses default 1.0f.
- `TerrainData.CHUNK = 5000`, `HOFF = 32768`, `DEFAULT_CELL = 200`, `TILE = 16`, `TILE_BATCH = 2500` — all instruction-budget driven, OK to be const.
- `BallisticsCalculator.tolerance = 0.0001`, Newton step bounds `[t*0.1, t*2.0]`, drift threshold `0.05` — all hard-coded; bounded Newton is documented in CLAUDE.md.
- `MissileBayHelper.BIT_SPACING = 255.0/7.0` (`l. 228`) — magic 0xe100 base for the in-game color font character set; documented elsewhere in SE community but no reference comment here.
- `RadarTrackingModule.ForcedRefreshRate = 40` — comment says "force a position relog on static grids", but it's a tick count not wall-clock seconds. CLAUDE.md prefers wall-clock for durations.

### Methods whose name says one thing but body does another
- `SpriteHelpers.DrawCircleOutline` (`SpriteHelpers.cs:85`) — comment says "single CircleHollow sprite — was 24 line segments". The body uses `TEXTURE_CIRCLE` (which is `"CircleHollow"`, but the constant is named `TEXTURE_CIRCLE` — the name is misleading; one would expect `TEXTURE_CIRCLE` to be the solid disc).
- `SpriteHelpers.DrawRectangleOutline` (`SpriteHelpers.cs:67`) — branches between `SquareHollow` (single sprite) and four 1-px filled rects depending on aspect; behaviour is documented in the comment but worth knowing line widths are nominal once the SquareHollow path is hit.
- `MissileBayHelper.IsBayReady` (`l. 16`) — only checks `IsConnected`, ignores `Enabled`/`IsFunctional`/inventory. The "Ready" name overstates the check.

### `RadarTrackingModule` `_waypointBuffer` is not cleared on detach
The `_waypointBuffer` list is reused across calls and cleared at the top of `UpdateTracking` — but if the radar stops being polled (state changes to IDLE), the buffer keeps its last contents until the next poll. Negligible memory.

### `CustomDataManager.ParseCustomData` rebuild ordering pitfall
`SetValue` calls `ParseCustomData` first to ensure cache is fresh, then mutates dict, then calls `RebuildCustomData`. This is correct, but during rebuild the dictionary iteration order is not guaranteed (`foreach var kvp in customDataCache`). For SE this only matters because the rebuilt CustomData column order may shuffle. Cosmetic only, but the original line order from the user's manual edits is lost on first `SetValue`.

---

## 6. Notes for the cross-folder consolidation

1. **Sprite-mod texture inventory is over-defined**: 27 of ~55 `JetOS_*` sprite IDs are declared in `Shortcuts.cs` and never referenced elsewhere — the overall summary should track these against `Data/LCDTextures.sbc` (per memory `reference_se_lcd_sprite_schema.md`) to verify whether the mod even ships them. Either the sprites exist and the rendering code never shipped (likely — module icons, background patterns, key hints) or the constants need pruning. Either way it is a noticeable surface-area burden in the most-included file.

2. **SoundManager has a dead weapon channel**: `RequestWeapon`, `PRIORITY_LOCK`, `PRIORITY_SEARCH` are all unused after the AirtoAir consolidation noted in `MEMORY.md`. The channel still gets initialized (`weaponChannel`/`PrepChannel`/`TickChannel`) every frame for nothing. Either resurrect AIM9 lock/search tones (intent in the memory file) or drop the second channel and the unused constants.

3. **MissileBayHelper public surface is wider than callers need**: Only 7 of 13 public members have external callers. Tightening `WriteLaunchSetup`, `TryGetTargetPosition`, `TryGetTargetData`, `ExtractBayNumber`, `ColorToChar`, `FireNextAvailableBay` to `private` would make the AirtoAir interface contract explicit and shrink minified output. (The IGC contract — `IGC_CHANNEL_PREFIX` and the `BroadcastTargetUpdates` payload format — should remain documented public.)

4. **`TerrainAPI.cs` should be deleted from disk**: Superseded by `TerrainData.cs`, excluded from the build, no compiled callers. Keeping the file invites accidental re-enable conflicts. Same applies to the stale `Utilities/README.md` (lists types that don't exist: `PIDController`, `Player`, `Obstacle`, `Vector2I`; calls `RadarTrackingModule` "Deprecated" when it's actively used).

5. **Unused TerrainData tile-min/max subsystem wastes ticks**: `BuildTileChunk` runs every tick after download until `_tilesReady` is true, building a `_tileMin`/`_tileMax` cache that nothing reads (`TileRange`/`TilesReady` are unused). Either wire it up to the spatial culling it was designed for, or drop the whole tile pipeline (saves ~2500 instructions/tick during the build phase).
