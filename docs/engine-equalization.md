# Engine Equalization

> **Source**: `Jet.cs` (engine grouping, grid position classification), `Modules/HUDModule.cs` (equalization algorithm in `UpdateThrottleControl()`), `Modules/HUDModule.cs` (`SetGroupOverride()` helper)

## Overview

Engine equalization prevents yaw drift caused by asymmetric thrust. Every tick, the system reads `MaxEffectiveThrust` from each side's engines, caps total output to what the weaker side can produce, and computes per-side override percentages that deliver equal Newtons of force. This runs continuously, adapting instantly to engine damage, atmospheric density changes, and altitude transitions.

> **Try it live:** [interactive/throttle-demo.html](interactive/throttle-demo.html) — drag the throttle slider, click damage scenarios, watch the override percentages and yaw torque indicator rebalance in real time.

**[Open the equalization diagram](engine-equalization.svg)** to see the balancing mechanism visualized.

---

## The Problem

Without equalization, any thrust asymmetry causes uncommanded yaw:

```mermaid
flowchart LR
    subgraph before ["WITHOUT EQUALIZATION"]
        direction TB
        L1["Left engines\n200 kN max\nOverride: 80%\n= 160 kN"]
        R1["Right engines\n300 kN max\nOverride: 80%\n= 240 kN"]
        YAW["80 kN imbalance\nYAW LEFT"]
    end

    L1 --> YAW
    R1 --> YAW

    subgraph after ["WITH EQUALIZATION"]
        direction TB
        L2["Left engines\n200 kN max\nOverride: 80%\n= 160 kN"]
        R2["Right engines\n300 kN max\nOverride: 53.3%\n= 160 kN"]
        BAL["0 kN imbalance\nSTRAIGHT FLIGHT"]
    end

    L2 --> BAL
    R2 --> BAL

    style YAW fill:#2a1a1a,stroke:#c05050,color:#e08080
    style BAL fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style L1 fill:#1a2a1a,stroke:#3a6a3a,color:#7aaa7a
    style R1 fill:#3a2a0a,stroke:#c0a030,color:#e0c060
    style L2 fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style R2 fill:#1a3a1a,stroke:#40a040,color:#90cc90
```

Common causes of asymmetry:
- **Battle damage**: One or more engines destroyed on one side
- **Atmospheric density**: Altitude-dependent `MaxEffectiveThrust` changes (atmospheric thrusters)
- **Unequal engine count**: Asymmetric ship design (left side has 3 engines, right side has 2)

---

## Algorithm Pipeline

The equalization runs every tick inside `UpdateThrottleControl()` (`HUDModule.cs:542-585`):

```mermaid
flowchart TD
    subgraph read ["1 — READ MAX THRUST PER SIDE"]
        SCAN_L["Sum leftEngines[i].MaxEffectiveThrust\n(skip null / non-functional)"]
        SCAN_R["Sum rightEngines[i].MaxEffectiveThrust\n(skip null / non-functional)"]
        LM["leftMax (Newtons)"]
        RM["rightMax (Newtons)"]
    end

    subgraph compute ["2 — COMPUTE WEAKER SIDE"]
        WEAK["weakerMax = Math.Min(leftMax, rightMax)"]
        TARGET["targetThrust = weakerMax × scaledThrottle"]
    end

    subgraph override ["3 — COMPUTE PER-SIDE OVERRIDES"]
        OVR_L["leftOverride = targetThrust ÷ leftMax"]
        OVR_R["rightOverride = targetThrust ÷ rightMax"]
        GUARD["Guard: if side max = 0, override = 0"]
    end

    subgraph apply ["4 — APPLY TO ENGINES"]
        SET_L["SetGroupOverride(leftEngines, leftOverride)"]
        SET_R["SetGroupOverride(rightEngines, rightOverride)"]
        SET_C["SetGroupOverride(centerEngines, scaledThrottle)"]
        SET_AB["SetGroupOverride(leftAB/rightAB/centerAB, 1.0)\n(only when AB active)"]
    end

    SCAN_L --> LM
    SCAN_R --> RM
    LM --> WEAK
    RM --> WEAK
    WEAK --> TARGET
    TARGET --> OVR_L
    TARGET --> OVR_R
    OVR_L --> GUARD
    OVR_R --> GUARD
    GUARD --> SET_L
    GUARD --> SET_R
    SET_C ~~~ SET_L
    SET_AB ~~~ SET_R

    style WEAK fill:#2a1a1a,stroke:#c05050,color:#e08080
    style TARGET fill:#3a2a0a,stroke:#c0a030,color:#e0c060
    style SET_L fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style SET_R fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style SET_C fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style SET_AB fill:#3a2a0a,stroke:#c0a030,color:#e0c060
```

> The `scaledThrottle` value maps the pilot's 0-80% throttle range to 0.0-1.0. When throttle is above the MIL threshold (80%), `scaledThrottle` is clamped to `1.0` (atmospheric engines at full power; additional thrust comes from AB hydrogen engines).

---

## Throttle Scaling

The equalization operates on `scaledThrottle`, not raw `throttlecontrol`:

```csharp
float scaledThrottle = throttlecontrol <= THROTTLE_HYDROGEN_THRESHOLD
    ? throttlecontrol / THROTTLE_HYDROGEN_THRESHOLD  // 0.0–0.8 → 0.0–1.0
    : 1.0f;                                          // 0.8–1.0 → 1.0 (AB adds extra)
```

| Pilot Throttle | `throttlecontrol` | `scaledThrottle` | Atmospheric Engines | AB Engines |
|:-:|:-:|:-:|:-:|:-:|
| Idle | 0.00 | 0.00 | Off | Off |
| 25% | 0.20 | 0.25 | 25% balanced | Off |
| 50% | 0.40 | 0.50 | 50% balanced | Off |
| 75% | 0.60 | 0.75 | 75% balanced | Off |
| MIL (100% atmo) | 0.80 | 1.00 | 100% balanced | Off |
| Afterburner | 0.80 – 1.00 | 1.00 | 100% balanced | 100% (all) |

---

## Engine Groups

The six engine groups are populated in the `Jet` constructor based on grid position relative to the cockpit:

```mermaid
flowchart TD
    subgraph grouping ["Engine Classification (Jet Constructor)"]
        THR["All backward thrusters\n(excl. 'Industrial')"]
        THR --> XPOS{Position.X vs\ncockpit.Position.X}

        XPOS -- "X > cockpitX" --> LEFT["LEFT side"]
        XPOS -- "X < cockpitX" --> RIGHT["RIGHT side"]
        XPOS -- "X == cockpitX" --> CENTER["CENTER"]

        LEFT --> LFUEL{Hydrogen?}
        RIGHT --> RFUEL{Hydrogen?}
        CENTER --> CFUEL{Hydrogen?}

        LFUEL -- No --> LE["leftEngines"]
        LFUEL -- Yes --> LA["leftAB"]
        RFUEL -- No --> RE["rightEngines"]
        RFUEL -- Yes --> RA["rightAB"]
        CFUEL -- No --> CE["centerEngines"]
        CFUEL -- Yes --> CA["centerAB"]
    end

    style LE fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style RE fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style CE fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style LA fill:#3a2a0a,stroke:#c0a030,color:#e0c060
    style RA fill:#3a2a0a,stroke:#c0a030,color:#e0c060
    style CA fill:#3a2a0a,stroke:#c0a030,color:#e0c060
```

**Center engines are excluded from balancing.** They sit on the centerline (same grid X as the cockpit) and produce no yaw moment regardless of thrust level. They run at straight `scaledThrottle`.

> SE grid coordinate convention: looking from cockpit forward, **X+ is LEFT**, X- is RIGHT.

---

## Damage Scenarios

### Single Engine Destroyed

```mermaid
stateDiagram-v2
    direction LR

    state "NORMAL FLIGHT\nboth sides healthy" as NORMAL
    state "RIGHT ENGINE HIT\nrightMax drops" as HIT
    state "EQUALIZATION ADAPTS\nnext tick" as ADAPT
    state "BALANCED FLIGHT\nreduced total thrust" as BALANCED

    [*] --> NORMAL
    NORMAL --> HIT : Damage event
    HIT --> ADAPT : Next tick reads\nnew MaxEffectiveThrust
    ADAPT --> BALANCED : weakerMax = new rightMax\nboth sides limited

```

**NORMAL**: leftMax=400kN, rightMax=400kN, weakerMax=400kN, both overrides at 80%. **BALANCED (after damage)**: rightMax drops to 200kN, weakerMax=200kN, leftOverride=40%, rightOverride=80%.

### Progressive Degradation

| Scenario | leftMax | rightMax | weakerMax | leftOverride | rightOverride | Total Thrust |
|:--|:-:|:-:|:-:|:-:|:-:|:-:|
| **Healthy** (2L + 2R, 200 kN each) | 400 kN | 400 kN | 400 kN | 80% | 80% | 640 kN |
| **1R destroyed** | 400 kN | 200 kN | 200 kN | 40% | 80% | 320 kN |
| **1R destroyed + altitude** (atmo thrust halved) | 200 kN | 100 kN | 100 kN | 40% | 80% | 160 kN |
| **All right destroyed** | 400 kN | 0 kN | 0 kN | 0% | 0% | 0 kN |

> When one side is fully destroyed (`rightMax = 0`), the weaker-side calculation produces `weakerMax = 0`, shutting down all lateral engines. The pilot retains only center engine thrust. This is intentional: running one side at full power with zero opposing thrust would cause an uncontrollable spin.

---

## Damage Cascade Flow

```mermaid
flowchart TD
    DMG["Engine destroyed\n(IsFunctional = false)"] --> TICK["Next tick: equalization loop"]
    TICK --> READ["Read MaxEffectiveThrust\nskips non-functional engines"]
    READ --> CALC["weakerMax drops\ntargetThrust drops"]
    CALC --> CHOICE{Weaker side\nstill > 0?}

    CHOICE -- Yes --> REDUCE["Reduce stronger side override\nto match weaker side"]
    CHOICE -- No --> ZERO["Both sides = 0 override\nCenter engines only"]

    REDUCE --> FLY["Controlled flight continues\nat reduced thrust"]
    ZERO --> LIMP["Limp mode: center thrust only\nno yaw correction possible"]

    style DMG fill:#2a1a1a,stroke:#c05050,color:#e08080
    style ZERO fill:#2a1a1a,stroke:#c05050,color:#e08080
    style LIMP fill:#2a1a1a,stroke:#c05050,color:#e08080
    style FLY fill:#1a3a1a,stroke:#40a040,color:#90cc90
    style REDUCE fill:#3a2a0a,stroke:#c0a030,color:#e0c060
    style CALC fill:#3a2a0a,stroke:#c0a030,color:#e0c060
```

---

## SetGroupOverride Helper

The `SetGroupOverride()` method applies a percentage to a list of thrusters with a tolerance check:

```csharp
private static void SetGroupOverride(List<IMyThrust> group, float value)
{
    for (int i = 0; i < group.Count; i++)
    {
        if (group[i] != null && Math.Abs(group[i].ThrustOverridePercentage - value) > 0.001f)
            group[i].ThrustOverridePercentage = value;
    }
}
```

| Guard | Purpose |
|:--|:--|
| `group[i] != null` | Destroyed blocks become null between cache refreshes |
| `Math.Abs(... - value) > 0.001f` | Avoids redundant API calls (saves instruction cycles) |

> `ThrustOverridePercentage` is a 0.0-1.0 float. Setting it costs ~2 instruction cycles per thruster. The tolerance check avoids wasting cycles when the value hasn't meaningfully changed.

---

## Afterburner Engines

AB (hydrogen) engines are **not equalized**. When afterburner is engaged, all AB engines receive a flat `1.0` override:

```csharp
if (hydrogenswitch)
{
    SetGroupOverride(myjet.leftAB, 1f);
    SetGroupOverride(myjet.rightAB, 1f);
    SetGroupOverride(myjet.centerAB, 1f);
}
```

**Rationale**: AB engines are supplemental thrust for short-duration combat maneuvers. Balancing them against damaged atmospheric engines would defeat the purpose of afterburner (maximum thrust). The atmospheric equalization already handles yaw; AB engines add symmetric boost on top.

---

## Why MaxEffectiveThrust

The algorithm uses `MaxEffectiveThrust`, not `MaxThrust`:

| Property | Meaning | Varies With |
|:--|:--|:--|
| `MaxThrust` | Rated maximum thrust (block spec) | Never changes |
| `MaxEffectiveThrust` | Actual achievable thrust right now | Atmosphere density, altitude, damage, power |

Atmospheric thrusters produce less thrust at higher altitudes (thinner atmosphere). `MaxEffectiveThrust` reflects this in real-time, so the equalization automatically adjusts as the jet climbs or descends. A naive approach using `MaxThrust` would produce incorrect override ratios at altitude.

---

## Performance Notes

- **Per-tick execution**: The equalization runs every tick (~60/sec). This is necessary because `MaxEffectiveThrust` changes continuously with altitude.
- **Override tolerance**: `SetGroupOverride` only writes `ThrustOverridePercentage` when the delta exceeds `0.001`, saving ~2 instruction cycles per unchanged thruster per tick.
- **Null safety**: Destroyed engines are skipped via null checks. Block lists refresh every 60 ticks, so a destroyed engine may linger as null for up to 1 second before removal.
- **Zero-division guard**: `leftMax > 0` / `rightMax > 0` checks prevent division by zero when an entire side is destroyed.
- **No GC pressure**: The algorithm uses only stack-allocated floats. No heap allocations per tick.
- **Center engine bypass**: Center engines skip the min/divide math entirely, receiving `scaledThrottle` directly.
