# Optimization: Reduce SystemManager Static Field Proliferation

## Problem

`SystemManager` holds individual static references to specific modules:

```csharp
private static Program.HUDModule hudProgram;
private static Program.ConfigurationModule configModule;
private static Program.RadarControlModule radarControlModule;
private static Program.AirtoAir airtoAirModule;
private static Program.GunControlModule gunControlModule;
private static Program.TerrainModule terrainModule;
private static Program.AeroRecorderModule aeroRecorder;
```

These are in addition to the `modules` list that already contains all of them. Each new module requires:
1. A new static field
2. Assignment in `Initialize()`
3. A background-tick block in `Main()`
4. Possibly a public getter method (like `GetGunControl()`)

This pattern couples SystemManager tightly to every module type.

## Proposed Solution

Use the `modules` list as the single source of truth. Access specific modules by type when needed:

```csharp
// Generic accessor (cached after first call per type):
private static Dictionary<Type, ProgramModule> _moduleByType = new Dictionary<Type, ProgramModule>();

public static T GetModule<T>() where T : ProgramModule
{
    ProgramModule m;
    if (!_moduleByType.TryGetValue(typeof(T), out m))
    {
        for (int i = 0; i < modules.Count; i++)
        {
            if (modules[i] is T)
            {
                m = modules[i];
                _moduleByType[typeof(T)] = m;
                break;
            }
        }
    }
    return (T)m;
}
```

However, note that SE's scripting environment may not support generics with `typeof(T)` efficiently. A simpler alternative is to keep the explicit fields but combine with optimization #07 (background tick loop) to at least remove the tick blocks.

## Impact

- **Maintainability**: Adding a new module doesn't require touching SystemManager
- **Risk**: Medium - the generic approach may not be worth the complexity in SE's constrained environment. The main win is combining this with #07 for the tick loop.
- **Files affected**: SystemManager.cs
