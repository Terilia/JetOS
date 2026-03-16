using System;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class SpriteHelpers
        {
            public static void AddLineSprite(MySpriteDrawFrame frame, Vector2 start, Vector2 end, float thickness, Color color)
            {
                Vector2 delta = end - start;
                float length = delta.Length();
                if (length < 0.1f) return;

                Vector2 position = start + delta / 2f;
                float rotation = (float)Math.Atan2(delta.Y, delta.X) - (float)Math.PI / 2f;

                var line = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = MFDTheme.SQ,
                    Position = position,
                    Size = new Vector2(thickness, length),
                    Color = color,
                    RotationOrScale = rotation,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(line);
            }

            public static void DrawRectangleOutline(MySpriteDrawFrame frame, float x, float y, float width, float height, float lineWidth, Color color)
            {
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = MFDTheme.SQ, Position = new Vector2(x + width / 2f, y), Size = new Vector2(width, lineWidth), Color = color, Alignment = TextAlignment.CENTER });
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = MFDTheme.SQ, Position = new Vector2(x + width / 2f, y + height), Size = new Vector2(width, lineWidth), Color = color, Alignment = TextAlignment.CENTER });
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = MFDTheme.SQ, Position = new Vector2(x, y + height / 2f), Size = new Vector2(lineWidth, height), Color = color, Alignment = TextAlignment.CENTER });
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = MFDTheme.SQ, Position = new Vector2(x + width, y + height / 2f), Size = new Vector2(lineWidth, height), Color = color, Alignment = TextAlignment.CENTER });
            }

            public static void DrawCircleOutline(MySpriteDrawFrame frame, Vector2 center, float radius, Color color, float thickness)
            {
                int segments = 24;
                float angleStep = (float)(2 * Math.PI / segments);

                for (int i = 0; i < segments; i++)
                {
                    float angle1 = i * angleStep;
                    float angle2 = (i + 1) * angleStep;

                    Vector2 p1 = center + new Vector2((float)Math.Cos(angle1) * radius, (float)Math.Sin(angle1) * radius);
                    Vector2 p2 = center + new Vector2((float)Math.Cos(angle2) * radius, (float)Math.Sin(angle2) * radius);

                    Vector2 direction = p2 - p1;
                    float length = direction.Length();
                    if (length > 0)
                    {
                        direction /= length;
                        float rotation = (float)Math.Atan2(direction.Y, direction.X);

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = MFDTheme.SQ,
                            Position = (p1 + p2) / 2f,
                            Size = new Vector2(length + thickness, thickness),
                            RotationOrScale = rotation,
                            Color = color,
                            Alignment = TextAlignment.CENTER
                        });
                    }
                }
            }

            public static string FormatRange(double meters)
            {
                return meters >= 1000 ? $"{meters / 1000:F1}km" : $"{meters:F0}m";
            }

            /// <summary>
            /// Projects a local-space direction (relative to cockpit) onto 2D screen coordinates
            /// using FOV perspective projection. The localDirection must already be transformed
            /// into cockpit-local space (via worldToCockpitMatrix).
            /// </summary>
            internal static Vector2 ProjectToScreen(Vector3D localDirection, Vector2 center, Vector2 surfaceSize)
            {
                float scale = surfaceSize.Y / HUDModule.COCKPIT_FOV_SCALE_Y;
                float screenX = center.X + (float)(localDirection.X / -localDirection.Z) * scale;
                float screenY = center.Y + (float)(-localDirection.Y / -localDirection.Z) * scale;
                return new Vector2(screenX, screenY);
            }

            /// <summary>
            /// Transforms a world-space direction into cockpit-local space and projects to screen.
            /// Convenience overload that combines TransformNormal + ProjectToScreen.
            /// </summary>
            internal static Vector2 WorldToScreen(Vector3D worldDirection, MatrixD worldToCockpitMatrix, Vector2 center, Vector2 surfaceSize)
            {
                Vector3D localDirection = Vector3D.TransformNormal(worldDirection, worldToCockpitMatrix);
                return ProjectToScreen(localDirection, center, surfaceSize);
            }

            public static Vector2 RotatePoint(Vector2 point, Vector2 pivot, float angle)
            {
                float cosTheta = (float)Math.Cos(angle);
                float sinTheta = (float)Math.Sin(angle);
                Vector2 translatedPoint = point - pivot;
                Vector2 rotatedPoint = new Vector2(
                    translatedPoint.X * cosTheta - translatedPoint.Y * sinTheta,
                    translatedPoint.X * sinTheta + translatedPoint.Y * cosTheta
                );
                return rotatedPoint + pivot;
            }
        }
    }
}
