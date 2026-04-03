# Optimization: Cache Gun Ammo Count

## Problem

`Jet.GetTotalGunAmmo()` is called every tick from `GridVisualization.DrawFlightData()`. For each gatling gun, it opens the inventory, iterates all items, and sums amounts. Inventory access is one of the more expensive SE API operations.

```csharp
// Called every tick in GridVisualization:
int ammo = jet.GetTotalGunAmmo();

// Which does:
public int GetTotalGunAmmo()
{
    int total = 0;
    for (int i = 0; i < _gatlings.Count; i++)
        total += GetGunAmmo(_gatlings[i]);  // Opens inventory, iterates items
    return total;
}
```

Additionally, `GunControlModule.GetTotalAmmo()` also calls `Jet.GetGunAmmo()` for each turret gun.

## Proposed Solution

Cache the total ammo count and refresh it every N ticks (e.g., every 30 ticks = 0.5 seconds):

```csharp
private int _cachedAmmo = 0;
private int _ammoCacheTick = 0;
private const int AMMO_CACHE_INTERVAL = 30;

public int GetTotalGunAmmo()
{
    if (GameTicks - _ammoCacheTick >= AMMO_CACHE_INTERVAL)
    {
        _ammoCacheTick = (int)GameTicks;
        _cachedAmmo = 0;
        for (int i = 0; i < _gatlings.Count; i++)
            _cachedAmmo += GetGunAmmo(_gatlings[i]);
    }
    return _cachedAmmo;
}
```

Ammo count doesn't need per-tick accuracy for HUD display purposes.

## Impact

- **Instruction savings**: ~20-40 instructions saved on 29 out of every 30 ticks (per gatling gun)
- **Visual impact**: Ammo display updates at 2Hz instead of 60Hz - imperceptible
- **Risk**: Very low
- **Files affected**: Jet.cs
