# Optimization: Simplify Background Tick Loop

## Problem

`SystemManager.Main()` has a verbose if-chain for background-ticking modules:

```csharp
if (currentModule != null)
    currentModule.Tick();

if (hudProgram != null && currentModule != hudProgram)
    hudProgram.Tick();

if (radarControlModule != null && currentModule != radarControlModule)
    radarControlModule.Tick();

if (airtoAirModule != null && currentModule != airtoAirModule)
    airtoAirModule.Tick();

if (gunControlModule != null && currentModule != gunControlModule)
    gunControlModule.Tick();

if (aeroRecorder != null && aeroRecorder.IsActive && currentModule != aeroRecorder)
    aeroRecorder.Tick();
```

This requires manually adding a new block for every module that needs background ticking. It's easy to forget, and the pattern is repetitive.

## Proposed Solution

Add a `BackgroundTick` flag to `ProgramModule` and iterate the modules list:

```csharp
// In ProgramModule:
public bool AlwaysTick = false;  // Set in constructor for modules that need it

// In SystemManager.Main():
for (int i = 0; i < modules.Count; i++)
{
    var m = modules[i];
    if (m == currentModule)
        m.Tick();
    else if (m.AlwaysTick)
        m.Tick();
}
```

Modules set `AlwaysTick = true` in their constructors. The special case for `aeroRecorder.IsActive` can be handled by having its `Tick()` method return early if not active (which is the standard pattern).

## Impact

- **Lines saved**: ~15 lines, replaces 6 if-blocks with a 5-line loop
- **Maintainability**: Adding a new background-ticking module requires zero changes to SystemManager
- **Risk**: Low - same behavior, just structured differently
- **Caveat**: Must preserve tick order if any module depends on another's output within the same tick. Current order: currentModule -> HUD -> Radar -> AirtoAir -> GunControl -> AeroRecorder. A list iteration preserves insertion order.
- **Files affected**: SystemManager.cs, ProgramModule.cs, HUDModule.cs, RadarControlModule.cs, AirtoAir.cs, GunControlModule.cs, AeroRecorderModule.cs
