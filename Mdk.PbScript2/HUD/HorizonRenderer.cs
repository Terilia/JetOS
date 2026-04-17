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
            private List<MySprite> _horizonSprites = new List<MySprite>();

            private void DrawArtificialHorizon(
                MySpriteDrawFrame frame,
                float pitch,
                float roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                List<MySprite> sprites = _horizonSprites;
                sprites.Clear();

                // Compute visible pitch range — typically ~5 lines instead of 36 iterations
                float halfVisibleDeg = (hud.SurfaceSize.Y / 2f + 100f) / pixelsPerDegree;
                int loopMin = Mx(-90, (int)Math.Floor((pitch - halfVisibleDeg) / 5f) * 5);
                int loopMax = Mn(90, (int)Math.Ceiling((pitch + halfVisibleDeg) / 5f) * 5);

                for (int i = loopMin; i <= loopMax; i += 5)
                {
                    if (i == 0)
                        continue;

                    float markerY = centerY - (i - pitch) * pixelsPerDegree;

                    bool isPositive = (i > 0);

                    float lineWidth = 90f;
                    float lineThickness = 2f;
                    Color lineColor = HUD_PRIMARY;

                    float halfWidth = lineWidth * 1.225f;

                    if (!isPositive)
                    {
                        // Solid lines for nose-up pitch lines (i<0 because pitch sign is inverted:
                        // pitch = asin(dot(forward, gravityDown)), so nose-up = negative pitch)
                        sprites.Add(SpriteHelpers.FBx(centerX * 0.75f, markerY, lineWidth, lineThickness, lineColor));
                        sprites.Add(SpriteHelpers.FBx(centerX * 1.25f, markerY, lineWidth, lineThickness, lineColor));
                    }
                    else
                    {
                        // Dashed lines for nose-down pitch lines (i>0 = below horizon)
                        int dashCount = 4;
                        float totalDashWidth = lineWidth;
                        float dashWidth = totalDashWidth / (dashCount * 2 - 1);

                        for (int d = 0; d < dashCount; d++)
                        {
                            float dashOffset = -totalDashWidth / 2f + d * (dashWidth * 2) + dashWidth / 2f;
                            sprites.Add(SpriteHelpers.FBx(centerX * 0.75f + dashOffset, markerY, dashWidth, lineThickness, lineColor));
                            sprites.Add(SpriteHelpers.FBx(centerX * 1.25f + dashOffset, markerY, dashWidth, lineThickness, lineColor));
                        }
                    }

                    float tipLength = 12f;

                    string label = Ab(i).ToString();
                    float labelOffsetX = halfWidth + tipLength + 10f;

                    sprites.Add(SpriteHelpers.FTt(label, centerX - labelOffsetX, markerY + 10f, 0.8f, lineColor, MFDTheme.AR, MFDTheme.FONT_W));
                    sprites.Add(SpriteHelpers.FTt(label, centerX + labelOffsetX, markerY + 10f, 0.8f, lineColor, MFDTheme.AL, MFDTheme.FONT_W));
                }

                float horizonY = centerY + pitch * pixelsPerDegree;
                sprites.Add(SpriteHelpers.FBx(centerX * 1.25f, horizonY, hud.SurfaceSize.X * 0.125f, 4f, HUD_HORIZON));
                sprites.Add(SpriteHelpers.FBx(centerX * 0.75f, horizonY, hud.SurfaceSize.X * 0.125f, 4f, HUD_HORIZON));

                float rollRad = ToRad(-roll);
                float cosRoll = (float)Cs(rollRad);
                float sinRoll = (float)Sn(rollRad);

                for (int s = 0; s < sprites.Count; s++)
                {
                    MySprite sprite = sprites[s];
                    Vector2 pos = sprite.Position ?? Vector2.Zero;
                    Vector2 offset = pos - V2(centerX, centerY);

                    Vector2 rotated = V2(
                        offset.X * cosRoll - offset.Y * sinRoll,
                        offset.X * sinRoll + offset.Y * cosRoll
                    );

                    sprite.Position = rotated + V2(centerX, centerY);

                    if (sprite.Type == MFDTheme.TX)
                    {
                        float existing = sprite.RotationOrScale;
                        sprite.RotationOrScale = existing + rollRad;
                    }

                    sprites[s] = sprite;

                    frame.Add(sprite);
                }

            }

            // F-18 style aircraft waterline / reference symbol — classic "W" shape:
            // horizontal wings dipping into a center V. Pilot preference over the
            // F-16 gun cross.
            private void DrawAircraftSymbol(MySpriteDrawFrame frame, float centerX, float centerY)
            {
                float wingSpan = 35f;
                float innerSpan = 10f;
                float dipDepth = 6f;
                float refThickness = 2.5f;
                Color refColor = HUD_EMPHASIS;

                SpriteHelpers.AddLineSprite(frame, V2(centerX - wingSpan, centerY),
                    V2(centerX - innerSpan, centerY), refThickness, refColor);
                SpriteHelpers.AddLineSprite(frame, V2(centerX - innerSpan, centerY),
                    V2(centerX, centerY + dipDepth), refThickness, refColor);
                SpriteHelpers.AddLineSprite(frame, V2(centerX, centerY + dipDepth),
                    V2(centerX + innerSpan, centerY), refThickness, refColor);
                SpriteHelpers.AddLineSprite(frame, V2(centerX + innerSpan, centerY),
                    V2(centerX + wingSpan, centerY), refThickness, refColor);
            }

            private void DrawBankAngleMarkers(MySpriteDrawFrame frame, float centerX, float centerY, float roll, float pixelsPerDegree)
            {
                int[] bankAngles = new int[] { 15, 30, 45, 60, -15, -30, -45, -60 };
                float horizonRadius = pixelsPerDegree * 20f;

                float rollRad = ToRad(-roll);
                float cosRoll = (float)Cs(rollRad);
                float sinRoll = (float)Sn(rollRad);

                foreach (int angle in bankAngles)
                {
                    float angleRad = ToRad(angle);
                    Vector2 tickPos = V2((float)Sn(angleRad) * horizonRadius, -(float)Cs(angleRad) * horizonRadius);

                    Vector2 rotatedTick = V2(
                        tickPos.X * cosRoll - tickPos.Y * sinRoll,
                        tickPos.X * sinRoll + tickPos.Y * cosRoll
                    );

                    Vector2 finalPos = V2(centerX, centerY) + rotatedTick;

                    bool isMajor = (Ab(angle) % 30 == 0);
                    float tickLength = isMajor ? 8f : 5f;
                    Color tickColor = isMajor ? HUD_EMPHASIS : HUD_SECONDARY;

                    SpriteHelpers.Bx(frame, finalPos.X, finalPos.Y, 2f, tickLength, tickColor, angleRad + rollRad);
                }

                // Roll pointer — fixed index triangle at 12 o'clock of bank arc
                // Doesn't rotate; the bank ticks slide past it to indicate current roll
                SpriteHelpers.Sp(frame, "Triangle", centerX, centerY - horizonRadius - 6f, 10f, 8f, HUD_PRIMARY, (float)PI);
            }

            // F-18 Flight Path Marker (velocity vector symbol).
            // Compact hollow circle + stumpy horizontal wings flush with the circle +
            // a short vertical tail tick flush on top. Earth-stabilized: wings/tail
            // counter-rotate with roll so they stay parallel to the true horizon.
            // Drawn in HUD primary color (monochrome HUD convention).
            private void DrawFlightPathMarker(
                MySpriteDrawFrame frame,
                Vector3D currentVelocity,
                MatrixD worldToCockpitMatrix,
                double roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                if (currentVelocity.LengthSquared() < 1.0) return;

                const float CircleSize = 11f;
                const float WingLength = 13f;
                const float WingThickness = 1.8f;
                const float WingGap = 0f;    // F-18: wings touch circle edge
                const float TailLength = 6f; // short tail tick, flush with top

                // Use perspective projection (same as lead pip / target brackets)
                // to get a physically correct screen position for the velocity vector.
                Vector3D velocityDirection = VN(currentVelocity);
                Vector3D localVelocity = VTN(velocityDirection, worldToCockpitMatrix);

                // Only draw when velocity has a forward component
                if (localVelocity.Z >= 0) return;
                if (Ab(localVelocity.Z) < MIN_Z_FOR_PROJECTION)
                    localVelocity.Z = -MIN_Z_FOR_PROJECTION;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 markerPosition = SpriteHelpers.ProjectToScreen(localVelocity, V2(centerX, centerY), surfaceSize);

                Color fpmColor = HUD_PRIMARY;

                // Hollow circle (CircleHollow texture)
                SpriteHelpers.Sp(frame, TEXTURE_CIRCLE, markerPosition.X, markerPosition.Y,
                    CircleSize, CircleSize, fpmColor);

                // Boresight-to-FPM connector — only when FPM is off-screen
                bool fpmOnScreen = markerPosition.X >= 0 && markerPosition.X <= surfaceSize.X &&
                                   markerPosition.Y >= 0 && markerPosition.Y <= surfaceSize.Y;
                if (!fpmOnScreen)
                {
                    Vector2 boresight = V2(centerX, centerY);
                    SpriteHelpers.AddLineSprite(frame, boresight, markerPosition, 1f, Cr(fpmColor, 0.35f));
                }

                // Wings + tail counter-rotate by roll to stay horizon-aligned (F-16 convention)
                float rollRad = ToRad((float)roll);
                float halfCircle = CircleSize * 0.5f;

                // Left wing: center is half-wing outside the circle
                Vector2 leftWingOffset = V2(-(halfCircle + WingGap + WingLength / 2f), 0f);
                Vector2 rotLeftWing = SpriteHelpers.RotatePoint(leftWingOffset, Vector2.Zero, -rollRad);
                Vector2 lw = markerPosition + rotLeftWing;
                SpriteHelpers.Bx(frame, lw.X, lw.Y, WingLength, WingThickness, fpmColor, -rollRad);

                // Right wing
                Vector2 rightWingOffset = V2(halfCircle + WingGap + WingLength / 2f, 0f);
                Vector2 rotRightWing = SpriteHelpers.RotatePoint(rightWingOffset, Vector2.Zero, -rollRad);
                Vector2 rw = markerPosition + rotRightWing;
                SpriteHelpers.Bx(frame, rw.X, rw.Y, WingLength, WingThickness, fpmColor, -rollRad);

                // Vertical stabilizer tick: center is half-tail above the circle
                Vector2 tailOffset = V2(0f, -(halfCircle + TailLength / 2f));
                Vector2 rotTail = SpriteHelpers.RotatePoint(tailOffset, Vector2.Zero, -rollRad);
                Vector2 tp = markerPosition + rotTail;
                SpriteHelpers.Bx(frame, tp.X, tp.Y, WingThickness, TailLength, fpmColor, -rollRad);
            }
        }
    }
}
