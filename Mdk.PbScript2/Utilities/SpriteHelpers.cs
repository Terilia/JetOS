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
            // Precomputed sin/cos for 24-segment circles — eliminates 48 trig calls per circle per frame
            internal const int CIRC_SEGS = 24;
            internal static readonly float[] CSin = new float[CIRC_SEGS + 1];
            internal static readonly float[] CCos = new float[CIRC_SEGS + 1];

            static SpriteHelpers()
            {
                for (int i = 0; i <= CIRC_SEGS; i++)
                {
                    double a = i * 2.0 * PI / CIRC_SEGS;
                    CSin[i] = (float)Sn(a);
                    CCos[i] = (float)Cs(a);
                }
            }

            public static void Bx(MySpriteDrawFrame f, float x, float y, float w, float h, Color c)
            {
                Sq(x, y, w, h, c);
            }

            public static void Bx(MySpriteDrawFrame f, float x, float y, float w, float h, Color c, float r)
            {
                Sq(x, y, w, h, c, r);
            }

            public static void Sp(MySpriteDrawFrame f, string d, float x, float y, float w, float h, Color c, float r = 0f)
            {
                SqT(d, x, y, w, h, c, r);
            }

            public static void Tt(MySpriteDrawFrame f, string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AC, string fn = null)
            {
                Tx(d, x, y, s, c, a, fn);
            }

            public static MySprite FBx(float x, float y, float w, float h, Color c)
            {
                return new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ, Position = V2(x, y), Size = V2(w, h), Color = c, Alignment = MFDTheme.AC };
            }

            public static MySprite FTt(string d, float x, float y, float s, Color c, TextAlignment a, string fn)
            {
                return new MySprite { Type = MFDTheme.TT, Data = d, Position = V2(x, y), RotationOrScale = s, Color = c, Alignment = a, FontId = fn };
            }

            public static void AddLineSprite(MySpriteDrawFrame frame, Vector2 start, Vector2 end, float thickness, Color color)
            {
                Vector2 delta = end - start;
                float length = delta.Length();
                if (length < 0.1f) return;
                Vector2 position = start + delta / 2f;
                float rotation = (float)At2(delta.Y, delta.X) - (float)PI / 2f;
                Bx(frame, position.X, position.Y, thickness, length, color, rotation);
            }

            public static void DrawRectangleOutline(MySpriteDrawFrame frame, float x, float y, float width, float height, float lineWidth, Color color)
            {
                Bx(frame, x + width / 2f, y, width, lineWidth, color);
                Bx(frame, x + width / 2f, y + height, width, lineWidth, color);
                Bx(frame, x, y + height / 2f, lineWidth, height, color);
                Bx(frame, x + width, y + height / 2f, lineWidth, height, color);
            }

            public static void DrawCircleOutline(MySpriteDrawFrame frame, Vector2 center, float radius, Color color, float thickness)
            {
                // Line-segment circle using precomputed trig — same visual quality, zero runtime sin/cos
                for (int i = 0; i < CIRC_SEGS; i++)
                {
                    Vector2 p1 = center + V2(CCos[i] * radius, CSin[i] * radius);
                    Vector2 p2 = center + V2(CCos[i + 1] * radius, CSin[i + 1] * radius);
                    Vector2 delta = p2 - p1;
                    float length = delta.Length();
                    if (length > 0)
                    {
                        Vector2 mid = (p1 + p2) * 0.5f;
                        float rotation = (float)At2(delta.Y, delta.X);
                        Bx(frame, mid.X, mid.Y, length + thickness, thickness, color, rotation);
                    }
                }
            }

            public static string FormatRange(double meters)
            {
                return meters >= 1000 ? $"{meters / 1000:F1}km" : $"{meters:F0}m";
            }

            internal static Vector2 ProjectToScreen(Vector3D localDirection, Vector2 center, Vector2 surfaceSize)
            {
                float scale = surfaceSize.Y / HUDModule.COCKPIT_FOV_SCALE_Y;
                float screenX = center.X + (float)(localDirection.X / -localDirection.Z) * scale;
                float screenY = center.Y + (float)(-localDirection.Y / -localDirection.Z) * scale;
                return V2(screenX, screenY);
            }

            public static Vector2 RotatePoint(Vector2 point, Vector2 pivot, float angle)
            {
                float cosTheta = (float)Cs(angle);
                float sinTheta = (float)Sn(angle);
                Vector2 translatedPoint = point - pivot;
                Vector2 rotatedPoint = V2(
                    translatedPoint.X * cosTheta - translatedPoint.Y * sinTheta,
                    translatedPoint.X * sinTheta + translatedPoint.Y * cosTheta
                );
                return rotatedPoint + pivot;
            }
        }
    }
}
