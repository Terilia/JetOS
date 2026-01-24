# Modules

This folder contains the main functional modules of JetOS. Each module inherits from `ProgramModule` and provides specific aircraft functionality.

## Base Class

### ProgramModule.cs
Abstract base class that all modules must inherit from.

Required implementations:
- `GetOptions()` - Returns menu option strings
- `ExecuteOption(int)` - Handles menu selection
- `Tick()` - Per-frame update (60 Hz)
- `HandleSpecialFunction(int)` - Hotkey handling (keys 5-8)
- `GetHotkeys()` - Returns help text for available hotkeys

Optional overrides:
- `HandleNavigation(bool)` - Custom up/down navigation
- `HandleBack()` - Custom back button behavior

## Feature Modules

### HUDModule.cs (~3,470 lines)
The largest module - renders the F-18 style heads-up display.

Features:
- Artificial horizon with pitch ladder
- Flight path marker (velocity vector)
- Speed and altitude tapes
- Compass with heading indicator
- Leading pip (gun sight) with ballistic prediction
- Multi-target radar display
- RWR threat cones
- G-force and AOA indicators
- Weapon screen rendering

### RadarControlModule.cs
Centralized radar and RWR (Radar Warning Receiver) management.

Features:
- Manages all AI Flight/Combat block pairs
- Tracks multiple enemy contacts with decay
- Assigns radars to requesting modules
- Threat detection and warning

### AirtoAir.cs
Air-to-air combat module for missile engagements.

Features:
- Radar lock/search using AI blocks
- Target velocity tracking
- Audio cues (AIM-9 lock/search tones)
- GPS coordinate caching for missiles

### AirToGround.cs
Ground attack module for bombing and missile strikes.

Features:
- Missile bay management
- Bombardment pattern generation
- Top-down attack mode toggle
- GPS target caching and transfer

### RaycastCameraControl.cs
Targeting pod control using camera raycasting.

Features:
- Camera turret control via rotor/hinge
- PID-stabilized tracking
- GPS coordinate extraction from raycasts
- Target slot population

### ConfigurationModule.cs
Settings and configuration management.

Features:
- Hierarchical menu system
- Category-based parameter organization
- Value adjustment with step controls
- Save/load via CustomData
- Config import/export

### LogoDisplay.cs
Animated screensaver/splash screen.

Features:
- Particle effects (snowflakes, dust)
- Animated logo with pulsing
- Motivational text rotation
- Easter egg "evil" messages (rare)

## Module Communication

Modules communicate primarily through:
1. The shared `Jet` instance (hardware abstraction)
2. CustomData storage (GPS coordinates, configuration)
3. Direct module references (e.g., HUDModule references RadarControlModule)

The `SystemManager` orchestrates module lifecycle and ensures critical modules (radar, HUD) run even when not actively selected.
