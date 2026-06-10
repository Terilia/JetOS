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
        // Turret auto-aiming logic. No longer a menu module — owned + ticked by
        // ConfigurationModule; on/off is the CFG_GUN_AUTO config toggle, tuning lives in
        // the config "Gun" category, and the weapon-screen turret indicators read its getters.
        class GunControlModule
        {
            // --- Turret Assembly ---
            private class TurretAssembly
            {
                public IMyMotorStator Rotor;
                public IMyMotorStator Hinge;
                public IMySmallGatlingGun Gun;
                public string Name;
                public bool IsTracking;
                public float YawError;
                public float PitchError;
                public int ElevationSign; // +1 or -1, derived from hinge mounting orientation
                // Ship rotation compensation (feedforward)
                public MatrixD LastShipMatrix;
                public bool HasPreviousMatrix;
                // Target LOS rate (D-term): derivative of aim direction
                public Vector3D LastAimDir;
                public bool HasLastAimDir;
                // Aim source bookkeeping — the D-term must not differentiate across a
                // source change (target switch / target↔forward), or it kicks for one tick.
                public int AimSource;
                public long AimTargetId;
            }

            // --- Turret References ---
            private TurretAssembly leftTurret;
            private TurretAssembly rightTurret;

            // References
            private Jet myJet;
            private IMyCockpit cockpit;

            // --- Constants ---
            private const float MAX_ANGLE_DEG = 15f;
            private const float MAX_ANGLE_RAD = MAX_ANGLE_DEG * (float)PI / 180f;
            // D-term gain: how aggressively we track target LOS rate.
            // 1.0 = full feedforward; tune up for fast-moving targets, down for jitter.
            private const float KD_LOS = 1.0f;

            // --- Configurable (read from config) ---
            private float KP => SystemManager.GetConfigValue(CFG_GUN_KP);
            private float MAX_VELOCITY_RPM => SystemManager.GetConfigValue(CFG_GUN_MAX_RPM);
            private float LOCK_THRESHOLD_DEG => SystemManager.GetConfigValue(CFG_GUN_LOCK_THRESHOLD);
            private double MUZZLE_VELOCITY => SystemManager.GetConfigValue(CFG_GUN_MUZZLE_VELOCITY);
            private double MAX_ENGAGE_RANGE => SystemManager.GetConfigValue(CFG_GUN_MAX_RANGE);

            // --- Block Names ---
            private const string ROTOR_LEFT_NAME = "Gun Rotor Left";
            private const string HINGE_LEFT_NAME = "Gun Hinge Left";
            private const string ROTOR_RIGHT_NAME = "Gun Rotor Right";
            private const string HINGE_RIGHT_NAME = "Gun Hinge Right";

            public GunControlModule(Program program, Jet jet)
            {
                myJet = jet;
                cockpit = jet._cockpit;

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

            private static float RotationFeedforward(Vector3D curFwd, Vector3D lastFwd, Vector3D axis, double dt, double scale)
            {
                Vector3D flat = curFwd - VD(curFwd, axis) * axis;
                if (flat.LengthSquared() <= 1e-10) return 0f;
                flat = VN(flat);
                Vector3D cross = VX(flat, lastFwd);
                double angle = At2(cross.Length(), VD(flat, lastFwd));
                angle *= Sg(VD(cross, axis));
                return (float)(angle / dt * scale);
            }

            public void Tick()
            {
                // Motor signs depend on static mounting geometry (hinge axis relative
                // to rotor axis) — these don't change during flight. Calculated once
                // in constructor; no periodic recalc needed.

                if (SystemManager.GetConfigValue(CFG_GUN_AUTO) < 0.5f)
                {
                    leftTurret.IsTracking = false;
                    rightTurret.IsTracking = false;

                    // Return turrets to cockpit forward when disabled
                    DriveForward(leftTurret);
                    DriveForward(rightTurret);
                    return;
                }

                var selected = myJet.GetSelectedEnemy();

                // Lead solution is ship-level — solve once per tick, not per turret.
                // Per-gun parallax is preserved in TrackTarget via aimDir from gunPosition.
                bool haveSolution = false;
                Vector3D aimPoint = VZ;
                if (selected.HasValue && cockpit != null)
                {
                    var enemy = selected.Value;
                    Vector3D shooterVelocity = myJet.CockpitVelocity;
                    // Spawn-delay compensation: one dt of relative motion between computing
                    // the lead and the bullet actually spawning. Matches HUD lead pip.
                    Vector3D spawnAdjusted = enemy.Position
                        + (enemy.Velocity - shooterVelocity) * SystemManager.DeltaSeconds;
                    Vector3D interceptPoint;
                    double timeToIntercept;
                    if (!BallisticsCalculator.CalculateInterceptPoint(
                        myJet.CockpitPosition, shooterVelocity, MUZZLE_VELOCITY,
                        spawnAdjusted, enemy.Velocity, myJet.CachedGravity,
                        out interceptPoint, out timeToIntercept, out aimPoint))
                        aimPoint = spawnAdjusted;
                    haveSolution = true;
                }

                TrackTarget(leftTurret, selected, haveSolution, aimPoint);
                TrackTarget(rightTurret, selected, haveSolution, aimPoint);
            }

            // Forward-drive with aim-source bookkeeping (and cockpit-null safety).
            void DriveForward(TurretAssembly turret)
            {
                if (cockpit == null) return;
                Retarget(turret, 1, 0);
                DriveTowardDirection(turret, WF(cockpit));
            }

            static void Retarget(TurretAssembly t, int source, long id)
            {
                if (t.AimSource != source || t.AimTargetId != id)
                {
                    t.HasLastAimDir = false;
                    t.AimSource = source;
                    t.AimTargetId = id;
                }
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

                    yawFeedforward = RotationFeedforward(curFwd, lastFwd, lastUp, dt, radPerSecToRpm);
                    pitchFeedforward = RotationFeedforward(curFwd, lastFwd, lastLeft, dt, turret.ElevationSign * radPerSecToRpm);
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

            private void TrackTarget(TurretAssembly turret, Jet.EnemyContact? selected, bool haveSolution, Vector3D aimPoint)
            {
                turret.IsTracking = false;

                if (turret.Rotor == null || turret.Hinge == null || turret.Gun == null)
                    return;

                if (cockpit == null)
                    return;

                if (!haveSolution)
                {
                    DriveForward(turret);
                    return;
                }

                var enemy = selected.Value;
                Vector3D gunPosition = GP(turret.Gun);
                Vector3D shipForward = WF(cockpit);
                Vector3D toTarget = enemy.Position - gunPosition;
                double distance = toTarget.Length();

                if (distance < 10)
                {
                    DriveForward(turret);
                    return;
                }

                Vector3D toTargetNorm = toTarget / distance;
                double angleRad = At2(VX(shipForward, toTargetNorm).Length(), VD(shipForward, toTargetNorm));

                if (angleRad > MAX_ANGLE_RAD || distance > MAX_ENGAGE_RANGE)
                {
                    DriveForward(turret);
                    return;
                }

                Retarget(turret, 2, enemy.EntityId);

                // Drive toward the shared aim point, with this gun's parallax
                Vector3D aimDir = VN(aimPoint - gunPosition);
                DriveTowardDirection(turret, aimDir);

                if (turret.YawError < LOCK_THRESHOLD_DEG && turret.PitchError < LOCK_THRESHOLD_DEG)
                {
                    turret.IsTracking = true;
                }
            }

            // Public getters for HUD integration (weapon-screen turret indicators)
            public bool IsControlEnabled => SystemManager.GetConfigValue(CFG_GUN_AUTO) > 0.5f;
            public bool IsLeftTracking => leftTurret.IsTracking;
            public bool IsRightTracking => rightTurret.IsTracking;
        }
    }
}
