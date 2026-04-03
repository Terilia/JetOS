# Optimization: Cache Cockpit API Calls Per Tick

## Problem

Multiple modules call the same expensive SE API methods on `IMyCockpit` every tick:

- `cockpit.GetShipVelocities()` - called in HUDModule (3x), RadarRenderer, WeaponScreenRenderer, GunControlModule (per turret)
- `cockpit.GetShipSpeed()` - called in HUDModule and Jet
- `cockpit.WorldMatrix` - called in HUDModule (2x), GunControlModule (3x+), RadarRenderer, TargetingRenderer
- `cockpit.GetPosition()` - called in HUDModule, RadarRenderer, Jet, RadarControlModule
- `cockpit.GetNaturalGravity()` - cached in `Jet.CachedGravity` already, but `NavigationHelper.CalculateHeading()` calls it again independently

Each of these is a cross-boundary call into the SE game engine. Even if SE caches internally, the managed/native transition has overhead.

## Current State

```csharp
// HUDModule.UpdateFlightData() - call 1
velocity = cockpit.GetShipSpeed();
Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;

// HUDModule.RenderHUD() - call 2
Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;

// RadarRenderer - call 3
Vector3D cockpitVel = cockpit.GetShipVelocities().LinearVelocity;

// GunControlModule.TrackTarget() - call 4 (per turret!)
Vector3D shooterVelocity = cockpit.GetShipVelocities().LinearVelocity;
```

## Proposed Solution

Add cached flight data fields to `Jet` that get updated once at the start of `SystemManager.Main()`:

```csharp
// In Jet.cs
public Vector3D CachedVelocity;
public double CachedSpeed;
public MatrixD CachedWorldMatrix;
public Vector3D CachedPosition;

public void UpdatePerTickCache()
{
    if (_cockpit == null) return;
    CachedWorldMatrix = _cockpit.WorldMatrix;
    CachedPosition = _cockpit.GetPosition();
    var shipVel = _cockpit.GetShipVelocities();
    CachedVelocity = shipVel.LinearVelocity;
    CachedSpeed = _cockpit.GetShipSpeed();
    CachedGravity = _cockpit.GetNaturalGravity();
}
```

Then replace all direct cockpit API calls with the cached values.

## Impact

- **Instruction savings**: ~15-25 instructions per eliminated API call, ~6-10 calls per tick = ~90-250 instructions saved
- **Risk**: Low - values are consistent within a single tick anyway (SE processes physics between ticks)
- **Files affected**: Jet.cs, SystemManager.cs, HUDModule.cs, GunControlModule.cs, RadarRenderer.cs, NavigationHelper.cs
