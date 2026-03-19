using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // ── NYINAH CORP MFD Theme palette ──
        static class MFDTheme
        {
            public static readonly Color BG            = new Color(5, 8, 5);
            public static readonly Color PANEL_BG      = new Color(8, 14, 8);
            public static readonly Color HEADER_BG     = new Color(10, 18, 10);
            public static readonly Color BORDER        = new Color(24, 40, 24);
            public static readonly Color BORDER_LIGHT  = new Color(20, 30, 20);
            public static readonly Color DIM_TEXT      = new Color(42, 74, 42);
            public static readonly Color DIM_TEXT_MID  = new Color(58, 90, 58);
            public static readonly Color MID_TEXT      = new Color(74, 122, 74);
            public static readonly Color NORMAL_TEXT   = new Color(90, 154, 90);
            public static readonly Color BRIGHT_TEXT   = new Color(144, 208, 144);
            public static readonly Color ACCENT        = new Color(64, 160, 64);
            public static readonly Color CORP_GOLD     = new Color(138, 122, 80);
            public static readonly Color GOLD_DIM      = new Color(55, 49, 32);
            public static readonly Color GOLD_LINE     = new Color(58, 53, 32);
            public static readonly Color SEL_FILL      = new Color(14, 28, 14);
            public static readonly Color SEL_BORDER    = new Color(26, 48, 26);
            public static readonly Color ROW_DIVIDER   = new Color(12, 20, 12);
            public static readonly Color CORNER        = new Color(42, 74, 42);
            public static readonly Color BC_BG         = new Color(8, 14, 8);
            public static readonly Color BC_BORDER     = new Color(16, 26, 16);
            public static readonly Color STATUS_RDY    = new Color(80, 160, 80);
            public static readonly Color BAR_TRACK     = new Color(6, 10, 6);
            public static readonly Color BAR_FILL      = new Color(48, 144, 48);
            public static readonly Color STATUS_VAL    = new Color(80, 144, 80);
            public static readonly Color WARN          = new Color(192, 160, 48);
            public static readonly string FONT          = "Monospace";
            public static readonly string FONT_W        = "White";
            public static readonly string SQ            = "SquareSimple";
            public static readonly string NC            = "NYINAH CORP";
            public const SpriteType TX = SpriteType.TEXTURE;
            public const SpriteType TT = SpriteType.TEXT;
            public const TextAlignment AC = TextAlignment.CENTER;
            public const TextAlignment AL = TextAlignment.LEFT;
            public const TextAlignment AR = TextAlignment.RIGHT;
        }

        class UIController
        {
            private IMyTextSurface mainScreen;
            private IMyTextSurface extraScreen;
            private RectangleF mainViewport;

            // Layout cache (recomputed if surface size changes)
            private float SW, SH;
            private float HEADER_H, ACCENT_H, BC_H, FOOTER_H;
            private float PAD_X, PAD_Y;
            private float CORNER_INSET, CORNER_LEN;
            private float ROW_H, ROW_H_COMPACT;
            private float SIDEBAR_W;
            private float TITLE_SCALE, TEXT_SCALE, TEXT_SCALE_COMPACT;
            private float SMALL_SCALE, TINY_SCALE;

            public IMyTextSurface MainScreen => mainScreen;
            public IMyTextSurface ExtraScreen => extraScreen;

            public UIController(IMyTextSurface mainScreen, IMyTextSurface extraScreen)
            {
                this.mainScreen = mainScreen;
                this.extraScreen = extraScreen;
                PrepareTextSurfaceForSprites(mainScreen);
                PrepareTextSurfaceForSprites(extraScreen);
                mainViewport = new RectangleF(Vector2.Zero, mainScreen.SurfaceSize);

                mainScreen.BackgroundColor = MFDTheme.BG;
                extraScreen.BackgroundColor = new Color(0, 0, 0);

                ComputeLayout();
            }

            private void ComputeLayout()
            {
                SW = mainViewport.Width;
                SH = mainViewport.Height;
                HEADER_H  = SH * 0.069f;
                ACCENT_H  = 1f;
                BC_H      = SH * 0.044f;
                FOOTER_H  = SH * 0.054f;
                PAD_X     = SW * 0.019f;
                PAD_Y     = SH * 0.020f;
                CORNER_INSET = 4f;
                CORNER_LEN   = Math.Min(SW, SH) * 0.03f;
                ROW_H         = SH * 0.079f;
                ROW_H_COMPACT = SH * 0.062f;
                SIDEBAR_W  = SW * 0.347f;

                // Font scales (SE Monospace: scale 1.0 ~ 28px tall)
                TITLE_SCALE        = SH * 0.00085f; // ~0.35 at 405
                TEXT_SCALE         = SH * 0.00104f;  // ~0.42 at 405
                TEXT_SCALE_COMPACT = SH * 0.00094f;  // ~0.38 at 405
                SMALL_SCALE        = SH * 0.00069f;  // ~0.28 at 405
                TINY_SCALE         = SH * 0.00055f;  // ~0.22 at 405
            }

            // ════════════════════════════════════════
            // MAIN SCREEN RENDER
            // ════════════════════════════════════════
            public void RenderMainScreen(
                string title,
                string[] options,
                int currentMenuIndex,
                string moduleName,
                Action<MySpriteDrawFrame, RectangleF> statusPanelRenderer = null)
            {
                var frame = mainScreen.DrawFrame();
                bool inModule = moduleName != null;
                bool hasSidebar = !inModule && statusPanelRenderer != null;
                bool compact = options.Length > 7;
                float rowH;
                float txtScale;
                if (inModule)
                {
                    // Tighter rows inside modules
                    rowH = compact ? ROW_H_COMPACT * 0.5f : ROW_H * 0.5f;
                    txtScale = compact ? TEXT_SCALE_COMPACT : TEXT_SCALE;
                }
                else
                {
                    rowH = compact ? ROW_H_COMPACT : ROW_H;
                    txtScale = compact ? TEXT_SCALE_COMPACT : TEXT_SCALE;
                }

                // 1. Background
                R(ref frame, SW / 2f, SH / 2f, SW, SH, MFDTheme.BG);

                // 2. Corner brackets
                DrawCornerBrackets(ref frame);

                // 3. Header (offset down so top border doesn't clip at viewing angles)
                float headerY = 15f;
                DrawHeader(ref frame, headerY);
                float curY = HEADER_H + ACCENT_H;

                // 4. Breadcrumb (module only)
                if (inModule)
                {
                    DrawBreadcrumb(ref frame, curY, moduleName);
                    curY += BC_H;
                }

                // 5. Content area
                float contentTop = curY + PAD_Y;
                float contentBot = SH - FOOTER_H;
                float menuLeft = PAD_X;
                float menuWidth = hasSidebar ? (SW - SIDEBAR_W - PAD_X * 3) : (SW - PAD_X * 2);

                // 6. Section title
                DrawSectionTitle(ref frame, contentTop, menuLeft, menuWidth, title);
                float menuTop = contentTop + SH * 0.045f;

                // 7. Menu rows
                float rowY = menuTop;
                for (int i = 0; i < options.Length; i++)
                {
                    bool selected = (i == currentMenuIndex);
                    float thisRowY = rowY;

                    if (selected)
                        DrawSelection(ref frame, menuLeft, thisRowY, menuWidth, rowH);

                    // Row text
                    Color txtColor = selected ? MFDTheme.BRIGHT_TEXT : MFDTheme.NORMAL_TEXT;
                    T(ref frame, options[i], menuLeft + 10f, thisRowY + rowH * 0.2f, txtScale, txtColor);

                    // Row divider
                    R(ref frame, menuLeft + menuWidth / 2f, thisRowY + rowH, menuWidth, 1f, MFDTheme.ROW_DIVIDER);

                    rowY += rowH;
                }

                // 8. Sidebar (main menu only)
                if (hasSidebar)
                {
                    float sideX = SW - SIDEBAR_W - PAD_X;
                    // Vertical divider
                    R(ref frame, sideX - 1f, contentTop, 1f, contentBot - contentTop - PAD_Y, MFDTheme.BORDER_LIGHT);

                    var sideArea = new RectangleF(
                        new Vector2(sideX + 4f, contentTop),
                        new Vector2(SIDEBAR_W - 4f, contentBot - contentTop - PAD_Y));
                    statusPanelRenderer(frame, sideArea);
                }

                // 9. Footer
                DrawFooter(ref frame, SH - FOOTER_H);

                // 10. Screen border (4 edges, on top of everything)
                DrawScreenBorder(ref frame);

                frame.Dispose();
            }

            // ── Header ──
            private void DrawHeader(ref MySpriteDrawFrame frame, float y)
            {
                R(ref frame, SW / 2f, y + HEADER_H / 2f, SW, HEADER_H, MFDTheme.HEADER_BG);
                // Bottom border
                R(ref frame, SW / 2f, y + HEADER_H, SW, 1f, MFDTheme.BORDER);
                // Gold accent
                R(ref frame, SW / 2f, y + HEADER_H + 0.5f, SW, ACCENT_H, MFDTheme.GOLD_LINE);

                // Brand: NYINAH CORP + TACTICAL SYSTEM
                T(ref frame, MFDTheme.NC, PAD_X, y + HEADER_H * 0.15f, TITLE_SCALE, MFDTheme.CORP_GOLD);
                float corpTextW = SW * 0.22f; // approx width of "NYINAH CORP" text
                T(ref frame, "TACTICAL SYSTEM " + Jet.IC + "/" + Jet.IA + "/" + Jet.IP, PAD_X + corpTextW, y + HEADER_H * 0.22f, SMALL_SCALE, MFDTheme.MID_TEXT);

                // Right: RDY . MFD-1
                T(ref frame, "RDY", SW - PAD_X - SW * 0.08f, y + HEADER_H * 0.2f, SMALL_SCALE, MFDTheme.STATUS_RDY);
                T(ref frame, "MFD-1", SW - PAD_X, y + HEADER_H * 0.2f, SMALL_SCALE, MFDTheme.DIM_TEXT_MID, MFDTheme.AR);
            }

            // ── Breadcrumb ──
            private void DrawBreadcrumb(ref MySpriteDrawFrame frame, float y, string moduleName)
            {
                R(ref frame, SW / 2f, y + BC_H / 2f, SW, BC_H, MFDTheme.BC_BG);
                R(ref frame, SW / 2f, y + BC_H, SW, 1f, MFDTheme.BC_BORDER);

                float tx = PAD_X;
                float ty = y + BC_H * 0.15f;
                float bcScale = TINY_SCALE * 1.1f;
                T(ref frame, "SYSTEM MENU", tx, ty, bcScale, MFDTheme.DIM_TEXT);
                tx += SW * 0.16f;
                T(ref frame, ">", tx, ty, bcScale, MFDTheme.BORDER);
                tx += SW * 0.02f;
                T(ref frame, moduleName.ToUpper(), tx, ty, bcScale, MFDTheme.NORMAL_TEXT);
            }

            // ── Section title with flanking lines ──
            private void DrawSectionTitle(ref MySpriteDrawFrame frame, float y, float left, float width, string text)
            {
                float lineY = y + SH * 0.012f;
                float textW = text.Length * SW * 0.012f; // rough text width estimate
                float centerX = left + width / 2f;
                float halfGap = textW / 2f + 8f;

                // Left line
                float leftLineW = centerX - halfGap - left;
                if (leftLineW > 2f)
                    R(ref frame, left + leftLineW / 2f, lineY, leftLineW, 1f, MFDTheme.BORDER);

                // Right line
                float rightStart = centerX + halfGap;
                float rightLineW = (left + width) - rightStart;
                if (rightLineW > 2f)
                    R(ref frame, rightStart + rightLineW / 2f, lineY, rightLineW, 1f, MFDTheme.BORDER);

                // Center text
                T(ref frame, text, centerX, y, SMALL_SCALE * 1.05f, MFDTheme.MID_TEXT, MFDTheme.AC);
            }

            // ── Selection indicator ──
            private void DrawSelection(ref MySpriteDrawFrame frame, float x, float y, float width, float rowH)
            {
                // Fill rectangle
                R(ref frame, x + width / 2f, y + rowH / 2f, width, rowH, MFDTheme.SEL_FILL);
                // Left accent bar
                R(ref frame, x + 1f, y + rowH / 2f, 2f, rowH, MFDTheme.ACCENT);
                // Top border line
                R(ref frame, x + width / 2f, y, width, 1f, MFDTheme.SEL_BORDER);
                // Bottom border line
                R(ref frame, x + width / 2f, y + rowH, width, 1f, MFDTheme.SEL_BORDER);
            }

            // ── Corner brackets ──
            private void DrawCornerBrackets(ref MySpriteDrawFrame frame)
            {
                float i = CORNER_INSET;
                float l = CORNER_LEN;
                // Top-left
                R(ref frame, i + l / 2f, i, l, 1f, MFDTheme.CORNER);
                R(ref frame, i, i + l / 2f, 1f, l, MFDTheme.CORNER);
                // Top-right
                R(ref frame, SW - i - l / 2f, i, l, 1f, MFDTheme.CORNER);
                R(ref frame, SW - i, i + l / 2f, 1f, l, MFDTheme.CORNER);
                // Bottom-left
                R(ref frame, i + l / 2f, SH - i, l, 1f, MFDTheme.CORNER);
                R(ref frame, i, SH - i - l / 2f, 1f, l, MFDTheme.CORNER);
                // Bottom-right
                R(ref frame, SW - i - l / 2f, SH - i, l, 1f, MFDTheme.CORNER);
                R(ref frame, SW - i, SH - i - l / 2f, 1f, l, MFDTheme.CORNER);
            }

            // ── Screen border ──
            private void DrawScreenBorder(ref MySpriteDrawFrame frame)
            {
                R(ref frame, SW / 2f, 1f, SW, 2f, MFDTheme.BORDER);     // top
                R(ref frame, SW / 2f, SH - 1f, SW, 2f, MFDTheme.BORDER); // bottom
                R(ref frame, 1f, SH / 2f, 2f, SH, MFDTheme.BORDER);     // left
                R(ref frame, SW - 1f, SH / 2f, 2f, SH, MFDTheme.BORDER); // right
            }

            // ── Footer ──
            private void DrawFooter(ref MySpriteDrawFrame frame, float y)
            {
                R(ref frame, SW / 2f, y + FOOTER_H / 2f, SW, FOOTER_H, MFDTheme.HEADER_BG);
                R(ref frame, SW / 2f, y, SW, 1f, MFDTheme.BORDER);

                // Nav keys
                string navStr = "1 UP  2 DN  3 SEL  4 BACK  5-8 FN  9 MENU";
                T(ref frame, navStr, PAD_X, y + FOOTER_H * 0.15f, TINY_SCALE, MFDTheme.DIM_TEXT);

                // Corp watermark
                T(ref frame, MFDTheme.NC, SW - PAD_X, y + FOOTER_H * 0.15f, TINY_SCALE, MFDTheme.GOLD_DIM, MFDTheme.AR);
            }

            // ════════════════════════════════════════
            // CUSTOM FRAME RENDERERS (for HUD module etc.)
            // ════════════════════════════════════════

            public void RenderCustomFrame(Action<MySpriteDrawFrame, RectangleF> customRender, RectangleF area)
            {
                var frame = mainScreen.DrawFrame();
                customRender?.Invoke(frame, area);
                frame.Dispose();
            }

            public void RenderCustomExtraFrame(Action<MySpriteDrawFrame, RectangleF> customRender, RectangleF area)
            {
                var frame = extraScreen.DrawFrame();
                customRender?.Invoke(frame, area);
                frame.Dispose();
            }

            // ════════════════════════════════════════
            // SPRITE HELPERS (inlined for performance)
            // ════════════════════════════════════════

            private void R(ref MySpriteDrawFrame f, float cx, float cy, float w, float h, Color c)
            {
                f.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ,
                    Position = new Vector2(cx, cy), Size = new Vector2(w, h),
                    Color = c, Alignment = MFDTheme.AC });
            }

            private void T(ref MySpriteDrawFrame f, string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AL)
            {
                f.Add(new MySprite { Type = MFDTheme.TT, Data = d,
                    Position = new Vector2(x, y), RotationOrScale = s,
                    Color = c, Alignment = a, FontId = MFDTheme.FONT });
            }

            private void PrepareTextSurfaceForSprites(IMyTextSurface textSurface)
            {
                textSurface.ContentType = ContentType.SCRIPT;
                textSurface.Script = "";
                textSurface.BackgroundColor = Color.Transparent;
                textSurface.FontColor = Color.Black;
                textSurface.FontSize = 0.1f;
                textSurface.TextPadding = 0f;
                textSurface.Alignment = MFDTheme.AC;
            }
        }
    }
}
