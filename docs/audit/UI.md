# UI Folder Audit

Scope: `Mdk.PbScript2/UI/*.cs`. Read-only research; CLAUDE.md and `Mdk.PbScript2/UI/README.md` taken as authoritative.

---

## 1. Types defined in this folder

### `MfdPage.cs`
- `MfdPage` (abstract, public, nested in `Program`) — base for any MFD page (chrome metadata + `RenderContent` / `RenderSidebar` virtuals). Defined at `Mdk.PbScript2/UI/MfdPage.cs:11`.

### `UIController.cs`
- `MFDTheme` (static, internal) — palette, font/sprite/alignment constants for every renderer. `Mdk.PbScript2/UI/UIController.cs:12`.
- `UIController` (class, internal) — single-entry-point renderer for one MFD surface; owns selection-tween state and the transition replay. `Mdk.PbScript2/UI/UIController.cs:50`.

### `MFDFrame.cs`
- `MFDFrame` (static, internal) — shared NYINAH CORP chrome (header/footer/corners/border) + `Rect`/`Txt` text-style aliases. `Mdk.PbScript2/UI/MFDFrame.cs:13`.

### `SpriteBus.cs`
- `SpriteBus` (static, internal) — central sprite chokepoint with optional capture list for transition replay. `Mdk.PbScript2/UI/SpriteBus.cs:21`.

### `MenuMfdPage.cs`
- `MenuMfdPage` (class, internal) — default page wrapping a `ProgramModule.GetOptions()`/`name`/`GetHotkeys()`; also handles the main menu via the explicit-items ctor. `Mdk.PbScript2/UI/MenuMfdPage.cs:13`.

### `GridMfdPage.cs`
- `GridMfdPage` (class, internal) — surface-1 chrome wrapper that forwards to `GridVisualization.Render`. `Mdk.PbScript2/UI/GridMfdPage.cs:11`.

### `GridVisualization.cs`
- `GridVisualization` (static, internal) — surface-1 status visualization (3-tick staggered grid outline + cached sprite list, fuel bar, G-meter, flight readouts, missile pips). `Mdk.PbScript2/UI/GridVisualization.cs:12`.

### `WeaponMfdPage.cs`
- `WeaponMfdPage` (class, internal) — surface-2 chrome wrapper; delegates content to `HUDModule.RenderWeaponContent`. `Mdk.PbScript2/UI/WeaponMfdPage.cs:10`.

### `StatusPanelRenderer.cs`
- `StatusPanelRenderer` (static, internal) — sidebar renderer used by main menu and every module page (H2 fuel, battery, dual-engine card, terrain minimap). `Mdk.PbScript2/UI/StatusPanelRenderer.cs:9`.

### `[NOT COMPILED] StartupSequence.cs`
- `StartupSequence` (static, internal) — disabled boot animation. `Mdk.PbScript2/UI/StartupSequence.cs:10`.

### `[NOT COMPILED] TerrainRenderer.cs`
- `TerrainRenderer` (static, internal) — disabled marching-squares contour map (sidebar + fullscreen). `Mdk.PbScript2/UI/TerrainRenderer.cs:11`.

### `[NOT COMPILED] StatusPanelRenderer.idle-slides.cs`
- `StatusPanelRenderer` (static, internal) — alternate sidebar with a 5-slide "tactical idle" deck (sat recon, SIGINT, countdown, exfil, asset). Same type name as the compiled file; cannot be linked simultaneously. `Mdk.PbScript2/UI/StatusPanelRenderer.idle-slides.cs:9`.

---

## 2. Inputs (what this folder reads/calls from elsewhere)

### From root (Program / Jet / SystemManager)
- `SystemManager.ElapsedSeconds` — wall-clock for transition window and selection tween. `Mdk.PbScript2/UI/UIController.cs:130`, `:156`, `:247`.
- `SystemManager.GetConfigValue("bingo_fuel" / "low_fuel")` — fuel-bar thresholds. `Mdk.PbScript2/UI/GridVisualization.cs:39-40`.
- `Jet.IC` / `Jet.IA` / `Jet.IP` — instruction-count counters baked into the chrome header. `Mdk.PbScript2/UI/MFDFrame.cs:54`.
- `Jet._cockpit` / `tanks` / `batteries` / `_bays` / `leftEngines` / `rightEngines` / `leftAB` / `rightAB` / `CachedGravity` — read by status sidebar and grid viz. `StatusPanelRenderer.cs:20,27,36,78-79`, `GridVisualization.cs:82,85,265`.
- `Jet.GetFuelStatus`, `Jet.GetBatteryStatus`, `Jet.GetTotalGunAmmo`, `Jet.GetEngineHealth`, `Jet.GetEngineThrust` — `StatusPanelRenderer.cs:26,35,86-88`, `GridVisualization.cs:249`.
- `Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>` — grid outline collection. `GridVisualization.cs:95`.
- `IMyShipMergeBlock.IsConnected` (bay readiness pip). `GridVisualization.cs:217`.

### From Modules/
- `HUDModule.smoothedVelocity / smoothedAltitude / smoothedAoA / mach / throttlePercent / smoothedGForces / peakGForce` — flight data + G-meter. `GridVisualization.cs:235-244,303-304`.
- `TerrainModule.RenderMinimap(frame, area, jet)` — sidebar terrain minimap. `StatusPanelRenderer.cs:64`. Type-checked guard `SystemManager.currentModule is TerrainModule` at `:62` to avoid double-rendering.
- `TerrainData.Ready` — gating for the terrain-minimap inset. `StatusPanelRenderer.cs:62`.
- `RadarControlModule` is taken as a ctor parameter on `GridMfdPage` and forwarded to `GridVisualization.Render` but **never read inside `Render`** (see Section 4).

### From Utilities/
- `Shortcuts.Sq` / `Tx` (the only sprite emitters) — invoked indirectly via `MFDFrame.Rect`/`Txt`, `UIController.Rect`/`Txt`, and `StatusPanelRenderer.Rect`/`Txt`.
- `SpriteHelpers.Bx`, `Tt`, `FBx`, `Sp`, `DrawCircleOutline`, `DrawRectangleOutline` — `GridVisualization.cs:193,213-228,253-256,277-291,306-330`, `StatusPanelRenderer.cs:60,75,96,103,116,121,125,132`, `MFDFrame.cs:40-43` (`Sp` for corner brackets).
- `MFDTheme` (defined in `UIController.cs` but conceptually "the theme") — heavy reuse across all renderers.
- `Anim.EaseOut`, `Lerp`, `LerpColor`, `WithAlpha`, `WarnAlpha` — `UIController.cs:173,190-191,268,296`, `GridVisualization.cs:237,290`.
- `AnimatedValue` — `GridVisualization.cs:43-44`, `StatusPanelRenderer.cs:12-13`.
- `TEX_*` constants — `TEX_MFD_CORNER` in `MFDFrame.cs:40-43`; `TEX_FUEL_TANK`, `TEX_BATTERY`, `TEX_STATUS_DOT`, `TEX_HATCH` in `StatusPanelRenderer.cs:30,41,103,125`.
- Shortcuts (`PI`, `Mn`, `Mx`, `Ab`, `Cl`, `Cr`, `V2`, `SX`, `SY`, `SS`) — used pervasively.

### From HUD/
- `HUDModule.RenderWeaponContent` — invoked by `WeaponMfdPage.RenderContent`. `Mdk.PbScript2/UI/WeaponMfdPage.cs:19`.

### From SE API
- `IMyTextSurface.DrawFrame()` / `MySpriteDrawFrame.Add` / `Dispose` — `UIController.cs:88,165`, `SpriteBus.cs:37,46`. Direct `frame.Add` only in `GridVisualization.cs:80` (cached-outline replay) and `SpriteBus.cs` itself.
- `IMyTextSurface.ContentType / Script / BackgroundColor / FontColor / FontSize / TextPadding / Alignment` — `UIController.cs:311-320`.
- `MySprite { Type, Data, Position, Size, Color, Alignment, RotationOrScale, FontId }` construction is centralized in `Utilities/Shortcuts.cs` (per-comment policy at `Shortcuts.cs:11-15`); UI compiled code never builds `MySprite` directly. `TerrainRenderer.cs:139-176` and `StatusPanelRenderer.idle-slides.cs:691-694` do build sprites directly but are excluded.
- `RectangleF` / `Vector2` / `Color` / `TextAlignment` / `SpriteType` / `ContentType` — VRageMath / VRage.Game.GUI.TextPanel.

---

## 3. Outputs (what this folder exposes to callers)

- `MfdPage` (`MfdPage.cs:11`) — extended by `MenuMfdPage`, `GridMfdPage`, `WeaponMfdPage` (this folder) and `TerrainModule.TerrainMfdPage` (`Modules/TerrainModule.cs:101`). Returned by `ProgramModule.GetPage()` (`Modules/ProgramModule.cs:20`); consumed by `UIController.Render`.
- `MFDTheme.*` (`UIController.cs:12`) — palette + alignment/sprite-type/font constants. Heavy reuse across all UI/HUD renderers (e.g. `WeaponScreenRenderer.cs`, `RadarRenderer.cs`, `InstrumentRenderer.cs`, `TargetingRenderer.cs`, `HorizonRenderer.cs`, `TerrainModule.cs`, `TerrainMapModule.cs` — see counts in audit traces).
- `MFDTheme.FONT` — only consumed by `Shortcuts.Tx` default param (`Shortcuts.cs:27`). Single consumer.
- `MFDTheme.FONT_W` — used by every HUD renderer for white-text glyphs. Confirmed widely used.
- `UIController(IMyTextSurface, IMyTextSurface)` ctor + `Render(MfdPage, IMyTextSurface, int, double, List<MySprite>, List<MySprite>)` — sole caller `SystemManager.cs:111,331-333`.
- `UIController.MainScreen` / `ExtraScreen` — public read-only properties. Repo-wide search shows zero callers. **UNUSED**.
- `MFDFrame.DrawChrome(frame, sw, sh, headerRight, drawFooterNav, footerRight)` — sole caller `UIController.cs:95`.
- `MFDFrame.ContentBottom(sh)` — callers: `UIController.cs:100`, `Modules/TerrainMapModule.cs:48` (the latter is the excluded alternate terrain page).
- `MFDFrame.Rect(frame, ...)` / `Txt(frame, ...)` — text aliases. Callers: `UIController.cs` (its own private wrappers shadow these), `WeaponScreenRenderer.cs` (HUD), `Modules/TerrainModule.cs`, `Modules/TerrainMapModule.cs` (excluded), `UI/StartupSequence.cs` (excluded), `UI/TerrainRenderer.cs` (excluded). Compiled callers outside this folder: `WeaponScreenRenderer` and `TerrainModule`.
- `SpriteBus.Begin(frame, captureInto)` / `End()` / `Add(sprite)` / `AddRaw(sprite)` — callers: `UIController.cs:89,159,161,165,182,192`, `Modules/HUDModule.cs:294,377`, `Utilities/Shortcuts.cs:17,20,23,26`.
- `MenuMfdPage(ProgramModule)` — `ProgramModule.GetPage` default (`Modules/ProgramModule.cs:20`).
- `MenuMfdPage(string, string[], bool, Action<MySpriteDrawFrame, RectangleF>)` — main-menu ctor. Sole caller `SystemManager.cs:317`.
- `GridMfdPage(Program, Jet, RadarControlModule, HUDModule)` ctor — sole caller `SystemManager.cs:133`. The `RadarControlModule` parameter flows to `GridVisualization.Render` but is dead-end (see Section 4).
- `WeaponMfdPage(HUDModule)` — sole caller `SystemManager.cs:134`.
- `GridVisualization.Render(frame, surfaceSize, contentArea, Program, Jet, RadarControlModule, HUDModule)` — sole caller `GridMfdPage.cs:25`. Last `radarModule` parameter unused inside (see Section 4).
- `StatusPanelRenderer.Render(frame, area, jet, hud, tick)` — callers: `SystemManager.cs:143` and `:319`. The `tick` parameter is **unused** inside the method body (see Section 5).

---

## 4. Dead code findings

### Excluded files (stranded code)
- `UI/StartupSequence.cs` (248 LOC) — the entire `StartupSequence` static class never compiles. Functions: `Tick(jet, vel, arg, m0, m1, m2)`, `Dark`, `Bx`, `Tx`, `WaitScr`, `HudBoot`, `Panel`, `Post`, plus `phase`/`t`/`waitT`/`LEN`/`post[]` state. None reachable.
- `UI/TerrainRenderer.cs` (258 LOC) — `TerrainRenderer.DrawContours`, `Compute`, `Lrp`, `AL`, `JetAxes`, `Render`. Calls `TerrainAPI.*` (also excluded) — superseded by the active `TerrainData` + `Modules/TerrainModule.cs`.
- `UI/StatusPanelRenderer.idle-slides.cs` (~700 LOC) — alternate `StatusPanelRenderer` with `RenderSatRecon`, `RenderSIGINT`, `RenderCountdown`, `RenderExfil`, `RenderAssets` and string tables (`OPS`, `NAMES`, `INTEL_S`, `SIG_FREQ`, `SIG_FRAG`, `CD_NAMES`, `EX_FILES`, `EX_TGTS`). Same type name as the active `StatusPanelRenderer` — would conflict if un-excluded. Imports `TerrainModule.GetMinimap` (which doesn't exist on the live `TerrainModule` — the live one exposes `RenderMinimap` instead) at line 202; even if compiled today, this file would not link.

### Public members of compiled UI files with no callers
- `UIController.MainScreen` (`UIController.cs:55`) — **UNUSED** anywhere in the project.
- `UIController.ExtraScreen` (`UIController.cs:56`) — **UNUSED** anywhere in the project.

### Private/forwarded fields never used
- `GridMfdPage._radar` (`GridMfdPage.cs:15,19,25`) — stored, forwarded into `GridVisualization.Render`, but `Render` never reads its `radarModule` parameter. Whole chain (ctor parameter `RadarControlModule radar` on `GridMfdPage`, member `_radar`, parameter `radarModule` on `GridVisualization.Render`) is dead.
- `GridVisualization.Render` parameter `RadarControlModule radarModule` (`GridVisualization.cs:47`) — **UNUSED** in the method body.
- `StatusPanelRenderer.Render` parameter `int tick` (`StatusPanelRenderer.cs:18`) — **UNUSED**. The compiled file has no tick-driven code (it uses `AnimatedValue` instead). Both call sites still pass `currentTick` (`SystemManager.cs:143,319`).

### Comments / TODOs / debug
- No commented-out code blocks > 3 lines in compiled UI files. No TODO / FIXME markers in compiled UI files. The `TACTICAL SYSTEM <IC>/<IA>/<IP>` text at `MFDFrame.cs:54` is a permanent debug HUD baked into chrome — by design (see CLAUDE.md "Debugging").

### Direct `frame.Add` calls in compiled UI files
- `GridVisualization.cs:80` — `frame.Add(cachedSprites[i])` replaying the cached ship-outline sprite list. Documented exception (CLAUDE.md UI section + `UI/README.md` line 27 + `SpriteBus.cs:18-20`). This surface (extra screen) does not participate in transitions, so non-capture is correct.
- `SpriteBus.cs:37` and `SpriteBus.cs:46` — the bus implementation itself. Required.
- No other direct `frame.Add` in compiled UI files. (`StartupSequence.cs:67`, `TerrainRenderer.cs:56`, `StatusPanelRenderer.idle-slides.cs:204` all live in excluded files.)

---

## 5. Odd code findings

### Inconsistent rect/text helpers
The folder uses three concurrent dialects for the same primitives:
- `UIController.cs` defines its own `Rect` / `Txt` (`UIController.cs:308-309`) that delegate to `Sq` / `Tx` shortcuts.
- `MFDFrame.cs` defines `Rect` / `Txt` (`MFDFrame.cs:99-100`) doing the same delegation.
- `StatusPanelRenderer.cs` defines yet another `Rect` / `Txt` (`StatusPanelRenderer.cs:141-142`) doing the same.
- `GridVisualization.cs` opts out and calls `SpriteHelpers.Bx` / `Tt` directly throughout.
The four duplicate two-line wrappers all minify to the same code; the copy is only stylistic but means a reader hopping between `UIController`, `MFDFrame`, and `StatusPanelRenderer` finds three near-identical helpers. `MFDFrame.Rect`/`Txt` is the only one with cross-folder callers (`WeaponScreenRenderer`, `TerrainModule`); `UIController.Rect`/`Txt` and `StatusPanelRenderer.Rect`/`Txt` are both file-local — they could call `MFDFrame.Rect`/`Txt` directly without their own wrappers.

### Sprite names by string literal vs `TEX_*`
None in compiled UI files (good). `"SquareSimple"` is centralized in `MFDTheme.SQ` (`UIController.cs:41`) and `Shortcuts.TEXTURE_SQUARE`. The excluded `TerrainRenderer.cs:224,242,243,244` uses `"Triangle"` / `"Circle"` literals — should be `TEXTURE_TRIANGLE` / `TEXTURE_CIRCLE_SOLID` if revived.

### Hardcoded colors that should come from `MFDTheme`
- `Cr(180, 50, 40)` (red warning) appears 11 times in `GridVisualization.cs` (lines 186, 226, 237, 242, 245, 250, 275, 313, 328) and once in `StatusPanelRenderer.cs:103` (`MFDTheme.WARN` is amber, not red — there is currently no `MFDTheme.DANGER` / `MFDTheme.CRITICAL`).
- `Cr(120, 20, 20)` (dark red, non-functional block) — `GridVisualization.cs:185`. One-off.
- `Cr(20, 80, 20)` (bay-loaded green) — `GridVisualization.cs:219`. One-off.
- `Cr(80, 110, 200)` (negative-G blue) — `GridVisualization.cs:314`. One-off.
- `Cr(12, 22, 12)` (engine-card "off" fill) — `StatusPanelRenderer.cs:99,106` and `GridVisualization` does not use it. One-off.
- `Cr(2, 3, 2)` (terrain inset background) — `StatusPanelRenderer.cs:59`. One-off.
- `Cr(14, 26, 16)` (terrain inset border) — `StatusPanelRenderer.cs:60`. Looks similar to but distinct from `MFDTheme.BC_BORDER = (16, 26, 16)` and `MFDTheme.BORDER_LIGHT = (20, 30, 20)`.
- `Cr(42, 74, 42)` for "NO TERRAIN" text — `StatusPanelRenderer.cs:66`. Identical to `MFDTheme.DIM_TEXT` and could just be that constant.
- `Cr(0, 0, 0)` extra-screen background — `UIController.cs:65`. Could be `Color.Black`.
The biggest gap is the missing critical-red token; almost every renderer reinvents `(180, 50, 40)`.

### Tick counter for animation timing
- `StatusPanelRenderer.Render` accepts `int tick` (`StatusPanelRenderer.cs:18`) but doesn't use it — vestigial parameter from the older idle-slide implementation.
- `GridVisualization` uses `refreshTick` / `damageCheckCounter` / `rebuildPhase` (`:23-26`) — these are tick-budget gates, NOT animation timing, so wall-clock would be unnatural here.
- The excluded `TerrainRenderer.cs:48` reads `SystemManager.currentTick` for its 15-tick recompute cadence.

### Per-frame allocations
- `UIController.Render`: `new RectangleF(...)` (`:125`), `Vector2 V2(...)` macro builds new struct values. Structs, no GC pressure.
- `UIController.ReplayWithTransform` allocates a new `Color grayC` per sprite (`:189`) — could be inlined since `s` is mutated in place. `Color` is a struct, but the loop runs over up-to-384 captured sprites every transition tick (~18 ticks per fade).
- `GridVisualization.Render`: phase-2 allocates `new bool[gridW, gridH]`, `new float[gridW, gridH]`, `new bool[gridW, gridH]` (`:131-133`) and frees them at end of phase 3 (`:198-200`). Throwaway arrays could be reused across rebuilds (the typical jet has stable W/H), but each rebuild only happens every 60–300 ticks so the cost is tolerable.
- `MenuMfdPage(string, string[], bool, Action<...>)` — the main-menu ctor takes a closure (`SystemManager.cs:318`); the closure captures `panelArea` and `frame` per call. Allocation per render tick. Could be moved to a cached static lambda field on `SystemManager` since the captured state is module-static.
- `StatusPanelRenderer.DrawResCard` builds `string bt` via ternary + `FmtTime` (`:40`) → unavoidable boxing-free string each tick. `$"{(int)(pct * 100)}%"` interpolations throughout — every tick string-allocs about a dozen short strings on each MFD surface. Acceptable but not free.

### Pages reaching into module internals through chains of references
- `WeaponMfdPage` -> `_hud.RenderWeaponContent(...)` (`WeaponMfdPage.cs:19`). Single hop, fine.
- `GridMfdPage._program._program.GridTerminalSystem.GetBlocksOfType<>(...)` — `GridVisualization.cs:95` reaches through the program reference held by `GridMfdPage` to obtain the GridTerminalSystem. Two hops. Could be a one-time `GetBlocksOfType` snapshot (it's already cached via `gridBlocks`), but the rebuild cadence means it's only called once per 60 ticks.
- `StatusPanelRenderer.DrawTerrain` reaches `SystemManager.currentModule is TerrainModule` (`StatusPanelRenderer.cs:63`) and then calls the static `TerrainModule.RenderMinimap`. Module-state coupling — fine in this codebase but worth highlighting.
- `GridMfdPage` carries a `RadarControlModule _radar` that is never read (Section 4). Constructor signature gives a misleading impression that radar drives some grid rendering.

### Layout magic numbers that look like they should be constants
- `MFDFrame.DrawChrome` magic factors `0.069f` (header height), `0.054f` (footer height), `0.03f` (corner length), `0.019f` (pad), `0.00085f`/`0.00069f`/`0.00055f` (title/small/tiny scales), `0.22f` (corp-watermark width), `15f` (top offset), `2f` (border-content gap). All sit as inline literals (`MFDFrame.cs:22-29,46-79`).
- `UIController.Render` magic factors `0.019f` (padX), `0.347f` (sidebar width), `0.020f` (title body padding), `0.045f` (post-title gap), `0.012f` (section-line Y), `0.044f` (breadcrumb height), `0.20f`/`0.23f` (breadcrumb x positions), `0.062f` / `0.079f` / `0.00094f` / `0.00104f` (row heights and text scales), `0.85` (transition cap), `0.35` (radial dispersion factor), `0.7f` (desat factor) — `UIController.cs:102-122,201-244`.
- `GridVisualization.cs`: cached-outline parameters `cs * 5f`, `cs * 2f` (sprite size factors at `:193`), `gL = 55f`, `gR - 40f`, `+ 30f`, `- 30f` (grid bounds at `:165-166`), `<= 0` / `< 500` / `< 2400` ammo thresholds at `:250-254`, `+ 100f` / `- 40f` G-meter top/bottom at `:299-300`, fuel-time multiplier `* 600` at `:286`.
- `StatusPanelRenderer.cs`: `resH = 46f`, `engH = 90f`, `gap = 6f`, `+ 4f` icon padding, `+ 30f` bar Y, `+ 11f` time-x at `:23-50` and `:113-136`. The card sizes are tightly tuned to the "DRP-90 SciFi Cockpit Console" pose; a refactor would want them named.
- `MFDTheme` has plenty of color constants but no layout / scale constants — every spatial number is per-renderer.
- `currentTick` is referenced as a parameter `tick` in `StatusPanelRenderer.Render` even though the field has been retired in favor of `ElapsedSeconds` everywhere else; the dead parameter is the smoking gun for the refactor not being finished.

---

## 6. Notes for the cross-folder consolidation

1. **Surface contracts are tight; only two pieces of dead public API exist**: `UIController.MainScreen`/`ExtraScreen` and the `RadarControlModule` chain through `GridMfdPage` -> `GridVisualization.Render`. Both were left behind by the radar-consolidation refactor. Removing them tightens 3 ctor signatures across two folders and saves a cap-field on every menu render.
2. **`MFDTheme` is the de-facto cross-folder shared style**: every HUD renderer, every Module page, and every UI page reads `MFDTheme.*`. There is currently **no critical-red color token** — `(180, 50, 40)` is reinvented 12+ times across the folder (and again in HUD code). Adding `MFDTheme.DANGER` / `CRITICAL` to `UIController.cs:38` would unify ~15 inline literals folder-wide.
3. **The `int tick` parameter on `StatusPanelRenderer.Render` is residue** from a pre-`ElapsedSeconds` API and is wired into both call sites in `SystemManager.cs:143`/`:319`. Cleaning it up touches `SystemManager`, `StatusPanelRenderer.cs`, and the excluded idle-slides variant — useful "low-hanging" item for the cross-folder report.
4. **Excluded files are inert but two of them collide with live identifiers**: `StatusPanelRenderer.idle-slides.cs` redefines the same `StatusPanelRenderer` class name as the live file (compile error if un-excluded), and references `TerrainModule.GetMinimap` which no longer exists (the live API is `RenderMinimap`). `TerrainRenderer.cs` references the also-excluded `TerrainAPI` static class. Reviving any of these requires migration before `<Compile Remove>` is dropped.
