using System;
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
        IMyTextSurface hud,
        MatrixD worldToCockpitMatrix,
        Vector3D shooterPosition,
        Vector3D targetPosition,
        Vector3D interceptPoint,
        Vector3D aimPoint,
        double timeToIntercept,
        Color pipColor,
        Color offScreenColor,
        Color behindColor,
        Color reticleColor
    )
            {
                if (hud == null) return;
                const float MIN_DISTANCE_FOR_SCALING = 50f;
                const float MAX_DISTANCE_FOR_SCALING = 3000f;
                const float MAX_PIP_SIZE_FACTOR = 0.1f;
                const float MIN_PIP_SIZE_FACTOR = 0.01f;

                bool isAimingAtPip = false;

                // Use aimPoint (accounts for bullet drop) instead of interceptPoint (target future position)
                Vector3D directionToIntercept = aimPoint - shooterPosition;
                Vector3D localDirectionToIntercept = VTN(directionToIntercept, worldToCockpitMatrix);

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;
                float viewportMinDim = Math.Min(surfaceSize.X, surfaceSize.Y);
                float targetMarkerSize = viewportMinDim * 0.02f;
                float lineThickness = Math.Max(1f, viewportMinDim * 0.004f);
                float reticleArmLength = viewportMinDim * 0.025f;
                float arrowSize = viewportMinDim * 0.04f;
                float arrowHeadSize = viewportMinDim * 0.025f;
                double distanceToIntercept = VDi(shooterPosition, interceptPoint);
                float distanceScaleFactor = Cl((float)((MAX_DISTANCE_FOR_SCALING - distanceToIntercept) / (MAX_DISTANCE_FOR_SCALING - MIN_DISTANCE_FOR_SCALING)), 0.0f, 1.0f);
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


                Vector2 pipScreenPos = SpriteHelpers.ProjectToScreen(localDirectionToIntercept, center, surfaceSize);

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
                        if (!myjet._gatlings[i].Enabled)
                            myjet._gatlings[i].Enabled = true;
                    }
                }
                else
                {
                    if (myjet.manualfire == false)
                    {
                        for (int i = 0; i < myjet._gatlings.Count; i++)
                        {
                            if (myjet._gatlings[i].Enabled)
                                myjet._gatlings[i].Enabled = false;
                        }
                    }
                }
                if (isOnScreen)
                {
                    var pipSprite = new MySprite()
                    {
                        Type = MFDTheme.TX,
                        Data = TEXTURE_CIRCLE,
                        Position = pipScreenPos,
                        Size = new Vector2(dynamicPipSize, dynamicPipSize),
                        Color = pipColor,
                        Alignment = MFDTheme.AC
                    };
                    frame.Add(pipSprite);

                    // Draw time-to-intercept (TTI) near the lead pip
                    if (timeToIntercept > 0 && timeToIntercept < 30)
                    {
                        string ttiText = $"{timeToIntercept:F1}s";
                        Color ttiColor = timeToIntercept < 2 ? HUD_WARNING : (timeToIntercept < 5 ? HUD_EMPHASIS : HUD_PRIMARY);

                        frame.Add(new MySprite()
                        {
                            Type = MFDTheme.TT,
                            Data = ttiText,
                            Position = pipScreenPos + new Vector2(dynamicPipSize / 2 + 8f, -8f),
                            RotationOrScale = 0.5f,
                            Color = ttiColor,
                            Alignment = MFDTheme.AL,
                            FontId = MFDTheme.FONT
                        });

                        // Draw range to intercept point
                        string rangeText = SpriteHelpers.FormatRange(distanceToIntercept);

                        frame.Add(new MySprite()
                        {
                            Type = MFDTheme.TT,
                            Data = rangeText,
                            Position = pipScreenPos + new Vector2(dynamicPipSize / 2 + 8f, 4f),
                            RotationOrScale = 0.45f,
                            Color = ttiColor,
                            Alignment = MFDTheme.AL,
                            FontId = MFDTheme.FONT
                        });
                    }

                    Vector3D directionToTarget = targetPosition - shooterPosition;
                    Vector3D localDirectionToTarget = VTN(directionToTarget, worldToCockpitMatrix);

                    Vector2 currentTargetScreenPos = Vector2.Zero; // Initialize

                    if (localDirectionToTarget.Z < -MIN_Z_FOR_PROJECTION)
                    {
                        currentTargetScreenPos = SpriteHelpers.ProjectToScreen(localDirectionToTarget, center, surfaceSize);
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
                    float angle = (float)At2(direction.Y, direction.X);

                    float edgeX = (float)Cs(angle) * maxDistX;
                    float edgeY = (float)Sn(angle) * maxDistY;

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


                    edgePoint.X = Cl(edgePoint.X, arrowSize / 2f, surfaceSize.X - arrowSize / 2f);
                    edgePoint.Y = Cl(edgePoint.Y, arrowSize / 2f, surfaceSize.Y - arrowSize / 2f);


                    float arrowRotation = (float)At2(direction.Y, direction.X);
                    var arrowSprite = new MySprite()
                    {
                        Type = MFDTheme.TX,
                        Data = TEXTURE_TRIANGLE,
                        Position = edgePoint,
                        Size = new Vector2(arrowHeadSize, arrowHeadSize),
                        Color = offScreenColor,
                        RotationOrScale = arrowRotation + (float)Math.PI / 2f,
                        Alignment = MFDTheme.AC
                    };
                    frame.Add(arrowSprite);

                    // Range label next to off-screen arrow
                    double offscreenRange = VDi(shooterPosition, targetPosition);
                    string offscreenRangeText = SpriteHelpers.FormatRange(offscreenRange);
                    Vector2 perpDir = new Vector2(-direction.Y, direction.X);
                    frame.Add(new MySprite()
                    {
                        Type = MFDTheme.TT,
                        Data = offscreenRangeText,
                        Position = edgePoint + perpDir * 14f - direction * 10f,
                        RotationOrScale = 0.45f,
                        Color = offScreenColor,
                        Alignment = MFDTheme.AC,
                        FontId = MFDTheme.FONT
                    });
                }
            }

            private void DrawTargetBrackets(
                MySpriteDrawFrame frame,
                IMyTextSurface hud,
                MatrixD worldToCockpitMatrix,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D shooterPosition,
                Vector3D shooterVelocity
            )
            {
                if (hud == null) return;

                double range = VDi(shooterPosition, targetPosition);

                // Closure rate: positive = closing (shooter approaching target)
                Vector3D relativeVelocity = shooterVelocity - targetVelocity;
                Vector3D directionToTarget = VN(targetPosition - shooterPosition);
                double closureRate = VD(relativeVelocity, directionToTarget);

                Vector3D targetForward = targetVelocity.LengthSquared() > 0.01
                    ? VN(targetVelocity) : directionToTarget;
                Vector3D toShooter = VN(shooterPosition - targetPosition);
                double aspectAngle = At2(VX(targetForward, toShooter).Length(), VD(targetForward, toShooter)) * (180.0 / Math.PI);

                Vector3D directionToTargetLocal = VTN(targetPosition - shooterPosition, worldToCockpitMatrix);

                if (Math.Abs(directionToTargetLocal.Z) < MIN_Z_FOR_PROJECTION)
                    directionToTargetLocal.Z = -MIN_Z_FOR_PROJECTION;

                if (directionToTargetLocal.Z >= 0) return;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                Vector2 targetScreenPos = SpriteHelpers.ProjectToScreen(directionToTargetLocal, center, surfaceSize);

                bool isOnScreen = targetScreenPos.X >= 0 && targetScreenPos.X <= surfaceSize.X &&
                                  targetScreenPos.Y >= 0 && targetScreenPos.Y <= surfaceSize.Y;

                if (!isOnScreen) return;

                float bracketSize = Cl((float)(3000.0 / range), 20f, 80f);
                float bracketThickness = 2f;
                float cornerLength = bracketSize * 0.3f;

                Color bracketColor = closureRate > 10 ? HUD_WARNING :
                                   closureRate < -10 ? HUD_EMPHASIS : HUD_PRIMARY;

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

                string rangeText = SpriteHelpers.FormatRange(range);
                var rangeSprite = new MySprite()
                {
                    Type = MFDTheme.TT,
                    Data = rangeText,
                    Position = new Vector2(targetScreenPos.X, textY),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = MFDTheme.AC,
                    FontId = MFDTheme.FONT
                };
                frame.Add(rangeSprite);

                string closureLabel = closureRate > 10 ? "HOT" : closureRate < -10 ? "COLD" : "---";
                string closureText = $"Vc:{Math.Abs(closureRate):F0} {closureLabel}";
                var closureSprite = new MySprite()
                {
                    Type = MFDTheme.TT,
                    Data = closureText,
                    Position = new Vector2(targetScreenPos.X, textY + 12f),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = MFDTheme.AC,
                    FontId = MFDTheme.FONT
                };
                frame.Add(closureSprite);

                string aspectText = $"AA:{aspectAngle:F0}\u00B0";
                var aspectSprite = new MySprite()
                {
                    Type = MFDTheme.TT,
                    Data = aspectText,
                    Position = new Vector2(targetScreenPos.X, textY + 24f),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = MFDTheme.AC,
                    FontId = MFDTheme.FONT
                };
                frame.Add(aspectSprite);
            }

            private void DrawGunFunnel(
                MySpriteDrawFrame frame,
                IMyTextSurface hud,
                MatrixD worldToCockpitMatrix,
                Vector3D interceptPoint,
                Vector3D shooterPosition,
                double range,
                bool isAimingAtPip
            )
            {
                if (hud == null) return;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                float funnelWidthFactor = Cl((float)(range / 2000.0), 0.05f, 0.3f);
                float funnelBaseWidth = surfaceSize.X * funnelWidthFactor;

                Vector3D directionToIntercept = interceptPoint - shooterPosition;
                Vector3D localDirectionToIntercept = VTN(directionToIntercept, worldToCockpitMatrix);

                if (localDirectionToIntercept.Z >= 0) return;

                if (Math.Abs(localDirectionToIntercept.Z) < MIN_Z_FOR_PROJECTION)
                    localDirectionToIntercept.Z = -MIN_Z_FOR_PROJECTION;

                Vector2 pipScreenPos = SpriteHelpers.ProjectToScreen(localDirectionToIntercept, center, surfaceSize);

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
                        Type = MFDTheme.TT,
                        Data = cueText,
                        Position = new Vector2(center.X, center.Y - 60f),
                        RotationOrScale = 1.0f,
                        Color = cueColor,
                        Alignment = MFDTheme.AC,
                        FontId = MFDTheme.FONT_W
                    });
                }
            }

            private void DrawBreakawayWarning(MySpriteDrawFrame frame, double altitude, Vector3D velocity, Vector3D targetPosition, Vector3D shooterPosition, Vector3D targetVelocity)
            {
                bool lowAltitudeWarning = altitude < 100 && velocity.Y < -5;
                bool collisionWarning = false;

                if (targetPosition != Vector3D.Zero)
                {
                    double range = VDi(shooterPosition, targetPosition);
                    Vector3D relativeVelocity = velocity - targetVelocity;
                    Vector3D toTarget = VN(targetPosition - shooterPosition);
                    double closureRate = -VD(relativeVelocity, toTarget);

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
                        Type = MFDTheme.TT,
                        Data = warningText,
                        Position = new Vector2(center.X, center.Y + xSize/2 + 20f),
                        RotationOrScale = 1.2f,
                        Color = warningColor,
                        Alignment = MFDTheme.AC,
                        FontId = MFDTheme.FONT_W
                    });
                }
            }

        }
    }
}
