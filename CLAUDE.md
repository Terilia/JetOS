# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

JetOS is a Space Engineers programmable block script providing a fighter jet operating system with HUD, weapon systems, radar, targeting pods, and flight control. Built using MDK2 (Malware's Development Kit 2).

## Build System

**Target**: .NET Framework 4.8, C# 6.0 | **Platform**: x64 only

```bash
# Build and deploy to SE (from repository root)
dotnet build Mdk.PbScript2.sln --configuration Release

# Debug build
dotnet build Mdk.PbScript2.sln --configuration Debug

# Clean
dotnet clean Mdk.PbScript2.sln
```

A successful Release build automatically deploys to `%APPDATA%/SpaceEngineers/IngameScripts/local/Mdk.PbScript2`. There are no tests — verification is done in-game.

MDK minification is configured in `Mdk.PbScript2/Mdk.PbScript2.mdk.ini` (currently `full`). Levels: `none` < `trim` < `stripcomments` < `lite` < `full`.

## Architecture

### Partial Class Pattern (Critical)

All `.cs` files use `partial class Program` inside the `IngameScript` namespace. MDK2 merges these into a single compilation unit for Space Engineers. Every new class must be nested inside `partial class Program`:

```csharp
namespace IngameScript
{
    partial class Program
    {
        public class MyNewClass { ... }
    }
}
```

### File Layout

```
Mdk.PbScript2/
├── Program.cs              # Entry point: constructor, Main(), FindEmptyOrOldestSlot()
├── SystemManager.cs        # Static orchestrator: module init, tick loop, input routing,
│                           #   background ticking, menu rendering
├── Jet.cs                  # Hardware abstraction: block refs, TargetSlot[], EnemyContact list,
│                           #   flight data APIs, gun ammo tracking
├── Modules/
│   ├── ProgramModule.cs    # Abstract base class for all modules
│   ├── HUDModule.cs        # Flight instruments, artificial horizon, lead pip, stabilizer PID
│   ├── AirToGround.cs      # Ground attack missile bay management
│   ├── AirtoAir.cs         # Air-to-air missiles, radar lock, passive scanning
│   ├── RaycastCameraControl.cs  # Targeting pod (camera raycast + rotor/hinge tracking)
│   ├── RadarControlModule.cs    # Centralized radar + RWR (Radar Warning Receiver)
│   ├── ConfigurationModule.cs   # Runtime config with category/parameter/value hierarchy
│   └── GunControlModule.cs     # Gun turret auto-tracking (rotor+hinge assemblies)
├── HUD/                    # HUD rendering split into focused renderers
│   ├── HorizonRenderer.cs  # Artificial horizon, pitch ladder, flight path marker
│   ├── InstrumentRenderer.cs # Speed/altitude tapes, compass, G-force, throttle
│   ├── RadarRenderer.cs    # Radar display overlay on HUD
│   ├── TargetingRenderer.cs # Lead pip, target boxes, lock indicators
│   └── WeaponScreenRenderer.cs # Weapon status LCD (surface 2)
├── UI/
│   ├── UIController.cs     # LCD rendering (main screen, extra screen, custom frames)
│   ├── UIElements.cs       # UI primitives (UIElement, UIContainer, UILabel)
│   └── GridVisualization.cs # Ship grid outline display on extra LCD
├── Utilities/
│   ├── BallisticsCalculator.cs  # Iterative intercept point solver (Newton's method with gravity)
│   ├── CircularBuffer.cs       # Fixed-size circular buffer
│   ├── CommonTypes.cs           # Shared types: RWRWarning, Player, Obstacle, Vector2I
│   ├── CustomDataManager.cs     # Dictionary-cached CustomData read/write
│   ├── NavigationHelper.cs      # Vector math and navigation utilities
│   ├── RadarTrackingModule.cs   # Generic AI block-based target tracking class
│   ├── SoundManager.cs          # Priority-based dual-channel sound with 3-tick state machine
│   └── SpriteHelpers.cs        # Sprite creation helpers for LCD rendering
├── Extensions/
│   └── RandomExtensions.cs # Extension methods
└── Diagnostics/            # Standalone PB scripts for in-game debugging (excluded from build)
    └── TurretDiagnostic.cs # Turret orientation/sign verification
```

### Core Systems

**SystemManager** (static class) orchestrates everything:
- Initializes all modules in `Initialize()` — order matters (RadarControlModule first, referenced by others)
- Runs the tick loop in `Main()` — background-ticks HUD, raycast, radar, air-to-air, and gun control even when not the active module
- Delegates CustomData to `CustomDataManager` and sound to `SoundManager`

**Jet** (data + hardware abstraction):
- Holds all block references gathered in constructor via `GridTerminalSystem`
- Contains `TargetSlot[5]` array for multi-target tracking with timestamps based on `GameTicks`
- Maintains `List<EnemyContact> enemyList` with decay (3-minute timeout) and proximity-based deduplication

**Background-ticking modules** (always tick regardless of which module is active):
- `HUDModule` + `RaycastCameraControl` — always tick (lines 204-208)
- `RadarControlModule` — always ticks if not current module (line 210)
- `AirtoAir` — always ticks if not current module (line 215)
- `GunControlModule` — always ticks if not current module (line 220)

**SoundManager** (static class, dual-channel):
- Two independent channels: `warningChannel` (altitude/RWR) and `weaponChannel` (AIM9 tones)
- Priority system: each tick, highest-priority `RequestWarning()`/`RequestWeapon()` call wins
- 3-tick state machine per sound change: stop → select → play (SE limitation: 1 sound API action per tick)

**CustomDataManager** (static class):
- Dictionary cache over the PB's CustomData string (key:value lines)
- Auto-reparses only when raw string changes or dirty flag set
- All reads/writes go through `SystemManager.GetCustomDataValue()`/`SetCustomDataValue()`

### Module System

All modules inherit from `ProgramModule` (`Modules/ProgramModule.cs`):
- **Required**: `GetOptions()`, `ExecuteOption(int)`
- **Optional overrides**: `Tick()`, `HandleSpecialFunction(int)`, `HandleNavigation(bool)`, `HandleBack()`, `GetHotkeys()`
- The `name` field is displayed in the main menu

**Adding a new module**:
1. Create file in `Modules/` with class inheriting `ProgramModule` inside `partial class Program`
2. Implement abstract methods
3. Instantiate in `SystemManager.Initialize()` and `modules.Add()`
4. If it needs background ticking, add a block after the air-to-air check in `SystemManager.Main()`
5. `mainMenuOptions` auto-populates from module names — no separate array update needed

### GunControlModule — Turret Aiming

The gun turrets use rotor+hinge assemblies with mirror-mounted left/right configurations. Key design decisions:

- **Yaw sign**: Uses `SignedAngleBetween(flatGun, flatTarget, rotorUp)` then applies `-KP * yawDeg` because SE positive RPM = counterclockwise from above (opposite of cross-product sign)
- **Pitch sign**: `ElevationSign = Sign(Dot(Cross(rotorUp, gunFwd), hinge.Up))` — automatically handles left vs right hinge mounting orientation
- **Cone check**: Uses `cockpit.WorldMatrix.Forward` (ship-fixed), NOT the gun's own forward (which would create a tracking feedback loop)
- **`DetermineMotorSigns()`** recalculates every 60 ticks to handle rotor movement changing geometry
- Barrel direction is always `Gun.WorldMatrix.Forward` regardless of physical hinge mounting angle

### Target Data Flow

1. **Acquisition**: `RaycastCameraControl` (raycast) or `AirtoAir` (AI block radar lock) detects target
2. **Slot Storage**: `Jet.targetSlots[0-4]` stores position, velocity, name, timestamp
3. **Active Selection**: `Jet.activeSlotIndex` tracks which slot is "selected" — `FlipGPS()` cycles through fresh slots
4. **CustomData Sync**: `UpdateActiveTargetGPS()` writes active slot to `Cached:GPS:` and `CachedSpeed:` in CustomData
5. **Consumption**: HUD reads for lead pip; weapon modules read for missile programming
6. **Bay Transfer**: Before firing, GPS copied to bay-specific CustomData entries for missile scripts to read

### Input System

Toolbar arguments (numpad 1-9):
- **1/2**: Navigate up/down (modules can override via `HandleNavigation`)
- **3**: Select/execute
- **4**: Back (modules can override via `HandleBack`)
- **5-8**: Module-specific hotkeys via `HandleSpecialFunction`
- **6/7**: Global AoA trim offset (decrement/increment `Jet.offset`)
- **8**: Cycle target slots (`FlipGPS()`)
- **9**: Return to main menu

### Performance Constraints

Space Engineers enforces ~50,000 instructions per tick. Key optimizations in place:
- **CustomData cache**: Dictionary-based, only re-parses when raw string changes or dirty flag set
- **Block list cache**: Refreshes every 60 ticks (1 second), not every frame
- **Sprite cache**: Grid outline only recalculated when block count changes
- **Conditional API calls**: Thrust override only set when value actually differs (>0.001 tolerance)
- **Enemy decay check**: Only runs every 60 ticks, not every tick
- **Reusable buffers**: `GetClosestNEnemies` uses pre-allocated sort/result lists to reduce GC

### Block Naming Conventions

**Required**:
- `"Jet Pilot Seat"` — IMyCockpit (script won't initialize without it)
- `"JetOS"` — IMyTextSurfaceProvider (surfaces 0, 1, 2 for main UI, extra info, weapons screen)
- `"Fighter HUD"` — IMyTextSurface for flight instruments

**Optional**:
- `"AI Flight"` / `"AI Combat"` — Primary radar AI pair
- `"AI Flight N"` / `"AI Combat N"` (2-99) — Additional radar/RWR pairs (auto-detected)
- `"Bay X"` (1, 2, ...) — Missile merge blocks (sorted numerically)
- `"Gun Rotor Left"`, `"Gun Hinge Left"`, `"Gun Rotor Right"`, `"Gun Hinge Right"` — Gun turret assemblies
- `"Camera Targeting Turret"`, `"LCD Targeting Pod"`, `"Targeting Rotor"`, `"Targeting Hinge"`, `"Remote Control"` — Targeting pod
- `"Sound Block Warning"` — Altitude/speed warning audio
- `"Canopy Side Plate Sound Block"` — Weapon lock/search tones
- `"invertedstab"` / `"normalstab"` — Right/left stabilizer groups
- Thrusters with `"Industrial"` in name are excluded; gas tanks with `"Jet"` in name are hydrogen tanks

### Diagnostics

The `Diagnostics/` folder contains standalone PB scripts for in-game debugging. These are **excluded from the MDK build** via `<Compile Remove="Diagnostics\**" />` in the csproj. They are meant to be pasted into a separate programmable block in-game — they must have a top-level `Main()` method and NOT use the `partial class Program` pattern.

## Debugging

```csharp
ParentProgram.Echo($"Debug: {value}");
ParentProgram.Echo($"Instructions: {ParentProgram.Runtime.CurrentInstructionCount}");
```

Exception handling in `Program.Main()`:
- `NullReferenceException` → logs + auto-reinitializes (can hide bugs — consider commenting out during development)
- All other exceptions → logs only (no auto-recovery)

## Known Issues

1. **Sound state machine**: Requires 3 ticks minimum per sound change (SE limitation: 1 API action per tick)
2. **CustomData writes**: Must call `MarkCustomDataDirty()` after direct `Me.CustomData` writes that bypass `SetCustomDataValue()`
3. **Block cache staleness**: 60-tick refresh interval means newly placed/destroyed blocks take up to 1 second to appear
4. **Target slot aging**: Uses `Jet.GameTicks` (incremented by SystemManager), not real time — pausing the script freezes aging
5. **SE motor RPM convention**: Positive RPM = counterclockwise from above — opposite of right-hand rule. Cross-product-based angle signs must be negated for rotor control.
6. **SE `MatrixD.Left` setter**: Stores `-Left` as `Right` internally — affects `TransformNormal` results and Whiplash reference code sign conventions
