# Optimization: Cache Fuel and Battery Status

## Problem

Fuel and battery status are computed in multiple places per tick:

1. **`StatusPanelRenderer.Render()`** calls `jet.GetFuelStatus()` and `jet.GetBatteryStatus()` - iterates all tanks and batteries
2. **`GridVisualization.DrawFuelBar()`** iterates all tanks again independently with its own inline loop
3. Both run every tick

```csharp
// StatusPanelRenderer:
jet.GetFuelStatus(out fuelPct, out fuelSec);
jet.GetBatteryStatus(out curMWh, out maxMWh, out netDrain);

// GridVisualization.DrawFuelBar() - SEPARATE loop over same tanks:
foreach (var t in tanks)
    if (t.BlockDefinition.SubtypeId.Contains("Hydrogen"))
    { cap += t.Capacity; filled += t.Capacity * t.FilledRatio; }
```

Fuel level doesn't change noticeably between two calls in the same tick. Even across ticks, updating every 30 ticks (0.5s) would be sufficient for display.

## Proposed Solution

Cache fuel and battery status in `Jet` with a refresh interval:

```csharp
// In Jet:
public double CachedFuelRatio;
public double CachedFuelSeconds;
public float CachedBatteryMWh, CachedBatteryMaxMWh, CachedBatteryDrain;
private int _fuelCacheTick = 0;

public void UpdateFuelCache()
{
    if (GameTicks - _fuelCacheTick < 30) return;
    _fuelCacheTick = (int)GameTicks;
    GetFuelStatus(out CachedFuelRatio, out CachedFuelSeconds);
    GetBatteryStatus(out CachedBatteryMWh, out CachedBatteryMaxMWh, out CachedBatteryDrain);
}
```

Both `StatusPanelRenderer` and `GridVisualization` read the cached values instead of recomputing.

## Impact

- **Instruction savings**: Eliminates duplicate tank/battery iteration. With 4 tanks and 2 batteries, saves ~30-50 instructions per tick.
- **Risk**: Very low - fuel display updates at 2Hz instead of 60Hz
- **Files affected**: Jet.cs, StatusPanelRenderer.cs, GridVisualization.cs
