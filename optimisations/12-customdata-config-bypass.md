# Optimization: ConfigurationModule Bypasses CustomDataManager

## Problem

`ConfigurationModule` reads and writes CustomData independently of `CustomDataManager`, using its own parsing logic:

```csharp
// ConfigurationModule.LoadFromCustomData() - parses raw CustomData directly:
string customData = ParentProgram.Me.CustomData;
string[] lines = customData.Split('\n');
foreach (string line in lines)
{
    if (line.StartsWith("Config:"))
    {
        string[] parts = line.Substring(7).Split(':');
        // ...
    }
}
```

Meanwhile, `CustomDataManager` has its own dictionary cache of the same CustomData, with its own key:value format. They're two independent caching layers over the same string.

Worse: `ConfigurationModule.SaveToCustomData()` writes directly to `ParentProgram.Me.CustomData` then calls `SystemManager.MarkCustomDataDirty()` to force CustomDataManager to re-parse. This means every config save triggers a full re-parse of ALL CustomData.

## Current Architecture

```
CustomData string
    ├── CustomDataManager (key:value cache, used by GPS/radar/etc)
    └── ConfigurationModule (Config:name:value, separate parsing)
```

Both read the same raw string but parse it differently.

## Proposed Solution

### Option A: Store config in CustomDataManager
Use `CustomDataManager.SetValue("Config:gun_kp", "5.0")` format. ConfigurationModule reads via `CustomDataManager.GetValue()` instead of parsing raw CustomData.

This eliminates:
- Duplicate CustomData parsing
- The manual `Split('\n')` loop
- The need for `MarkCustomDataDirty()` after saves

### Option B: Keep config in-memory only, save on change
Config values are already cached in the `allConfigs` dictionary. Only read from CustomData on startup. Only write to CustomData when a value changes. No per-tick parsing needed at all.

This is actually what the current code does, except `LoadFromCustomData()` re-parses every time it's called. Making it constructor-only (which it currently is) means no ongoing cost.

The real fix is just ensuring `SaveToCustomData()` doesn't break CustomDataManager's cache. Currently it does, causing a full re-parse.

## Impact

- **Complexity reduction**: One CustomData parsing system instead of two
- **Risk**: Low - config format in CustomData changes but functionality is identical
- **Files affected**: ConfigurationModule.cs, possibly CustomDataManager.cs
