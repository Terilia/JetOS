# Code Size Sweep - 2026-05-10

Scope: research and analysis only. No source implementation changes were made.

## Current Output Size

- Deployed MDK output inspected: `%APPDATA%/SpaceEngineers/IngameScripts/local/Mdk.PbScript2/script.cs`
- File size on disk: `113,234` bytes
- Raw script character count: `98,566` characters
- MDK minification: `full`

The byte count is higher than the character count because full minify uses Unicode identifiers. For the Space Engineers programmable block limit, the practical number to watch is the character count.

## Highest Value Cuts

### 1. Remove unused sprite constants from `Shortcuts.cs`

`Utilities/Shortcuts.cs` still declares sprite constants that have no references outside their own declaration. Because MDK packs source text, these unused const declarations still survive into the minified output.

Confirmed unused constants include:

- `TEX_RADAR_SWEEP`
- `TEX_NO_SIGNAL`
- `TEX_MISSILE_HEAT`
- `TEX_MISSILE_RADAR`
- `TEX_MISSILE`
- `TEX_WARNING`
- `TEX_STATUS_RING`
- all module/icon constants like `TEX_ICON_HUD`, `TEX_ICON_RADAR`, `TEX_ICON_CANARD`, etc.
- `TEX_KEY_HINT_BOX`, `TEX_GLYPH_CHECK`, `TEX_GLYPH_BACK`
- `TEX_PITCH_ZERO`, `TEX_PITCH_INV`, `TEX_TAPE_BUG`
- `TEXTURE_SQUARE`

Evidence:

- Ref scan showed 26 consts with only one reference, their declaration.
- Source declaration lines total about `1,587` chars, with `421` chars of string literal content.
- The deployed minified script still contains examples like `JetOS_RadarSweep`, `JetOS_NoSignal`, and icon strings.

Estimated savings: several hundred to about `1k` script characters.

Risk: low. Remove only constants with zero external references. Do not delete the sprite assets from the mod, only unused script constants.

### 2. Delete legacy dead widgets in `GridVisualization.cs`

The active status screen render path now calls:

- `DrawAirframeBand`
- cached airframe sprites
- `DrawAirframeSummary`
- `DrawStatusSynoptic`

But the file still compiles old, unused widgets:

- `DrawFlightData`
- `DrawFuelBar`
- `DrawGMeter`
- `GToNeedleRotation`

These are not called by the current `Render` path. Removing them should also allow removing unused animation fields:

- `_animSpeed`
- `_animAltitude`
- `_animAoA`
- `_animMach`
- `_animThrottle`
- `_animGForce`

Evidence:

- The unused method block is about `6,089` source chars across 108 lines.
- `rg` found definitions only for the old widget methods, no active calls.

Estimated savings: likely `2.5k-4k` minified characters.

Risk: low after one in-game status-page visual check. This should not touch the current status screen layout.

### 3. Remove unused RWR position history

`RadarControlModule` still maintains `RWRTrackingState.PositionHistory`, `HistoryIndex`, `PositionSampleAccum`, and samples positions every `RWR_POSITION_SAMPLE_SECONDS`.

However, `IsThreatening(...)` accepts `positionHistory` but never reads it. Threat classification currently uses current position, velocity, player position, player velocity, gravity, range, closing velocity, closest approach, and aspect angle.

Evidence:

- `IsThreatening(..., List<Vector3D> positionHistory)` never uses `positionHistory`.
- `PositionHistory` maintenance source chunk is about `1,297` chars.

Potential cut:

- Remove `PositionHistory`, `HistoryIndex`, `PositionSampleAccum`.
- Remove `RWR_POSITION_SAMPLE_SECONDS`.
- Remove the sampling block in `TickRWRRadar`.
- Simplify `ClearHistory()` or remove it if it only resets position history.
- Remove the unused `positionHistory` parameter from `IsThreatening`.

Estimated savings: about `500-900` minified characters.

Risk: low-medium. Behavior should be unchanged because the history is not used today, but this is safety-adjacent RWR logic, so verify threat warnings in-game.

### 4. Remove dead weapon sound channel if seeker tones are not coming back

`SoundManager.RequestWeapon(...)` has no current callers. The weapon channel still initializes, preps blocks, ticks every frame, and carries unused `PRIORITY_SEARCH` / `PRIORITY_LOCK` constants.

Evidence:

- `rg RequestWeapon` finds only the method definition.
- Current warning callers use `RequestWarning("Tief", PRIORITY_ALTITUDE)` and `RequestWarning("Alert 2", PRIORITY_RWR, 1.0)`.
- Approximate source tied to weapon request/init path is about `1,432` chars before considering tick/reset branches.

Potential cut:

- Remove `weaponChannel`.
- Remove weapon-channel initialization and `PrepChannel(weaponChannel)`.
- Remove `RequestWeapon`.
- Remove weapon tick/reset block.
- Remove `PRIORITY_SEARCH` and `PRIORITY_LOCK`.

Estimated savings: about `700-1,200` minified characters.

Risk: medium only if AIM9 search/lock tones are planned to return. If weapon tones are intentionally gone, this is dead code.

### 5. Empty or remove `Instructions.readme`

The injected readme comment is still present at the top of the full-minified deployed script.

Evidence:

- Deployed output begins with:
  `// R e a d m e`
- `Instructions.readme` is 226 source chars.

Estimated savings: about `226` script characters.

Risk: none, unless that header is intentionally wanted in pasted PB output.

## Medium Value / Optional Cuts

### 6. Consolidate tiny repeated formatting helpers

There are repeated small helpers and patterns:

- `FmtTime(double s)` exists in both `GridVisualization` and `StatusPanelRenderer` with the same body.
- Canards, Grid, and Status each have small label/value drawing helpers.
- Horizontal bar drawing logic appears in multiple status-style renderers.

This should not become a giant generic UI framework. The safe version is only extracting very small helpers that have identical behavior.

Estimated savings: `200-500` chars if done carefully.

Risk: medium if over-abstracted. Keep it boring.

### 7. Compress radar menu strings

`RadarControlModule.GetOptions()` is string-heavy and allocates a `List<string>` each call. Some display strings are long:

- `RWR Units + (Current: {0}/{1})`
- `RWR Units - (Current: {0}/{1})`
- `Total Contacts: ...`
- `Pool: ... | RWR: ...`

Possible compact display:

- `RWR+ {0}/{1}`
- `RWR- {0}/{1}`
- `CONTACTS {n}`
- `POOL {pool} RWR {active}`

Estimated savings: `200-500` chars plus small allocation reduction.

Risk: low, but this is a visible UX change.

### 8. Reduce runtime error echo strings

`Program.cs` echoes stack traces for both null-reference recovery and critical errors. This is useful during development but costs strings and code.

Potential cut:

- Keep `Echo(e.Message)` only.
- Or gate stack traces behind a debug flag.

Estimated savings: `200-400` chars.

Risk: medium. Losing stack traces hurts in-game debugging.

### 9. Emergency-only: simplify the canards overlay

The canards page now keeps the standard menu/sidebar architecture, which is correct. The canards drawing itself is about `5,224` source chars including helper methods.

If character pressure becomes severe, trim only decoration:

- Drop `CANARD TILT` / `NEUTRAL + L/R` labels.
- Drop `+45`, `0`, `-45` text labels.
- Drop `DrawBladeLabel`.
- Keep only neutral line, two centered blades, and pivot dot.

Estimated savings: maybe `400-900` minified chars.

Risk: visual quality loss. Not recommended before dead-code cuts above.

## Lower Priority

### Configuration strings

`ConfigurationModule.cs` has the largest source string load among normal source files:

- 101 string literals
- about `1,323` source string chars

Most of this is useful configuration UI and CustomData keys. Only abbreviate if the menu text can stay readable.

### `Back to Main Menu` rows

`HUDModule` and `TerrainModule` both expose a single `Back to Main Menu` option even though global `4 BACK` / `9 MENU` exist. Removing these would require making zero-option modules safe in `SystemManager.NavigateDown()` / `ExecuteCurrentOption()`.

Potential savings are small. Not worth doing before larger cuts.

## Things That Are Already Not Worth Chasing

- `Diagnostics/**` is excluded by the project and should not affect packed script size.
- The old `RandomExtensions` dead-code note is stale; no active `RandomExtensions.cs` source is currently compiled.
- The old `CircularBuffer.Peek/Clear/ToArray` dead-code note is stale; current `CircularBuffer<T>` only has `Enqueue`, `Dequeue`, and `Count`.
- `StatusPanelRenderer.engine-backup.cs` is under `Diagnostics/**` and excluded.

## Recommended Order

1. Remove unused `Shortcuts.cs` constants.
2. Remove dead legacy `GridVisualization` widgets and their animation fields.
3. Remove unused RWR position history.
4. Decide whether weapon-channel audio is dead; if yes, remove it.
5. Empty `Instructions.readme`.
6. Only then consider formatting helper consolidation or visible menu-string compression.

The first five items should plausibly recover several thousand characters without touching core flight behavior or the current status-page design.
