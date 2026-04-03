# Optimization: Simplify RWR Position History

## Problem

Each `RWRTrackingState` maintains a 10-element position history list that:
1. Is initialized with 10 `Vector3D.Zero` entries in the constructor
2. Records a position every 10 ticks
3. Is never actually read by `IsThreatening()` - the `positionHistory` parameter is accepted but not used for anything meaningful

```csharp
// RWRTrackingState constructor:
PositionHistory = new List<Vector3D>();
for (int i = 0; i < 10; i++)
    PositionHistory.Add(VZ);  // 10 * 24 bytes = 240 bytes per RWR radar

// Recorded every 10 ticks:
if (state.TickCounter % 10 == 0)
{
    state.PositionHistory[state.HistoryIndex] = enemyPos;
    state.HistoryIndex = (state.HistoryIndex + 1) % state.PositionHistory.Count;
}

// Passed to IsThreatening() but NEVER READ inside that method:
bool isThreatening = IsThreatening(enemyPos, enemyVel, playerPos, playerVel, gravity, state.PositionHistory);
```

Looking at `IsThreatening()`: it uses `enemyPos`, `enemyVel`, `playerPos`, `playerVel` for all threat calculations. The `positionHistory` parameter is completely unused.

## Proposed Solution

Remove `PositionHistory`, `HistoryIndex`, and `TickCounter` from `RWRTrackingState`. Remove the `positionHistory` parameter from `IsThreatening()`. Remove the history recording code from `ProcessRWR()`.

If position history is intended for future use (e.g., trajectory prediction), it should be documented and actually used. Otherwise it's dead code consuming memory and instructions.

## Impact

- **Lines saved**: ~25 lines
- **Memory saved**: ~240 bytes per RWR radar (10 * Vector3D)
- **Instructions saved**: ~5 per tick per RWR radar (modulo check + array write)
- **Risk**: None if position history truly isn't used. If it was intended for future use, document it.
- **Files affected**: RadarControlModule.cs
