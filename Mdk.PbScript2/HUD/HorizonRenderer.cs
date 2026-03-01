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
            private void DrawArtificialHorizon(
                MySpriteDrawFrame frame,
                float pitch,
                float roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                List<MySprite> sprites = new List<MySprite>();

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
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX * 0.75f, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = TextAlignment.CENTER
                        }
                    );
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX * 1.25f, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = TextAlignment.CENTER
                        }
                    );

                    float tipLength = 12f;
                    float tipAngle = MathHelper.ToRadians(isPositive ? 45f : -45f);

                    string label = Math.Abs(i).ToString();
                    float labelOffsetX = halfWidth + tipLength + 10f;

                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            Position = new Vector2(centerX - labelOffsetX, markerY + 10f),
                            RotationOrScale = 0.8f,
                            Color = lineColor,
                            Alignment = TextAlignment.RIGHT,
                            FontId = "White"
                        }
                    );
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            Position = new Vector2(centerX + labelOffsetX, markerY + 10f),
                            RotationOrScale = 0.8f,
                            Color = lineColor,
                            Alignment = TextAlignment.LEFT,
                            FontId = "White"
                        }
                    );
                }

                float horizonY = centerY + pitch * pixelsPerDegree;
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX * 1.25f, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = HUD_HORIZON,
                        Alignment = TextAlignment.CENTER
                    }
                );
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX * 0.75f, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = HUD_HORIZON,
                        Alignment = TextAlignment.CENTER
                    }
                );
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "-^-",
                        Position = new Vector2(centerX, centerY - 10),
                        RotationOrScale = 0.8f,
                        Color = HUD_EMPHASIS,
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    }
                );

                float rollRad = MathHelper.ToRadians(-roll);
                float cosRoll = (float)Math.Cos(rollRad);
                float sinRoll = (float)Math.Sin(rollRad);

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

                    if (sprite.Type == SpriteType.TEXTURE)
                    {
                        float existing = sprite.RotationOrScale;
                        sprite.RotationOrScale = existing + rollRad;
                    }

                    sprites[s] = sprite;

                    frame.Add(sprite);
                }
            }

            private void DrawBankAngleMarkers(MySpriteDrawFrame frame, float centerX, float centerY, float roll, float pixelsPerDegree)
            {
                int[] bankAngles = new int[] { 15, 30, 45, 60, -15, -30, -45, -60 };
                float horizonRadius = pixelsPerDegree * 20f;

                float rollRad = MathHelper.ToRadians(-roll);
                float cosRoll = (float)Math.Cos(rollRad);
                float sinRoll = (float)Math.Sin(rollRad);

                foreach (int angle in bankAngles)
                {
                    float angleRad = MathHelper.ToRadians(angle);
                    Vector2 tickPos = new Vector2((float)Math.Sin(angleRad) * horizonRadius, -(float)Math.Cos(angleRad) * horizonRadius);

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
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = finalPos,
                        Size = new Vector2(2f, tickLength),
                        Color = tickColor,
                        Alignment = TextAlignment.CENTER,
                        RotationOrScale = angleRad + rollRad
                    });
                }
            }

            private void DrawFlightPathMarker(
                MySpriteDrawFrame frame,
                Vector3D currentVelocity,
                MatrixD worldMatrix,
                double roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                const double DegToRad = Math.PI / 180.0;
                const float MarkerSize = 20f;
                const float WingLength = 15f;
                const float WingThickness = 2f;
                const float WingOffsetX = 10f;

                Vector3D velocityDirection = Vector3D.Normalize(currentVelocity);

                Vector3D localVelocity = Vector3D.TransformNormal(
                    velocityDirection,
                    MatrixD.Transpose(worldMatrix)
                );

                double velocityYaw = Math.Atan2(localVelocity.X, -localVelocity.Z) * 180.0 / Math.PI;
                double velocityPitch = Math.Atan2(localVelocity.Y, -localVelocity.Z) * 180.0 / Math.PI;

                float rollRad = (float)(roll * DegToRad);

                Vector2 markerOffset = new Vector2(
                    (float)(-velocityYaw * pixelsPerDegree),
                    (float)(velocityPitch * pixelsPerDegree)
                );

                Vector2 rotatedOffset = SpriteHelpers.RotatePoint(markerOffset, Vector2.Zero, -rollRad);
                Vector2 markerPosition = new Vector2(centerX, centerY) + rotatedOffset;

                var marker = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = markerPosition,
                    Size = new Vector2(MarkerSize, MarkerSize),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(marker);

                Vector2 leftWingOffset = new Vector2(-WingLength / 2 - WingOffsetX, 0f);
                Vector2 rightWingOffset = new Vector2(WingLength / 2 + WingOffsetX, 0f);

                Vector2 rotatedLeftWingOffset = SpriteHelpers.RotatePoint(leftWingOffset, Vector2.Zero, -rollRad);
                Vector2 rotatedRightWingOffset = SpriteHelpers.RotatePoint(rightWingOffset, Vector2.Zero, -rollRad);

                var leftWing = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = markerPosition + rotatedLeftWingOffset,
                    Size = new Vector2(WingLength, WingThickness),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = -rollRad
                };
                frame.Add(leftWing);

                var rightWing = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = markerPosition + rotatedRightWingOffset,
                    Size = new Vector2(WingLength, WingThickness),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = -rollRad
                };
                frame.Add(rightWing);
            }
        }
    }
}
