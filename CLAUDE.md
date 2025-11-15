# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

JetOS is a comprehensive Space Engineers programmable block script that provides a complete operating system for fighter jets. It includes advanced flight control, HUD, weapon systems, targeting pods, and multiple operational modules. The script is built using the MDK2 (Malware's Development Kit) framework for Space Engineers.

## Build System

**Framework**: MDK2 (Malware's Development Kit 2) for Space Engineers
**Target**: .NET Framework 4.8, C# 6.0
**Main Project**: `Mdk.PbScript2/Mdk.PbScript2.csproj`

### Build Commands

```bash
# Build the project (from repository root)
dotnet build Mdk.PbScript2.sln --configuration Release

# Debug build
dotnet build Mdk.PbScript2.sln --configuration Debug

# Clean build artifacts
dotnet clean Mdk.PbScript2.sln
```

### MDK Configuration

The build process is configured via `Mdk.PbScript2/Mdk.PbScript2.mdk.ini`:
- **Minification**: Set to `full` for production (renames identifiers, removes whitespace)
- **Output**: Compiled script is packaged for deployment to Space Engineers
- **Ignored files**: `obj/**/*`, `MDK/**/*`, `**/*.debug.cs`

To change minification level, edit the `.mdk.ini` file:
- `none`: No minification
- `trim`: Removes unused types
- `stripcomments`: trim + removes comments
- `lite`: stripcomments + removes whitespace
- `full`: lite + renames identifiers (production setting)

## Architecture

### High-Level Structure

```
Program (MyGridProgram entry point)
├── SystemManager (static orchestrator)
│   ├── UIController (menu/LCD rendering)
│   ├── Module System (plugin architecture)
│   │   ├── HUDModule (flight instruments)
│   │   ├── AirToGround (ground attack missiles)
│   │   ├── AirtoAir (air-to-air missiles)
│   │   ├── RaycastCameraControl (targeting pod)
│   │   ├── LogoDisplay (screensaver)
│   │   └── FroggerGameControl (mini-game)
│   └── Jet (hardware abstraction layer)
├── PIDController (control system utility)
└── UI Framework (UIElement, UIContainer, UILabel)
```

### Core Design Patterns

**Module System**: All subsystems inherit from `ProgramModule` base class with standardized interface:
- `GetOptions()`: Menu items
- `ExecuteOption(int)`: Menu selection handler
- `HandleSpecialFunction(int)`: Hotkeys (5-8)
- `Tick()`: Per-frame update (60Hz)
- `GetHotkeys()`: Help text

**Hardware Abstraction**: The `Jet` class encapsulates all physical block references and provides clean APIs for:
- Flight data (velocity, altitude, position, attitude)
- Thruster control (forward, backward, hydrogen)
- Radar/turret management
- Weapon bay management
- Stabilizer control (left/right groups)

**CustomData as Database**: The programmable block's `CustomData` field stores configuration and target data:
- `Cached:GPS:...` - Active target coordinates
- `CacheGPS0:` through `CacheGPS3:` - Target slots (4 total)
- `CachedSpeed:X:Y:Z` - Target velocity vector
- `Topdown:true/false` - Air-to-ground mode setting
- `AntiAir:true/false` - Air-to-air mode setting
- `DataSlot0:` through `DataSlotN:` - Bay-specific data

### Critical Data Flow: GPS Target Management

Understanding the GPS data flow is essential for working with weapons and targeting:

1. **Acquisition**: `RaycastCameraControl` (camera raycast) or `AirtoAir` (radar lock) detects target
2. **Storage**: Updates `Cached:GPS:Target:...` in CustomData
3. **Caching**: `FlipGPS()` cycles through 4 target slots (`CacheGPS0-3`)
4. **Consumption**:
   - `HUDModule` reads GPS to display lead indicators and ballistic solutions
   - Weapon modules read GPS to program missiles
5. **Transfer**: When firing, `TransferCacheToSlots()` copies GPS data to bay-specific slots
6. **Launch**: Missiles read bay-specific CustomData on release

**Key Optimization**: CustomData is cached in a dictionary (`customDataCache`) with dirty flag tracking to avoid expensive string parsing every tick.

### Input System

User commands via toolbar (numpad arguments 1-9):
- **1**: Navigate Up
- **2**: Navigate Down
- **3**: Select/Execute
- **4**: Back/Deselect
- **5-8**: Module-specific hotkeys (varies by active module)
- **9**: Return to Main Menu

Module activation changes menu context and enables module-specific `Tick()` updates.

### Performance Optimizations

Several critical optimizations are implemented:

1. **CustomData Dictionary Cache**: Parses CustomData once, caches in dictionary, only re-parses when dirty flag set (~90% improvement)
2. **Sprite Caching**: Grid outline visualization cached and only recalculated when block count changes
3. **Circular Buffers**: Used for smoothing flight data (velocity, G-force, AoA) with running sum optimization
4. **Conditional Updates**: Many blocks check if values changed before calling API (e.g., thrust override checks)
5. **Block List Caching**: Grid blocks cached with dirty flag on structure changes

### PID Controllers

PID (Proportional-Integral-Derivative) controllers are used throughout:
- **Camera Tracking**: Targeting pod stabilization (pitch/yaw control via rotor/hinge)
- **Flight Stabilization**: AoA (Angle of Attack) maintenance via stabilizer trim
- **Anti-Windup**: Integral term reset when pilot input detected or limits reached

Standard PID pattern:
```csharp
output = (Kp * error) + (Ki * integralError) + (Kd * deltaError)
output = Clamp(output, minOutput, maxOutput)
```

## Block Naming Conventions

The script expects specific block names on the same grid as the programmable block:

**Required Blocks**:
- `"Jet Pilot Seat"` - Main cockpit (IMyCockpit)
- `"JetOS"` - Control LCD surface (IMyTextSurfaceProvider, surfaces 0 & 1)
- `"Fighter HUD"` - HUD display (IMyTextSurface)
- `"JetNoseRad"` - Primary radar turret (IMyLargeGatlingTurret)

**Optional Blocks**:
- `"Camera Targeting Turret"` - Targeting pod camera
- `"LCD Targeting Pod"` - Targeting pod display
- `"Remote Control"` - For targeting pod position
- `"Targeting Rotor"` - Targeting pod yaw
- `"Targeting Hinge"` - Targeting pod pitch
- `"Bay X"` (e.g., "Bay 1", "Bay 2") - Missile merge blocks (sorted numerically)
- `"Sound Block Warning"` - Altitude/speed warnings
- `"Canopy Side Plate Sound Block"` - Air-to-air lock/search tones
- `"invertedstab"` - Right stabilizer group
- `"normalstab"` - Left stabilizer group
- Thrusters with `"Industrial"` in name are excluded from main thruster list
- Gas tanks with `"Jet"` in name are hydrogen tanks for the jet

## Common Development Workflows

### Adding a New Module

1. Create class inheriting from `ProgramModule`
2. Implement required abstract methods (`GetOptions`, `ExecuteOption`, `Tick`, etc.)
3. Add instance to `modules` list in `SystemManager.Initialize()`
4. Add menu option to `mainMenuOptions` array
5. Update module index handling in `SystemManager.Main()` switch statement

### Modifying HUD Elements

HUD rendering is in `HUDModule.Tick()`. Key subsystems:
- `DrawArtificialHorizon()` - Pitch/roll horizon with F-18 style
- `DrawFlightPathMarker()` - Velocity vector indicator
- `DrawCompassTape()` - Heading display
- `DrawAltitudeTape()` - F-18 style altitude with VVI
- `DrawLeadingPip()` - Gun targeting with ballistic calculation
- `AdjustStabilizers()` - PID-based AoA control

All HUD drawing uses sprite-based rendering via `IMyTextSurface.DrawFrame()`.

### Working with Weapon Systems

**Air-to-Ground** (`AirToGround` class):
- Manages missile bays for ground attack
- Supports bombardment patterns and top-down attack mode
- Reads GPS from `Cached:GPS:...` CustomData
- Transfers to bay-specific slots before firing

**Air-to-Air** (`AirtoAir` class):
- Radar-guided missile system using `"JetNoseRad"` turret
- Tracks target via `GetTargetedEntity()`
- Updates GPS and velocity in CustomData continuously
- Provides audio cues (AIM9Lock/AIM9Search sounds)

### Debugging

The main exception handler in `Program.Main()` catches errors and logs to Echo:
- `NullReferenceException`: Triggers automatic `SystemManager.Initialize()` recovery
- Other exceptions: Logged with type, message, and stack trace

**Add diagnostic output**:
```csharp
parentProgram.Echo($"Debug: {variableName} = {value}");
```

**Monitor performance**:
```csharp
parentProgram.Echo($"Instructions: {parentProgram.Runtime.CurrentInstructionCount}");
```

Space Engineers has instruction limits (~50,000 per tick on most servers). Monitor instruction count if experiencing performance issues.

## Known Issues and Considerations

1. **Exception Handling**: The main exception handler automatically reinitializes on NullReferenceException. This can hide bugs during development. Consider commenting out auto-recovery when debugging.

2. **CustomData Format**: Changes to CustomData format require updating both read and write operations. The dictionary cache must be invalidated (`customDataDirty = true`) after writes.

3. **Bay Array Synchronization**: If missile bays are added/removed at runtime, call `EnsureBayArraySynced()` to resize selection arrays.

4. **Performance**: The LogoDisplay module includes a Mandelbrot renderer that is computationally expensive. Avoid using screensaver mode on performance-constrained servers.

5. **Sound State Machine**: The sound system uses a 7-tick state machine for sound playback. This is legacy behavior and may be simplifiable.

6. **Block Caching**: Grid block cache is invalidated only when total block count changes. Damage that removes/adds blocks will trigger recache.

## Testing Recommendations

When making changes, test:
- **With missing blocks**: Script should handle null references gracefully
- **Module transitions**: Menu navigation between all modules
- **GPS data flow**: Verify targeting → caching → missile transfer chain
- **Performance**: Check `Runtime.CurrentInstructionCount` in various scenarios
- **Multi-tick operations**: Sound system, animations, PID convergence

## File Structure

```
JetOS/
├── Mdk.PbScript2.sln          # Visual Studio solution
├── Mdk.PbScript2/
│   ├── Mdk.PbScript2.csproj   # Project file
│   ├── Program.cs             # Main script (6300+ lines, all code in one file)
│   ├── Mdk.PbScript2.mdk.ini  # MDK build configuration
│   └── Instructions.readme     # Optional header for compiled script
├── README.md                   # User documentation and setup instructions
├── project.md                  # Performance analysis and architecture notes
└── CLAUDE.md                   # This file
```

Note: All code is in a single `Program.cs` file. Space Engineers programmable blocks require all code in one compilation unit.
