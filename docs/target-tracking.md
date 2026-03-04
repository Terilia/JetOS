# Target Tracking & Data Flow

## Overview

Targets flow from sensors through a central enemy list to consumers (HUD, weapons, gun turrets). Three independent sensor types feed into one shared `enemyList` on the `Jet` class.

```mermaid
flowchart TD
    subgraph Sensors ["Acquisition Layer"]
        RAY["RaycastCameraControl\n(camera raycast, 35km)"]
        ATA["AirtoAir\n(AI block radar lock)"]
        RCM["RadarControlModule\n(scan + track + RWR)"]
    end

    subgraph Storage ["Central Storage"]
        UOA["Jet.UpdateOrAddEnemy()\n3-tier deduplication"]
        EL["Jet.enemyList\nList&lt;EnemyContact&gt;"]
        PIN["Jet.pinnedRaycastTarget\n(static, no decay)"]
        SEL["Jet.GetSelectedEnemy()\nidentity-based lookup"]
    end

    subgraph Consumers ["Consumption Layer"]
        HUD["HUDModule\nlead pip, radar scope,\ntarget brackets"]
        GUN["GunControlModule\nclosest enemy in cone,\nauto-aim turrets"]
        FIRE["AirtoAir / AirToGround\nmissile GPS programming"]
        TGP["RaycastCameraControl\nservo tracking"]
    end

    RAY --> UOA
    RAY --> PIN
    ATA --> UOA
    RCM --> UOA

    UOA --> EL
    EL --> |"decay every 60 ticks"| EL
    EL --> SEL
    PIN --> SEL

    SEL --> HUD
    EL --> GUN
    SEL --> FIRE
    SEL --> TGP

    style PIN fill:#8b6914
    style SEL fill:#2d5a2d
```

---

## EnemyContact Structure

Each contact in `enemyList` holds:

| Field | Type | Description |
|-------|------|-------------|
| Position | Vector3D | World position |
| Velocity | Vector3D | Velocity vector |
| Acceleration | Vector3D | EMA-filtered (60% old + 40% new) |
| Name | string | Grid name or "Raycast" |
| EntityId | long | SE entity ID (0 if unknown) |
| LastSeenTicks | long | GameTicks when last updated |
| SourceIndex | int | 0=scan, 1=track, 2+=RWR, -1=raycast |

**Source:** `Jet.cs` — `EnemyContact` struct

---

## Contact Deduplication

When a sensor reports a target, `UpdateOrAddEnemy()` tries to match it against existing contacts using a 3-tier priority system:

```mermaid
flowchart TD
    NEW["New detection:\npos, vel, name, entityId"] --> P1{EntityId match?}
    P1 -- "Yes" --> UPDATE["Update existing contact"]
    P1 -- "No" --> P2{Name match?}
    P2 -- "Yes" --> UPDATE
    P2 -- "No" --> P3{"Position within 50m\nof existing contact?"}
    P3 -- "Yes" --> UPDATE
    P3 -- "No" --> ADD["Add new contact"]

    UPDATE --> ACCEL{"Time delta\n0-300 ticks?"}
    ACCEL -- "Yes" --> EMA["Compute acceleration\nraw = (vel - prevVel) / dt\naccel = 0.6 * old + 0.4 * raw"]
    ACCEL -- "No" --> SKIP["Keep existing acceleration"]
```

**Source:** `Jet.cs` — `UpdateOrAddEnemy()` method

---

## Contact Lifecycle

```mermaid
sequenceDiagram
    participant Sensor as Radar/Raycast
    participant Jet as Jet.enemyList
    participant Decay as UpdateEnemyDecay()
    participant Consumer as HUD/Weapons

    Sensor->>Jet: UpdateOrAddEnemy(pos, vel, name, source)
    Note over Jet: Deduplicate (EntityId → Name → 50m proximity)
    Note over Jet: Compute EMA acceleration if < 5s old
    Jet->>Jet: Update LastSeenTicks = GameTicks

    loop Every tick
        Consumer->>Jet: GetSelectedEnemy() / GetClosestNEnemies()
        Jet-->>Consumer: EnemyContact (or null)
    end

    loop Every 60 ticks
        Decay->>Jet: Remove contacts where AgeTicks > CONTACT_DECAY_TICKS
        Note over Jet: Stale contacts removed
    end
```

**Source:** `Jet.cs` — `UpdateOrAddEnemy()`, `UpdateEnemyDecay()`, `GetSelectedEnemy()`

---

## Target Selection

The pilot selects targets via `FlipGPS()` (toolbar key 8), which cycles through enemies sorted by distance:

```mermaid
flowchart TD
    FLIP["FlipGPS() — toolbar key 8"] --> SORT["GetEnemiesSortedByDistance()"]
    SORT --> FIND["Find current selection in sorted list\n(match by EntityId, then Name)"]
    FIND --> NEXT["Advance to next entry (wrapping)"]
    NEXT --> ISPIN{"Is next entry the\npinned raycast target?"}
    ISPIN -- "Yes" --> SELPIN["Jet.SelectPinned()"]
    ISPIN -- "No" --> SELEN["Jet.SelectEnemy(contact)"]
    SELPIN --> GPS["UpdateActiveTargetGPS()"]
    SELEN --> GPS
```

### Selection Priority in GetSelectedEnemy()

```mermaid
flowchart TD
    GET["GetSelectedEnemy()"] --> PINQ{"isPinnedSelected\n&& pinnedRaycastTarget != null?"}
    PINQ -- "Yes" --> RETPIN["Return pinned target"]
    PINQ -- "No" --> EIDQ{"selectedEnemyEntityId != 0?"}
    EIDQ -- "Yes (match in list)" --> RETEID["Return by EntityId"]
    EIDQ -- "No" --> NAMEQ{"selectedEnemyName != empty?"}
    NAMEQ -- "Yes (match in list)" --> RETNAME["Return by Name"]
    NAMEQ -- "No" --> RETNULL["Return null"]
```

**Source:** `Jet.cs` — `GetSelectedEnemy()`, `SelectEnemy()`, `SelectPinned()`; `SystemManager.cs` — `FlipGPS()`

---

## GPS Sync to CustomData

When a target is selected, its GPS coordinates are written to the programmable block's CustomData so missile scripts can read them:

```mermaid
flowchart LR
    SEL["Selected Enemy\n(pos + vel)"] --> UGPS["UpdateActiveTargetGPS()"]
    UGPS --> CD_C["CustomData\nCached = GPS:Target:X:Y:Z:..."]
    UGPS --> CD_S["CustomData\nCachedSpeed = X:Y:Z:..."]

    subgraph Fire ["Missile Fire Sequence"]
        CD_C --> BAY["Cache{N} = GPS string"]
        BAY --> MERGE["bay.ApplyAction('Fire')"]
        MERGE --> XFER["TransferCacheToSlots()\nCache{N} → slot {N}"]
        XFER --> MSL["Missile script reads\nCustomData slot {N}"]
    end
```

### CustomData Key Map

| Key | Format | Writers | Readers |
|-----|--------|---------|---------|
| `Cached` | `GPS:Target:X:Y:Z:#FF75C9F1:` | SystemManager, RaycastCamera, AirtoAir | Weapon modules (missile GPS) |
| `CachedSpeed` | `X:Y:Z:#FF75C9F1:` | SystemManager, RaycastCamera, AirtoAir | External missile scripts |
| `Cache0`-`CacheN` | GPS format | AirToGround, AirtoAir | Same modules (pre-fire staging) |
| `0`-`4` | GPS format | AirToGround, AirtoAir | Detached missile scripts |
| `Topdown` | `true`/`false` | AirToGround | AirToGround (persisted toggle) |

**Source:** `SystemManager.cs` — `UpdateActiveTargetGPS()`, `FlipGPS()`; `Utilities/CustomDataManager.cs` — cache layer

---

## Sensor Details

### Raycast (RaycastCameraControl)

- Camera raycast up to 35 km
- Hit creates an `EnemyContact` with `SourceIndex = -1`
- Also stored as `Jet.pinnedRaycastTarget` (never decays, survives enemy list cleanup)
- Auto-selects via `SelectPinned()` on successful hit

**Source:** `Modules/RaycastCameraControl.cs` — `ExecuteRaycast()`

### Radar (RadarControlModule)

- Uses AI Flight + AI Combat block pairs
- Index 0 = scan radar, Index 1 = track radar, Index 2+ = RWR
- Auto-detects pairs named `"AI Flight"` / `"AI Combat"` through `"AI Flight 99"` / `"AI Combat 99"`
- Each pair feeds `UpdateOrAddEnemy()` with its source index

**Source:** `Modules/RadarControlModule.cs` — `Tick()` method

### AirtoAir Seeker

- Wraps the primary AI block pair for active radar lock
- Provides lock/search sound cues via SoundManager
- Auto-selects closest enemy if no selection exists

**Source:** `Modules/AirtoAir.cs` — `Tick()` method

---

## GunControlModule: Independent Targeting

The gun turrets do **not** use the pilot's selected target. They independently find the closest enemy within a forward cone:

```mermaid
flowchart TD
    GUN["GunControlModule.Tick()"] --> CONE["Scan enemyList for closest\nenemy within 15 deg cone\nof cockpit.WorldMatrix.Forward"]
    CONE --> FOUND{"Target found\nin cone?"}
    FOUND -- "Yes" --> BALLI["BallisticsCalculator\ncompute intercept point"]
    FOUND -- "No" --> CENTER["Center turrets forward"]
    BALLI --> AIM["DriveTowardDirection()\nyaw rotor + pitch hinge"]
```

> The cone check uses the ship's forward vector (not the gun's) to prevent feedback loops.

**Source:** `Modules/GunControlModule.cs` — `TrackTarget()`, `DriveTowardDirection()`
