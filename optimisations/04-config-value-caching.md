# Optimization: Cache Config Values Per Tick

## Problem

`SystemManager.GetConfigValue()` is called many times per tick, especially from HUD rendering code which checks config toggles for every optional HUD element:

```csharp
// HUDModule.RenderHUD() - called every tick:
if (SystemManager.GetConfigValue("hud_fpm") > 0.5f) ...
if (SystemManager.GetConfigValue("hud_compass") > 0.5f) ...
if (SystemManager.GetConfigValue("hud_gforce") > 0.5f) ...
if (SystemManager.GetConfigValue("hud_aoa") > 0.5f) ...
if (SystemManager.GetConfigValue("hud_radar") > 0.5f) ...
if (SystemManager.GetConfigValue("hud_gun_funnel") > 0.5f) ...
if (SystemManager.GetConfigValue("hud_target_brackets") > 0.5f) ...
if (SystemManager.GetConfigValue("hud_breakaway") > 0.5f) ...
```

Each call goes through: `SystemManager.GetConfigValue()` -> `configModule.GetValue()` -> `allConfigs.ContainsKey()` + `allConfigs[key]` (two dictionary lookups per call).

`GunControlModule` has config properties that call this on every access:
```csharp
private float KP => SystemManager.GetConfigValue("gun_kp");
private float MAX_VELOCITY_RPM => SystemManager.GetConfigValue("gun_max_rpm");
```
These are accessed multiple times per turret per tick.

## Proposed Solution

### Option A: Fix the double-lookup (minimal change)
Change `ConfigurationModule.GetValue()` to use `TryGetValue`:

```csharp
public float GetValue(string configName)
{
    ConfigParam p;
    return allConfigs.TryGetValue(configName, out p) ? p.Value : 0f;
}
```

### Option B: Cache at call site (better for GunControlModule)
Cache gun control config values once per tick:

```csharp
// GunControlModule
private float _kp, _maxRpm, _lockThreshold, _muzzleVelocity, _maxRange;

public override void Tick()
{
    _kp = SystemManager.GetConfigValue("gun_kp");
    _maxRpm = SystemManager.GetConfigValue("gun_max_rpm");
    // ... etc, then use _kp instead of KP property
}
```

### Option C: Bitmask for HUD toggles (best for HUD)
Pack all HUD boolean toggles into a single int, computed once per tick:

```csharp
[Flags] enum HudFlags { Fpm=1, Compass=2, GForce=4, Aoa=8, Radar=16, GunFunnel=32, TargetBrackets=64, Breakaway=128 }
static HudFlags cachedFlags;
// Set once per tick, then: if ((cachedFlags & HudFlags.Compass) != 0) ...
```

## Impact

- **Option A**: Eliminates ~16 unnecessary dictionary lookups per tick (easy win)
- **Option B**: Eliminates ~10 dictionary lookups per tick in gun control
- **Option C**: Eliminates ~8 dictionary lookups per tick, replaces with bitwise AND
- **Risk**: Very low for all options
- **Files affected**: ConfigurationModule.cs, GunControlModule.cs, HUDModule.cs
