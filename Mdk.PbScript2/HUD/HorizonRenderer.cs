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

                    if (!isPositive)
                    {
                        // Solid lines for nose-up pitch lines (i<0 because pitch sign is inverted:
                        // pitch = asin(dot(forward, gravityDown)), so nose-up = negative pitch)
                        sprites.Add(new MySprite()
                        {
                            Type = MFDTheme.TX,
                            Data = MFDTheme.SQ,
                            Position = new Vector2(centerX * 0.75f, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = MFDTheme.AC
                        });
                        sprites.Add(new MySprite()
                        {
                            Type = MFDTheme.TX,
                            Data = MFDTheme.SQ,
                            Position = new Vector2(centerX * 1.25f, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = MFDTheme.AC
                        });
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
                            sprites.Add(new MySprite()
                            {
                                Type = MFDTheme.TX,
                                Data = MFDTheme.SQ,
                                Position = new Vector2(centerX * 0.75f + dashOffset, markerY),
                                Size = new Vector2(dashWidth, lineThickness),
                                Color = lineColor,
                                Alignment = MFDTheme.AC
                            });
                            sprites.Add(new MySprite()
                            {
                                Type = MFDTheme.TX,
                                Data = MFDTheme.SQ,
                                Position = new Vector2(centerX * 1.25f + dashOffset, markerY),
                                Size = new Vector2(dashWidth, lineThickness),
                                Color = lineColor,
                                Alignment = MFDTheme.AC
                            });
                        }
                    }

                    float tipLength = 12f;
                    float tipAngle = ToRad(isPositive ? 45f : -45f);

                    string label = Math.Abs(i).ToString();
                    float labelOffsetX = halfWidth + tipLength + 10f;

                    sprites.Add(
                        new MySprite()
                        {
                            Type = MFDTheme.TT,
                            Data = label,
                            Position = new Vector2(centerX - labelOffsetX, markerY + 10f),
                            RotationOrScale = 0.8f,
                            Color = lineColor,
                            Alignment = MFDTheme.AR,
                            FontId = MFDTheme.FONT_W
                        }
                    );
                    sprites.Add(
                        new MySprite()
                        {
                            Type = MFDTheme.TT,
                            Data = label,
                            Position = new Vector2(centerX + labelOffsetX, markerY + 10f),
                            RotationOrScale = 0.8f,
                            Color = lineColor,
                            Alignment = MFDTheme.AL,
                            FontId = MFDTheme.FONT_W
                        }
                    );
                }

                float horizonY = centerY + pitch * pixelsPerDegree;
                sprites.Add(
                    new MySprite()
                    {
                        Type = MFDTheme.TX,
                        Data = MFDTheme.SQ,
                        Position = new Vector2(centerX * 1.25f, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = HUD_HORIZON,
                        Alignment = MFDTheme.AC
                    }
                );
                sprites.Add(
                    new MySprite()
                    {
                        Type = MFDTheme.TX,
                        Data = MFDTheme.SQ,
                        Position = new Vector2(centerX * 0.75f, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = HUD_HORIZON,
                        Alignment = MFDTheme.AC
                    }
                );
                float rollRad = ToRad(-roll);
                float cosRoll = (float)Cs(rollRad);
                float sinRoll = (float)Sn(rollRad);

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

                    if (sprite.Type == MFDTheme.TX)
                    {
                        float existing = sprite.RotationOrScale;
                        sprite.RotationOrScale = existing + rollRad;
                    }

                    sprites[s] = sprite;

                    frame.Add(sprite);
                }

            }

            private void DrawAircraftSymbol(MySpriteDrawFrame frame, float centerX, float centerY)
            {
                float wingSpan = 35f;
                float innerSpan = 10f;
                float dipDepth = 6f;
                float refThickness = 2.5f;
                Color refColor = HUD_EMPHASIS;

                SpriteHelpers.AddLineSprite(frame, new Vector2(centerX - wingSpan, centerY),
                    new Vector2(centerX - innerSpan, centerY), refThickness, refColor);
                SpriteHelpers.AddLineSprite(frame, new Vector2(centerX - innerSpan, centerY),
                    new Vector2(centerX, centerY + dipDepth), refThickness, refColor);
                SpriteHelpers.AddLineSprite(frame, new Vector2(centerX, centerY + dipDepth),
                    new Vector2(centerX + innerSpan, centerY), refThickness, refColor);
                SpriteHelpers.AddLineSprite(frame, new Vector2(centerX + innerSpan, centerY),
                    new Vector2(centerX + wingSpan, centerY), refThickness, refColor);
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
                    Vector2 tickPos = new Vector2((float)Sn(angleRad) * horizonRadius, -(float)Cs(angleRad) * horizonRadius);

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
                        Type = MFDTheme.TX,
                        Data = MFDTheme.SQ,
                        Position = finalPos,
                        Size = new Vector2(2f, tickLength),
                        Color = tickColor,
                        Alignment = MFDTheme.AC,
                        RotationOrScale = angleRad + rollRad
                    });
                }
            }

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

                const float MarkerSize = 20f;
                const float WingLength = 15f;
                const float WingThickness = 2f;
                const float WingOffsetX = 10f;

                // Use perspective projection (same as lead pip / target brackets)
                // to get a physically correct screen position for the velocity vector.
                Vector3D velocityDirection = VN(currentVelocity);
                Vector3D localVelocity = VTN(velocityDirection, worldToCockpitMatrix);

                // Only draw when velocity has a forward component
                if (localVelocity.Z >= 0) return;
                if (Math.Abs(localVelocity.Z) < MIN_Z_FOR_PROJECTION)
                    localVelocity.Z = -MIN_Z_FOR_PROJECTION;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 markerPosition = SpriteHelpers.ProjectToScreen(localVelocity, new Vector2(centerX, centerY), surfaceSize);

                frame.Add(new MySprite
                {
                    Type = MFDTheme.TX,
                    Data = TEXTURE_CIRCLE_SOLID,
                    Position = markerPosition,
                    Size = new Vector2(MarkerSize, MarkerSize),
                    Color = Color.White,
                    Alignment = MFDTheme.AC
                });

                // Wings counter-rotate by roll to stay horizon-aligned (like a real F-18 FPM)
                float rollRad = ToRad((float)roll);

                Vector2 leftWingOffset = new Vector2(-WingLength / 2 - WingOffsetX, 0f);
                Vector2 rightWingOffset = new Vector2(WingLength / 2 + WingOffsetX, 0f);

                Vector2 rotatedLeftWingOffset = SpriteHelpers.RotatePoint(leftWingOffset, Vector2.Zero, -rollRad);
                Vector2 rotatedRightWingOffset = SpriteHelpers.RotatePoint(rightWingOffset, Vector2.Zero, -rollRad);

                frame.Add(new MySprite
                {
                    Type = MFDTheme.TX,
                    Data = MFDTheme.SQ,
                    Position = markerPosition + rotatedLeftWingOffset,
                    Size = new Vector2(WingLength, WingThickness),
                    Color = Color.White,
                    Alignment = MFDTheme.AC,
                    RotationOrScale = -rollRad
                });

                frame.Add(new MySprite
                {
                    Type = MFDTheme.TX,
                    Data = MFDTheme.SQ,
                    Position = markerPosition + rotatedRightWingOffset,
                    Size = new Vector2(WingLength, WingThickness),
                    Color = Color.White,
                    Alignment = MFDTheme.AC,
                    RotationOrScale = -rollRad
                });
            }
        }
    }
}
