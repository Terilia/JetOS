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

            private void DrawRadarMinimap(MySpriteDrawFrame frame, IMyCockpit cockpit, IMyTextSurface hud)
            {
                if (cockpit == null || hud == null) return;

                Vector2 surfaceSize = hud.SurfaceSize;

                // Radar box: bottom-right corner of HUD
                Vector2 radarOrigin = V2(
                    surfaceSize.X * 0.8f - RADAR_BORDER_MARGIN,
                    surfaceSize.Y - RADAR_BOX_SIZE_PX - RADAR_BORDER_MARGIN
                );
                Vector2 radarSize = V2(RADAR_BOX_SIZE_PX, RADAR_BOX_SIZE_PX);
                Vector2 radarCenter = radarOrigin + radarSize / 2f;
                float radarRadius = RADAR_BOX_SIZE_PX / 2f;

                // --- Collect radar contacts (enemyList only, skip pinned) ---
                Vector3D cockpitPos = cockpit.GetPosition();
                Vector3D cockpitVel = cockpit.GetShipVelocities().LinearVelocity;

                // Build local-space transform: cockpit inverse gives us
                //   .X = right, .Y = up, .Z = backward (negative = forward)
                // Same as the lead pip uses, proven correct.
                MatrixD worldToLocal = MatrixD.Transpose(cockpit.WorldMatrix);

                // We need a horizontal-plane projection, not a cockpit-relative one.
                // The cockpit matrix pitches/rolls with the jet — we only want yaw.
                // Project the cockpit forward onto the gravity plane to get "yaw forward".
                Vector3D gravity = myjet.CachedGravity;
                Vector3D worldUp;
                if (gravity.LengthSquared() < 0.01)
                    worldUp = cockpit.WorldMatrix.Up;
                else
                    worldUp = VN(-gravity);

                Vector3D shipForward = cockpit.WorldMatrix.Forward;
                Vector3D yawForward = shipForward - VD(shipForward, worldUp) * worldUp;

                if (yawForward.LengthSquared() < 0.01)
                {
                    // Pointing straight up/down — fall back to right vector
                    Vector3D shipRight = cockpit.WorldMatrix.Right;
                    Vector3D rightFlat = shipRight - VD(shipRight, worldUp) * worldUp;
                    if (rightFlat.LengthSquared() > 0.01)
                        yawForward = VX(worldUp, VN(rightFlat));
                    else
                        yawForward = shipForward; // Last resort
                }
                yawForward = VN(yawForward);

                // Yaw-right perpendicular to yaw-forward on the horizontal plane
                Vector3D yawRight = VX(yawForward, worldUp);
                if (yawRight.LengthSquared() < 0.01)
                    yawRight = cockpit.WorldMatrix.Right;
                else
                    yawRight = VN(yawRight);

                // --- Determine auto-scale range from radar contacts ---
                float maxDist = 0f;
                var enemies = myjet.enemyList;

                for (int i = 0; i < enemies.Count; i++)
                {
                    float dist = (float)VDi(enemies[i].Position, cockpitPos);
                    if (dist > maxDist)
                        maxDist = dist;
                }

                float targetRange = Mx(maxDist * RADAR_RANGE_PADDING, RADAR_MIN_RANGE);
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
                    DrawDashedCircle(frame, radarCenter, ringPx, Cr(HUD_SECONDARY, 0.35f));
                    string ringLabel = SpriteHelpers.FormatRange(ringRange);
                    SpriteHelpers.Tt(frame, ringLabel, radarCenter.X, radarCenter.Y - ringPx - 5f, 0.3f, Cr(HUD_SECONDARY, 0.5f));
                }

                // Outer range label
                string outerLabel = SpriteHelpers.FormatRange(radarRange);
                SpriteHelpers.Tt(frame, outerLabel, radarCenter.X, radarOrigin.Y - 8f, 0.28f, Cr(HUD_SECONDARY, 0.5f));

                // Player arrow (always center, pointing up)
                SpriteHelpers.Sp(frame, TEXTURE_TRIANGLE, radarCenter.X, radarCenter.Y, radarRadius * 0.15f, radarRadius * 0.15f, HUD_PRIMARY);

                // --- Draw contacts ---
                var selectedEnemy = myjet.GetSelectedEnemy();

                for (int i = 0; i < enemies.Count; i++)
                {
                    var enemy = enemies[i];
                    Vector3D toTarget = enemy.Position - cockpitPos;
                    float dist = (float)toTarget.Length();
                    if (dist < 1.0) continue;

                    float dotRight = (float)VD(toTarget, yawRight);
                    float dotForward = (float)VD(toTarget, yawForward);

                    Vector2 offset = V2(
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
                    double closingSpeed = -VD(VN(toTarget), relVel);
                    double timeToClosest = closingSpeed > 0 ? dist / closingSpeed : double.MaxValue;

                    Color contactColor;
                    if (timeToClosest < 5)
                        contactColor = HUD_WARNING;
                    else if (timeToClosest < 15)
                        contactColor = Cr(255, 128, 0);
                    else if (closingSpeed > 0)
                        contactColor = HUD_EMPHASIS;
                    else
                        contactColor = Cr(100, 100, 100);

                    // Highlight selected enemy
                    bool isSelected = selectedEnemy.HasValue && enemy.Matches(selectedEnemy.Value);

                    float iconSize = clamped ? 5f : 7f;

                    // Selected target: diamond, others: square
                    SpriteHelpers.Bx(frame, pos.X, pos.Y, iconSize, iconSize, contactColor, isSelected ? MathHelper.PiOver4 : 0f);

                    // Bearing line for dangerous/imminent threats
                    if (timeToClosest < 15 && closingSpeed > 0)
                    {
                        SpriteHelpers.AddLineSprite(frame, radarCenter, pos, 1f, Cr(contactColor, 0.35f));
                    }

                    // Range label for close contacts that fit on radar
                    if (dist < radarRange * 0.8f && !clamped)
                    {
                        string rangeText = dist >= 1000 ? $"{dist / 1000:F1}" : $"{dist:F0}";
                        SpriteHelpers.Tt(frame, rangeText, pos.X + 7f, pos.Y - 4f, 0.28f, contactColor, MFDTheme.AL);
                    }
                }

                // Threat count below radar
                if (enemies.Count > 0)
                {
                    SpriteHelpers.Tt(frame, $"TGT: {enemies.Count}", radarCenter.X, radarOrigin.Y + radarSize.Y + 5f, 0.4f, HUD_PRIMARY);
                }
            }

            private static float RoundToNiceRange(float range)
            {
                if (range >= 10000) return (float)Math.Round(range / 5000) * 5000;
                if (range >= 1000) return (float)Math.Round(range / 1000) * 1000;
                if (range >= 100) return (float)Math.Round(range / 500) * 500;
                return (float)Math.Round(range / 100) * 100;
            }

            private static void DrawDashedCircle(MySpriteDrawFrame frame, Vector2 center, float radius, Color color)
            {
                // Uses precomputed trig table — eliminates 24 sin/cos calls per frame
                for (int i = 0; i < SpriteHelpers.CIRC_SEGS; i += 2)
                {
                    Vector2 p1 = center + V2(SpriteHelpers.CCos[i] * radius, SpriteHelpers.CSin[i] * radius);
                    Vector2 p2 = center + V2(SpriteHelpers.CCos[i + 1] * radius, SpriteHelpers.CSin[i + 1] * radius);
                    SpriteHelpers.AddLineSprite(frame, p1, p2, 1f, color);
                }
            }

            // Pre-allocated list for wingman positions to avoid per-frame allocation
            private List<Vector3D> _wingmanPositionBuffer = new List<Vector3D>();

            private void DrawFormationGhosts(MySpriteDrawFrame frame, IMyTextSurface hud, MatrixD worldToCockpitMatrix)
            {
                _wingmanPositionBuffer.Clear();

                // Use CustomDataManager cache instead of parsing raw CustomData every frame
                for (int w = 1; w <= 4; w++)
                {
                    string wingmanKey = "Wingman" + w;
                    string value;
                    if (SystemManager.TryGetCustomDataValue(wingmanKey, out value) && !string.IsNullOrEmpty(value))
                    {
                        Vector3D pos;
                        if (NavigationHelper.TryParseGps(value, out pos))
                        {
                            _wingmanPositionBuffer.Add(pos);
                        }
                    }
                }

                if (_wingmanPositionBuffer.Count == 0) return;

                Vector3D shooterPosition = cockpit.GetPosition();
                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                foreach (var wingmanPos in _wingmanPositionBuffer)
                {
                    Vector3D directionToWingman = wingmanPos - shooterPosition;
                    Vector3D localDirection = VTN(directionToWingman, worldToCockpitMatrix);

                    if (localDirection.Z >= 0) continue;

                    if (Ab(localDirection.Z) < MIN_Z_FOR_PROJECTION)
                        localDirection.Z = -MIN_Z_FOR_PROJECTION;

                    Vector2 ghostPos = SpriteHelpers.ProjectToScreen(localDirection, center, surfaceSize);
                    SpriteHelpers.Sp(frame, "Triangle", ghostPos.X, ghostPos.Y, 15f, 15f, Cr(HUD_RADAR_FRIENDLY, 0.7f));
                }
            }
        }
    }
}
