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
            // --- HUD Color Palette (themed) ---
            // Theme index: 0=Green (default), 1=Cyan, 2=Orange, 3=White
            private static readonly Color[] THEME_PRIMARY = { Color.Lime, Color.Cyan, Color.Orange, Color.White };
            private static readonly Color[] THEME_SECONDARY = { Color.Green, Color.DodgerBlue, Color.DarkGoldenrod, Color.Gray };
            private static readonly Color[] THEME_HORIZON = { Color.LimeGreen, Color.DeepSkyBlue, Color.Goldenrod, Color.LightGray };
            private static readonly Color[] THEME_RADAR_FRIENDLY = { Color.DarkGreen, Color.DarkBlue, Color.DarkGoldenrod, Color.DarkGray };

            private static int _cachedTheme = 0;

            internal static void CacheTheme()
            {
                int t = (int)SystemManager.GetConfigValue("hud_theme");
                _cachedTheme = (t >= 0 && t < THEME_PRIMARY.Length) ? t : 0;
            }

            internal static Color HUD_PRIMARY => THEME_PRIMARY[_cachedTheme];
            internal static Color HUD_SECONDARY => THEME_SECONDARY[_cachedTheme];
            internal static Color HUD_HORIZON => THEME_HORIZON[_cachedTheme];
            internal static Color HUD_RADAR_FRIENDLY => THEME_RADAR_FRIENDLY[_cachedTheme];
            internal static readonly Color HUD_EMPHASIS = Color.Yellow;
            internal static readonly Color HUD_WARNING = Color.Red;
            internal static readonly Color HUD_INFO = Color.White;

            // --- Layout Constants ---
            private const float INFO_BOX_Y_OFFSET_FACTOR = 1.85f;
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
            private List<IMyDoor> airbrakes = new List<IMyDoor>();
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
            internal Vector3D previousVelocity = VZ;

            // --- Smoothed Values with Running Averages ---
            private CircularBuffer<double> velocityHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);
            internal CircularBuffer<AltitudeTimePoint> altitudeHistory = new CircularBuffer<AltitudeTimePoint>(SMOOTHING_WINDOW_SIZE);
            private CircularBuffer<double> gForcesHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);
            private CircularBuffer<double> aoaHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);

            internal double smoothedVelocity = 0;
            internal double smoothedAltitude = 0;
            internal double smoothedGForces = 0;
            internal double smoothedAoA = 0;
            internal double throttlePercent = 0;
            internal double verticalVelocityMps = 0;

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
            // AB gate: requires releasing W once at MIL, then re-engaging to activate AB.
            // Or hold at MIL for AB_AUTO_ENGAGE_SECONDS to auto-engage.
            private const float AB_AUTO_ENGAGE_SECONDS = 0.667f;
            private float abHoldTimer = 0f;
            private bool abGatePassed = false; // true after pilot released W once at MIL

            // --- Thrust Balancing Cache ---
            // MaxEffectiveThrust changes slowly with atmosphere; refresh at 0.5s intervals.
            private float cachedLeftMax = 0f, cachedRightMax = 0f;
            private double thrustCacheAge = double.MaxValue;
            private const double THRUST_CACHE_REFRESH_SECONDS = 0.5;

            // --- Airbrake State ---
            private bool airbrakesOpen = false;

            // --- Thrust Balancing ---
            // Reads MaxEffectiveThrust per side each tick to keep thrust equal

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

            // Radar sweep animation
            internal int radarSweepTick = 0;


            // --- Shared Constants for renderers ---
            internal const int INTERCEPT_ITERATIONS = 10;
            internal const double MIN_Z_FOR_PROJECTION = 0.1;
            internal const string TEXTURE_SQUARE = "SquareSimple";
            internal const string TEXTURE_CIRCLE = "CircleHollow";
            internal const string TEXTURE_TRIANGLE = "Triangle";
            internal const string TEXTURE_CIRCLE_SOLID = "Circle";

            internal const float RADAR_BOX_SIZE_PX = 100f;
            internal const float RADAR_BORDER_MARGIN = 10f;

            // --- Stall warning thresholds ---
            internal const double STALL_AOA = 28.0;
            internal const double STALL_CAUTION_PERCENT = 0.80;
            internal const double STALL_WARNING_PERCENT = 0.90;
            internal const int STALL_LEVEL_NORMAL = 0;
            internal const int STALL_LEVEL_CAUTION = 1;
            internal const int STALL_LEVEL_WARNING = 2;
            internal const int STALL_LEVEL_STALL = 3;

            // --- Tape constants ---
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
                weaponScreen = weaponSurface;
                radarControl = radar;

                rightstab = jet.rightstab;
                leftstab = jet.leftstab;

                thrusters = jet._thrustersbackwards;
                tanks = jet.tanks;
                // Disable hydrogen tanks on startup
                SetTanksEnabled(false);
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
                hud.ScriptBackgroundColor = Cr(0, 0, 0, 0);
                hud.ScriptForegroundColor = Color.White;

                ParentProgram.GridTerminalSystem.GetBlocksOfType(airbrakes, b => b.IsSameConstructAs(ParentProgram.Me));
                name = "HUD Control";
            }

            // =============================================
            // MODULE INTERFACE
            // =============================================

            public override string[] GetOptions() => new string[] { "Back to Main Menu" };
            public override void ExecuteOption(int index) { if (index == 0) SystemManager.ReturnToMainMenu(); }

            // =============================================
            // TICK - MAIN UPDATE LOOP
            // =============================================

            public override void Tick()
            {
                if (!ValidateHUDState())
                    return;

                radarSweepTick++;
                CacheTheme();

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
                viewportMinDim = Mn(hud.SurfaceSize.X, hud.SurfaceSize.Y);

                float centerX = hudCenter.X;
                float centerY = hudCenter.Y;
                float pixelsPerDegree = hud.SurfaceSize.Y / 16f;

                Vector3D shooterPosition = cockpit.GetPosition();
                double altitude = GetAltitude();

                MatrixD worldToCockpitMatrix = MatrixD.Transpose(cockpit.WorldMatrix);

                using (var frame = hud.DrawFrame())
                {
                    // Horizon & attitude
                    DrawArtificialHorizon(frame, (float)pitch, (float)roll, centerX, centerY, pixelsPerDegree);
                    DrawAircraftSymbol(frame, centerX, centerY);
                    DrawBankAngleMarkers(frame, centerX, centerY, (float)roll, pixelsPerDegree);
                    if (SystemManager.GetConfigValue("hud_fpm") > 0.5f)
                        DrawFlightPathMarker(frame, currentVelocity, worldToCockpitMatrix, roll, centerX, centerY, pixelsPerDegree);

                    // Instruments
                    DrawLeftInfoBox(frame, smoothedVelocity, centerX + 30f, centerY + centerY * INFO_BOX_Y_OFFSET_FACTOR, pixelsPerDegree, new LabelValue("T", myjet.offset));
                    DrawFlightInfo(frame, throttlePercent);
                    DrawSpeedIndicatorF18StyleKph(frame, smoothedVelocity);
                    if (SystemManager.GetConfigValue("hud_compass") > 0.5f)
                        DrawCompass(frame, heading);
                    DrawAltitudeIndicatorF18Style(frame, smoothedAltitude, totalElapsedTime);
                    if (SystemManager.GetConfigValue("hud_gforce") > 0.5f)
                        DrawGForceIndicator(frame, smoothedGForces, peakGForce);

                    if (velocity > 1.0 && SystemManager.GetConfigValue("hud_aoa") > 0.5f)
                    {
                        Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                        DrawAOAIndexer(frame, smoothedAoA, acceleration, velocity);
                    }

                    // Radar minimap
                    if (SystemManager.GetConfigValue("hud_radar") > 0.5f)
                        DrawRadarMinimap(frame, cockpit, hud);

                    Vector2 surfaceSize = hud.SurfaceSize;
                    var selectedEnemy = myjet.GetSelectedEnemy();

                    // Targeting
                    if (selectedEnemy.HasValue)
                    {
                        Vector3D activeTargetVel = selectedEnemy.Value.Velocity;
                        Vector3D activeTargetAccel = selectedEnemy.Value.Acceleration;
                        // Compensate for 1-tick spawn delay: in the tick between calculation
                        // and bullet spawn, both objects move. Adjust target by V_rel * dt
                        // to reflect the shorter range at spawn time.
                        // Spawn-delay compensation: one dt of relative motion between
                        // computing the lead and the bullet actually spawning.
                        Vector3D activeTargetPos = selectedEnemy.Value.Position
                            + (activeTargetVel - currentVelocity) * deltaTime;
                        double muzzleVelocity = SystemManager.GetConfigValue("gun_muzzle_velocity");
                        double range = VDi(shooterPosition, activeTargetPos);

                        Vector3D interceptPoint;
                        double timeToIntercept;
                        Vector3D aimPoint;
                        bool hasIntercept = BallisticsCalculator.CalculateInterceptPoint(shooterPosition, currentVelocity, muzzleVelocity, activeTargetPos, activeTargetVel, INTERCEPT_ITERATIONS, out interceptPoint, out timeToIntercept, out aimPoint, activeTargetAccel);

                        if (hasIntercept)
                        {
                            Vector3D directionToIntercept = aimPoint - shooterPosition;
                            Vector3D localDirectionToIntercept = VTN(directionToIntercept, worldToCockpitMatrix);

                            bool isAimingAtPip = false;
                            if (localDirectionToIntercept.Z < 0)
                            {
                                Vector2 pipScreenPos = SpriteHelpers.ProjectToScreen(localDirectionToIntercept, surfaceSize / 2f, surfaceSize);
                                float pipRadius = viewportMinDim * 0.05f;
                                float distanceToPip = Vector2.Distance(surfaceSize / 2f, pipScreenPos);
                                isAimingAtPip = distanceToPip <= pipRadius;
                            }

                            if (SystemManager.GetConfigValue("hud_gun_funnel") > 0.5f)
                                DrawGunFunnel(frame, hud, worldToCockpitMatrix, interceptPoint, shooterPosition, range, isAimingAtPip);
                            DrawLeadingPip(frame, hud, worldToCockpitMatrix, shooterPosition, activeTargetPos, interceptPoint, aimPoint, timeToIntercept, HUD_WARNING, HUD_EMPHASIS, HUD_WARNING, HUD_INFO);
                            if (SystemManager.GetConfigValue("hud_target_brackets") > 0.5f)
                                DrawTargetBrackets(frame, hud, worldToCockpitMatrix, activeTargetPos, activeTargetVel, shooterPosition, currentVelocity);
                        }

                        if (SystemManager.GetConfigValue("hud_breakaway") > 0.5f)
                            DrawBreakawayWarning(frame, altitude, currentVelocity, activeTargetPos, shooterPosition, activeTargetVel);
                    }
                    DrawFormationGhosts(frame, hud, worldToCockpitMatrix);
                    DrawGunControlOverlay(frame);
                }

                RenderWeaponScreen(heading, altitude, currentVelocity, shooterPosition);
            }

            // =============================================
            // FLIGHT DATA & CONTROL SYSTEMS
            // =============================================

            private bool ValidateHUDState()
            {
                if (cockpit == null || hud == null || hudBlock == null)
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

                gravity = myjet.CachedGravity;
                inGravity = gravity.LengthSquared() > 0;
                gravityDirection = inGravity ? VN(gravity) : VZ;

                if (inGravity)
                {
                    pitch = Math.Asin(VD(forwardVector, gravityDirection)) * (180 / PI);
                    roll = At2(
                        VD(leftVector, gravityDirection),
                        VD(upVector, gravityDirection)
                    ) * (180 / PI);
                }

                velocity = cockpit.GetShipSpeed();
                mach = velocity / SEA_LEVEL_SPEED_OF_SOUND;

                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;

                // VVI: vertical component of velocity (positive = climbing)
                if (inGravity)
                    verticalVelocityMps = VD(currentVelocity, -gravityDirection);
                else
                    verticalVelocityMps = currentVelocity.Y;
                deltaTime = ParentProgram.Runtime.TimeSinceLastRun.TotalSeconds;

                if (deltaTime <= 0)
                    deltaTime = 0.0167;

                Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                double gForces = acceleration.Length() / GRAVITY_ACCELERATION;
                previousVelocity = currentVelocity;

                if (gForces > peakGForce)
                    peakGForce = gForces;

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

                    // Clamp at MIL until AB gate is passed
                    if (!hydrogenswitch && throttlecontrol > THROTTLE_HYDROGEN_THRESHOLD)
                        throttlecontrol = THROTTLE_HYDROGEN_THRESHOLD;

                    if (throttlecontrol > 1f)
                        throttlecontrol = 1f;

                    if (throttlecontrol >= THROTTLE_HYDROGEN_THRESHOLD && !hydrogenswitch)
                    {
                        abHoldTimer += (float)deltaTime;
                        // AB engages if: pilot released W once and re-pushed, OR held at MIL long enough.
                        if (abGatePassed || abHoldTimer > AB_AUTO_ENGAGE_SECONDS)
                        {
                            SetTanksEnabled(true);
                            hydrogenswitch = true;
                            abGatePassed = false;
                            abHoldTimer = 0f;
                        }
                    }
                    else if (throttlecontrol < THROTTLE_HYDROGEN_THRESHOLD)
                    {
                        abHoldTimer = 0f;
                    }
                }
                else if (throttle < -0.5)
                {
                    throttlecontrol -= throttleChange;
                    if (throttlecontrol < 0f)
                        throttlecontrol = 0f;

                    if (throttlecontrol < (THROTTLE_HYDROGEN_THRESHOLD - HYDROGEN_HYSTERESIS) && hydrogenswitch)
                    {
                        SetTanksEnabled(false);
                        hydrogenswitch = false;
                    }
                    // Dropping below MIL resets the gate
                    if (throttlecontrol < THROTTLE_HYDROGEN_THRESHOLD)
                    {
                        abGatePassed = false;
                        abHoldTimer = 0f;
                    }
                }
                else
                {
                    // W not pressed — if sitting at MIL, this release arms the gate
                    if (throttlecontrol >= THROTTLE_HYDROGEN_THRESHOLD && !hydrogenswitch)
                    {
                        abGatePassed = true;
                        abHoldTimer = 0f;
                    }
                }

                bool shouldOpenAirbrakes = jumpthrottle > 0.5;
                if (shouldOpenAirbrakes != airbrakesOpen)
                {
                    for (int i = 0; i < airbrakes.Count; i++)
                    {
                        if (shouldOpenAirbrakes)
                            airbrakes[i].OpenDoor();
                        else
                            airbrakes[i].CloseDoor();
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
                        if (myjet._gatlings[i] != null && !myjet._gatlings[i].Enabled)
                            myjet._gatlings[i].Enabled = true;
                    }
                }

                float scaledThrottle = throttlecontrol <= THROTTLE_HYDROGEN_THRESHOLD
                    ? throttlecontrol / THROTTLE_HYDROGEN_THRESHOLD
                    : 1.0f;

                // Read current max thrust capacity per side (center excluded — they don't cause yaw).
                // MaxEffectiveThrust changes slowly with atmosphere, so cache for THRUST_CACHE_REFRESH_SECONDS.
                thrustCacheAge += deltaTime;
                if (thrustCacheAge >= THRUST_CACHE_REFRESH_SECONDS)
                {
                    cachedLeftMax = 0f;
                    cachedRightMax = 0f;
                    for (int i = 0; i < myjet.leftEngines.Count; i++)
                        if (myjet.leftEngines[i] != null && myjet.leftEngines[i].IsFunctional)
                            cachedLeftMax += myjet.leftEngines[i].MaxEffectiveThrust;
                    for (int i = 0; i < myjet.rightEngines.Count; i++)
                        if (myjet.rightEngines[i] != null && myjet.rightEngines[i].IsFunctional)
                            cachedRightMax += myjet.rightEngines[i].MaxEffectiveThrust;
                    thrustCacheAge = 0;
                }
                float leftMax = cachedLeftMax, rightMax = cachedRightMax;

                // Target thrust = weakest side * throttle percentage
                float weakerMax = Mn(leftMax, rightMax);
                float targetThrust = weakerMax * scaledThrottle;

                // Set each side's override so they produce equal Newtons
                float leftOverride = leftMax > 0 ? targetThrust / leftMax : 0f;
                float rightOverride = rightMax > 0 ? targetThrust / rightMax : 0f;

                SetGroupOverride(myjet.leftEngines, leftOverride);
                SetGroupOverride(myjet.rightEngines, rightOverride);

                // Center engines: no balancing needed, straight throttle
                SetGroupOverride(myjet.centerEngines, scaledThrottle);

                // Hydrogen/AB engines: full override when AB is on
                if (hydrogenswitch)
                {
                    SetGroupOverride(myjet.leftAB, 1f);
                    SetGroupOverride(myjet.rightAB, 1f);
                    SetGroupOverride(myjet.centerAB, 1f);
                }
            }

            private static void SetGroupOverride(List<IMyThrust> group, float value)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    if (group[i] != null && Ab(group[i].ThrustOverridePercentage - value) > 0.001f)
                        group[i].ThrustOverridePercentage = value;
                }
            }

            private void SetTanksEnabled(bool enabled)
            {
                for (int i = 0; i < tanks.Count; i++)
                {
                    if (tanks[i] != null && tanks[i].Enabled != enabled)
                        tanks[i].Enabled = enabled;
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

                throttlePercent = throttle * 100;
            }

            static float _lastTrimOffset = float.NaN;

            private void AdjustStabilizers(double aoa, Jet myjet)
            {
                if (cockpit == null || CanardModule.OwnsStabs || myjet.offset == _lastTrimOffset)
                    return;
                _lastTrimOffset = myjet.offset;

                AdjustTrim(rightstab, myjet.offset);
                AdjustTrim(leftstab, -myjet.offset);
            }

            private void AdjustTrim(IEnumerable<IMyTerminalBlock> stabilizers, float desiredTrim)
            {
                foreach (var item in stabilizers)
                {
                    currentTrim = item.GetValueFloat("Trim");
                    if (Ab(currentTrim - desiredTrim) > 0.001f)
                        item.SetValue("Trim", desiredTrim);
                }
            }

            private double CalculateHeading()
            {
                return NavigationHelper.CalculateHeading(cockpit);
            }

            internal double GetAltitude()
            {
                return myjet.GetAltitude();
            }

            internal double CalculateAngleOfAttack(Vector3D forwardVector, Vector3D velocity, Vector3D upVector)
            {
                if (velocity.LengthSquared() < 0.01)
                    return 0;

                Vector3D velocityDirection = VN(velocity);

                double angleOfAttack =
                    At2(
                        VD(velocityDirection, upVector),
                        VD(velocityDirection, forwardVector)
                    ) * (180 / PI);

                return angleOfAttack;
            }
        }
    }
}
