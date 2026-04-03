# Optimization: Ballistics Calculator Early Exit for Far Targets

## Problem

`BallisticsCalculator.CalculateInterceptPoint()` runs Newton's method iterations for every target, even when the target is clearly out of range. With 10 iterations and the quartic polynomial evaluation, this is ~100+ floating-point operations per call.

It's called from:
1. `HUDModule.RenderHUD()` - for the selected enemy (lead pip)
2. `GunControlModule.TrackTarget()` - for each turret's target

## Current State

The function does check `if (t <= 0) return false` after the initial quadratic solution, but doesn't check if the resulting time-to-intercept is reasonable before entering Newton's method.

## Proposed Solution

Add an early range check before the expensive computation:

```csharp
public static bool CalculateInterceptPoint(
    Vector3D shooterPosition, Vector3D shooterVelocity, double muzzleSpeed,
    Vector3D targetPosition, Vector3D targetVelocity, int maxIterations,
    out Vector3D interceptPoint, out double timeToIntercept, out Vector3D aimPoint,
    Vector3D targetAcceleration = default(Vector3D))
{
    interceptPoint = VZ; timeToIntercept = -1; aimPoint = VZ;

    Vector3D D = targetPosition - shooterPosition;
    double rangeSq = D.LengthSquared();

    // Quick range check: if target is beyond muzzle_velocity * 10 seconds,
    // the bullet can't reach it in any reasonable time
    double maxRange = muzzleSpeed * 10.0;
    if (rangeSq > maxRange * maxRange)
        return false;

    // ... rest of calculation
}
```

Also, reduce `INTERCEPT_ITERATIONS` from 10 to 6 for the HUD lead pip. The gun control already uses 6. The difference in accuracy is negligible at display distances.

## Impact

- **Instructions saved**: ~100+ per far-target call (skips entire Newton's method)
- **Risk**: Very low - targets beyond 10-second flight time aren't hittable anyway
- **Files affected**: BallisticsCalculator.cs, HUDModule.cs (reduce INTERCEPT_ITERATIONS constant)
