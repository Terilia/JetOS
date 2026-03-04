# HUD Rendering Pipeline

## Overview

The HUD renders on the `"Fighter HUD"` text surface at 60 Hz. `HUDModule.Tick()` computes flight data, then calls `RenderHUD()` which dispatches to specialized renderers in the `HUD/` folder.

```mermaid
flowchart TD
    TICK["HUDModule.Tick()"] --> VALID{"ValidateHUDState()"}
    VALID -- "fail" --> SKIP["Skip frame"]
    VALID -- "ok" --> FLIGHT["UpdateFlightData()\npitch, roll, velocity, AoA,\nG-force, Mach, altitude"]
    FLIGHT --> THROTTLE["UpdateThrottleControl()\nthrust override, H2 tanks, airbrakes"]
    THROTTLE --> SMOOTH["UpdateSmoothedValues()\ncircular buffer averaging"]
    SMOOTH --> STAB["AdjustStabilizers()\nPID trim to normalstab/invertedstab"]
    STAB --> RENDER["RenderHUD()"]

    RENDER --> R1["HorizonRenderer"]
    RENDER --> R2["InstrumentRenderer"]
    RENDER --> R3["RadarRenderer"]
    RENDER --> R4["TargetingRenderer"]
    RENDER --> R5["WeaponScreenRenderer"]

    style R1 fill:#2d5a2d
    style R2 fill:#2d4a5a
    style R3 fill:#5a2d2d
    style R4 fill:#5a4a2d
    style R5 fill:#4a2d5a
```

**Source:** `Modules/HUDModule.cs` — `Tick()`, `RenderHUD()`

---

## Render Order

`RenderHUD()` draws elements in this order (back to front):

```mermaid
flowchart TD
    FRAME["frame = hud.DrawFrame()"] --> H1["1. Artificial Horizon\n(pitch ladder + roll)"]
    H1 --> H2["2. Bank Angle Markers"]
    H2 --> H3["3. Flight Path Marker\n(velocity vector circle)"]
    H3 --> H4["4. Trim Offset Info Box"]
    H4 --> H5["5. Speed Tape (F-18 style)"]
    H5 --> H6["6. Compass (optional)"]
    H6 --> H7["7. Altitude Tape (F-18 style)"]
    H7 --> H8["8. G-Force Indicator (optional)"]
    H8 --> H9["9. AoA Indexer + Stall Warning"]
    H9 --> H10["10. Radar Minimap"]
    H10 --> H11["11. Lead Pip + TTI"]
    H11 --> H12["12. Target Brackets + Vc + AA"]
    H12 --> H13["13. Gun Funnel Lines"]
    H13 --> H14["14. Breakaway Warning"]
    H14 --> DISPOSE["frame.Dispose()"]
```

---

## Renderer Breakdown

### HorizonRenderer (`HUD/HorizonRenderer.cs`)

Draws the artificial horizon that rotates with the aircraft.

| Element | Description |
|---------|-------------|
| Pitch Ladder | Lines every 5 deg from -90 to +90, split left/right with center gap |
| Horizon Line | Thick line at 0 deg pitch, moves vertically with pitch angle |
| Roll Rotation | All elements rotated around screen center by `-roll` angle |
| Flight Path Marker | Circle at actual velocity direction (where you're going, not where you're pointed) |
| Bank Angle Markers | Tick marks at 15/30/45/60 deg arranged radially |

```mermaid
flowchart LR
    subgraph Inputs
        PITCH["pitch (deg)"]
        ROLL["roll (deg)"]
        VEL["velocity vector"]
    end

    subgraph Render
        PL["Pitch Ladder\nmarkerY = centerY - (i - pitch) * pixelsPerDeg"]
        HL["Horizon Line\nhorizonY = centerY + pitch * pixelsPerDeg"]
        ROT["Rotate all sprites by -roll\naround (centerX, centerY)"]
        FPM["Flight Path Marker\nlocalVel = TransformNormal(vel, Transpose(worldMatrix))\nscreenX/Y from atan2(localX/Y, -localZ)"]
    end

    PITCH --> PL
    PITCH --> HL
    ROLL --> ROT
    VEL --> FPM
```

**Source:** `HUD/HorizonRenderer.cs`

---

### InstrumentRenderer (`HUD/InstrumentRenderer.cs`)

Draws numeric readouts and tape gauges around the HUD edges.

| Element | Position | Data Source |
|---------|----------|-------------|
| Speed Tape | Left edge, 200px tall | `cockpit.GetShipSpeed()` converted to kph |
| Altitude Tape | Right edge, 200px tall | `cockpit.TryGetPlanetElevation()` |
| Compass | Top center, 90 deg FOV | Heading from gravity-plane projection |
| G-Force | Bottom-left | `acceleration.Length() / 9.81` |
| AoA Indexer | Left, centered | `atan2(dot(vel, up), dot(vel, fwd))` |
| Throttle Bar | Configurable | `cockpit.MoveIndicator.Z * -1` |

#### AoA Indexer Warning Levels

```mermaid
flowchart LR
    AOA["Current AoA"] --> PCT["stallPercent = |AoA| / 28 deg"]
    PCT --> C1{"< 0.80"}
    C1 -- "Yes" --> NORMAL["NORMAL\n(green circle)"]
    C1 -- "No" --> C2{"< 0.90"}
    C2 -- "Yes" --> CAUTION["CAUTION\n(yellow 'AOA')"]
    C2 -- "No" --> C3{"< 1.00"}
    C3 -- "Yes" --> WARNING["WARNING\n(orange 'HIGH AOA', flashing)"]
    C3 -- "No" --> STALL["STALL\n(red 'STALL', fast flash)"]
```

**Source:** `HUD/InstrumentRenderer.cs`

---

### RadarRenderer (`HUD/RadarRenderer.cs`)

Draws a top-down radar minimap in the bottom-right corner.

```mermaid
flowchart TD
    subgraph Projection ["World to Radar Screen"]
        GRAV["Get gravity → worldUp"]
        FWD["cockpit.Forward → project onto horizontal plane → yawForward"]
        RIGHT["cross(worldUp, yawForward) → yawRight"]
        PROJ["For each enemy:\ndotRight = delta . yawRight\ndotForward = delta . yawForward\nscreenX = center + dotRight * scale\nscreenY = center - dotForward * scale"]
    end

    subgraph Display ["Radar Display"]
        FRAME["100x100px box, bottom-right"]
        RING["Range ring at 50% max range"]
        PLAYER["Triangle at center (player)"]
        CONTACTS["Enemy dots:\n- Diamond if selected\n- Color by closing speed"]
        RANGE["Auto-scale to fit farthest contact\nsmoothed with alpha=0.1"]
    end

    Projection --> Display
```

#### Contact Color Coding

| Condition | Color | Meaning |
|-----------|-------|---------|
| `timeToClosest < 5s` | Red | Imminent |
| `timeToClosest < 15s` | Orange | Threat |
| `closingSpeed > 0` | Yellow | Approaching |
| `closingSpeed <= 0` | Gray | Receding |

**Source:** `HUD/RadarRenderer.cs`

---

### TargetingRenderer (`HUD/TargetingRenderer.cs`)

Draws the lead pip (gun sight), target brackets, gun funnel, and breakaway warnings.

#### Lead Pip Calculation

```mermaid
flowchart TD
    subgraph Inputs ["Inputs"]
        SPOS["Shooter position + velocity"]
        TPOS["Target position + velocity + acceleration"]
        MV["Muzzle velocity (910 m/s)"]
    end

    subgraph Ballistics ["BallisticsCalculator"]
        REL["D = target - shooter\nV_rel = targetVel - shooterVel"]
        QUAD["Quadratic: qA*t^2 + qB*t + qC = 0\nqA = |V_rel|^2 - muzzleSpeed^2\nSolve for initial t guess"]
        NEWTON["Newton's method (10 iterations)\nRefine t accounting for acceleration\nConverge within 0.0001s"]
        VALIDATE["Validate: |requiredSpeed - muzzleSpeed| / muzzle < 2%"]
    end

    subgraph Output ["Screen Projection"]
        AIM["aimPoint (world space)"]
        LOCAL["Transform to cockpit-local:\nlocalDir = TransformNormal(aimDir, Invert(cockpitMatrix))"]
        FOV["Project through FOV:\nscreenX = center + (localX / -localZ) * scaleX\nscreenY = center + (-localY / -localZ) * scaleY"]
        PIP["Draw pip circle + TTI label"]
    end

    Inputs --> Ballistics
    Ballistics --> Output

    style NEWTON fill:#2d5a2d
```

> FOV scale constants (0.3434 horizontal, 0.31 vertical) are empirically determined from cockpit perspective.

**Source:** `HUD/TargetingRenderer.cs` — `DrawLeadingPip()`; `Utilities/BallisticsCalculator.cs` — `CalculateInterceptPoint()`

#### Gun Enable Logic

```mermaid
flowchart TD
    DIST["Distance from reticle center to pip"] --> CHECK{"distance <= pip radius?"}
    CHECK -- "Yes" --> ENABLE["Enable all gatling guns\nisAimingAtPip = true"]
    CHECK -- "No" --> MANUAL{"manualfire mode?"}
    MANUAL -- "Yes" --> KEEP["Keep guns enabled"]
    MANUAL -- "No" --> DISABLE["Disable all gatling guns"]
```

#### Target Brackets

Shows tactical data around the selected target:

| Display | Calculation |
|---------|-------------|
| Range | `distance(target, shooter)` in km or m |
| Closure Rate (Vc) | `dot(relativeVel, toTargetNorm)` — positive = closing |
| Aspect Angle (AA) | `acos(dot(targetFwd, toShooter))` — 0 = nose-on, 180 = tail |

#### Breakaway Warning

Triggers flashing "PULL UP" or "BREAK AWAY":
- **Low altitude:** `altitude < 100m AND vertical_velocity < -5 m/s`
- **Collision:** `range < 500m AND closureRate > 100 m/s`

**Source:** `HUD/TargetingRenderer.cs` — `DrawTargetBrackets()`, `DrawGunFunnel()`, `DrawBreakawayWarning()`

---

### WeaponScreenRenderer (`HUD/WeaponScreenRenderer.cs`)

Renders on LCD surface 2 (weapons status screen). Shows the enemy contact list and missile time-of-flight.

```mermaid
flowchart TD
    subgraph Screen ["Weapons LCD (Surface 2)"]
        TITLE["Title Bar: TARGET LIST"]
        DETAIL["Selected Target Detail\nName, Range, Bearing, Vc, Age, Speed, Source"]
        SEP["Separator Line"]
        LIST["Enemy List (up to 10)\nSorted by distance\nAge bar + range label"]
        TOF["Missile TOF Display\n(up to 5 in-flight missiles)"]
    end

    TITLE --> DETAIL --> SEP --> LIST --> TOF
```

**Contact Age Color:** Green (fresh) → Yellow (aging) → Red (stale, approaching 180s timeout)

**Source tag meanings:**
- `RDR` = radar scan/track
- `RWR1` = RWR channel 1
- `PIN` = pinned raycast target
- `STT` = Single Target Track (radar locked)
- `TWS` = Track While Scan

**Source:** `HUD/WeaponScreenRenderer.cs`

---

## LCD Surface Allocation

| Surface | Block | Content |
|---------|-------|---------|
| 0 | `JetOS` | Main menu / module UI |
| 1 | `JetOS` | Grid visualization + fuel bar |
| 2 | `JetOS` | Weapons status screen |
| — | `Fighter HUD` | Full HUD (all renderers above) |
| — | `LCD Targeting Pod` | Targeting pod camera feed |

**Source:** `SystemManager.cs` — `Initialize()` (surface setup); `UI/UIController.cs` (menu rendering); `UI/GridVisualization.cs` (grid outline)

---

## Smoothing System

Flight data is smoothed using circular buffers with running sums (O(1) per update):

```mermaid
flowchart LR
    NEW["New value"] --> FULL{"Buffer full?"}
    FULL -- "Yes" --> DEQ["Dequeue oldest\nrunningSum -= oldest"]
    FULL -- "No" --> ENQ
    DEQ --> ENQ["Enqueue new\nrunningSum += new"]
    ENQ --> AVG["smoothed = runningSum / count"]
```

| Buffer | Size | Data |
|--------|------|------|
| velocityHistory | 10 | Ship speed (m/s) |
| altitudeHistory | 10 | Surface altitude (m) |
| gForcesHistory | 10 | G-force magnitude |
| aoaHistory | 10 | Angle of attack (deg) |

**Source:** `Modules/HUDModule.cs` — `UpdateSmoothedValues()`; `Utilities/CircularBuffer.cs`
