# JetOS - Advanced Fighter Jet Operating System

**JetOS** is a comprehensive, military-grade operating system for fighter jets in Space Engineers. Designed for combat pilots, it provides advanced flight instrumentation, weapon systems integration, target acquisition, and automated flight control capabilities that rival modern fighter aircraft.

## 🎯 Overview

JetOS transforms your Space Engineers fighter jet into a sophisticated combat platform with:

- **F/A-18 Style HUD**: Full-featured heads-up display with artificial horizon, flight path marker, and comprehensive flight data
- **Advanced Targeting**: Integrated targeting pod with raycast acquisition and GPS coordinate caching
- **Weapon Systems**: Complete air-to-ground and air-to-air missile management with smart bay selection
- **Flight Control**: PID-based automatic stabilization system for angle of attack (AoA) management
- **Leading Pip Calculation**: Automated gun targeting with ballistic trajectory compensation
- **Combat Audio**: Situational awareness through lock-on tones and warning systems
- **Modular Architecture**: Clean, extensible module system for future enhancements

---

## ✨ Key Features

### 🎮 Advanced HUD System

The HUD module provides a complete flight instrument suite:

- **Artificial Horizon**: Pitch and roll indication with F/A-18 style pitch ladder
- **Flight Path Marker**: Real-time velocity vector display with roll compensation
- **Speed Indicators**: Displays in km/h, knots, and Mach number
- **Altitude Display**: F/A-18 style altitude tape with vertical velocity indicator (VVI)
- **Compass Tape**: 360-degree heading indicator
- **G-Force Monitor**: Current and peak G-force tracking
- **Angle of Attack (AoA)**: Real-time AoA measurement and display
- **Throttle Indicator**: Visual throttle position bar
- **Fuel Status**: Hydrogen tank level monitoring
- **Radar Scope**: Target detection and tracking display

### 🎯 Targeting Systems

#### Targeting Pod (`RaycastCameraControl`)
- Camera-based raycast targeting up to 35km range
- GPS coordinate capture and caching system
- Target tracking with automated rotor/hinge control
- LCD display integration for targeting information
- Multiple GPS slot management for rapid target switching

#### Leading Pip System
- Iterative ballistic trajectory calculation
- Compensates for:
  - Shooter velocity
  - Target velocity
  - Gravity effects
  - Projectile speed
- Automatic gatling gun fire control when on target
- Off-screen target direction indicators

### 🚀 Weapon Systems

#### Air-to-Ground (`AirToGround`)
- Multi-bay missile management system
- Individual bay selection and firing
- Bombardment mode: Distributes missiles across area targets
- Top-down attack mode for vertical strikes
- GPS coordinate transfer to missile guidance systems
- Custom data integration for target handoff

#### Air-to-Air (`AirtoAir`)
- Radar integration with "JetNoseRad" turret
- Automatic target lock acquisition
- Real-time target position and velocity caching
- Audio cues:
  - "AIM9Lock" tone when target is locked
  - "AIM9Search" tone when scanning
- Missile bay management with sequential or simultaneous firing

### 🛫 Flight Control Systems

#### PID Stabilizer Control
- Automatic angle of attack maintenance
- Proportional-Integral-Derivative (PID) controller
- Adjustable AoA offset for different flight regimes
- Automatic engagement when pilot input ceases
- Anti-windup protection
- Separate control surfaces: "normalstab" and "invertedstab"

#### Automated Systems
- Hydrogen thrust management with switchover control
- Airbrake integration (any `IMyDoor` blocks)
- Thruster override for precise throttle control
- Automatic radar cycling for ammunition loading

### 🎨 User Interface

- **Multi-Screen Support**: Cockpit surfaces for OS and secondary displays
- **Menu Navigation**: Intuitive menu system with hotkey support
- **Module Selection**: Quick access to all system modules
- **Real-time Status**: Live display of system states and selections
- **Grid Visualization**: Top-down ship schematic display
- **Mini-Game**: Frogger game for downtime entertainment

### 🔊 Audio Warning System

- Low altitude warnings ("Tief" callout when low and fast)
- Missile lock tones
- Target acquisition sounds
- Adjustable volume controls
- Multiple sound block support for spatial audio

---

## 📋 Requirements

### Development Environment
- **MDK2 (Malware's Development Kit)**: Required for compilation
  - Mal.Mdk2.PbAnalyzers 2.1.11+
  - Mal.Mdk2.PbPackager 2.1.4+
  - Mal.Mdk2.References 2.2.4+
- **.NET Framework 4.8**: Target framework
- **C# 6.0**: Language version

### In-Game Requirements
- Space Engineers game
- A fighter jet grid with appropriate blocks (see Block Setup)
- Programmable Block on the jet's grid

---

## 🔧 Installation & Setup

### Step 1: Compile the Script

Using MDK2:
```bash
# Build the project
dotnet build Mdk.PbScript2.csproj
```

The compiled script will be in your `%appdata%\SpaceEngineers\IngameScripts\local` folder.

### Step 2: Block Naming Convention

**Critical**: All blocks must be on the **same grid** as the Programmable Block. Name your blocks exactly as specified:

#### Required Blocks

| Block Type | Name | Purpose | Notes |
|------------|------|---------|-------|
| **Cockpit** | `Jet Pilot Seat` | Primary cockpit | Must implement `IMyCockpit` |
| **Cockpit (UI)** | `JetOS` | Operating system display | Used for menus (Surface 0 & 1) |
| **LCD/Surface** | `Fighter HUD` | Heads-up display | Main HUD surface |
| **Turret** | `JetNoseRad` | Primary radar/targeting | `IMyLargeGatlingTurret` |
| **Sound Block** | `Sound Block Warning` | Altitude warnings | Can have multiple |
| **Merge Blocks** | `Bay 1`, `Bay 2`, etc. | Missile bays | Numbered sequentially |
| **Terminal Block** | `normalstab` | Normal stabilizers | For AoA control |
| **Terminal Block** | `invertedstab` | Inverted stabilizers | For AoA control |

#### Optional Blocks

| Block Type | Name | Purpose |
|------------|------|---------|
| **Camera** | `Camera Targeting Turret` | Targeting pod raycast |
| **LCD** | `LCD Targeting Pod` | Targeting pod display |
| **Remote Control** | `Remote Control` | Targeting pod reference |
| **Rotor** | `Targeting Rotor` | Targeting pod azimuth |
| **Hinge** | `Targeting Hinge` | Targeting pod elevation |
| **Turret** | Contains "Radar" | Additional radar turrets |
| **Sound Block** | `Canopy Side Plate Sound Block` | Air-to-air audio cues |
| **Gas Tank** | Contains "Jet" | Hydrogen fuel tanks |
| **Door** | Any | Airbrakes (any `IMyDoor`) |

#### Automatic Detection

These blocks are auto-detected on the same grid:
- **Thrusters**: All non-"Industrial" thrusters (backward thrusters for braking detected via `GridThrustDirection.Backward`)
- **Gatling Guns**: All `IMySmallGatlingGun` blocks

### Step 3: Programmable Block Configuration

Add the following lines to your Programmable Block's **Custom Data**:

```
Cached:GPS:
CachedSpeed:
Topdown:false
AntiAir:false
CacheGPS0:
CacheGPS1:
CacheGPS2:
CacheGPS3:
DataSlot0:
DataSlot1:
DataSlot2:
DataSlot3:
```

#### Custom Data Explanation

- `Cached:GPS:` - Stores current target GPS coordinates
- `CachedSpeed:` - Stores target velocity vector for intercept calculations
- `Topdown:false` - Enable/disable top-down attack mode for AGM
- `AntiAir:false` - Air-to-air mode toggle
- `CacheGPS0-3:` - GPS coordinate slots for multiple targets
- `DataSlot0-3:` - Data transfer slots for missile guidance

**Note**: Add more `DataSlot` entries if you have more than 4 missile bays.

### Step 4: Load and Run

1. Copy the compiled script into a Programmable Block on your jet
2. Verify block naming is correct
3. Check that Custom Data is configured
4. Run the Programmable Block
5. The script automatically runs at `UpdateFrequency.Update1` (every tick)

---

## 🕹️ Controls & Usage

### Command Arguments

Assign these arguments to toolbar actions on the Programmable Block:

| Argument | Function | Description |
|----------|----------|-------------|
| `1` | **Navigate Up** | Move selection up in menus |
| `2` | **Navigate Down** | Move selection down in menus |
| `3` | **Select/Execute** | Confirm selection or execute action |
| `4` | **Back/Cancel** | Return to previous menu / deselect module |
| `5-8` | **Module Hotkeys** | Context-sensitive functions (varies by module) |
| `9` | **Main Menu** | Return to main menu from anywhere |

### Main Menu Modules

1. **Air To Ground**: Ground attack missile management
2. **Air To Air**: Air combat missile management
3. **HUD Control**: Flight instrumentation settings
4. **TargetingPod Control**: Targeting system controls
5. **Logo Display**: Ship schematic visualization
6. **Frogger Game**: Mini-game

### Module-Specific Hotkeys

#### HUD Control
- **5**: Toggle manual gatling fire override
- **6**: Increase AoA offset (+1 degree)
- **7**: Decrease AoA offset (-1 degree)
- **8**: Toggle hydrogen thrust system

#### Targeting Pod
- **5**: Execute raycast to acquire target
- **6**: Activate tracking camera view
- **7**: Toggle GPS lock / target tracking

#### Air-to-Ground
- **5**: Fire next available bay
- **6**: Fire all selected bays
- **7**: Toggle bay selection mode
- **8**: Execute bombardment pattern

#### Air-to-Air
- **5**: Fire next available bay
- **6**: Fire selected bays
- **7**: Toggle bay selection

---

## 🎓 How It Works

### HUD Rendering Pipeline

#### Artificial Horizon
1. Calculates pitch line positions based on current pitch angle
2. Scales by `pixelsPerDegree` for consistent FOV
3. Draws pitch ladder with degree labels (e.g., -80°, -70°, ... +70°, +80°)
4. Distinguishes positive/negative pitch with different styling
5. Rotates all elements around screen center based on roll angle
6. Draws central aiming reticle

#### Flight Path Marker
1. Transforms velocity vector to cockpit local coordinates
2. Calculates yaw and pitch angles relative to cockpit forward vector
3. Determines screen offset based on velocity angles
4. Rotates offset by aircraft roll
5. Draws velocity vector symbol (circle with wings)
6. Updates every frame to track velocity changes

#### Leading Pip Calculation
Uses iterative ballistic trajectory solver:

1. **Initial Setup**:
   - Gets shooter position and velocity
   - Gets target position and velocity
   - Applies projectile speed (gatling gun muzzle velocity)
   - Factors in gravity vector

2. **Iterative Refinement** (10 iterations):
   - Predicts target position at time T
   - Calculates required launch velocity accounting for gravity
   - Solves quadratic equation for intercept time
   - Refines time estimate based on actual ballistics
   - Converges on optimal intercept point

3. **Screen Projection**:
   - Transforms intercept point to cockpit local space
   - Applies perspective division (3D → 2D)
   - Rotates by aircraft roll
   - Draws pip or off-screen indicator

4. **Fire Control**:
   - Measures distance from reticle to pip
   - Enables gatling guns when pip is within threshold
   - Disables when off-target (unless manual fire enabled)

### PID Stabilizer Control

The stabilizer system maintains desired angle of attack:

```
Error = Current_AoA + User_Offset
```

**Control Loop**:
1. **Pilot Input Detection**: Monitors `cockpit.RotationIndicator.X` for pitch input
2. **PID Pause**: When pilot is pitching, PID disengages and resets integral
3. **Delay**: After pilot input ceases, waits for stabilization
4. **PID Engagement**: 
   - Proportional term: `Kp × Error`
   - Integral term: `Ki × ∫Error dt` (with anti-windup clamping)
   - Derivative term: `Kd × dError/dt`
5. **Output Application**: Trim values applied to left/right stabilizer groups with opposite signs

**Default PID Gains**:
- Kp = 1.2 (Proportional)
- Ki = 0.0024 (Integral)
- Kd = 0.5 (Derivative)
- Max Output = ±60°

### GPS Targeting & Data Flow

#### Acquisition Phase
1. **Raycast Targeting**:
   - `RaycastCameraControl` fires raycast from targeting camera
   - Hit position stored in `Cached:GPS:Target2:X:Y:Z:#FF75C9F1:`

2. **Air-to-Air Lock**:
   - `AirtoAir` module reads turret target via `GetTargetedEntity()`
   - Position and velocity cached in `Cached:GPS:Target2:...` and `CachedSpeed:...`

#### Transfer Phase
1. Module reads coordinates from `Cached:GPS:...`
2. Parses X, Y, Z coordinates
3. For bombardment mode, calculates spread pattern

#### Launch Phase
1. Selected bay GPS data written to bay-specific cache: `Cache<N>:GPS:Target:...`
2. Merge block triggered via `ApplyAction("Fire")`
3. GPS data transferred to missile-readable format: `<BayNumber>:GPS:Target:...`
4. Cache cleared after successful transfer
5. Missile script on detached missile reads targeting data

---

## ⚙️ Advanced Configuration

### Adjusting PID Parameters

Located in `HUDModule` class (around line 2260):

```csharp
private float Kp = 1.2f;      // Proportional gain
private float Ki = 0.0024f;   // Integral gain  
private float Kd = 0.5f;      // Derivative gain
```

**Tuning Guide**:
- **Increase Kp**: Faster response, but may oscillate
- **Increase Ki**: Eliminates steady-state error, but can cause windup
- **Increase Kd**: Reduces overshoot, but sensitive to noise

### Manual Fire Override

Set in `Jet` class (line 65):
```csharp
public bool manualfire = true;  // false for automatic fire control
```

When `false`, gatling guns fire automatically when leading pip is aligned.

### AoA Offset

Adjust in-flight using hotkeys 6 and 7, or set default in `Jet` class:
```csharp
public int offset = 0;  // Degrees of AoA offset
```

Positive values pitch up, negative pitch down.

### Sound Thresholds

Located in `SystemManager.Main()` (around line 348):
```csharp
if (velocityKnots > 350 && altitude < 400)
{
    selectedsound = "Tief";  // German for "low"
}
```

Customize velocity and altitude thresholds as needed.

---

## 🐛 Troubleshooting

### Common Issues

#### "No HUD Display"
- **Check**: Block named `Fighter HUD` exists and is functional
- **Check**: Block implements `IMyTextSurface` or `IMyTextSurfaceProvider`
- **Check**: Block is on the same grid as the Programmable Block
- **Solution**: Verify `hudBlock` is found during initialization

#### "No Targeting"
- **Check**: Camera named `Camera Targeting Turret` exists
- **Check**: Camera has clear line of sight
- **Check**: Camera is powered and functional
- **Check**: Camera scan range ≥35km
- **Solution**: Try manual raycast via hotkey 5 in Targeting Pod module

#### "Missiles Don't Get Target Data"
- **Check**: Custom Data contains `Cached:GPS:` entry
- **Check**: Target has been acquired via raycast or radar lock
- **Check**: Bay-specific `DataSlot` entries exist in Custom Data
- **Check**: Merge block is named correctly (e.g., `Bay 1`)
- **Solution**: Verify GPS coordinates appear in Custom Data after target acquisition

#### "Stabilizers Not Working"
- **Check**: Blocks named `normalstab` and `invertedstab` exist
- **Check**: Blocks support `Trim` property (control surfaces)
- **Check**: AoA offset is reasonable (-10° to +10°)
- **Solution**: Test manual trim control in terminal to verify block functionality

#### "Audio Not Playing"
- **Check**: Sound blocks named `Sound Block Warning` exist
- **Check**: Sound blocks have the required sound files ("Tief", "AIM9Lock", "AIM9Search")
- **Check**: Sound blocks are powered and functional
- **Solution**: Check in-game sound block settings

#### "Script Crashes on Start"
- **Check**: Cockpit named `Jet Pilot Seat` exists
- **Check**: Cockpit named `JetOS` exists (for UI)
- **Check**: All required blocks are on the same grid
- **Solution**: Review initialization in `SystemManager.Initialize()`

### Performance Optimization

If experiencing slowdowns:

1. **Reduce HUD Complexity**: Comment out expensive draw calls
2. **Increase Update Interval**: Change from `Update1` to `Update3` or `Update10`
3. **Disable Unused Modules**: Comment out module additions in `SystemManager.Initialize()`
4. **Optimize Raycast Frequency**: Add tick counter to limit raycast rate

---

## 🏗️ Architecture

### Module System

All modules extend `ProgramModule` abstract class:

```csharp
abstract class ProgramModule
{
    public string name;
    public abstract string[] GetOptions();
    public abstract void ExecuteOption(int index);
    public virtual void HandleSpecialFunction(int key) { }
    public virtual void Tick() { }
    public virtual string GetHotkeys() { return ""; }
}
```

**Current Modules**:
- `AirToGround`: Ground attack weapons
- `AirtoAir`: Air-to-air weapons
- `HUDModule`: Flight instruments
- `RaycastCameraControl`: Targeting pod
- `LogoDisplay`: Ship visualization
- `FroggerGameControl`: Mini-game

### Class Structure

- **`Program`**: Main entry point, extends `MyGridProgram`
- **`SystemManager`**: Orchestrates modules, handles input, manages UI
- **`Jet`**: Encapsulates all jet blocks and utilities
- **`UIController`**: Renders menus and information panels
- **`PIDController`**: Generic PID controller implementation
- **`VectorMath`**: Vector mathematics utilities

### Data Flow

```
User Input (Toolbar)
    ↓
SystemManager.Main()
    ↓
Module Selection / Execution
    ↓
Module.Tick() / Module.ExecuteOption()
    ↓
Jet Block Control
    ↓
Game State Update
```

---

## 🤝 Contributing
?
