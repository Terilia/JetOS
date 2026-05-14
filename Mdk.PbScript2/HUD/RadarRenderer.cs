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
            private const float RADAR_MAX_RANGE = 15000f;
            private const float RADAR_RANGE_PADDING = 1.3f;       // 30% padding beyond farthest target
            private const float RADAR_SPEED_LOOKAHEAD_SEC = 25f;  // ~half a minute of forward visibility at current speed

            // Pre-allocated cache shared between the maxDist pass and the render pass —
            // avoids recomputing (toTarget, dist, dotRight, dotForward) twice per enemy.
            private struct RadarContact
            {
                public Vector3D ToTarget;
                public float Distance;
                public float DotRight;
                public float DotForward;
            }
            private RadarContact[] _radarBuf = new RadarContact[16];

            private void DrawRadarMinimap(IMyCockpit cockpit, IMyTextSurface hud)
            {
                if (cockpit == null || hud == null) return;

                Vector2 surfaceSize = SS(hud);

                // Radar box: bottom-right corner of HUD
                Vector2 radarOrigin = V2(
                    surfaceSize.X * 0.8f - RADAR_BORDER_MARGIN,
                    surfaceSize.Y - RADAR_BOX_SIZE_PX - RADAR_BORDER_MARGIN
                );
                Vector2 radarSize = V2(RADAR_BOX_SIZE_PX, RADAR_BOX_SIZE_PX);
                Vector2 radarCenter = radarOrigin + radarSize / 2f;
                float radarRadius = RADAR_BOX_SIZE_PX / 2f;

                // --- Collect radar contacts (enemyList only, skip pinned) ---
                Vector3D cockpitPos = GP(cockpit);
                Vector3D cockpitVel = LV(cockpit);

                // Build local-space transform: cockpit inverse gives us
                //   .X = right, .Y = up, .Z = backward (negative = forward)
                // Same as the lead pip uses, proven correct.
                MatrixD worldToLocal = MatrixD.Transpose(WM(cockpit));

                // We need a horizontal-plane projection, not a cockpit-relative one.
                // The cockpit matrix pitches/rolls with the jet — we only want yaw.
                // Project the cockpit forward onto the gravity plane to get "yaw forward".
                Vector3D gravity = myjet.CachedGravity;
                Vector3D worldUp;
                if (gravity.LengthSquared() < 0.01)
                    worldUp = WU(cockpit);
                else
                    worldUp = VN(-gravity);

                Vector3D shipForward = WF(cockpit);
                Vector3D yawForward = shipForward - VD(shipForward, worldUp) * worldUp;

                if (yawForward.LengthSquared() < 0.01)
                {
                    // Pointing straight up/down — fall back to right vector
                    Vector3D shipRight = WR(cockpit);
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
                    yawRight = WR(cockpit);
                else
                    yawRight = VN(yawRight);

                // --- Pass 1: compute toTarget + distance + axis dots once, find maxDist for scaling.
                // Pass 2 below reuses these so we don't recompute per-enemy vector math.
                var enemies = myjet.enemyList;
                int n = enemies.Count;
                if (_radarBuf.Length < n) _radarBuf = new RadarContact[Mx(n, _radarBuf.Length * 2)];
                float maxDist = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector3D toTarget = enemies[i].Position - cockpitPos;
                    float dist = (float)toTarget.Length();
                    _radarBuf[i].ToTarget = toTarget;
                    _radarBuf[i].Distance = dist;
                    _radarBuf[i].DotRight = (float)VD(toTarget, yawRight);
                    _radarBuf[i].DotForward = (float)VD(toTarget, yawForward);
                    if (dist > maxDist) maxDist = dist;
                }

                // Range = max(farthest contact × padding, speed × lookahead, hard min), clamped to hard max.
                // The speed term keeps the dish proportional to "seconds-to-edge" so a 300m/s pass shows the
                // same forward visibility (in time) as a 50m/s pass.
                float speed = (float)cockpitVel.Length();
                float speedRange = speed * RADAR_SPEED_LOOKAHEAD_SEC;
                float targetRange = Mx(Mx(maxDist * RADAR_RANGE_PADDING, speedRange), RADAR_MIN_RANGE);
                if (targetRange > RADAR_MAX_RANGE) targetRange = RADAR_MAX_RANGE;
                // Smooth the range so it doesn't jump around
                smoothedRadarRange += (targetRange - smoothedRadarRange) * RADAR_RANGE_SMOOTH;
                float radarRange = smoothedRadarRange;
                float pixelsPerMeter = radarRadius / radarRange;

                // --- Draw radar frame ---
                SpriteHelpers.DrawRectangleOutline(radarOrigin.X - 5f, radarOrigin.Y - 5f,
                    radarSize.X + 10f, radarSize.Y + 10f, 1f, HUD_PRIMARY);

                // Range ring at ~50% radius with label
                float ringRange = RoundToNiceRange(radarRange * 0.5f);
                float ringPx = ringRange * pixelsPerMeter;
                if (ringPx > 5f && ringPx < radarRadius)
                {
                    SpriteHelpers.Sp(TEX_RANGE_RING, radarCenter.X, radarCenter.Y, ringPx * 2.13f, ringPx * 2.13f, Cr(HUD_SECONDARY, 0.35f));
                    string ringLabel = SpriteHelpers.FormatRange(ringRange);
                    SpriteHelpers.Tt(ringLabel, radarCenter.X, radarCenter.Y - ringPx - 5f, 0.3f, Cr(HUD_SECONDARY, 0.5f));
                }

                // Outer range label
                string outerLabel = SpriteHelpers.FormatRange(radarRange);
                SpriteHelpers.Tt(outerLabel, radarCenter.X, radarOrigin.Y - 8f, 0.28f, Cr(HUD_SECONDARY, 0.5f));

                // Own ship at radar center (top-down jet silhouette).
                SpriteHelpers.Sp(TEX_OWN_SHIP, radarCenter.X, radarCenter.Y, radarRadius * 0.25f, radarRadius * 0.25f, HUD_PRIMARY);

                // --- Draw contacts (reuses Pass 1 cache) ---
                var selectedEnemy = myjet.GetSelectedEnemy();

                for (int i = 0; i < n; i++)
                {
                    var enemy = enemies[i];
                    Vector3D toTarget = _radarBuf[i].ToTarget;
                    float dist = _radarBuf[i].Distance;
                    if (dist < 1.0) continue;

                    Vector2 offset = V2(
                        _radarBuf[i].DotRight * pixelsPerMeter,
                        -_radarBuf[i].DotForward * pixelsPerMeter
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

                    // Selected: hostile diamond glyph (with armed line). Others: plain square.
                    // Visible content fills ~70% of canvas, so sprite size is ~1.5× iconSize.
                    string contactTex = isSelected ? TEX_C_HOSTILE : TEX_C_UNKNOWN;
                    float iconSize = clamped ? 9f : 12f;
                    SpriteHelpers.Sp(contactTex, pos.X, pos.Y, iconSize, iconSize, contactColor);

                    // Bearing line for dangerous/imminent threats
                    if (timeToClosest < 15 && closingSpeed > 0)
                    {
                        SpriteHelpers.AddLineSprite(radarCenter, pos, 1f, Cr(contactColor, 0.35f));
                    }

                    // Range label for close contacts that fit on radar
                    if (dist < radarRange * 0.8f && !clamped)
                    {
                        string rangeText = dist >= 1000 ? $"{dist / 1000:F1}" : $"{dist:F0}";
                        SpriteHelpers.Tt(rangeText, pos.X + 7f, pos.Y - 4f, 0.28f, contactColor, MFDTheme.AL);
                    }
                }

                // Threat count below radar
                if (n > 0)
                {
                    SpriteHelpers.Tt($"TGT: {n}", radarCenter.X, radarOrigin.Y + radarSize.Y + 5f, 0.4f, HUD_PRIMARY);
                }
            }

            private static float RoundToNiceRange(float range)
            {
                if (range >= 10000) return (float)Rd(range / 5000) * 5000;
                if (range >= 1000) return (float)Rd(range / 1000) * 1000;
                if (range >= 100) return (float)Rd(range / 500) * 500;
                return (float)Rd(range / 100) * 100;
            }

            private void DrawFormationGhosts(IMyTextSurface hud, MatrixD worldToCockpitMatrix)
            {
                var friends = Datalink.GetActiveFriendlies();
                if (friends.Count == 0) return;

                Vector3D shooterPosition = myjet.CockpitPosition;
                Vector2 surfaceSize = SS(hud);
                Vector2 center = surfaceSize / 2f;

                for (int i = 0; i < friends.Count; i++)
                {
                    Vector3D directionToWingman = friends[i].Position - shooterPosition;
                    Vector3D localDirection = VTN(directionToWingman, worldToCockpitMatrix);

                    if (localDirection.Z >= 0) continue;

                    if (Ab(localDirection.Z) < MIN_Z_FOR_PROJECTION)
                        localDirection.Z = -MIN_Z_FOR_PROJECTION;

                    Vector2 ghostPos = SpriteHelpers.ProjectToScreen(localDirection, center, surfaceSize);
                    SpriteHelpers.Sp(TEX_C_FRIENDLY, ghostPos.X, ghostPos.Y, 18f, 18f, Cr(HUD_RADAR_FRIENDLY, 0.7f));
                }
            }
        }
    }
}
