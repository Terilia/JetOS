using System;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class StatusPanelRenderer
        {
            // Animated state for the status bars — values lerp toward their targets each tick.
            static AnimatedValue _animFuel = new AnimatedValue();
            static AnimatedValue _animBattery = new AnimatedValue();

            // ════════════════════════════════════════
            // PUBLIC ENTRY POINT
            // ════════════════════════════════════════
            public static void Render(MySpriteDrawFrame frame, RectangleF area, Jet jet, HUDModule hud)
            {
                if (jet == null || jet._cockpit == null) return;
                float x = area.Position.X, y = area.Position.Y;
                float w = area.Width, areaH = area.Height;
                float gap = 6f, resH = 46f;

                double fuelPct, fuelSec;
                jet.GetFuelStatus(out fuelPct, out fuelSec);
                if (jet.tanks.Count > 0)
                {
                    _animFuel.SetTarget(fuelPct);
                    DrawResCard(frame, x, y, w, resH, "H2 FUEL", (float)_animFuel.Value, FmtTime(fuelSec), TEX_FUEL_TANK, 18f, 22f);
                    y += resH + gap;
                }

                float curMWh, maxMWh, netDrain;
                jet.GetBatteryStatus(out curMWh, out maxMWh, out netDrain);
                if (jet.batteries.Count > 0)
                {
                    float bp = maxMWh > 0 ? curMWh / maxMWh : 0f;
                    _animBattery.SetTarget(bp);
                    string bt = netDrain > 0.001f ? FmtTime(curMWh / netDrain * 3600) : netDrain < -0.001f ? "CHRG" : "---";
                    DrawResCard(frame, x, y, w, resH, "BATTERY", (float)_animBattery.Value, bt, TEX_BATTERY, 22f, 16f);
                    y += resH + gap;
                }

                float engH = 90f, remaining = area.Position.Y + areaH - y;
                if (remaining > engH + gap + 50f)
                { DrawEngCard(frame, x, y, w, engH, jet); y += engH + gap; }

                float mapH = area.Position.Y + areaH - y;
                if (mapH > 50f)
                    DrawTerrain(frame, x, y, w, mapH, jet);
            }

            // ════════════════════════════════════════
            // TERRAIN MINIMAP
            // ════════════════════════════════════════
            static void DrawTerrain(MySpriteDrawFrame frame, float x, float y, float w, float h, Jet jet)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, Cr(2, 3, 2));
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, Cr(14, 26, 16));
                var area = new RectangleF(V2(x + 2f, y + 2f), V2(w - 4f, h - 4f));
                if (TerrainData.Ready && jet.CachedGravity.LengthSquared() > 0.01
                    && !(SystemManager.currentModule is TerrainModule))
                    TerrainModule.RenderMinimap(frame, area, jet);
                else
                    Txt(frame, "NO TERRAIN", x + w / 2f, y + h / 2f - 6f, 0.35f, Cr(42, 74, 42));
            }

            // ════════════════════════════════════════
            // ENGINE + RESOURCE CARDS
            // ════════════════════════════════════════
            static void DrawEngCard(MySpriteDrawFrame frame, float x, float y, float w, float h, Jet jet)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, MFDTheme.BORDER_LIGHT);
                Txt(frame, "THRUST", x + w / 2f, y + 2f, 0.32f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                float midX = x + w / 2f, colW = (w - 16f) / 2f, top = y + 16f, colH = h - 20f;
                DrawEngCol(frame, x + 4f, top, colW, colH,
                    jet.leftEnginesAll, jet.leftABAll, jet.leftEngines, jet.leftAB, "ENG L");
                DrawEngCol(frame, midX + 4f, top, colW, colH,
                    jet.rightEnginesAll, jet.rightABAll, jet.rightEngines, jet.rightAB, "ENG R");
            }

            static void DrawEngCol(MySpriteDrawFrame frame, float x, float y, float w, float colH,
                System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyThrust> allEng,
                System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyThrust> allAb,
                System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyThrust> driveEng,
                System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyThrust> driveAb, string label)
            {
                int fn, tot, abFn, abTot;
                Jet.GetEngineHealth(allEng, out fn, out tot);
                Jet.GetEngineHealth(allAb, out abFn, out abTot);
                fn += abFn; tot += abTot;
                float curKN, maxKN; Jet.GetEngineThrust(driveEng, out curKN, out maxKN);
                float abCur, abMax; Jet.GetEngineThrust(driveAb, out abCur, out abMax);
                float tMax = maxKN + abMax, tCur = curKN + abCur;
                float pct = tMax > 0 ? tCur / tMax : 0f; bool dmg = fn < tot;
                Txt(frame, label, x, y, 0.32f, MFDTheme.DIM_TEXT_MID, MFDTheme.AL);
                Txt(frame, $"{fn}/{tot}", x + w, y, 0.3f, dmg ? MFDTheme.WARN : MFDTheme.ACCENT, MFDTheme.AR);
                float bx = x + 2f, bw = w - 4f, bt = y + 14f, bh = colH - 28f;
                if (bh < 6f) bh = 6f;
                Rect(frame, bx + bw / 2f, bt + bh / 2f, bw, bh, MFDTheme.BAR_TRACK);
                SpriteHelpers.DrawRectangleOutline(frame, bx, bt, bw, bh, 0.5f, MFDTheme.BORDER);
                if (dmg && fn > 0) {
                    float ch = bh * Cl((float)fn / tot, 0f, 1f);
                    Rect(frame, bx + bw / 2f, bt + bh - ch / 2f, bw, ch, Cr(12, 22, 12));
                    // Diagonal hatch over the DAMAGED portion (top of bar) to make the
                    // failure visually obvious — dim red overlay so it doesn't drown the bar.
                    float dmgH = bh - ch;
                    if (dmgH > 2f) SpriteHelpers.Sp(frame, TEX_HATCH, bx + bw / 2f, bt + dmgH / 2f, bw, dmgH, Cr(MFDTheme.WARN, 0.55f));
                    if (ch > 1f && ch < bh - 1f) Rect(frame, bx + bw / 2f, bt + bh - ch, bw + 2f, 1f, MFDTheme.WARN);
                }
                else if (!dmg) Rect(frame, bx + bw / 2f, bt + bh / 2f, bw, bh, Cr(12, 22, 12));
                float fh = bh * Cl(pct, 0f, 1f);
                if (fh > 0.5f) Rect(frame, bx + bw / 2f, bt + bh - fh / 2f, bw, fh, abCur > 0.1f ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
                Txt(frame, tMax > 0 ? $"{tCur,4:F0}/{tMax,4:F0}" : " ---/ ---", x + w / 2f, bt + bh + 1f, 0.28f, MFDTheme.STATUS_VAL, MFDTheme.AC);
                if (tMax > 0) Txt(frame, "kN", x + w / 2f, bt + bh + 11f, 0.24f, MFDTheme.DIM_TEXT, MFDTheme.AC);
            }

            static void DrawResCard(MySpriteDrawFrame frame, float x, float y, float w, float h, string title, float pct, string timeStr, string iconTex = null, float iconW = 9f, float iconH = 9f)
            {
                float safePct = Cl(pct, 0f, 1f);
                Rect(frame, x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, MFDTheme.BORDER);
                float titleX = x + 4f;
                if (iconTex != null)
                {
                    Color iconColor = safePct < 0.2f ? MFDTheme.WARN : safePct < 0.5f ? MFDTheme.STATUS_VAL : MFDTheme.DIM_TEXT_MID;
                    SpriteHelpers.Sp(frame, iconTex, x + 4f + iconW / 2f, y + 4f + iconH / 2f, iconW, iconH, iconColor);
                    titleX = x + 6f + iconW;
                    // Health dot — position to the right of percentage text.
                    Color dotColor = safePct < 0.2f ? MFDTheme.WARN : safePct < 0.5f ? MFDTheme.STATUS_VAL : MFDTheme.STATUS_RDY;
                    SpriteHelpers.Sp(frame, TEX_STATUS_DOT, x + w - 2f, y + 5f, 5f, 5f, dotColor);
                }
                Txt(frame, title, titleX, y + 2f, 0.32f, MFDTheme.DIM_TEXT_MID, MFDTheme.AL);
                int pctText = (int)(safePct * 100);
                Txt(frame, $"{pctText,3}%", x + w - 9f, y + 1f, 0.38f, MFDTheme.STATUS_VAL, MFDTheme.AR);
                // Bar sits BELOW the icon — icon now reaches ~y+26, so the bar starts at y+30.
                float by = y + 30f, bw = w - 8f, bh = 4f, bx = x + 4f;
                Rect(frame, bx + bw / 2f, by + bh / 2f, bw, bh, MFDTheme.BAR_TRACK);
                SpriteHelpers.DrawRectangleOutline(frame, bx, by, bw, bh, 0.5f, MFDTheme.BORDER);
                float fw = bw * safePct;
                Color barColor = safePct < 0.2f ? MFDTheme.WARN : safePct < 0.5f ? MFDTheme.STATUS_VAL : MFDTheme.ACCENT;
                if (fw > 0.5f) Rect(frame, bx + fw / 2f, by + bh / 2f, fw, bh, barColor);
                Txt(frame, "REMAIN", bx, by + bh + 2f, 0.28f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                Txt(frame, timeStr, bx + bw, by + bh + 2f, 0.28f, MFDTheme.STATUS_VAL, MFDTheme.AR);
            }

            static string FmtTime(double s) { return s <= 0 ? "---" : $"{(int)(s / 60):D2}:{(int)(s % 60):D2}"; }

            static void Rect(MySpriteDrawFrame f, float cx, float cy, float w, float h, Color c) => Sq(cx, cy, w, h, c);
            static void Txt(MySpriteDrawFrame f, string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AL) => Tx(d, x, y, s, c, a, null);
        }
    }
}
