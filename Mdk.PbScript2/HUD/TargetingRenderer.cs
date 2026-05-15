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
        IMyTextSurface hud,
        MatrixD worldToCockpitMatrix,
        Vector3D shooterPosition,
        Vector3D targetPosition,
        Vector3D interceptPoint,
        Vector3D aimPoint,
        double timeToIntercept,
        bool isAimingAtPip,
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

                // Use aimPoint (accounts for bullet drop) instead of interceptPoint (target future position)
                Vector3D directionToIntercept = aimPoint - shooterPosition;
                Vector3D localDirectionToIntercept = VTN(directionToIntercept, worldToCockpitMatrix);

                Vector2 surfaceSize = SS(hud);
                Vector2 center = surfaceSize / 2f;
                float viewportMinDim = Mn(surfaceSize.X, surfaceSize.Y);
                float targetMarkerSize = viewportMinDim * 0.02f;
                float lineThickness = Mx(1f, viewportMinDim * 0.004f);
                float reticleArmLength = viewportMinDim * 0.025f;
                float arrowSize = viewportMinDim * 0.04f;
                float arrowHeadSize = viewportMinDim * 0.025f;
                double distanceToIntercept = VDi(shooterPosition, interceptPoint);
                float distanceScaleFactor = Cl((float)((MAX_DISTANCE_FOR_SCALING - distanceToIntercept) / (MAX_DISTANCE_FOR_SCALING - MIN_DISTANCE_FOR_SCALING)), 0.0f, 1.0f);
                float currentPipSizeFactor = MathHelper.Lerp(MIN_PIP_SIZE_FACTOR, MAX_PIP_SIZE_FACTOR, distanceScaleFactor);
                float dynamicPipSize = viewportMinDim * currentPipSizeFactor;


                // Boresight crosshair — single sprite (visible cross spans 176/256 of canvas).
                float boresightSize = reticleArmLength * 2f * 256f / 176f;

                if (localDirectionToIntercept.Z > MIN_Z_FOR_PROJECTION)
                {
                    SpriteHelpers.Sp(TEX_BORESIGHT, center.X, center.Y, boresightSize, boresightSize, behindColor);
                    return;
                }

                SpriteHelpers.Sp(TEX_BORESIGHT, center.X, center.Y, boresightSize, boresightSize, reticleColor);


                if (Ab(localDirectionToIntercept.Z) < MIN_Z_FOR_PROJECTION)
                {
                    localDirectionToIntercept.Z = -MIN_Z_FOR_PROJECTION;
                }


                Vector2 pipScreenPos = SpriteHelpers.ProjectToScreen(localDirectionToIntercept, center, surfaceSize);

                bool isOnScreen = pipScreenPos.X >= 0 && pipScreenPos.X <= surfaceSize.X &&
                                  pipScreenPos.Y >= 0 && pipScreenPos.Y <= surfaceSize.Y;
                if (isOnScreen)
                {
                    SpriteHelpers.Sp(TEX_LEAD_PIP, pipScreenPos.X, pipScreenPos.Y, dynamicPipSize, dynamicPipSize, pipColor);

                    // Draw time-to-intercept (TTI) near the lead pip
                    if (timeToIntercept > 0 && timeToIntercept < 30)
                    {
                        string ttiText = $"{timeToIntercept:F1}s";
                        Color ttiColor = timeToIntercept < 2 ? HUD_WARNING : (timeToIntercept < 5 ? HUD_EMPHASIS : HUD_PRIMARY);

                        SpriteHelpers.Tt(ttiText, pipScreenPos.X + dynamicPipSize / 2 + 8f, pipScreenPos.Y - 8f, 0.5f, ttiColor, MFDTheme.AL);

                        // Draw range to intercept point
                        string rangeText = SpriteHelpers.FormatRange(distanceToIntercept);
                        SpriteHelpers.Tt(rangeText, pipScreenPos.X + dynamicPipSize / 2 + 8f, pipScreenPos.Y + 4f, 0.45f, ttiColor, MFDTheme.AL);
                    }

                    Vector3D directionToTarget = targetPosition - shooterPosition;
                    Vector3D localDirectionToTarget = VTN(directionToTarget, worldToCockpitMatrix);

                    if (localDirectionToTarget.Z < -MIN_Z_FOR_PROJECTION)
                    {
                        Vector2 currentTargetScreenPos = SpriteHelpers.ProjectToScreen(localDirectionToTarget, center, surfaceSize);
                        float halfMark = targetMarkerSize / 2f;
                        SpriteHelpers.AddLineSprite(currentTargetScreenPos - V2(halfMark, halfMark), currentTargetScreenPos + V2(halfMark, halfMark), lineThickness, Color.Yellow);
                        SpriteHelpers.AddLineSprite(currentTargetScreenPos - V2(halfMark, -halfMark), currentTargetScreenPos + V2(halfMark, -halfMark), lineThickness, Color.Yellow);
                        SpriteHelpers.AddLineSprite(pipScreenPos, currentTargetScreenPos, lineThickness, Color.Yellow);
                    }
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
                    float absEdgeX = Ab(edgeX);
                    float absEdgeY = Ab(edgeY);

                    // Add epsilon to prevent division by zero
                    if (absEdgeX < 1e-6f) absEdgeX = 1e-6f;
                    if (absEdgeY < 1e-6f) absEdgeY = 1e-6f;

                    if (Ab(edgeX / maxDistX) > Ab(edgeY / maxDistY))
                    {
                        edgePoint = V2(center.X + Sg(edgeX) * maxDistX, center.Y + edgeY * (maxDistX / absEdgeX));
                    }
                    else
                    {
                        edgePoint = V2(center.X + edgeX * (maxDistY / absEdgeY), center.Y + Sg(edgeY) * maxDistY);
                    }


                    edgePoint.X = Cl(edgePoint.X, arrowSize / 2f, surfaceSize.X - arrowSize / 2f);
                    edgePoint.Y = Cl(edgePoint.Y, arrowSize / 2f, surfaceSize.Y - arrowSize / 2f);


                    float arrowRotation = (float)At2(direction.Y, direction.X);
                    SpriteHelpers.Sp(TEX_NAV_ARROW, edgePoint.X, edgePoint.Y, arrowHeadSize, arrowHeadSize, offScreenColor, arrowRotation + (float)PI / 2f);

                    // Range label next to off-screen arrow
                    double offscreenRange = VDi(shooterPosition, targetPosition);
                    string offscreenRangeText = SpriteHelpers.FormatRange(offscreenRange);
                    Vector2 perpDir = V2(-direction.Y, direction.X);
                    Vector2 labelPos = edgePoint + perpDir * 14f - direction * 10f;
                    SpriteHelpers.Tt(offscreenRangeText, labelPos.X, labelPos.Y, 0.45f, offScreenColor);
                }
            }

            private void DrawTargetBrackets(
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
                double aspectAngle = At2(VX(targetForward, toShooter).Length(), VD(targetForward, toShooter)) * (180.0 / PI);

                Vector3D directionToTargetLocal = VTN(targetPosition - shooterPosition, worldToCockpitMatrix);

                if (Ab(directionToTargetLocal.Z) < MIN_Z_FOR_PROJECTION)
                    directionToTargetLocal.Z = -MIN_Z_FOR_PROJECTION;

                if (directionToTargetLocal.Z >= 0) return;

                Vector2 surfaceSize = SS(hud);
                Vector2 center = surfaceSize / 2f;

                Vector2 targetScreenPos = SpriteHelpers.ProjectToScreen(directionToTargetLocal, center, surfaceSize);

                bool isOnScreen = targetScreenPos.X >= 0 && targetScreenPos.X <= surfaceSize.X &&
                                  targetScreenPos.Y >= 0 && targetScreenPos.Y <= surfaceSize.Y;

                if (!isOnScreen) return;

                float bracketSize = Cl((float)(3000.0 / range), 20f, 80f);

                Color bracketColor = closureRate > 10 ? HUD_WARNING :
                                   closureRate < -10 ? HUD_EMPHASIS : HUD_PRIMARY;

                // Single sprite — visible bracket spans 160/256 of canvas, so render size = bracketSize * 1.6.
                SpriteHelpers.Sp(TEX_TGT_BRACKET, targetScreenPos.X, targetScreenPos.Y,
                    bracketSize * 1.6f, bracketSize * 1.6f, bracketColor);

                // STT lock indicator — flashing diamond inside the bracket when track-locked.
                if (radarControl != null && radarControl.IsTrackLocked && Anim.Blink(0.27))
                {
                    SpriteHelpers.Sp(TEX_LOCK_DIAMOND, targetScreenPos.X, targetScreenPos.Y,
                        bracketSize * 1.0f, bracketSize * 1.0f, HUD_WARNING);
                }

                float textY = targetScreenPos.Y + bracketSize/2 + 5f;
                float textScale = 0.5f;

                string rangeText = SpriteHelpers.FormatRange(range);
                SpriteHelpers.Tt(rangeText, targetScreenPos.X, textY, textScale, bracketColor);

                string closureLabel = closureRate > 10 ? "HOT" : closureRate < -10 ? "COLD" : "---";
                string closureText = $"Vc:{Ab(closureRate):F0} {closureLabel}";
                SpriteHelpers.Tt(closureText, targetScreenPos.X, textY + 12f, textScale, bracketColor);

                string aspectText = $"AA:{aspectAngle:F0}\u00B0";
                SpriteHelpers.Tt(aspectText, targetScreenPos.X, textY + 24f, textScale, bracketColor);
            }

            private void DrawUnifiedContactBrackets(
                IMyTextSurface hud,
                MatrixD worldToCockpitMatrix,
                Vector3D shooterPosition,
                Vector3D shooterVelocity
            )
            {
                if (hud == null || myjet == null) return;
                for (int i = 0; i < myjet.enemyList.Count; i++)
                {
                    var c = myjet.enemyList[i];
                    if (myjet.IsSelected(c)) continue;
                    Vector3D p = c.Position + (c.Velocity - shooterVelocity) * SystemManager.DeltaSeconds;
                    DrawSimpleHudBracket(hud, worldToCockpitMatrix, shooterPosition, p, Cr(myjet.GetEnemyContactColor(c), 0.58f));
                }

                var friends = Datalink.GetActiveFriendlies();
                for (int i = 0; i < friends.Count; i++)
                    DrawSimpleHudBracket(hud, worldToCockpitMatrix, shooterPosition, friends[i].Position, Cr(Color.DeepSkyBlue, 0.78f));
            }

            private void DrawSimpleHudBracket(IMyTextSurface hud, MatrixD worldToCockpitMatrix, Vector3D shooterPosition, Vector3D targetPosition, Color color)
            {
                double range = VDi(shooterPosition, targetPosition);
                if (range < 1) return;
                Vector3D local = VTN(targetPosition - shooterPosition, worldToCockpitMatrix);
                if (Ab(local.Z) < MIN_Z_FOR_PROJECTION)
                    local.Z = -MIN_Z_FOR_PROJECTION;
                if (local.Z >= 0) return;

                Vector2 surfaceSize = SS(hud);
                Vector2 pos = SpriteHelpers.ProjectToScreen(local, surfaceSize / 2f, surfaceSize);
                if (pos.X < 0 || pos.X > surfaceSize.X || pos.Y < 0 || pos.Y > surfaceSize.Y) return;
                float s = Cl((float)(2200.0 / range), 16f, 52f) * 1.6f;
                SpriteHelpers.Sp(TEX_TGT_BRACKET, pos.X, pos.Y, s, s, color);
            }

            private void DrawGunFunnel(
                IMyTextSurface hud,
                MatrixD worldToCockpitMatrix,
                Vector3D interceptPoint,
                Vector3D shooterPosition,
                double range,
                bool isAimingAtPip
            )
            {
                if (hud == null) return;

                Vector2 surfaceSize = SS(hud);
                Vector2 center = surfaceSize / 2f;

                float funnelWidthFactor = Cl((float)(range / 2000.0), 0.05f, 0.3f);
                float funnelBaseWidth = surfaceSize.X * funnelWidthFactor;

                Vector3D directionToIntercept = interceptPoint - shooterPosition;
                Vector3D localDirectionToIntercept = VTN(directionToIntercept, worldToCockpitMatrix);

                if (localDirectionToIntercept.Z >= 0) return;

                if (Ab(localDirectionToIntercept.Z) < MIN_Z_FOR_PROJECTION)
                    localDirectionToIntercept.Z = -MIN_Z_FOR_PROJECTION;

                Vector2 pipScreenPos = SpriteHelpers.ProjectToScreen(localDirectionToIntercept, center, surfaceSize);

                Color funnelColor = Cr(HUD_PRIMARY, 0.3f);
                float lineThickness = 1f;

                float halfFunnel = funnelBaseWidth / 2f;
                SpriteHelpers.AddLineSprite(V2(center.X - halfFunnel, 0), pipScreenPos, lineThickness, funnelColor);
                SpriteHelpers.AddLineSprite(V2(center.X + halfFunnel, 0), pipScreenPos, lineThickness, funnelColor);
                SpriteHelpers.AddLineSprite(V2(center.X + halfFunnel, surfaceSize.Y), pipScreenPos, lineThickness, funnelColor);
                SpriteHelpers.AddLineSprite(V2(center.X - halfFunnel, surfaceSize.Y), pipScreenPos, lineThickness, funnelColor);

                if (isAimingAtPip && range < 2500)
                {
                    string cueText = range < 1500 ? "SHOOT" : "IN RANGE";
                    Color cueColor = range < 1500 ? HUD_WARNING : HUD_EMPHASIS;
                    SpriteHelpers.Tt(cueText, center.X, center.Y - 60f, 1.0f, cueColor, MFDTheme.AC, MFDTheme.FONT_W);
                }
            }

            private void DrawBreakawayWarning(double altitude, Vector3D velocity, Vector3D targetPosition, Vector3D shooterPosition, Vector3D targetVelocity)
            {
                bool lowAltitudeWarning = altitude < 100 && verticalVelocityMps < -5;
                bool collisionWarning = false;

                if (targetPosition != VZ)
                {
                    double range = VDi(shooterPosition, targetPosition);
                    Vector3D relativeVelocity = velocity - targetVelocity;
                    Vector3D toTarget = VN(targetPosition - shooterPosition);
                    double closureRate = -VD(relativeVelocity, toTarget);

                    if (range < 500 && closureRate > 100)
                        collisionWarning = true;
                }

                if (!lowAltitudeWarning && !collisionWarning) return;

                Vector2 center = SS(hud) / 2f;
                float xSize = Mn(SX(hud), SY(hud)) * 0.4f;
                Color warningColor = HUD_WARNING;

                if (Anim.Blink(0.33))
                {
                    SpriteHelpers.Sp(TEX_GLYPH_CROSS, center.X, center.Y, xSize, xSize, warningColor);

                    string warningText = lowAltitudeWarning ? "PULL UP" : "BREAK AWAY";
                    SpriteHelpers.Tt(warningText, center.X, center.Y + xSize / 2f + 12f, 1.2f, warningColor, MFDTheme.AC, MFDTheme.FONT_W);
                }
            }

        }
    }
}
