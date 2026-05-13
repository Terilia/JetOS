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
                float halfVisibleDeg = (SY(hud) / 2f + 100f) / pixelsPerDegree;
                int loopMin = Mx(-90, (int)Math.Floor((pitch - halfVisibleDeg) / 5f) * 5);
                int loopMax = Mn(90, (int)Math.Ceiling((pitch + halfVisibleDeg) / 5f) * 5);

                for (int i = loopMin; i <= loopMax; i += 5)
                {
                    if (i == 0)
                        continue;

                    float markerY = centerY - (i - pitch) * pixelsPerDegree;

                    bool isPositive = (i > 0);

                    float lineWidth = 90f;
                    Color lineColor = HUD_PRIMARY;

                    float halfWidth = lineWidth * 1.225f;

                    // Climb rungs (i<0, nose-up) use solid+down-ticks sprite; dive rungs use dashed+up-ticks.
                    // Position rotates with roll via the closing pass; orientation stays so the rung tilts as a unit.
                    string rungTex = isPositive ? TEX_PITCH_NEG : TEX_PITCH_POS;
                    float rungW = halfWidth * 2f + lineWidth;
                    float rungH = rungW * 0.25f;
                    sprites.Add(new MySprite(SpriteType.TEXTURE, rungTex, V2(centerX, markerY), V2(rungW, rungH), lineColor));

                    float tipLength = 12f;
                    string label = Ab(i).ToString();
                    float labelOffsetX = halfWidth + tipLength + 10f;

                    sprites.Add(SpriteHelpers.FTt(label, centerX - labelOffsetX, markerY + 10f, 0.8f, lineColor, MFDTheme.AR, MFDTheme.FONT_W));
                    sprites.Add(SpriteHelpers.FTt(label, centerX + labelOffsetX, markerY + 10f, 0.8f, lineColor, MFDTheme.AL, MFDTheme.FONT_W));
                }

                float horizonY = centerY + pitch * pixelsPerDegree;
                sprites.Add(SpriteHelpers.FBx(centerX * 1.25f, horizonY, SX(hud) * 0.125f, 4f, HUD_HORIZON));
                sprites.Add(SpriteHelpers.FBx(centerX * 0.75f, horizonY, SX(hud) * 0.125f, 4f, HUD_HORIZON));

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

            // F-18 style aircraft waterline / reference symbol — wing-tip-dipping "W".
            // Single sprite from the JetOS-Sprites mod. Visible content spans canvas
            // x=40..216 (176/256 = 68.75%) and y=128..156 (28/256 = 11%). Sized so the
            // visible width matches the original 70px wingspan.
            private void DrawAircraftSymbol(float centerX, float centerY)
            {
                const float SPRITE_W = 102f;   // → visible 70px wide (matches old 2*wingSpan)
                const float SPRITE_H = 102f;   // square sprite, visible content is short+wide
                // Source canvas places the W's horizontal at y=128 (center) and the V tip
                // at y=156 (offset +28). The visible center sits ~14px below sprite center;
                // shift anchor up so the wing line lands on (centerX, centerY).
                SpriteHelpers.Sp(TEX_AIRCRAFT_SYM, centerX, centerY - SPRITE_H * 14f / 256f, SPRITE_W, SPRITE_H, HUD_EMPHASIS);
            }

            private void DrawBankAngleMarkers(float centerX, float centerY, float roll, float pixelsPerDegree)
            {
                float horizonRadius = pixelsPerDegree * 20f;
                float rollRad = ToRad(-roll);

                // Bank arc + ticks baked into one sprite. Source arc radius = 100/256 of canvas;
                // render size = horizonRadius / 0.39 so the arc lands at horizonRadius from center.
                float arcSize = horizonRadius / 0.39f;
                SpriteHelpers.Sp(TEX_BANK_ARC, centerX, centerY, arcSize, arcSize, HUD_EMPHASIS, rollRad);

                // Fixed roll-pointer triangle at 12 o'clock — doesn't rotate; arc sweeps past it.
                SpriteHelpers.Sp(TEX_ROLL_POINTER, centerX, centerY - horizonRadius - 6f, 10f, 8f, HUD_PRIMARY);
            }

            // F-18 Flight Path Marker. Single sprite from the JetOS-Sprites mod,
            // counter-rotated by roll so the wings stay parallel to the true horizon.
            private void DrawFlightPathMarker(
                Vector3D currentVelocity,
                MatrixD worldToCockpitMatrix,
                double roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                if (currentVelocity.LengthSquared() < 1.0) return;

                const float FpmDrawSize = 48f;

                Vector3D velocityDirection = VN(currentVelocity);
                Vector3D localVelocity = VTN(velocityDirection, worldToCockpitMatrix);

                if (localVelocity.Z >= 0) return;
                if (Ab(localVelocity.Z) < MIN_Z_FOR_PROJECTION)
                    localVelocity.Z = -MIN_Z_FOR_PROJECTION;

                Vector2 surfaceSize = SS(hud);
                Vector2 markerPosition = SpriteHelpers.ProjectToScreen(localVelocity, V2(centerX, centerY), surfaceSize);

                Color fpmColor = HUD_PRIMARY;

                bool fpmOnScreen = markerPosition.X >= 0 && markerPosition.X <= surfaceSize.X &&
                                   markerPosition.Y >= 0 && markerPosition.Y <= surfaceSize.Y;
                if (!fpmOnScreen)
                {
                    Vector2 boresight = V2(centerX, centerY);
                    SpriteHelpers.AddLineSprite(boresight, markerPosition, 1f, Cr(fpmColor, 0.35f));
                }

                float rollRad = ToRad((float)roll);
                SpriteHelpers.Sp(TEXTURE_FPM, markerPosition.X, markerPosition.Y,
                    FpmDrawSize, FpmDrawSize, fpmColor, -rollRad);
            }
        }
    }
}
