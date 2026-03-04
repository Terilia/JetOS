using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        partial class HUDModule
        {
            private void DrawLeadingPip(
        MySpriteDrawFrame frame,
        IMyCockpit cockpit,
        IMyTextSurface hud,
        Vector3D targetPosition,
        Vector3D targetVelocity,
        Vector3D shooterPosition,
        Vector3D shooterVelocity,
        double projectileSpeed,
        Color pipColor,
        Color offScreenColor,
        Color behindColor,
        Color reticleColor,
        Vector3D targetAcceleration = default(Vector3D)
    )
            {
                if (cockpit == null || hud == null) return; // Safety check
                const float MIN_DISTANCE_FOR_SCALING = 50f;  // Target closer than this uses max pip size (e.g., 500 meters)
                const float MAX_DISTANCE_FOR_SCALING = 3000f; // Target farther than this uses min pip size (e.g., 3000 meters)
                const float MAX_PIP_SIZE_FACTOR = 0.1f;      // Pip size factor at min distance (relative to viewportMinDim)
                const float MIN_PIP_SIZE_FACTOR = 0.01f;     // Pip size factor at max distance (relative to viewportMinDim)

                Vector3D interceptPoint;
                double timeToIntercept;
                Vector3D aimPoint;
                bool isAimingAtPip = false; // Initialize the output parameter to false
                                            // Use the iterative solver
                if (!BallisticsCalculator.CalculateInterceptPoint(
                shooterPosition,
                shooterVelocity,
                projectileSpeed,
                targetPosition,
                targetVelocity,
                INTERCEPT_ITERATIONS,
                out interceptPoint,
                out timeToIntercept,
                out aimPoint,
                targetAcceleration
            ))
                {
                    return;
                }


                // Use aimPoint (accounts for bullet drop) instead of interceptPoint (target future position)
                Vector3D directionToIntercept = aimPoint - shooterPosition;
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
                    SpriteHelpers.AddLineSprite(frame, center - new Vector2(reticleArmLength, 0), center + new Vector2(reticleArmLength, 0), lineThickness, behindColor);
                    SpriteHelpers.AddLineSprite(frame, center - new Vector2(0, reticleArmLength), center + new Vector2(0, reticleArmLength), lineThickness, behindColor);
                    return;
                }

                SpriteHelpers.AddLineSprite(frame, center - new Vector2(reticleArmLength, 0), center + new Vector2(reticleArmLength, 0), lineThickness, reticleColor);
                SpriteHelpers.AddLineSprite(frame, center - new Vector2(0, reticleArmLength), center + new Vector2(0, reticleArmLength), lineThickness, reticleColor);


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

                    // Draw time-to-intercept (TTI) near the lead pip
                    if (timeToIntercept > 0 && timeToIntercept < 30)
                    {
                        string ttiText = $"{timeToIntercept:F1}s";
                        Color ttiColor = timeToIntercept < 2 ? HUD_WARNING : (timeToIntercept < 5 ? HUD_EMPHASIS : HUD_PRIMARY);

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = ttiText,
                            Position = pipScreenPos + new Vector2(dynamicPipSize / 2 + 8f, -8f),
                            RotationOrScale = 0.5f,
                            Color = ttiColor,
                            Alignment = TextAlignment.LEFT,
                            FontId = "Monospace"
                        });

                        // Draw range to intercept point
                        string rangeText = distanceToIntercept >= 1000
                            ? $"{distanceToIntercept / 1000:F1}km"
                            : $"{distanceToIntercept:F0}m";

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = rangeText,
                            Position = pipScreenPos + new Vector2(dynamicPipSize / 2 + 8f, 4f),
                            RotationOrScale = 0.45f,
                            Color = ttiColor,
                            Alignment = TextAlignment.LEFT,
                            FontId = "Monospace"
                        });
                    }

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
                    SpriteHelpers.AddLineSprite(frame, currentTargetScreenPos - new Vector2(halfMark, halfMark), currentTargetScreenPos + new Vector2(halfMark, halfMark), lineThickness, Color.Yellow);
                    SpriteHelpers.AddLineSprite(frame, currentTargetScreenPos - new Vector2(halfMark, -halfMark), currentTargetScreenPos + new Vector2(halfMark, -halfMark), lineThickness, Color.Yellow);
                    SpriteHelpers.AddLineSprite(frame, pipScreenPos, currentTargetScreenPos, lineThickness, Color.Yellow);
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

                SpriteHelpers.AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2 + cornerLength, -bracketSize/2),
                                    bracketThickness, bracketColor);
                SpriteHelpers.AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2 + cornerLength),
                                    bracketThickness, bracketColor);

                SpriteHelpers.AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2 - cornerLength, -bracketSize/2),
                                    bracketThickness, bracketColor);
                SpriteHelpers.AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2 + cornerLength),
                                    bracketThickness, bracketColor);

                SpriteHelpers.AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2 + cornerLength, bracketSize/2),
                                    bracketThickness, bracketColor);
                SpriteHelpers.AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2 - cornerLength),
                                    bracketThickness, bracketColor);

                SpriteHelpers.AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2 - cornerLength, bracketSize/2),
                                    bracketThickness, bracketColor);
                SpriteHelpers.AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, bracketSize/2),
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

                string aspectText = $"AA:{aspectAngle:F0}\u00B0";
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
                    SpriteHelpers.AddLineSprite(frame, edgePoint, pipScreenPos, lineThickness, funnelColor);
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
                    SpriteHelpers.AddLineSprite(frame, center - new Vector2(xSize/2, xSize/2), center + new Vector2(xSize/2, xSize/2), lineThickness, warningColor);
                    SpriteHelpers.AddLineSprite(frame, center - new Vector2(xSize/2, -xSize/2), center + new Vector2(xSize/2, -xSize/2), lineThickness, warningColor);

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
        }
    }
}
