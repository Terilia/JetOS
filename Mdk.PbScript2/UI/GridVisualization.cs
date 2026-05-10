using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class GridVisualization
        {
            static int originalBlockCount;
            static List<IMyTerminalBlock> gridBlocks = new List<IMyTerminalBlock>();
            static List<MySprite> cachedSprites = new List<MySprite>();

            // Staggered rebuild: spreads work across 3 ticks
            // Phase 0: idle (counting down refreshTick)
            // Phase 1: GetBlocksOfType — collect blocks
            // Phase 2: Build occupancy/integrity arrays
            // Phase 3: Generate sprite cache
            static int refreshTick = 0;
            static int rebuildPhase = 0;
            static int lastBlockCount = 0;
            static int damageCheckCounter = 0;
            const int REFRESH_INTERVAL = 60;     // check every 60 ticks
            const int DAMAGE_REFRESH = 300;      // full damage rebuild every 300 ticks

            // Intermediate data between phases
            static int gridMinX, gridMaxX, gridMinZ, gridMaxZ;
            static int gridW, gridH;
            static bool[,] gridOcc;
            static float[,] gridIntegrity;
            static bool[,] gridFunctional;
            static float cachedContentY, cachedContentBot;
            static RectangleF cachedArea;

            static double BINGO_FUEL => SystemManager.GetConfigValue("bingo_fuel");
            static double LOW_FUEL => SystemManager.GetConfigValue("low_fuel");

            // Animated bar values — keep the visuals smooth even when underlying values jitter.
            static AnimatedValue _animFuelBar = new AnimatedValue();
            static AnimatedValue _animBattery = new AnimatedValue();
            static AnimatedValue _animGForce = new AnimatedValue();
            static AnimatedValue _animSpeed = new AnimatedValue(0.20);
            static AnimatedValue _animAltitude = new AnimatedValue(0.20);
            static AnimatedValue _animAoA = new AnimatedValue(0.20);
            static AnimatedValue _animMach = new AnimatedValue(0.20);
            static AnimatedValue _animThrottle = new AnimatedValue(0.18);

            public static void Render(MySpriteDrawFrame frame, Vector2 surfaceSize, RectangleF contentArea,
                Program program, Jet jet, HUDModule hud = null)
            {
                // surfaceSize covers the full text surface (used for centering / right-edge anchoring).
                // contentArea is the post-chrome inner rect (top..bot), supplied by the host MfdPage.
                float sw = surfaceSize.X;
                float sh = surfaceSize.Y;
                float contentY = contentArea.Position.Y;
                float contentBot = contentArea.Position.Y + contentArea.Height;
                // 'area' kept as a local for the existing rebuild-phase cache below.
                var area = contentArea;
                float innerLeft = contentArea.Position.X + 2f;
                float innerRight = contentArea.Position.X + contentArea.Width - 2f;
                float innerW = innerRight - innerLeft;
                float synH = Mn(164f, Mx(154f, contentArea.Height * 0.38f));
                float synTop = contentBot - synH - 2f;
                float airTop = contentY + 1f;
                float airBot = synTop - 4f;
                if (airBot < airTop + 140f)
                {
                    synH = Mn(152f, Mx(140f, contentArea.Height * 0.35f));
                    synTop = contentBot - synH - 2f;
                    airBot = synTop - 3f;
                }
                float gridTop = airTop + 3f;
                float gridBot = airBot - 36f;
                if (gridBot < gridTop + 40f) gridBot = airBot - 16f;

                // Staggered rebuild state machine
                if (rebuildPhase > 0)
                {
                    RunRebuildPhase(program, area, gridTop, gridBot);
                }
                else
                {
                    refreshTick--;
                    damageCheckCounter--;
                    if (refreshTick <= 0 || gridBlocks.Count == 0)
                    {
                        // Start phase 1 on next call
                        rebuildPhase = 1;
                        cachedArea = area;
                        cachedContentY = gridTop;
                        cachedContentBot = gridBot;
                        refreshTick = REFRESH_INTERVAL;
                    }
                }

                DrawAirframeBand(frame, innerLeft, airTop, innerW, airBot - airTop);

                // Always draw cached sprites
                for (int i = 0; i < cachedSprites.Count; i++)
                    frame.Add(cachedSprites[i]);

                DrawAirframeSummary(frame, innerLeft, airBot - 34f, innerW, jet);
                DrawStatusSynoptic(frame, innerLeft, synTop, innerW, contentBot - synTop - 2f, jet);
            }

            static void RunRebuildPhase(Program program, RectangleF area, float contentY, float contentBot)
            {
                switch (rebuildPhase)
                {
                    case 1: // Phase 1: Collect blocks
                        gridBlocks.Clear();
                        program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(gridBlocks);
                        if (originalBlockCount == 0) originalBlockCount = gridBlocks.Count;

                        // Only proceed to phase 2 if count changed or damage check due
                        bool needsRebuild = gridBlocks.Count != lastBlockCount
                            || cachedSprites.Count == 0
                            || damageCheckCounter <= 0;
                        lastBlockCount = gridBlocks.Count;

                        if (needsRebuild)
                        {
                            rebuildPhase = 2;
                            damageCheckCounter = DAMAGE_REFRESH;
                        }
                        else
                        {
                            rebuildPhase = 0; // Skip rebuild, cache is still valid
                        }
                        break;

                    case 2: // Phase 2: Build occupancy + integrity arrays
                        if (gridBlocks.Count == 0) { rebuildPhase = 0; break; }

                        gridMinX = int.MaxValue; gridMaxX = int.MinValue;
                        gridMinZ = int.MaxValue; gridMaxZ = int.MinValue;
                        for (int i = 0; i < gridBlocks.Count; i++)
                        {
                            var p = gridBlocks[i].Position;
                            if (p.X < gridMinX) gridMinX = p.X;
                            if (p.X > gridMaxX) gridMaxX = p.X;
                            if (p.Z < gridMinZ) gridMinZ = p.Z;
                            if (p.Z > gridMaxZ) gridMaxZ = p.Z;
                        }

                        gridW = gridMaxX - gridMinX + 1;
                        gridH = gridMaxZ - gridMinZ + 1;
                        gridOcc = new bool[gridW, gridH];
                        gridIntegrity = new float[gridW, gridH];
                        gridFunctional = new bool[gridW, gridH];

                        for (int x = 0; x < gridW; x++)
                            for (int z = 0; z < gridH; z++)
                            {
                                gridIntegrity[x, z] = 1f;
                                gridFunctional[x, z] = true;
                            }

                        for (int i = 0; i < gridBlocks.Count; i++)
                        {
                            var b = gridBlocks[i];
                            int x = b.Position.X - gridMinX, z = b.Position.Z - gridMinZ;
                            if (x < 0 || x >= gridW || z < 0 || z >= gridH) continue;
                            gridOcc[x, z] = true;
                            var slim = b.CubeGrid.GetCubeBlock(b.Position);
                            if (slim != null)
                            {
                                float mi = slim.MaxIntegrity;
                                float r = mi > 0 ? (mi - slim.CurrentDamage) / mi : 0f;
                                if (r < gridIntegrity[x, z]) gridIntegrity[x, z] = r;
                            }
                            if (!b.IsFunctional) gridFunctional[x, z] = false;
                        }

                        rebuildPhase = 3;
                        break;

                    case 3: // Phase 3: Generate sprites
                        if (gridOcc == null || gridW <= 0 || gridH <= 0) { rebuildPhase = 0; break; }
                        cachedSprites.Clear();

                        float gL = cachedArea.Position.X + cachedArea.Width * 0.12f;
                        float gR = cachedArea.Position.X + cachedArea.Width * 0.88f;
                        float gT = cachedContentY;
                        float gB = cachedContentBot;
                        float cs = Mn((gR - gL) / gridW, (gB - gT) / gridH);
                        cs = Mn(cs, 16f);
                        Vector2 center = V2((gL + gR) / 2f, (gT + gB) / 2f);
                        Vector2 topLeft = center - V2(gridW * cs, gridH * cs) / 2f;

                        for (int x = 0; x < gridW; x++)
                        {
                            for (int z = 0; z < gridH; z++)
                            {
                                if (!gridOcc[x, z]) continue;
                                bool outline = false;
                                if (x == 0 || !gridOcc[x - 1, z]) outline = true;
                                else if (x == gridW - 1 || !gridOcc[x + 1, z]) outline = true;
                                else if (z == 0 || !gridOcc[x, z - 1]) outline = true;
                                else if (z == gridH - 1 || !gridOcc[x, z + 1]) outline = true;
                                if (!outline) continue;

                                Color c;
                                if (!gridFunctional[x, z]) c = Cr(120, 20, 20);
                                else if (gridIntegrity[x, z] < 0.30f) c = MFDTheme.DANGER;
                                else if (gridIntegrity[x, z] < 0.80f) c = MFDTheme.WARN;
                                else c = MFDTheme.ACCENT;

                                // Flip X so left side of ship shows on left side of screen
                                // (SE grid X+ is leftward from cockpit perspective)
                                Vector2 dp = topLeft + V2((gridW - 1 - x) * cs + cs / 2f, (gridH - 1 - z) * cs + cs / 2f);
                                cachedSprites.Add(SpriteHelpers.FBx(dp.X, dp.Y, cs * 5f, cs * 2f, c));
                            }
                        }

                        // Free intermediate arrays
                        gridOcc = null;
                        gridIntegrity = null;
                        gridFunctional = null;
                        rebuildPhase = 0;
                        break;

                    default:
                        rebuildPhase = 0;
                        break;
                }
            }

            static void DrawAirframeBand(MySpriteDrawFrame f, float x, float y, float w, float h)
            {
                SpriteHelpers.Bx(f, x + w / 2f, y + h / 2f, w, h, Cr(4, 7, 4));
                SpriteHelpers.Bx(f, x + w / 2f, y + h - 22f, w, 1f, MFDTheme.BORDER);

                float gx = x + w / 2f;
                float gt = y + 4f;
                float gb = y + h - 25f;
                float gh = gb - gt;
                if (gh > 20f)
                {
                    SpriteHelpers.Bx(f, gx, gt + gh / 2f, 1f, gh, Cr(MFDTheme.GOLD_LINE, 0.35f));
                    SpriteHelpers.Bx(f, x + w / 2f, gt + gh * 0.50f, w - 16f, 1f, Cr(MFDTheme.GOLD_LINE, 0.22f));
                }
            }

            static void DrawAirframeSummary(MySpriteDrawFrame f, float x, float y, float w, Jet jet)
            {
                int cur = gridBlocks.Count;
                int orig = originalBlockCount > 0 ? originalBlockCount : cur;
                double air = orig > 0 ? Cl((double)cur / orig, 0.0, 1.0) : 1.0;
                Color airC = cur >= orig ? MFDTheme.BRIGHT_TEXT : cur > orig * 0.7 ? MFDTheme.WARN : MFDTheme.DANGER;

                int useFn, useTot, allFn, allTot;
                EngineHealth2(jet.leftEngines, jet.leftAB, out useFn, out useTot);
                int rFn, rTot;
                EngineHealth2(jet.rightEngines, jet.rightAB, out rFn, out rTot);
                useFn += rFn; useTot += rTot;

                EngineHealth2(jet.leftEnginesAll, jet.leftABAll, out allFn, out allTot);
                EngineHealth2(jet.rightEnginesAll, jet.rightABAll, out rFn, out rTot);
                allFn += rFn; allTot += rTot;

                float col = w / 4f;
                DrawSummaryCell(f, x, y, col, "BLOCKS", $"{cur}/{orig}", airC);
                DrawSummaryCell(f, x + col, y, col, "AIRFRAME", $"{air * 100,3:F0}%", airC);
                DrawSummaryCell(f, x + col * 2f, y, col, "THR USE", $"{useFn}/{useTot}", useFn < useTot ? MFDTheme.WARN : MFDTheme.BRIGHT_TEXT);
                DrawSummaryCell(f, x + col * 3f, y, col, "THR ALL", $"{allFn}/{allTot}", allFn < allTot ? MFDTheme.WARN : MFDTheme.BRIGHT_TEXT);
            }

            static void DrawSummaryCell(MySpriteDrawFrame f, float x, float y, float w, string label, string value, Color valueColor)
            {
                SpriteHelpers.Tt(f, label, x + 1f, y, 0.48f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(f, value, x + 1f, y + 16f, 0.70f, valueColor, MFDTheme.AL);
            }

            static void DrawStatusSynoptic(MySpriteDrawFrame f, float x, float y, float w, float h, Jet jet)
            {
                if (h < 114f) return;
                SpriteHelpers.Bx(f, x + w / 2f, y, w, 1f, MFDTheme.GOLD_LINE);
                float gap = 4f;
                float colW = (w - gap * 2f) / 3f;
                DrawPropulsionSection(f, x, y + 4f, colW, h - 4f, jet);
                SpriteHelpers.Bx(f, x + colW + gap / 2f, y + h / 2f + 3f, 1f, h - 8f, MFDTheme.BORDER);
                DrawFuelSection(f, x + colW + gap, y + 4f, colW, h - 4f, jet);
                SpriteHelpers.Bx(f, x + colW * 2f + gap * 1.5f, y + h / 2f + 3f, 1f, h - 8f, MFDTheme.BORDER);
                DrawPowerSection(f, x + colW * 2f + gap * 2f, y + 4f, colW, h - 4f, jet);
            }

            static void DrawPropulsionSection(MySpriteDrawFrame f, float x, float y, float w, float h, Jet jet)
            {
                DrawSysHeader(f, x, y, w, "PROP", "USE");
                float rowY = y + 31f;
                DrawEngineRail(f, x, rowY, w, "L", jet.leftEnginesAll, jet.leftABAll, jet.leftEngines, jet.leftAB);
                DrawEngineRail(f, x, rowY + 36f, w, "R", jet.rightEnginesAll, jet.rightABAll, jet.rightEngines, jet.rightAB);

                int lFn, lTot, rFn, rTot;
                EngineHealth2(jet.leftABAll, null, out lFn, out lTot);
                EngineHealth2(jet.rightABAll, null, out rFn, out rTot);
                float lCur, lMax, rCur, rMax, tmpCur, tmpMax;
                Jet.GetEngineThrust(jet.leftEngines, out lCur, out lMax);
                Jet.GetEngineThrust(jet.leftAB, out tmpCur, out tmpMax);
                lCur += tmpCur; lMax += tmpMax;
                Jet.GetEngineThrust(jet.rightEngines, out rCur, out rMax);
                Jet.GetEngineThrust(jet.rightAB, out tmpCur, out tmpMax);
                rCur += tmpCur; rMax += tmpMax;
                float diff = lCur - rCur;

                DrawTinyLabelValue(f, x, y + h - 52f, w, "AB", $"{lFn + rFn}/{lTot + rTot}", lFn + rFn < lTot + rTot ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
                DrawTinyLabelValue(f, x, y + h - 25f, w, "BAL", SignedKn(diff), Ab(diff) > 30f ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
            }

            static void DrawEngineRail(MySpriteDrawFrame f, float x, float y, float w, string side,
                List<IMyThrust> allEng, List<IMyThrust> allAb, List<IMyThrust> driveEng, List<IMyThrust> driveAb)
            {
                int fn, tot;
                EngineHealth2(allEng, allAb, out fn, out tot);
                float cur, max, abCur, abMax;
                Jet.GetEngineThrust(driveEng, out cur, out max);
                Jet.GetEngineThrust(driveAb, out abCur, out abMax);
                cur += abCur; max += abMax;
                float pct = max > 0 ? Cl(cur / max, 0f, 1f) : 0f;
                Color c = fn < tot ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
                SpriteHelpers.Tt(f, side, x, y - 6f, 0.72f, MFDTheme.CORP_GOLD, MFDTheme.AL);
                DrawHBar(f, x + 24f, y + 9f, w - 88f, 10f, pct, c);
                SpriteHelpers.Tt(f, $"{fn}/{tot}", x + w, y - 5f, 0.62f, c, MFDTheme.AR);
            }

            static void DrawFuelSection(MySpriteDrawFrame f, float x, float y, float w, float h, Jet jet)
            {
                DrawSysHeader(f, x, y, w, "FUEL", "H2");
                double fuelPct, fuelSec;
                jet.GetFuelStatus(out fuelPct, out fuelSec);
                _animFuelBar.SetTarget(fuelPct);
                float pct = (float)Cl(_animFuelBar.Value, 0, 1);
                Color c = pct < BINGO_FUEL ? MFDTheme.DANGER : pct < LOW_FUEL ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
                SpriteHelpers.Tt(f, "RES", x, y + 32f, 0.56f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(f, $"{pct * 100,3:F0}%", x + w, y + 26f, 0.82f, c, MFDTheme.AR);
                DrawHBar(f, x, y + 66f, w, 10f, pct, c);
                DrawBarTick(f, x, y + 60f, w, 22f, (float)BINGO_FUEL, MFDTheme.CORP_GOLD);
                DrawBarTick(f, x, y + 60f, w, 22f, (float)LOW_FUEL, MFDTheme.CORP_GOLD);
                DrawTinyLabelValue(f, x, y + h - 52f, w, "TIME", FmtTime(fuelSec), c);
                DrawTinyLabelValue(f, x, y + h - 25f, w, "LIM", $"{BINGO_FUEL * 100,2:F0}/{LOW_FUEL * 100,2:F0}%", MFDTheme.STATUS_VAL);
            }

            static void DrawPowerSection(MySpriteDrawFrame f, float x, float y, float w, float h, Jet jet)
            {
                DrawSysHeader(f, x, y, w, "POWER", "BUS");
                float curMWh, maxMWh, netDrain;
                jet.GetBatteryStatus(out curMWh, out maxMWh, out netDrain);
                float bp = maxMWh > 0 ? Cl(curMWh / maxMWh, 0f, 1f) : 0f;
                _animBattery.SetTarget(bp);
                float pct = (float)_animBattery.Value;
                Color c = pct < 0.20f ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
                SpriteHelpers.Tt(f, "BAT", x, y + 32f, 0.56f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(f, $"{pct * 100,3:F0}%", x + w, y + 26f, 0.82f, c, MFDTheme.AR);
                DrawHBar(f, x, y + 66f, w, 10f, pct, c);
                string state = netDrain > 0.001f ? "DISCH" : netDrain < -0.001f ? "CHRG" : "IDLE";
                DrawTinyLabelValue(f, x, y + h - 52f, w, "STATE", state, state == "CHRG" ? MFDTheme.ACCENT : MFDTheme.STATUS_VAL);
                DrawTinyLabelValue(f, x, y + h - 25f, w, "NET", SignedMw(-netDrain), netDrain > 0.001f ? MFDTheme.STATUS_VAL : MFDTheme.ACCENT);
            }

            static void DrawSysHeader(MySpriteDrawFrame f, float x, float y, float w, string left, string right)
            {
                SpriteHelpers.Tt(f, left, x, y, 0.62f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(f, right, x + w, y, 0.62f, MFDTheme.CORP_GOLD, MFDTheme.AR);
            }

            static void DrawTinyLabelValue(MySpriteDrawFrame f, float x, float y, float w, string label, string value, Color valueColor)
            {
                SpriteHelpers.Tt(f, label, x, y, 0.54f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(f, value, x + w, y, 0.62f, valueColor, MFDTheme.AR);
            }

            static void DrawHBar(MySpriteDrawFrame f, float x, float y, float w, float h, float pct, Color c)
            {
                pct = Cl(pct, 0f, 1f);
                SpriteHelpers.Bx(f, x + w / 2f, y + h / 2f, w, h, MFDTheme.BAR_TRACK);
                SpriteHelpers.DrawRectangleOutline(f, x, y, w, h, 0.5f, MFDTheme.BORDER);
                float fw = w * pct;
                if (fw > 0.5f) SpriteHelpers.Bx(f, x + fw / 2f, y + h / 2f, fw, h, c);
            }

            static void DrawBarTick(MySpriteDrawFrame f, float x, float y, float w, float h, float pct, Color c)
            {
                pct = Cl(pct, 0f, 1f);
                SpriteHelpers.Bx(f, x + w * pct, y + h / 2f, 1f, h, Cr(c, 0.75f));
            }

            static void EngineHealth2(List<IMyThrust> a, List<IMyThrust> b, out int fn, out int tot)
            {
                int af, at, bf = 0, bt = 0;
                Jet.GetEngineHealth(a, out af, out at);
                if (b != null) Jet.GetEngineHealth(b, out bf, out bt);
                fn = af + bf;
                tot = at + bt;
            }

            static string FmtTime(double s)
            {
                return s <= 0 ? "---" : $"{(int)(s / 60):D2}:{(int)(s % 60):D2}";
            }

            static string SignedKn(float v)
            {
                return v >= 0 ? $"+{v,3:F0} kN" : $"{v,4:F0} kN";
            }

            static string SignedMw(float v)
            {
                return v >= 0 ? $"+{v:F1}MW" : $"{v:F1}MW";
            }

            static void DrawMslPips(MySpriteDrawFrame f, List<IMyShipMergeBlock> bays)
            {
                if (bays == null || bays.Count == 0) return;
                SpriteHelpers.Tt(f, "MSL", 12f, 8f, 0.35f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                for (int i = 0; i < bays.Count; i++)
                {
                    float px = 12f + i * 18f, py = 26f;
                    bool rdy = bays[i] != null && bays[i].IsConnected;
                    SpriteHelpers.Bx(f, px + 7f, py + 7f, 14f, 14f, rdy ? MFDTheme.ACCENT : MFDTheme.BORDER);
                    if (rdy) SpriteHelpers.Bx(f, px + 7f, py + 7f, 10f, 10f, Cr(20, 80, 20));
                }
            }

            static void DrawBlockCount(MySpriteDrawFrame f, RectangleF area, float contentY)
            {
                int cur = gridBlocks.Count, orig = originalBlockCount > 0 ? originalBlockCount : cur;
                Color c = cur >= orig ? MFDTheme.DIM_TEXT_MID : cur > orig * 0.7 ? MFDTheme.WARN : MFDTheme.DANGER;
                SpriteHelpers.Tt(f, $"{cur}/{orig}", area.Width / 2f, contentY + 4f, 0.45f, c);
            }

            static void DrawFlightData(MySpriteDrawFrame f, RectangleF area, HUDModule hud, Jet jet, float contentY)
            {
                float rx = area.Width - 6f, y = contentY + 8f, lh = 18f;
                if (hud != null)
                {
                    _animSpeed.SetTarget(hud.smoothedVelocity);
                    _animAltitude.SetTarget(hud.smoothedAltitude);
                    _animAoA.SetTarget(hud.smoothedAoA);
                    _animMach.SetTarget(hud.mach);
                    _animThrottle.SetTarget(hud.throttlePercent);

                    double spd = _animSpeed.Value;
                    double alt = _animAltitude.Value;
                    double aoa = _animAoA.Value;
                    double mch = _animMach.Value;
                    double thr = _animThrottle.Value;

                    SpriteHelpers.Tt(f, $"SPD {spd,4:F0} KPH", rx, y, 0.45f, MFDTheme.STATUS_VAL, MFDTheme.AR);
                    Color altC = hud.smoothedAltitude < 200
                        ? Anim.WithAlpha(MFDTheme.DANGER, Anim.WarnAlpha())
                        : MFDTheme.STATUS_VAL;
                    SpriteHelpers.Tt(f, $"ALT {alt,4:F0} M", rx, y + lh, 0.45f, altC, MFDTheme.AR);
                    SpriteHelpers.Tt(f, $"AOA {aoa,5:F1}\u00B0", rx, y + lh * 2, 0.45f,
                        Ab(hud.smoothedAoA) > 15 ? MFDTheme.DANGER : Ab(hud.smoothedAoA) > 10 ? MFDTheme.WARN : MFDTheme.STATUS_VAL, MFDTheme.AR);
                    SpriteHelpers.Tt(f, $"MCH {mch,4:F2}", rx, y + lh * 3, 0.45f, MFDTheme.STATUS_VAL, MFDTheme.AR);
                    SpriteHelpers.Tt(f, $"THR {thr,3:F0}%", rx, y + lh * 4, 0.45f,
                        hud.throttlePercent < 20 ? MFDTheme.DANGER : MFDTheme.ACCENT, MFDTheme.AR);
                }

                float gy = y + lh * 5 + 4f;
                int ammo = jet.GetTotalGunAmmo();
                Color gc = ammo <= 0 ? MFDTheme.DANGER : ammo < 500 ? MFDTheme.WARN : MFDTheme.ACCENT;
                SpriteHelpers.Tt(f, "GUN", rx - 100f, gy, 0.35f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                float bx = rx - 60f;
                SpriteHelpers.Bx(f, bx + 20f, gy + 6f, 40f, 8f, MFDTheme.BAR_TRACK);
                float pct = Mn(ammo / 2400f, 1f);
                if (pct > 0.01f) SpriteHelpers.Bx(f, bx + 20f * pct, gy + 6f, 40f * pct, 8f, gc);
                SpriteHelpers.Tt(f, ammo.ToString("0000"), rx, gy - 2f, 0.4f, gc, MFDTheme.AR);
            }

            static void DrawFuelBar(MySpriteDrawFrame f, RectangleF area, List<IMyGasTank> tanks,
                float contentY, float contentBot)
            {
                if (tanks == null || tanks.Count == 0) return;
                double cap = 0, filled = 0;
                foreach (var t in tanks)
                    if (t.BlockDefinition.SubtypeId.Contains("Hydrogen"))
                    { cap += t.Capacity; filled += t.Capacity * t.FilledRatio; }
                if (cap <= 0) return;

                _animFuelBar.SetTarget(filled / cap);
                double pct = _animFuelBar.Value;
                float bx = 27f;
                float top = contentY + 30f;
                float bot = contentBot - 30f;
                float bh = bot - top;
                Color fc = pct < BINGO_FUEL ? MFDTheme.DANGER : pct < LOW_FUEL ? MFDTheme.WARN : MFDTheme.ACCENT;

                SpriteHelpers.Tt(f, $"{pct * 100:F0}%", bx, top - 18f, 0.5f, fc);
                SpriteHelpers.Bx(f, bx, top + bh / 2f, 16f, bh + 2f, MFDTheme.BORDER);
                SpriteHelpers.Bx(f, bx, top + bh / 2f, 14f, bh, MFDTheme.BAR_TRACK);

                float fh = bh * (float)pct;
                if (fh > 1f) SpriteHelpers.Bx(f, bx, top + bh - fh / 2f, 14f, fh, fc);

                if (pct > 0.01)
                {
                    double tr = pct * 600;
                    SpriteHelpers.Tt(f, $"{(int)(tr / 60):D2}:{(int)(tr % 60):D2}", bx + 11f, top + bh / 2f - 8f, 0.35f,
                        MFDTheme.DIM_TEXT_MID, MFDTheme.AL);
                }
                Color bingoColor = pct < BINGO_FUEL ? Anim.WithAlpha(fc, Anim.WarnAlpha()) : fc;
                SpriteHelpers.Tt(f, pct < BINGO_FUEL ? "BINGO" : "FUEL", bx, bot + 4f, 0.4f, bingoColor);
            }

            static void DrawGMeter(MySpriteDrawFrame f, RectangleF area, HUDModule hud,
                float contentY, float contentBot)
            {
                if (hud == null) return;
                _animGForce.SetTarget(hud.smoothedGForces);
                double g = _animGForce.Value, pk = hud.peakGForce;

                float size = Mn(82f, contentBot - contentY - 122f);
                if (size < 52f) size = 52f;
                float cx = area.Width - 46f;
                float cy = contentBot - size * 0.58f - 18f;
                if (cy < contentY + 128f) cy = contentY + 128f;

                Color gvc = Ab(g) > 7 ? MFDTheme.DANGER : Ab(g) > 5 ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
                SpriteHelpers.Sp(f, TEX_GMETER_FACE, cx, cy, size, size, MFDTheme.BORDER_LIGHT);
                SpriteHelpers.Sp(f, TEX_GAUGE_NEEDLE, cx, cy, size * 0.86f, size * 0.86f,
                    Cr(MFDTheme.DIM_TEXT_MID, 0.35f), GToNeedleRotation(pk));
                SpriteHelpers.Sp(f, TEX_GAUGE_NEEDLE, cx, cy, size * 0.90f, size * 0.90f,
                    gvc, GToNeedleRotation(g));
                SpriteHelpers.Sp(f, TEXTURE_CIRCLE_SOLID, cx, cy, 5f, 5f, MFDTheme.BRIGHT_TEXT);

                SpriteHelpers.Tt(f, "-3", cx - size * 0.39f, cy + size * 0.34f, 0.28f, MFDTheme.DIM_TEXT);
                SpriteHelpers.Tt(f, "+9", cx + size * 0.39f, cy + size * 0.34f, 0.28f, MFDTheme.DIM_TEXT);
                SpriteHelpers.Tt(f, $"{g,4:F1}G", cx, cy + size * 0.48f, 0.43f, gvc);
                SpriteHelpers.Tt(f, $"PK {pk,4:F1}", cx, cy + size * 0.48f + 14f, 0.28f, MFDTheme.DIM_TEXT);
            }

            static float GToNeedleRotation(double g)
            {
                double clamped = Cl(g, -3.0, 9.0);
                double t = (clamped + 3.0) / 12.0;
                return ToRad((float)(-135.0 + t * 270.0));
            }
        }
    }
}
