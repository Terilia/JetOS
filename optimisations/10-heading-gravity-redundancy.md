# Optimization: Eliminate Redundant Gravity Call in Heading Calculation

## Problem

`NavigationHelper.CalculateHeading()` calls `cockpit.GetNaturalGravity()` independently, even though `Jet.CachedGravity` already holds the same value cached at the start of the tick.

```csharp
// NavigationHelper.CalculateHeading() - line 17
Vector3D gravity = cockpit.GetNaturalGravity();  // Redundant API call!

// Already cached in SystemManager.Main():
_myJet.CachedGravity = _myJet._cockpit.GetNaturalGravity();
```

## Proposed Solution

Add an overload that accepts pre-computed gravity and world-up:

```csharp
public static double CalculateHeading(IMyCockpit cockpit, Vector3D gravity)
{
    Vector3D worldUp;
    if (gravity.LengthSquared() > 1e-6)
        worldUp = -VN(gravity);
    else
        worldUp = Vector3D.Up;
    // ... rest of calculation using worldUp ...
}
```

Call from HUDModule:
```csharp
double heading = NavigationHelper.CalculateHeading(cockpit, myjet.CachedGravity);
```

## Impact

- **Instruction savings**: ~10-15 instructions (one fewer API call)
- **Risk**: None - pure pass-through of already-available data
- **Files affected**: NavigationHelper.cs, HUDModule.cs
