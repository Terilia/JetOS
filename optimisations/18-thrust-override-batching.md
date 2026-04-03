# Optimization: Reduce Thrust Override Write Frequency

## Problem

`HUDModule.UpdateThrottleControl()` calls `SetGroupOverride()` for 5 engine groups every tick:

```csharp
SetGroupOverride(myjet.leftEngines, leftOverride);
SetGroupOverride(myjet.rightEngines, rightOverride);
SetGroupOverride(myjet.centerEngines, scaledThrottle);

if (hydrogenswitch)
{
    SetGroupOverride(myjet.leftAB, 1f);
    SetGroupOverride(myjet.rightAB, 1f);
    SetGroupOverride(myjet.centerAB, 1f);
}
```

Each `SetGroupOverride` iterates all engines in the group:
```csharp
private static void SetGroupOverride(List<IMyThrust> group, float value)
{
    for (int i = 0; i < group.Count; i++)
    {
        if (group[i] != null && Ab(group[i].ThrustOverridePercentage - value) > 0.001f)
            group[i].ThrustOverridePercentage = value;
    }
}
```

The `0.001f` tolerance check prevents redundant writes, which is good. But reading `ThrustOverridePercentage` for every engine every tick is itself an API call.

## Proposed Solution

Track the last-written override value per group and skip the entire group iteration when the target value hasn't changed:

```csharp
private float _lastLeftOverride = -1f;
private float _lastRightOverride = -1f;
private float _lastCenterOverride = -1f;

// In UpdateThrottleControl():
if (Ab(leftOverride - _lastLeftOverride) > 0.001f)
{
    SetGroupOverride(myjet.leftEngines, leftOverride);
    _lastLeftOverride = leftOverride;
}
// ... same for right and center
```

When throttle is stable (which is most of the time during cruise), this skips all engine API calls entirely.

## Impact

- **Instructions saved**: ~10-20 per tick during stable throttle (skips all ThrustOverridePercentage reads)
- **Risk**: Very low - the tolerance check already exists at the per-engine level
- **Files affected**: HUDModule.cs
