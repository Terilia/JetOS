using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // ── NYINAH CORP MFD Theme palette ── (ported verbatim from the jet)
        static class MFDTheme
        {
            public static readonly Color BG            = Cr(5, 8, 5);
            public static readonly Color PANEL_BG      = Cr(8, 14, 8);
            public static readonly Color HEADER_BG     = Cr(10, 18, 10);
            public static readonly Color BORDER        = Cr(24, 40, 24);
            public static readonly Color BORDER_LIGHT  = Cr(20, 30, 20);
            public static readonly Color DIM_TEXT      = Cr(42, 74, 42);
            public static readonly Color DIM_TEXT_MID  = Cr(58, 90, 58);
            public static readonly Color MID_TEXT      = Cr(74, 122, 74);
            public static readonly Color NORMAL_TEXT   = Cr(90, 154, 90);
            public static readonly Color BRIGHT_TEXT   = Cr(144, 208, 144);
            public static readonly Color ACCENT        = Cr(64, 160, 64);
            public static readonly Color CORP_GOLD     = Cr(138, 122, 80);
            public static readonly Color GOLD_DIM      = Cr(55, 49, 32);
            public static readonly Color GOLD_LINE     = Cr(58, 53, 32);
            public static readonly Color SEL_FILL      = Cr(14, 28, 14);
            public static readonly Color SEL_BORDER    = Cr(26, 48, 26);
            public static readonly Color ROW_DIVIDER   = Cr(12, 20, 12);
            public static readonly Color CORNER        = Cr(42, 74, 42);
            public static readonly Color BC_BG         = Cr(8, 14, 8);
            public static readonly Color BC_BORDER     = Cr(16, 26, 16);
            public static readonly Color STATUS_RDY    = Cr(80, 160, 80);
            public static readonly Color BAR_TRACK     = Cr(6, 10, 6);
            public static readonly Color BAR_FILL      = Cr(48, 144, 48);
            public static readonly Color STATUS_VAL    = Cr(80, 144, 80);
            public static readonly Color WARN          = Cr(192, 160, 48);
            public static readonly Color DANGER        = Cr(180, 50, 40);
            public static readonly string FONT          = "Monospace";
            public static readonly string FONT_W        = "White";
            public static readonly string SQ            = "SquareSimple";
            public static readonly string NC            = "NYINAH CORP";
            public const SpriteType TX = SpriteType.TEXTURE;
            public const SpriteType TT = SpriteType.TEXT;
            public const TextAlignment AC = TextAlignment.CENTER;
            public const TextAlignment AL = TextAlignment.LEFT;
            public const TextAlignment AR = TextAlignment.RIGHT;

            // TACSIT level → palette: RED danger, AMBER warn, GREEN accent.
            public static Color TacsitColor(string t) => t == "RED" ? DANGER : t == "AMBER" ? WARN : ACCENT;
        }

        class UIController
        {
            internal const double PAGE_FADE_DURATION = 0.300;
            private const double SELECTION_TWEEN_DURATION = 0.080;

            private int _selPrevIndex = -1;
            private float _selAnimFromY;
            private double _selAnimStart = -1;

            // Single render entry point. Chrome → breadcrumb → title → (menu | content) → sidebar → footer.
            public void Render(MfdPage page, IMyTextSurface surface, int selectedIndex = 0,
                double transitionStart = -1, List<MySprite> captureInto = null, List<MySprite> prevFrame = null)
            {
                if (page == null || surface == null) return;
                var frame = surface.DrawFrame();
                SpriteBus.Begin(frame, captureInto);
                try
                {
                    float sw = SX(surface);
                    float sh = SY(surface);

                    float chromeTop = MFDFrame.DrawChrome(
                        sw, sh,
                        headerRight: page.HeaderRight,
                        drawFooterNav: page.ShowFooterNav,
                        footerRight: page.FooterRight,
                        compact: page.CompactChrome);
                    float contentBot = MFDFrame.ContentBottom(sh);

                    float padX = sw * 0.019f;
                    float menuLeft = padX;
                    float sidebarW = sw * 0.347f;
                    float sideX = sw - sidebarW - padX;
                    float menuWidth = page.HasSidebar ? (sw - sidebarW - padX * 3) : (sw - padX * 2);
                    float bcWidth = page.HasSidebar ? sideX : sw;

                    float menuTop = chromeTop;
                    if (page.ShowBreadcrumb)
                        menuTop = DrawBreadcrumb(sh, chromeTop, page.BreadcrumbPath, bcWidth);

                    float titleScale = sh * 0.00069f * 1.05f;
                    float bodyTop = menuTop;
                    if (!SE(page.Title))
                    {
                        bodyTop += sh * 0.020f;
                        DrawSectionTitle(sw, sh, bodyTop, menuLeft, menuWidth, page.Title, titleScale);
                        bodyTop += sh * 0.045f;
                    }

                    var contentArea = new RectangleF(menuLeft, bodyTop, menuWidth, contentBot - bodyTop);

                    if (page.HasMenu && page.MenuItems != null)
                    {
                        bool inTransition = transitionStart >= 0
                            && (SystemManager.ElapsedSeconds - transitionStart) < PAGE_FADE_DURATION;
                        DrawMenuList(sh, page.CompactRows, page.MenuItems, selectedIndex,
                            menuLeft, bodyTop, menuWidth, contentBot, inTransition);
                        page.RenderMenuSupplement(new RectangleF(menuLeft, bodyTop, menuWidth, contentBot - bodyTop),
                            SS(surface), selectedIndex);
                    }
                    else
                        page.RenderContent(contentArea, SS(surface));

                    if (page.HasSidebar)
                    {
                        Rect(sideX - 1f, chromeTop, 1f, contentBot - chromeTop - sh * 0.020f, MFDTheme.BORDER_LIGHT);
                        var sideArea = new RectangleF(
                            V2(sideX + 4f, chromeTop),
                            V2(sidebarW - 4f, contentBot - chromeTop - sh * 0.020f));
                        page.RenderSidebar(sideArea);
                    }

                    MFDFrame.DrawScreenBorder(sw, sh);

                    if (prevFrame != null && transitionStart >= 0)
                    {
                        double elapsed = SystemManager.ElapsedSeconds - transitionStart;
                        if (elapsed < PAGE_FADE_DURATION * 0.85)
                        {
                            SpriteBus.End();
                            ReplayWithTransform(prevFrame, sw, sh, elapsed / PAGE_FADE_DURATION);
                            SpriteBus.Begin(frame, captureInto);
                        }
                    }

                    // Global mouse cursor lives on top of every MFD page (left half of the canvas).
                    MouseCursor.DrawLeft(sw, sh);
                }
                finally { SpriteBus.End(); frame.Dispose(); }
            }

            // Re-emits cached sprites with shader-like per-sprite math: radial drift, desaturate, fade.
            private static void ReplayWithTransform(List<MySprite> prev, float sw, float sh, double progress)
            {
                float ep = (float)Anim.EaseOut(progress);
                float alpha = 1f - ep;
                float disp = ep * 0.35f;
                float desatT = ep * 0.7f;
                Vector2 center = V2(sw / 2f, sh / 2f);

                for (int i = 0; i < prev.Count; i++)
                {
                    var s = prev[i];
                    if (!s.Position.HasValue || !s.Color.HasValue) { SpriteBus.AddRaw(s); continue; }
                    Vector2 pos = s.Position.Value;
                    Vector2 dir = pos - center;
                    s.Position = pos + dir * disp;

                    Color c = s.Color.Value;
                    float gray = (c.R + c.G + c.B) / (3f * 255f);
                    Color grayC = new Color(gray, gray, gray);
                    Color mixed = Anim.LerpColor(c, grayC, desatT);
                    s.Color = Anim.WithAlpha(mixed, mixed.A / 255f * alpha);
                    SpriteBus.AddRaw(s);
                }
            }

            private static float DrawBreadcrumb(float sh, float y, string path, float bcWidth)
            {
                float bcH = sh * 0.044f;
                Rect(bcWidth / 2f, y + bcH / 2f, bcWidth, bcH, MFDTheme.BC_BG);
                Rect(bcWidth / 2f, y + bcH, bcWidth, 1f, MFDTheme.BC_BORDER);

                float padX = bcWidth * 0.019f;
                float scale = sh * 0.00055f * 1.1f;
                float ty = y + bcH * 0.15f;
                Txt("HQ", padX, ty, scale, MFDTheme.DIM_TEXT);
                Txt(">", padX + bcWidth * 0.20f, ty, scale, MFDTheme.BORDER);
                Txt((path ?? "").ToUpper(), padX + bcWidth * 0.23f, ty, scale, MFDTheme.NORMAL_TEXT);

                return y + bcH + 2f;
            }

            private static void DrawSectionTitle(float sw, float sh,
                float y, float left, float width, string text, float scale)
            {
                float lineY = y + sh * 0.012f;
                float textW = text.Length * sw * 0.012f;
                float centerX = left + width / 2f;
                float halfGap = textW / 2f + 8f;

                float leftLineW = centerX - halfGap - left;
                if (leftLineW > 2f)
                    Rect(left + leftLineW / 2f, lineY, leftLineW, 1f, MFDTheme.BORDER);

                float rightStart = centerX + halfGap;
                float rightLineW = (left + width) - rightStart;
                if (rightLineW > 2f)
                    Rect(rightStart + rightLineW / 2f, lineY, rightLineW, 1f, MFDTheme.BORDER);

                Txt(text, centerX, y, scale, MFDTheme.MID_TEXT, MFDTheme.AC);
            }

            private void DrawMenuList(float sh, bool compactRows,
                string[] items, int selectedIndex, float left, float top, float width, float contentBot,
                bool snapSelection)
            {
                bool tightItems = items.Length > 7;
                float rowH = tightItems ? sh * 0.062f : sh * 0.079f;
                if (compactRows) rowH *= 0.5f;
                float txtScale = tightItems ? sh * 0.00094f : sh * 0.00104f;

                float targetY = top + selectedIndex * rowH;
                double now = SystemManager.ElapsedSeconds;

                if (snapSelection || _selPrevIndex < 0)
                {
                    _selPrevIndex = selectedIndex;
                    _selAnimStart = -1;
                }
                else if (selectedIndex != _selPrevIndex)
                {
                    _selAnimFromY = CurrentSelectionY(top, rowH, now);
                    _selPrevIndex = selectedIndex;
                    _selAnimStart = now;
                }

                float animY;
                if (_selAnimStart >= 0)
                {
                    double t = (now - _selAnimStart) / SELECTION_TWEEN_DURATION;
                    if (t >= 1) { _selAnimStart = -1; animY = targetY; }
                    else animY = (float)Anim.Lerp(_selAnimFromY, targetY, Anim.EaseOut(t));
                }
                else animY = targetY;

                Rect(left + width / 2f, animY + rowH / 2f, width, rowH, MFDTheme.SEL_FILL);
                Rect(left + 1f, animY + rowH / 2f, 2f, rowH, MFDTheme.ACCENT);
                Rect(left + width / 2f, animY, width, 1f, MFDTheme.SEL_BORDER);
                Rect(left + width / 2f, animY + rowH, width, 1f, MFDTheme.SEL_BORDER);

                // Register clickable row regions for the global mouse cursor + highlight on hover.
                UiHit.Clear();
                bool cOnMfd = MouseCursor.Visible && !Canvas.OnRight(MouseCursor.X);
                float cy = Cl(MouseCursor.Y, 0f, Canvas.LH);

                float rowY = top;
                for (int i = 0; i < items.Length; i++)
                {
                    bool hover = cOnMfd && MouseCursor.X >= left && MouseCursor.X <= left + width
                                 && cy >= rowY && cy <= rowY + rowH;
                    if (hover && i != selectedIndex)
                    {
                        Rect(left + width / 2f, rowY + rowH / 2f, width, rowH, MFDTheme.ROW_DIVIDER);
                        Rect(left + 1f, rowY + rowH / 2f, 2f, rowH, MFDTheme.MID_TEXT);
                    }
                    Color tc = (i == selectedIndex || hover) ? MFDTheme.BRIGHT_TEXT : MFDTheme.NORMAL_TEXT;
                    Txt(items[i], left + 10f, rowY + rowH * 0.2f, txtScale, tc);
                    Rect(left + width / 2f, rowY + rowH, width, 1f, MFDTheme.ROW_DIVIDER);
                    UiHit.AddRow(left, rowY, width, rowH, i);
                    rowY += rowH;
                }
            }

            private float CurrentSelectionY(float top, float rowH, double now)
            {
                float prevTarget = top + _selPrevIndex * rowH;
                if (_selAnimStart < 0) return prevTarget;
                double t = (now - _selAnimStart) / SELECTION_TWEEN_DURATION;
                if (t >= 1) return prevTarget;
                return (float)Anim.Lerp(_selAnimFromY, prevTarget, Anim.EaseOut(t));
            }

            private static void Rect(float cx, float cy, float w, float h, Color c) => Sq(cx, cy, w, h, c);
            private static void Txt(string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AL) => Tx(d, x, y, s, c, a, null);
        }
    }
}
