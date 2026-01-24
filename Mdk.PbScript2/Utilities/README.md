# Utilities

This folder contains utility classes and helper functions used across JetOS modules.

## Files

### PIDController.cs
A proportional-integral-derivative (PID) controller for smooth control system feedback.

- Configurable Kp, Ki, Kd gains
- Integral anti-windup clamping
- Used for camera stabilization and flight control

### NavigationHelper.cs
Static helper class for navigation calculations.

- `CalculateHeading(IMyCockpit)` - Calculates compass heading (0-360) from cockpit orientation
- Projects forward vector onto horizontal plane using gravity
- Returns 0 if gravity is not available

### CommonTypes.cs
Common data structures shared across modules:

- `RWRWarning` - Radar Warning Receiver threat data (position, type, incoming flag)
- `Player` - Player entity for mini-games
- `Obstacle` - Obstacle entity for mini-games
- `Vector2I` - Integer 2D vector for grid calculations

### RadarTrackingModule.cs (Deprecated)
Legacy radar tracking using AI block pairs. Replaced by centralized `RadarControlModule`.

- Tracks targets via AI Flight + Combat block combinations
- Maintained for backward compatibility
- New code should use `Jet.radarControl` instead

## Architecture Notes

These utilities are designed to be stateless where possible, reducing complexity and making them easy to test. The PIDController is the exception, as it must maintain state (integral error, previous error) between updates.
