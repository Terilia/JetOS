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
            // Smoothed radar range to prevent jittery scaling
            private float smoothedRadarRange = 5000f;
            private const float RADAR_RANGE_SMOOTH = 0.1f; // Low alpha = slow adaptation
            private const float RADAR_MIN_RANGE = 2000f;
            private const float RADAR_RANGE_PADDING = 1.3f; // 30% padding beyond farthest target

            /// <summary>
            /// Top-down radar minimap. Ship nose = up. Auto-scales range to fit all radar contacts.
            /// Uses cockpit inverse matrix for reliable local-space projection (same basis as the pip).
            /// </summary>
            private void DrawRadarMinimap(MySpriteDrawFrame frame, IMyCockpit cockpit, IMyTextSurface hud)
            {
                if (cockpit == null || hud == null) return;

                Vector2 surfaceSize = hud.SurfaceSize;

                // Radar box: bottom-right corner of HUD
                Vector2 radarOrigin = new Vector2(
                    surfaceSize.X * 0.8f - RADAR_BORDER_MARGIN,
                    surfaceSize.Y - RADAR_BOX_SIZE_PX - RADAR_BORDER_MARGIN
                );
                Vector2 radarSize = new Vector2(RADAR_BOX_SIZE_PX, RADAR_BOX_SIZE_PX);
                Vector2 radarCenter = radarOrigin + radarSize / 2f;
                float radarRadius = RADAR_BOX_SIZE_PX / 2f;

                // --- Collect radar contacts (enemyList only, skip pinned) ---
                Vector3D cockpitPos = cockpit.GetPosition();
                Vector3D cockpitVel = cockpit.GetShipVelocities().LinearVelocity;

                // Build local-space transform: cockpit inverse gives us
                //   .X = right, .Y = up, .Z = backward (negative = forward)
                // Same as the lead pip uses, proven correct.
                MatrixD worldToLocal = MatrixD.Invert(cockpit.WorldMatrix);

                // We need a horizontal-plane projection, not a cockpit-relative one.
                // The cockpit matrix pitches/rolls with the jet — we only want yaw.
                // Project the cockpit forward onto the gravity plane to get "yaw forward".
                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D worldUp;
                if (gravity.LengthSquared() < 0.01)
                    worldUp = cockpit.WorldMatrix.Up;
                else
                    worldUp = Vector3D.Normalize(-gravity);

                Vector3D shipForward = cockpit.WorldMatrix.Forward;
                Vector3D yawForward = shipForward - Vector3D.Dot(shipForward, worldUp) * worldUp;

                if (yawForward.LengthSquared() < 0.01)
                {
                    // Pointing straight up/down — fall back to right vector
                    Vector3D shipRight = cockpit.WorldMatrix.Right;
                    Vector3D rightFlat = shipRight - Vector3D.Dot(shipRight, worldUp) * worldUp;
                    if (rightFlat.LengthSquared() > 0.01)
                        yawForward = Vector3D.Cross(worldUp, Vector3D.Normalize(rightFlat));
                    else
                        yawForward = shipForward; // Last resort
                }
                yawForward = Vector3D.Normalize(yawForward);

                // Yaw-right perpendicular to yaw-forward on the horizontal plane
                Vector3D yawRight = Vector3D.Cross(worldUp, yawForward);
                if (yawRight.LengthSquared() < 0.01)
                    yawRight = cockpit.WorldMatrix.Right;
                else
                    yawRight = Vector3D.Normalize(yawRight);

                // --- Determine auto-scale range from radar contacts ---
                float maxDist = 0f;
                var enemies = myjet.enemyList;
                var pinnedTarget = myjet.pinnedRaycastTarget;

                for (int i = 0; i < enemies.Count; i++)
                {
                    // Skip pinned target for range calculation
                    if (pinnedTarget.HasValue && enemies[i].EntityId != 0 &&
                        enemies[i].EntityId == pinnedTarget.Value.EntityId)
                        continue;
                    if (pinnedTarget.HasValue && enemies[i].EntityId == 0 &&
                        enemies[i].Name == pinnedTarget.Value.Name)
                        continue;

                    float dist = (float)Vector3D.Distance(enemies[i].Position, cockpitPos);
                    if (dist > maxDist)
                        maxDist = dist;
                }

                float targetRange = Math.Max(maxDist * RADAR_RANGE_PADDING, RADAR_MIN_RANGE);
                // Smooth the range so it doesn't jump around
                smoothedRadarRange += (targetRange - smoothedRadarRange) * RADAR_RANGE_SMOOTH;
                float radarRange = smoothedRadarRange;
                float pixelsPerMeter = radarRadius / radarRange;

                // --- Draw radar frame ---
                SpriteHelpers.DrawRectangleOutline(frame,
                    radarOrigin.X - 5f, radarOrigin.Y - 5f,
                    radarSize.X + 10f, radarSize.Y + 10f, 1f, HUD_PRIMARY);

                // Range ring at ~50% radius with label
                float ringRange = RoundToNiceRange(radarRange * 0.5f);
                float ringPx = ringRange * pixelsPerMeter;
                if (ringPx > 5f && ringPx < radarRadius)
                {
                    DrawDashedCircle(frame, radarCenter, ringPx, new Color(HUD_SECONDARY, 0.35f));
                    string ringLabel = ringRange >= 1000 ? $"{ringRange / 1000:F0}km" : $"{ringRange:F0}m";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = ringLabel,
                        Position = radarCenter + new Vector2(0, -ringPx - 5f),
                        RotationOrScale = 0.3f,
                        Color = new Color(HUD_SECONDARY, 0.5f),
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    });
                }

                // Outer range label
                string outerLabel = radarRange >= 1000 ? $"{radarRange / 1000:F1}km" : $"{radarRange:F0}m";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = outerLabel,
                    Position = new Vector2(radarCenter.X, radarOrigin.Y - 8f),
                    RotationOrScale = 0.28f,
                    Color = new Color(HUD_SECONDARY, 0.5f),
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                });

                // Player arrow (always center, pointing up)
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = TEXTURE_TRIANGLE,
                    Position = radarCenter,
                    Size = new Vector2(radarRadius * 0.15f, radarRadius * 0.15f),
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = 0 // Points up
                });

                // --- Draw contacts ---
                var selectedEnemy = myjet.GetSelectedEnemy();

                for (int i = 0; i < enemies.Count; i++)
                {
                    var enemy = enemies[i];
                    Vector3D toTarget = enemy.Position - cockpitPos;
                    float dist = (float)toTarget.Length();
                    if (dist < 1.0) continue;

                    // Project onto horizontal plane relative to ship heading:
                    //   dotRight  = how far right of us (positive = right on screen)
                    //   dotForward = how far ahead of us (positive = ahead = UP on screen)
                    float dotRight = (float)Vector3D.Dot(toTarget, yawRight);
                    float dotForward = (float)Vector3D.Dot(toTarget, yawForward);

                    // Screen mapping: X = right, Y = up (forward).
                    // Screen Y is inverted (positive Y = down), so negate forward for screen Y.
                    Vector2 offset = new Vector2(
                        dotRight * pixelsPerMeter,
                        -dotForward * pixelsPerMeter
                    );

                    // Clamp to radar circle edge
                    float offsetLen = offset.Length();
                    bool clamped = false;
                    if (offsetLen > radarRadius)
                    {
                        offset = offset / offsetLen * radarRadius;
                        clamped = true;
                    }

                    Vector2 pos = radarCenter + offset;
                    if (!pos.IsValid()) continue;

                    // Color by threat level
                    Vector3D relVel = enemy.Velocity - cockpitVel;
                    double closingSpeed = -Vector3D.Dot(Vector3D.Normalize(toTarget), relVel);
                    double timeToClosest = closingSpeed > 0 ? dist / closingSpeed : double.MaxValue;

                    Color contactColor;
                    if (timeToClosest < 5)
                        contactColor = HUD_WARNING;
                    else if (timeToClosest < 15)
                        contactColor = new Color(255, 128, 0);
                    else if (closingSpeed > 0)
                        contactColor = HUD_EMPHASIS;
                    else
                        contactColor = new Color(100, 100, 100);

                    // Highlight selected enemy
                    bool isSelected = selectedEnemy.HasValue &&
                        ((enemy.EntityId != 0 && enemy.EntityId == selectedEnemy.Value.EntityId) ||
                         (enemy.Name == selectedEnemy.Value.Name));

                    float iconSize = clamped ? 5f : 7f;

                    // Selected target: diamond, others: square
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = pos,
                        Size = new Vector2(iconSize, iconSize),
                        RotationOrScale = isSelected ? MathHelper.PiOver4 : 0f,
                        Color = contactColor,
                        Alignment = TextAlignment.CENTER
                    });

                    // Bearing line for dangerous/imminent threats
                    if (timeToClosest < 15 && closingSpeed > 0)
                    {
                        SpriteHelpers.AddLineSprite(frame, radarCenter, pos, 1f, new Color(contactColor, 0.35f));
                    }

                    // Range label for close contacts that fit on radar
                    if (dist < radarRange * 0.8f && !clamped)
                    {
                        string rangeText = dist >= 1000 ? $"{dist / 1000:F1}" : $"{dist:F0}";
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = rangeText,
                            Position = pos + new Vector2(7f, -4f),
                            RotationOrScale = 0.28f,
                            Color = contactColor,
                            Alignment = TextAlignment.LEFT,
                            FontId = "Monospace"
                        });
                    }
                }

                // Threat count below radar
                if (enemies.Count > 0)
                {
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = $"TGT: {enemies.Count}",
                        Position = new Vector2(radarCenter.X, radarOrigin.Y + radarSize.Y + 5f),
                        RotationOrScale = 0.4f,
                        Color = HUD_PRIMARY,
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    });
                }
            }

            /// <summary>
            /// Rounds a range to a nice human-readable value for ring labels.
            /// </summary>
            private static float RoundToNiceRange(float range)
            {
                if (range >= 10000) return (float)Math.Round(range / 5000) * 5000;
                if (range >= 1000) return (float)Math.Round(range / 1000) * 1000;
                if (range >= 100) return (float)Math.Round(range / 500) * 500;
                return (float)Math.Round(range / 100) * 100;
            }

            /// <summary>
            /// Draws a dashed circle on the radar.
            /// </summary>
            private static void DrawDashedCircle(MySpriteDrawFrame frame, Vector2 center, float radius, Color color)
            {
                const int SEGMENTS = 24;
                for (int i = 0; i < SEGMENTS; i += 2)
                {
                    float a1 = (i / (float)SEGMENTS) * MathHelper.TwoPi;
                    float a2 = ((i + 1) / (float)SEGMENTS) * MathHelper.TwoPi;

                    Vector2 p1 = center + new Vector2((float)Math.Cos(a1) * radius, (float)Math.Sin(a1) * radius);
                    Vector2 p2 = center + new Vector2((float)Math.Cos(a2) * radius, (float)Math.Sin(a2) * radius);

                    SpriteHelpers.AddLineSprite(frame, p1, p2, 1f, color);
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
