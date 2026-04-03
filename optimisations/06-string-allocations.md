# Optimization: Reduce Per-Tick String Allocations

## Problem

Every tick, the HUD and MFD renderers create dozens of temporary strings via `ToString()`, `$""` interpolation, and `string.Format()`. In a garbage-collected environment with a 50k instruction limit, string allocations both consume instructions and create GC pressure.

## Hotspots

### HUD Instruments (every tick)
```csharp
currentSpeedKph.ToString("F0")        // speed tape
speedMark.ToString("F0")              // tick labels (multiple)
altMark.ToString("F0")                // altitude tick labels (multiple)
currentAltitude.ToString("F0")        // altitude readout
$"M {mach:F2}"                        // mach number
$"G: {gForces:F1}"                    // G-force
$"Max G: {peakGForce:F1}"            // peak G
$"{verticalVelocity:F0}"             // VVI
((int)heading).ToString("D3")         // compass heading
```

### Weapon Screen (every tick)
```csharp
$"{range / 1000:F2} km"              // range text
$"{Ab(closureRate):F0} {closureLabel}" // closure
$"{bearing:F0}\u00B0"                // bearing
$"{tgtSpeed:F0} m/s"                 // target speed
$"MSL {missile.BayIndex + 1}: ..."   // missile TOF (per active missile)
```

### Radar Module Console Output
```csharp
// StringBuilder allocated every tick in UpdateConsoleOutput()
var sb = new StringBuilder();
```

### GridVisualization (every tick)
```csharp
$"SPD {hud.smoothedVelocity:F0} kph"
$"ALT {hud.smoothedAltitude:F0} m"
$"AoA {aoa:F1}\u00B0"
$"MCH {hud.mach:F2}"
$"THR {hud.throttlePercent:F0}%"
ammo.ToString()
```

## Proposed Solutions

### 1. Reuse StringBuilder in RadarControlModule
```csharp
private StringBuilder _consoleSb = new StringBuilder(128);
// In UpdateConsoleOutput():
_consoleSb.Clear();  // instead of new StringBuilder()
```

### 2. Cache static/slow-changing strings
Values like heading, altitude, speed change smoothly. Only regenerate the string when the displayed integer value actually changes:

```csharp
private int _lastDisplayedSpeed = -1;
private string _speedText = "0";

int displaySpeed = (int)currentSpeedKph;
if (displaySpeed != _lastDisplayedSpeed)
{
    _lastDisplayedSpeed = displaySpeed;
    _speedText = displaySpeed.ToString();
}
```

### 3. Pre-format compass directions
The `GetCompassDirection()` method returns string literals which is already efficient. But the heading text `((int)heading).ToString("D3")` allocates every frame - could cache when heading integer changes.

### 4. Reduce formatting precision where not needed
Many `F2` formats are displayed at small font sizes where the extra decimal is invisible. Using `F1` or `F0` where appropriate reduces string length and cognitive load.

## Impact

- **Allocation reduction**: ~30-50 fewer string allocations per tick
- **Instruction savings**: ~2-5 instructions per avoided allocation + format
- **Risk**: Low - purely cosmetic changes, same visual output
- **Files affected**: InstrumentRenderer.cs, WeaponScreenRenderer.cs, RadarControlModule.cs, GridVisualization.cs
