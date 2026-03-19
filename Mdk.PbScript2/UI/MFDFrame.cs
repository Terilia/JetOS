using System;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        /// <summary>
        /// Shared NYINAH CORP MFD frame drawing — header, footer, corners, border.
        /// Used by all three MFD surfaces for consistent theming.
        /// </summary>
        static class MFDFrame
        {
            /// <summary>
            /// Draws the standard MFD chrome (background, border, corners, header, gold accent, footer).
            /// Returns the Y position where content should start drawing.
            /// </summary>
            public static float DrawChrome(MySpriteDrawFrame frame, float sw, float sh,
                string headerRight = null, bool drawFooterNav = true)
            {
                float padX = sw * 0.019f;
                float headerH = sh * 0.069f;
                float footerH = sh * 0.054f;
                float cornerLen = Math.Min(sw, sh) * 0.03f;
                float topOffset = 15f;
                float titleScale = sh * 0.00085f;
                float smallScale = sh * 0.00069f;
                float tinyScale = sh * 0.00055f;

                // Background
                Rect(frame, sw / 2f, sh / 2f, sw, sh, MFDTheme.BG);

                // Corner brackets
                float ci = 4f;
                Rect(frame, ci + cornerLen / 2f, ci, cornerLen, 1f, MFDTheme.CORNER);
                Rect(frame, ci, ci + cornerLen / 2f, 1f, cornerLen, MFDTheme.CORNER);
                Rect(frame, sw - ci - cornerLen / 2f, ci, cornerLen, 1f, MFDTheme.CORNER);
                Rect(frame, sw - ci, ci + cornerLen / 2f, 1f, cornerLen, MFDTheme.CORNER);
                Rect(frame, ci + cornerLen / 2f, sh - ci, cornerLen, 1f, MFDTheme.CORNER);
                Rect(frame, ci, sh - ci - cornerLen / 2f, 1f, cornerLen, MFDTheme.CORNER);
                Rect(frame, sw - ci - cornerLen / 2f, sh - ci, cornerLen, 1f, MFDTheme.CORNER);
                Rect(frame, sw - ci, sh - ci - cornerLen / 2f, 1f, cornerLen, MFDTheme.CORNER);

                // Header
                float hy = topOffset;
                Rect(frame, sw / 2f, hy + headerH / 2f, sw, headerH, MFDTheme.HEADER_BG);
                Rect(frame, sw / 2f, hy + headerH, sw, 1f, MFDTheme.BORDER);
                Rect(frame, sw / 2f, hy + headerH + 0.5f, sw, 1f, MFDTheme.GOLD_LINE);

                // Brand
                Txt(frame, MFDTheme.NC, padX, hy + headerH * 0.15f, titleScale, MFDTheme.CORP_GOLD);
                float corpW = sw * 0.22f;
                Txt(frame, "TACTICAL SYSTEM " + Jet.IC + "/" + Jet.IA + "/" + Jet.IP, padX + corpW, hy + headerH * 0.22f, smallScale, MFDTheme.MID_TEXT);

                // Header right text
                if (headerRight != null)
                {
                    Txt(frame, headerRight, sw - padX, hy + headerH * 0.2f, smallScale,
                        MFDTheme.DIM_TEXT_MID, MFDTheme.AR);
                }

                // Footer
                float fy = sh - footerH;
                Rect(frame, sw / 2f, fy + footerH / 2f, sw, footerH, MFDTheme.HEADER_BG);
                Rect(frame, sw / 2f, fy, sw, 1f, MFDTheme.BORDER);

                if (drawFooterNav)
                {
                    Txt(frame, "1 UP  2 DN  3 SEL  4 BACK  5-8 FN  9 MENU",
                        padX, fy + footerH * 0.15f, tinyScale, MFDTheme.DIM_TEXT);
                }

                Txt(frame, MFDTheme.NC, sw - padX, fy + footerH * 0.15f,
                    tinyScale, MFDTheme.GOLD_DIM, MFDTheme.AR);

                // Screen border
                Rect(frame, sw / 2f, 1f, sw, 2f, MFDTheme.BORDER);
                Rect(frame, sw / 2f, sh - 1f, sw, 2f, MFDTheme.BORDER);
                Rect(frame, 1f, sh / 2f, 2f, sh, MFDTheme.BORDER);
                Rect(frame, sw - 1f, sh / 2f, 2f, sh, MFDTheme.BORDER);

                // Return content start Y
                return hy + headerH + 2f;
            }

            /// <summary>
            /// Returns the Y position where content should stop (above the footer).
            /// </summary>
            public static float ContentBottom(float sh)
            {
                return sh - sh * 0.054f;
            }

            public static void Rect(MySpriteDrawFrame f, float cx, float cy, float w, float h, Color c)
            {
                f.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ,
                    Position = new Vector2(cx, cy), Size = new Vector2(w, h),
                    Color = c, Alignment = MFDTheme.AC });
            }

            public static void Txt(MySpriteDrawFrame f, string d, float x, float y, float s, Color c,
                TextAlignment a = MFDTheme.AL)
            {
                f.Add(new MySprite { Type = MFDTheme.TT, Data = d,
                    Position = new Vector2(x, y), RotationOrScale = s,
                    Color = c, Alignment = a, FontId = MFDTheme.FONT });
            }
        }
    }
}
