# JetOS Audit — Cross-Folder Summary

Consolidates findings from the five per-folder audits in this directory:

- `Modules.md`
- `HUD.md`
- `UI.md`
- `Utilities.md`
- `Root_and_Extensions.md`

Each item below cites the per-folder reports as `[Folder.md §N]` where N is the section number, plus `file:line` for code locations. Read this report top-to-bottom to plan work; consult the per-folder reports for the full detail.

---

## 0. Scope at a glance

- 5 folders audited (Modules, HUD, UI, Utilities, Root + Extensions).
- 6 source files excluded from build via `<Compile Remove>` (csproj lines 35-39): `Diagnostics/**`, `UI/StartupSequence.cs`, `UI/TerrainRenderer.cs`, `UI/StatusPanelRenderer.idle-slides.cs`, `Utilities/TerrainAPI.cs`, `Modules/TerrainMapModule.cs`. Two of those reference symbols that no longer exist on the current API (`StatusPanelRenderer.idle-slides.cs` calls `TerrainModule.GetMinimap`; `TerrainMapModule.cs` overrides `HasCustomScreen`/`RenderCustomScreen`) — they would not link if un-excluded.
- Build is clean (Release + Debug); 4 pre-existing warnings on uninitialized `MissileTrackingData` fields — see broken-contract item #1 below.

---

## 1. Broken cross-folder contracts (highest priority)

### 1.1 `HUDModule.activeMissiles` is a UI-only feature with no producer

[Modules.md §5.6, HUD.md §2 row "activeMissiles"]

- `WeaponScreenRenderer` reads `activeMissiles` to render the missile-TOF panel (`HUD/WeaponScreenRenderer.cs:82,92,249,338-348`).
- The only fire path, `MissileBayHelper.FireSelectedBays` (`Utilities/MissileBayHelper.cs`), never calls `activeMissiles.Add(...)`.
- The list is therefore always empty; the panel never displays. The struct's four warnings (`HUDModule.cs:127-130 CS0649`) are the static-analyser confirming that nothing assigns to `BayIndex`/`LaunchTime`/`EstimatedTOF`/`TargetPosition`.

**Decision needed**: wire missile launch into `activeMissiles`, OR delete the panel + struct + field together.

### 1.2 `Jet.radarControl` is write-only

[Root_and_Extensions.md §4 #9]

- `SystemManager.cs:103` assigns `_myJet.radarControl = radarControlModule`.
- No file in the repo reads `myJet.radarControl`. Every consumer (`HUDModule`, HUD renderers, `AirtoAir`, `WeaponScreenRenderer`) takes the radar reference through its own constructor.
- The field exists as a "dot-accessible reference" with zero dotters.

**Action**: remove `Jet.radarControl` (`Jet.cs:101`) and the assignment site, or make consumers read `myJet.radarControl` instead of holding parallel references.

### 1.3 `GridMfdPage._radar` chain is dead plumbing

[UI.md §4, Root_and_Extensions.md §3]

- `GridMfdPage` ctor takes a `RadarControlModule radar` (`UI/GridMfdPage.cs:15,19,25`), stores it, and forwards to `GridVisualization.Render`.
- `GridVisualization.Render`'s `RadarControlModule radarModule` parameter (`UI/GridVisualization.cs:47`) is never read inside the method.
- Three signatures lie about a dependency that doesn't exist.

**Action**: drop the `RadarControlModule` parameter from `GridMfdPage` ctor, the `_radar` field, and the `radarModule` parameter on `GridVisualization.Render`. Update the caller at `SystemManager.cs:133`.

### 1.4 `StatusPanelRenderer.Render(..., int tick)` parameter is dead

[UI.md §3, §5; Root_and_Extensions.md §3]

- `int tick` parameter (`UI/StatusPanelRenderer.cs:18`) is never read in the method body. The renderer uses `AnimatedValue` (wall-clock) for everything that used to be tick-driven.
- `SystemManager.cs:143` and `:319` still pass `currentTick`.

**Action**: remove the parameter from `StatusPanelRenderer.Render` and the two call sites.

### 1.5 `SystemManager.MainSurface` getter has no callers

[Root_and_Extensions.md §4 #3]

- `MainSurface => lcdMain` (`SystemManager.cs:138`) — public, zero readers. Comment claims pages need it; no page does.

**Action**: delete the property.

### 1.6 `PAGE_FADE_DURATION` is duplicated and can drift

[Root_and_Extensions.md §5 #14, UI.md §5]

- `SystemManager.cs:329` hardcodes `0.30` for the in-transition window check.
- `UIController.cs:68` defines `PAGE_FADE_DURATION = 0.300` and uses it everywhere else.
- Two sources of truth for one constant. If one changes the menu transition will desync.

**Action**: delete the literal in `SystemManager.cs:329` and reference `UIController.PAGE_FADE_DURATION` (or move the constant to `MFDTheme`).

### 1.7 `TargetingRenderer.DrawLeadingPip` does fire-control side effects

[HUD.md §5.1, §5.11]

- The "renderer" mutates `IMyUserControllableGun.Enabled` based on aim alignment (`HUD/TargetingRenderer.cs:81-99`). Auto-fire snap-shoot logic lives inside the draw call.
- Gatling `Enabled` is also written by `HUDModule.UpdateThrottleControl` (`Modules/HUDModule.cs:600-606`) when `manualfire` is on.
- Two files own the same hardware state, with no documented contract for ordering.

**Action**: move the auto-fire toggle out of the renderer into `GunControlModule` (or a new `MainGunModule`), or document the split-ownership invariant in CLAUDE.md.

---

## 2. Confirmed dead code (delete unless flagged otherwise)

Items confirmed by ≥1 audit; cross-folder confirmations are noted.

### 2.1 Files
| Path | Status | Notes |
|---|---|---|
| `Mdk.PbScript2/Extensions/RandomExtensions.cs` | empty | `namespace IngameScript {}` only [Root_and_Extensions.md §1] |
| `Mdk.PbScript2/Utilities/TerrainAPI.cs` | excluded, dead | superseded by `TerrainData.cs` [Utilities.md §4] |
| `Mdk.PbScript2/Modules/TerrainMapModule.cs` | excluded, dead | references `TerrainAPI` + `HasCustomScreen` (no longer on base) [Modules.md §4] |
| `Mdk.PbScript2/UI/TerrainRenderer.cs` | excluded, dead | depends on `TerrainAPI` [UI.md §4] |
| `Mdk.PbScript2/UI/StartupSequence.cs` | excluded, disabled | boot animation, never enabled [UI.md §4] |
| `Mdk.PbScript2/UI/StatusPanelRenderer.idle-slides.cs` | excluded, would conflict | redefines `StatusPanelRenderer` (same name as live file) and calls `TerrainModule.GetMinimap` (doesn't exist) [UI.md §4] |

These six form one removable bundle. Removing them also removes the `<Compile Remove>` lines from `Mdk.PbScript2.csproj:35-39`.

### 2.2 Dead fields/properties on live types

| Member | Location | Notes |
|---|---|---|
| `Jet._thrusters` | `Jet.cs:16` | populated, never read [Root_and_Extensions.md §4 #2] |
| `Jet.radarControl` | `Jet.cs:101` | see §1.2 |
| `Jet.GameTicks` | `Jet.cs:29` | incremented per tick, no compiled reader [Root_and_Extensions.md §4 #7] |
| `Jet.EnemyContact.IsStale` | `Jet.cs:67` | never queried [Root_and_Extensions.md §4 #5] |
| `Jet.EnemyContact.Matches` | `Jet.cs:73` | never called; `UpdateOrAddEnemy` does the equivalent inline [Root_and_Extensions.md §4 #6] |
| `Jet.GetCockpitMatrix()` | `Jet.cs:487` | callers use `_cockpit.WorldMatrix` directly [Root_and_Extensions.md §4 #4] |
| `SystemManager.MainSurface` | `SystemManager.cs:138` | see §1.5 |
| `UIController.MainScreen` / `ExtraScreen` | `UI/UIController.cs:55-56` | zero callers [UI.md §4] |
| `HUDModule.altitudeHistory` | `Modules/HUDModule.cs:74` | written every tick, never read; smoothed value is what consumers use [Modules.md §4] |
| `HUDModule.currentTrim` | `Modules/HUDModule.cs:64` | scratch variable mistakenly hoisted to a field [Modules.md §4] |
| `HUDModule.MIN_Z_FOR_PROJECTION` | `Modules/HUDModule.cs:138` | unused constant [Modules.md §4] |
| `RadarControlModule.activeThreats` | `Modules/RadarControlModule.cs:96` | public but only consumed in-class — make `private` or surface a `ThreatList` consumer [Modules.md §4] |
| `RadarControlModule.detectedAIPairs` | `Modules/RadarControlModule.cs:17` | populated, never read [Modules.md §5 #13] |
| `RadarTrackingModule.CurrentTime`, `CurrentTick` | `Utilities/RadarTrackingModule.cs:32-33` | public, no external readers [Utilities.md §3] |

### 2.3 Dead methods on live types

| Method | Location | Notes |
|---|---|---|
| `RadarRenderer.DrawDashedCircle` | `HUD/RadarRenderer.cs:228-237` | last reader removed when minimap was rewritten [HUD.md §4.1] |
| `TerrainModule.RenderMinimap` | `Modules/TerrainModule.cs:116` | no callers anywhere; `StatusPanelRenderer.DrawTerrain` calls a different path [Modules.md §4] |
| `Anim.EaseInOut` | `Utilities/Anim.cs:30` | unused [Utilities.md §3] |
| `Anim.Saw` | `Utilities/Anim.cs:46` | unused [Utilities.md §3] |
| `Anim.Pulse` | `Utilities/Anim.cs:38` | only called by `WarnAlpha` — make `private` [Utilities.md §3] |
| `SpriteHelpers.RotatePoint` | `Utilities/SpriteHelpers.cs:107` | unused [Utilities.md §3] |
| `SoundManager.RequestWeapon` | `Utilities/SoundManager.cs:97` | entire weapon channel API is unused after AirtoAir consolidation [Utilities.md §6 #2] |
| `SoundManager.PRIORITY_LOCK`, `PRIORITY_SEARCH` | `Utilities/SoundManager.cs:13-14` | orphan priority constants for unused channel |
| `MissileBayHelper.WriteLaunchSetup` | `Utilities/MissileBayHelper.cs` | only called internally; could be `private` [Utilities.md §3] |
| `MissileBayHelper.TryGetTargetPosition` | same | same |
| `MissileBayHelper.TryGetTargetData` | same | same |
| `MissileBayHelper.ExtractBayNumber` | same | same; also note duplicated logic in `Jet.ExtractBayNumber` (Utilities §5) |
| `MissileBayHelper.ColorToChar` | same | same |
| `MissileBayHelper.FireNextAvailableBay` | same | same |

### 2.4 Dead parameters on live methods

| Method | Parameter | Location |
|---|---|---|
| `InstrumentRenderer.DrawAltitudeIndicatorF18Style` | `TimeSpan currentTime` | `HUD/InstrumentRenderer.cs:148` [HUD.md §5.8] |
| `InstrumentRenderer.DrawLeftInfoBox` | `airspeed`, `pixelsPerDegree` | `HUD/InstrumentRenderer.cs:370-375` [HUD.md §5.9] |
| `GridVisualization.Render` | `RadarControlModule radarModule` | `UI/GridVisualization.cs:47` (see §1.3) |
| `StatusPanelRenderer.Render` | `int tick` | `UI/StatusPanelRenderer.cs:18` (see §1.4) |

### 2.5 Sprite mod constants with no callers (27)

[Utilities.md §4 — full list]

`TEXTURE_TRIANGLE` plus 26 `JetOS_*` constants in `Shortcuts.cs:73-161` covering: pitch zero/inverted rungs, tape bug, gauge face/needle, generic warning, radar sweep, status ring, all 7 module icons, all 3 status-label icons (`fuel/power/ammo`), aircraft symbol, missile heat/radar variants, both background patterns, key-hint box, two of three glyphs.

**Decision needed**: are these "ship the mod, plan to use them" sprites, or "the mod doesn't ship them" leftovers? Cross-check against `Mod/testmod/Data/LCDTextures.sbc`. Either delete the constants or reach the rendering paths they were declared for.

### 2.6 Dead subsystems (built every tick, output never consumed)

| Subsystem | Location | Cost |
|---|---|---|
| `TerrainData` tile-min/max cache | `Utilities/TerrainData.cs` (`_tileMin`/`_tileMax`/`BuildTileChunk`/`TILE_BATCH`/`_tileOfs`/`_tileRows`/`_tileCols`/`_tilesReady`) — exposed as `TileRange`/`TilesReady`/`Gen`/`MeanR`/`Rows`/`Cols`/`SurfRaw`/`W2G`(non-frac) | runs every tick post-download until `_tilesReady`; saves ~2500 instr/tick when removed [Utilities.md §6 #5] |
| Sound weapon channel | `SoundManager.cs` `weaponChannel` (init + tick) | one channel ticked per frame for nothing [Utilities.md §6 #2] |

---

## 3. Architectural / consistency issues

### 3.1 Inconsistent block-discovery filters in `Jet` ctor

[Root_and_Extensions.md §5 #3 — table]

| Block list | Filter |
|---|---|
| `_thrusters`, `_thrustersbackwards`, `_gatlings`, `tanks`, `batteries` | strict `t.CubeGrid == _cockpit.CubeGrid` |
| `_bays`, `leftstab`, `rightstab` | `IsSameConstructAs(_cockpit)` |

Engines/gatlings/tanks/batteries on a sub-grid (rotor / hinge / merge mount) silently disappear from JetOS while bays and stabilizers continue to work. This is the same family of geometry assumption as the engine-detection-when-seated bug discussed earlier in the session.

**Action**: pick one filter and apply it consistently across all per-grid lists. `IsSameConstructAs` is the more permissive choice and matches the bays/stabs already. The strict `CubeGrid ==` filter for thrusters is currently coupled to the `Position.X` engine grouping (see §3.2); changing the filter without addressing the grouping invariant breaks left/right split for sub-grid engines.

### 3.2 Engine L/R grouping uses raw grid-X coordinates

[Root_and_Extensions.md §5 #4]

`Jet.cs:158-181` splits engines using `t.Position.X` vs `_cockpit.Position.X`. `Position` is grid-cell coordinates (`Vector3I`), not cockpit-relative. If the cockpit is mounted rotated relative to the grid axes, "left/right" silently mis-classifies. Combined with `GridThrustDirection == Vector3I.Backward` (`Jet.cs:156`) — which is grid-relative, not cockpit-relative — this is the family that produces the "must be seated to detect engines" symptom: in a fully-cold init both filters can return Vector3I.Zero / wrong-axis values.

**Action**: replace with cockpit-orientation-aware grouping, e.g. `Vector3D.Dot(t.GetPosition() - _cockpit.GetPosition(), _cockpit.WorldMatrix.Right)` for the L/R sign, and `Base6Directions.GetFlippedDirection(t.Orientation.Forward) == _cockpit.Orientation.Forward` for the engine direction filter (see `docs/se-scripting-oddities.md §12`). Also defer engine discovery to the first `Tick()` rather than the constructor so block components have time to register.

### 3.3 Two parallel clocks in `HUDModule`

[Modules.md §5 #1, #2]

- `HUDModule.deltaTime` (`Modules/HUDModule.cs:485`) reads `Runtime.TimeSinceLastRun.TotalSeconds` directly, with `0.0167` fallback. Used for throttle ramp, AB-gate timer, smoothing, accel computation.
- `HUDModule.totalElapsedTime` (`Modules/HUDModule.cs:118`) accumulates `Runtime.TimeSinceLastRun` separately. `WeaponScreenRenderer` keys missile TOF off this clock (`HUD/WeaponScreenRenderer.cs:343,348`).
- Every other module uses `SystemManager.DeltaSeconds` / `ElapsedSeconds`. CLAUDE.md mandates wall-clock only.

**Action**: replace `HUDModule.deltaTime` with `SystemManager.DeltaSeconds`, and `HUDModule.totalElapsedTime` with `SystemManager.ElapsedSeconds` (or `Jet.GameSeconds`, which mirrors it). Remove both fields.

### 3.4 `ConfigurationModule` bypasses `CustomDataManager`

[Modules.md §5 #3]

- `ConfigurationModule.cs:138-188` parses and rewrites `Me.CustomData` directly with `string.Split('\n')`, then calls `MarkCustomDataDirty()` to force the manager cache to re-parse.
- Already noted in `optimisations/12-customdata-config-bypass.md`. CLAUDE.md says all reads/writes go through `SystemManager.GetCustomDataValue`/`SetCustomDataValue`.

**Action**: route the per-key reads/writes through `CustomDataManager` for the same lazy-parse benefit other consumers get.

### 3.5 `CanardModule.gain` and `coupling` are not persisted

[Modules.md §5 #5]

- Pilot can adjust them in the menu (`Gain+/-`, `Coupling+/-`) but the values live as instance fields. PB recompile or NRE-recovery resets them.
- Other tunables route through `ConfigurationModule`, which persists in CustomData.

**Action**: move both into the configuration system, or save/load them on init.

### 3.6 No critical-red token in `MFDTheme`

[UI.md §5, §6 #2]

- `Cr(180, 50, 40)` is reinvented 11 times in `GridVisualization.cs` and once in `StatusPanelRenderer.cs` (and several more times in HUD code). `MFDTheme.WARN` is amber, not red.

**Action**: add `MFDTheme.DANGER` (or `CRITICAL`) at `UI/UIController.cs:38`. Replace inline literals.

### 3.7 Hardcoded constants that probably belong in `ConfigurationModule`

[Modules.md §5 #5, HUD.md §4.8, Root_and_Extensions.md §5 #13-15]

Already-tunable behaviours pinned to `internal const` in source:
- `HUDModule`: `THROTTLE_RATE = 0.6f`, `HYDROGEN_HYSTERESIS = 0.02f`, `AB_AUTO_ENGAGE_SECONDS = 0.667f`, `STALL_AOA = 28.0`, `SMOOTHING_WINDOW_SIZE = 10`, `OPTIMAL_AOA_MIN/MAX` (in `InstrumentRenderer.cs`).
- `GunControlModule`: `MAX_ANGLE_DEG = 15f`, `INTERCEPT_ITERATIONS = 6`, `KD_LOS = 1.0f`.
- `RadarControlModule`: `LOST_TARGET_TIMEOUT_SECONDS = 2.0`, RWR sample rates.
- `RadarRenderer`: range padding/lookahead/min/max constants.
- `SystemManager`: hysteresis dead-bands `20 kts` / `40 m`, EMA divisor `60`.

Pick a tier of "definitely tunable in flight" vs "dev knob"; promote the first tier to `ConfigurationModule`.

### 3.8 Inconsistent text/rect helper dialects

[UI.md §5]

Three files define their own `Rect`/`Txt` two-line wrappers (UIController, MFDFrame, StatusPanelRenderer); GridVisualization opts out and calls `SpriteHelpers.Bx`/`Tt` directly. Only `MFDFrame.Rect`/`Txt` has cross-folder callers.

**Action**: keep `MFDFrame.Rect/Txt` as the public surface; make the local copies in UIController/StatusPanelRenderer call through to `MFDFrame`.

### 3.9 `SpriteHelpers.Bx`/`Sp`/`Tt` are pass-throughs that ignore their `frame` param

[Utilities.md §5]

- `SpriteHelpers.Bx(frame, ...)` simply delegates to `Sq(...)`. The `frame` parameter is dead weight maintained for argument-shape uniformity.
- 70+ callsites across HUD/UI/Modules pay the parameter cost.

**Decision**: keep for API uniformity, OR drop the param and have callers use `Sq`/`Tx`/`SqT` directly (saves minified output but is a wide change).

### 3.10 Naming inconsistencies

[Modules.md §5 #14, Utilities.md §5]

- `AirtoAir` should be `AirToAir` and `AirToAirModule`. Every other module ends with `Module`.
- `RadarTrackingModule` is in `Utilities/`, not `Modules/`, and is not a `ProgramModule`. The "Module" suffix is misleading.
- `RadarTrackingModule.L_CombatBLock` has a typo (`Block` is missing the `c`); the typo propagates to `RadarControlModule` consumers.

### 3.11 `MissileBayHelper` public surface wider than callers need

[Utilities.md §6 #3]

Only 7 of 13 public members have external callers. Tighten 6 to `private`/`internal` (see §2.3 list). The IGC contract (`IGC_CHANNEL_PREFIX`, `BroadcastTargetUpdates`) stays public — it is the missile-script-side contract.

---

## 4. Documentation drift

[Root_and_Extensions.md §6, Utilities.md §6 #4]

- `Mdk.PbScript2/Utilities/README.md` — describes `PIDController`, `Player`, `Obstacle`, `Vector2I` (none exist) and calls `RadarTrackingModule` "Deprecated" while it is actively used. **Rewrite**.
- `CLAUDE.md` — says SoundManager uses `Jet.GameTicks` for `FRAME_DELAY`. SoundManager actually uses its own `_frame` counter. Update the "Timing" section if `Jet.GameTicks` is kept, or remove the field and the doc together.

`Mdk.PbScript2/UI/README.md` was rewritten earlier in this session and is current.

---

## 5. Off-by-one / ordering observations (record, do not necessarily act)

| Observation | Source |
|---|---|
| MFD chrome displays last-tick `IC/IP/IA` because `uiController.Render` runs at `:331-333` before `Jet.IC` is captured at `:285-287`. By design for perf counters but worth knowing. | [Root_and_Extensions.md §5 #20] |
| `HandleSpecialFunctionInputs` runs after all module ticks (`SystemManager.cs:278`); a HandleSpecialFunction that mutates state has a one-tick render delay. | [Root_and_Extensions.md §5 #9] |
| `_pendingArgument` consumed at `:189-193` — toolbar press is processed in tick N+1 (matches the documented double-Main guard). | [Root_and_Extensions.md §5 #10] |
| NRE recovery in `Program.cs:33-40` re-runs `Initialize` but leaves `_lastModule`, `_mainTransitionStart`, `_pendingArgument`, `currentTick` intact across the boundary. | [Root_and_Extensions.md §4] |

---

## 6. Suggested action plan

Order is by blast radius (low first) so each step is independently reviewable.

### Tier 1 — pure deletions (no behaviour change)

1. Delete the 6 excluded files (§2.1) and the matching `<Compile Remove>` lines in `Mdk.PbScript2.csproj`.
2. Delete dead members in §2.2 (fields/properties on live types).
3. Delete dead methods in §2.3.
4. Delete dead parameters in §2.4 + their pass sites.
5. Delete the `TerrainData` tile-min/max subsystem (§2.6).
6. Delete the `SoundManager` weapon channel (§2.6, §2.3) — confirm no plan to revive AIM9 lock/search tones first.
7. Audit `Mod/testmod/Data/LCDTextures.sbc` against the unused `TEX_*` list in §2.5; delete constants the mod doesn't ship; keep+document constants the mod ships but JetOS has not yet rendered.

### Tier 2 — broken contracts (small behaviour change, mostly cleanup)

8. Resolve `HUDModule.activeMissiles` (§1.1) — either wire to `MissileBayHelper.FireSelectedBays` or delete the panel.
9. Remove `Jet.radarControl` (§1.2).
10. Remove the `RadarControlModule` plumbing through `GridMfdPage` → `GridVisualization.Render` (§1.3).
11. Remove the `int tick` parameter from `StatusPanelRenderer.Render` (§1.4).
12. Remove `SystemManager.MainSurface` (§1.5).
13. Reference `UIController.PAGE_FADE_DURATION` from `SystemManager.cs:329` instead of duplicating `0.30` (§1.6).
14. Move auto-fire toggle out of `TargetingRenderer.DrawLeadingPip` into `GunControlModule` (§1.7).

### Tier 3 — consistency refactors

15. Unify the block-discovery filters in `Jet` ctor on `IsSameConstructAs(_cockpit)` (§3.1) — careful with §3.2 dependency.
16. Replace the engine L/R grouping with cockpit-orientation-aware classification, and defer block discovery from ctor to first `Tick()` (§3.2). Validates / fixes the engine-detection-when-seated symptom.
17. Replace `HUDModule.deltaTime` and `HUDModule.totalElapsedTime` with `SystemManager.DeltaSeconds`/`ElapsedSeconds` (§3.3).
18. Route `ConfigurationModule` reads/writes through `CustomDataManager` (§3.4).
19. Persist `CanardModule.gain`/`coupling` to CustomData (§3.5).
20. Add `MFDTheme.DANGER` and replace `Cr(180,50,40)` reinventions (§3.6).
21. Promote the "definitely tunable in flight" subset of constants (§3.7) to `ConfigurationModule`.

### Tier 4 — naming + docs (low-priority but clarifies the model)

22. Fix `L_CombatBLock` typo (§3.10).
23. Rename `AirtoAir` → `AirToAirModule` and `RadarTrackingModule` → `RadarTracker` (§3.10).
24. Rewrite `Utilities/README.md` to match reality (§4).
25. Update `CLAUDE.md` "Timing" section once `Jet.GameTicks` is removed or its real consumer is identified (§4).

---

## 7. What this audit did NOT cover

- Dynamic / runtime-only failure modes (race conditions in IGC, mod-API absence handling beyond what's already audited, performance under sustained 50K-instruction pressure).
- The `Diagnostics/` folder contents (excluded from build per CLAUDE.md, scope-skipped by every agent).
- The `Mod/testmod/` mod assets themselves — the audit treats them as a black box providing the `JetOS_*` sprite atlas and the `TerrainAPI` mod property.
- The `decompiled_dlls/` SE source references used by CLAUDE.md.
- `Tools/`, `optimisations/`, `docs/interactive/` — not part of the build.

If any of these are needed, queue a follow-up audit pass.
