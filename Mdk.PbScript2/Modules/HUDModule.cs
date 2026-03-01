using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        partial class HUDModule : ProgramModule
        {
            // --- HUD Color Palette ---
            internal static readonly Color HUD_PRIMARY = Color.Lime;
            internal static readonly Color HUD_SECONDARY = Color.Green;
            internal static readonly Color HUD_HORIZON = Color.LimeGreen;
            internal static readonly Color HUD_EMPHASIS = Color.Yellow;
            internal static readonly Color HUD_WARNING = Color.Red;
            internal static readonly Color HUD_INFO = Color.White;
            internal static readonly Color HUD_RADAR_FRIENDLY = Color.DarkGreen;

            // --- Layout Constants ---
            private const float SPEED_TAPE_CENTER_Y_FACTOR = 2.25f;
            private const float INFO_BOX_Y_OFFSET_FACTOR = 1.85f;
            private const float ALTITUDE_TAPE_CENTER_Y_FACTOR = 2.0f;
            internal const float THROTTLE_HYDROGEN_THRESHOLD = 0.8f;

            // --- Physics Constants ---
            private const double SEA_LEVEL_SPEED_OF_SOUND = 343.0;
            private const double GRAVITY_ACCELERATION = 9.81;

            // --- Smoothing Configuration ---
            private const int SMOOTHING_WINDOW_SIZE = 10;

            // --- Component References ---
            internal IMyCockpit cockpit;
            internal IMyTextSurface hud;
            internal IMyTextSurface weaponScreen;
            internal IMyTerminalBlock hudBlock;
            internal List<IMyTerminalBlock> leftstab = new List<IMyTerminalBlock>();
            internal List<IMyTerminalBlock> rightstab = new List<IMyTerminalBlock>();
            private List<IMyThrust> thrusters = new List<IMyThrust>();
            internal List<IMyGasTank> tanks = new List<IMyGasTank>();
            private List<IMyDoor> airbreaks = new List<IMyDoor>();
            internal Jet myjet;
            internal RadarControlModule radarControl;

            // --- Flight Data ---
            internal double peakGForce = 0;
            internal float currentTrim;
            internal double pitch = 0;
            internal double roll = 0;
            internal double velocity;
            internal double deltaTime;
            internal double mach;
            internal Vector3D previousVelocity = Vector3D.Zero;

            // --- Smoothed Values with Running Averages ---
            private CircularBuffer<double> velocityHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);
            internal CircularBuffer<AltitudeTimePoint> altitudeHistory = new CircularBuffer<AltitudeTimePoint>(SMOOTHING_WINDOW_SIZE);
            private CircularBuffer<double> gForcesHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);
            private CircularBuffer<double> aoaHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);

            internal double smoothedVelocity = 0;
            internal double smoothedAltitude = 0;
            internal double smoothedGForces = 0;
            internal double smoothedAoA = 0;
            internal double smoothedThrottle = 0;

            // Running sums for efficient smoothing
            private double velocitySum = 0;
            private double altitudeSum = 0;
            private double gForcesSum = 0;
            private double aoaSum = 0;

            // --- Throttle Control ---
            internal float throttlecontrol = 0f;
            internal bool hydrogenswitch = false;
            private const float THROTTLE_RATE = 0.6f;
            private const float HYDROGEN_HYSTERESIS = 0.02f;

            // --- Airbrake State ---
            private bool airbrakesOpen = false;

            // --- Manual Fire Toggle ---
            private bool manualFireToggleCooldown = false;

            // --- UI State ---
            internal TimeSpan totalElapsedTime = TimeSpan.Zero;

            // --- Cached HUD Values ---
            internal Vector2 hudCenter;
            internal float viewportMinDim;

            // --- Missile tracking ---
            internal struct MissileTrackingData
            {
                public int BayIndex;
                public TimeSpan LaunchTime;
                public double EstimatedTOF;
                public Vector3D TargetPosition;
            }
            internal List<MissileTrackingData> activeMissiles = new List<MissileTrackingData>();

            // HUD Mode System
            private enum HUDMode { AirToAir, AirToGround, Navigation }
            private HUDMode currentHUDMode = HUDMode.AirToAir;

            // Radar sweep animation
            internal int radarSweepTick = 0;

            // Fuel state tracking
            internal const double BINGO_FUEL_PERCENT = 0.20;
            internal const double LOW_FUEL_PERCENT = 0.35;

            // Pre-allocated radar target array
            private const int MAX_RADAR_TARGETS = 10;
            private Vector3D[] radarTargetBuffer = new Vector3D[MAX_RADAR_TARGETS];
            private int radarTargetCount = 0;

            // --- Shared Constants for renderers ---
            internal const int INTERCEPT_ITERATIONS = 10;
            internal const double MIN_Z_FOR_PROJECTION = 0.1;
            internal const string TEXTURE_SQUARE = "SquareSimple";
            internal const string TEXTURE_CIRCLE = "CircleHollow";
            internal const string TEXTURE_TRIANGLE = "Triangle";
            internal const float RADAR_RANGE_METERS = 15000f;
            internal const float RADAR_BOX_SIZE_PX = 100f;
            internal const float RADAR_BORDER_MARGIN = 10f;

            // --- Stall warning thresholds ---
            internal const double STALL_AOA = 28.0;
            internal const double STALL_CAUTION_PERCENT = 0.80;
            internal const double STALL_WARNING_PERCENT = 0.90;
            internal bool stallWarningActive = false;
            internal const int STALL_LEVEL_NORMAL = 0;
            internal const int STALL_LEVEL_CAUTION = 1;
            internal const int STALL_LEVEL_WARNING = 2;
            internal const int STALL_LEVEL_STALL = 3;

            // --- Tape constants ---
            internal TimeSpan historyDuration = TimeSpan.FromSeconds(1);
            internal const float TAPE_HEIGHT_PIXELS = 200f;
            internal const float ALTITUDE_UNITS_PER_TAPE_HEIGHT = 1000f;
            internal const float PIXELS_PER_ALTITUDE_UNIT = TAPE_HEIGHT_PIXELS / ALTITUDE_UNITS_PER_TAPE_HEIGHT;
            internal const float TICK_INTERVAL = 100f;
            internal const float MAJOR_TICK_INTERVAL = 500f;
            internal const string FONT = "Monospace";
            internal const float SPEED_MAJOR_TICK_INTERVAL = 50f;
            internal const float SPEED_TICK_INTERVAL = 25f;
            internal const float SPEED_KPH_UNITS_PER_TAPE_HEIGHT = 600f;

            // --- FOV projection constants ---
            internal const float COCKPIT_FOV_SCALE_X = 0.3434f;
            internal const float COCKPIT_FOV_SCALE_Y = 0.31f;

            // --- Structs ---
            internal struct LabelValue
            {
                public string Label;
                public double Value;

                public LabelValue(string label, double value)
                {
                    Label = label;
                    Value = value;
                }
            }

            internal struct AltitudeTimePoint
            {
                public readonly TimeSpan Time;
                public readonly double Altitude;

                public AltitudeTimePoint(TimeSpan time, double altitude)
                {
                    Time = time;
                    Altitude = altitude;
                }
            }

            // =============================================
            // CONSTRUCTOR
            // =============================================

            public HUDModule(Program program, Jet jet, IMyTextSurface weaponSurface, RadarControlModule radar) : base(program)
            {
                cockpit = jet._cockpit;
                hudBlock = jet.hudBlock;
                hud = jet.hud;
                weaponScreen = weaponSurface;
                radarControl = radar;

                rightstab = jet.rightstab;
                leftstab = jet.leftstab;

                thrusters = jet._thrustersbackwards;
                tanks = jet.tanks;
                // Disable hydrogen tanks on startup
                for (int i = 0; i < tanks.Count; i++)
                {
                    if (tanks[i] != null)
                    {
                        tanks[i].Enabled = false;
                    }
                }
                myjet = jet;

                if (hudBlock == null)
                {
                    return;
                }

                hud = hudBlock as IMyTextSurface;
                if (hud == null)
                {
                    var surfaceProvider = hudBlock as IMyTextSurfaceProvider;
                    if (surfaceProvider != null)
                    {
                        hud = surfaceProvider.GetSurface(0);
                    }
                }

                if (hud == null)
                {
                    return;
                }

                hud.ContentType = ContentType.SCRIPT;
                hud.ScriptBackgroundColor = new Color(0, 0, 0, 0);
                hud.ScriptForegroundColor = Color.White;

                ParentProgram.GridTerminalSystem.GetBlocksOfType(airbreaks);
                name = "HUD Control";
            }

            // =============================================
            // MODULE INTERFACE
            // =============================================

            public override string GetHotkeys()
            {
                string modeText = currentHUDMode == HUDMode.AirToAir ? "A/A" :
                                 currentHUDMode == HUDMode.AirToGround ? "A/G" : "NAV";
                return $"5: HUD Mode [{modeText}]";
            }

            public override void HandleSpecialFunction(int functionNumber)
            {
                if (functionNumber == 5)
                {
                    currentHUDMode = (HUDMode)(((int)currentHUDMode + 1) % 3);
                }
            }

            public override string[] GetOptions() => new string[] { "Back to Main Menu" };
            public override void ExecuteOption(int index) { if (index == 0) SystemManager.ReturnToMainMenu(); }

            // =============================================
            // TICK - MAIN UPDATE LOOP
            // =============================================

            public override void Tick()
            {
                if (!ValidateHUDState())
                    return;

                double throttle = cockpit.MoveIndicator.Z * -1;
                double jumpthrottle = cockpit.MoveIndicator.Y;

                MatrixD worldMatrix;
                Vector3D forwardVector, upVector, gravity, gravityDirection;
                bool inGravity;
                UpdateFlightData(out worldMatrix, out forwardVector, out upVector,
                    out gravity, out inGravity, out gravityDirection);

                UpdateThrottleControl(throttle, jumpthrottle);

                double heading = CalculateHeading();
                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;
                RenderHUD(heading, gravity, gravityDirection, currentVelocity, worldMatrix);
            }

            // =============================================
            // HUD RENDERING - ORCHESTRATOR
            // =============================================

            private void RenderHUD(double heading, Vector3D gravity, Vector3D gravityDirection, Vector3D currentVelocity, MatrixD worldMatrix)
            {
                hudCenter = hud.SurfaceSize / 2f;
                viewportMinDim = Math.Min(hud.SurfaceSize.X, hud.SurfaceSize.Y);

                float centerX = hudCenter.X;
                float centerY = hudCenter.Y;
                float pixelsPerDegree = hud.SurfaceSize.Y / 16f;

                Vector3D shooterPosition = cockpit.GetPosition();
                double altitude = GetAltitude();

                using (var frame = hud.DrawFrame())
                {
                    // Horizon & attitude
                    DrawArtificialHorizon(frame, (float)pitch, (float)roll, centerX, centerY, pixelsPerDegree);
                    DrawBankAngleMarkers(frame, centerX, centerY, (float)roll, pixelsPerDegree);
                    DrawFlightPathMarker(frame, currentVelocity, worldMatrix, roll, centerX, centerY, pixelsPerDegree);

                    // Instruments
                    DrawLeftInfoBox(frame, smoothedVelocity, centerX + 30f, centerY + centerY * INFO_BOX_Y_OFFSET_FACTOR, pixelsPerDegree, new LabelValue("T", myjet.offset));
                    DrawFlightInfo(frame, smoothedVelocity, smoothedGForces, heading, smoothedAltitude, smoothedAoA, smoothedThrottle, mach);
                    DrawSpeedIndicatorF18StyleKph(frame, smoothedVelocity);
                    DrawCompass(frame, heading);
                    DrawAltitudeIndicatorF18Style(frame, smoothedAltitude, totalElapsedTime);
                    DrawGForceIndicator(frame, smoothedGForces, peakGForce);

                    if (velocity > 1.0)
                    {
                        Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                        DrawAOAIndexer(frame, smoothedAoA, acceleration, velocity);
                    }

                    // Radar display
                    Vector2 surfaceSize = hud.SurfaceSize;
                    Vector2 radarOrigin = new Vector2(hud.SurfaceSize.X * 0.8f - RADAR_BORDER_MARGIN, surfaceSize.Y - RADAR_BOX_SIZE_PX - RADAR_BORDER_MARGIN);
                    Vector2 radarCenter = radarOrigin + new Vector2(RADAR_BOX_SIZE_PX / 2f, RADAR_BOX_SIZE_PX / 2f);

                    radarTargetCount = 0;
                    if (myjet.targetSlots[myjet.activeSlotIndex].IsOccupied && radarTargetCount < MAX_RADAR_TARGETS)
                        radarTargetBuffer[radarTargetCount++] = myjet.targetSlots[myjet.activeSlotIndex].Position;
                    for (int i = 0; i < myjet.targetSlots.Length && radarTargetCount < MAX_RADAR_TARGETS; i++)
                    {
                        if (i != myjet.activeSlotIndex && myjet.targetSlots[i].IsOccupied)
                            radarTargetBuffer[radarTargetCount++] = myjet.targetSlots[i].Position;
                    }

                    DrawTopDownRadarOptimized(frame, cockpit, hud, radarTargetBuffer, radarTargetCount, Color.White, HUD_PRIMARY, HUD_EMPHASIS, HUD_WARNING);
                    DrawRadarSweepLine(frame, radarCenter, RADAR_BOX_SIZE_PX / 2f);
                    DrawEnhancedThreatDisplay(frame, cockpit, radarCenter, RADAR_BOX_SIZE_PX / 2f);
                    DrawRWRThreatCones(frame, cockpit, radarCenter, RADAR_BOX_SIZE_PX / 2f);

                    // Targeting
                    if (myjet.targetSlots[myjet.activeSlotIndex].IsOccupied)
                    {
                        Vector3D activeTargetPos = myjet.targetSlots[myjet.activeSlotIndex].Position;
                        Vector3D activeTargetVel = myjet.targetSlots[myjet.activeSlotIndex].Velocity;
                        Vector3D activeTargetAccel = myjet.targetSlots[myjet.activeSlotIndex].Acceleration;
                        double muzzleVelocity = 910;
                        double range = Vector3D.Distance(shooterPosition, activeTargetPos);

                        Vector3D interceptPoint;
                        double timeToIntercept;
                        Vector3D aimPoint;
                        bool hasIntercept = BallisticsCalculator.CalculateInterceptPointIterative(shooterPosition, currentVelocity, muzzleVelocity, activeTargetPos, activeTargetVel, gravity, INTERCEPT_ITERATIONS, out interceptPoint, out timeToIntercept, out aimPoint, activeTargetAccel);

                        if (hasIntercept)
                        {
                            MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                            Vector3D directionToIntercept = aimPoint - shooterPosition;
                            Vector3D localDirectionToIntercept = Vector3D.TransformNormal(directionToIntercept, worldToCockpitMatrix);

                            bool isAimingAtPip = false;
                            if (localDirectionToIntercept.Z < 0)
                            {
                                Vector2 center = surfaceSize / 2f;
                                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;
                                float screenX = center.X + (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z) * scaleX;
                                float screenY = center.Y + (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z) * scaleY;
                                Vector2 pipScreenPos = new Vector2(screenX, screenY);
                                float pipRadius = viewportMinDim * 0.05f;
                                float distanceToPip = Vector2.Distance(center, pipScreenPos);
                                isAimingAtPip = distanceToPip <= pipRadius;
                            }

                            DrawGunFunnel(frame, cockpit, hud, interceptPoint, shooterPosition, range, isAimingAtPip);
                            DrawLeadingPip(frame, cockpit, hud, activeTargetPos, activeTargetVel, shooterPosition, currentVelocity, muzzleVelocity, gravity, HUD_WARNING, HUD_EMPHASIS, Color.HotPink, HUD_INFO, activeTargetAccel);
                            DrawTargetBrackets(frame, cockpit, hud, activeTargetPos, activeTargetVel, shooterPosition, currentVelocity);
                        }
                        DrawBreakawayWarning(frame, altitude, currentVelocity, activeTargetPos, shooterPosition);
                    }
                    DrawFormationGhosts(frame, cockpit, hud);

                    // Gun Control Overlay
                    DrawGunControlOverlay(frame);
                }

                RenderWeaponScreen(heading, altitude, currentVelocity, shooterPosition);
            }

            // =============================================
            // FLIGHT DATA & CONTROL SYSTEMS
            // =============================================

            private bool ValidateHUDState()
            {
                if (cockpit == null || hud == null)
                    return false;

                if (!cockpit.IsFunctional || !hudBlock.IsFunctional)
                    return false;

                return true;
            }

            private void UpdateFlightData(out MatrixD worldMatrix, out Vector3D forwardVector,
                out Vector3D upVector, out Vector3D gravity, out bool inGravity,
                out Vector3D gravityDirection)
            {
                totalElapsedTime += ParentProgram.Runtime.TimeSinceLastRun;

                worldMatrix = cockpit.WorldMatrix;
                forwardVector = worldMatrix.Forward;
                upVector = worldMatrix.Up;
                Vector3D leftVector = worldMatrix.Left;

                gravity = cockpit.GetNaturalGravity();
                inGravity = gravity.LengthSquared() > 0;
                gravityDirection = inGravity ? Vector3D.Normalize(gravity) : Vector3D.Zero;

                if (inGravity)
                {
                    pitch = Math.Asin(Vector3D.Dot(forwardVector, gravityDirection)) * (180 / Math.PI);
                    roll = Math.Atan2(
                        Vector3D.Dot(leftVector, gravityDirection),
                        Vector3D.Dot(upVector, gravityDirection)
                    ) * (180 / Math.PI);

                    if (roll < 0)
                        roll += 360;
                }

                velocity = cockpit.GetShipSpeed();
                mach = velocity / SEA_LEVEL_SPEED_OF_SOUND;

                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;
                deltaTime = ParentProgram.Runtime.TimeSinceLastRun.TotalSeconds;

                if (deltaTime <= 0)
                    deltaTime = 0.0167;

                Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                double gForces = acceleration.Length() / GRAVITY_ACCELERATION;
                previousVelocity = currentVelocity;

                if (gForces > peakGForce)
                    peakGForce = gForces;

                double heading = CalculateHeading();
                double altitude = GetAltitude();
                double aoa = CalculateAngleOfAttack(
                    cockpit.WorldMatrix.Forward,
                    cockpit.GetShipVelocities().LinearVelocity,
                    upVector
                );

                double velocityKPH = velocity * 3.6;

                UpdateSmoothedValues(velocityKPH, altitude, gForces, aoa, throttlecontrol);
                AdjustStabilizers(aoa, myjet);
            }

            private void UpdateThrottleControl(double throttle, double jumpthrottle)
            {
                float throttleChange = (float)(THROTTLE_RATE * deltaTime);

                if (throttle > 0.5)
                {
                    throttlecontrol += throttleChange;
                    if (throttlecontrol > 1f)
                        throttlecontrol = 1f;

                    if (throttlecontrol >= THROTTLE_HYDROGEN_THRESHOLD && !hydrogenswitch)
                    {
                        for (int i = 0; i < tanks.Count; i++)
                        {
                            if (tanks[i] != null)
                                tanks[i].Enabled = true;
                        }
                        hydrogenswitch = true;
                    }
                }
                else if (throttle < -0.5)
                {
                    throttlecontrol -= throttleChange;
                    if (throttlecontrol < 0f)
                        throttlecontrol = 0f;

                    if (throttlecontrol < (THROTTLE_HYDROGEN_THRESHOLD - HYDROGEN_HYSTERESIS) && hydrogenswitch)
                    {
                        for (int i = 0; i < tanks.Count; i++)
                        {
                            if (tanks[i] != null)
                                tanks[i].Enabled = false;
                        }
                        hydrogenswitch = false;
                    }
                }

                bool shouldOpenAirbrakes = jumpthrottle > 0.5;
                if (shouldOpenAirbrakes != airbrakesOpen)
                {
                    for (int i = 0; i < airbreaks.Count; i++)
                    {
                        if (shouldOpenAirbrakes)
                            airbreaks[i].OpenDoor();
                        else
                            airbreaks[i].CloseDoor();
                    }
                    airbrakesOpen = shouldOpenAirbrakes;
                }

                if (jumpthrottle < -0.5)
                {
                    if (!manualFireToggleCooldown)
                    {
                        myjet.manualfire = !myjet.manualfire;
                        manualFireToggleCooldown = true;
                    }
                }
                else
                {
                    manualFireToggleCooldown = false;
                }

                if (myjet.manualfire)
                {
                    for (int i = 0; i < myjet._gatlings.Count; i++)
                    {
                        if (myjet._gatlings[i] != null)
                            myjet._gatlings[i].Enabled = true;
                    }
                }

                float scaledThrottle = throttlecontrol <= THROTTLE_HYDROGEN_THRESHOLD
                    ? throttlecontrol / THROTTLE_HYDROGEN_THRESHOLD
                    : 1.0f;

                for (int i = 0; i < thrusters.Count; i++)
                {
                    if (thrusters[i] != null)
                        thrusters[i].ThrustOverridePercentage = scaledThrottle;
                }
            }

            // =============================================
            // SMOOTHING & CALCULATIONS
            // =============================================

            private void UpdateSmoothedValues(double velocityKPH, double altitude, double gForces, double aoa, double throttle)
            {
                if (velocityHistory.Count >= SMOOTHING_WINDOW_SIZE)
                {
                    velocitySum -= velocityHistory.Dequeue();
                }
                velocityHistory.Enqueue(velocityKPH);
                velocitySum += velocityKPH;
                smoothedVelocity = velocitySum / velocityHistory.Count;

                if (altitudeHistory.Count >= SMOOTHING_WINDOW_SIZE)
                {
                    altitudeSum -= altitudeHistory.Dequeue().Altitude;
                }
                altitudeHistory.Enqueue(new AltitudeTimePoint(totalElapsedTime, altitude));
                altitudeSum += altitude;
                smoothedAltitude = altitudeSum / altitudeHistory.Count;

                if (gForcesHistory.Count >= SMOOTHING_WINDOW_SIZE)
                {
                    gForcesSum -= gForcesHistory.Dequeue();
                }
                gForcesHistory.Enqueue(gForces);
                gForcesSum += gForces;
                smoothedGForces = gForcesSum / gForcesHistory.Count;

                if (aoaHistory.Count >= SMOOTHING_WINDOW_SIZE)
                {
                    aoaSum -= aoaHistory.Dequeue();
                }
                aoaHistory.Enqueue(aoa);
                aoaSum += aoa;
                smoothedAoA = aoaSum / aoaHistory.Count;

                smoothedThrottle = throttle * 100;
            }

            private void AdjustStabilizers(double aoa, Jet myjet)
            {
                if (cockpit == null)
                    return;

                AdjustTrim(rightstab, myjet.offset);
                AdjustTrim(leftstab, -myjet.offset);
            }

            private void AdjustTrim(IEnumerable<IMyTerminalBlock> stabilizers, float desiredTrim)
            {
                foreach (var item in stabilizers)
                {
                    currentTrim = item.GetValueFloat("Trim");
                    item.SetValue("Trim", desiredTrim);
                }
            }

            private double CalculateHeading()
            {
                return NavigationHelper.CalculateHeading(cockpit);
            }

            internal double GetAltitude()
            {
                double altitude;
                if (!cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude))
                {
                    return 0;
                }
                return altitude;
            }

            internal double CalculateAngleOfAttack(Vector3D forwardVector, Vector3D velocity, Vector3D upVector)
            {
                if (velocity.LengthSquared() < 0.01)
                    return 0;

                Vector3D velocityDirection = Vector3D.Normalize(velocity);

                double angleOfAttack =
                    Math.Atan2(
                        Vector3D.Dot(velocityDirection, upVector),
                        Vector3D.Dot(velocityDirection, forwardVector)
                    ) * (180 / Math.PI);

                return angleOfAttack;
            }
        }
    }
}
