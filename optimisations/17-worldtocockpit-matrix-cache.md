# Optimization: Cache worldToCockpitMatrix

## Problem

`HUDModule.RenderHUD()` computes `MatrixD.Transpose(cockpit.WorldMatrix)` and then passes it to ~5 different renderer methods. But `cockpit.WorldMatrix` is also accessed separately in other places during the same tick.

The bigger issue is that the transpose matrix is computed from `cockpit.WorldMatrix` which is itself an API call. If cockpit data is cached per tick (see optimization #01), this matrix should be computed once from the cached matrix.

## Current State

```csharp
// HUDModule.RenderHUD():
MatrixD worldToCockpitMatrix = MatrixD.Transpose(cockpit.WorldMatrix);  // API call + transpose

// Passed to:
DrawFlightPathMarker(frame, currentVelocity, worldToCockpitMatrix, ...)
DrawLeadingPip(frame, hud, worldToCockpitMatrix, ...)
DrawGunFunnel(frame, hud, worldToCockpitMatrix, ...)
DrawTargetBrackets(frame, hud, worldToCockpitMatrix, ...)
DrawFormationGhosts(frame, hud, worldToCockpitMatrix)
```

Also in `RadarRenderer`:
```csharp
MatrixD worldToLocal = MatrixD.Transpose(cockpit.WorldMatrix);  // Same transpose, SECOND API call
```

## Proposed Solution

With the per-tick cockpit cache from optimization #01:

```csharp
// Computed once in Jet.UpdatePerTickCache():
public MatrixD CachedWorldToCockpit;

public void UpdatePerTickCache()
{
    CachedWorldMatrix = _cockpit.WorldMatrix;
    CachedWorldToCockpit = MatrixD.Transpose(CachedWorldMatrix);
    // ...
}
```

All renderers use `myjet.CachedWorldToCockpit` instead of computing their own.

## Impact

- **Instructions saved**: Eliminates 1 redundant `MatrixD.Transpose()` call (~16 float copies) and 1 redundant `cockpit.WorldMatrix` API call
- **Risk**: None - the transpose of the world matrix is mathematically identical within a tick
- **Files affected**: Jet.cs (cache), HUDModule.cs, RadarRenderer.cs
