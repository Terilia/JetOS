using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class GunControlModule : ProgramModule
        {
            // --- Turret Assembly ---
            private class TurretAssembly
            {
                public IMyMotorStator Rotor;
                public IMyMotorStator Hinge;
                public IMySmallGatlingGun Gun;
                public string Name;
                public bool IsTracking;
                public Vector3D TargetPosition;
                public float YawError;
                public float PitchError;
                public int ElevationSign; // +1 or -1, derived from hinge mounting orientation
                // Ship rotation compensation (feedforward)
                public MatrixD LastShipMatrix;
                public bool HasPreviousMatrix;
                // Target LOS rate (D-term): derivative of aim direction
                public Vector3D LastAimDir;
                public bool HasLastAimDir;
            }

            // --- Turret References ---
            private TurretAssembly leftTurret;
            private TurretAssembly rightTurret;

            // References
            private Jet myJet;
            private IMyCockpit cockpit;

            // --- Control State ---
            private bool controlEnabled = false;

            // --- Constants ---
            private const float MAX_ANGLE_DEG = 15f;
            private const float MAX_ANGLE_RAD = MAX_ANGLE_DEG * (float)PI / 180f;
            private const int INTERCEPT_ITERATIONS = 6;
            // D-term gain: how aggressively we track target LOS rate.
            // 1.0 = full feedforward; tune up for fast-moving targets, down for jitter.
            private const float KD_LOS = 1.0f;

            // --- Configurable (read from config) ---
            private float KP => SystemManager.GetConfigValue("gun_kp");
            private float MAX_VELOCITY_RPM => SystemManager.GetConfigValue("gun_max_rpm");
            private float LOCK_THRESHOLD_DEG => SystemManager.GetConfigValue("gun_lock_threshold");
            private double MUZZLE_VELOCITY => SystemManager.GetConfigValue("gun_muzzle_velocity");
            private double MAX_ENGAGE_RANGE => SystemManager.GetConfigValue("gun_max_range");

            // --- Block Names ---
            private const string ROTOR_LEFT_NAME = "Gun Rotor Left";
            private const string HINGE_LEFT_NAME = "Gun Hinge Left";
            private const string ROTOR_RIGHT_NAME = "Gun Rotor Right";
            private const string HINGE_RIGHT_NAME = "Gun Hinge Right";

            public GunControlModule(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                cockpit = jet._cockpit;
                name = "Gun Control";

                leftTurret = new TurretAssembly { Name = "Left" };
                rightTurret = new TurretAssembly { Name = "Right" };

                FindTurretBlocks(program.GridTerminalSystem);
            }

            private void FindTurretBlocks(IMyGridTerminalSystem grid)
            {
                leftTurret.Rotor = grid.GetBlockWithName(ROTOR_LEFT_NAME) as IMyMotorStator;
                leftTurret.Hinge = grid.GetBlockWithName(HINGE_LEFT_NAME) as IMyMotorStator;
                rightTurret.Rotor = grid.GetBlockWithName(ROTOR_RIGHT_NAME) as IMyMotorStator;
                rightTurret.Hinge = grid.GetBlockWithName(HINGE_RIGHT_NAME) as IMyMotorStator;

                FindGunOnHinge(grid, leftTurret);
                FindGunOnHinge(grid, rightTurret);

                DetermineMotorSigns(leftTurret);
                DetermineMotorSigns(rightTurret);
            }

            private void FindGunOnHinge(IMyGridTerminalSystem grid, TurretAssembly turret)
            {
                if (turret.Hinge == null || turret.Hinge.TopGrid == null)
                    return;

                var guns = new List<IMySmallGatlingGun>();
                grid.GetBlocksOfType(guns, g => g.CubeGrid == turret.Hinge.TopGrid);
                if (guns.Count > 0)
                    turret.Gun = guns[0];
            }

            private void DetermineMotorSigns(TurretAssembly turret)
            {
                if (turret.Rotor == null || turret.Hinge == null || turret.Gun == null)
                {
                    turret.ElevationSign = 1;
                    return;
                }

                // Build base "left" axis from rotor Up and gun Forward
                Vector3D gunFwd = WF(turret.Gun);
                Vector3D rotorUp = WU(turret.Rotor);
                Vector3D baseLeft = VX(rotorUp, gunFwd);
                if (baseLeft.LengthSquared() < 1e-6)
                {
                    turret.ElevationSign = 1;
                    return;
                }
                baseLeft = VN(baseLeft);

                // elevationSign: which way the hinge's rotation axis relates to baseLeft
                turret.ElevationSign = Sg(VD(baseLeft, WU(turret.Hinge)));
                if (turret.ElevationSign == 0)
                    turret.ElevationSign = 1;
            }

            private static double SignedAngleBetween(Vector3D from, Vector3D to, Vector3D axis)
            {
                from = VN(from);
                to = VN(to);
                Vector3D cross = VX(from, to);
                double angle = At2(cross.Length(), VD(from, to));
                return angle * Sg(VD(cross, axis));
            }

            private static double GetElevationAngle(Vector3D direction, Vector3D rotorUp, Vector3D baseForward, Vector3D baseLeft)
            {
                // Project direction onto the elevation plane (perpendicular to baseLeft)
                Vector3D projected = direction - VD(direction, baseLeft) * baseLeft;
                if (projected.LengthSquared() < 1e-10)
                    return 0;
                projected = VN(projected);

                Vector3D projCross = VX(projected, baseForward);
                double angle = At2(projCross.Length(), VD(projected, baseForward));
                if (VD(projected, rotorUp) < 0)
                    angle = -angle;
                return angle;
            }

            public override string[] GetOptions()
            {
                string controlStatus = controlEnabled ? "ON" : "OFF";
                string leftStatus = GetTurretStatus(leftTurret);
                string rightStatus = GetTurretStatus(rightTurret);
                string leftLock = leftTurret.IsTracking ? "LOCKED" : "---";
                string rightLock = rightTurret.IsTracking ? "LOCKED" : "---";
                int totalAmmo = GetTotalAmmo();

                return new string[]
                {
                    $"Auto-Track [{controlStatus}]",
                    $"Ammo: {totalAmmo} rounds",
                    $"Left: {leftStatus} [{leftLock}]",
                    $"Right: {rightStatus} [{rightLock}]",
                    "Center Turrets"
                };
            }

            private string GetTurretStatus(TurretAssembly turret)
            {
                if (turret.Rotor == null || turret.Hinge == null)
                    return "MISSING";
                if (!turret.Rotor.IsFunctional || !turret.Hinge.IsFunctional)
                    return "DAMAGED";
                if (turret.Gun == null)
                    return "NO GUN";
                if (!turret.Gun.IsFunctional)
                    return "GUN DMG";
                return "OK";
            }

            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0:
                        ToggleControl();
                        break;
                    case 4:
                        CenterTurrets();
                        break;
                }
            }

            public override void HandleSpecialFunction(int key)
            {
                switch (key)
                {
                    case 5:
                        ToggleControl();
                        break;
                    case 6:
                        CenterTurrets();
                        break;
                }
            }

            public override string GetHotkeys()
            {
                return "5: Toggle Auto-Track\n6: Center Turrets";
            }

            private void ToggleControl()
            {
                controlEnabled = !controlEnabled;

                if (!controlEnabled)
                {
                    StopAllMotors();
                    leftTurret.IsTracking = false;
                    rightTurret.IsTracking = false;
                }
            }

            private void CenterTurrets()
            {
                DriveTowardDirection(leftTurret, WF(cockpit));
                DriveTowardDirection(rightTurret, WF(cockpit));
            }

            private void StopAllMotors()
            {
                if (leftTurret.Rotor != null) leftTurret.Rotor.TargetVelocityRPM = 0f;
                if (leftTurret.Hinge != null) leftTurret.Hinge.TargetVelocityRPM = 0f;
                if (rightTurret.Rotor != null) rightTurret.Rotor.TargetVelocityRPM = 0f;
                if (rightTurret.Hinge != null) rightTurret.Hinge.TargetVelocityRPM = 0f;
            }

            public override void Tick()
            {
                // Motor signs depend on static mounting geometry (hinge axis relative
                // to rotor axis) — these don't change during flight. Calculated once
                // in constructor; no periodic recalc needed.

                if (!controlEnabled)
                {
                    leftTurret.IsTracking = false;
                    rightTurret.IsTracking = false;

                    // Return turrets to cockpit forward when disabled
                    DriveTowardDirection(leftTurret, WF(cockpit));
                    DriveTowardDirection(rightTurret, WF(cockpit));
                    return;
                }

                var enemies = myJet.enemyList;
                TrackTarget(leftTurret, enemies);
                TrackTarget(rightTurret, enemies);
            }

            // Unified aiming: drives turret rotor/hinge to align gun with targetWorldDir.
            // Uses cross-product for yaw sign (correct for any rotor orientation)
            // and elevationSign for pitch (correct for any hinge mounting side).
            private void DriveTowardDirection(TurretAssembly turret, Vector3D targetWorldDir)
            {
                if (turret.Rotor == null || turret.Hinge == null || turret.Gun == null || cockpit == null)
                    return;

                Vector3D gunFwd = WF(turret.Gun);
                Vector3D rotorUp = WU(turret.Rotor);

                // --- Yaw: signed angle in the rotor's rotation plane ---
                // Project both gun forward and target direction onto the plane perpendicular to rotorUp
                Vector3D flatGun = gunFwd - VD(gunFwd, rotorUp) * rotorUp;
                Vector3D flatTarget = targetWorldDir - VD(targetWorldDir, rotorUp) * rotorUp;

                double yawRad = SignedAngleBetween(flatGun, flatTarget, rotorUp);
                float yawDeg = (float)ToDeg(yawRad);

                // --- Pitch: elevation angle difference with mounting-aware sign ---
                Vector3D baseLeft = VX(rotorUp, gunFwd);
                if (baseLeft.LengthSquared() < 1e-6)
                {
                    // Gun pointing along rotor axis — can't determine yaw, just stop
                    turret.Rotor.TargetVelocityRPM = 0f;
                    turret.Hinge.TargetVelocityRPM = 0f;
                    return;
                }
                baseLeft = VN(baseLeft);
                Vector3D baseForward = VN(VX(baseLeft, rotorUp));

                double desiredPitch = GetElevationAngle(targetWorldDir, rotorUp, baseForward, baseLeft);
                double currentPitch = GetElevationAngle(gunFwd, rotorUp, baseForward, baseLeft);
                float pitchDeg = (float)ToDeg((desiredPitch - currentPitch) * turret.ElevationSign);

                // Ship rotation feedforward using cockpit matrix (ship-only rotation,
                // avoids self-coupling from turret's own yaw included in rotor matrix).
                // rad/s → RPM: RPM = rad/s * 60 / (2π)
                float yawFeedforward = 0f;
                float pitchFeedforward = 0f;
                MatrixD currentShipMatrix = WM(cockpit);
                double dt = SystemManager.DeltaSeconds;
                double radPerSecToRpm = 60.0 / (2.0 * PI);
                if (turret.HasPreviousMatrix && dt > 0)
                {
                    Vector3D lastFwd = turret.LastShipMatrix.Forward;
                    Vector3D lastUp = turret.LastShipMatrix.Up;
                    Vector3D lastLeft = turret.LastShipMatrix.Left;
                    Vector3D curFwd = currentShipMatrix.Forward;

                    // Yaw drift: project current forward onto last frame's horizontal plane
                    Vector3D flatCurFwd = curFwd - VD(curFwd, lastUp) * lastUp;
                    if (flatCurFwd.LengthSquared() > 1e-10)
                    {
                        flatCurFwd = VN(flatCurFwd);
                        Vector3D driftCross = VX(flatCurFwd, lastFwd);
                        double driftAngle = At2(driftCross.Length(), VD(flatCurFwd, lastFwd));
                        driftAngle *= Sg(VD(driftCross, lastUp));
                        yawFeedforward = (float)(driftAngle / dt * radPerSecToRpm);
                    }

                    // Pitch drift: similar for elevation axis
                    Vector3D flatCurFwdElev = curFwd - VD(curFwd, lastLeft) * lastLeft;
                    if (flatCurFwdElev.LengthSquared() > 1e-10)
                    {
                        flatCurFwdElev = VN(flatCurFwdElev);
                        Vector3D elevCross = VX(flatCurFwdElev, lastFwd);
                        double elevAngle = At2(elevCross.Length(), VD(flatCurFwdElev, lastFwd));
                        elevAngle *= Sg(VD(elevCross, lastLeft));
                        pitchFeedforward = (float)(elevAngle / dt * turret.ElevationSign * radPerSecToRpm);
                    }
                }
                turret.LastShipMatrix = currentShipMatrix;
                turret.HasPreviousMatrix = true;

                // Target LOS rate D-term: differentiate aim direction itself.
                // This captures target lateral motion that the ship-rotation feedforward misses.
                float yawLosRate = 0f;
                float pitchLosRate = 0f;
                if (turret.HasLastAimDir && dt > 0)
                {
                    Vector3D lastAim = turret.LastAimDir;
                    // Yaw component: angular rate around rotorUp
                    Vector3D flatLastAim = lastAim - VD(lastAim, rotorUp) * rotorUp;
                    Vector3D flatCurAim = targetWorldDir - VD(targetWorldDir, rotorUp) * rotorUp;
                    if (flatLastAim.LengthSquared() > 1e-10 && flatCurAim.LengthSquared() > 1e-10)
                    {
                        double losRate = SignedAngleBetween(flatLastAim, flatCurAim, rotorUp) / dt;
                        yawLosRate = (float)(-losRate * radPerSecToRpm) * KD_LOS;
                    }
                    // Pitch component: elevation rate
                    double lastPitch = GetElevationAngle(lastAim, rotorUp, baseForward, baseLeft);
                    double curPitch = GetElevationAngle(targetWorldDir, rotorUp, baseForward, baseLeft);
                    double pitchRate = (curPitch - lastPitch) / dt;
                    pitchLosRate = (float)(pitchRate * turret.ElevationSign * radPerSecToRpm) * KD_LOS;
                }
                turret.LastAimDir = targetWorldDir;
                turret.HasLastAimDir = true;

                // Compute final commands: P (error) + ship-rotation FF + target-LOS D
                float yawCmd = Cl(-KP * yawDeg + yawFeedforward + yawLosRate, -MAX_VELOCITY_RPM, MAX_VELOCITY_RPM);
                float pitchCmd = Cl(KP * pitchDeg + pitchFeedforward + pitchLosRate, -MAX_VELOCITY_RPM, MAX_VELOCITY_RPM);

                // Deadband: zero out when close enough to prevent jitter
                if (Ab(yawDeg) < 0.5f && Ab(pitchDeg) < 0.5f
                    && Ab(yawFeedforward) < 0.5f && Ab(pitchFeedforward) < 0.5f
                    && Ab(yawLosRate) < 0.5f && Ab(pitchLosRate) < 0.5f)
                {
                    yawCmd = 0f;
                    pitchCmd = 0f;
                }

                // Conditional writes: only set RPM if value actually changed (avoids network sync)
                if (Ab(turret.Rotor.TargetVelocityRPM - yawCmd) > 0.01f)
                    turret.Rotor.TargetVelocityRPM = yawCmd;
                if (Ab(turret.Hinge.TargetVelocityRPM - pitchCmd) > 0.01f)
                    turret.Hinge.TargetVelocityRPM = pitchCmd;

                turret.YawError = Ab(yawDeg);
                turret.PitchError = Ab(pitchDeg);
            }

            private void TrackTarget(TurretAssembly turret, List<Jet.EnemyContact> enemies)
            {
                turret.IsTracking = false;

                if (turret.Rotor == null || turret.Hinge == null || turret.Gun == null)
                    return;

                if (cockpit == null)
                    return;

                Vector3D gunPosition = GP(turret.Gun);
                Vector3D shipForward = WF(cockpit);

                Vector3D shooterVelocity = LV(cockpit);
                Vector3D gravity = myJet.CachedGravity;

                // Find closest enemy within cone of ship's forward (fixed cone, not gun's moving forward)
                Vector3D? bestTargetPos = null;
                Vector3D bestTargetVel = VZ;
                Vector3D bestTargetAccel = VZ;
                double bestDistance = double.MaxValue;

                for (int i = 0; i < enemies.Count; i++)
                {
                    var enemy = enemies[i];
                    Vector3D toTarget = enemy.Position - gunPosition;
                    double distance = toTarget.Length();

                    if (distance < 10) continue;

                    Vector3D toTargetNorm = toTarget / distance;
                    double angleRad = At2(VX(shipForward, toTargetNorm).Length(), VD(shipForward, toTargetNorm));

                    if (angleRad <= MAX_ANGLE_RAD && distance <= MAX_ENGAGE_RANGE && distance < bestDistance)
                    {
                        bestTargetPos = enemy.Position;
                        bestTargetVel = enemy.Velocity;
                        bestTargetAccel = enemy.Acceleration;
                        bestDistance = distance;
                    }
                }

                if (!bestTargetPos.HasValue)
                {
                    // No target — return to ship forward
                    DriveTowardDirection(turret, WF(cockpit));
                    return;
                }

                // Spawn-delay compensation: one dt of relative motion between computing
                // the lead and the bullet actually spawning. Matches HUD lead pip.
                Vector3D spawnAdjustedTargetPos = bestTargetPos.Value
                    + (bestTargetVel - shooterVelocity) * SystemManager.DeltaSeconds;
                turret.TargetPosition = spawnAdjustedTargetPos;

                // Lead prediction
                Vector3D aimPoint;
                Vector3D interceptPoint;
                double timeToIntercept;

                bool hasIntercept = BallisticsCalculator.CalculateInterceptPoint(
                    gunPosition, shooterVelocity, MUZZLE_VELOCITY,
                    spawnAdjustedTargetPos, bestTargetVel,
                    INTERCEPT_ITERATIONS,
                    out interceptPoint, out timeToIntercept, out aimPoint,
                    bestTargetAccel);

                if (!hasIntercept)
                    aimPoint = spawnAdjustedTargetPos;

                // Drive toward computed aim direction
                Vector3D aimDir = VN(aimPoint - gunPosition);
                DriveTowardDirection(turret, aimDir);

                if (turret.YawError < LOCK_THRESHOLD_DEG && turret.PitchError < LOCK_THRESHOLD_DEG)
                {
                    turret.IsTracking = true;
                }
            }

            // Ammo display cache — inventory iteration every tick is wasteful.
            private int _cachedAmmo = 0;
            private double _ammoCacheAge = double.MaxValue;
            private const double AMMO_CACHE_REFRESH_SECONDS = 0.5;

            private int GetTotalAmmo()
            {
                _ammoCacheAge += SystemManager.DeltaSeconds;
                if (_ammoCacheAge < AMMO_CACHE_REFRESH_SECONDS)
                    return _cachedAmmo;

                int total = 0;
                total += GetGunAmmo(leftTurret.Gun);
                total += GetGunAmmo(rightTurret.Gun);
                _cachedAmmo = total;
                _ammoCacheAge = 0;
                return total;
            }

            private static int GetGunAmmo(IMySmallGatlingGun gun)
            {
                return Jet.GetGunAmmo(gun);
            }

            // Public getters for HUD integration
            public bool IsControlEnabled => controlEnabled;
            public bool IsLeftTracking => leftTurret.IsTracking;
            public bool IsRightTracking => rightTurret.IsTracking;
        }
    }
}
