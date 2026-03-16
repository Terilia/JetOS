# Weapons & Radar Systems

## Radar Architecture

The radar system uses Space Engineers AI Flight + AI Combat block pairs. RadarControlModule manages multiple pairs with different roles.

```mermaid
flowchart TD
    subgraph Pairs ["AI Block Pairs (auto-detected)"]
        P0["AI Flight + AI Combat\n(Index 0 = Scan Radar)"]
        P1["AI Flight 2 + AI Combat 2\n(Index 1 = Track Radar)"]
        P2["AI Flight 3 + AI Combat 3\n(Index 2 = RWR 1)"]
        P3["AI Flight N + AI Combat N\n(Index N = RWR N-2)"]
    end

    subgraph RCM ["RadarControlModule.Tick()"]
        SCAN["Scan (Index 0)\nDetect new contacts"]
        TRACK["Track (Index 1)\nFine tracking of selected enemy"]
        RWR["RWR (Index 2+)\nPassive threat detection"]
    end

    P0 --> SCAN
    P1 --> TRACK
    P2 --> RWR
    P3 --> RWR

    SCAN --> |"UpdateOrAddEnemy(source=0)"| EL["Jet.enemyList"]
    TRACK --> |"UpdateOrAddEnemy(source=1)"| EL
    TRACK --> LOCK{"Tracked entity matches\nselected enemy?"}
    LOCK -- "Yes" --> LOCKED["IsTrackLocked = true"]

    RWR --> THREAT["ProcessRWR()\nThreat assessment"]
    THREAT --> WARNINGS["activeThreats list\n+ sound warnings"]

    style SCAN fill:#2d5a2d
    style TRACK fill:#2d4a5a
    style RWR fill:#5a2d2d
```

**Source:** `Modules/RadarControlModule.cs` — constructor (pair detection), `Tick()` (scan/track/RWR loop)

---

## Radar State Machine — Sequential Activation

Pool radars (non-RWR) use a sequential activation chain:

```mermaid
stateDiagram-v2
    [*] --> IDLE
    IDLE --> SEARCHING : ActivateNextSearcher()
    SEARCHING --> LOCKED : New target found\n(not already locked)
    LOCKED --> IDLE : Lost target\n(120 ticks timeout)
    SEARCHING --> SEARCHING : Target already locked\nby another radar\n(stays searching)
```

- Only 1 pool radar is SEARCHING at any time
- When SEARCHING finds a new target, it transitions to LOCKED and the next IDLE radar becomes SEARCHING
- SEARCHING always feeds `enemyList` (even for already-locked targets)
- LOCKED radars continuously feed position data; demote to IDLE after `LOST_TARGET_TIMEOUT = 120` ticks
- Priority rotation (`Closest → Largest → Smallest`) ensures diversity in multi-target detection

**Source:** `Modules/RadarControlModule.cs` — `ProcessSearchingRadar()`, `ProcessLockedRadar()`, `StartSearching()`

---

## RWR Threat Assessment

Each RWR radar independently tracks an enemy and evaluates whether it's a threat:

```mermaid
flowchart TD
    RWR["RWR Radar N"] --> TRACKING{"radar.IsTracking\n&& HasReceivedPosition?"}
    TRACKING -- "No" --> CLEAR["Clear state"]
    TRACKING -- "Yes" --> CHANGED{"Enemy name changed?"}
    CHANGED -- "Yes" --> RESET["Reset history\nTicksSinceEnemyChange = 0"]
    CHANGED -- "No" --> INC["TicksSinceEnemyChange++"]
    INC --> HIST["Record position every 10 ticks\n(circular buffer of 10 positions)"]
    HIST --> STABLE{"TicksSinceEnemyChange >= 30?"}
    STABLE -- "No" --> WAIT["Wait for stable track"]
    STABLE -- "Yes" --> ASSESS["IsThreatening()"]

    ASSESS --> CV{"Closing velocity > 0?"}
    CV -- "No" --> SAFE["Not a threat"]
    CV -- "Yes" --> TCA{"Time to closest\napproach < 300s?"}
    TCA -- "No" --> SAFE
    TCA -- "Yes" --> MISS{"Miss distance\n< 500m?"}
    MISS -- "No" --> SAFE
    MISS -- "Yes" --> ASPECT{"Aspect angle\n< 90 deg?"}
    ASPECT -- "No" --> SAFE
    ASPECT -- "Yes" --> THREAT["THREAT DETECTED"]

    THREAT --> SOUND["SoundManager.RequestWarning()\nRWR priority tone"]

    style THREAT fill:#8b0000,color:#fff
```

**Threat criteria summary:** Enemy must be closing, within 300s of closest approach, miss distance < 500m, and oriented within 90 deg of heading toward player.

**Source:** `Modules/RadarControlModule.cs` — `ProcessRWR()`, `IsThreatening()`

---

## RadarTrackingModule (AI Block Wrapper)

Each AI block pair is wrapped in a `RadarTrackingModule` that extracts position/velocity from the AI's internal waypoint system:

```mermaid
flowchart LR
    subgraph AI ["SE AI Blocks"]
        COMBAT["IMyOffensiveCombatBlock\n(target finder)"]
        FLIGHT["IMyFlightMovementBlock\n(waypoint receiver)"]
    end

    COMBAT --> |"FoundEnemyId"| COMBAT
    COMBAT --> |"Waypoint target"| FLIGHT
    FLIGHT --> |"CurrentWaypoint.Matrix.Translation"| RTM["RadarTrackingModule"]

    RTM --> POS["TargetPosition\n= extrapolate from last 2 points"]
    RTM --> VEL["TargetVelocity\n= (p0 - p1) / dt"]
    RTM --> STAT["IsTracking\n= FoundEnemyId != null"]
    RTM --> NAME["TrackedObjectName\n= CombatBlock.DetailedInfo line 0"]
```

**Source:** `Utilities/RadarTrackingModule.cs` — `UpdateTracking()`, `TargetPosition`, `TargetVelocity`

---

## Air-to-Air Missiles

### AirtoAir Module Flow

AirtoAir is a radar **consumer**, not a sensor. It reads from `Jet.enemyList` (populated by `RadarControlModule`) and uses `RadarControlModule.IsTrackLocked` for lock detection.

```mermaid
flowchart TD
    subgraph TickLoop ["AirtoAir.Tick() — runs every frame"]
        AUTO["Auto-select closest enemy\nif no selection exists\n(from Jet.enemyList)"]
        GPS["UpdateActiveTargetGPS()\nwrite selected enemy to CustomData"]
        SEEKER{"Seeker enabled?"}
        SEEKER -- "No" --> DONE["Skip weapon tones"]
        SEEKER -- "Yes" --> CHECK["Check radarControl.IsTrackLocked"]
        CHECK --> LOCKED{"Lock on\nselected enemy?"}
        LOCKED -- "Yes" --> LOCK_TONE["SoundManager.RequestWeapon\n('AIM9Lock', PRIORITY_LOCK)"]
        LOCKED -- "No" --> SEARCH_TONE["SoundManager.RequestWeapon\n('AIM9Search', PRIORITY_SEARCH)"]
    end

    AUTO --> GPS --> SEEKER

    style LOCK_TONE fill:#2d5a2d
    style SEARCH_TONE fill:#5a4a2d
```

### Seeker Toggle

Toggling the seeker enables/disables weapon lock/search tones. The seeker does **not** control radar blocks directly — `RadarControlModule` manages all AI block activation independently.

| Seeker State | Behavior |
|-------------|----------|
| ON | Plays AIM9Lock tone when `RadarControlModule.IsTrackLocked`, otherwise AIM9Search tone |
| OFF | No weapon tones, radar continues operating in background |

**Source:** `Modules/AirtoAir.cs` — `Tick()`, `ToggleAirtoAirMode()`

---

## Missile Fire Sequence

Both AirtoAir and AirToGround share a similar fire pattern:

```mermaid
sequenceDiagram
    participant Pilot as Pilot Input
    participant Module as AirtoAir / AirToGround
    participant CD as CustomData Cache
    participant Bay as Merge Block (Bay N)
    participant Missile as Detached Missile Script

    Pilot->>Module: Fire Selected Bays
    Module->>CD: Read "Cached" GPS
    Note over Module: Parse GPS coordinates

    loop For each selected bay
        Module->>CD: Write "Cache{N}" = GPS string
        Module->>Bay: bay.ApplyAction("Fire")
        Note over Bay: Merge block releases missile
    end

    Module->>CD: TransferCacheToSlots()
    Note over CD: "Cache{N}" → "{N}"
    Note over CD: Clear "Cache{N}"

    Missile->>CD: Read slot "{N}" for target GPS
    Note over Missile: Navigate to target
```

**Source:** `Modules/AirtoAir.cs` — `FireMissileFromBayWithGps()`, `TransferCacheToSlots()`; `Modules/AirToGround.cs` — same pattern

---

## Bombardment Pattern (Air-to-Ground)

When bombardment mode fires, targets are spread across 4 cardinal directions:

```mermaid
flowchart TD
    CENTER["Central target position\n(from selected enemy or Cached GPS)"] --> CALC["CalculateTargetPositions()"]
    CALC --> COUNT["Count selected bays"]
    COUNT --> SPREAD["Distribute across 4 directions\nE, W, N, S\n4m spacing per target"]
    SPREAD --> FIRE["Fire each bay with its\noffset target position"]
```

**Example:** 5 selected bays → 2 East (4m, 8m), 2 West (4m, 8m), 1 North (4m)

**Topdown mode:** Toggle via menu, persisted in CustomData as `Topdown:true/false`. Tells missile scripts to approach from above.

**Source:** `Modules/AirToGround.cs` — `ExecuteBombardment()`, `CalculateTargetPositions()`

---

## Gun Turret Auto-Tracking

GunControlModule drives rotor+hinge assemblies to aim gatling guns at the closest enemy.

### Turret Assembly

```
     Rotor (yaw)
       │
       └── Hinge (pitch)
             │
             └── Gatling Gun (barrel)
```

Left and right turrets use mirrored mounting. The `ElevationSign` auto-detects orientation.

### Aiming Pipeline

```mermaid
flowchart TD
    subgraph Target ["Find Target"]
        ENEMIES["Jet.enemyList"] --> CONE["Filter: within 15 deg cone\nof cockpit.WorldMatrix.Forward"]
        CONE --> CLOSEST["Pick closest in range"]
    end

    subgraph Ballistics ["Compute Aim Point"]
        CLOSEST --> CALC["BallisticsCalculator\nmuzzleVelocity = 1100 m/s\n10 iterations"]
        CALC --> AIMPT["aimPoint (world space)"]
    end

    subgraph Motors ["Drive Motors"]
        AIMPT --> YAW["Yaw: SignedAngleBetween(\nflatGun, flatTarget, rotorUp)\nrotor.RPM = -KP * yawDeg"]
        AIMPT --> PITCH["Pitch: GetElevationAngle()\nhinge.RPM = KP * pitchDeg * elevationSign"]
        YAW --> DAMP{"error < 0.5 deg?"}
        PITCH --> DAMP
        DAMP -- "Yes" --> STOP["Stop motors (prevent jitter)"]
        DAMP -- "No" --> DRIVE["Apply proportional control"]
    end

    Target --> Ballistics --> Motors
```

### Motor Sign Conventions

| Motor | Convention | Code |
|-------|-----------|------|
| Yaw (rotor) | SE positive RPM = counterclockwise from above. Cross-product sign must be **negated**: `RPM = -KP * yawDeg` | `SignedAngleBetween()` + negate |
| Pitch (hinge) | `ElevationSign = Sign(Dot(Cross(rotorUp, gunFwd), hinge.Up))`. Handles left vs right mounting automatically | `DetermineMotorSigns()` every 60 ticks |

### Configuration (via ConfigurationModule)

| Parameter | Default | Range | Key |
|-----------|---------|-------|-----|
| KP Gain | 5.0 | 0.5-20 | `gun_kp` |
| Max RPM | 30 | 5-60 | `gun_max_rpm` |
| Lock Threshold | 2.0 deg | 0.5-10 | `gun_lock_threshold` |
| Max Range | 6000m | 1000-15000 | `gun_max_range` |
| Muzzle Velocity | 1100 m/s | 200-2000 | `gun_muzzle_velocity` |

**Source:** `Modules/GunControlModule.cs` — `Tick()`, `TrackTarget()`, `DriveTowardDirection()`, `DetermineMotorSigns()`
