# JetOS

A fighter jet operating system for Space Engineers programmable blocks. Provides a full HUD, weapon systems, radar, targeting pod, gun turret auto-tracking, and flight control. Built with [MDK2](https://github.com/malware-dev/MDK2).

## Features

- **HUD** — F/A-18 style heads-up display with artificial horizon, pitch ladder, flight path marker, speed/altitude tapes, compass, G-force, AoA, and throttle indicator
- **Radar & RWR** — Multi-pair AI block radar with centralized tracking, threat analysis, and Radar Warning Receiver
- **Targeting Pod** — Camera raycast acquisition (35 km range) with rotor/hinge servo tracking and LCD feed
- **Air-to-Air** — Radar lock, passive scanning, AIM-9 lock/search tones, missile bay management
- **Air-to-Ground** — Missile bay selection, bombardment patterns, top-down attack mode
- **Gun Turrets** — Rotor+hinge auto-tracking with ballistic lead, mirror-mounted left/right configs
- **Lead Pip** — Iterative intercept solver (Newton's method with gravity) for gun aiming
- **Flight Control** — PID-based AoA stabilization with automatic engagement
- **Configuration** — In-game settings module with category/parameter/value hierarchy
- **Multi-Target Tracking** — 5-slot target array with enemy contact list, decay, and deduplication

## Requirements

- [MDK2](https://github.com/malware-dev/MDK2) (Malware's Development Kit 2)
- .NET Framework 4.8 / C# 6.0
- Space Engineers (PC)

## Build

```bash
# Release build — auto-deploys to %APPDATA%/SpaceEngineers/IngameScripts/local/Mdk.PbScript2
dotnet build Mdk.PbScript2.sln --configuration Release

# Debug build
dotnet build Mdk.PbScript2.sln --configuration Debug
```

There are no automated tests — verification is done in-game. Minification level is configured in `Mdk.PbScript2/Mdk.PbScript2.mdk.ini`.

## Project Structure

```
Mdk.PbScript2/
├── Program.cs                  Entry point (constructor, Main, slot helpers)
├── SystemManager.cs            Static orchestrator (module init, tick loop, input routing)
├── Jet.cs                      Hardware abstraction (block refs, target slots, enemy list)
├── Modules/
│   ├── ProgramModule.cs        Abstract base class for all modules
│   ├── HUDModule.cs            Flight instruments, stabilizer PID, lead pip
│   ├── RadarControlModule.cs   Centralized radar + RWR
│   ├── AirtoAir.cs             Air-to-air missiles and radar lock
│   ├── AirToGround.cs          Ground attack missile bay management
│   ├── RaycastCameraControl.cs Targeting pod (camera raycast + servo tracking)
│   ├── GunControlModule.cs     Gun turret auto-tracking (rotor+hinge)
│   └── ConfigurationModule.cs  Runtime config with settings menu
├── HUD/
│   ├── HorizonRenderer.cs      Artificial horizon, pitch ladder, flight path marker
│   ├── InstrumentRenderer.cs   Speed/altitude tapes, compass, G-force, throttle
│   ├── RadarRenderer.cs        Radar display overlay
│   ├── TargetingRenderer.cs    Lead pip, target boxes, lock indicators
│   └── WeaponScreenRenderer.cs Weapon status LCD
├── UI/
│   ├── UIController.cs         LCD rendering (main screen, extra screen, custom frames)
│   ├── UIElements.cs           UI primitives (UIElement, UIContainer, UILabel)
│   └── GridVisualization.cs    Ship grid outline display
├── Utilities/
│   ├── BallisticsCalculator.cs  Iterative intercept solver
│   ├── CircularBuffer.cs        Fixed-size circular buffer
│   ├── CommonTypes.cs           Shared types (RWRWarning, EnemyContact, etc.)
│   ├── CustomDataManager.cs     Dictionary-cached CustomData read/write
│   ├── NavigationHelper.cs      Vector math and heading calculation
│   ├── RadarTrackingModule.cs   AI block-based target tracking
│   ├── SoundManager.cs          Priority-based dual-channel audio
│   └── SpriteHelpers.cs         Sprite creation helpers
├── Extensions/
│   └── RandomExtensions.cs      Extension methods (NextFloat)
└── Diagnostics/
    └── TurretDiagnostic.cs      Standalone turret debug script (excluded from build)
```

All `.cs` files use `partial class Program` inside the `IngameScript` namespace — MDK2 merges them into a single compilation unit for Space Engineers.

## In-Game Setup

### Required Blocks

| Block | Name | Purpose |
|-------|------|---------|
| Cockpit | `Jet Pilot Seat` | Primary pilot seat (script won't init without it) |
| Text Panel / Cockpit | `JetOS` | OS display — surfaces 0, 1, 2 for main UI, extra info, weapons |
| Text Surface | `Fighter HUD` | Heads-up display |

### Optional Blocks

| Block | Name | Purpose |
|-------|------|---------|
| AI Flight + AI Combat | `AI Flight` / `AI Combat` | Primary radar pair |
| AI Flight + AI Combat | `AI Flight N` / `AI Combat N` | Additional radar/RWR pairs (2-99, auto-detected) |
| Merge Block | `Bay 1`, `Bay 2`, ... | Missile bays (sorted numerically) |
| Rotor + Hinge | `Gun Rotor Left/Right`, `Gun Hinge Left/Right` | Gun turret assemblies |
| Camera | `Camera Targeting Turret` | Targeting pod raycast |
| LCD | `LCD Targeting Pod` | Targeting pod display |
| Rotor + Hinge | `Targeting Rotor`, `Targeting Hinge` | Targeting pod servo |
| Remote Control | `Remote Control` | Targeting pod reference |
| Sound Block | `Sound Block Warning` | Altitude/speed warnings |
| Sound Block | `Canopy Side Plate Sound Block` | Weapon lock/search tones |
| Group | `invertedstab` / `normalstab` | Right/left stabilizer groups |

Thrusters with `"Industrial"` in the name are excluded. Gas tanks with `"Jet"` in the name are treated as hydrogen tanks.

### Controls

Toolbar arguments on the Programmable Block (numpad 1-9):

| Key | Function |
|-----|----------|
| 1 / 2 | Navigate up / down |
| 3 | Select / execute |
| 4 | Back |
| 5-8 | Module-specific hotkeys |
| 6 / 7 | Global AoA trim (decrease / increase) |
| 8 | Cycle target slots |
| 9 | Return to main menu |

## Documentation

Detailed system documentation with diagrams lives in [`docs/`](docs/):

| Document | Contents |
|----------|----------|
| [Architecture](docs/architecture.md) | Tick loop, initialization order, input routing, module system |
| [Target Tracking](docs/target-tracking.md) | Sensor → enemyList → selection → GPS sync → missiles |
| [HUD Rendering](docs/hud-rendering.md) | Render pipeline, each renderer's role, lead pip calculation |
| [Weapons & Radar](docs/weapons.md) | Radar pairs, RWR threat assessment, missile fire, gun turrets |
| [Sound System](docs/sound-system.md) | Dual-channel audio, priority system, 3-tick state machine |

## Architecture

**SystemManager** orchestrates all modules — initializes them (order matters: RadarControlModule first), runs the tick loop, and background-ticks HUD, radar, air-to-air, and gun control even when they're not the active module.

**Jet** is the hardware abstraction layer holding all block references, a 5-slot `TargetSlot[]` array for multi-target tracking, and a decaying `EnemyContact` list with proximity-based deduplication.

**Modules** inherit from `ProgramModule` and implement `GetOptions()`, `ExecuteOption()`, and optionally `Tick()`, `HandleSpecialFunction()`, `HandleNavigation()`, `HandleBack()`, and `GetHotkeys()`.

**Target data flow**: sensors (raycast camera, AI block radar) write to target slots and the enemy list. The HUD reads the active slot for lead pip rendering. Weapon modules read it for missile GPS programming. Gun turrets independently track the closest enemy from the enemy list.

## Debugging

```csharp
ParentProgram.Echo($"Debug: {value}");
ParentProgram.Echo($"Instructions: {ParentProgram.Runtime.CurrentInstructionCount}");
```

The script runs at `UpdateFrequency.Update1` (every tick). Space Engineers enforces ~50,000 instructions per tick.

## License

Private repository.
