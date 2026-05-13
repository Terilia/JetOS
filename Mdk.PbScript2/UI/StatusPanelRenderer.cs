using SpaceEngineers.Game.ModAPI.Ingame;
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
            public static void Render(RectangleF area, Jet jet, HUDModule hud)
            {
                if (jet == null || jet._cockpit == null) return;
                float x = area.Position.X, y = area.Position.Y;
                float w = area.Width, areaH = area.Height;
                float gap = 6f, resH = 46f;

                if (jet.tanks.Count > 0)
                {
                    _animFuel.SetTarget(jet.FuelPct);
                    DrawResCard(x, y, w, resH, "H2", (float)_animFuel.Value, FmtTime(jet.FuelSec), TEX_FUEL_TANK, 18f, 22f);
                    y += resH + gap;
                }

                if (jet.batteries.Count > 0)
                {
                    _animBattery.SetTarget(jet.BatteryPct);
                    string bt = jet.BatteryNetDrainMW > 0.001f ? FmtTime(jet.BatteryCurMWh / jet.BatteryNetDrainMW * 3600) : jet.BatteryNetDrainMW < -0.001f ? "CHRG" : "---";
                    DrawResCard(x, y, w, resH, "BATT", (float)_animBattery.Value, bt, TEX_BATTERY, 22f, 16f);
                    y += resH + gap;
                }

                float mslH = 90f, remaining = area.Position.Y + areaH - y;
                if (remaining > mslH + gap + 50f)
                { DrawMissileCard(x, y, w, mslH, jet); y += mslH + gap; }

                float mapH = area.Position.Y + areaH - y;
                if (mapH > 50f)
                    DrawTerrain(x, y, w, mapH, jet);
            }

            // ════════════════════════════════════════
            // TERRAIN MINIMAP
            // ════════════════════════════════════════
            static void DrawTerrain(float x, float y, float w, float h, Jet jet)
            {
                Rect(x + w / 2f, y + h / 2f, w, h, Cr(2, 3, 2));
                SpriteHelpers.DrawRectangleOutline(x, y, w, h, 1f, Cr(14, 26, 16));
                var area = new RectangleF(V2(x + 2f, y + 2f), V2(w - 4f, h - 4f));
                if (TerrainData.Ready && jet.CachedGravity.LengthSquared() > 0.01
                    && !(SystemManager.currentModule is TerrainModule))
                    TerrainModule.RenderMinimap(area, jet);
                else
                    Txt("NO TER", x + w / 2f, y + h / 2f - 6f, 0.35f, Cr(42, 74, 42));
            }

            // ════════════════════════════════════════
            // MISSILE + RESOURCE CARDS
            // ════════════════════════════════════════
            static void DrawMissileCard(float x, float y, float w, float h, Jet jet)
            {
                Rect(x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(x, y, w, h, 1f, MFDTheme.BORDER_LIGHT);
                Txt("MSL", x + w / 2f, y + 2f, 0.32f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                var bays = jet._bays;
                int n = bays != null ? Mn(bays.Count, 12) : 0, rdy = 0, air = 0, active = 0;
                for (int i = 0; i < n; i++)
                {
                    int bayNum = MissileBayHelper.GetBayNumber(bays[i], i + 1);
                    MissileBayHelper.MissileStatus ms;
                    if (MissileBayHelper.TryGetMissileStatus(bayNum, out ms))
                    {
                        air++;
                        if (ms.ActiveTrackingUnlocked) active++;
                    }
                    if (MissileBayHelper.IsBayReady(bays[i])) rdy++;
                }
                Txt($"RDY {rdy}/{n}", x + 4f, y + 15f, 0.30f, MFDTheme.STATUS_VAL);
                Txt($"AIR {air}", x + w - 4f, y + 15f, 0.30f, air > 0 ? MFDTheme.WARN : MFDTheme.DIM_TEXT_MID, MFDTheme.AR);
                if (active > 0)
                    Txt("AI UNLOCKED", x + w / 2f, y + 15f, 0.24f, MFDTheme.ACCENT, MFDTheme.AC);
                float bx = x + 4f, by = y + 30f, bw = (w - 14f) / 4f, bh = (h - 40f) / 3f;
                for (int i = 0; i < 12; i++)
                {
                    float cx = bx + (i % 4) * (bw + 2f), cy = by + (i / 4) * (bh + 2f);
                    if (i < n) DrawBayCell(cx + 1f, cy + 1f, bw - 2f, bh - 2f, bays[i], i + 1);
                    else DrawEmptyBayCell(cx + 1f, cy + 1f, bw - 2f, bh - 2f);
                }
            }

            static void DrawBayCell(float x, float y, float w, float h, IMyShipMergeBlock bay, int fallback)
            {
                int bayNum = MissileBayHelper.GetBayNumber(bay, fallback);
                MissileBayHelper.MissileStatus ms;
                bool live = MissileBayHelper.TryGetMissileStatus(bayNum, out ms);
                bool ready = MissileBayHelper.IsBayReady(bay);
                Color c = live ? (ms.Acquired ? MFDTheme.ACCENT : MFDTheme.WARN) : ready ? MFDTheme.STATUS_RDY : MFDTheme.DIM_TEXT_MID;
                Rect(x + w / 2f, y + h / 2f, w, h, live ? Cr(c, 0.16f) : Cr(4, 8, 4, 180));
                SpriteHelpers.DrawRectangleOutline(x, y, w, h, 0.5f, c);
                Txt(bayNum.ToString(), x + 2f, y, 0.24f, MFDTheme.DIM_TEXT_MID);
                if (live)
                {
                    Sq(x + w - 7f, y + 8f, 7f, 2f, c, 1.5708f);
                    Txt(MissileBayHelper.FormatEta(ms.Eta), x + w / 2f, y + h / 2f - 5f, 0.46f, c, MFDTheme.AC);
                    if (ms.ActiveTrackingUnlocked)
                        Txt("AI", x + w / 2f, y + h - 11f, 0.22f, MFDTheme.ACCENT, MFDTheme.AC);
                }
                else Txt(ready ? "RDY" : "---", x + w / 2f, y + h / 2f - 4f, 0.27f, c, MFDTheme.AC);
            }

            static void DrawEmptyBayCell(float x, float y, float w, float h)
            {
                Rect(x + w / 2f, y + h / 2f, w, h, Cr(2, 4, 2, 120));
                SpriteHelpers.DrawRectangleOutline(x, y, w, h, 0.5f, MFDTheme.BORDER);
            }

            static void DrawResCard(float x, float y, float w, float h, string title, float pct, string timeStr, string iconTex = null, float iconW = 9f, float iconH = 9f)
            {
                float safePct = Cl(pct, 0f, 1f);
                Rect(x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(x, y, w, h, 1f, MFDTheme.BORDER);
                float titleX = x + 4f;
                if (iconTex != null)
                {
                    Color iconColor = safePct < 0.2f ? MFDTheme.WARN : safePct < 0.5f ? MFDTheme.STATUS_VAL : MFDTheme.DIM_TEXT_MID;
                    SpriteHelpers.Sp(iconTex, x + 4f + iconW / 2f, y + 4f + iconH / 2f, iconW, iconH, iconColor);
                    titleX = x + 6f + iconW;
                    // Health dot — position to the right of percentage text.
                    Color dotColor = safePct < 0.2f ? MFDTheme.WARN : safePct < 0.5f ? MFDTheme.STATUS_VAL : MFDTheme.STATUS_RDY;
                    SpriteHelpers.Sp(TEX_STATUS_DOT, x + w - 2f, y + 5f, 5f, 5f, dotColor);
                }
                Txt(title, titleX, y + 2f, 0.32f, MFDTheme.DIM_TEXT_MID, MFDTheme.AL);
                int pctText = (int)(safePct * 100);
                Txt($"{pctText,3}%", x + w - 9f, y + 1f, 0.38f, MFDTheme.STATUS_VAL, MFDTheme.AR);
                // Bar sits BELOW the icon — icon now reaches ~y+26, so the bar starts at y+30.
                float by = y + 30f, bw = w - 8f, bh = 4f, bx = x + 4f;
                Rect(bx + bw / 2f, by + bh / 2f, bw, bh, MFDTheme.BAR_TRACK);
                SpriteHelpers.DrawRectangleOutline(bx, by, bw, bh, 0.5f, MFDTheme.BORDER);
                float fw = bw * safePct;
                Color barColor = safePct < 0.2f ? MFDTheme.WARN : safePct < 0.5f ? MFDTheme.STATUS_VAL : MFDTheme.ACCENT;
                if (fw > 0.5f) Rect(bx + fw / 2f, by + bh / 2f, fw, bh, barColor);
                Txt("REM", bx, by + bh + 2f, 0.28f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                Txt(timeStr, bx + bw, by + bh + 2f, 0.28f, MFDTheme.STATUS_VAL, MFDTheme.AR);
            }

            static string FmtTime(double s) { return s <= 0 ? "---" : $"{(int)(s / 60):D2}:{(int)(s % 60):D2}"; }

            static void Rect(float cx, float cy, float w, float h, Color c) => Sq(cx, cy, w, h, c);
            static void Txt(string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AL) => Tx(d, x, y, s, c, a, null);
        }
    }
}
