using System;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Shared NYINAH CORP MFD frame — header, footer, corners, border. Ported from the
        // jet's UI/MFDFrame.cs; the only change is the instruction-count readout reads HQ's
        // SystemManager counters instead of Jet.IC/IA/IP.
        static class MFDFrame
        {
            // Draws the standard MFD chrome. Returns the Y where content should start.
            public static float DrawChrome(float sw, float sh,
                string headerRight = null, bool drawFooterNav = true, string footerRight = null,
                bool compact = false)
            {
                float padX = sw * 0.019f;
                float headerH = sh * (compact ? 0.058f : 0.069f);
                float footerH = sh * 0.054f;
                float cornerLen = Mn(sw, sh) * 0.03f;
                float topOffset = compact ? 10f : 15f;
                float titleScale = sh * 0.00085f;
                float smallScale = sh * 0.00069f;
                float tinyScale = sh * 0.00055f;

                Rect(sw / 2f, sh / 2f, sw, sh, MFDTheme.BG);

                float ci = 4f;
                float spriteSize = cornerLen * 256f / 96f;
                float pivot = ci + cornerLen;
                SpriteHelpers.Sp(TEX_MFD_CORNER, pivot,      pivot,      spriteSize, spriteSize, MFDTheme.CORNER, 0f);
                SpriteHelpers.Sp(TEX_MFD_CORNER, sw - pivot, pivot,      spriteSize, spriteSize, MFDTheme.CORNER, (float)(PI * 0.5));
                SpriteHelpers.Sp(TEX_MFD_CORNER, sw - pivot, sh - pivot, spriteSize, spriteSize, MFDTheme.CORNER, (float)PI);
                SpriteHelpers.Sp(TEX_MFD_CORNER, pivot,      sh - pivot, spriteSize, spriteSize, MFDTheme.CORNER, (float)(PI * 1.5));

                float hy = topOffset;
                Rect(sw / 2f, hy + headerH / 2f, sw, headerH, MFDTheme.HEADER_BG);
                Rect(sw / 2f, hy + headerH, sw, 1f, MFDTheme.BORDER);
                Rect(sw / 2f, hy + headerH + 0.5f, sw, 1f, MFDTheme.GOLD_LINE);

                Txt(MFDTheme.NC, padX, hy + headerH * 0.15f, titleScale, MFDTheme.CORP_GOLD);
                float corpW = sw * 0.22f;
                Txt("HQ " + SystemManager.IC + "/" + SystemManager.IA + "/" + SystemManager.IP,
                    padX + corpW, hy + headerH * 0.22f, smallScale, MFDTheme.MID_TEXT);

                if (headerRight != null)
                    Txt(headerRight, sw - padX, hy + headerH * 0.2f, smallScale, MFDTheme.DIM_TEXT_MID, MFDTheme.AR);

                float fy = sh - footerH;
                Rect(sw / 2f, fy + footerH / 2f, sw, footerH, MFDTheme.HEADER_BG);
                Rect(sw / 2f, fy, sw, 1f, MFDTheme.BORDER);

                if (drawFooterNav)
                    Txt("1/2 NAV 3 SEL 4 BACK 5-8 FN 9 MENU",
                        padX, fy + footerH * 0.15f, tinyScale, MFDTheme.DIM_TEXT);

                if (!SE(footerRight))
                    Txt(footerRight, sw - padX, fy + footerH * 0.15f, tinyScale, MFDTheme.DIM_TEXT_MID, MFDTheme.AR);
                else
                    Txt(MFDTheme.NC, sw - padX, fy + footerH * 0.15f, tinyScale, MFDTheme.GOLD_DIM, MFDTheme.AR);

                DrawScreenBorder(sw, sh);
                return hy + headerH + 2f;
            }

            public static float ContentBottom(float sh) => sh - sh * 0.054f;

            public static void DrawScreenBorder(float sw, float sh)
            {
                Rect(sw / 2f, 1f, sw, 2f, MFDTheme.BORDER);
                Rect(sw / 2f, sh - 1f, sw, 2f, MFDTheme.BORDER);
                Rect(1f, sh / 2f, 2f, sh, MFDTheme.BORDER);
                Rect(sw - 1f, sh / 2f, 2f, sh, MFDTheme.BORDER);
            }

            public static void Rect(float cx, float cy, float w, float h, Color c) => Sq(cx, cy, w, h, c);
            public static void Txt(string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AL) => Tx(d, x, y, s, c, a, null);
        }
    }
}
