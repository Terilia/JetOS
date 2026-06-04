using System;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // MFD drawing helpers. Ported from the jet's Utilities/SpriteHelpers.cs, minus the
        // jet-only ProjectToScreen (HUD FOV projection) — the HQ map does its own top-down
        // projection.
        static class SpriteHelpers
        {
            public static void Bx(float x, float y, float w, float h, Color c) => Sq(x, y, w, h, c);
            public static void Bx(float x, float y, float w, float h, Color c, float r) => Sq(x, y, w, h, c, r);
            public static void Sp(string d, float x, float y, float w, float h, Color c, float r = 0f) => SqT(d, x, y, w, h, c, r);
            public static void Tt(string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AC, string fn = null) => Tx(d, x, y, s, c, a, fn);

            public static MySprite FBx(float x, float y, float w, float h, Color c)
            {
                return new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ, Position = V2(x, y), Size = V2(w, h), Color = c, Alignment = MFDTheme.AC };
            }

            public static MySprite FTt(string d, float x, float y, float s, Color c, TextAlignment a, string fn)
            {
                return new MySprite { Type = MFDTheme.TT, Data = d, Position = V2(x, y), RotationOrScale = s, Color = c, Alignment = a, FontId = fn };
            }

            public static void AddLineSprite(Vector2 start, Vector2 end, float thickness, Color color)
            {
                Vector2 delta = end - start;
                float length = delta.Length();
                if (length < 0.1f) return;
                Vector2 position = start + delta / 2f;
                float rotation = (float)At2(delta.Y, delta.X) - (float)PI / 2f;
                Bx(position.X, position.Y, thickness, length, color, rotation);
            }

            public static void DrawRectangleOutline(float x, float y, float width, float height, float lineWidth, Color color)
            {
                // Square-ish outlines collapse to a single SquareHollow sprite (1 sprite, not 4).
                float aspect = width / Mx(height, 0.001f);
                if (aspect >= 0.7f && aspect <= 1.4f)
                {
                    SqT(TEXTURE_SQUARE_HOLLOW, x + width / 2f, y + height / 2f, width, height, color);
                    return;
                }
                Bx(x + width / 2f, y, width, lineWidth, color);
                Bx(x + width / 2f, y + height, width, lineWidth, color);
                Bx(x, y + height / 2f, lineWidth, height, color);
                Bx(x + width, y + height / 2f, lineWidth, height, color);
            }

            public static void DrawCircleOutline(Vector2 center, float radius, Color color, float thickness)
            {
                float size = radius * 2f;
                SqT(TEXTURE_CIRCLE, center.X, center.Y, size, size, color);
            }

            public static string FormatRange(double meters)
            {
                return meters >= 1000 ? $"{meters / 1000:F1}km" : $"{meters:F0}m";
            }
        }
    }
}
