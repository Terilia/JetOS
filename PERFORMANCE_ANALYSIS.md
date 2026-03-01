# JetOS Performance Analysis

This document identifies performance sinks and optimization opportunities in the JetOS codebase for Space Engineers programmable blocks.

**Context**: Space Engineers limits scripts to ~50,000 instructions per tick. Running at Update1 (60Hz) requires extremely efficient code.

---

## Critical Performance Issues

### 1. Multiple CustomData Parsing Systems (HIGH IMPACT) - FIXED

**Location**: `SystemManager.cs:134-189`, `AirtoAir.cs:32-68`, `RadarControlModule.cs:284-317`

**Problem**: Three separate systems parse CustomData independently:
- `SystemManager` has an optimized dictionary cache
- `AirtoAir.UpdateCustomDataWithCache()` parses with `Split('\n')` every call
- `RadarControlModule.GetCustomDataValue()` parses with `Split('\n')` every call

**Impact**: `Split('\n')` creates new string arrays. Called frequently, this generates significant garbage.

**Solution Applied**: Made `SystemManager.GetCustomDataValue()`, `SetCustomDataValue()`, and `TryGetCustomDataValue()` public. Refactored `AirtoAir` and `RadarControlModule` to use these centralized methods. Also added `RemoveCustomDataValue()` for cache cleanup.

---

### 2. AI Block Detection Loop (MEDIUM IMPACT)

**Location**: `Jet.cs:166-180`, `RadarControlModule.cs:86-100`

**Problem**: Constructor loops 1-99 calling `GetBlockWithName()` for each potential AI block pair:
```csharp
for (int i = 1; i <= 99; i++)
{
    var flightBlock = grid.GetBlockWithName(flightName) as IMyFlightMovementBlock;
    var combatBlock = grid.GetBlockWithName(combatName) as IMyOffensiveCombatBlock;
}
```

**Impact**: Up to 198 `GetBlockWithName()` calls at initialization. Not per-tick, but adds startup latency.

**Recommendation**: Use `GetBlocksOfType<T>()` once and filter by naming pattern, or implement early termination when consecutive misses occur.

---

### 3. LINQ Usage in Hot Paths (HIGH IMPACT) - FIXED

**Location**: `AirtoAir.cs:530,573-575,597-598,603`

**Problem**: LINQ operations allocate memory:
```csharp
customDataLines.FirstOrDefault(line => line.StartsWith("Cached:GPS:"))  // Allocates
customDataLines.ToList()  // Allocates new List
customDataLines.FindIndex(line => line.StartsWith(cacheLabel))  // Lambda allocation
```

**Impact**: Each `ToList()` allocates a new list. Lambda expressions may allocate closures.

**Solution Applied**: Removed all LINQ usage from `AirtoAir.cs` by refactoring to use `SystemManager.GetCustomDataValue()` which uses the cached dictionary. Removed `using System.Linq;` from both `AirtoAir.cs` and `RadarControlModule.cs`.

---

### 4. Array.Resize in Rendering Loop (HIGH IMPACT) - FIXED

**Location**: `HUDModule.cs:1002`

**Problem**:
```csharp
Array.Resize(ref radarTargetPositions, radarTargetCount);
```

**Impact**: `Array.Resize` allocates a new array every frame (60x/second).

**Solution Applied**: Added pre-allocated `radarTargetBuffer[MAX_RADAR_TARGETS]` as a class field. Modified `DrawTopDownRadarOptimized()` to accept a `targetCount` parameter. The buffer is reused every frame with no allocations.

---

### 5. CircularBuffer.ToArray() Allocations (MEDIUM IMPACT)

**Location**: `HUDModule.cs:1463`

**Problem**:
```csharp
var points = altitudeHistory.ToArray();
for (int i = 0; i < points.Length; i++) { ... }
```

**Impact**: `ToArray()` allocates new array each call. Called in `UpdateSmoothedValues()` every frame.

**Recommendation**: Add iteration method to `CircularBuffer` that avoids allocation, or maintain running sum like velocity/gForces already do.

---

### 6. Double Module Ticking (MEDIUM IMPACT)

**Location**: `SystemManager.cs:377-404`

**Problem**: Module tick order creates potential double-tick scenarios:
```csharp
if (currentModule != null) currentModule.Tick();  // Line 379
raycastProgram.Tick();                            // Line 384
hudProgram.Tick();                                // Line 385
if (radarControlModule != null && currentModule != radarControlModule) radarControlModule.Tick();  // Line 393
if (airtoAirModule != null && currentModule != airtoAirModule) airtoAirModule.Tick();  // Line 401
```

**Impact**: `hudProgram` is always ticked AND may be `currentModule`, causing double tick. Same potential issue with raycast.

**Recommendation**: Consolidate tick logic. Track which modules need per-tick updates vs only when active.

---

### 7. String Formatting in Rendering (HIGH IMPACT)

**Location**: Throughout `HUDModule.cs` (dozens of locations)

**Problem**: String interpolation/formatting generates allocations:
```csharp
$"G: {gForces:F1}"                    // String allocation
$"{distanceToIntercept / 1000:F1}km"  // String allocation
ttiText = $"{timeToIntercept:F1}s"    // Every frame
```

**Impact**: Dozens of string allocations per frame at 60Hz.

**Recommendation**:
- Use `StringBuilder` with pre-allocated capacity
- Cache static strings
- Consider numeric-only display updates (update text only when values change significantly)

---

### 8. Matrix Inversion Per Frame (MEDIUM IMPACT)

**Location**: `HUDModule.cs:395,709,1025,1709,1990`

**Problem**: Multiple `MatrixD.Invert()` calls per frame:
```csharp
MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
```

**Impact**: Matrix inversion is computationally expensive. Called 3-5 times per frame.

**Recommendation**: Cache the inverted matrix once per tick at the start of `Tick()` and pass it to drawing methods.

---

### 9. Grid Outline Sprite Caching Issue (LOW IMPACT)

**Location**: `SystemManager.cs:485-636`

**Problem**: Grid visualization rebuilds sprite cache when block count changes, but dynamic text sprites (Manual Fire, RWR Status) are added to `cachedSprites` on every rebuild, causing them to be repeatedly added.

**Impact**: Inefficient but only triggers when block count changes.

**Recommendation**: Separate static grid sprites from dynamic text sprites.

---

### 10. Sound State Machine Duplication (MEDIUM IMPACT)

**Location**: `SystemManager.cs:256-342`, `AirtoAir.cs:366-428`, `RadarControlModule.cs:456-483`

**Problem**: Three independent sound management systems with similar state machines.

**Impact**: Code duplication, harder to maintain, potential for conflicts.

**Recommendation**: Create centralized `SoundManager` class used by all modules.

---

## Moderate Performance Issues

### 11. Unnecessary Null Checks in Tight Loops

**Location**: `HUDModule.cs:136-142`, `Jet.cs:444-450`

**Problem**: Null checks inside loops that could be pre-filtered:
```csharp
for (int i = 0; i < tanks.Count; i++)
{
    if (tanks[i] != null) { ... }  // Should filter nulls at construction
}
```

**Recommendation**: Filter null/non-functional blocks when building lists, not every tick.

---

### 12. DateTime.Now.Ticks Usage (LOW IMPACT)

**Location**: `SystemManager.cs:837`, `Jet.cs:26-27`

**Problem**: Mix of `DateTime.Now.Ticks` and `Jet.GameTicks` for timing:
```csharp
long age = DateTime.Now.Ticks - _myJet.targetSlots[nextIndex].TimestampTicks;
```

**Impact**: Inconsistent timing sources could cause issues after game load.

**Recommendation**: Standardize on `Jet.GameTicks` for all game-related timing.

---

### 13. Repeated Surface Size Queries (LOW IMPACT)

**Location**: Throughout HUDModule drawing methods

**Problem**: `hud.SurfaceSize` accessed multiple times per method:
```csharp
Vector2 surfaceSize = hud.SurfaceSize;  // Line 1
... use surfaceSize ...
float screenWidth = hud.SurfaceSize.X;  // Later - redundant call
```

**Recommendation**: Query once at tick start, pass as parameter.

---

### 14. Inventory Iteration (LOW IMPACT)

**Location**: `Jet.cs:489-497`

**Problem**: `GetTotalGunAmmo()` iterates all inventory items every call:
```csharp
for (int j = 0; j < inventory.ItemCount; j++) { ... }
```

**Impact**: Called from HUDModule which renders every tick.

**Recommendation**: Cache ammo count with dirty flag, only recalculate periodically (every 30-60 ticks).

---

### 15. Position History in RWR (LOW IMPACT)

**Location**: `RadarControlModule.cs:42-48`

**Problem**: Pre-allocates 10 `Vector3D` positions but uses list operations:
```csharp
PositionHistory = new List<Vector3D>();
for (int i = 0; i < 10; i++) PositionHistory.Add(Vector3D.Zero);
```

**Recommendation**: Use fixed-size array instead of List for circular buffer.

---

## Existing Optimizations (Good Practices Found)

The codebase already implements several optimizations:

1. **CustomData Dictionary Cache** (`SystemManager.cs:134-189`) - Parses once, caches in dictionary
2. **Running Sum Smoothing** (`HUDModule.cs:73-76`) - Efficient circular buffer averaging
3. **Sprite Caching** (`SystemManager.cs:51-56`) - Grid outline cached when structure unchanged
4. **Block List Caching** (`SystemManager.cs:470-479`) - Periodic refresh vs every frame
5. **State Change Detection** (`HUDModule.cs:877-889`) - Airbrake only calls API when state changes
6. **Thrust Override Check** (`Jet.cs:446-449`) - Only sets if value changed
7. **Enemy Decay Throttling** (`Jet.cs:294-308`) - Only checks every 60 ticks
8. **Pre-allocated Sort Buffers** (`Jet.cs:312-313`) - Reusable lists for sorting

---

## Prioritized Recommendations

### Immediate (High ROI)

1. **Unify CustomData parsing** - Have all modules use `SystemManager.GetCustomDataValue()` - **COMPLETED**
2. **Eliminate Array.Resize** - Pre-allocate radar target array - **COMPLETED**
3. **Cache matrix inversion** - Calculate once per tick
4. **Replace LINQ with loops** - Remove `ToList()`, `FirstOrDefault()`, `FindIndex()` - **COMPLETED** (as part of #1)

### Short-term

5. **Add StringBuilder for HUD text** - Reduce string allocations
6. **Fix double-tick issue** - Ensure modules tick exactly once
7. **Add iteration to CircularBuffer** - Avoid `ToArray()` allocation
8. **Cache ammo count** - Update every 60 ticks instead of every frame

### Long-term

9. **Centralize sound management** - Single sound manager class
10. **Profile instruction usage** - Add `Runtime.CurrentInstructionCount` logging
11. **Consider multi-tick rendering** - Split HUD updates across frames
12. **Implement dirty flags for text** - Only regenerate text sprites when values change

---

## Instruction Count Monitoring

Add this to `SystemManager.Main()` for profiling:

```csharp
// At end of Main()
if (currentTick % 60 == 0)  // Log every second
{
    parentProgram.Echo($"Instructions: {parentProgram.Runtime.CurrentInstructionCount}");
    parentProgram.Echo($"Max: {parentProgram.Runtime.MaxInstructionCount}");
}
```

---

## Estimated Impact

| Issue | Instructions Saved | Memory Saved |
|-------|-------------------|--------------|
| Unified CustomData | 500-1000/tick | ~2KB/tick |
| Array.Resize fix | 100-200/tick | 64B/frame |
| Matrix cache | 200-400/tick | - |
| LINQ removal | 300-500/tick | ~1KB/tick |
| String caching | 500-1000/tick | ~4KB/tick |
| **Total Estimated** | **1600-3100/tick** | **~7KB/tick** |

---

*Generated: 2026-01-24*
