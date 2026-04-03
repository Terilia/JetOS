# Optimization: Consolidate AirToGround and AirtoAir Modules

## Problem

`AirToGround` (187 lines) and `AirtoAir` (122 lines) are structurally near-identical. Both:

1. Hold a reference to the same `Jet._bays` missile bay list
2. Maintain a separate `bool[] baySelected` array for the same bays
3. Offer "Fire Selected Bays", "Toggle Selected Bays", bay selection options
4. Delegate to `MissileBayHelper` for all fire/toggle/hotkey operations
5. Have identical `HandleSpecialFunction()` and `GetHotkeys()` implementations

The only meaningful differences:
- AirToGround has bombardment mode (spread pattern targeting)
- AirToGround has topdown mode toggle
- AirtoAir has seeker toggle + weapon tone sounds
- AirtoAir auto-selects closest enemy and syncs GPS in `Tick()`

## Current Duplication

```csharp
// Both modules:
private List<IMyShipMergeBlock> missileBays = new List<IMyShipMergeBlock>();
private bool[] baySelected;

// Identical in both:
MissileBayHelper.FireSelectedBays(missileBays, baySelected, ParentProgram);
MissileBayHelper.TransferCacheToSlots(missileBays.Count);
MissileBayHelper.ToggleSelectedBays(missileBays, baySelected);
MissileBayHelper.ToggleBaySelection(baySelected, index - offset);
```

## Proposed Solution

Merge into a single `WeaponsModule` with a mode selector (Air-to-Air / Air-to-Ground). The bay management code exists only once. Mode-specific behavior (bombardment, seeker tones) is toggled by the current mode.

This removes ~80 lines of duplicated code and eliminates the confusing situation where both modules independently select bays on the same bay list.

## Impact

- **Lines saved**: ~80 lines
- **Complexity reduction**: One module instead of two. One bay selection state instead of two independent ones.
- **Risk**: Medium - the menu structure changes (one module instead of two in the main menu). But functionally everything is preserved behind a mode toggle.
- **Files affected**: AirToGround.cs, AirtoAir.cs (merged into new WeaponsModule.cs), SystemManager.cs (initialization)
