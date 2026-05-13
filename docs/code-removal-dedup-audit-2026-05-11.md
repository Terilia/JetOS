# JetOS Code Removal and Deduplication Audit - 2026-05-11

## Scope

This audit looks for active JetOS source that can be removed, simplified, or deduplicated without removing features. The main source set is `Mdk.PbScript2/**/*.cs`, with `Mdk.PbScript2/Diagnostics/**` treated as non-compiled support material because the project excludes it from compilation.

No implementation cleanup is performed by this report. It is a removal/deduplication plan plus a verification log.

Project context that affects recommendations:

- `Mdk.PbScript2/Mdk.PbScript2.mdk.ini` uses `minify=full`, so comments and most whitespace are already stripped from the packed programmable block script.
- Under MDK full minification, local variable names and field names are not the prize. A dead local can be only a few packed characters, while an unused class, method, list allocation, branch, or repeated call path can still be meaningful.
- `Mdk.PbScript2/Mdk.PbScript2.csproj` excludes `Diagnostics/**` from compile, so diagnostic scripts and backups do not affect compiled script size.
- The sprite manifest currently keeps unused sprites inside XML comments, so the mod loader should not treat those entries as active definitions.

## Executive Summary

Best payoff removals:

1. Replace `activeThreats: List<RWRWarning>` with a simple count/flag model, then remove `RWRWarning` if no future display is planned.
2. Remove the unused center "all engine" buckets: `centerEnginesAll` and `centerABAll`.
3. Remove or restore the stale `Instructions.readme` project item for project hygiene, not packed script size.
4. Refresh or remove stale source-layout comments and the empty `Extensions` README for source hygiene, not packed script size.

Low-payoff cleanup:

- Remove the unused `isCenter` local in engine classification.
- Remove the unused `worldToLocal` local in `RadarRenderer.DrawRadarMinimap`.

These two are correct, but they are not juicy under MDK minification. They should be swept up only when touching nearby code.

Best deduplication candidates:

1. Share the `"Back"`-only options array between modules.
2. Consider caching menu option arrays only where menus do not need live values.
3. Keep current UI drawing helpers for now; they look similar, but a generic abstraction is unlikely to save much and may make layout code harder to tune.
4. Keep `Tools/build-sprites.ps1` as the source of truth for disabled sprites; do not hand-edit the generated XML as a primary workflow.

Items I specifically checked and do not recommend removing:

- Active `Shortcuts.cs` sprite constants: all `TEXTURE_*` and `TEX_*` constants have at least one reference outside their declaration.
- `centerEngines` and `centerAB`: these are used by throttle override logic and are not removable.
- `manualfire`: it is not displayed, but it controls gatling auto-fire behavior and must stay unless the feature is intentionally redesigned.
- `TerrainData` status properties: `Available`, `Ready`, `Loading`, and `DownloadProgress` are displayed/used by the terrain page/status panel.
- `SoundManager` warning channel: current altitude and RWR alerts still call it.

## Removal Candidates

### 1. RWR Warning Payload Is Built but Never Consumed

Confidence: high for unused fields, medium for behavior-preserving simplification  
Recommended action: replace `activeThreats` with count/flags if no per-threat display is planned  
Packed-script payoff: high relative to other findings  
Runtime payoff: moderate  
Risk: medium

Current source:

- `Mdk.PbScript2/Utilities/CommonTypes.cs:8-24` defines `RWRWarning` with:
  - `Position`
  - `Velocity`
  - `Name`
  - `IsIncoming`
  - `RWRIndex`
- `Mdk.PbScript2/Modules/RadarControlModule.cs:58` declares `private List<RWRWarning> activeThreats`.
- `Mdk.PbScript2/Modules/RadarControlModule.cs:141` reads only `activeThreats.Count`.
- `Mdk.PbScript2/Modules/RadarControlModule.cs:197` and `358` clear the list.
- `Mdk.PbScript2/Modules/RadarControlModule.cs:713` adds incoming warnings.
- `Mdk.PbScript2/Modules/RadarControlModule.cs:718` adds non-incoming warnings.
- `Mdk.PbScript2/Modules/RadarControlModule.cs:773-775` warning sound uses `anyThreatDetected`, not the contents of `activeThreats`.

Observed use:

- No code reads any field from `RWRWarning`.
- The menu only displays a count.
- The sound path only needs `anyThreatDetected`.

Why this is removable:

The system calculates detailed RWR payloads, but the current UI/logic consumes only:

- Total stabilized RWR detections for display.
- Whether any stabilized detection is threatening for warning audio.

Why this is a good MDK target:

- This removes a whole type, constructor, field assignments, list element creation, and list storage from the hot RWR path.
- Unlike an unused local variable, this is more than a name-length issue after minification.

Recommendation:

- Replace `List<RWRWarning> activeThreats` with an integer such as `activeRwrDetectionCount`.
- Preserve current visible behavior by incrementing the count for both incoming and non-incoming stabilized contacts, because `activeThreats.Count` currently includes both.
- Keep `anyThreatDetected` for the warning sound.
- Delete `Utilities/CommonTypes.cs` if `RWRWarning` remains its only type.

Optional semantic improvement:

- If the menu label should mean only incoming threats, use `activeIncomingThreatCount` instead. That would be a behavior change, because today non-incoming stabilized detections are included in the displayed count.

Validation after implementation:

- RWR menu still shows `NO THR`, `1 THR`, or `N THR`.
- RWR audio still plays only when `IsThreatening(...)` returns true.
- RWR disable still clears the displayed count.

### 2. Unused Center Engine "All" Buckets

Confidence: high  
Recommended action: remove or stop populating `centerEnginesAll` and `centerABAll`  
Packed-script payoff: medium  
Runtime payoff: low to medium  
Risk: low to medium

Current source:

- `Mdk.PbScript2/Jet.cs:27` declares `centerEnginesAll`.
- `Mdk.PbScript2/Jet.cs:30` declares `centerABAll`.
- `Mdk.PbScript2/Jet.cs:214-215` clears both lists.
- `Mdk.PbScript2/Jet.cs:238` populates `centerABAll`.
- `Mdk.PbScript2/Jet.cs:244` populates `centerEnginesAll`.

Observed use:

- No code reads either center "all" list after population.
- `UpdateEngineMetricCache()` only consumes left/right all-buckets:
  - `Mdk.PbScript2/Jet.cs:260-265` calls `CacheEngineSide(leftEnginesAll, leftABAll, ...)` and `CacheEngineSide(rightEnginesAll, rightABAll, ...)`.
- The usable center buckets are still needed:
  - `Mdk.PbScript2/HUDModule.cs:602` sets overrides for `centerEngines`.
  - `Mdk.PbScript2/HUDModule.cs:609` sets overrides for `centerAB`.

Why this is removable:

The center "all" lists are calculated but never used for display, balancing, health metrics, or throttle control. They are pure bookkeeping overhead in the current code.

Why this is a real target despite minification:

- The variable names themselves are not important.
- The payoff comes from removing two list fields, two list allocations, two clear calls, and the center sink arguments/path for all-bucket classification.

Recommendation:

- Remove the two fields.
- Stop clearing them.
- Change the all-engine classification call so center entries are ignored for all-bucket metrics, or split `AddEngineToSide` into a left/right-only helper for the all-bucket case.
- Keep `centerEngines` and `centerAB`.

Validation after implementation:

- Build the script.
- Confirm center engines still receive throttle override.
- Confirm engine health/status display still reports left/right all and usable totals correctly.

### 3. Unused `isCenter` Local

Confidence: high  
Recommended action: remove local only when touching nearby code  
Packed-script payoff: tiny  
Runtime payoff: tiny  
Risk: none

Current source:

- `Mdk.PbScript2/Jet.cs:234` calculates `bool isCenter = !isLeft && !isRight;`.

Observed use:

- `isCenter` is never read.

Recommendation:

- Delete the local.

MDK note:

- This is correct dead code, but it is not a meaningful size win by itself. Bundle it with the engine-list cleanup above.

### 4. Unused Radar Minimap Transform

Confidence: high  
Recommended action: remove local and refresh nearby comment only when touching nearby code  
Packed-script payoff: tiny to low  
Runtime payoff: tiny  
Risk: none

Current source:

- `Mdk.PbScript2/HUD/RadarRenderer.cs:51-54` comments on a local-space transform and calculates `MatrixD worldToLocal = MatrixD.Transpose(WM(cockpit));`.

Observed use:

- `worldToLocal` is never read.
- The minimap now uses yaw-plane projections built from gravity/up, forward, and right vectors, not that matrix.

Recommendation:

- Delete `worldToLocal`.
- Remove or rewrite the comment block above it so it describes the yaw-plane approach that the code actually uses.

### 5. Stale `Instructions.readme` Project Item

Confidence: high  
Recommended action: remove the stale project entries or restore the file  
Packed-script payoff: likely none  
Runtime payoff: none  
Risk: low

Current source:

- `Mdk.PbScript2/Mdk.PbScript2.csproj:29-30` removes/includes `Instructions.readme`.
- `Mdk.PbScript2/Instructions.readme` is not present in the workspace.
- Current builds still succeed without the file.

Recommendation:

- If MDK no longer needs this file, remove the two project entries.
- If MDK or your workflow expects instruction text, restore a minimal `Instructions.readme`.

Size impact:

- Likely none in the packed script because the file is missing now.
- Source/project hygiene benefit only.

### 6. Stale Source-Layout Comments and Empty Extension Folder

Confidence: high  
Recommended action: refresh or delete stale documentation comments/files  
Packed-script payoff: none or near-none  
Runtime payoff: none  
Risk: none

Current source:

- `Mdk.PbScript2/Program.cs:16` mentions `UI/UIElements.cs`, which is not present.
- `Mdk.PbScript2/Program.cs:18` says utilities include `PID`, but no active PID utility was found.
- `Mdk.PbScript2/Program.cs:19` mentions `Extensions/RandomExtensions.cs`, which is not present.
- `Mdk.PbScript2/Extensions/README.md` describes a `RandomExtensions.cs` file that is not present.

Recommendation:

- Either remove the file-structure block from `Program.cs` or update it to the current layout.
- Delete `Mdk.PbScript2/Extensions/README.md` and the empty folder if extension methods are not coming back.

Size impact:

- Minimal or none in packed output because `minify=full` strips comments.
- Helps prevent future audits from rediscovering stale false positives.

### 7. Diagnostics and Backup Files Are Not Compiled

Confidence: high  
Recommended action: archive/delete only if repository noise matters  
Risk: low

Current source:

- `Mdk.PbScript2/Mdk.PbScript2.csproj:34-35` removes `Diagnostics/**` from compile and includes it as non-source content.
- `Mdk.PbScript2/Diagnostics/StatusPanelRenderer.engine-backup.cs` is a large backup renderer, but it is excluded from compilation.

Recommendation:

- Do not treat diagnostics as script-size problems.
- If the repository should be slimmer, move obsolete diagnostics/backups into docs or delete them after confirming they are no longer useful.

## Deduplication Candidates

### 1. Shared "Back"-Only Options

Confidence: high  
Recommended action: optional low-risk dedupe  
Risk: low

Current source:

- `Mdk.PbScript2/Modules/HUDModule.cs:215` returns `new string[] { "Back" }`.
- `Mdk.PbScript2/Modules/TerrainModule.cs:34` returns `new string[] { "Back" }`.

Recommendation:

- Add a shared static readonly `string[]` on `ProgramModule`, or a protected helper, for modules with only a Back option.

Tradeoff:

- Saves a tiny amount of source and avoids repeated allocations.
- The gain is small, so do this only as part of a broader cleanup pass.

### 2. Menu Option Builders Allocate Repeated Lists

Confidence: medium  
Recommended action: optimize only where the menu is stable or GC pressure is visible  
Risk: medium

Current source examples:

- `Mdk.PbScript2/Modules/AirtoAir.cs:47-55` builds a `List<string>` then returns `ToArray()`.
- `Mdk.PbScript2/Modules/RadarControlModule.cs:123-178` builds dynamic options and returns `ToArray()`.
- `Mdk.PbScript2/Modules/ConfigurationModule.cs:213-225` builds dynamic config options and returns `ToArray()`.

Recommendation:

- Do not blindly cache all options. Several menus include live values such as threat counts, enemy counts, bay readiness, selected bay state, and modified config markers.
- If allocation pressure becomes a real runtime issue, cache by state:
  - Radar options: invalidate when RWR enable/count, selected target, radar state, or threat count changes.
  - Air-to-air options: invalidate when bay selection/readiness or topdown mode changes.
  - Configuration options: invalidate when category/index/value/dirty marker changes.

Size impact:

- Not a strong script-size win. More likely a runtime allocation/GC improvement if implemented carefully.

### 3. Similar UI Label/Value Helpers

Confidence: medium  
Recommended action: leave for now unless actively refactoring UI  
Risk: medium

Current source:

- `Mdk.PbScript2/UI/GridVisualization.cs:253` has `DrawSummaryCell`.
- `Mdk.PbScript2/UI/GridVisualization.cs:328` has `DrawTinyLabelValue`.
- `Mdk.PbScript2/Modules/CanardModule.cs:259` has `DrawMetric`.
- `Mdk.PbScript2/UI/StatusPanelRenderer.cs:135-136` and `Mdk.PbScript2/UI/UIController.cs:310-311` each define local `Rect`/`Txt` wrappers.

Recommendation:

- Keep these local for now.
- Deduplicate only if a new shared helper reduces actual repeated drawing code without forcing awkward parameter lists.

Why not remove now:

- These helpers are tiny and layout-specific.
- A generic helper may cost as much or more after minification and can make visual tuning harder.

### 4. Sprite Disabled List Source of Truth

Confidence: high  
Recommended action: keep generator as source of truth; consider a data file if the list grows  
Risk: low

Current source:

- `Tools/build-sprites.ps1:14-40` lists disabled sprite names.
- `Tools/build-sprites.ps1:170-180` writes disabled sprites as XML comments.
- `Mod/testmod/Data/LCDTextures.sbc` currently has 32 active definitions and 25 commented unused definitions.

Recommendation:

- Treat `Tools/build-sprites.ps1` as the source of truth.
- Avoid hand-editing `LCDTextures.sbc` for sprite enable/disable decisions.
- If the disabled list keeps changing, move it to a small data file such as `Tools/disabled-sprites.txt` and have the build script read it. That would avoid mixing generator logic and project policy.

## Stale Findings From Older Audits

These older cleanup ideas were checked against the current source and should not be repeated as active findings unless they reappear.

### Active Sprite Constants

Older note:

- Remove unused sprite constants from `Shortcuts.cs`.

Current check:

- All `TEXTURE_*` and `TEX_*` constants in `Mdk.PbScript2/Utilities/Shortcuts.cs` have at least one reference outside their declaration.

Recommendation:

- No removal from active sprite constants right now.

### Legacy `GridVisualization` Widgets

Older note:

- Remove dead `GridVisualization` widgets such as old engine/fuel/flight panels.

Current check:

- Current `GridVisualization.cs` render path calls the remaining helper methods.
- Old named dead widgets from the prior report were not found in active source.

Recommendation:

- No current removal based on the older widget list.

### RWR Position History

Older note:

- Remove unused RWR position history.

Current check:

- The earlier `positionHistory` pattern is not present in active source.
- Current RWR cleanup target is the separate `RWRWarning` payload/list described above.

Recommendation:

- Do not chase the old position-history finding.

### Weapon Sound Channel

Older note:

- Remove `RequestWeapon`, search/lock priorities, and a weapon sound channel.

Current check:

- Active `SoundManager` only has the warning channel.
- `RequestWarning(...)` is used by altitude warnings and RWR warnings.

Recommendation:

- Do not remove the current warning channel.

### `RandomExtensions` and `CircularBuffer`

Older note:

- Remove `RandomExtensions`.
- Remove unused `CircularBuffer` members.

Current check:

- `RandomExtensions.cs` is not active source.
- `CircularBuffer<T>` is used by `HUDModule` smoothing and now only exposes `Enqueue`, `Dequeue`, and `Count`.

Recommendation:

- Only clean stale references/docs for `RandomExtensions`.
- Keep `CircularBuffer<T>`.

## Do Not Remove Without Feature Decisions

These look tempting during a size sweep, but they are currently active behavior.

### `manualfire`

Current source:

- `Mdk.PbScript2/Jet.cs:127` stores `manualfire`.
- `Mdk.PbScript2/Modules/HUDModule.cs:353-354` uses it to decide whether auto-fire controls gatlings.
- `Mdk.PbScript2/Modules/HUDModule.cs:557` toggles it from jump throttle.
- `Mdk.PbScript2/Modules/HUDModule.cs:566-567` enables gatlings when manual fire is active.

Recommendation:

- Do not remove.
- If pilot feedback is needed, display the current mode on HUD/weapon UI.

### `centerEngines` and `centerAB`

Current source:

- These are set in `Jet.cs` and used by `HUDModule.UpdateThrottleControl`.

Recommendation:

- Do not remove. Only the center "all" buckets are dead.

### Terrain Status Properties

Current source:

- `TerrainData.Available`, `Ready`, `Loading`, and `DownloadProgress` are used by `TerrainModule` and `StatusPanelRenderer`.

Recommendation:

- Do not remove.

### Warning Sound Channel

Current source:

- Altitude warnings call `SoundManager.RequestWarning("Tief", SoundManager.PRIORITY_ALTITUDE)`.
- RWR warnings call `SoundManager.RequestWarning("Alert 2", SoundManager.PRIORITY_RWR, 1.0)`.

Recommendation:

- Do not remove unless all warning audio is intentionally removed.

## Recommended Cleanup Order

### Phase 1 - No-Behavior Cleanup

1. Remove or restore `Instructions.readme` project entries.
2. Refresh/delete stale file-structure docs and `Extensions/README.md`.
3. Remove `isCenter` only if already touching `Jet.cs`.
4. Remove `worldToLocal` only if already touching `RadarRenderer.cs`.

Expected risk: very low.

MDK payoff: mostly hygiene, not meaningful packed script shrinkage.

### Phase 2 - Engine Classification Slimming

1. Remove `centerEnginesAll` and `centerABAll`.
2. Adjust all-bucket classification so center all-bucket entries are ignored.
3. Keep usable center engine lists untouched.

Expected risk: low to medium.

### Phase 3 - RWR Payload Simplification

1. Replace `activeThreats` with a count.
2. Preserve current count semantics unless intentionally changing the UI.
3. Remove `RWRWarning` and `CommonTypes.cs` if no types remain.

Expected risk: medium because RWR display/audio semantics are pilot-facing.

MDK payoff: best current target in this report.

### Phase 4 - Optional Runtime/Dedupe Work

1. Share Back-only option arrays.
2. Cache dynamic menu options only with explicit invalidation.
3. Revisit UI helper dedupe only during a UI refactor.
4. Consider moving disabled sprite names to a data file if sprite pruning becomes frequent.

Expected risk: low to medium, but savings are also smaller.

## Double-Check Log

Commands and checks used for this report:

- Searched active source for `centerEnginesAll`, `centerABAll`, `isCenter`, `worldToLocal`, `activeThreats`, `RWRWarning`, and `IsIncoming`.
  - Confirmed the center all-buckets are only declared, cleared, and populated.
  - Confirmed `isCenter` and `worldToLocal` have no readers.
  - Confirmed `RWRWarning` fields are never read.
- Searched active source for stale audit findings:
  - `RequestWeapon`, `PRIORITY_SEARCH`, `PRIORITY_LOCK`
  - old `GridVisualization` widget names
  - `positionHistory`
  - `Peek`, `Clear`, and `ToArray` on `CircularBuffer`
  - Confirmed these older findings are stale in current active code.
- Checked `Shortcuts.cs` sprite constants:
  - All `TEXTURE_*` and `TEX_*` constants have references outside `Shortcuts.cs`.
- Checked project inputs:
  - `Diagnostics/**` is excluded from compile.
  - `Instructions.readme` is referenced by the project but missing on disk.
- Checked sprite manifest state:
  - `LCDTextures.sbc` has 32 active `<LCDTextureDefinition>` blocks and 25 unused definitions inside XML comments.

## Verification Still Required Before Implementing Removals

Before applying the recommended cleanup, run:

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT
```

After engine cleanup, also manually validate in-game or with the relevant test setup:

- Center engines still throttle.
- Left/right engine health and thrust summaries still update.
- RWR count and warning sound behavior remain correct.
- Terrain and HUD pages still render without missing sprites.
