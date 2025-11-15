# JetOS Development Log

## Current Status

Implemented multi-tick sound state machine for both altitude warnings and air-to-air radar sounds.

## Problem Summary

When using **mouse yaw** to look left/right, HUD target indicators appear on the **wrong side** of the screen. The behavior is **inconsistent depending on location on the planet**, which suggests a coordinate system or reference frame issue rather than a simple sign flip.

## What We've Fixed So Far (Working)

✅ **Airspeed Bug** - Fixed velocity accumulation in CircularBuffer (was continuously increasing)
✅ **AOA Indexer** - Fixed Unicode symbols (now uses Triangle/Circle sprites instead of "?")
✅ **Fuel Ring** - Moved to Surface 1 (left side) and integrated with jet health grid
✅ **Weapon Screen** - Cleaned up styling with better colors, spacing, and panels
✅ **G-Force & AoA Smoothing** - Fixed same accumulation bug as velocity

## Current Code State

**X-Axis Projection: Using `+` operator (ORIGINAL CODE)**

All 7 instances of screen projection currently use:
```csharp
float screenX = center.X + (float)(localDirection.X / -localDirection.Z) * scaleX;
```

**Affected Functions:**
1. Line 2283 - `DrawLeadingPip()` - Gun leading indicator
2. Line 2342 - Target velocity endpoint visualization
3. Line 2358 - Current target position projection
4. Line 3055 - Leading pip (alternate path)
5. Line 4161 - `DrawTargetBrackets()` - Target acquisition brackets
6. Line 4397 - `DrawFunnelLines()` - Funnel line convergence
7. Line 4925 - `DrawFormationGhosts()` - Wingman formation display

## What We've Tried

1. **First Attempt**: Changed all `+` to `-` thinking it was a simple inversion
   - Result: Made it worse, now inverted the opposite way

2. **Second Attempt**: Reverted all back to original `+`
   - Result: Back to original behavior (inconsistent based on planet location)

## Key Symptom: Location-Dependent Behavior

**User reported**: "It depends where I am on the planet"

This is **NOT** a simple sign flip. This suggests:
- Coordinate system handedness changes in different contexts
- Planetary reference frame interference
- Gimbal lock or singularity at certain orientations (poles?)
- Possible Space Engineers engine quirk with `WorldMatrix` transformation

## Required Tests (WHEN GAME WORKS AGAIN)

Test the **CURRENT DEPLOYED CODE** (with `+` operator) in these scenarios:

### Test 1: North Pole Region
```
1. Face a stationary target
2. Use mouse to YAW LEFT
3. Observe: Does target appear on LEFT or RIGHT of HUD?
   Expected: Target should appear on RIGHT (you turned away from it)
```

### Test 2: Equator
```
1. Face same or different target
2. Use mouse to YAW LEFT
3. Observe: Does target appear on LEFT or RIGHT of HUD?
```

### Test 3: South Pole / Opposite Hemisphere
```
1. Travel to opposite side of planet
2. Face a target
3. Use mouse to YAW LEFT
4. Observe: Does target appear on LEFT or RIGHT of HUD?
```

### Test 4: Different Cardinal Directions
```
At same location, face:
- North and yaw left
- South and yaw left
- East and yaw left
- West and yaw left

Does behavior change based on which direction you're facing?
```

## Diagnostic Pattern Analysis

Based on test results, determine root cause:

| Test Results | Root Cause | Fix |
|--------------|------------|-----|
| **Always wrong everywhere** | Simple sign error | Change `+` to `-` in all 7 locations |
| **Wrong at poles, correct at equator** | Planetary coordinate system bug | Need to account for latitude/singularity |
| **Wrong in one hemisphere only** | Handedness/coordinate frame issue | May need hemisphere detection |
| **Changes with facing direction** | Gimbal lock or axis alignment issue | Complex fix needed |
| **Inconsistent/random** | WorldMatrix not updating properly | May need to use different reference |

## Code References

**Transformation being used:**
```csharp
MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
Vector3D localDirection = Vector3D.TransformNormal(worldPosition - cockpitPosition, worldToCockpitMatrix);
```

**Projection formula:**
```csharp
const float COCKPIT_FOV_SCALE_X = 0.3434f;
const float COCKPIT_FOV_SCALE_Y = 0.31f;
float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;

// X-axis (ISSUE IS HERE):
float screenX = center.X + (float)(localDirection.X / -localDirection.Z) * scaleX;

// Y-axis (seems correct):
float screenY = center.Y + (float)(-localDirection.Y / -localDirection.Z) * scaleY;
```

## Possible Solutions (Ranked by Likelihood)

### Solution 1: Simple Sign Flip (If "always wrong everywhere")
Change all 7 instances from `+` to `-`:
```csharp
float screenX = center.X - (float)(localDirection.X / -localDirection.Z) * scaleX;
```

### Solution 2: Use Right Vector Instead of X (If coordinate handedness issue)
```csharp
Vector3D rightDirection = Vector3D.TransformNormal(worldPos - cockpitPos, worldToCockpitMatrix);
float screenX = center.X + (float)(-rightDirection.X / -rightDirection.Z) * scaleX;
```

### Solution 3: Use Gravity-Aligned Reference Frame
Instead of pure cockpit WorldMatrix, create a gravity-aligned reference:
```csharp
Vector3D gravity = cockpit.GetNaturalGravity();
Vector3D worldUp = (gravity.LengthSquared() > 0.01) ? -Vector3D.Normalize(gravity) : cockpit.WorldMatrix.Up;
Vector3D shipForward = cockpit.WorldMatrix.Forward;
Vector3D yawForward = Vector3D.Normalize(Vector3D.Reject(shipForward, worldUp));
Vector3D yawRight = Vector3D.Cross(yawForward, worldUp);
// Build custom transformation matrix using yawForward, yawRight, worldUp
```

### Solution 4: Negate Based on Hemisphere
Detect planetary hemisphere and flip sign accordingly:
```csharp
Vector3D planetCenter = /* get from natural gravity vector */;
bool isNorthernHemisphere = /* calculate */;
float sign = isNorthernHemisphere ? 1f : -1f;
float screenX = center.X + sign * (float)(localDirection.X / -localDirection.Z) * scaleX;
```

## Next Steps

1. **Run the diagnostic tests above** when game is working
2. **Record exact behavior** at each location
3. **Identify the pattern** from test results table
4. **Apply the appropriate solution** based on pattern
5. **Test the fix** at all previously problematic locations

## Contact Points for Help

- **Test results needed**: Which side does target appear when yawing left?
- **Location matters**: Test at poles vs equator vs different hemispheres
- **Direction matters**: Test facing north/south/east/west

## File Locations

- **Main code**: `C:\Users\xerdi\source\repos\Terilia\JetOS\Mdk.PbScript2\Program.cs`
- **Build command**: `dotnet build Mdk.PbScript2.sln --configuration Release`
- **Deployed to**: `AppData\Roaming\SpaceEngineers\IngameScripts\local\Mdk.PbScript2`

---

**Last Build**: Successfully deployed with `+` operator (original code restored)
**Last Modified**: All X-axis projections reverted to original behavior
**Status**: Awaiting diagnostic test results to determine proper fix
