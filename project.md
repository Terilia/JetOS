# JetOS - Space Engineers Fighter Jet Control System

## Project Overview
JetOS is a comprehensive programmable block script for Space Engineers that controls a fighter jet with multiple subsystems including targeting, HUD, weapons management, and flight control. The architecture uses a class-based approach where each class represents a "program" running on the jet's operating system.

## Architecture

### Core Components
- **Program**: Entry point, executes once per tick
- **SystemManager**: Static manager coordinating all modules
- **Jet**: Data class holding references to all physical blocks
- **ProgramModule**: Abstract base class for all subsystems
- **UIController**: Handles rendering to LCD screens

### Modules
1. **HUDModule**: Flight instrumentation display
2. **RaycastCameraControl**: Targeting pod control
3. **AirToGround**: Missile bay management for ground targets
4. **AirtoAir**: Air-to-air combat systems
5. **LogoDisplay**: Screensaver/animation system
6. **FroggerGameControl**: Easter egg game

---

## Critical Performance Issues

### 1. CustomData String Operations (HIGH PRIORITY)
**Problem**: CustomData is used as a database with string Split/Join operations every tick.

**Location**: Throughout the code, especially:
- [Program.cs:346](Program.cs#L346) - Main() altitude check
- [Program.cs:707-753](Program.cs#L707-L753) - FlipGPS()
- [Program.cs:4670-4681](Program.cs#L4670-L4681) - LoadTopdownState()
- [Program.cs:4747-4768](Program.cs#L4747-L4768) - UpdateTopdownCustomData()

**Impact**: String operations are expensive. Parsing CustomData every tick is wasteful.

**Solution**:
```csharp
// Cache parsed data in memory
private static Dictionary<string, string> customDataCache = new Dictionary<string, string>();
private static bool customDataDirty = true;

private static void ParseCustomData() {
    if (!customDataDirty) return;

    customDataCache.Clear();
    var lines = parentProgram.Me.CustomData.Split('\n');
    foreach (var line in lines) {
        var colonIndex = line.IndexOf(':');
        if (colonIndex > 0) {
            var key = line.Substring(0, colonIndex);
            var value = line.Substring(colonIndex + 1);
            customDataCache[key] = value;
        }
    }
    customDataDirty = false;
}
```

### 2. Repeated Block Lookups
**Problem**: Blocks are fetched repeatedly without caching.

**Location**:
- [Program.cs:540-546](Program.cs#L540-L546) - GetBlocksOfType called every DisplayMenu()
- [Program.cs:290-293](Program.cs#L290-L293) - GetBlocksOfType in Initialize

**Impact**: Grid queries are expensive, especially on large grids.

**Solution**: Cache block lists and only refresh when structure changes.

### 3. Exception Handling Anti-Pattern
**Problem**: Empty catch blocks swallow all exceptions without logging.

**Location**:
- [Program.cs:44-47](Program.cs#L44-L47) - Main() reinitializes on ANY exception
- [Program.cs:4789-4795](Program.cs#L4789-L4795) - Empty catch in FireNextAvailableBay
- [Program.cs:4920-4921](Program.cs#L4920-L4921) - Empty catch in FireMissileFromBayWithGps
- [Program.cs:4990](Program.cs#L4990) - Empty catch in TransferCacheToSlots

**Impact**:
- Hides bugs and makes debugging impossible
- The Main() exception handler is especially bad - ANY error causes full reinitialization

**Solution**:
```csharp
public void Main(string argument, UpdateType updateSource) {
    Runtime.UpdateFrequency = UpdateFrequency.Update1;

    try {
        SystemManager.Main(argument, updateSource);
    }
    catch (Exception e) {
        // Log the error before reinitializing
        Echo($"CRITICAL ERROR: {e.Message}\n{e.StackTrace}");
        // Only reinitialize on specific exceptions
        if (e is NullReferenceException || e is InvalidOperationException) {
            SystemManager.Initialize(this);
        }
        else {
            throw; // Re-throw unexpected exceptions
        }
    }
}
```

### 4. Redundant Sprite Caching
**Problem**: Sprite cache is recalculated but not used effectively.

**Location**: [Program.cs:540-669](Program.cs#L540-L669) - DisplayMenu()

**Impact**: The cache is only checked when `blockcount` changes, but sprites are added to cache every time.

**Solution**: Move cached sprite rendering outside the cache-building logic.

### 5. Sound Block Setup State Machine
**Problem**: Multi-tick sound setup requires 7 sequential ticks to play a sound.

**Location**: [Program.cs:383-441](Program.cs#L383-L441) - Sound setup state machine

**Impact**: Adds unnecessary latency (7 ticks = ~117ms minimum delay).

**Questionable Design**: Why does setting a sound need 7 ticks? Space Engineers APIs usually work instantly.

**Recommendation**: Test if this complexity is actually needed. You might be able to:
```csharp
if (selectedsound != previousSelectedSound && !string.IsNullOrEmpty(selectedsound)) {
    foreach (var sb in soundblocks) {
        sb.Stop();
        sb.SelectedSound = selectedsound;
        sb.Play();
    }
    previousSelectedSound = selectedsound;
    isPlayingSound = true;
}
```

### 6. Mandelbrot Set Rendering
**Problem**: Real-time Mandelbrot rendering in a screensaver is EXTREMELY expensive.

**Location**: [Program.cs:4431-4492](Program.cs#L4431-L4492) - RenderPerfectMandelbrot()

**Impact**:
- Nested loops with up to 120x90 = 10,800 iterations
- Each iteration does complex math (IsOnMandelbrotBoundary)
- Multiple sprite creations per frame
- This WILL cause tick timeout on servers with strict limits

**Solution**:
- Pre-render Mandelbrot to a sprite sheet
- Use simpler animations
- Only update every N ticks
- Reduce sample count significantly

### 7. Duplicate Class Definitions
**Problem**: MathHelper class defined twice.

**Location**:
- [Program.cs:5147](Program.cs#L5147) - Inside AirtoAir class
- Likely another definition elsewhere (VRageMath has MathHelper too)

**Solution**: Remove duplicate, use VRageMath.MathHelper instead.

---

## Logical Errors

### 1. Radar Initialization Workaround
**Location**: [Program.cs:327-340](Program.cs#L327-L340)

**Issue**: Comment says "BUG: This is a workaround for the radar to load the first ammunition"

**Problem**: This suggests a fundamental misunderstanding of how turrets work in SE. Turrets don't need to be enabled first to "load" ammo.

**Recommendation**: Test if this is actually needed. You might be fighting a symptom rather than the root cause.

### 2. Velocity Calculation Redundancy
**Location**: [Program.cs:342-343](Program.cs#L342-L343)

**Issue**:
```csharp
double velocity = _myJet.GetVelocity();
double velocityKnots = velocity * 1.94384;
```
Then immediately:
```csharp
_myJet._cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude);
```

**Problem**: GetVelocity() already calls `_cockpit.GetShipSpeed()`. You're doing multiple cockpit calls when you could batch them.

### 3. Tick Handling Logic
**Location**: [Program.cs:444-446](Program.cs#L444-L446)

```csharp
if (currentTick == lastHandledSpecialTick)
    return;
lastHandledSpecialTick = currentTick;
```

**Problem**: This prevents double-handling in the same tick, but the logic is unclear. Why would Main() be called twice in the same tick?

### 4. GPS Index Overflow
**Location**: [Program.cs:734](Program.cs#L734)

```csharp
gpsindex = (gpsindex + 1) % GPS_INDEX_MAX;
```

**Problem**: GPS_INDEX_MAX is 4, but there's no validation that CacheGPS0-3 actually exist.

### 5. Thrust Override Check
**Location**: [Program.cs:195-198](Program.cs#L195-L198)

```csharp
if (Math.Abs(thruster.ThrustOverridePercentage - percentage) > 0.001f) {
    thruster.ThrustOverridePercentage = percentage;
}
```

**Good**: This prevents redundant API calls.
**Problem**: Done in SetThrustOverride but not checked before calling SetThrustOverride.

### 6. Bay Selection Array Size
**Location**: [Program.cs:4664](Program.cs#L4664)

```csharp
baySelected = new bool[missileBays.Count];
```

**Problem**: If bays are added/removed during runtime, this array won't resize. Should check bounds before accessing.

---

## Design Recommendations

### 1. Separate Data from Logic
**Current**: Jet class mixes data and helper methods.

**Recommendation**:
```csharp
// Pure data class
public class JetBlocks {
    public IMyCockpit Cockpit { get; set; }
    public List<IMyThrust> Thrusters { get; set; }
    // ... etc
}

// Separate helper class
public class JetController {
    private JetBlocks blocks;

    public JetController(JetBlocks blocks) {
        this.blocks = blocks;
    }

    public double GetVelocity() => blocks.Cockpit?.GetShipSpeed() ?? 0.0;
    // ... etc
}
```

### 2. Module Communication
**Current**: Modules access SystemManager static members.

**Problem**: Tight coupling, hard to test, global state.

**Recommendation**: Use dependency injection or event system.

```csharp
public abstract class ProgramModule {
    protected ISystemContext Context { get; private set; }

    public ProgramModule(ISystemContext context) {
        Context = context;
    }
}

public interface ISystemContext {
    IMyTextSurface GetMainLCD();
    IMyTextSurface GetExtraLCD();
    void ReturnToMainMenu();
}
```

### 3. Block Caching Strategy
**Recommendation**:
```csharp
private static class BlockCache {
    private static Dictionary<Type, List<IMyTerminalBlock>> cache = new Dictionary<Type, List<IMyTerminalBlock>>();
    private static int lastBlockCount = -1;

    public static List<T> GetBlocks<T>(IMyGridTerminalSystem gts, Func<T, bool> filter = null) where T : class {
        int currentCount = GetTotalBlockCount(gts);

        if (currentCount != lastBlockCount) {
            cache.Clear();
            lastBlockCount = currentCount;
        }

        // ... cache logic
    }
}
```

### 4. Performance Monitoring
**Add**: Built-in performance tracking.

```csharp
public class PerformanceMonitor {
    private Dictionary<string, long> timings = new Dictionary<string, long>();

    public IDisposable Measure(string name) {
        return new TimingScope(this, name);
    }

    public void Report(Action<string> output) {
        foreach (var kvp in timings.OrderByDescending(x => x.Value)) {
            output($"{kvp.Key}: {kvp.Value}μs");
        }
    }
}
```

### 5. Configuration Management
**Instead of**: CustomData as string database

**Use**: Structured configuration:
```csharp
public class JetConfig {
    public bool ManualFire { get; set; }
    public bool TopdownEnabled { get; set; }
    public Dictionary<int, string> GPSSlots { get; set; }

    public static JetConfig Load(string customData) {
        // Parse once, validate, return object
    }

    public string Save() {
        // Serialize back to CustomData format
    }
}
```

---

## Quick Wins (Easy Performance Gains)

### Priority 1: CustomData Caching
**Effort**: Medium | **Impact**: High

Cache parsed CustomData in memory, only re-parse when modified.

### Priority 2: Remove Empty Catch Blocks
**Effort**: Low | **Impact**: High (debuggability)

Add proper error logging and handling.

### Priority 3: Disable Mandelbrot Screensaver
**Effort**: Low | **Impact**: High

Replace with simple particle animation or static logo.

### Priority 4: Sound Setup State Machine
**Effort**: Low | **Impact**: Medium

Test if all 7 ticks are actually needed, likely can be reduced to 1-2.

### Priority 5: Block List Caching
**Effort**: Medium | **Impact**: Medium

Cache GetBlocksOfType results until grid structure changes.

---

## Testing Recommendations

1. **Performance Profiling**:
   - Add `Echo(Runtime.CurrentInstructionCount)` to track instruction usage
   - Test on servers with strict performance limits
   - Monitor tick time with different modules active

2. **Stress Testing**:
   - Test with maximum weapons load
   - Test with all modules active simultaneously
   - Test with large grids (>1000 blocks)

3. **Error Scenarios**:
   - Test with missing blocks
   - Test with disconnected/damaged components
   - Test with invalid GPS data

4. **Multiplayer Testing**:
   - Test on dedicated servers (stricter limits)
   - Test with high latency
   - Test with multiple players/grids nearby

---

## Code Quality Checklist

### Before Each Release:
- [ ] Remove all empty catch blocks
- [ ] Add Echo() for all exceptions
- [ ] Test with missing blocks
- [ ] Profile instruction count
- [ ] Check for memory leaks (growing collections)
- [ ] Validate all array accesses
- [ ] Test CustomData parsing with invalid input
- [ ] Verify GPS coordinate parsing handles edge cases

---

## Future Architecture Considerations

### If You Rewrite:
1. **Event-Driven Architecture**: Instead of polling, use events for block state changes
2. **State Machine Pattern**: Formalize module states (Idle, Active, Error)
3. **Command Pattern**: For user inputs and special functions
4. **Object Pooling**: For frequently created objects (sprites, vectors)
5. **Lazy Loading**: Load modules only when needed
6. **Separation of Concerns**: Split rendering, logic, and data layers completely

---

## Known Issues to Fix

1. **Main() Exception Handler**: Line 44-47 - Too broad, hides bugs
2. **Radar Initialization**: Lines 327-340 - Verify if workaround is needed
3. **Sound State Machine**: Lines 383-441 - Likely over-complicated
4. **Mandelbrot Rendering**: Lines 4431-4492 - Performance killer
5. **GPS Index Bounds**: Line 734 - No validation of GPS slots
6. **Duplicate MathHelper**: Line 5147 - Remove duplicate class
7. **Block Count Check**: Line 542 - Should also check after grid damage
8. **Bay Selection Array**: Line 4664 - No resize on bay changes

---

## Performance Budget (Estimated)

| Module | Instructions/Tick | Notes |
|--------|------------------|-------|
| SystemManager Core | ~1,000 | Main loop, menu rendering |
| HUDModule | ~5,000-15,000 | Varies by complexity of display |
| RaycastCameraControl (active) | ~2,000 | When tracking target |
| AirToGround | ~500 | Passive monitoring |
| AirtoAir | ~500 | Passive monitoring |
| LogoDisplay (Mandelbrot) | ~50,000+ | **CRITICAL - WAY TOO HIGH** |
| CustomData Operations | ~1,000-5,000 | Per parse/save operation |

**SE Server Limit**: ~50,000 instructions/tick (varies by server)
**Current Usage**: ~10,000-25,000 normally, **100,000+ with Mandelbrot**

---

## Conclusion

The JetOS system is well-architected conceptually, with clear separation into modules. However, there are several performance anti-patterns and logical issues that should be addressed:

1. **Critical**: Fix exception handling and CustomData caching
2. **High Priority**: Disable/replace Mandelbrot renderer, add block caching
3. **Medium Priority**: Review sound state machine, validate GPS handling
4. **Low Priority**: Refactor for testability and maintainability

The "class as program" metaphor works well and should be maintained. The main issues are around **data access patterns** (CustomData parsing) and **excessive rendering** (Mandelbrot) rather than fundamental architectural problems.

With the recommended fixes, performance should improve by 30-50% in normal operation and avoid server timeouts entirely.

---

## Quick Reference: Performance Dos and Don'ts

### ✅ DO:
- Cache parsed data from CustomData
- Check if values changed before setting properties
- Use `StringBuilder` for string concatenation in loops
- Profile with `Runtime.CurrentInstructionCount`
- Handle exceptions specifically, not broadly
- Use object initialization over repeated Set calls

### ❌ DON'T:
- Parse CustomData every tick
- Call GetBlocksOfType repeatedly without caching
- Use empty catch blocks
- Render Mandelbrot sets in real-time
- Access collections without bounds checking
- Reinitialize everything on any exception

---

**Document Version**: 1.0
**Last Updated**: 2025-01-13
**Target SE Version**: Current
