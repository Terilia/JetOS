# Optimization: Reduce GetOptions() Allocations

## Problem

Every module's `GetOptions()` creates new `List<string>` and/or `string[]` arrays every time it's called. `GetOptions()` is called every tick during `DisplayMenu()` -> `NavigateDown()` (to check total options count) AND during rendering.

### Worst offenders:

**RadarControlModule.GetOptions()** - creates a `new List<string>()`, does `string.Format()` for each radar state, converts to array via `.ToArray()`. With 5 radars, that's ~10+ string allocations per tick.

**ConfigurationModule.GetOptions()** - at `ParameterList` level, creates a `new List<string>()`, iterates all configs, formats each value string, calls `.ToArray()`.

**AirToGround/AirtoAir.GetOptions()** - creates `new List<string>`, adds formatted bay options, `.ToArray()`.

**SystemManager.NavigateDown()** calls `currentModule.GetOptions().Length` just to check bounds - creating an entire options array just to read its length.

## Proposed Solution

### 1. Cache options array and dirty-flag it
```csharp
// In ProgramModule base class:
private string[] _cachedOptions;
private bool _optionsDirty = true;

public string[] Options
{
    get
    {
        if (_optionsDirty || _cachedOptions == null)
        {
            _cachedOptions = GetOptions();
            _optionsDirty = false;
        }
        return _cachedOptions;
    }
}

protected void InvalidateOptions() { _optionsDirty = true; }
```

Modules call `InvalidateOptions()` in `ExecuteOption()`, `HandleNavigation()`, or when state changes.

### 2. Add GetOptionCount() to avoid full rebuild
```csharp
// In ProgramModule:
public virtual int GetOptionCount() => GetOptions().Length;
```

Override in modules where the count is trivially known (e.g., `HUDModule` always returns 1).

### 3. Use pre-allocated lists
Instead of `new List<string>()`, keep a `List<string>` field and `.Clear()` it:
```csharp
private List<string> _optionBuffer = new List<string>();

public override string[] GetOptions()
{
    _optionBuffer.Clear();
    // ... add options ...
    return _optionBuffer.ToArray(); // still allocates, but the list doesn't
}
```

Even better: change the interface to return `List<string>` instead of `string[]` to avoid the `.ToArray()` copy.

## Impact

- **Allocation reduction**: Eliminates ~10-20 string[] allocations per tick
- **Risk**: Low for option 1 and 3. Option 2 requires interface change.
- **Files affected**: ProgramModule.cs, all module files, SystemManager.cs, UIController.cs
