# Dead Code Audit — JetOS

Audit date: 2026-03-23. All items below are compiled into the final script (not excluded
by `.csproj`) but have zero references in the codebase.

> **Do not remove** — this document exists to track byte/character cost of unused code.

---

## 1. `RandomExtensions` class — `Extensions/RandomExtensions.cs`

**Entire file is dead.** The `NextFloat(this Random, float, float)` extension method is
never called anywhere.

| Lines | ~5 LOC (entire class body) |
|-------|----------------------------|
| File  | `Extensions/RandomExtensions.cs:7-10` |

---

## 2. `Jet.hud2` field — `Jet.cs:107`

```csharp
public IMyTextSurface hud2; // Back layer for parallax depth
```

Declared but never assigned or read. The parallax depth feature was never implemented.

---

## 3. `Jet.IsCockpitFunctional` property — `Jet.cs:443`

```csharp
public bool IsCockpitFunctional => _cockpit != null && _cockpit.IsFunctional;
```

Never read anywhere in the codebase.

---

## 4. `Jet.GetVelocityKnots()` method — `Jet.cs:458-461`

```csharp
public double GetVelocityKnots()
{
    return GetVelocity() * 1.94384;
}
```

Never called. The knots conversion is done inline in `SystemManager.Main()` instead.

---

## 5. `Jet.SetThrustOverride()` method — `Jet.cs:507-517`

```csharp
public void SetThrustOverride(float percentage)
{
    percentage = Cl(percentage, 0f, 1f);
    foreach (var thruster in _thrusters)
    {
        if (Ab(thruster.ThrustOverridePercentage - percentage) > 0.001f)
        {
            thruster.ThrustOverridePercentage = percentage;
        }
    }
}
```

Never called. `HUDModule.UpdateThrottleControl()` manages thrust directly via per-engine-group overrides.

---

## 6. `Jet.Thrusters` property — `Jet.cs:488`

```csharp
public IReadOnlyList<IMyThrust> Thrusters => _thrusters;
```

Never read anywhere. All consumers use `_thrusters` directly.

---

## 7. `Jet.HasGunAmmo()` method — `Jet.cs:605-608`

```csharp
public bool HasGunAmmo()
{
    return GetTotalGunAmmo() > 0;
}
```

Never called. Callers use `GetTotalGunAmmo()` directly and compare.

---

## 8. `Jet.GetGunCount()` method — `Jet.cs:613-622`

```csharp
public int GetGunCount()
{
    int count = 0;
    for (int i = 0; i < _gatlings.Count; i++)
    {
        if (_gatlings[i] != null && _gatlings[i].IsFunctional)
            count++;
    }
    return count;
}
```

Never called anywhere.

---

## 9. `SystemManager.thrusters` field — `SystemManager.cs:32`

```csharp
private static List<IMyThrust> thrusters = new List<IMyThrust>();
```

Assigned at line 69 (`thrusters = _myJet._thrusters`) but never read after assignment.

---

## 10. `SystemManager.GetMainLCD()` method — `SystemManager.cs:459-462`

```csharp
public static IMyTextSurface GetMainLCD()
{
    return lcdMain;
}
```

Never called anywhere.

---

## 11. `SystemManager.GetExtraLCD()` method — `SystemManager.cs:464-467`

```csharp
public static IMyTextSurface GetExtraLCD()
{
    return lcdExtra;
}
```

Never called anywhere.

---

## 12. `SystemManager.GetHUDModule()` method — `SystemManager.cs:474-477`

```csharp
public static HUDModule GetHUDModule()
{
    return hudProgram;
}
```

Never called anywhere.

---

## 13. `SystemManager.RemoveCustomDataValue()` method — `SystemManager.cs:124-127`

```csharp
public static void RemoveCustomDataValue(string key)
{
    CustomDataManager.RemoveValue(key);
}
```

Never called anywhere. (This also makes `CustomDataManager.RemoveValue()` effectively dead.)

---

## 14. `CustomDataManager.RemoveValue()` method — `Utilities/CustomDataManager.cs:69-77`

```csharp
public static void RemoveValue(string key)
{
    ParseCustomData();
    if (customDataCache.ContainsKey(key))
    {
        customDataCache.Remove(key);
        RebuildCustomData();
    }
}
```

Only called by the dead `SystemManager.RemoveCustomDataValue()`.

---

## 15. `RadarControlModule.GetRadarCount()` method — `Modules/RadarControlModule.cs:659-662`

```csharp
public int GetRadarCount()
{
    return allRadars.Count;
}
```

Never called from outside the class.

---

## 16. `RadarControlModule.IsRWREnabled` property — `Modules/RadarControlModule.cs:96`

```csharp
public bool IsRWREnabled { get { return rwrEnabled; } }
```

Never read from outside the class.

---

## 17. `RadarControlModule.IsThreat` property — `Modules/RadarControlModule.cs:97`

```csharp
public bool IsThreat { get { return anyThreatDetected; } }
```

Never read from outside the class.

---

## 18. `GunControlModule.IsLeftCalibrating` property — `Modules/GunControlModule.cs:450`

```csharp
public bool IsLeftCalibrating => false;
```

Always returns `false` — the calibration feature was removed. Still referenced by
`WeaponScreenRenderer.cs` but the branch is always dead at runtime.

---

## 19. `GunControlModule.IsRightCalibrating` property — `Modules/GunControlModule.cs:451`

```csharp
public bool IsRightCalibrating => false;
```

Same as above — always `false`, calibration was removed.

---

## 20. `HUDModule.stallWarningActive` field — `Modules/HUDModule.cs:147`

```csharp
internal bool stallWarningActive = false;
```

Written to in `InstrumentRenderer.cs:308` but never read anywhere. The stall state
doesn't drive any sound or other system.

---

## 21. `CircularBuffer<T>.Peek()` method — `Utilities/CircularBuffer.cs:28`

```csharp
public T Peek() => _queue.Peek();
```

Never called on any `CircularBuffer` instance.

---

## 22. `CircularBuffer<T>.Clear()` method — `Utilities/CircularBuffer.cs:30`

```csharp
public void Clear() => _queue.Clear();
```

Never called on any `CircularBuffer` instance. (The `.Clear()` calls in the codebase
are on `List<T>`, `Dictionary`, `StringBuilder`, etc. — not `CircularBuffer`.)

---

## 23. `CircularBuffer<T>.ToArray()` method — `Utilities/CircularBuffer.cs:32`

```csharp
public T[] ToArray() => _queue.ToArray();
```

Never called on any `CircularBuffer` instance. (All `.ToArray()` calls are on `List<string>`.)

---

## Summary

| # | Location | Type | Est. chars |
|---|----------|------|-----------|
| 1 | `RandomExtensions.cs` (entire file) | Class + method | ~200 |
| 2 | `Jet.cs:107` | Field | ~60 |
| 3 | `Jet.cs:443` | Property | ~75 |
| 4 | `Jet.cs:458-461` | Method | ~95 |
| 5 | `Jet.cs:507-517` | Method | ~275 |
| 6 | `Jet.cs:488` | Property | ~60 |
| 7 | `Jet.cs:605-608` | Method | ~75 |
| 8 | `Jet.cs:613-622` | Method | ~210 |
| 9 | `SystemManager.cs:32` | Field | ~60 |
| 10 | `SystemManager.cs:459-462` | Method | ~95 |
| 11 | `SystemManager.cs:464-467` | Method | ~95 |
| 12 | `SystemManager.cs:474-477` | Method | ~85 |
| 13 | `SystemManager.cs:124-127` | Method | ~85 |
| 14 | `CustomDataManager.cs:69-77` | Method | ~200 |
| 15 | `RadarControlModule.cs:659-662` | Method | ~70 |
| 16 | `RadarControlModule.cs:96` | Property | ~60 |
| 17 | `RadarControlModule.cs:97` | Property | ~60 |
| 18 | `GunControlModule.cs:450` | Property | ~50 |
| 19 | `GunControlModule.cs:451` | Property | ~50 |
| 20 | `HUDModule.cs:147` | Field | ~50 |
| 21 | `CircularBuffer.cs:28` | Method | ~45 |
| 22 | `CircularBuffer.cs:30` | Method | ~45 |
| 23 | `CircularBuffer.cs:32` | Method | ~45 |
| **Total** | | | **~2,250** |

All 23 items are compiled into the final MDK-minified script and contribute to the
SE programmable block's character/instruction budget without providing any runtime value.
