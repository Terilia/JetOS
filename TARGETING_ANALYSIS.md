# JetOS Targeting System — Full Analysis

## Data Structures

### TargetSlot struct (Jet.cs:25-56) — 5-slot array

| Field | Type | Description |
|---|---|---|
| `IsOccupied` | bool | Whether slot holds valid data |
| `Position` | Vector3D | World position |
| `Velocity` | Vector3D | Velocity vector |
| `Acceleration` | Vector3D | EMA-filtered acceleration |
| `Name` | string | Identifier ("Raycast", grid name, etc.) |
| `TimestampTicks` | long | `GameTicks` when last updated |
| `AgeTicks` / `AgeSeconds` | computed | Age since last update |

### EnemyContact struct (Jet.cs:62-85) — master enemy list with decay

| Field | Type | Description |
|---|---|---|
| `Position`, `Velocity`, `Acceleration` | Vector3D | Kinematic state |
| `Name` | string | Grid name |
| `EntityId` | long | SE EntityId (0 if unknown) |
| `LastSeenTicks` | long | GameTicks when last updated |
| `SourceIndex` | int | Which AI pair detected it (0=primary, 1=RWR, etc.) |

### activeSlotIndex (Jet.cs:59)

Which of the 5 slots is "selected" for weapons/HUD.

---

## Target Acquisition (Writers)

### 1. RaycastCameraControl (targeting pod)
- **Writes to**: `targetSlots[FindEmptyOrOldestSlot()]` — does NOT auto-activate
- **Writes to**: `enemyList` via `UpdateOrAddEnemy()` (source=-1)
- **Writes to**: CustomData `Cached` + `CachedSpeed`
- **Name**: `"Raycast"`
- **No acceleration** — defaults to zero

### 2. AirtoAir — Passive Mode (lines 171-187)
- **Writes to**: `targetSlots[FindSlotForTarget()]` — reuses existing slot by name, or empty/oldest
- **Does NOT** change `activeSlotIndex`
- Looks up acceleration from `enemyList` via `LookupEnemyAcceleration()`
- Multiple radars can fill different slots simultaneously

### 3. AirtoAir — Active Mode (lines 189-211)
- **Writes to**: `targetSlots[]` same as passive
- **Additionally**: primary radar (i==0) auto-sets `activeSlotIndex` and calls `UpdateActiveTargetGPS()`
- This is the only module that auto-activates a slot

### 4. RadarControlModule (lines 199-217)
- **Writes to**: `enemyList` via `UpdateOrAddEnemy()` with source index per AI pair
- **Does NOT** write to `targetSlots` directly — AirtoAir reads from the radar modules instead
- Also runs `UpdateEnemyDecay()` every tick and produces RWR threat analysis

---

## Target Consumption (Readers)

### HUDModule + HUD renderers
- **Reads**: `targetSlots[activeSlotIndex]` — position, velocity, acceleration for lead pip
- **Reads**: All `targetSlots[*]` — positions for radar scope display (up to 10 targets)
- **Calls**: `BallisticsCalculator.CalculateInterceptPointIterative()` for lead pip computation
- Pure reader — writes nothing to target data

### GunControlModule
- **Reads**: `enemyList` (NOT targetSlots) — iterates all enemies, picks closest within cone of `cockpit.WorldMatrix.Forward`
- **Calls**: `BallisticsCalculator` with enemy position/velocity/acceleration + MUZZLE_VELOCITY=1100
- Drives rotor/hinge motors to aim — writes no target data

### RaycastCameraControl (tracking mode)
- **Reads**: `targetSlots[activeSlotIndex].Position` to physically point the camera servo at the active target

### AirToGround (missile fire)
- **Reads**: `targetSlots[activeSlotIndex].Position` for bombardment center
- **Falls back** to CustomData `Cached` key if slot empty
- Writes bombardment offsets to bay-specific cache keys

### AirtoAir (missile fire)
- **Reads**: CustomData `Cached` key for GPS to program missiles

---

## CustomData Target Keys

| Key | Format | Writers | Readers |
|---|---|---|---|
| `Cached` | `GPS:Target:X:Y:Z:#FF75C9F1:` | SystemManager, RaycastCameraControl, AirtoAir | AirToGround, AirtoAir (missile fire) |
| `CachedSpeed` | `X:Y:Z:#FF75C9F1:` | SystemManager, RaycastCameraControl, AirtoAir | Missile scripts (external) |
| `Cache0`-`CacheN` | GPS format | AirToGround, AirtoAir | Same modules (pre-fire staging) |
| `0`-`4` | GPS format | AirToGround, AirtoAir | External missile scripts in bays |

---

## EnemyList Management

- **`UpdateOrAddEnemy()`** (Jet.cs:179-246): 3-tier dedup — EntityId > Name > Position proximity (50m). EMA acceleration (60% old + 40% new), only if update < 5 seconds old.
- **`UpdateEnemyDecay()`** (Jet.cs:252-267): Every 60 ticks, removes contacts older than 3 minutes.
- **`GetClosestNEnemies()`** (Jet.cs:277-305): Returns N closest enemies with pre-allocated sort/result buffers.

---

## Slot Selection Helpers

- **`FindSlotForTarget(name)`** (Program.cs:53-68): Finds existing slot with same name, falls back to `FindEmptyOrOldestSlot()`
- **`FindEmptyOrOldestSlot()`** (Program.cs:71-96): First empty slot, or oldest by timestamp
- **`FlipGPS()`** (SystemManager.cs:314-342): Cycles `activeSlotIndex` to next occupied non-stale slot; triggered by toolbar key 8

---

## Data Flow

```
 SENSORS                          STORAGE                    CONSUMERS
 -------                          -------                    ---------

 Raycast Camera ------+
                      +---> targetSlots[0-4] ---> HUD (lead pip + radar scope)
 AirtoAir (passive) --+         |                 Targeting Pod (servo tracking)
                      |         |                 AirToGround (bombardment center)
 AirtoAir (active) ---+         |
   (also sets         |    activeSlotIndex
    activeSlotIndex)  |         |
                      |         v
                      |  UpdateActiveTargetGPS()
                      |         |
                      |         v
                      |  CustomData[Cached/CachedSpeed] --> Missile fire (GPS programming)
                      |                                     Bay slots (Cache0-N -> 0-4)
                      |
 RadarControlModule --+---> enemyList ---> GunControlModule (auto-tracking)
   (all AI pairs)              |           AirtoAir (acceleration lookup)
                               |           RadarControlModule (RWR threat analysis)
                               |
                        3-min decay cleanup
```

---

## Known Issues

1. **FlipGPS stale check** uses `DateTime.Now.Ticks` (real time) but `TimestampTicks` uses `GameTicks` — completely different time domains, comparison is broken
2. **Dual-write to CustomData**: Both `RaycastCameraControl` and `SystemManager.UpdateActiveTargetGPS()` write to `Cached`/`CachedSpeed` — potential race
3. **GunControl reads enemyList, not targetSlots** — tracks independently from the pilot's selected target (gun may aim at different enemy than HUD lead pip)
4. **Raycast doesn't auto-activate** — user must manually FlipGPS, while active radar auto-selects
5. **No acceleration from raycast** — TargetSlot acceleration always zero for raycast; only enemyList computes EMA
6. **enemyList and targetSlots are disconnected** — target can exist in one but not the other; acceleration lookup can silently fail if names don't match
