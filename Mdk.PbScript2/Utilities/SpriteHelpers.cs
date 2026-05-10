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
                // Square-ish outlines collapse to a single SquareHollow sprite (1 sprite instead of 4).
                // Stretched rects fall back to four 1-px filled rects so the border stays uniform —
                // SquareHollow's edge thickness scales with sprite dimensions and looks lopsided
                // when w/h diverges much.
                float aspect = width / Mx(height, 0.001f);
                if (aspect >= 0.7f && aspect <= 1.4f)
                {
                    SqT(TEXTURE_SQUARE_HOLLOW, x + width / 2f, y + height / 2f, width, height, color);
                    return;
                }
                Bx(frame, x + width / 2f, y, width, lineWidth, color);
                Bx(frame, x + width / 2f, y + height, width, lineWidth, color);
                Bx(frame, x, y + height / 2f, lineWidth, height, color);
                Bx(frame, x + width, y + height / 2f, lineWidth, height, color);
            }

            public static void DrawCircleOutline(MySpriteDrawFrame frame, Vector2 center, float radius, Color color, float thickness)
            {
                // Single CircleHollow sprite — was 24 line segments. The border thickness now
                // scales with sprite size rather than being fixed pixels, but for the few callers
                // (gun overlay, terrain insets) the visual is indistinguishable.
                float size = radius * 2f;
                SqT(TEXTURE_CIRCLE, center.X, center.Y, size, size, color);
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

        }
    }
}
