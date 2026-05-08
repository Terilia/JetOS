# HUD Folder Audit — `Mdk.PbScript2/HUD/`

Read-only audit of the five renderer files that extend `partial class HUDModule`. The class itself is declared in `Mdk.PbScript2/Modules/HUDModule.cs`; this folder contributes draw methods only (no constructors, no module-interface overrides).

Files in scope:
- `HorizonRenderer.cs`
- `InstrumentRenderer.cs`
- `RadarRenderer.cs`
- `TargetingRenderer.cs`
- `WeaponScreenRenderer.cs`

---

## 1. Types defined in this folder

All five files declare `partial class Program { partial class HUDModule { … } }` (no nested types beyond the inner structs noted below). They do **not** introduce new top-level types — every member becomes part of `HUDModule`.

### `HorizonRenderer.cs`
- Field: `private List<MySprite> _horizonSprites` (l.13) — pre-allocated buffer used by `DrawArtificialHorizon` for the rotation pass.
- Methods:
  - `private void DrawArtificialHorizon(...)` (l.15)
  - `private void DrawAircraftSymbol(...)` (l.99)
  - `private void DrawBankAngleMarkers(...)` (l.109)
  - `private void DrawFlightPathMarker(...)` (l.125)

### `InstrumentRenderer.cs`
- Methods:
  - `private void DrawSpeedIndicatorF18StyleKph(...)` (l.14)
  - `private void DrawCompass(...)` (l.89)
  - `private string GetCompassDirection(double heading)` (l.136)
  - `private void DrawAltitudeIndicatorF18Style(...)` (l.148)
  - `private void DrawGForceIndicator(...)` (l.222)
  - `private void DrawAOAIndexer(...)` (l.235)
  - `private void DrawStallWarning(...)` (l.317)
  - `private void DrawLeftInfoBox(... params LabelValue[] extraValues)` (l.370)
  - `private void DrawFlightInfo(MySpriteDrawFrame, double throttle)` (l.401)

### `RadarRenderer.cs`
- Fields:
  - `private float smoothedRadarRange = 5000f` (l.14)
  - `private const float RADAR_RANGE_SMOOTH = 0.1f` (l.15)
  - `private const float RADAR_MIN_RANGE = 2000f` (l.16)
  - `private const float RADAR_MAX_RANGE = 15000f` (l.17)
  - `private const float RADAR_RANGE_PADDING = 1.3f` (l.18)
  - `private const float RADAR_SPEED_LOOKAHEAD_SEC = 25f` (l.19)
  - `private RadarContact[] _radarBuf = new RadarContact[16]` (l.30)
  - `private List<Vector3D> _wingmanPositionBuffer` (l.240)
- Inner type: `private struct RadarContact { Vector3D ToTarget; float Distance, DotRight, DotForward; }` (l.23-29)
- Methods:
  - `private void DrawRadarMinimap(MySpriteDrawFrame, IMyCockpit, IMyTextSurface)` (l.32)
  - `private static float RoundToNiceRange(float)` (l.220)
  - `private static void DrawDashedCircle(MySpriteDrawFrame, Vector2, float, Color)` (l.228)
  - `private void DrawFormationGhosts(MySpriteDrawFrame, IMyTextSurface, MatrixD)` (l.242)

### `TargetingRenderer.cs`
- Methods:
  - `private void DrawLeadingPip(...)` (l.12)
  - `private void DrawTargetBrackets(...)` (l.175)
  - `private void DrawGunFunnel(...)` (l.246)
  - `private void DrawBreakawayWarning(...)` (l.291)

### `WeaponScreenRenderer.cs`
- Fields:
  - `private bool _wasLocked` (l.15)
  - `private double _lockAcquiredAt` (l.16)
  - `private const double LOCK_FLASH_DURATION = 0.20` (l.17)
- Methods:
  - `internal void RenderWeaponContent(MySpriteDrawFrame, RectangleF, Vector2)` (l.21) — called by `WeaponMfdPage`
  - `private static void DrawWpnSectionTitle(...)` (l.106)
  - `private void DrawSelectedTargetDetail(...)` (l.126)
  - `private void DrawTrackingTimeline(...)` (l.204)
  - `private void DrawEnemyList(...)` (l.242)
  - `private bool IsContactSelected(...)` (l.298)
  - `private double CalculateBearingToTarget(...)` (l.304)
  - `private void DrawMissileTOFToScreen(...)` (l.336)
  - `private void DrawBayStrip(MySpriteDrawFrame, float, float, List<IMyShipMergeBlock>)` (l.364)
  - `private void DrawGunControlOverlay(MySpriteDrawFrame)` (l.381)
  - `private void DrawTurretIndicator(...)` (l.422)

---

## 2. Inputs (what HUD reads/calls from elsewhere)

### From `HUDModule.cs` (the same class — declared in `Modules/HUDModule.cs`)
Renderers read instance state declared in the parent file. Visibility is implicit (same class):

| Member (declared in HUDModule.cs) | Read by |
|---|---|
| `cockpit` (`HUDModule.cs:50`) | RadarRenderer.cs:32-33, WeaponScreenRenderer.cs:23, TargetingRenderer.cs:185 (param), 304 |
| `hud` (`HUDModule.cs:51`) | InstrumentRenderer.cs:28, 91, 153, 168, 229, 232, 238, 320, 366; RadarRenderer.cs:33, 36; TargetingRenderer.cs:309-310; WeaponScreenRenderer.cs:387 |
| `radarControl` (`HUDModule.cs:60`) | RadarRenderer.cs:139, TargetingRenderer.cs:226, WeaponScreenRenderer.cs:28, 147 |
| `myjet` (`HUDModule.cs:59`) | RadarRenderer.cs:59, 90, 149, 263; TargetingRenderer.cs:83-96; WeaponScreenRenderer.cs:51, 69, 81, 88, 269, 308 |
| `verticalVelocityMps` | InstrumentRenderer.cs:151, TargetingRenderer.cs:293 |
| `mach` | InstrumentRenderer.cs:81 |
| `activeMissiles` (List<MissileTrackingData>) | WeaponScreenRenderer.cs:82, 92, 249, 338, 343, 345, 347 |
| `totalElapsedTime` | InstrumentRenderer.cs:148 (param), WeaponScreenRenderer.cs:343, 348 |
| `deltaTime` | (used in HUDModule.cs:319, 341 — feeds renderers via param) |
| `HUD_PRIMARY/SECONDARY/HORIZON/EMPHASIS/WARNING/INFO/RADAR_FRIENDLY` (static accessors, `HUDModule.cs:30-36`) | All five renderers — sole color source besides `Cr(...)` |
| `MIN_Z_FOR_PROJECTION`, `INTERCEPT_ITERATIONS`, `STALL_*`, `TAPE_*`, `SPEED_*`, `ALTITUDE_*`, `TICK_*`, `MAJOR_TICK_INTERVAL`, `THROTTLE_HYDROGEN_THRESHOLD`, `RADAR_BOX_SIZE_PX`, `RADAR_BORDER_MARGIN`, `INFO_BOX_Y_OFFSET_FACTOR`, `FONT` | constants declared in HUDModule.cs:138-160 used across renderers |
| `LabelValue` struct | InstrumentRenderer.cs:376 (DrawLeftInfoBox parameter) |

### From root (`Program.cs`, `Jet.cs`, `SystemManager.cs`)

| Member | Consumer |
|---|---|
| `Jet.CachedGravity` | RadarRenderer.cs:59, WeaponScreenRenderer.cs:308 |
| `Jet.enemyList` (public field, `List<EnemyContact>`) | RadarRenderer.cs:90 |
| `Jet.GetSelectedEnemy()` | RadarRenderer.cs:149, WeaponScreenRenderer.cs:51 |
| `Jet.GetEnemiesSortedByDistance()` | WeaponScreenRenderer.cs:69 |
| `Jet.GetEnemyContactColor(EnemyContact)` | WeaponScreenRenderer.cs:269 |
| `Jet._bays` (`List<IMyShipMergeBlock>`) | WeaponScreenRenderer.cs:81-88 |
| `Jet._gatlings` | TargetingRenderer.cs:83-96 (HUD writes `.Enabled` for auto-fire) |
| `Jet.manualfire` | TargetingRenderer.cs:91 |
| `Jet.EnemyContact` (struct + `.Position/.Velocity/.Name/.SourceIndex/.IsStale/.GetDisplayHistory()/.Matches()`) | WeaponScreenRenderer.cs:126, 204, 242, 298 |
| `SystemManager.TryGetCustomDataValue(string, out string)` | RadarRenderer.cs:251 (Wingman1..4 GPS) |
| `SystemManager.ElapsedSeconds` | WeaponScreenRenderer.cs:29, 142 |
| `SystemManager.GetGunControl()` | WeaponScreenRenderer.cs:383 |

### From `Modules/`
- `RadarControlModule.IsTrackLocked` (held in `radarControl` field) — RadarRenderer.cs:139, TargetingRenderer.cs:226, WeaponScreenRenderer.cs:28, 147
- `GunControlModule.IsControlEnabled / IsLeftTracking / IsRightTracking` via `SystemManager.GetGunControl()` — WeaponScreenRenderer.cs:384, 398, 401, 403, 412

### From `Utilities/`
- `Shortcuts.cs` aliases (heavy use across all five files): `V2 SS SX SY VN VD VX VDi VTN GP LV WM WF WR WU Sn Cs As At2 Rd Sg Cl Mn Mx Ab ToDeg ToRad Cr VZ PI` plus all `TEX_*` / `TEXTURE_*` string constants.
- `SpriteHelpers.Bx Tt Sp DrawRectangleOutline DrawCircleOutline AddLineSprite ProjectToScreen FormatRange FBx FTt CIRC_SEGS CCos CSin` — ubiquitous.
- `Anim.Blink(double seconds)` — wall-clock blink helper:
  - InstrumentRenderer.cs:339 (HIGH AOA at 0.33s), 345 (STALL at 0.17s)
  - TargetingRenderer.cs:226 (lock diamond at 0.27s), 313 (PULL UP / BREAK AWAY at 0.33s)
  - WeaponScreenRenderer.cs:407 (FIRE flash at 0.17s)
- `Anim.LerpColor / Anim.EaseOut` — WeaponScreenRenderer.cs:143 (lock-acquired flash)
- `NavigationHelper.TryParseGps` — RadarRenderer.cs:254
- `MissileBayHelper.IsBayReady` — WeaponScreenRenderer.cs:373

`BallisticsCalculator` is **not** called from this folder — interception is computed in `HUDModule.RenderHUD` (Modules/HUDModule.cs:348) and the result is passed in.

### From `UI/`
- `MFDTheme.AC AL AR TX TT FONT FONT_W ACCENT BORDER BORDER_LIGHT PANEL_BG SEL_FILL DIM_TEXT DIM_TEXT_MID MID_TEXT BRIGHT_TEXT WARN STATUS_RDY STATUS_VAL BAR_TRACK` — used by WeaponScreenRenderer (the only renderer drawing into the MFD-themed surface) and as text-alignment shorthand throughout the HUD-glass renderers.
- `MFDFrame.Rect / MFDFrame.Txt` — WeaponScreenRenderer only (these route through `SpriteBus`).
- `SpriteBus` — only referenced from `Modules/HUDModule.cs:294/377` (in `RenderHUD`). Renderers in this folder pass the raw `frame` to `SpriteHelpers.*`, which themselves go through `SpriteBus.Add`.
- `WeaponMfdPage.RenderContent` calls back into `RenderWeaponContent` (`UI/WeaponMfdPage.cs:19`).

### From SE API
- `IMyCockpit.MoveIndicator` — only in HUDModule.cs (not this folder).
- `IMyTextSurface.SurfaceSize` (via `SS/SX/SY` shortcuts) — every renderer.
- `IMyShipMergeBlock` (parameter to `DrawBayStrip`) — passed in from `myjet._bays`.
- `MySprite.Position/Type/RotationOrScale` getters/setters — HorizonRenderer.cs:72-86 (rotation pass).
- `MyDetectedEntityInfo.Velocity` — **not used directly here**; HUD reads pre-shifted `Jet.EnemyContact.Velocity` (Vector3D), so the `Vector3` typing gotcha doesn't apply.
- `MatrixD.Transpose` — only in HUDModule.cs:288 (passed in).
- No `MatrixD.Left` setter use — safe.

---

## 3. Outputs (what HUD exposes to callers)

`partial class HUDModule` is the only externally visible type. The HUD folder adds **one** member that's called from outside the class:

| Member (file:line) | Caller(s) | Purpose | Status |
|---|---|---|---|
| `internal void RenderWeaponContent(MySpriteDrawFrame, RectangleF, Vector2)` (`HUD/WeaponScreenRenderer.cs:21`) | `UI/WeaponMfdPage.cs:19` (the only caller) | Renders surface-2 weapons MFD content inside chrome supplied by `WeaponMfdPage`. | **In use** |

Every other method in the folder is `private`. Every field is `private`. No public/internal field added by the HUD folder is read externally.

(For completeness, the surrounding `HUDModule` exports — `smoothedAoA/Velocity/Altitude/GForces`, `peakGForce`, `mach`, `throttlePercent`, `Tick()` — are declared in `Modules/HUDModule.cs`, not in this folder, so they fall outside this audit's scope. They are consumed by `SystemManager`, `WeaponMfdPage`, `GridVisualization`, `StatusPanelRenderer`.)

---

## 4. Dead code findings

### 4.1 Dead private members in this folder
- **`DrawDashedCircle` (RadarRenderer.cs:228-237)** — `private static`, **zero callers** anywhere in the repo. Its precomputed-trig comment ("eliminates 24 sin/cos calls per frame") confirms it was an optimization for an older minimap that no longer uses dashed rings. **DEAD CODE.**
- **`_horizonSprites` reuse in HorizonRenderer.cs:13** — alive (used by `DrawArtificialHorizon`), but see 4.4.
- **`_radarBuf` / `_wingmanPositionBuffer`** — both alive.

### 4.2 Direct `frame.Add` (bypasses SpriteBus)
- `HorizonRenderer.cs:90` — `frame.Add(sprite)` inside the rotation pass loop. The horizon's pitch ladder is built into a temporary `_horizonSprites` list, rotated/translated for roll, then emitted. The HUD glass surface explicitly opens its own `DrawFrame` in `Modules/HUDModule.cs:290-294` and registers it with `SpriteBus.Begin(frame, null)` (no capture target), so direct `frame.Add` here works correctly — but it is the **only** sprite emission in the HUD folder that doesn't go through `SpriteHelpers/SpriteBus`. If transitions or capture ever need to include the pitch ladder, this loop is the lone holdout.

  Note that `SpriteHelpers.FBx`/`FTt` (l.57-58, 62-63) **return** a `MySprite` — the "F" prefix is "factory, no emit" — so those calls *are* intentional non-bus paths. The bypass is real but per the SpriteBus contract it's tolerable for HUD glass.

### 4.3 Tick-based vs wall-clock — all clean
Every blink/flash in the folder uses `Anim.Blink(seconds)` (wall-clock) or `SystemManager.ElapsedSeconds` (lock-acquired flash). **No tick counters.** This matches the CLAUDE.md timing rule.

### 4.4 Per-frame allocation
- **HorizonRenderer.cs:24-25** — uses pre-allocated `_horizonSprites` (good).
- **RadarRenderer.cs:90-103** — pre-allocated `_radarBuf` with growth-on-demand. Good.
- **RadarRenderer.cs:244-260** — pre-allocated `_wingmanPositionBuffer.Clear()` then `Add`. Good.
- **InstrumentRenderer.cs:54** — `Ab(i).ToString()` and `i.ToString()` on each rung iteration; only a handful per frame, but allocates short-lived strings. Minor, unavoidable without an int→string cache.
- **InstrumentRenderer.cs:81** — `$"M {mach:F2}"` per frame.
- **InstrumentRenderer.cs:218** — `vviText` interpolation per frame.
- **InstrumentRenderer.cs:228, 231, 314** — G-force / energy strings per frame.
- **WeaponScreenRenderer.cs:69** — `myjet.GetEnemiesSortedByDistance()` returns a list (per CLAUDE.md it reuses pre-allocated buffers, so cost is amortized; but the call chain is invoked twice on the weapon screen tick: once here and indirectly via the radar minimap on the main HUD using `enemyList` directly — different code paths, OK).
- **WeaponScreenRenderer.cs:343** — `activeMissiles.RemoveAll(...)` allocates a delegate every frame. Minor; unavoidable without a manual loop.
- **TargetingRenderer.cs:107, 113, 168, 235, 239, 242** — string interpolations per frame for TTI/range/closure/aspect.

These are all "format-only" strings or trivially small allocations. No collection allocations per frame other than the lambda in `RemoveAll`.

### 4.5 Recomputed-per-frame values
- **TargetingRenderer.cs:36-50** — `DrawLeadingPip` computes `surfaceSize = SS(hud)` and `viewportMinDim`, but `HUDModule.RenderHUD` already cached these into `hudCenter`/`viewportMinDim` (`Modules/HUDModule.cs:278-279`). The renderer ignores the cached values and recomputes locally. Same applies in `DrawTargetBrackets` (l.206), `DrawGunFunnel` (l.258-259), `DrawBreakawayWarning` (l.309-310), `RenderWeaponContent` is given `surfaceSize` already. The `hudCenter`/`viewportMinDim` cached fields exist but **are only used inside HUDModule.cs** — not by this folder. Mild code smell; the renderers were probably written before the cache was added.

### 4.6 Commented-out blocks > 3 lines
None. The folder is comment-clean (only doc comments + design notes).

### 4.7 TODOs / leftover diagnostics
None found.

### 4.8 Ad-hoc tuning constants
Numerous magic numbers exist as inline `const float` or `const double` blocks scoped to a single method. These match HUD design and are not configurable; calling them out:
- `HorizonRenderer.cs:101-102` — `SPRITE_W/H = 102f` aircraft symbol.
- `HorizonRenderer.cs:137` — `FpmDrawSize = 48f`.
- `InstrumentRenderer.cs:17` — `PIXELS_PER_SPEED_UNIT` derived constant (good).
- `InstrumentRenderer.cs:241-242` — `OPTIMAL_AOA_MIN/MAX = 8.0/15.0` — not a config key (could arguably be moved to `ConfigurationModule`).
- `InstrumentRenderer.cs:260` — speed-factor stall reduction floor `100`.
- `RadarRenderer.cs:14-19` — radar range smoothing/padding/lookahead — would fit `ConfigurationModule` if the user wants to tune them in-flight.
- `TargetingRenderer.cs:28-31` — pip scaling distance bracket.
- `TargetingRenderer.cs:283` — gun-funnel cue range `2500` and `1500`.
- `TargetingRenderer.cs:293` — breakaway altitude `100m` and VVI `-5 m/s`.
- `TargetingRenderer.cs:303` — collision warning `range < 500 && closure > 100`.
- `WeaponScreenRenderer.cs:17` — `LOCK_FLASH_DURATION = 0.20`.

---

## 5. Odd code findings

### 5.1 Side effects in a renderer
**TargetingRenderer.cs:81-99** — `DrawLeadingPip` is named like a draw function but **mutates `IMyUserControllableGun.Enabled`** every frame: when the boresight overlaps the lead pip, it enables every gatling; otherwise it disables them (unless `manualfire` is true). This is fire-control logic embedded in the rendering pass. CLAUDE.md says guns route through `GunControlModule`, so this is the legacy "snap-shoot" auto-fire on the main gun (separate from the turrets in `GunControlModule`). Worth flagging — searching for a bug here from rendering would not be obvious.

### 5.2 Hardcoded values that exist as config keys
- `MFDTheme.AC/AL/AR/FONT_W` are accessed correctly. `font_w` etc. are not configurable.
- `INTERCEPT_ITERATIONS = 10` is a HUDModule constant (not in renderers).
- HUD feature toggles (`hud_radar`, `hud_compass`, `hud_gforce`, `hud_aoa`, `hud_fpm`, `hud_gun_funnel`, `hud_target_brackets`, `hud_breakaway`, `hud_theme`) are all gated in `Modules/HUDModule.cs:RenderHUD` and **not** in the renderers themselves — that's the right place. **No config-key bypass detected in this folder.**

### 5.3 Sprites referenced by string literal
Searched all five files for raw `"Square*"/"Circle*"/"Triangle"` literals — none. Every sprite is referenced via either a `TEX_*` constant from `Shortcuts.cs` or one of the legacy `TEXTURE_*` constants (`TEXTURE_FPM`, `TEXTURE_TRIANGLE`, `TEXTURE_CIRCLE_SOLID`). Clean.

### 5.4 Patterns contradicting CLAUDE.md
- **None for tick counters** — all blinks use `Anim.Blink(seconds)`.
- **`SystemManager.ElapsedSeconds`** (wall-clock) is used correctly for the lock-acquired flash.
- **Spawn-delay compensation** is performed in `Modules/HUDModule.cs:340-341` *before* calling `DrawLeadingPip`, matching the contract in CLAUDE.md "guns and pip stay aligned." Good.

### 5.5 Duplicate / redundant work across renderers (same tick)
- `MatrixD.Transpose(WM(cockpit))` is computed once in `RenderHUD` (`Modules/HUDModule.cs:288`) and passed in — good.
- **However**, `RadarRenderer.cs:54` does its own `MatrixD.Transpose(WM(cockpit))` because `DrawRadarMinimap`'s signature takes `(IMyCockpit, IMyTextSurface)` rather than the cockpit matrix. Recomputed per frame. Mild redundancy; would be eliminated by passing `worldToCockpitMatrix` like the other targeting renderers.
- `WF(cockpit)` and `myjet.CachedGravity` are read in `RadarRenderer.cs:66` *and* in `WeaponScreenRenderer.cs:308-311` (inside `CalculateBearingToTarget`). Two passes through the same gravity-projection math per frame.
- `myjet.GetSelectedEnemy()` is called in both `RadarRenderer.cs:149` and `WeaponScreenRenderer.cs:51` per frame. Cheap, but a tick-cached selected enemy would let renderers share.

### 5.6 Inconsistent rendering helpers
- **WeaponScreenRenderer** uses `MFDFrame.Rect/Txt` (which routes through `SpriteBus`) for the MFD-themed weapon panel — correct.
- **All other renderers** use `SpriteHelpers.Bx/Tt/Sp` because they paint onto the HUD glass surface, which doesn't participate in MFD page transitions. Both helper families ultimately call `SpriteBus.Add`, so the SpriteBus invariant holds.
- **Exception:** `HorizonRenderer.cs:90` uses raw `frame.Add(sprite)` (see 4.2). This is the lone direct-emit path.

### 5.7 `DrawDashedCircle` is unused
Already noted — dead.

### 5.8 `DrawAltitudeIndicatorF18Style` takes `TimeSpan currentTime` it doesn't use
`InstrumentRenderer.cs:148` — parameter `TimeSpan currentTime` is declared but never referenced inside the method body. Either a leftover from a prior altitude-trend render or a future hook. Caller passes `totalElapsedTime` (`Modules/HUDModule.cs:313`), but the value is dropped. Minor dead parameter.

### 5.9 `DrawLeftInfoBox` `pixelsPerDegree` parameter is unused
`InstrumentRenderer.cs:370-398` — `pixelsPerDegree` and `airspeed` are declared but neither is read. Caller (`Modules/HUDModule.cs:308`) passes both. Unused parameters; the function only renders the `extraValues` array.

### 5.10 `_horizonSprites` rotation modifies the list **and** emits
`HorizonRenderer.cs:69-91` — the loop both writes `sprites[s] = sprite` (line 88) and calls `frame.Add(sprite)` (line 90). The write-back is unnecessary because the list is `Clear()`-ed at the top of every call (l.25) and never read after the loop. Harmless but redundant.

### 5.11 `manualFire` cross-module write
`TargetingRenderer.cs:85-86, 96` writes `myjet._gatlings[i].Enabled = true/false`. The same gatlings are also touched by `Modules/HUDModule.cs:600-602` inside `UpdateThrottleControl` when `myjet.manualfire` is true. Two places enable/disable the same blocks per tick depending on aim/manual state. Not necessarily a bug (ordering is fine — render runs after throttle update), but the contract "who owns gatling Enabled" is split across two files.

### 5.12 Removed/renamed members
None observed — `radarTracker` (removed per memory) and `_aiFlightBlock` (removed per memory) are not referenced. `myjet.radarControl` is used via the local field `radarControl`, which mirrors the new naming.

---

## 6. Notes for the cross-folder consolidation

1. **One genuine dead method**: `DrawDashedCircle` in `RadarRenderer.cs:228-237` should be flagged for removal in the global dead-code summary — last reader was deleted with the old minimap, the precomputed-trig comment is now misleading.
2. **`DrawLeadingPip` is a fire-control function disguised as a renderer** — it sets `IMyUserControllableGun.Enabled` based on aim alignment. The summary should call out the split-ownership of gatling state between `TargetingRenderer` (auto-fire when aimed) and `HUDModule.UpdateThrottleControl` (manual fire). Either consolidate into `GunControlModule` or document the contract clearly.
3. **Per-frame redundant computations to flag**: (a) `worldToCockpitMatrix` is recomputed in `RadarRenderer.DrawRadarMinimap` despite being available; (b) `hudCenter`/`viewportMinDim` are cached on `HUDModule` but no renderer in this folder reads them — every renderer recomputes `SS(hud)/2f` and `Mn(SX,SY)` locally. Two minor wins toward the SE 50K-instruction budget.
