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
        class HUDModule : ProgramModule
        {
            // --- HUD Color Palette ---
            private static readonly Color HUD_PRIMARY = Color.Lime;
            private static readonly Color HUD_SECONDARY = Color.Green;
            private static readonly Color HUD_HORIZON = Color.LimeGreen;
            private static readonly Color HUD_EMPHASIS = Color.Yellow;
            private static readonly Color HUD_WARNING = Color.Red;
            private static readonly Color HUD_INFO = Color.White;
            private static readonly Color HUD_RADAR_FRIENDLY = Color.DarkGreen;

            // --- Layout Constants ---
            private const float SPEED_TAPE_CENTER_Y_FACTOR = 2.25f;
            private const float INFO_BOX_Y_OFFSET_FACTOR = 1.85f;
            private const float ALTITUDE_TAPE_CENTER_Y_FACTOR = 2.0f;
            private const float THROTTLE_HYDROGEN_THRESHOLD = 0.8f;

            // --- Physics Constants ---
            private const double SEA_LEVEL_SPEED_OF_SOUND = 343.0; // m/s
            private const double GRAVITY_ACCELERATION = 9.81; // m/s²

            // --- Smoothing Configuration ---
            private const int SMOOTHING_WINDOW_SIZE = 10;

            // --- Component References ---
            IMyCockpit cockpit;
            IMyTextSurface hud;
            IMyTextSurface weaponScreen; // Third screen for weapon/combat info
            IMyTerminalBlock hudBlock;
            List<IMyTerminalBlock> leftstab = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> rightstab = new List<IMyTerminalBlock>();
            private List<IMyThrust> thrusters = new List<IMyThrust>();
            private List<IMyGasTank> tanks = new List<IMyGasTank>();
            private List<IMyDoor> airbreaks = new List<IMyDoor>();
            Jet myjet;
            RadarControlModule radarControl; // Reference to Radar Control for threat cone display

            // --- Flight Data ---
            double peakGForce = 0;
            float currentTrim;
            double pitch = 0;
            double roll = 0;
            double velocity;
            double deltaTime;
            double mach;
            Vector3D previousVelocity = Vector3D.Zero;

            // --- Smoothed Values with Running Averages ---
            CircularBuffer<double> velocityHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);
            CircularBuffer<AltitudeTimePoint> altitudeHistory = new CircularBuffer<AltitudeTimePoint>(SMOOTHING_WINDOW_SIZE);
            CircularBuffer<double> gForcesHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);
            CircularBuffer<double> aoaHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);

            double smoothedVelocity = 0;
            double smoothedAltitude = 0;
            double smoothedGForces = 0;
            double smoothedAoA = 0;
            double smoothedThrottle = 0;

            // Running sums for efficient smoothing
            private double velocitySum = 0;
            private double gForcesSum = 0;
            private double aoaSum = 0;

            // --- Throttle Control ---
            float throttlecontrol = 0f;
            bool hydrogenswitch = false;

            // --- UI State ---
            string hotkeytext = "Test";
            private TimeSpan totalElapsedTime = TimeSpan.Zero;

            // --- Cached HUD Values (recalculate when surface size changes) ---
            private Vector2 hudCenter;
            private float viewportMinDim;

            // --- NEW: Enhanced HUD Features ---
            // Velocity trail (tadpole)

            // Missile tracking
            private struct MissileTrackingData
            {
                public int BayIndex;
                public TimeSpan LaunchTime;
                public double EstimatedTOF;
                public Vector3D TargetPosition;
            }
            private List<MissileTrackingData> activeMissiles = new List<MissileTrackingData>();

            // HUD Mode System
            private enum HUDMode { AirToAir, AirToGround, Navigation }
            private HUDMode currentHUDMode = HUDMode.AirToAir;

            // Radar sweep animation
            private int radarSweepTick = 0;

            // Fuel state tracking
            private const double BINGO_FUEL_PERCENT = 0.20; // 20% fuel = Bingo state
            private const double LOW_FUEL_PERCENT = 0.35;   // 35% fuel = Low fuel warning

            public HUDModule(Program program, Jet jet, IMyTextSurface weaponSurface, RadarControlModule radar) : base(program)
            {
                cockpit = jet._cockpit;
                hudBlock = jet.hudBlock;
                hud = jet.hud;
                weaponScreen = weaponSurface; // Store weapon screen reference
                radarControl = radar; // Store Radar Control reference

                rightstab = jet.rightstab;
                leftstab = jet.leftstab;

                thrusters = jet._thrustersbackwards;
                tanks = jet.tanks;
                // Disable hydrogen tanks on startup - they are enabled via throttle control
                // when afterburner threshold is exceeded. This ensures controlled H2 usage.
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
                    // Cycle through HUD modes
                    currentHUDMode = (HUDMode)(((int)currentHUDMode + 1) % 3);
                }
            }


            /// <summary>
            /// Fixed-size circular buffer that automatically removes oldest items when full.
            /// Uses composition instead of inheritance to avoid method hiding issues.
            /// </summary>
            public class CircularBuffer<T>
            {
                private readonly Queue<T> _queue;
                private readonly int _capacity;

                public CircularBuffer(int capacity)
                {
                    _capacity = capacity;
                    _queue = new Queue<T>(capacity);
                }

                public void Enqueue(T item)
                {
                    _queue.Enqueue(item);
                    while (_queue.Count > _capacity)
                    {
                        _queue.Dequeue();
                    }
                }

                public T Dequeue() => _queue.Dequeue();
                public T Peek() => _queue.Peek();
                public int Count => _queue.Count;
                public void Clear() => _queue.Clear();

                // Return array for iteration to avoid IEnumerator issues with MDK minification
                public T[] ToArray() => _queue.ToArray();
            }

            // --- Constants ---
            const int INTERCEPT_ITERATIONS = 10; // Number of iterations for ballistic calculation
            const double MIN_Z_FOR_PROJECTION = 0.1; // Minimum absolute Z value for safe projection
            const string TEXTURE_SQUARE = "SquareSimple";
            const string TEXTURE_CIRCLE = "CircleHollow"; // Assumes you have this texture
            const string TEXTURE_TRIANGLE = "Triangle";


            private void AddLineSprite(MySpriteDrawFrame frame, Vector2 start, Vector2 end, float thickness, Color color)
            {
                Vector2 delta = end - start;
                float length = delta.Length();
                if (length < 0.1f) return; // Don't draw zero-length lines

                Vector2 position = start + delta / 2f; // Center of the line
                float rotation = (float)Math.Atan2(delta.Y, delta.X) - (float)Math.PI / 2f; // Rotation to align the square

                var line = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = TEXTURE_SQUARE,
                    Position = position,
                    Size = new Vector2(thickness, length),
                    Color = color,
                    RotationOrScale = rotation,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(line);
            }



            private bool CalculateInterceptPointIterative(
                Vector3D shooterPosition,
                Vector3D shooterVelocity,
                double projectileSpeed,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D gravity,
                int maxIterations,
                out Vector3D interceptPoint,
                out double timeToIntercept)
            {
                interceptPoint = Vector3D.Zero;
                timeToIntercept = -1;

                Vector3D relativePosition = targetPosition - shooterPosition;
                Vector3D relativeVelocity = targetVelocity - shooterVelocity;
                double a = relativeVelocity.LengthSquared() - projectileSpeed * projectileSpeed;
                double b = 2 * Vector3D.Dot(relativePosition, relativeVelocity);
                double c = relativePosition.LengthSquared();
                double t_guess = -1;

                if (Math.Abs(a) < 1e-6)
                {
                    if (Math.Abs(b) > 1e-6) t_guess = -c / b;
                }
                else
                {
                    double discriminant = b * b - 4 * a * c;
                    if (discriminant >= 0)
                    {
                        double sqrtDiscriminant = Math.Sqrt(discriminant);
                        double t1 = (-b - sqrtDiscriminant) / (2 * a);
                        double t2 = (-b + sqrtDiscriminant) / (2 * a);
                        if (t1 > 0 && t2 > 0) t_guess = Math.Min(t1, t2);
                        else t_guess = Math.Max(t1, t2);
                    }
                }

                if (t_guess <= 0) return false;

                timeToIntercept = t_guess;
                double previousTimeToIntercept = timeToIntercept;
                const double CONVERGENCE_THRESHOLD = 0.001; // Converged if change < 1ms

                for (int i = 0; i < maxIterations; ++i)
                {
                    if (timeToIntercept <= 0) break;

                    Vector3D predictedTargetPos = targetPosition + targetVelocity * timeToIntercept;
                    Vector3D projectileDisplacement = predictedTargetPos - shooterPosition;
                    Vector3D requiredLaunchVel = (projectileDisplacement - 0.5 * gravity * timeToIntercept * timeToIntercept) / timeToIntercept;

                    Vector3D launchDirection = Vector3D.Normalize(requiredLaunchVel);
                    Vector3D actualLaunchVel = launchDirection * projectileSpeed + shooterVelocity;
                    Vector3D newRelativeVelocity = targetVelocity - actualLaunchVel;

                    a = newRelativeVelocity.LengthSquared() - projectileSpeed * projectileSpeed;
                    b = 2 * Vector3D.Dot(relativePosition, newRelativeVelocity);
                    c = relativePosition.LengthSquared();

                    t_guess = -1;
                    if (Math.Abs(a) < 1e-6)
                    {
                        if (Math.Abs(b) > 1e-6) t_guess = -c / b;
                    }
                    else
                    {
                        double discriminant = b * b - 4 * a * c;
                        if (discriminant >= 0)
                        {
                            double sqrtDiscriminant = Math.Sqrt(discriminant);
                            double t1 = (-b - sqrtDiscriminant) / (2 * a);
                            double t2 = (-b + sqrtDiscriminant) / (2 * a);
                            if (t1 > 0 && t2 > 0) t_guess = Math.Min(t1, t2);
                            else t_guess = Math.Max(t1, t2);
                        }
                    }

                    if (t_guess <= 0)
                    {
                        return false;
                    }

                    // FIX: Check convergence - if change is small enough, we're done
                    double delta = Math.Abs(t_guess - previousTimeToIntercept);
                    previousTimeToIntercept = timeToIntercept;
                    timeToIntercept = t_guess;

                    if (delta < CONVERGENCE_THRESHOLD)
                    {
                        break; // Converged successfully
                    }
                }

                interceptPoint = targetPosition + targetVelocity * timeToIntercept;
                return timeToIntercept > 0;
            }
            private void DrawLeadingPip(
        MySpriteDrawFrame frame,
        IMyCockpit cockpit, // Pass cockpit for world matrix
        IMyTextSurface hud,   // Pass HUD surface for size/center
        Vector3D targetPosition,
        Vector3D targetVelocity,
        Vector3D shooterPosition,
        Vector3D shooterVelocity,
        double projectileSpeed,
        Vector3D gravity,         // World gravity vector
        Color pipColor,           // Customizable colors
        Color offScreenColor,
        Color behindColor,
        Color reticleColor
    )
            {
                if (cockpit == null || hud == null) return; // Safety check
                const float MIN_DISTANCE_FOR_SCALING = 50f;  // Target closer than this uses max pip size (e.g., 500 meters)
                const float MAX_DISTANCE_FOR_SCALING = 3000f; // Target farther than this uses min pip size (e.g., 3000 meters)
                const float MAX_PIP_SIZE_FACTOR = 0.1f;      // Pip size factor at min distance (relative to viewportMinDim)
                const float MIN_PIP_SIZE_FACTOR = 0.01f;     // Pip size factor at max distance (relative to viewportMinDim)

                Vector3D interceptPoint;
                double timeToIntercept;
                bool isAimingAtPip = false; // Initialize the output parameter to false
                                            // Use the iterative solver
                if (!CalculateInterceptPointIterative(
                shooterPosition,
                shooterVelocity,
                projectileSpeed,
                targetPosition,
                targetVelocity,
                gravity,
                INTERCEPT_ITERATIONS,
                out interceptPoint,
                out timeToIntercept
            ))
                {
                    return;
                }


                Vector3D directionToIntercept = interceptPoint - shooterPosition;
                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector3D localDirectionToIntercept = Vector3D.TransformNormal(directionToIntercept, worldToCockpitMatrix);

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;
                float viewportMinDim = Math.Min(surfaceSize.X, surfaceSize.Y);
                float targetMarkerSize = viewportMinDim * 0.02f;
                float lineThickness = Math.Max(1f, viewportMinDim * 0.004f);
                float reticleArmLength = viewportMinDim * 0.025f;
                float arrowSize = viewportMinDim * 0.04f;
                float arrowHeadSize = viewportMinDim * 0.025f;
                double distanceToIntercept = Vector3D.Distance(shooterPosition, interceptPoint);
                float distanceScaleFactor = (float)MathHelper.Clamp((MAX_DISTANCE_FOR_SCALING - distanceToIntercept) / (MAX_DISTANCE_FOR_SCALING - MIN_DISTANCE_FOR_SCALING), 0.0, 1.0);
                float currentPipSizeFactor = MathHelper.Lerp(MIN_PIP_SIZE_FACTOR, MAX_PIP_SIZE_FACTOR, distanceScaleFactor);
                float dynamicPipSize = viewportMinDim * currentPipSizeFactor;


                if (localDirectionToIntercept.Z > MIN_Z_FOR_PROJECTION)
                {
                    AddLineSprite(frame, center - new Vector2(reticleArmLength, 0), center + new Vector2(reticleArmLength, 0), lineThickness, behindColor);
                    AddLineSprite(frame, center - new Vector2(0, reticleArmLength), center + new Vector2(0, reticleArmLength), lineThickness, behindColor);
                    return;
                }

                AddLineSprite(frame, center - new Vector2(reticleArmLength, 0), center + new Vector2(reticleArmLength, 0), lineThickness, reticleColor);
                AddLineSprite(frame, center - new Vector2(0, reticleArmLength), center + new Vector2(0, reticleArmLength), lineThickness, reticleColor);


                if (Math.Abs(localDirectionToIntercept.Z) < MIN_Z_FOR_PROJECTION)
                {
                    localDirectionToIntercept.Z = -MIN_Z_FOR_PROJECTION;
                }


                // FOV projection constants - empirically determined from cockpit perspective
                // These values convert 3D directions to 2D screen coordinates
                const float COCKPIT_FOV_SCALE_X = 0.3434f; // Horizontal FOV scale factor
                const float COCKPIT_FOV_SCALE_Y = 0.31f;   // Vertical FOV scale (adjusted for aspect ratio)
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;
                float screenX = center.X + (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z) * scaleX;
                float screenY = center.Y + (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z) * scaleY;
                Vector2 pipScreenPos = new Vector2(screenX, screenY);

                bool isOnScreen = pipScreenPos.X >= 0 && pipScreenPos.X <= surfaceSize.X &&
                                  pipScreenPos.Y >= 0 && pipScreenPos.Y <= surfaceSize.Y;
                float distanceToPip = Vector2.Distance(center, pipScreenPos);
                float pipRadius = dynamicPipSize / 2f;
                if (distanceToPip <= pipRadius)
                {
                    isAimingAtPip = true;
                }
                if (isAimingAtPip)
                {
                    for (int i = 0; i < myjet._gatlings.Count; i++)
                    {
                        myjet._gatlings[i].Enabled = true;
                    }
                }
                else
                {
                    if (myjet.manualfire == false)
                    {
                        for (int i = 0; i < myjet._gatlings.Count; i++)
                        {
                            myjet._gatlings[i].Enabled = false;
                        }
                    }
                }
                if (isOnScreen)
                {
                    var pipSprite = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_CIRCLE,
                        Position = pipScreenPos,
                        Size = new Vector2(dynamicPipSize, dynamicPipSize),
                        Color = pipColor,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(pipSprite);
                    Vector2 targetScreenPos = Vector2.Zero; // Initialize
                    const float velocityIndicatorScale = 20f; // Example: Represents 0.3 seconds of travel

                    Vector3D targetVelocityEndPointWorld = interceptPoint + targetVelocity * velocityIndicatorScale;

                    // FIX: Removed duplicate worldToLocalMatrix (already have worldToCockpitMatrix)
                    // FIX: Removed dead code - localTargetVelocityEndPoint was never used
                    Vector2 targetVelEndPointScreenPos = Vector2.Zero; // Initialize

                    // 2. Get the direction vector FROM THE SHOOTER to that point
                    Vector3D directionToVelEndPoint = Vector3D.Normalize(targetVelocityEndPointWorld - shooterPosition);

                    // 3. Transform that DIRECTION into the cockpit's local reference frame
                    Vector3D localDirectionToVelEndPoint = Vector3D.TransformNormal(directionToVelEndPoint, worldToCockpitMatrix);

                    // 4. Now, project this correct local direction onto the screen
                    if (localDirectionToVelEndPoint.Z < 0) // Check if it's in front
                    {
                        float screenX_vel = center.X + (float)(localDirectionToVelEndPoint.X / -localDirectionToVelEndPoint.Z) * scaleX;
                        float screenY_vel = center.Y + (float)(-localDirectionToVelEndPoint.Y / -localDirectionToVelEndPoint.Z) * scaleY;
                        targetVelEndPointScreenPos = new Vector2(screenX_vel, screenY_vel);

                        // Draw your line from pipScreenPos to targetVelEndPointScreenPos
                    }

                    // FIX: Use worldToCockpitMatrix instead of duplicate worldToLocalMatrix
                    Vector3D directionToTarget = targetPosition - shooterPosition;
                    Vector3D localDirectionToTarget = Vector3D.TransformNormal(directionToTarget, worldToCockpitMatrix);

                    Vector2 currentTargetScreenPos = Vector2.Zero; // Initialize

                    if (localDirectionToTarget.Z < -MIN_Z_FOR_PROJECTION)
                    {

                        float screenX_tgt = center.X + (float)(localDirectionToTarget.X / -localDirectionToTarget.Z) * scaleX;
                        float screenY_tgt = center.Y + (float)(-localDirectionToTarget.Y / -localDirectionToTarget.Z) * scaleY; // Y inverted
                        currentTargetScreenPos = new Vector2(screenX_tgt, screenY_tgt);
                    }

                    // FIX: Removed redundant isOnScreen check (already inside isOnScreen block)
                    float halfMark = targetMarkerSize / 2f;
                    AddLineSprite(frame, currentTargetScreenPos - new Vector2(halfMark, halfMark), currentTargetScreenPos + new Vector2(halfMark, halfMark), lineThickness, Color.Yellow);
                    AddLineSprite(frame, currentTargetScreenPos - new Vector2(halfMark, -halfMark), currentTargetScreenPos + new Vector2(halfMark, -halfMark), lineThickness, Color.Yellow);
                    AddLineSprite(frame, pipScreenPos, currentTargetScreenPos, lineThickness, Color.Yellow);
                }
                else
                {
                    Vector2 direction = pipScreenPos - center;
                    direction.Normalize();
                    float maxDistX = surfaceSize.X / 2f - arrowSize / 2f;
                    float maxDistY = surfaceSize.Y / 2f - arrowSize / 2f;
                    float angle = (float)Math.Atan2(direction.Y, direction.X);

                    float edgeX = (float)Math.Cos(angle) * maxDistX;
                    float edgeY = (float)Math.Sin(angle) * maxDistY;

                    // FIX: Prevent division by zero when edgeX or edgeY is near zero
                    Vector2 edgePoint;
                    float absEdgeX = Math.Abs(edgeX);
                    float absEdgeY = Math.Abs(edgeY);

                    // Add epsilon to prevent division by zero
                    if (absEdgeX < 1e-6f) absEdgeX = 1e-6f;
                    if (absEdgeY < 1e-6f) absEdgeY = 1e-6f;

                    if (Math.Abs(edgeX / maxDistX) > Math.Abs(edgeY / maxDistY))
                    {
                        edgePoint = new Vector2(center.X + Math.Sign(edgeX) * maxDistX, center.Y + edgeY * (maxDistX / absEdgeX));
                    }
                    else
                    {
                        edgePoint = new Vector2(center.X + edgeX * (maxDistY / absEdgeY), center.Y + Math.Sign(edgeY) * maxDistY);
                    }


                    edgePoint.X = MathHelper.Clamp(edgePoint.X, arrowSize / 2f, surfaceSize.X - arrowSize / 2f);
                    edgePoint.Y = MathHelper.Clamp(edgePoint.Y, arrowSize / 2f, surfaceSize.Y - arrowSize / 2f);


                    float arrowRotation = (float)Math.Atan2(direction.Y, direction.X);
                    var arrowSprite = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_TRIANGLE,
                        Position = edgePoint,
                        Size = new Vector2(arrowHeadSize, arrowHeadSize),
                        Color = offScreenColor,
                        RotationOrScale = arrowRotation + (float)Math.PI / 2f,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(arrowSprite);
                }
            }




            const float RADAR_RANGE_METERS = 15000f;
            const float RADAR_BOX_SIZE_PX = 100f;
            const float RADAR_BORDER_MARGIN = 10f;

            // Optimized version using array of target positions
            private void DrawTopDownRadarOptimized(
                MySpriteDrawFrame frame,
                IMyCockpit cockpit,
                IMyTextSurface hud,
                Vector3D[] targetPositions,
                Color radarBgColor,
                Color radarBorderColor,
                Color playerColor,
                Color primaryTargetColor
            )
            {
                if (cockpit == null || hud == null) return;

                Vector2 surfaceSize = hud.SurfaceSize;

                // FIX: Simplified radar origin calculation (X - X*0.2 = X*0.8)
                Vector2 radarOrigin = new Vector2(
                    hud.SurfaceSize.X * 0.8f - RADAR_BORDER_MARGIN,  // 80% from left edge
                    surfaceSize.Y - RADAR_BOX_SIZE_PX - RADAR_BORDER_MARGIN
                );
                Vector2 radarSize = new Vector2(RADAR_BOX_SIZE_PX, RADAR_BOX_SIZE_PX);
                Vector2 radarCenter = radarOrigin + radarSize / 2f;
                float radarRadius = RADAR_BOX_SIZE_PX / 2f;

                DrawRectangleOutline(frame, radarOrigin.X - 5f, radarOrigin.Y - 5f, radarSize.X + 10f, radarSize.Y + 10f, 1f, radarBorderColor);


                var playerArrow = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = TEXTURE_TRIANGLE,
                    Position = radarCenter,
                    Size = new Vector2(radarRadius * 0.15f, radarRadius * 0.15f),
                    Color = playerColor,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = 0
                };
                frame.Add(playerArrow);

                Vector3D shooterPosition = cockpit.GetPosition();

                // Calculate world up vector from gravity
                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D worldUp;
                if (gravity.LengthSquared() < 0.01)
                {
                    worldUp = cockpit.WorldMatrix.Up;
                }
                else
                {
                    worldUp = Vector3D.Normalize(-gravity);
                }

                // Create yaw-aligned coordinate system
                Vector3D shipForward = cockpit.WorldMatrix.Forward;
                Vector3D yawForward = Vector3D.Normalize(Vector3D.Reject(shipForward, worldUp));

                if (!yawForward.IsValid() || yawForward.LengthSquared() < 0.1)
                {
                    Vector3D shipRightProjected = Vector3D.Normalize(Vector3D.Reject(cockpit.WorldMatrix.Right, worldUp));
                    if (!shipRightProjected.IsValid() || shipRightProjected.LengthSquared() < 0.1)
                    {
                        yawForward = shipForward;
                    }
                    else
                    {
                        yawForward = Vector3D.Cross(shipRightProjected, worldUp);
                    }
                }

                Vector3D yawRight = Vector3D.Cross(yawForward, worldUp);
                MatrixD yawMatrix = MatrixD.Identity;
                yawMatrix.Forward = yawForward;
                yawMatrix.Right = yawRight;
                yawMatrix.Up = worldUp;

                MatrixD worldToYawPlaneMatrix = MatrixD.Transpose(yawMatrix);
                float pixelsPerMeter = radarRadius / RADAR_RANGE_METERS;

                // Draw all targets using a loop (OPTIMIZED!)
                for (int i = 0; i < targetPositions.Length; i++)
                {
                    Vector3D targetVectorWorld = targetPositions[i] - shooterPosition;
                    Vector3D targetVectorYawLocal = Vector3D.TransformNormal(targetVectorWorld, worldToYawPlaneMatrix);

                    Vector2 targetOffset = new Vector2(
                        (float)targetVectorYawLocal.X * pixelsPerMeter,
                        (float)targetVectorYawLocal.Z * pixelsPerMeter
                    );

                    // Clamp to radar radius
                    float distFromCenter = targetOffset.Length();
                    if (distFromCenter > radarRadius)
                    {
                        if (distFromCenter > 1e-6f)
                            targetOffset /= distFromCenter;
                        targetOffset *= radarRadius;
                    }

                    Vector2 targetRadarPos = radarCenter + targetOffset;

                    // Determine color: primary target (0) gets special color, others are friendly
                    Color targetColor = (i == 0) ? primaryTargetColor : HUD_RADAR_FRIENDLY;

                    if (targetRadarPos.IsValid())
                    {
                        var targetIcon = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = TEXTURE_SQUARE,
                            Position = targetRadarPos,
                            Size = new Vector2(radarRadius * 0.1f, radarRadius * 0.1f),
                            Color = targetColor,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(targetIcon);
                    }
                }
            }


            private float integralError = 0f;
            private float previousError = 0f;
            private float Kp = 1.2f;
            private float Ki = 0.0024f;
            private float Kd = 0.5f;

            private const float MaxPIDOutput = 60f;
            private const float MaxAOA = 36f;



            private void AdjustStabilizers(double aoa, Jet myjet)
            {
                // Ensure cockpit is valid before accessing properties
                if (cockpit == null)
                {
                    // Handle the error appropriately, maybe disable stabilization
                    return;
                }

                Vector2 pitchyaw = cockpit.RotationIndicator;


                AdjustTrim(rightstab, myjet.offset);
                AdjustTrim(leftstab, -myjet.offset);

            }

            private float PIDController(float currentError)
            {
                // *** IMPORTANT: Load Kp, Ki, Kd ONCE in a setup method/constructor ***
                // Reading from CustomData every tick is very inefficient.
                // Assume Kp, Ki, Kd are already loaded into class variables here.

                // Integral term calculation
                integralError += currentError;
                integralError = MathHelper.Clamp(integralError, -200, 200); // Keep clamping

                // Derivative term calculation
                float derivative = currentError - previousError;

                // PID output calculation
                float proportionalTerm = Kp * currentError;
                float integralTerm = Ki * integralError;
                float derivativeTerm = Kd * derivative;
                float pidOutput = proportionalTerm + integralTerm + derivativeTerm;

                // Clamp PID output
                pidOutput = MathHelper.Clamp(pidOutput, -MaxPIDOutput, MaxPIDOutput);

                // Store current error for next derivative calculation
                previousError = currentError;

                // Optional: Log PID terms for debugging
                // Echo($" P: {proportionalTerm:F2}, I: {integralTerm:F2}, D: {derivativeTerm:F2}");

                return pidOutput;
            }



            private void AdjustTrim(IEnumerable<IMyTerminalBlock> stabilizers, float desiredTrim)
            {
                foreach (var item in stabilizers)
                {
                    currentTrim = item.GetValueFloat("Trim");
                    item.SetValue("Trim", desiredTrim);
                }
            }

            // --- Tick() Helper Methods (Refactored for clarity and performance) ---

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

                // World matrix and direction vectors
                worldMatrix = cockpit.WorldMatrix;
                forwardVector = worldMatrix.Forward;
                upVector = worldMatrix.Up;
                Vector3D leftVector = worldMatrix.Left;

                // Gravity checks
                gravity = cockpit.GetNaturalGravity();
                inGravity = gravity.LengthSquared() > 0;
                gravityDirection = inGravity ? Vector3D.Normalize(gravity) : Vector3D.Zero;

                // Pitch and roll calculations (only if in gravity)
                if (inGravity)
                {
                    pitch = Math.Asin(Vector3D.Dot(forwardVector, gravityDirection)) * (180 / Math.PI);
                    roll = Math.Atan2(
                        Vector3D.Dot(leftVector, gravityDirection),
                        Vector3D.Dot(upVector, gravityDirection)
                    ) * (180 / Math.PI);

                    // Normalize roll to [0, 360)
                    if (roll < 0)
                        roll += 360;
                }

                // Speed and Mach calculation
                velocity = cockpit.GetShipSpeed();
                mach = velocity / SEA_LEVEL_SPEED_OF_SOUND;

                // Acceleration and G-force calculations
                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;
                deltaTime = ParentProgram.Runtime.TimeSinceLastRun.TotalSeconds;

                // Fallback to ~1/60th of a second if deltaTime is not valid
                if (deltaTime <= 0)
                    deltaTime = 0.0167;

                Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                double gForces = acceleration.Length() / GRAVITY_ACCELERATION;
                previousVelocity = currentVelocity;

                // Track peak G-forces
                if (gForces > peakGForce)
                    peakGForce = gForces;

                // Calculate heading, altitude, angle of attack
                double heading = CalculateHeading();
                double altitude = GetAltitude();
                double aoa = CalculateAngleOfAttack(
                    cockpit.WorldMatrix.Forward,
                    cockpit.GetShipVelocities().LinearVelocity,
                    upVector
                );

                double velocityKPH = velocity * 3.6; // Convert m/s to KPH

                // Update smoothed values
                UpdateSmoothedValues(velocityKPH, altitude, gForces, aoa, throttlecontrol);
                AdjustStabilizers(aoa, myjet);


            }

            private void UpdateThrottleControl(double throttle, double jumpthrottle)
            {
                if (throttle == 1f)
                {
                    throttlecontrol += 0.01f;
                    if (throttlecontrol > 1f)
                        throttlecontrol = 1f;
                    if (throttlecontrol >= THROTTLE_HYDROGEN_THRESHOLD)
                    {
                        if (!hydrogenswitch)
                        {
                            for (int i = 0; i < tanks.Count; i++)
                            {
                                tanks[i].Enabled = true;
                            }
                            hydrogenswitch = true;
                        }
                    }
                }
                if (throttle == -1f)
                {
                    throttlecontrol -= 0.01f;
                    if (throttlecontrol < 0f)
                        throttlecontrol = 0f;
                    if (throttlecontrol < THROTTLE_HYDROGEN_THRESHOLD)
                    {
                        if (hydrogenswitch)
                        {
                            for (int i = 0; i < tanks.Count; i++)
                            {
                                tanks[i].Enabled = false;
                            }
                            hydrogenswitch = false;
                        }
                    }
                }

                // Airbrake control
                if (jumpthrottle == 1f)
                {
                    for (int i = 0; i < airbreaks.Count; i++)
                    {
                        airbreaks[i].OpenDoor();
                    }
                }
                if (jumpthrottle == 0f)
                {
                    for (int i = 0; i < airbreaks.Count; i++)
                    {
                        airbreaks[i].CloseDoor();
                    }
                }
                if (jumpthrottle < -0.01f)
                {
                    myjet.manualfire = !myjet.manualfire;
                }

                // Manual fire mode
                if (myjet.manualfire)
                {
                    for (int i = 0; i < myjet._gatlings.Count; i++)
                    {
                        myjet._gatlings[i].Enabled = true;
                    }
                }

                // Apply throttle to thrusters
                for (int i = 0; i < thrusters.Count; i++)
                {
                    float scaledThrottle;

                    if (throttlecontrol <= THROTTLE_HYDROGEN_THRESHOLD)
                    {
                        // Scale throttlecontrol to fit the range 0.0 to 1.0
                        scaledThrottle = throttlecontrol / THROTTLE_HYDROGEN_THRESHOLD;
                    }
                    else
                    {
                        // Cap it at 1.0 when throttlecontrol is over threshold
                        scaledThrottle = 1.0f;
                    }

                    thrusters[i].ThrustOverridePercentage = scaledThrottle;
                }
            }

            public override void Tick()
            {
                // Validation
                if (!ValidateHUDState())
                    return;

                // Get throttle inputs
                double throttle = cockpit.MoveIndicator.Z * -1;
                double jumpthrottle = cockpit.MoveIndicator.Y;

                // Update all flight data
                MatrixD worldMatrix;
                Vector3D forwardVector, upVector, gravity, gravityDirection;
                bool inGravity;
                UpdateFlightData(out worldMatrix, out forwardVector, out upVector,
                    out gravity, out inGravity, out gravityDirection);

                // Update throttle and control systems
                UpdateThrottleControl(throttle, jumpthrottle);

                // Render the HUD
                double heading = CalculateHeading();
                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;
                RenderHUD(heading, gravityDirection, currentVelocity, worldMatrix);
            }

            // NOTE: The remaining HUD drawing methods are included in the full file
            // For brevity, this shows the structure. The actual file contains all methods.

            private void RenderHUD(double heading, Vector3D gravityDirection, Vector3D currentVelocity, MatrixD worldMatrix)
            {
                // Cache frequently used values
                hudCenter = hud.SurfaceSize / 2f;
                viewportMinDim = Math.Min(hud.SurfaceSize.X, hud.SurfaceSize.Y);

                float centerX = hudCenter.X;
                float centerY = hudCenter.Y;
                float pixelsPerDegree = hud.SurfaceSize.Y / 16f; // F18-like scaling

                Vector3D shooterPosition = cockpit.GetPosition();
                double altitude = GetAltitude();

                using (var frame = hud.DrawFrame())
                {
                    DrawArtificialHorizon(frame, (float)pitch, (float)roll, centerX, centerY, pixelsPerDegree);
                    DrawBankAngleMarkers(frame, centerX, centerY, (float)roll, pixelsPerDegree);
                    DrawFlightPathMarker(frame, currentVelocity, worldMatrix, roll, centerX, centerY, pixelsPerDegree);

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

                    Vector3D[] radarTargetPositions = new Vector3D[myjet.targetSlots.Length];
                    int radarTargetCount = 0;
                    if (myjet.targetSlots[myjet.activeSlotIndex].IsOccupied)
                        radarTargetPositions[radarTargetCount++] = myjet.targetSlots[myjet.activeSlotIndex].Position;
                    for (int i = 0; i < myjet.targetSlots.Length; i++)
                    {
                        if (i != myjet.activeSlotIndex && myjet.targetSlots[i].IsOccupied)
                            radarTargetPositions[radarTargetCount++] = myjet.targetSlots[i].Position;
                    }
                    Array.Resize(ref radarTargetPositions, radarTargetCount);

                    DrawTopDownRadarOptimized(frame, cockpit, hud, radarTargetPositions, Color.White, HUD_PRIMARY, HUD_EMPHASIS, HUD_WARNING);
                    DrawRadarSweepLine(frame, radarCenter, RADAR_BOX_SIZE_PX / 2f);
                    DrawRWRThreatCones(frame, cockpit, radarCenter, RADAR_BOX_SIZE_PX / 2f);

                    if (myjet.targetSlots[myjet.activeSlotIndex].IsOccupied)
                    {
                        Vector3D activeTargetPos = myjet.targetSlots[myjet.activeSlotIndex].Position;
                        Vector3D activeTargetVel = myjet.targetSlots[myjet.activeSlotIndex].Velocity;
                        double muzzleVelocity = 910;
                        double range = Vector3D.Distance(shooterPosition, activeTargetPos);

                        Vector3D interceptPoint;
                        double timeToIntercept;
                        bool hasIntercept = CalculateInterceptPointIterative(shooterPosition, currentVelocity, muzzleVelocity, activeTargetPos, activeTargetVel, gravityDirection, INTERCEPT_ITERATIONS, out interceptPoint, out timeToIntercept);

                        if (hasIntercept)
                        {
                            MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                            Vector3D directionToIntercept = interceptPoint - shooterPosition;
                            Vector3D localDirectionToIntercept = Vector3D.TransformNormal(directionToIntercept, worldToCockpitMatrix);

                            bool isAimingAtPip = false;
                            if (localDirectionToIntercept.Z < 0)
                            {
                                Vector2 center = surfaceSize / 2f;
                                float scaleX = surfaceSize.X / 0.3434f;
                                float scaleY = surfaceSize.Y / 0.31f;
                                float screenX = center.X + (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z) * scaleX;
                                float screenY = center.Y + (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z) * scaleY;
                                Vector2 pipScreenPos = new Vector2(screenX, screenY);
                                float pipRadius = viewportMinDim * 0.05f;
                                float distanceToPip = Vector2.Distance(center, pipScreenPos);
                                isAimingAtPip = distanceToPip <= pipRadius;
                            }

                            DrawGunFunnel(frame, cockpit, hud, interceptPoint, shooterPosition, range, isAimingAtPip);
                            DrawLeadingPip(frame, cockpit, hud, activeTargetPos, activeTargetVel, shooterPosition, currentVelocity, muzzleVelocity, gravityDirection, HUD_WARNING, HUD_EMPHASIS, Color.HotPink, HUD_INFO);
                            DrawTargetBrackets(frame, cockpit, hud, activeTargetPos, activeTargetVel, shooterPosition, currentVelocity);
                        }
                        DrawBreakawayWarning(frame, altitude, currentVelocity, activeTargetPos, shooterPosition);
                    }
                    DrawFormationGhosts(frame, cockpit, hud);
                }

                RenderWeaponScreen(heading, altitude, currentVelocity, shooterPosition);
            }

            // Include the rest of the drawing methods here (abbreviated for this example)
            // The actual implementation includes all methods from the original file

            private void DrawGForceIndicator(MySpriteDrawFrame frame, double gForces, double peakGForce)
            {
                const float PADDING = 10f;
                const float TEXT_SCALE = 0.8f;
                const float LINE_HEIGHT = 20f;

                string gForceText = $"G: {gForces:F1}";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = gForceText,
                    Position = new Vector2(PADDING, hud.SurfaceSize.Y - PADDING - LINE_HEIGHT),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                });

                string peakGText = $"Max G: {peakGForce:F1}";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = peakGText,
                    Position = new Vector2(PADDING, hud.SurfaceSize.Y - PADDING - LINE_HEIGHT * 2),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                });
            }

            public struct LabelValue
            {
                public string Label;
                public double Value;

                public LabelValue(string label, double value)
                {
                    Label = label;
                    Value = value;
                }
            }

            struct AltitudeTimePoint
            {
                public readonly TimeSpan Time;
                public readonly double Altitude;

                public AltitudeTimePoint(TimeSpan time, double altitude)
                {
                    Time = time;
                    Altitude = altitude;
                }
            }

            private TimeSpan historyDuration = TimeSpan.FromSeconds(1);
            private const float TAPE_HEIGHT_PIXELS = 200f;
            private const float ALTITUDE_UNITS_PER_TAPE_HEIGHT = 1000f;
            private const float PIXELS_PER_ALTITUDE_UNIT = TAPE_HEIGHT_PIXELS / ALTITUDE_UNITS_PER_TAPE_HEIGHT;
            private const float TICK_INTERVAL = 100f;
            private const float MAJOR_TICK_INTERVAL = 500f;
            private const string FONT = "Monospace";
            const float SPEED_MAJOR_TICK_INTERVAL = 50f;
            const float SPEED_TICK_INTERVAL = 25f;
            const float SPEED_KPH_UNITS_PER_TAPE_HEIGHT = 600f;

            // === FULL IMPLEMENTATIONS OF DRAWING METHODS ===

            private void DrawLeftInfoBox(
                MySpriteDrawFrame frame,
                double airspeed,
                float centerX,
                float centerY,
                double pixelsPerDegree,
                params LabelValue[] extraValues
            )
            {
                const float Y_OFFSET_PER_VALUE = 30f;
                const float X_OFFSET_FACTOR = 0.75f;
                const float Y_OFFSET_FACTOR = 0.5f;
                const float LABEL_COLUMN_OFFSET = 40f;
                const float NUMBER_COLUMN_OFFSET = 40f;
                const float TEXT_SCALE = 0.75f;

                float xoffset = centerX - centerX * X_OFFSET_FACTOR;
                float yoffset = centerY - centerY * Y_OFFSET_FACTOR;
                float labelColumnX = xoffset - LABEL_COLUMN_OFFSET;
                float numberColumnX = xoffset + NUMBER_COLUMN_OFFSET;

                for (int i = 0; i < extraValues.Length; i++)
                {
                    string labelText = extraValues[i].Label;
                    double numericValue = extraValues[i].Value;

                    var labelSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = labelText,
                        Position = new Vector2(labelColumnX, yoffset + i * Y_OFFSET_PER_VALUE),
                        RotationOrScale = TEXT_SCALE,
                        Color = HUD_PRIMARY,
                        Alignment = TextAlignment.LEFT,
                        FontId = "White"
                    };
                    frame.Add(labelSprite);

                    var valueSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = numericValue.ToString("F1"),
                        Position = new Vector2(numberColumnX, yoffset + i * Y_OFFSET_PER_VALUE),
                        RotationOrScale = TEXT_SCALE,
                        Color = HUD_PRIMARY,
                        Alignment = TextAlignment.RIGHT,
                        FontId = "White"
                    };
                    frame.Add(valueSprite);
                }
            }

            private void UpdateAltitudeHistory(double currentAltitude, TimeSpan currentTime)
            {
                altitudeHistory.Enqueue(new AltitudeTimePoint(currentTime, currentAltitude));
                while (altitudeHistory.Count > 1 && currentTime - altitudeHistory.Peek().Time > historyDuration)
                {
                    altitudeHistory.Dequeue();
                }
            }

            private double CalculateVerticalVelocity(double currentAltitude, TimeSpan currentTime)
            {
                if (altitudeHistory.Count < 2)
                {
                    return 0;
                }

                AltitudeTimePoint oldestData = altitudeHistory.Peek();
                TimeSpan oldestTime = oldestData.Time;
                double oldestAltitude = oldestData.Altitude;

                TimeSpan timeDifference = currentTime - oldestTime;
                if (timeDifference.TotalSeconds < 0.01)
                {
                    return 0;
                }

                double altitudeChange = currentAltitude - oldestAltitude;
                double vvi = altitudeChange / timeDifference.TotalSeconds;

                return vvi;
            }

            private void DrawAltitudeIndicatorF18Style(MySpriteDrawFrame frame, double currentAltitude, TimeSpan currentTime)
            {
                UpdateAltitudeHistory(currentAltitude, currentTime);
                double verticalVelocity = CalculateVerticalVelocity(currentAltitude, currentTime);

                float screenWidth = hud.SurfaceSize.X;
                float screenHeight = hud.SurfaceSize.Y;
                float centerY = screenHeight / 2f;

                float tapeRightMargin = 10f;
                float tapeNumberMargin = 10f;
                float tapeWidth = 2f;
                float tickLength = 10f;
                float majorTickLength = 15f;

                float tapeLineX = screenWidth - tapeRightMargin;
                float digitalAltBoxWidth = 80f;
                float digitalAltBoxHeight = 30f;
                float digitalAltBoxX = tapeLineX - tapeNumberMargin - digitalAltBoxWidth;

                var tapeLine = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(tapeLineX, centerY),
                    Size = new Vector2(tapeWidth, TAPE_HEIGHT_PIXELS),
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(tapeLine);

                float tapeTopAlt = (float)currentAltitude + (ALTITUDE_UNITS_PER_TAPE_HEIGHT / 2f);
                float tapeBottomAlt = (float)currentAltitude - (ALTITUDE_UNITS_PER_TAPE_HEIGHT / 2f);

                float startTickAlt = (float)(Math.Floor(tapeBottomAlt / TICK_INTERVAL) * TICK_INTERVAL);
                if (startTickAlt < tapeBottomAlt)
                    startTickAlt += TICK_INTERVAL;

                for (float altMark = startTickAlt; altMark <= tapeTopAlt + (TICK_INTERVAL * 0.5f); altMark += TICK_INTERVAL)
                {
                    float yOffset = (float)(currentAltitude - altMark) * PIXELS_PER_ALTITUDE_UNIT;
                    float yPos = centerY + yOffset;

                    float tapeTopY = centerY - TAPE_HEIGHT_PIXELS / 2f;
                    float tapeBottomY = centerY + TAPE_HEIGHT_PIXELS / 2f;

                    if (yPos >= tapeTopY - 1f && yPos <= tapeBottomY + 1f)
                    {
                        bool isMajorTick = Math.Abs(altMark % MAJOR_TICK_INTERVAL) < (TICK_INTERVAL * 0.1f);
                        float currentTickLength = isMajorTick ? majorTickLength : tickLength;
                        if (altMark >= 0)
                        {
                            var tickMark = new MySprite()
                            {
                                Type = SpriteType.TEXTURE,
                                Data = "SquareSimple",
                                Position = new Vector2(tapeLineX - currentTickLength / 2f, yPos),
                                Size = new Vector2(currentTickLength, tapeWidth),
                                Color = HUD_PRIMARY,
                                Alignment = TextAlignment.CENTER
                            };
                            frame.Add(tickMark);
                        }

                        if (isMajorTick)
                        {
                            string altText = altMark.ToString("F0");
                            var numberLabel = new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = altText,
                                Position = new Vector2(tapeLineX - currentTickLength - tapeNumberMargin, yPos - 7.5f),
                                RotationOrScale = 0.5f,
                                Color = HUD_PRIMARY,
                                Alignment = TextAlignment.RIGHT,
                                FontId = FONT
                            };
                            frame.Add(numberLabel);
                        }
                    }
                }

                DrawRectangleOutline(frame, digitalAltBoxX - 20, centerY - digitalAltBoxHeight - 225 / 2f, digitalAltBoxWidth, digitalAltBoxHeight, 1f, HUD_PRIMARY);

                string currentAltitudeText = currentAltitude.ToString("F0");
                var altitudeLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = currentAltitudeText,
                    Position = new Vector2(digitalAltBoxX - 20 + digitalAltBoxWidth / 2f, centerY - 140),
                    RotationOrScale = 0.8f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER,
                    FontId = FONT
                };
                frame.Add(altitudeLabel);

                var caret = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "<",
                    Position = new Vector2(digitalAltBoxX + digitalAltBoxWidth + 15f, centerY - 7.5f),
                    RotationOrScale = 0.5f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = FONT
                };
                frame.Add(caret);
            }

            private void DrawSpeedIndicatorF18StyleKph(MySpriteDrawFrame frame, double currentSpeedKph)
            {
                currentSpeedKph = Math.Max(0, currentSpeedKph);
                const float PIXELS_PER_SPEED_UNIT = 800 / SPEED_KPH_UNITS_PER_TAPE_HEIGHT;

                float screenWidth = hud.SurfaceSize.X;
                float screenHeight = hud.SurfaceSize.Y;
                float centerY = screenHeight / 2.25f;

                float tapeLeftMargin = 10f;
                float tapeNumberMargin = 10f;
                float tapeWidth = 2f;
                float tickLength = 10f;
                float majorTickLength = 15f;

                float tapeLineX = tapeLeftMargin;
                float digitalSpeedBoxWidth = 80f;
                float digitalSpeedBoxHeight = 30f;
                float digitalSpeedBoxX = tapeLineX + tapeNumberMargin;

                var tapeLine = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(tapeLineX, centerY),
                    Size = new Vector2(tapeWidth, TAPE_HEIGHT_PIXELS),
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(tapeLine);

                float tapeTopSpeed = (float)currentSpeedKph + (SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f);
                float tapeBottomSpeed = (float)currentSpeedKph - (SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f);
                tapeBottomSpeed = Math.Max(0, tapeBottomSpeed);

                float startTickSpeed = (float)(Math.Floor(tapeBottomSpeed / SPEED_TICK_INTERVAL) * SPEED_TICK_INTERVAL);
                if (startTickSpeed < tapeBottomSpeed)
                    startTickSpeed += SPEED_TICK_INTERVAL;
                startTickSpeed = Math.Max(0, startTickSpeed);

                for (float speedMark = startTickSpeed; speedMark <= tapeTopSpeed + (SPEED_TICK_INTERVAL * 0.5f); speedMark += SPEED_TICK_INTERVAL)
                {
                    if (SPEED_TICK_INTERVAL <= 0) break;
                    if (speedMark < 0) continue;

                    float yOffset = (float)(currentSpeedKph - speedMark) * PIXELS_PER_SPEED_UNIT;
                    float yPos = centerY + yOffset;

                    float tapeTopY = centerY - TAPE_HEIGHT_PIXELS / 2f;
                    float tapeBottomY = centerY + TAPE_HEIGHT_PIXELS / 2f;

                    if (yPos >= tapeTopY - 1f && yPos <= tapeBottomY + 1f)
                    {
                        bool isMajorTick = Math.Abs(speedMark % SPEED_MAJOR_TICK_INTERVAL) < (SPEED_TICK_INTERVAL * 0.1f);
                        if (Math.Abs(speedMark) < (SPEED_TICK_INTERVAL * 0.1f)) isMajorTick = true;

                        float currentTickLength = isMajorTick ? majorTickLength : tickLength;

                        var tickMark = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(tapeLineX + currentTickLength / 2f, yPos),
                            Size = new Vector2(currentTickLength, tapeWidth),
                            Color = HUD_PRIMARY,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(tickMark);

                        if (isMajorTick)
                        {
                            string speedText = speedMark.ToString("F0");
                            var numberLabel = new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = speedText,
                                Position = new Vector2(tapeLineX + currentTickLength + tapeNumberMargin, yPos - 7.5f),
                                RotationOrScale = 0.5f,
                                Color = HUD_PRIMARY,
                                Alignment = TextAlignment.LEFT,
                                FontId = FONT
                            };
                            frame.Add(numberLabel);
                        }
                    }
                }

                DrawRectangleOutline(frame, digitalSpeedBoxX, centerY - digitalSpeedBoxHeight / 2f - 130, digitalSpeedBoxWidth, digitalSpeedBoxHeight, 1f, HUD_PRIMARY);

                string currentSpeedText = currentSpeedKph.ToString("F0");
                var speedLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = currentSpeedText,
                    Position = new Vector2(digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130 - digitalSpeedBoxHeight / 2f),
                    RotationOrScale = 0.8f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER,
                    FontId = FONT
                };
                frame.Add(speedLabel);

                var caret = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = ">",
                    Position = new Vector2(digitalSpeedBoxX - 10f, centerY - 7.5f),
                    RotationOrScale = 0.5f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.RIGHT,
                    FontId = FONT
                };
                frame.Add(caret);
            }

            private void DrawRectangleOutline(MySpriteDrawFrame frame, float x, float y, float width, float height, float lineWidth, Color color)
            {
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x + width / 2f, y), Size = new Vector2(width, lineWidth), Color = color, Alignment = TextAlignment.CENTER });
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x + width / 2f, y + height), Size = new Vector2(width, lineWidth), Color = color, Alignment = TextAlignment.CENTER });
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x, y + height / 2f), Size = new Vector2(lineWidth, height), Color = color, Alignment = TextAlignment.CENTER });
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x + width, y + height / 2f), Size = new Vector2(lineWidth, height), Color = color, Alignment = TextAlignment.CENTER });
            }

            private void UpdateSmoothedValues(
                double velocityKPH,
                double altitude,
                double gForces,
                double aoa,
                double throttle
            )
            {
                if (velocityHistory.Count >= SMOOTHING_WINDOW_SIZE)
                {
                    velocitySum -= velocityHistory.Dequeue();
                }
                velocityHistory.Enqueue(velocityKPH);
                velocitySum += velocityKPH;
                smoothedVelocity = velocitySum / velocityHistory.Count;

                altitudeHistory.Enqueue(new AltitudeTimePoint(totalElapsedTime, altitude));
                if (altitudeHistory.Count > 0)
                {
                    double altSum = 0;
                    var points = altitudeHistory.ToArray();
                    for (int i = 0; i < points.Length; i++)
                    {
                        altSum += points[i].Altitude;
                    }
                    smoothedAltitude = altSum / altitudeHistory.Count;
                }

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

            private void DrawFlightInfo(
                MySpriteDrawFrame frame,
                double velocityKPH,
                double gForces,
                double heading,
                double altitude,
                double aoa,
                double throttle,
                double mach
            )
            {
                float padding = 0f;
                float infoX = hud.SurfaceSize.X - hud.SurfaceSize.X / 2;
                float infoY = 60f;
                float boxPadding = 5f;
                float textHeight = 30f;

                var surface = hud;
                var textScale = 0.75f;

                Vector2 maxTextSize = surface.MeasureStringInPixels(
                    new StringBuilder("SPD: 00.0 kph"),
                    "White",
                    textScale
                );
                float maxWidth = maxTextSize.X + boxPadding * 2;

                DrawThrottleBarWithBox(
                    frame,
                    surface,
                    (float)throttle / 100,
                    new Vector2(5, hud.SurfaceSize.Y - textHeight - 140),
                    80,
                    8,
                    textScale
                );
            }

            private void DrawThrottleBarWithBox(
                MySpriteDrawFrame frame,
                IMyTextSurface surface,
                float throttle,
                Vector2 position,
                float boxPadding,
                float maxWidth,
                float barHeight,
                Color barColor = default(Color),
                Color boxColor = default(Color),
                float lineThickness = 2f
            )
            {
                barColor = barColor == default(Color) ? Color.Lime : barColor;
                boxColor = boxColor == default(Color) ? Color.Lime : boxColor;

                Vector2 boxSize = new Vector2(
                    maxWidth + boxPadding * 1f,
                    barHeight + boxPadding * 1.5f
                );
                boxSize.X = boxSize.X / 4;

                Vector2 boxTopLeft = position;

                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(0, lineThickness / 2),
                        Size = new Vector2(boxSize.X, lineThickness),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(0, boxSize.Y - lineThickness / 2),
                        Size = new Vector2(boxSize.X, lineThickness),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(lineThickness / 2, boxSize.Y / 2),
                        Size = new Vector2(lineThickness, boxSize.Y),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(boxSize.X - lineThickness / 2, boxSize.Y / 2),
                        Size = new Vector2(lineThickness, boxSize.Y),
                        Color = boxColor
                    }
                );

                float filledHeight = barHeight * throttle;
                Vector2 filledSize = new Vector2(maxWidth * 100, filledHeight * boxSize.Y * 1.25f);
                barColor = throttle > THROTTLE_HYDROGEN_THRESHOLD ? HUD_EMPHASIS : HUD_PRIMARY;

                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(0, (boxSize.Y - boxPadding / 33 - lineThickness / 2 - filledSize.Y / 2) * 1.025f),
                        Size = new Vector2(boxSize.X, filledSize.Y * 1.05f),
                        Color = barColor
                    }
                );
            }

            private void DrawFlightPathMarker(
                MySpriteDrawFrame frame,
                Vector3D currentVelocity,
                MatrixD worldMatrix,
                double roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                const double DegToRad = Math.PI / 180.0;
                const float MarkerSize = 20f;
                const float WingLength = 15f;
                const float WingThickness = 2f;
                const float WingOffsetX = 10f;

                Vector3D velocityDirection = Vector3D.Normalize(currentVelocity);

                Vector3D localVelocity = Vector3D.TransformNormal(
                    velocityDirection,
                    MatrixD.Transpose(worldMatrix)
                );

                double velocityYaw = Math.Atan2(localVelocity.X, -localVelocity.Z) * 180.0 / Math.PI;
                double velocityPitch = Math.Atan2(localVelocity.Y, -localVelocity.Z) * 180.0 / Math.PI;

                float rollRad = (float)(roll * DegToRad);

                Vector2 markerOffset = new Vector2(
                    (float)(-velocityYaw * pixelsPerDegree),
                    (float)(velocityPitch * pixelsPerDegree)
                );

                Vector2 rotatedOffset = RotatePoint(markerOffset, Vector2.Zero, -rollRad);
                Vector2 markerPosition = new Vector2(centerX, centerY) + rotatedOffset;

                var marker = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = markerPosition,
                    Size = new Vector2(MarkerSize, MarkerSize),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(marker);

                Vector2 leftWingOffset = new Vector2(-WingLength / 2 - WingOffsetX, 0f);
                Vector2 rightWingOffset = new Vector2(WingLength / 2 + WingOffsetX, 0f);

                Vector2 rotatedLeftWingOffset = RotatePoint(leftWingOffset, Vector2.Zero, -rollRad);
                Vector2 rotatedRightWingOffset = RotatePoint(rightWingOffset, Vector2.Zero, -rollRad);

                var leftWing = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = markerPosition + rotatedLeftWingOffset,
                    Size = new Vector2(WingLength, WingThickness),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = -rollRad
                };
                frame.Add(leftWing);

                var rightWing = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = markerPosition + rotatedRightWingOffset,
                    Size = new Vector2(WingLength, WingThickness),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = -rollRad
                };
                frame.Add(rightWing);
            }

            private void DrawTargetBrackets(
                MySpriteDrawFrame frame,
                IMyCockpit cockpit,
                IMyTextSurface hud,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D shooterPosition,
                Vector3D shooterVelocity
            )
            {
                if (cockpit == null || hud == null) return;

                double range = Vector3D.Distance(shooterPosition, targetPosition);

                Vector3D relativeVelocity = targetVelocity - shooterVelocity;
                Vector3D directionToTarget = Vector3D.Normalize(targetPosition - shooterPosition);
                double closureRate = Vector3D.Dot(relativeVelocity, directionToTarget);

                Vector3D targetForward = Vector3D.Normalize(targetVelocity);
                Vector3D toShooter = Vector3D.Normalize(shooterPosition - targetPosition);
                double aspectAngle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(targetForward, toShooter), -1, 1)) * (180.0 / Math.PI);

                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector3D directionToTargetLocal = Vector3D.TransformNormal(targetPosition - shooterPosition, worldToCockpitMatrix);

                if (Math.Abs(directionToTargetLocal.Z) < MIN_Z_FOR_PROJECTION)
                    directionToTargetLocal.Z = -MIN_Z_FOR_PROJECTION;

                if (directionToTargetLocal.Z >= 0) return;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                const float COCKPIT_FOV_SCALE_X = 0.3434f;
                const float COCKPIT_FOV_SCALE_Y = 0.31f;
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;
                float screenX = center.X + (float)(directionToTargetLocal.X / -directionToTargetLocal.Z) * scaleX;
                float screenY = center.Y + (float)(-directionToTargetLocal.Y / -directionToTargetLocal.Z) * scaleY;
                Vector2 targetScreenPos = new Vector2(screenX, screenY);

                bool isOnScreen = targetScreenPos.X >= 0 && targetScreenPos.X <= surfaceSize.X &&
                                  targetScreenPos.Y >= 0 && targetScreenPos.Y <= surfaceSize.Y;

                if (!isOnScreen) return;

                float bracketSize = MathHelper.Clamp((float)(3000.0 / range), 20f, 80f);
                float bracketThickness = 2f;
                float cornerLength = bracketSize * 0.3f;

                Color bracketColor = closureRate < -10 ? HUD_WARNING :
                                   closureRate > 10 ? HUD_EMPHASIS : HUD_PRIMARY;

                AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2 + cornerLength, -bracketSize/2),
                                    bracketThickness, bracketColor);
                AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2 + cornerLength),
                                    bracketThickness, bracketColor);

                AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2 - cornerLength, -bracketSize/2),
                                    bracketThickness, bracketColor);
                AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2 + cornerLength),
                                    bracketThickness, bracketColor);

                AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2 + cornerLength, bracketSize/2),
                                    bracketThickness, bracketColor);
                AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2 - cornerLength),
                                    bracketThickness, bracketColor);

                AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2 - cornerLength, bracketSize/2),
                                    bracketThickness, bracketColor);
                AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2, bracketSize/2 - cornerLength),
                                    bracketThickness, bracketColor);

                float textY = targetScreenPos.Y + bracketSize/2 + 5f;
                float textScale = 0.5f;

                string rangeText = range >= 1000 ? $"{range/1000:F1}km" : $"{range:F0}m";
                var rangeSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = rangeText,
                    Position = new Vector2(targetScreenPos.X, textY),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                };
                frame.Add(rangeSprite);

                string closureText = $"Vc:{closureRate:+0;-0}";
                var closureSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = closureText,
                    Position = new Vector2(targetScreenPos.X, textY + 12f),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                };
                frame.Add(closureSprite);

                string aspectText = $"AA:{aspectAngle:F0}°";
                var aspectSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = aspectText,
                    Position = new Vector2(targetScreenPos.X, textY + 24f),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                };
                frame.Add(aspectSprite);
            }

            private void DrawWeaponStatusPanelToScreen(MySpriteDrawFrame frame, Jet myjet, float panelX, float panelY, float panelWidth)
            {
                const float PANEL_HEIGHT = 90f;
                const float TEXT_SCALE = 0.7f;
                const float LINE_HEIGHT = 18f;

                DrawRectangleOutline(frame, panelX, panelY, panelWidth, PANEL_HEIGHT, 2f, HUD_PRIMARY);

                float textX = panelX + 10f;
                float textY = panelY + 10f;

                string weaponText = "WPN: GAU-8";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = weaponText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                textY += LINE_HEIGHT;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "RND: \u221E",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                textY += LINE_HEIGHT;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "MSL:",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                float baySquareSize = 10f;
                float bayStartX = textX + 35f;
                float bayY = textY + 2f;

                int maxBays = Math.Min(8, myjet._bays.Count);
                for (int i = 0; i < maxBays; i++)
                {
                    bool bayLoaded = myjet._bays[i].IsConnected;
                    Color bayColor = bayLoaded ? HUD_PRIMARY : HUD_WARNING;

                    float bayX = bayStartX + (i % 4) * (baySquareSize + 3f);
                    float currentBayY = bayY + (i / 4) * (baySquareSize + 3f);

                    if (bayLoaded)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(bayX + baySquareSize/2, currentBayY + baySquareSize/2),
                            Size = new Vector2(baySquareSize, baySquareSize),
                            Color = bayColor,
                            Alignment = TextAlignment.CENTER
                        });
                    }
                    else
                    {
                        DrawRectangleOutline(frame, bayX, currentBayY, baySquareSize, baySquareSize, 1f, bayColor);
                    }
                }

                textY += LINE_HEIGHT * 2;
                bool radarTracking = myjet.targetSlots[myjet.activeSlotIndex].IsOccupied;
                string podStatus = radarTracking ? "LOCK \u2713" : "----";
                Color podColor = radarTracking ? HUD_PRIMARY : HUD_WARNING;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"POD: {podStatus}",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = podColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });
            }

            private void DrawGunFunnel(
                MySpriteDrawFrame frame,
                IMyCockpit cockpit,
                IMyTextSurface hud,
                Vector3D interceptPoint,
                Vector3D shooterPosition,
                double range,
                bool isAimingAtPip
            )
            {
                if (cockpit == null || hud == null) return;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                float funnelWidthFactor = (float)MathHelper.Clamp(range / 2000.0, 0.05, 0.3);
                float funnelBaseWidth = surfaceSize.X * funnelWidthFactor;

                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector3D directionToIntercept = interceptPoint - shooterPosition;
                Vector3D localDirectionToIntercept = Vector3D.TransformNormal(directionToIntercept, worldToCockpitMatrix);

                if (localDirectionToIntercept.Z >= 0) return;

                if (Math.Abs(localDirectionToIntercept.Z) < MIN_Z_FOR_PROJECTION)
                    localDirectionToIntercept.Z = -MIN_Z_FOR_PROJECTION;

                const float COCKPIT_FOV_SCALE_X = 0.3434f;
                const float COCKPIT_FOV_SCALE_Y = 0.31f;
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;
                float screenX = center.X + (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z) * scaleX;
                float screenY = center.Y + (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z) * scaleY;
                Vector2 pipScreenPos = new Vector2(screenX, screenY);

                Color funnelColor = new Color(HUD_PRIMARY, 0.3f);
                float lineThickness = 1f;

                Vector2[] edgePoints = new Vector2[]
                {
                    new Vector2(center.X - funnelBaseWidth/2, 0),
                    new Vector2(center.X + funnelBaseWidth/2, 0),
                    new Vector2(center.X + funnelBaseWidth/2, surfaceSize.Y),
                    new Vector2(center.X - funnelBaseWidth/2, surfaceSize.Y)
                };

                foreach (var edgePoint in edgePoints)
                {
                    AddLineSprite(frame, edgePoint, pipScreenPos, lineThickness, funnelColor);
                }

                if (isAimingAtPip && range < 2500)
                {
                    string cueText = range < 1500 ? "SHOOT" : "IN RANGE";
                    Color cueColor = range < 1500 ? HUD_WARNING : HUD_EMPHASIS;

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = cueText,
                        Position = new Vector2(center.X, center.Y - 60f),
                        RotationOrScale = 1.0f,
                        Color = cueColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });
                }
            }

            private void DrawMultiTargetPanelToScreen(MySpriteDrawFrame frame, Vector3D[] targetPositions, Vector3D shooterPosition, float panelX, float startY, float panelWidth)
            {
                if (targetPositions == null || targetPositions.Length < 1) return;

                const float LINE_HEIGHT = 22f;
                const float TEXT_SCALE = 0.7f;

                float textX = panelX + 10f;
                float textY = startY;

                for (int i = 0; i < Math.Min(5, targetPositions.Length); i++)
                {
                    double range = Vector3D.Distance(shooterPosition, targetPositions[i]);
                    bool isPrimary = (i == 0);

                    Color targetColor = isPrimary ? HUD_WARNING : HUD_RADAR_FRIENDLY;
                    string circleSymbol = isPrimary ? "\u25C9" : "\u25CB";
                    string targetLabel = isPrimary ? "PRI" : $"T{i}";

                    string targetText = $"{circleSymbol} {targetLabel}";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = targetText,
                        Position = new Vector2(textX, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "Monospace"
                    });

                    float barMaxWidth = 80f;
                    float barHeight = 8f;
                    float barX = textX + 50f;
                    float barWidth = MathHelper.Clamp((float)(range / 15000.0 * barMaxWidth), 2f, barMaxWidth);

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(barX, textY + 5f),
                        Size = new Vector2(barWidth, barHeight),
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT
                    });

                    string rangeText = range >= 1000 ? $"{range/1000:F1}km" : $"{range:F0}m";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = rangeText,
                        Position = new Vector2(barX + barMaxWidth + 10f, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "Monospace"
                    });

                    textY += LINE_HEIGHT;
                }
            }

            private void DrawAOAIndexer(MySpriteDrawFrame frame, double aoa, Vector3D acceleration, double velocity)
            {
                const float INDEXER_X = 100f;
                float indexerY = hud.SurfaceSize.Y / 2f;
                const float SYMBOL_SIZE = 18f;

                const double OPTIMAL_AOA_MIN = 8.0;
                const double OPTIMAL_AOA_MAX = 15.0;

                Color indexerColor;
                string spriteType;

                if (aoa < OPTIMAL_AOA_MIN)
                {
                    indexerColor = HUD_PRIMARY;
                    spriteType = "Triangle";

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = spriteType,
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE, SYMBOL_SIZE),
                        RotationOrScale = 0f,
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
                else if (aoa > OPTIMAL_AOA_MAX)
                {
                    indexerColor = HUD_WARNING;
                    spriteType = "Triangle";

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = spriteType,
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE, SYMBOL_SIZE),
                        RotationOrScale = MathHelper.Pi,
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
                else
                {
                    indexerColor = HUD_EMPHASIS;

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE * 0.8f, SYMBOL_SIZE * 0.8f),
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
                }

                double energyRate = acceleration.Length();
                string energySymbol = energyRate > 5 ? "+" : energyRate < -5 ? "-" : "=";
                Color energyColor = energyRate > 5 ? HUD_PRIMARY : energyRate < -5 ? HUD_WARNING : HUD_EMPHASIS;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"E{energySymbol}",
                    Position = new Vector2(INDEXER_X, indexerY + 25f),
                    RotationOrScale = 0.5f,
                    Color = energyColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                });
            }

            private void DrawBankAngleMarkers(MySpriteDrawFrame frame, float centerX, float centerY, float roll, float pixelsPerDegree)
            {
                int[] bankAngles = new int[] { 15, 30, 45, 60, -15, -30, -45, -60 };
                float horizonRadius = pixelsPerDegree * 20f;

                float rollRad = MathHelper.ToRadians(-roll);
                float cosRoll = (float)Math.Cos(rollRad);
                float sinRoll = (float)Math.Sin(rollRad);

                foreach (int angle in bankAngles)
                {
                    float angleRad = MathHelper.ToRadians(angle);
                    Vector2 tickPos = new Vector2((float)Math.Sin(angleRad) * horizonRadius, -(float)Math.Cos(angleRad) * horizonRadius);

                    Vector2 rotatedTick = new Vector2(
                        tickPos.X * cosRoll - tickPos.Y * sinRoll,
                        tickPos.X * sinRoll + tickPos.Y * cosRoll
                    );

                    Vector2 finalPos = new Vector2(centerX, centerY) + rotatedTick;

                    bool isMajor = (Math.Abs(angle) % 30 == 0);
                    float tickLength = isMajor ? 8f : 5f;
                    Color tickColor = isMajor ? HUD_EMPHASIS : HUD_SECONDARY;

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = finalPos,
                        Size = new Vector2(2f, tickLength),
                        Color = tickColor,
                        Alignment = TextAlignment.CENTER,
                        RotationOrScale = angleRad + rollRad
                    });
                }
            }

            private void DrawMissileTOFToScreen(MySpriteDrawFrame frame, float centerX, float startY)
            {
                if (activeMissiles.Count == 0) return;

                const float TEXT_SCALE = 0.7f;
                const float LINE_HEIGHT = 20f;

                activeMissiles.RemoveAll(m => (totalElapsedTime - m.LaunchTime).TotalSeconds > m.EstimatedTOF + 5);

                for (int i = 0; i < Math.Min(5, activeMissiles.Count); i++)
                {
                    var missile = activeMissiles[i];
                    double timeRemaining = missile.EstimatedTOF - (totalElapsedTime - missile.LaunchTime).TotalSeconds;

                    if (timeRemaining > 0)
                    {
                        string tofText = $"MSL {missile.BayIndex + 1}: {timeRemaining:F1}s \u2192 TGT";
                        Color tofColor = timeRemaining < 3 ? HUD_WARNING : HUD_EMPHASIS;

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = tofText,
                            Position = new Vector2(centerX, startY + i * LINE_HEIGHT),
                            RotationOrScale = TEXT_SCALE,
                            Color = tofColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "Monospace"
                        });
                    }
                }
            }

            private void DrawRadarSweepLine(MySpriteDrawFrame frame, Vector2 radarCenter, float radarRadius)
            {
                radarSweepTick = (radarSweepTick + 1) % 360;
                float sweepAngle = radarSweepTick * 2f;

                float sweepRad = MathHelper.ToRadians(sweepAngle);
                Vector2 sweepEnd = radarCenter + new Vector2(
                    (float)Math.Cos(sweepRad) * radarRadius,
                    (float)Math.Sin(sweepRad) * radarRadius
                );

                Color sweepColor = new Color(HUD_EMPHASIS, 0.6f);
                AddLineSprite(frame, radarCenter, sweepEnd, 2f, sweepColor);
            }

            private void DrawRWRThreatCones(MySpriteDrawFrame frame, IMyCockpit cockpit, Vector2 radarCenter, float radarRadius)
            {
                if (radarControl == null || !radarControl.IsRWREnabled)
                    return;

                var threats = radarControl.activeThreats;
                if (threats.Count == 0)
                    return;

                Vector3D cockpitPos = cockpit.GetPosition();
                Vector3D worldUp = -Vector3D.Normalize(cockpit.GetNaturalGravity());

                Vector3D shipForward = cockpit.WorldMatrix.Forward;
                Vector3D shipRight = cockpit.WorldMatrix.Right;

                Vector3D shipForwardProjected = shipForward - Vector3D.Dot(shipForward, worldUp) * worldUp;
                Vector3D shipRightProjected = shipRight - Vector3D.Dot(shipRight, worldUp) * worldUp;

                Vector3D yawForward;
                if (shipForwardProjected.LengthSquared() > 0.01)
                {
                    yawForward = Vector3D.Normalize(shipForwardProjected);
                }
                else
                {
                    yawForward = Vector3D.Cross(shipRightProjected, worldUp);
                }

                Vector3D yawRight = Vector3D.Cross(yawForward, worldUp);
                MatrixD yawMatrix = MatrixD.Identity;
                yawMatrix.Forward = yawForward;
                yawMatrix.Right = yawRight;
                yawMatrix.Up = worldUp;

                MatrixD worldToYawPlaneMatrix = MatrixD.Transpose(yawMatrix);

                foreach (var threat in threats)
                {
                    Vector3D directionToThreat = threat.Position - cockpitPos;
                    Vector3D localDirection = Vector3D.TransformNormal(directionToThreat, worldToYawPlaneMatrix);

                    Vector2 horizontalDirection = new Vector2((float)localDirection.X, (float)localDirection.Z);

                    if (horizontalDirection.LengthSquared() < 0.001f)
                        continue;

                    horizontalDirection.Normalize();

                    float angleRad = (float)Math.Atan2(horizontalDirection.Y, horizontalDirection.X);
                    float CONE_WIDTH_RAD = MathHelper.ToRadians(30f);

                    float leftAngle = angleRad - CONE_WIDTH_RAD / 2f;
                    float rightAngle = angleRad + CONE_WIDTH_RAD / 2f;

                    Vector2 leftEdge = radarCenter + new Vector2(
                        (float)Math.Cos(leftAngle) * radarRadius,
                        (float)Math.Sin(leftAngle) * radarRadius
                    );

                    Vector2 rightEdge = radarCenter + new Vector2(
                        (float)Math.Cos(rightAngle) * radarRadius,
                        (float)Math.Sin(rightAngle) * radarRadius
                    );

                    Color coneColor = threat.IsIncoming ? HUD_WARNING : HUD_EMPHASIS;
                    Color coneColorFaded = new Color(coneColor, 0.3f);

                    AddLineSprite(frame, radarCenter, leftEdge, 2f, coneColor);
                    AddLineSprite(frame, radarCenter, rightEdge, 2f, coneColor);

                    const int ARC_SEGMENTS = 8;
                    for (int i = 0; i < ARC_SEGMENTS; i++)
                    {
                        float t1 = i / (float)ARC_SEGMENTS;
                        float t2 = (i + 1) / (float)ARC_SEGMENTS;

                        float angle1 = leftAngle + t1 * CONE_WIDTH_RAD;
                        float angle2 = leftAngle + t2 * CONE_WIDTH_RAD;

                        Vector2 arc1 = radarCenter + new Vector2(
                            (float)Math.Cos(angle1) * radarRadius,
                            (float)Math.Sin(angle1) * radarRadius
                        );

                        Vector2 arc2 = radarCenter + new Vector2(
                            (float)Math.Cos(angle2) * radarRadius,
                            (float)Math.Sin(angle2) * radarRadius
                        );

                        AddLineSprite(frame, arc1, arc2, 1f, coneColorFaded);
                    }

                    Vector2 threatIndicator = radarCenter + new Vector2(
                        (float)Math.Cos(angleRad) * (radarRadius * 0.9f),
                        (float)Math.Sin(angleRad) * (radarRadius * 0.9f)
                    );

                    if (threat.IsIncoming)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "!",
                            Position = threatIndicator,
                            RotationOrScale = 0.6f,
                            Color = HUD_WARNING,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                    }
                    else
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "^",
                            Position = threatIndicator,
                            RotationOrScale = 0.5f,
                            Color = HUD_EMPHASIS,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                    }
                }
            }

            private void DrawBreakawayWarning(MySpriteDrawFrame frame, double altitude, Vector3D velocity, Vector3D targetPosition, Vector3D shooterPosition)
            {
                bool lowAltitudeWarning = altitude < 100 && velocity.Y < -5;
                bool collisionWarning = false;

                if (targetPosition != Vector3D.Zero)
                {
                    double range = Vector3D.Distance(shooterPosition, targetPosition);
                    Vector3D relativeVelocity = velocity;
                    Vector3D toTarget = Vector3D.Normalize(targetPosition - shooterPosition);
                    double closureRate = -Vector3D.Dot(relativeVelocity, toTarget);

                    if (range < 500 && closureRate > 100)
                        collisionWarning = true;
                }

                if (!lowAltitudeWarning && !collisionWarning) return;

                Vector2 center = hud.SurfaceSize / 2f;
                float xSize = hud.SurfaceSize.X * 0.4f;
                Color warningColor = HUD_WARNING;
                float lineThickness = 4f;

                if ((radarSweepTick / 10) % 2 == 0)
                {
                    AddLineSprite(frame, center - new Vector2(xSize/2, xSize/2), center + new Vector2(xSize/2, xSize/2), lineThickness, warningColor);
                    AddLineSprite(frame, center - new Vector2(xSize/2, -xSize/2), center + new Vector2(xSize/2, -xSize/2), lineThickness, warningColor);

                    string warningText = lowAltitudeWarning ? "PULL UP" : "BREAK AWAY";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = warningText,
                        Position = new Vector2(center.X, center.Y + xSize/2 + 20f),
                        RotationOrScale = 1.2f,
                        Color = warningColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });
                }
            }

            private void DrawFormationGhosts(MySpriteDrawFrame frame, IMyCockpit cockpit, IMyTextSurface hud)
            {
                var customDataLines = ParentProgram.Me.CustomData.Split('\n');
                List<Vector3D> wingmanPositions = new List<Vector3D>();

                foreach (var line in customDataLines)
                {
                    if (line.StartsWith("Wingman"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 6)
                        {
                            Vector3D wingmanPos;
                            if (double.TryParse(parts[3], out wingmanPos.X) &&
                                double.TryParse(parts[4], out wingmanPos.Y) &&
                                double.TryParse(parts[5], out wingmanPos.Z))
                            {
                                wingmanPositions.Add(wingmanPos);
                            }
                        }
                    }
                }

                if (wingmanPositions.Count == 0) return;

                Vector3D shooterPosition = cockpit.GetPosition();
                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                const float COCKPIT_FOV_SCALE_X = 0.3434f;
                const float COCKPIT_FOV_SCALE_Y = 0.31f;
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;

                foreach (var wingmanPos in wingmanPositions)
                {
                    Vector3D directionToWingman = wingmanPos - shooterPosition;
                    Vector3D localDirection = Vector3D.TransformNormal(directionToWingman, worldToCockpitMatrix);

                    if (localDirection.Z >= 0) continue;

                    if (Math.Abs(localDirection.Z) < MIN_Z_FOR_PROJECTION)
                        localDirection.Z = -MIN_Z_FOR_PROJECTION;

                    float screenX = center.X + (float)(localDirection.X / -localDirection.Z) * scaleX;
                    float screenY = center.Y + (float)(-localDirection.Y / -localDirection.Z) * scaleY;

                    Vector2 ghostPos = new Vector2(screenX, screenY);
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Triangle",
                        Position = ghostPos,
                        Size = new Vector2(15f, 15f),
                        Color = new Color(HUD_RADAR_FRIENDLY, 0.7f),
                        Alignment = TextAlignment.CENTER
                    });
                }
            }

            private Vector2 RotatePoint(Vector2 point, Vector2 pivot, float angle)
            {
                float cosTheta = (float)Math.Cos(angle);
                float sinTheta = (float)Math.Sin(angle);
                Vector2 translatedPoint = point - pivot;
                Vector2 rotatedPoint = new Vector2(
                    translatedPoint.X * cosTheta - translatedPoint.Y * sinTheta,
                    translatedPoint.X * sinTheta + translatedPoint.Y * cosTheta
                );
                return rotatedPoint + pivot;
            }

            private double CalculateHeading()
            {
                return NavigationHelper.CalculateHeading(cockpit);
            }

            private double GetAltitude()
            {
                double altitude;
                if (!cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude))
                {
                    return 0;
                }
                return altitude;
            }

            private double CalculateAngleOfAttack(
                Vector3D forwardVector,
                Vector3D velocity,
                Vector3D upVector
            )
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

            private void DrawArtificialHorizon(
                MySpriteDrawFrame frame,
                float pitch,
                float roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                List<MySprite> sprites = new List<MySprite>();

                for (int i = -90; i <= 90; i += 5)
                {
                    if (i == 0)
                        continue;

                    float markerY = centerY - (i - pitch) * pixelsPerDegree;

                    if (markerY < -100 || markerY > hud.SurfaceSize.Y + 100)
                        continue;

                    bool isPositive = (i > 0);

                    float lineWidth = 90f;
                    float lineThickness = 2f;
                    Color lineColor = HUD_PRIMARY;

                    float halfWidth = lineWidth * 1.225f;
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX * 0.75f, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = TextAlignment.CENTER
                        }
                    );
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX * 1.25f, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = TextAlignment.CENTER
                        }
                    );

                    float tipLength = 12f;
                    float tipThickness = 2f;
                    float tipAngle = MathHelper.ToRadians(isPositive ? 45f : -45f);

                    string label = Math.Abs(i).ToString();
                    float labelOffsetX = halfWidth + tipLength + 10f;
                    float labelOffsetY = 0f;

                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            Position = new Vector2(centerX - labelOffsetX, markerY + 10f),
                            RotationOrScale = 0.8f,
                            Color = lineColor,
                            Alignment = TextAlignment.RIGHT,
                            FontId = "White"
                        }
                    );
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            Position = new Vector2(centerX + labelOffsetX, markerY + 10f),
                            RotationOrScale = 0.8f,
                            Color = lineColor,
                            Alignment = TextAlignment.LEFT,
                            FontId = "White"
                        }
                    );
                }

                float horizonY = centerY + pitch * pixelsPerDegree;
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX * 1.25f, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = HUD_HORIZON,
                        Alignment = TextAlignment.CENTER
                    }
                );
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX * 0.75f, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = HUD_HORIZON,
                        Alignment = TextAlignment.CENTER
                    }
                );
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "-^-",
                        Position = new Vector2(centerX, centerY - 10),
                        RotationOrScale = 0.8f,
                        Color = HUD_EMPHASIS,
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    }
                );

                float rollRad = MathHelper.ToRadians(-roll);
                float cosRoll = (float)Math.Cos(rollRad);
                float sinRoll = (float)Math.Sin(rollRad);

                for (int s = 0; s < sprites.Count; s++)
                {
                    MySprite sprite = sprites[s];
                    Vector2 pos = sprite.Position ?? Vector2.Zero;
                    Vector2 offset = pos - new Vector2(centerX, centerY);

                    Vector2 rotated = new Vector2(
                        offset.X * cosRoll - offset.Y * sinRoll,
                        offset.X * sinRoll + offset.Y * cosRoll
                    );

                    sprite.Position = rotated + new Vector2(centerX, centerY);

                    if (sprite.Type == SpriteType.TEXTURE)
                    {
                        float existing = sprite.RotationOrScale;
                        sprite.RotationOrScale = existing + rollRad;
                    }

                    sprites[s] = sprite;

                    frame.Add(sprite);
                }
            }

            private void DrawCompass(MySpriteDrawFrame frame, double heading)
            {
                float centerX = hud.SurfaceSize.X / 2f;
                float compassY = 40f;
                float compassWidth = hud.SurfaceSize.X * 0.9f;
                float compassHeight = 30f;
                float viewAngle = 90f;
                float halfViewAngle = viewAngle / 2f;
                int increment = 20;

                float headingScale = compassWidth / viewAngle;

                for (int markerHeading = 0; markerHeading < 360; markerHeading += increment)
                {
                    double deltaHeading = ((markerHeading - heading + 540) % 360) - 180;

                    if (deltaHeading >= -halfViewAngle && deltaHeading <= halfViewAngle)
                    {
                        float markerX = centerX + (float)deltaHeading * headingScale;

                        bool isMajorTick = (markerHeading % 90 == 0);

                        float markerLineHeight = isMajorTick ? compassHeight * 0.7f : compassHeight * 0.4f;
                        Color markerColor = isMajorTick ? HUD_SECONDARY : HUD_PRIMARY;

                        var markerLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(markerX, compassY),
                            Size = new Vector2(2f, markerLineHeight),
                            Color = markerColor,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(markerLine);

                        string label = isMajorTick ? GetCompassDirection(markerHeading) : markerHeading.ToString();
                        float textScale = 0.7f;

                        var markerText = new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            Position = new Vector2(markerX, compassY + compassHeight / 2f + 5f),
                            RotationOrScale = textScale,
                            Color = markerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        };
                        frame.Add(markerText);
                    }
                }

                var headingIndicator = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Triangle",
                    Position = new Vector2(centerX, compassY - compassHeight / 2f - 6f),
                    Size = new Vector2(12f, 10f),
                    Color = HUD_EMPHASIS,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = (float)Math.PI
                };
                frame.Add(headingIndicator);
            }

            private string GetCompassDirection(double heading)
            {
                if (heading >= 337.5 || heading < 22.5) return "N";
                else if (heading >= 22.5 && heading < 67.5) return "NE";
                else if (heading >= 67.5 && heading < 112.5) return "E";
                else if (heading >= 112.5 && heading < 157.5) return "SE";
                else if (heading >= 157.5 && heading < 202.5) return "S";
                else if (heading >= 202.5 && heading < 247.5) return "SW";
                else if (heading >= 247.5 && heading < 292.5) return "W";
                else return "NW";
            }

            private void RenderWeaponScreen(double heading, double altitude, Vector3D currentVelocity, Vector3D shooterPosition)
            {
                if (weaponScreen == null) return;

                using (var frame = weaponScreen.DrawFrame())
                {
                    float screenWidth = weaponScreen.SurfaceSize.X;
                    float screenHeight = weaponScreen.SurfaceSize.Y;
                    float margin = 10f;
                    float panelY = 25f;
                    Color titleColor = new Color(200, 180, 50);
                    Color headerColor = new Color(50, 180, 200);
                    Color borderColor = new Color(60, 120, 60);
                    Color panelBgColor = new Color(20, 20, 20, 180);

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, screenHeight / 2f),
                        Size = new Vector2(screenWidth, screenHeight),
                        Color = Color.Black,
                        Alignment = TextAlignment.CENTER
                    });

                    float titleHeight = 35f;
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY + 5f),
                        Size = new Vector2(screenWidth - margin * 2, titleHeight),
                        Color = new Color(30, 30, 30, 200),
                        Alignment = TextAlignment.CENTER
                    });

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "WEAPON SYSTEMS",
                        Position = new Vector2(screenWidth / 2f, panelY),
                        RotationOrScale = 0.75f,
                        Color = titleColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    panelY += 45f;

                    float weaponPanelHeight = 100f;
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY + weaponPanelHeight / 2f),
                        Size = new Vector2(screenWidth - margin * 2, weaponPanelHeight),
                        Color = panelBgColor,
                        Alignment = TextAlignment.CENTER
                    });

                    DrawWeaponStatusPanelToScreen(frame, myjet, margin, panelY, screenWidth - margin * 2);

                    panelY += weaponPanelHeight + 15f;

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY),
                        Size = new Vector2(screenWidth - margin * 4, 2f),
                        Color = borderColor,
                        Alignment = TextAlignment.CENTER
                    });

                    panelY += 15f;

                    if (activeMissiles.Count > 0)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "MISSILES IN FLIGHT",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            RotationOrScale = 0.65f,
                            Color = headerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                        panelY += 30f;

                        DrawMissileTOFToScreen(frame, screenWidth / 2f, panelY);
                        panelY += activeMissiles.Count * 20f + 25f;
                    }

                    int occupiedSlotCount = 0;
                    for (int i = 0; i < myjet.targetSlots.Length; i++)
                    {
                        if (myjet.targetSlots[i].IsOccupied) occupiedSlotCount++;
                    }
                    if (occupiedSlotCount > 1)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            Size = new Vector2(screenWidth - margin * 4, 2f),
                            Color = borderColor,
                            Alignment = TextAlignment.CENTER
                        });

                        panelY += 15f;

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "TARGET TRACKING",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            RotationOrScale = 0.65f,
                            Color = headerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                        panelY += 30f;

                        Vector3D[] occupiedPositions = new Vector3D[occupiedSlotCount];
                        int posIndex = 0;
                        for (int i = 0; i < myjet.targetSlots.Length; i++)
                        {
                            if (myjet.targetSlots[i].IsOccupied)
                            {
                                occupiedPositions[posIndex++] = myjet.targetSlots[i].Position;
                            }
                        }
                        DrawMultiTargetPanelToScreen(frame, occupiedPositions, shooterPosition, margin, panelY, screenWidth - margin * 2);
                    }
                }
            }

            public override string[] GetOptions() => new string[] { "Back to Main Menu" };
            public override void ExecuteOption(int index) { if (index == 0) SystemManager.ReturnToMainMenu(); }
        }
    }
}
