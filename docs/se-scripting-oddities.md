# Space Engineers Scripting Oddities

Reference compiled from Whiplash141's SpaceEngineersScripts repository (gold-standard SE scripts).
Cross-referenced with JetOS codebase experience and SE decompiled sources.

---

## 1. Coordinate System: Forward = -Z

SE inherited XNA/MonoGame conventions. `WorldMatrix.Forward` returns the **-Z basis vector**.

```csharp
// In local space, "directly ahead" is (0, 0, -1)
// To compute yaw angle to a target in local space:
yaw = Math.Atan2(localTarget.X, -localTarget.Z);  // must negate Z
```

**Impact everywhere:** Any `atan2` yaw calculation needs `-Z`. A target "behind" has `Z > 0`. The degenerate case check `forwardVector.Z < 0 ? 0 : Math.PI` means negative Z = aligned, positive Z = backwards.

From `GetRotationVector` (WHAM.cs:4287):
```csharp
angle = Math.Acos(MathHelper.Clamp(-forwardVector.Z, -1.0, 1.0));
// When forwardVector = (0,0,-1) → acos(1) = 0 → no rotation needed
```

---

## 2. Gyro Axes Are Inverted ("Backwards Ass Rotation Axes")

Whiplash's own words (WHAM.cs:4197-4201):
```
Takes pitch, yaw, and roll speeds relative to the gyro's backwards
ass rotation axes.
```

Documented formally (WHAM.cs:4240-4244):
```csharp
/// + pitch = -X rotation,
/// + yaw   = -Y rotation,
/// + roll  = -Z rotation
```

**Positive gyro.Pitch rotates in the -X direction**, opposite to mathematical convention. The `ApplyGyroOverride` double-transform cancels this out implicitly, but if you ever set gyro properties directly, you must account for the inversion.

---

## 3. Gyro Override Requires Per-Gyro Double Transform

Gyros can be placed in any orientation. You cannot just set `gyro.Pitch = desiredPitch`.

From `ApplyGyroOverride.cs`:
```csharp
void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed,
    List<IMyGyro> gyroList, MatrixD worldMatrix)
{
    var rotationVec = new Vector3D(pitchSpeed, yawSpeed, rollSpeed);
    // Step 1: reference-local → world
    var relativeRotationVec = Vector3D.TransformNormal(rotationVec, worldMatrix);
    foreach (var thisGyro in gyroList)
    {
        // Step 2: world → each gyro's local frame
        var transformedRotationVec = Vector3D.TransformNormal(
            relativeRotationVec, Matrix.Transpose(thisGyro.WorldMatrix));
        thisGyro.Pitch = (float)transformedRotationVec.X;
        thisGyro.Yaw = (float)transformedRotationVec.Y;
        thisGyro.Roll = (float)transformedRotationVec.Z;
        thisGyro.GyroOverride = true;
    }
}
```

Note: WHAM uses `Matrix.Transpose` (single-precision) for the gyro transform, not `MatrixD.Transpose`. Presumably to match the `float` gyro properties.

---

## 4. Gyro Override Values Persist After Disable

```csharp
// WRONG: just disabling leaves stale values
gyro.GyroOverride = false;
// Later re-enabling causes a lurch from the old Pitch/Yaw/Roll

// CORRECT: zero then disable
gyro.Pitch = 0;
gyro.Yaw = 0;
gyro.Roll = 0;
gyro.GyroOverride = false;
```

If the script crashes mid-tick, gyros keep their last override values and the ship spins uncontrollably.

---

## 5. MatrixD.Left Setter Stores -Left as Right

```csharp
matrix.Left = someVector;
// Internally: matrix.Right = -someVector
// Reading back: matrix.Left == someVector (negates again on read)
```

This is transparent for read-back but breaks `Vector3D.TransformNormal()` if you construct matrices manually via `.Left`. Whiplash only sets `Forward`, `Left`, `Up` via the object initializer in `GetRotationVector` (WHAM.cs:4297-4302) where the Rodrigues axis-angle extraction accounts for the internal representation.

**Safe rule:** Always use `Right`, `Up`, `Forward` directly, or use `MatrixD.CreateWorld()`.

---

## 6. Matrix Transpose as Inverse (Performance Optimization)

```csharp
// World → local
var localVec = Vector3D.TransformNormal(worldVec, MatrixD.Transpose(reference.WorldMatrix));
// Local → world
var worldVec = Vector3D.TransformNormal(localVec, reference.WorldMatrix);
```

`Transpose` is O(1) while `Invert` is O(n^3). Only works because `WorldMatrix` is orthonormal (pure rotation). Under the 50,000 instruction limit, this matters.

---

## 7. AngleBetween: atan2 vs acos

**Whiplash's standalone VectorMath.cs** uses the simple `acos` approach:
```csharp
return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
```

**WHAM.cs (production missile code)** upgraded to `atan2`:
```csharp
return Math.Atan2(Vector3D.Cross(a, b).Length(), Vector3D.Dot(a, b));
```

With the comment: *"This uses atan2 to avoid numerical precision issues associated with acos based dot-product backsolving."*

`acos` has severe precision loss near 0 and PI (derivative → infinity). `atan2` gives full-range precision. The `MathHelper.Clamp` on the acos version prevents NaN from float imprecision > 1.0, but doesn't fix the precision loss itself.

---

## 8. Positive X = Left (Not Right)

From `GetRotationAngles` (with roll), line 12:
```csharp
yaw = VectorMath.AngleBetween(Vector3D.Forward, flattenedTargetVector) * yawSign; //right is positive
```

But in turret_slaver.cs:2617:
```csharp
yaw = VectorMath.AngleBetween(Vector3D.Forward, flattenedTargetVector)
    * Math.Sign(localTargetVector.X); //left is positive
```

The X axis in SE's local coordinate system points **left**. Different Whiplash scripts handle the sign differently depending on the context (cockpit reference vs turret reference). Always check whether your convention expects left-positive or right-positive.

---

## 9. Pitch Sign Is Inverted

```csharp
// Up is positive for the sign:
pitch = VectorMath.AngleBetween(localTargetVector, flattenedTargetVector)
    * Math.Sign(localTargetVector.Y); //up is positive
```

But gyro pitch-positive means nose **down**. The `ApplyGyroOverride` function compensates through its transform chain. If you ever set gyro pitch directly, negate it.

---

## 10. Simultaneous vs Sequential Rotation

The naive decomposition (project to XZ, get yaw; project to YZ, get pitch) is **sequential** -- correct only if you yaw first, then pitch. It wobbles when both angles are large.

`GetRotationAnglesSimultaneous` (WHAM.cs `GetRotationVector`) solves this with axis-angle extraction from a full rotation matrix using Rodrigues' formula:
```csharp
axis = new Vector3D(M32 - M23, M13 - M31, M21 - M12);
double trace = M11 + M22 + M33;
angle = Math.Acos(MathHelper.Clamp((trace - 1) * 0.5, -1.0, 1.0));
```

Then `pitch = axisAngle.X; yaw = axisAngle.Y; roll = axisAngle.Z;`

---

## 11. Cross Product and Rotor RPM: CCW Positive

SE positive RPM = **counterclockwise** when viewed from the rotor head toward the base. This is opposite to what most developers expect.

Cross-product-based angle signs must be negated for rotor control. The elevation sign trick:
```csharp
int elevationSign = Math.Sign(Vector3D.Dot(turretBaseMatrix.Left, elevationRotor.WorldMatrix.Up));
```
Handles left-side vs right-side mounting automatically.

---

## 12. Thruster Orientation: Nozzle vs Thrust Direction

`thruster.Orientation.Forward` points where the **nozzle faces** (exhaust direction) -- opposite to the push direction.

```csharp
// WRONG: this matches nozzle direction
if (t.Orientation.Forward == reference.Orientation.Forward)

// CORRECT: flip to get thrust direction
var thrustDirn = Base6Directions.GetFlippedDirection(t.Orientation.Forward);
if (thrustDirn == reference.Orientation.Forward)
    _mainThrusters.Add(t);
```

---

## 13. Thrust Override 0 = "No Override"

Setting `ThrustOverride` or `ThrustOverridePercentage` to exactly 0 reverts the thruster to dampener/autopilot control. It does NOT mean "zero thrust."

```csharp
const float MinThrust = 1e-9f;  // WHAM.cs:248
// Use this instead of 0 to keep override active with negligible thrust
thruster.ThrustOverridePercentage = MinThrust;
```

`MaxEffectiveThrust` (not `MaxThrust`) should be used for thrust calculations -- it accounts for atmospheric and efficiency modifiers.

---

## 14. Conditional Property Writes (Network Sync)

Every block property write in multiplayer triggers a network sync packet regardless of whether the value changed.

```csharp
// WRONG: triggers sync every tick
t.Enabled = true;

// CORRECT: only write if different
if (t.Enabled != turnOn)
    t.Enabled = turnOn;
if (thrustProportion != t.ThrustOverridePercentage)
    t.ThrustOverridePercentage = thrustProportion;
```

---

## 15. Block Enumeration: IsSameConstructAs vs CubeGrid

```csharp
// Same grid segment only (excludes subgrids):
b.CubeGrid == Me.CubeGrid

// Full mechanical chain (rotors/pistons/hinges, NOT connectors):
b.IsSameConstructAs(Me)

// ALL connected grids (including via connectors):
GridTerminalSystem.GetBlocksOfType<T>(list)  // no filter
```

For vehicles with rotored turrets, `IsSameConstructAs` is usually correct. `CubeGrid ==` misses subgrid blocks.

---

## 16. GetBlocksOfType with null List Hack

```csharp
// Allocates no list; filter does all the work as side effects
GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, block => {
    if (block is IMyCameraBlock)
        cameras.Add((IMyCameraBlock)block);
    return false;  // always false = don't build a list
});
```

Single-pass multi-type block collection. Avoids the cost of multiple `GetBlocksOfType<T>` calls.

---

## 17. Camera Raycast Charges Over Time

```csharp
camera.EnableRaycast = true;  // must pre-enable to accumulate charge

// Check if enough range has accumulated
if (camera.AvailableScanRange >= scanRange)
{
    MyDetectedEntityInfo info = camera.Raycast(targetPos);
}

// Charge rate: ~1000 * RaycastTimeMultiplier meters per second per camera
// Scan interval = scanRange / (1000 * camera.RaycastTimeMultiplier) / cameraCount
```

`MyDetectedEntityInfo` is a struct. `info.IsEmpty()` must be checked (not null). `info.Velocity` is `Vector3` (single-precision), not `Vector3D`.

Camera FOV check -- Z > 0 means behind (because forward = -Z):
```csharp
Vector3D local = Vector3D.Rotate(direction, MatrixD.Transpose(camera.WorldMatrix));
if (local.Z > 0) return false;  // behind camera
var yawTan = Math.Abs(local.X / local.Z);
return yawTan <= 1;  // ~90 degree cone
```

---

## 18. Runtime Timing Is Not Wall-Clock

```csharp
const double RuntimeToRealtime = (1.0 / 60.0) / 0.0166666;

// TimeSinceLastRun can be NEGATIVE after world load
var dt = RuntimeToRealtime * Math.Max(Runtime.TimeSinceLastRun.TotalSeconds, 0);

// LastRunTimeMs reports the PREVIOUS tick's runtime, not current
double prevTickMs = Runtime.LastRunTimeMs;
```

The Scheduler converts .NET TimeSpan ticks to game ticks:
```csharp
const long ClockTicksPerGameTick = 166666L; // 100ns * 166666 = 1/60s
long deltaTicks = Math.Max(0, Runtime.TimeSinceLastRun.Ticks / ClockTicksPerGameTick);
```

---

## 19. IGC Messages Are One Tick Delayed

```csharp
_timeSinceLastIngest = Tick; // IGC messages are always a tick delayed
```

SE processes IGC at end of tick, delivers at start of next. For target extrapolation, add 1/60s to account for transmission delay.

---

## 20. Warhead StartCountdown Has One-Tick Delay

```csharp
thisWarhead.DetonationTime = Math.Max(0f, (float)fuzeTime - 1f / 60f);
thisWarhead.StartCountdown();
```

Subtract 1/60s from fuse time because `StartCountdown()` doesn't begin counting until the next tick.

---

## 21. Turret Target Refresh: Toggle Trick

No API to force a vanilla turret to re-evaluate targets. Must toggle each filter off then on:

```csharp
void RefreshDesignatorTargeting()
{
    foreach (var turret in _designators)
    {
        if (t.TargetMissiles) { t.TargetMissiles = false; t.TargetMissiles = true; }
        if (t.TargetSmallGrids) { t.TargetSmallGrids = false; t.TargetSmallGrids = true; }
        // ... same for each filter
    }
}
```

Only toggle filters that are currently enabled. Runs at ~0.25 Hz.

---

## 22. Turret Aim Direction: Must Reconstruct from Az/El

```csharp
Vector3D VectorAzimuthElevation(IMyLargeTurretBase turret)
{
    Vector3D.CreateFromAzimuthAndElevation(turret.Azimuth, turret.Elevation, out Vector3D dir);
    return Vector3D.TransformNormal(dir, turret.WorldMatrix);
}
```

No direct "aim direction" property exists.

Custom Turret Controller `GetShootDirection()` returns `Vector3D.Forward` (0,0,-1) as sentinel when no target -- must check for this.

---

## 23. Instruction Limit Coroutines (yield return)

SE has no async/await, no threading. C# iterators as cooperative multitasking:

```csharp
IEnumerator<SetupStatus> SetupStateMachine()
{
    foreach (var block in bigList)
    {
        ProcessBlock(block);
        if (Runtime.CurrentInstructionCount >= 5000)
            yield return SetupStatus.Running;  // pause, resume next tick
    }
    yield return SetupStatus.Done;
}
```

The 5,000 threshold (vs 50,000 limit) leaves headroom for other operations.

---

## 24. UpdateFrequency.Once for Deferred Init

Some API calls fail in the `Program()` constructor. Standard workaround:

```csharp
Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Once;  // run Main() once next tick
}

void Main(string arg, UpdateType updateSource)
{
    if ((updateSource & UpdateType.Once) != 0)
    {
        // Safe to call ApplyAction, terminal properties, etc. here
    }
}
```

---

## 25. PID Controller Oddities

**First-run guard:** Derivative is zero on tick 1 (no prior error exists):
```csharp
if (_firstRun) { errorDerivative = 0; _firstRun = false; }
```

**Variable timestep:** Must pass `Runtime.TimeSinceLastRun.TotalSeconds` -- tick rate is NOT constant:
```csharp
pid.Control(error, Runtime.TimeSinceLastRun.TotalSeconds);
```

**Decaying integral** for sudden state changes (docking, terrain hits):
```csharp
errorSum = errorSum * (1.0 - decayRatio) + currentError * timeStep;
```

**Buffered integral** via sliding window Queue -- forgets old errors naturally.

---

## 26. Sprite/LCD Oddities

**No line primitive.** Lines are rotated `SquareSimple` sprites:
```csharp
Vector2 size = new Vector2(length, width);
float angle = (float)Math.Acos(Vector2.Dot(diff, Vector2.UnitX)) * Math.Sign(Vector2.Dot(diff, Vector2.UnitY));
sprite = MySprite.CreateSprite("SquareSimple", position, size);
sprite.RotationOrScale = angle;
```

**Sprite rotation 0 = pointing right (+X), positive = counterclockwise** in screen space (Y-down).

**Must set ContentType and clear Script:**
```csharp
surface.ContentType = ContentType.SCRIPT;
surface.Script = "";  // disable built-in TSS
var frame = surface.DrawFrame();
// ... add sprites ...
frame.Dispose();  // MUST call or nothing renders
```

**No pixel buffer.** Images are one sprite per pixel. 64x64 = 4096 sprites.

**Texture names are magic strings:** `"SquareSimple"`, `"Circle"`, `"Triangle"`, `"SemiCircle"`, `"RightTriangle"` -- discovered by trial and error, not documented.

---

## 27. No Cardinal Directions

SE worlds have no "north." Compass scripts pick an arbitrary world axis:
```csharp
Vector3D northRef = Vector3D.Reject(new Vector3D(0, -1, 0), gravityNorm);
```

`GetNaturalGravity()` returns `Vector3D.Zero` in space. Always guard against this.

---

## 28. Mass Types

```csharp
var massInfo = controller.CalculateShipMass();
massInfo.PhysicalMass  // actual physics mass (use for thrust calcs)
massInfo.TotalMass     // includes virtual mass blocks
massInfo.BaseMass      // without cargo
```

---

## 29. Inertia Tensor Not Exposed

Must estimate by iterating all grid cells (parallel axis theorem):
```csharp
MatrixD EstimateInertiaTensor(IMyShipController reference)
{
    // Iterate grid.Min → grid.Max, check CubeExists at each cell
    // Accumulate I_xx, I_yy, I_zz, I_xy, I_yz, I_xz per block
    // Scale by (shipMass / blockCount)
}
```

Gyro torque values (3.36 MN*m large grid, 448.8 kN*m small grid) are also undocumented -- must be read from SBC files or measured empirically.

---

## 30. Drag Force Is Reverse-Engineered

Parachute drag formula (CalculateDragForce.cs) uses hardcoded constants matching SE's internal implementation:
```csharp
double num = 10.0 * (currentAtmosphere - 0.6);
num = num < 5 ? 5 : Math.Max(Math.Log(num - 0.99) + 5.0, 5.0);
double chuteRadius = num * 8.0 * gridSize / 2;
return 2.5 * (currentAtmosphere * 1.225) * Math.PI * chuteRadius * chuteRadius * dragCoeff;
```

The `1.225` is sea-level air density (kg/m^3). These constants were reverse-engineered from SE source.

---

## 31. Antenna Enable Delay (HUD Bug)

```csharp
const double AntennaEnableDelay = 2; // To prevent HUD bug
a.Enabled = false;
_scheduler.AddQueuedAction(EnableAntennas, AntennaEnableDelay);
```

Enabling antenna same tick as grid detachment shows the HUD marker at the wrong position. 2-second delay lets grid separation finalize.

Setting `antenna.Radius = 1f` (not 0) when not broadcasting -- 0 may have edge-case behavior.

---

## 32. PN Guidance: Close-Range Instability Guard

```csharp
Vector3D omega = Vector3D.Cross(missileToTarget, relativeVelocity)
    / Math.Max(missileToTarget.LengthSquared(), 1); // combat instability at close range
```

Standard PN formula `omega = (r x v) / |r|^2` explodes as range → 0. Clamping denominator to 1 prevents wild terminal oscillations.

---

## 33. IGC Packing: Matrix3x3 as Vector Container

```csharp
// Pack 3 Vector3s into one Matrix3x3:
// Item1.Col0: Hit position
// Item1.Col1: Target position
// Item1.Col2: Target velocity
MyTuple<Matrix3x3, Matrix3x3, float, long, long> payload;
```

`Matrix3x3` uses single-precision `Vector3` columns -- precision loss for large-coordinate positions.

---

## 34. TerminalAction/Property Lookup Is Expensive

`block.GetActionWithName()` iterates internal lists. Cache by block type:

```csharp
static Dictionary<Type, Dictionary<string, ITerminalAction>> _cache;

ITerminalAction GetAction(IMyTerminalBlock block, string name)
{
    Type type = block.GetType();
    // Check cache first, only call block.GetActionWithName() on miss
}
```

All blocks of the same runtime type share terminal actions, so cache by `GetType()`.

---

## 35. Weapon ShootOnce Requires Enabled

```csharp
weapon.Enabled = true;
weapon.ShootOnce();
// ... later
weapon.Enabled = false;
```

`ShootOnce()` on a disabled weapon does nothing. For salvos, enable one weapon per tick, fire, disable.

---

## 36. DoorStatus Has 4 States

```csharp
// Open, Opening, Closing, Closed
if (door.Status == DoorStatus.Open) door.CloseDoor();
// door.Enabled = false cuts power (stops mid-animation) -- different from CloseDoor()
```

---

## 37. Hydrogen vs Oxygen Tanks: No Separate Interface

```csharp
// Both are IMyGasTank -- must string-match to distinguish
if (tank.DefinitionDisplayNameText.Contains("Hydrogen")) { ... }
```

---

## 38. DetailedInfo Is Localized Text

Many block properties only available by parsing `block.DetailedInfo`, which is a localized human-readable string. Changes with game language. Later SE updates added structured properties for some data (e.g., `IMyBatteryBlock.CurrentStoredPower`), but many blocks still require text parsing.

---

## 39. Proximity Detonation: Cross-Pattern Raycast

SE raycast is a single ray, not a cone. WHAM casts 5 rays in a + pattern:
```csharp
var perp1 = Vector3D.CalculatePerpendicularVector(closingVelocity) * apparentRadius;
var perp2 = SafeNormalize(Vector3D.Cross(perp1, closingVelocity)) * apparentRadius;
// Center + 4 perpendicular offsets
RaycastTripwire(pos) || RaycastTripwire(pos + perp1) || RaycastTripwire(pos - perp1) || ...
```

`apparentRadius` computed from grid bounding box projected onto the closing velocity axis.

---

## 40. Gravity Compensation: Hover-Impossibility Fallback

```csharp
Vector3D gravityComp = -(VectorMath.Rejection(gravity, desiredDirection));
double diffSq = accel * accel - gravityComp.LengthSquared();
if (diffSq < 0)  // Can't hover
    return desiredDirection - gravity;  // Sink but still approach target
return directionNorm * Math.Sqrt(diffSq) + gravityComp;
```

---

## 41. Heading Error Feedforward (Ship Rotation Compensation)

Without this, turrets lag when the ship rotates:
```csharp
double headingError = AngleBetween(currentFwd, lastFwd) * Sign(Dot(currentFwd, lastLeft));
double errorRate = headingError / dt * RadiansPerSecondToRPM;
rotor.TargetVelocityRPM = (float)(aimSpeed + errorRate);
```

---

## 42. Bates Distribution Random (Cheap Bell Curve)

```csharp
// Average 3 uniform randoms → pseudo-Gaussian, no Math.Log/Sqrt needed
double GaussRnd() => (rnd.NextDouble() + rnd.NextDouble() + rnd.NextDouble()) / 3.0;
```

Used for aim dispersion and fudge vectors on missed scans.

---

## Summary: Top 10 Most Dangerous Traps

| # | Trap | Consequence |
|---|------|-------------|
| 1 | Forward = -Z | Every yaw calculation silently wrong |
| 2 | Gyro axes inverted | Ship pitches/rolls wrong direction |
| 3 | Gyro override not per-gyro transformed | Misoriented gyros fight each other |
| 4 | ThrustOverride = 0 means "no override" | Dampeners take over unexpectedly |
| 5 | TimeSinceLastRun can be negative | NaN propagation through PID |
| 6 | acos without clamp → NaN | Silent script death |
| 7 | MatrixD.Left setter negates internally | TransformNormal gives wrong results |
| 8 | IsSameConstructAs vs CubeGrid == | Missing subgrid blocks |
| 9 | Property writes trigger network sync | Server performance death spiral |
| 10 | No line primitive in sprite API | Must fake with rotated rectangles |
