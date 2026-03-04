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
            private const float MAX_ANGLE_RAD = MAX_ANGLE_DEG * (float)Math.PI / 180f;
            private const int INTERCEPT_ITERATIONS = 10;

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
                Vector3D gunFwd = turret.Gun.WorldMatrix.Forward;
                Vector3D rotorUp = turret.Rotor.WorldMatrix.Up;
                Vector3D baseLeft = Vector3D.Cross(rotorUp, gunFwd);
                if (baseLeft.LengthSquared() < 1e-6)
                {
                    turret.ElevationSign = 1;
                    return;
                }
                baseLeft = Vector3D.Normalize(baseLeft);

                // elevationSign: which way the hinge's rotation axis relates to baseLeft
                turret.ElevationSign = Math.Sign(Vector3D.Dot(baseLeft, turret.Hinge.WorldMatrix.Up));
                if (turret.ElevationSign == 0)
                    turret.ElevationSign = 1;
            }

            private static double SignedAngleBetween(Vector3D from, Vector3D to, Vector3D axis)
            {
                from = Vector3D.Normalize(from);
                to = Vector3D.Normalize(to);
                double dot = MathHelper.Clamp(Vector3D.Dot(from, to), -1, 1);
                double angle = Math.Acos(dot);
                Vector3D cross = Vector3D.Cross(from, to);
                return angle * Math.Sign(Vector3D.Dot(cross, axis));
            }

            private static double GetElevationAngle(Vector3D direction, Vector3D rotorUp, Vector3D baseForward, Vector3D baseLeft)
            {
                // Project direction onto the elevation plane (perpendicular to baseLeft)
                Vector3D projected = direction - Vector3D.Dot(direction, baseLeft) * baseLeft;
                if (projected.LengthSquared() < 1e-10)
                    return 0;
                projected = Vector3D.Normalize(projected);

                double dot = MathHelper.Clamp(Vector3D.Dot(projected, baseForward), -1, 1);
                double angle = Math.Acos(dot);
                if (Vector3D.Dot(projected, rotorUp) < 0)
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
                DriveTowardDirection(leftTurret, cockpit.WorldMatrix.Forward);
                DriveTowardDirection(rightTurret, cockpit.WorldMatrix.Forward);
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
                currentTick++;

                // Recalculate motor signs periodically (handles rotor movement changing geometry)
                if (currentTick % 60 == 0)
                {
                    DetermineMotorSigns(leftTurret);
                    DetermineMotorSigns(rightTurret);
                }

                if (!controlEnabled)
                {
                    leftTurret.IsTracking = false;
                    rightTurret.IsTracking = false;

                    // Return turrets to cockpit forward when disabled
                    DriveTowardDirection(leftTurret, cockpit.WorldMatrix.Forward);
                    DriveTowardDirection(rightTurret, cockpit.WorldMatrix.Forward);
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

                Vector3D gunFwd = turret.Gun.WorldMatrix.Forward;
                Vector3D rotorUp = turret.Rotor.WorldMatrix.Up;

                // --- Yaw: signed angle in the rotor's rotation plane ---
                // Project both gun forward and target direction onto the plane perpendicular to rotorUp
                Vector3D flatGun = gunFwd - Vector3D.Dot(gunFwd, rotorUp) * rotorUp;
                Vector3D flatTarget = targetWorldDir - Vector3D.Dot(targetWorldDir, rotorUp) * rotorUp;

                double yawRad = SignedAngleBetween(flatGun, flatTarget, rotorUp);
                float yawDeg = MathHelper.ToDegrees((float)yawRad);

                // --- Pitch: elevation angle difference with mounting-aware sign ---
                Vector3D baseLeft = Vector3D.Cross(rotorUp, gunFwd);
                if (baseLeft.LengthSquared() < 1e-6)
                {
                    // Gun pointing along rotor axis — can't determine yaw, just stop
                    turret.Rotor.TargetVelocityRPM = 0f;
                    turret.Hinge.TargetVelocityRPM = 0f;
                    return;
                }
                baseLeft = Vector3D.Normalize(baseLeft);
                Vector3D baseForward = Vector3D.Normalize(Vector3D.Cross(baseLeft, rotorUp));

                double desiredPitch = GetElevationAngle(targetWorldDir, rotorUp, baseForward, baseLeft);
                double currentPitch = GetElevationAngle(gunFwd, rotorUp, baseForward, baseLeft);
                float pitchDeg = MathHelper.ToDegrees((float)((desiredPitch - currentPitch) * turret.ElevationSign));

                // Yaw: negate because SE positive RPM = counterclockwise from above (leftward),
                // but SignedAngleBetween gives positive for clockwise (rightward) rotation.
                // Pitch: sign is correct via elevationSign (same formula as Whiplash).
                turret.Rotor.TargetVelocityRPM = MathHelper.Clamp(-KP * yawDeg, -MAX_VELOCITY_RPM, MAX_VELOCITY_RPM);
                turret.Hinge.TargetVelocityRPM = MathHelper.Clamp(KP * pitchDeg, -MAX_VELOCITY_RPM, MAX_VELOCITY_RPM);

                // Stop motors when close enough to prevent jitter
                if (Math.Abs(yawDeg) < 0.5f && Math.Abs(pitchDeg) < 0.5f)
                {
                    turret.Rotor.TargetVelocityRPM = 0f;
                    turret.Hinge.TargetVelocityRPM = 0f;
                }

                turret.YawError = Math.Abs(yawDeg);
                turret.PitchError = Math.Abs(pitchDeg);
            }

            private void TrackTarget(TurretAssembly turret, List<Jet.EnemyContact> enemies)
            {
                turret.IsTracking = false;

                if (turret.Rotor == null || turret.Hinge == null || turret.Gun == null)
                    return;

                if (cockpit == null)
                    return;

                Vector3D gunPosition = turret.Gun.GetPosition();
                Vector3D shipForward = cockpit.WorldMatrix.Forward;

                Vector3D shooterVelocity = cockpit.GetShipVelocities().LinearVelocity;
                Vector3D gravity = cockpit.GetNaturalGravity();

                // Find closest enemy within cone of ship's forward (fixed cone, not gun's moving forward)
                Vector3D? bestTargetPos = null;
                Vector3D bestTargetVel = Vector3D.Zero;
                Vector3D bestTargetAccel = Vector3D.Zero;
                double bestDistance = double.MaxValue;

                for (int i = 0; i < enemies.Count; i++)
                {
                    var enemy = enemies[i];
                    Vector3D toTarget = enemy.Position - gunPosition;
                    double distance = toTarget.Length();

                    if (distance < 10) continue;

                    Vector3D toTargetNorm = toTarget / distance;
                    double dotProduct = Vector3D.Dot(shipForward, toTargetNorm);
                    double angleRad = Math.Acos(MathHelper.Clamp(dotProduct, -1.0, 1.0));

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
                    DriveTowardDirection(turret, cockpit.WorldMatrix.Forward);
                    return;
                }

                turret.TargetPosition = bestTargetPos.Value;

                // Lead prediction
                Vector3D aimPoint;
                Vector3D interceptPoint;
                double timeToIntercept;

                bool hasIntercept = BallisticsCalculator.CalculateInterceptPoint(
                    gunPosition, shooterVelocity, MUZZLE_VELOCITY,
                    bestTargetPos.Value, bestTargetVel,
                    INTERCEPT_ITERATIONS,
                    out interceptPoint, out timeToIntercept, out aimPoint,
                    bestTargetAccel);

                if (!hasIntercept)
                    aimPoint = bestTargetPos.Value;

                // Drive toward computed aim direction
                Vector3D aimDir = Vector3D.Normalize(aimPoint - gunPosition);
                DriveTowardDirection(turret, aimDir);

                if (turret.YawError < LOCK_THRESHOLD_DEG && turret.PitchError < LOCK_THRESHOLD_DEG)
                {
                    turret.IsTracking = true;
                }
            }

            private int GetTotalAmmo()
            {
                int total = 0;
                total += GetGunAmmo(leftTurret.Gun);
                total += GetGunAmmo(rightTurret.Gun);
                return total;
            }

            private int GetGunAmmo(IMySmallGatlingGun gun)
            {
                if (gun == null || !gun.IsFunctional)
                    return 0;

                var inventory = gun.GetInventory();
                if (inventory == null)
                    return 0;

                int ammo = 0;
                for (int i = 0; i < inventory.ItemCount; i++)
                {
                    var item = inventory.GetItemAt(i);
                    if (item.HasValue)
                    {
                        ammo += (int)item.Value.Amount;
                    }
                }
                return ammo;
            }

            // Public getters for HUD integration
            public bool IsControlEnabled => controlEnabled;
            public bool IsLeftTracking => leftTurret.IsTracking;
            public bool IsRightTracking => rightTurret.IsTracking;
            public bool IsLeftCalibrating => false;
            public bool IsRightCalibrating => false;
        }
    }
}
