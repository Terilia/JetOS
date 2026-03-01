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
            // Optimized version using pre-allocated array of target positions
            private void DrawTopDownRadarOptimized(
                MySpriteDrawFrame frame,
                IMyCockpit cockpit,
                IMyTextSurface hud,
                Vector3D[] targetPositions,
                int targetCount,
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

                SpriteHelpers.DrawRectangleOutline(frame, radarOrigin.X - 5f, radarOrigin.Y - 5f, radarSize.X + 10f, radarSize.Y + 10f, 1f, radarBorderColor);


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
                for (int i = 0; i < targetCount; i++)
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
                SpriteHelpers.AddLineSprite(frame, radarCenter, sweepEnd, 2f, sweepColor);
            }

            /// <summary>
            /// Draws enhanced threat display with range rings, threat count, and color-coded contacts.
            /// </summary>
            private void DrawEnhancedThreatDisplay(MySpriteDrawFrame frame, IMyCockpit cockpit, Vector2 radarCenter, float radarRadius)
            {
                // Draw range rings at 5km, 10km, 15km intervals
                float[] rangeRings = { 5000f, 10000f, 15000f };
                float pixelsPerMeter = radarRadius / RADAR_RANGE_METERS;

                foreach (float range in rangeRings)
                {
                    float ringRadius = range * pixelsPerMeter;
                    if (ringRadius <= radarRadius)
                    {
                        // Draw range ring as dashed circle
                        int segments = 24;
                        Color ringColor = new Color(HUD_SECONDARY, 0.4f);

                        for (int i = 0; i < segments; i += 2) // Skip every other segment for dashed effect
                        {
                            float angle1 = (i / (float)segments) * MathHelper.TwoPi;
                            float angle2 = ((i + 1) / (float)segments) * MathHelper.TwoPi;

                            Vector2 p1 = radarCenter + new Vector2(
                                (float)Math.Cos(angle1) * ringRadius,
                                (float)Math.Sin(angle1) * ringRadius
                            );
                            Vector2 p2 = radarCenter + new Vector2(
                                (float)Math.Cos(angle2) * ringRadius,
                                (float)Math.Sin(angle2) * ringRadius
                            );

                            SpriteHelpers.AddLineSprite(frame, p1, p2, 1f, ringColor);
                        }

                        // Range label (at top of ring)
                        string rangeLabel = $"{range / 1000:F0}km";
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = rangeLabel,
                            Position = radarCenter + new Vector2(0, -ringRadius - 5f),
                            RotationOrScale = 0.35f,
                            Color = new Color(HUD_SECONDARY, 0.6f),
                            Alignment = TextAlignment.CENTER,
                            FontId = "Monospace"
                        });
                    }
                }

                // Get cockpit data for threat calculations
                Vector3D cockpitPos = cockpit.GetPosition();
                Vector3D cockpitVel = cockpit.GetShipVelocities().LinearVelocity;

                // Get world up from gravity for horizontal plane projection
                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D worldUp = gravity.LengthSquared() > 0.01
                    ? Vector3D.Normalize(-gravity)
                    : cockpit.WorldMatrix.Up;

                // Create yaw-aligned coordinate system
                Vector3D shipForward = cockpit.WorldMatrix.Forward;
                Vector3D yawForward = Vector3D.Normalize(Vector3D.Reject(shipForward, worldUp));
                if (!yawForward.IsValid() || yawForward.LengthSquared() < 0.1)
                    yawForward = shipForward;
                Vector3D yawRight = Vector3D.Cross(yawForward, worldUp);

                MatrixD yawMatrix = MatrixD.Identity;
                yawMatrix.Forward = yawForward;
                yawMatrix.Right = yawRight;
                yawMatrix.Up = worldUp;
                MatrixD worldToYawPlane = MatrixD.Transpose(yawMatrix);

                // Count and draw threats from enemy list
                int threatCount = 0;
                int immediateThreats = 0;
                int dangerousThreats = 0;

                foreach (var enemy in myjet.enemyList)
                {
                    // Calculate threat parameters
                    Vector3D relativePos = enemy.Position - cockpitPos;
                    double range = relativePos.Length();

                    if (range < 1.0)
                        continue;

                    Vector3D relativeVel = enemy.Velocity - cockpitVel;
                    double closingSpeed = -Vector3D.Dot(Vector3D.Normalize(relativePos), relativeVel);

                    // Calculate time to closest approach
                    double timeToClosest = closingSpeed > 0 ? range / closingSpeed : double.MaxValue;

                    // Determine threat level and color
                    Color threatColor;
                    if (timeToClosest < 5)
                    {
                        threatColor = HUD_WARNING; // Red - imminent
                        immediateThreats++;
                    }
                    else if (timeToClosest < 15)
                    {
                        threatColor = new Color(255, 128, 0); // Orange - dangerous
                        dangerousThreats++;
                    }
                    else if (closingSpeed > 0)
                    {
                        threatColor = HUD_EMPHASIS; // Yellow - aware (closing)
                    }
                    else
                    {
                        threatColor = new Color(100, 100, 100); // Gray - moving away
                    }

                    threatCount++;

                    // Project threat position onto radar
                    Vector3D threatVectorWorld = enemy.Position - cockpitPos;
                    Vector3D threatVectorLocal = Vector3D.TransformNormal(threatVectorWorld, worldToYawPlane);

                    Vector2 threatOffset = new Vector2(
                        (float)threatVectorLocal.X * pixelsPerMeter,
                        (float)threatVectorLocal.Z * pixelsPerMeter
                    );

                    // Clamp to radar radius
                    float distFromCenter = threatOffset.Length();
                    bool isOutsideRadar = distFromCenter > radarRadius;
                    if (isOutsideRadar)
                    {
                        if (distFromCenter > 1e-6f)
                            threatOffset /= distFromCenter;
                        threatOffset *= radarRadius;
                    }

                    Vector2 threatRadarPos = radarCenter + threatOffset;

                    // Draw threat icon (diamond for hostile)
                    float iconSize = isOutsideRadar ? 6f : 8f;

                    // Diamond shape using rotated square
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = threatRadarPos,
                        Size = new Vector2(iconSize, iconSize),
                        RotationOrScale = MathHelper.PiOver4, // 45 degree rotation for diamond
                        Color = threatColor,
                        Alignment = TextAlignment.CENTER
                    });

                    // Draw bearing line for dangerous threats
                    if (timeToClosest < 15 && closingSpeed > 0)
                    {
                        Color lineColor = new Color(threatColor, 0.4f);
                        SpriteHelpers.AddLineSprite(frame, radarCenter, threatRadarPos, 1f, lineColor);
                    }

                    // Draw range label for close threats
                    if (range < 8000 && !isOutsideRadar)
                    {
                        string rangeText = range >= 1000 ? $"{range / 1000:F1}" : $".{(int)(range / 100)}";
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = rangeText,
                            Position = threatRadarPos + new Vector2(8f, -4f),
                            RotationOrScale = 0.3f,
                            Color = threatColor,
                            Alignment = TextAlignment.LEFT,
                            FontId = "Monospace"
                        });
                    }
                }

                // Draw threat count indicator below radar
                if (threatCount > 0)
                {
                    Color countColor = immediateThreats > 0 ? HUD_WARNING :
                                       dangerousThreats > 0 ? new Color(255, 128, 0) : HUD_EMPHASIS;

                    string countText = $"THREATS: {threatCount}";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = countText,
                        Position = new Vector2(radarCenter.X, radarCenter.Y + radarRadius + 15f),
                        RotationOrScale = 0.5f,
                        Color = countColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    });

                    // Show breakdown if multiple threat levels
                    if (immediateThreats > 0 || dangerousThreats > 0)
                    {
                        string detailText = "";
                        if (immediateThreats > 0)
                            detailText += $"IMM:{immediateThreats} ";
                        if (dangerousThreats > 0)
                            detailText += $"DNG:{dangerousThreats}";

                        if (!string.IsNullOrEmpty(detailText))
                        {
                            frame.Add(new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = detailText.Trim(),
                                Position = new Vector2(radarCenter.X, radarCenter.Y + radarRadius + 28f),
                                RotationOrScale = 0.4f,
                                Color = countColor,
                                Alignment = TextAlignment.CENTER,
                                FontId = "Monospace"
                            });
                        }
                    }
                }
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

                    SpriteHelpers.AddLineSprite(frame, radarCenter, leftEdge, 2f, coneColor);
                    SpriteHelpers.AddLineSprite(frame, radarCenter, rightEdge, 2f, coneColor);

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

                        SpriteHelpers.AddLineSprite(frame, arc1, arc2, 1f, coneColorFaded);
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

            // Pre-allocated list for wingman positions to avoid per-frame allocation
            private List<Vector3D> _wingmanPositionBuffer = new List<Vector3D>();

            private void DrawFormationGhosts(MySpriteDrawFrame frame, IMyCockpit cockpit, IMyTextSurface hud)
            {
                _wingmanPositionBuffer.Clear();

                // Use CustomDataManager cache instead of parsing raw CustomData every frame
                for (int w = 1; w <= 4; w++)
                {
                    string wingmanKey = "Wingman" + w;
                    string value;
                    if (SystemManager.TryGetCustomDataValue(wingmanKey, out value) && !string.IsNullOrEmpty(value))
                    {
                        // Value format: "GPS:Name:X:Y:Z:Color:" - split the value portion
                        var parts = value.Split(':');
                        if (parts.Length >= 5)
                        {
                            double x, y, z;
                            if (double.TryParse(parts[2], out x) &&
                                double.TryParse(parts[3], out y) &&
                                double.TryParse(parts[4], out z))
                            {
                                _wingmanPositionBuffer.Add(new Vector3D(x, y, z));
                            }
                        }
                    }
                }

                if (_wingmanPositionBuffer.Count == 0) return;

                Vector3D shooterPosition = cockpit.GetPosition();
                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                const float COCKPIT_FOV_SCALE_X = 0.3434f;
                const float COCKPIT_FOV_SCALE_Y = 0.31f;
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;

                foreach (var wingmanPos in _wingmanPositionBuffer)
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
        }
    }
}
