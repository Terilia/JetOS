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

### RadarTrackingModule.cs
AI block target extraction used by centralized `RadarControlModule`.

- Tracks targets via AI Flight + Combat block combinations
- Reads combat-block target waypoints without enabling flight autopilot
- Provides target position, velocity, entity id, and name to radar control

## Architecture Notes

These utilities are designed to be stateless where possible, reducing complexity and making them easy to test. The PIDController is the exception, as it must maintain state (integral error, previous error) between updates.
