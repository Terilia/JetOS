using Sandbox.ModAPI.Ingame;
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
                DrawStatusSynoptic(frame, innerLeft, synTop, innerW, contentBot - synTop - 2f, jet, hud);
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

                int useFn = jet.LeftUseFn + jet.RightUseFn;
                int useTot = jet.LeftUseTot + jet.RightUseTot;
                int allFn = jet.LeftAllFn + jet.RightAllFn;
                int allTot = jet.LeftAllTot + jet.RightAllTot;

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

            static void DrawStatusSynoptic(MySpriteDrawFrame f, float x, float y, float w, float h, Jet jet, HUDModule hud)
            {
                if (h < 114f) return;
                SpriteHelpers.Bx(f, x + w / 2f, y, w, 1f, MFDTheme.GOLD_LINE);
                float gap = 4f;
                float colW = (w - gap * 2f) / 3f;
                DrawPropulsionSection(f, x, y + 4f, colW, h - 4f, jet);
                SpriteHelpers.Bx(f, x + colW + gap / 2f, y + h / 2f + 3f, 1f, h - 8f, MFDTheme.BORDER);
                DrawLoadGSection(f, x + colW + gap, y + 4f, colW, h - 4f, hud);
                SpriteHelpers.Bx(f, x + colW * 2f + gap * 1.5f, y + h / 2f + 3f, 1f, h - 8f, MFDTheme.BORDER);
                DrawLoadVSection(f, x + colW * 2f + gap * 2f, y + 4f, colW, h - 4f, jet, hud);
            }

            static void DrawPropulsionSection(MySpriteDrawFrame f, float x, float y, float w, float h, Jet jet)
            {
                DrawSysHeader(f, x, y, w, "ENG", "THR");
                float s = Mn(w * 0.43f, h - 64f);
                float cy = y + 20f + s * 0.5f;
                DrawDial(f, x + w * 0.28f, cy, s, "L", Pct(jet.LeftUseCurKN, jet.LeftUseMaxKN),
                    $"{jet.LeftAllFn}/{jet.LeftAllTot}", jet.LeftAllFn < jet.LeftAllTot ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
                DrawDial(f, x + w * 0.72f, cy, s, "R", Pct(jet.RightUseCurKN, jet.RightUseMaxKN),
                    $"{jet.RightAllFn}/{jet.RightAllTot}", jet.RightAllFn < jet.RightAllTot ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
                int lFn = jet.LeftAbFn, lTot = jet.LeftAbTot, rFn = jet.RightAbFn, rTot = jet.RightAbTot;
                float diff = jet.LeftUseCurKN - jet.RightUseCurKN;
                DrawTinyLabelValue(f, x, y + h - 52f, w, "AB", $"{lFn + rFn}/{lTot + rTot}", lFn + rFn < lTot + rTot ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
                DrawTinyLabelValue(f, x, y + h - 25f, w, "BAL", SignedKn(diff), Ab(diff) > 30f ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
            }

            static void DrawLoadGSection(MySpriteDrawFrame f, float x, float y, float w, float h, HUDModule hud)
            {
                double g = hud != null ? hud.smoothedGForces : 0;
                double aoa = hud != null ? hud.smoothedAoA : 0;
                DrawSysHeader(f, x, y, w, "LOAD", "G/AOA");
                float s = Mn(w * 0.72f, h - 48f);
                Color c = g > 7 || g < -1 ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
                DrawDial(f, x + w / 2f, y + 22f + s / 2f, s, "G", (float)((g + 3) / 12.0), $"{g:F1}", c);
                DrawTinyLabelValue(f, x, y + h - 25f, w, "AOA", $"{aoa,4:F1}", Ab(aoa) > 18 ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
            }

            static void DrawLoadVSection(MySpriteDrawFrame f, float x, float y, float w, float h, Jet jet, HUDModule hud)
            {
                double vvi = hud != null ? hud.verticalVelocityMps : 0;
                DrawSysHeader(f, x, y, w, "LOAD", "VVI");
                float s = Mn(w * 0.72f, h - 48f);
                Color c = Ab(vvi) > 30 ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
                DrawDial(f, x + w / 2f, y + 22f + s / 2f, s, "VVI", (float)((vvi + 60) / 120.0), $"{vvi:F0}", c);
                DrawTinyLabelValue(f, x, y + h - 25f, w, "AGL", $"{jet.SurfaceAltitude,4:F0}", jet.SurfaceAltitude < 100 ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
            }

            static float Pct(float cur, float max)
            {
                return max > 0 ? Cl(cur / max, 0f, 1f) : 0f;
            }

            static void DrawDial(MySpriteDrawFrame f, float cx, float cy, float s, string label, float pct, string val, Color c)
            {
                pct = Cl(pct, 0f, 1f);
                SpriteHelpers.Sp(f, TEX_GMETER_FACE, cx, cy, s, s, Cr(MFDTheme.DIM_TEXT_MID, 0.55f));
                SpriteHelpers.Sp(f, TEX_GAUGE_NEEDLE, cx, cy, s * 0.72f, s * 0.72f, c, -2.25f + pct * 4.5f);
                SpriteHelpers.Tt(f, label, cx, cy - s * 0.25f, 0.42f, MFDTheme.DIM_TEXT, MFDTheme.AC);
                SpriteHelpers.Tt(f, val, cx, cy + s * 0.25f, 0.56f, c, MFDTheme.AC);
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

            static string SignedKn(float v)
            {
                return v >= 0 ? $"+{v,3:F0} kN" : $"{v,4:F0} kN";
            }
        }
    }
}
