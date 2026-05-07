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
                string headerRight = null, bool drawFooterNav = true, string footerRight = null)
            {
                float padX = sw * 0.019f;
                float headerH = sh * 0.069f;
                float footerH = sh * 0.054f;
                float cornerLen = Mn(sw, sh) * 0.03f;
                float topOffset = 15f;
                float titleScale = sh * 0.00085f;
                float smallScale = sh * 0.00069f;
                float tinyScale = sh * 0.00055f;

                // Background
                Rect(frame, sw / 2f, sh / 2f, sw, sh, MFDTheme.BG);

                // Corner brackets — one sprite per corner, rotated. Source canvas places the
                // L-bracket in the top-left quadrant with arms ending at (128/256) of canvas; sized
                // so visible arms are cornerLen long and the bracket point sits at (ci, ci).
                float ci = 4f;
                float spriteSize = cornerLen * 256f / 96f;   // 96 = arm-length in 256 source canvas
                float pivot = ci + cornerLen;                // sprite center offset from edge
                SpriteHelpers.Sp(frame, TEX_MFD_CORNER, pivot,        pivot,        spriteSize, spriteSize, MFDTheme.CORNER, 0f);
                SpriteHelpers.Sp(frame, TEX_MFD_CORNER, sw - pivot,   pivot,        spriteSize, spriteSize, MFDTheme.CORNER, (float)(PI * 0.5));
                SpriteHelpers.Sp(frame, TEX_MFD_CORNER, sw - pivot,   sh - pivot,   spriteSize, spriteSize, MFDTheme.CORNER, (float)PI);
                SpriteHelpers.Sp(frame, TEX_MFD_CORNER, pivot,        sh - pivot,   spriteSize, spriteSize, MFDTheme.CORNER, (float)(PI * 1.5));

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

                if (!string.IsNullOrEmpty(footerRight))
                    Txt(frame, footerRight, sw - padX, fy + footerH * 0.15f,
                        tinyScale, MFDTheme.DIM_TEXT_MID, MFDTheme.AR);
                else
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

            public static void Rect(MySpriteDrawFrame f, float cx, float cy, float w, float h, Color c) => Sq(cx, cy, w, h, c);
            public static void Txt(MySpriteDrawFrame f, string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AL) => Tx(d, x, y, s, c, a, null);
        }
    }
}
