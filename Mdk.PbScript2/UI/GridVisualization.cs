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

            public static void Render(MySpriteDrawFrame frame, RectangleF area,
                Program program, Jet jet, RadarControlModule radarModule, HUDModule hud = null)
            {
                float sw = area.Width;
                float sh = area.Height;

                float contentY = MFDFrame.DrawChrome(frame, sw, sh, headerRight: "STATUS", drawFooterNav: false);
                float contentBot = MFDFrame.ContentBottom(sh);

                // Staggered rebuild state machine
                if (rebuildPhase > 0)
                {
                    RunRebuildPhase(program, area, contentY, contentBot);
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
                        cachedContentY = contentY;
                        cachedContentBot = contentBot;
                        refreshTick = REFRESH_INTERVAL;
                    }
                }

                // Always draw cached sprites
                for (int i = 0; i < cachedSprites.Count; i++)
                    frame.Add(cachedSprites[i]);

                DrawMslPips(frame, jet._bays);
                DrawBlockCount(frame, area, contentY);
                DrawFlightData(frame, area, hud, jet, contentY);
                DrawFuelBar(frame, area, jet.tanks, contentY, contentBot);
                DrawGMeter(frame, area, hud, contentY, contentBot);
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
                        cachedSprites.Clear();

                        float gL = 55f, gR = cachedArea.Width - 40f;
                        float gT = cachedContentY + 30f, gB = cachedContentBot - 30f;
                        float cs = Math.Min((gR - gL) / gridW, (gB - gT) / gridH);
                        cs = Math.Min(cs, 16f);
                        Vector2 center = new Vector2((gL + gR) / 2f, (gT + gB) / 2f);
                        Vector2 topLeft = center - new Vector2(gridW * cs, gridH * cs) / 2f;

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
                                if (!gridFunctional[x, z]) c = new Color(120, 20, 20);
                                else if (gridIntegrity[x, z] < 0.30f) c = new Color(180, 50, 40);
                                else if (gridIntegrity[x, z] < 0.80f) c = MFDTheme.WARN;
                                else c = MFDTheme.ACCENT;

                                // Flip X so left side of ship shows on left side of screen
                                // (SE grid X+ is leftward from cockpit perspective)
                                Vector2 dp = topLeft + new Vector2((gridW - 1 - x) * cs + cs / 2f, (gridH - 1 - z) * cs + cs / 2f);
                                cachedSprites.Add(new MySprite
                                {
                                    Type = SpriteType.TEXTURE, Data = MFDTheme.SQ,
                                    Position = dp, Size = new Vector2(cs * 5f, cs * 2f),
                                    Color = c, Alignment = TextAlignment.CENTER
                                });
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

            static void DrawMslPips(MySpriteDrawFrame f, List<IMyShipMergeBlock> bays)
            {
                if (bays == null || bays.Count == 0) return;
                Txt(f, "MSL", 12f, 8f, 0.35f, MFDTheme.DIM_TEXT, TextAlignment.LEFT);
                for (int i = 0; i < bays.Count; i++)
                {
                    float px = 12f + i * 18f, py = 26f;
                    bool rdy = bays[i] != null && bays[i].IsConnected;
                    Box(f, px + 7f, py + 7f, 14f, 14f, rdy ? MFDTheme.ACCENT : MFDTheme.BORDER);
                    if (rdy) Box(f, px + 7f, py + 7f, 10f, 10f, new Color(20, 80, 20));
                }
            }

            static void DrawBlockCount(MySpriteDrawFrame f, RectangleF area, float contentY)
            {
                int cur = gridBlocks.Count, orig = originalBlockCount > 0 ? originalBlockCount : cur;
                Color c = cur >= orig ? MFDTheme.DIM_TEXT_MID : cur > orig * 0.7 ? MFDTheme.WARN : new Color(180, 50, 40);
                Txt(f, $"{cur}/{orig}", area.Width / 2f, contentY + 4f, 0.45f, c);
            }

            static void DrawFlightData(MySpriteDrawFrame f, RectangleF area, HUDModule hud, Jet jet, float contentY)
            {
                float rx = area.Width - 6f, y = contentY + 8f, lh = 18f;
                if (hud != null)
                {
                    FVal(f, rx, y, "SPD", $"{hud.smoothedVelocity:F0} kph", MFDTheme.STATUS_VAL);
                    FVal(f, rx, y + lh, "ALT", $"{hud.smoothedAltitude:F0} m",
                        hud.smoothedAltitude < 200 ? new Color(180, 50, 40) : MFDTheme.STATUS_VAL);
                    double aoa = hud.smoothedAoA;
                    FVal(f, rx, y + lh * 2, "AoA", $"{aoa:F1}\u00B0",
                        Math.Abs(aoa) > 15 ? new Color(180, 50, 40) : Math.Abs(aoa) > 10 ? MFDTheme.WARN : MFDTheme.STATUS_VAL);
                    FVal(f, rx, y + lh * 3, "MCH", $"{hud.mach:F2}", MFDTheme.STATUS_VAL);
                    FVal(f, rx, y + lh * 4, "THR", $"{hud.smoothedThrottle:F0}%",
                        hud.smoothedThrottle < 20 ? new Color(180, 50, 40) : MFDTheme.ACCENT);
                }

                float gy = y + lh * 5 + 4f;
                int ammo = jet.GetTotalGunAmmo();
                Color gc = ammo <= 0 ? new Color(180, 50, 40) : ammo < 500 ? MFDTheme.WARN : MFDTheme.ACCENT;
                Txt(f, "GUN", rx - 100f, gy, 0.35f, MFDTheme.DIM_TEXT, TextAlignment.LEFT);
                float bx = rx - 60f;
                Box(f, bx + 20f, gy + 6f, 40f, 8f, MFDTheme.BAR_TRACK);
                float pct = Math.Min(ammo / 2400f, 1f);
                if (pct > 0.01f) Box(f, bx + 20f * pct, gy + 6f, 40f * pct, 8f, gc);
                Txt(f, ammo.ToString(), rx, gy - 2f, 0.4f, gc, TextAlignment.RIGHT);
            }

            static void FVal(MySpriteDrawFrame f, float rx, float y, string lbl, string val, Color vc)
            {
                Txt(f, $"{lbl} {val}", rx, y, 0.45f, vc, TextAlignment.RIGHT);
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

                double pct = filled / cap;
                float bx = 27f;
                float top = contentY + 30f;
                float bot = contentBot - 30f;
                float bh = bot - top;
                Color fc = pct < BINGO_FUEL ? new Color(180, 50, 40) : pct < LOW_FUEL ? MFDTheme.WARN : MFDTheme.ACCENT;

                Txt(f, $"{pct * 100:F0}%", bx, top - 18f, 0.5f, fc);
                Box(f, bx, top + bh / 2f, 16f, bh + 2f, MFDTheme.BORDER);
                Box(f, bx, top + bh / 2f, 14f, bh, MFDTheme.BAR_TRACK);

                float fh = bh * (float)pct;
                if (fh > 1f) Box(f, bx, top + bh - fh / 2f, 14f, fh, fc);

                if (pct > 0.01)
                {
                    double tr = pct * 600;
                    Txt(f, $"{(int)(tr / 60):D2}:{(int)(tr % 60):D2}", bx + 11f, top + bh / 2f - 8f, 0.35f,
                        MFDTheme.DIM_TEXT_MID, TextAlignment.LEFT);
                }
                Txt(f, pct < BINGO_FUEL ? "BINGO" : "FUEL", bx, bot + 4f, 0.4f, fc);
            }

            static void DrawGMeter(MySpriteDrawFrame f, RectangleF area, HUDModule hud,
                float contentY, float contentBot)
            {
                if (hud == null) return;
                float mx = area.Width - 20f;
                float top = contentY + 100f;
                float bh = contentBot - top - 40f;
                if (bh < 30f) return;
                float cy = top + bh / 2f;
                double g = hud.smoothedGForces, pk = hud.peakGForce;

                Txt(f, "+9", mx, top - 16f, 0.35f, MFDTheme.DIM_TEXT);
                Box(f, mx, cy, 14f, bh + 2f, MFDTheme.BORDER);
                Box(f, mx, cy, 12f, bh, MFDTheme.BAR_TRACK);
                Box(f, mx, cy, 12f, 1f, MFDTheme.DIM_TEXT);

                float half = bh / 2f;
                float gc = (float)MathHelper.Clamp(g, -3, 9);
                Color fColor = g > 7 ? new Color(180, 50, 40) : g > 5 ? MFDTheme.WARN
                    : g < -1 ? new Color(80, 110, 200) : MFDTheme.ACCENT;

                if (gc >= 0)
                {
                    float fh = half * gc / 9f;
                    if (fh > 1f) Box(f, mx, cy - fh / 2f, 10f, fh, fColor);
                }
                else
                {
                    float fh = half * Math.Abs(gc) / 3f;
                    if (fh > 1f) Box(f, mx, cy + fh / 2f, 10f, fh, fColor);
                }

                Txt(f, "-3", mx, top + bh + 2f, 0.35f, MFDTheme.DIM_TEXT);
                Color gvc = Math.Abs(g) > 7 ? new Color(180, 50, 40) : Math.Abs(g) > 5 ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
                Txt(f, $"{g:F1}G", mx, top + bh + 18f, 0.45f, gvc);
                Txt(f, $"pk {pk:F1}", mx, top + bh + 36f, 0.3f, MFDTheme.DIM_TEXT);
            }

            static void Txt(MySpriteDrawFrame f, string d, float x, float y, float s, Color c, TextAlignment a = TextAlignment.CENTER)
            {
                f.Add(new MySprite { Type = SpriteType.TEXT, Data = d, Position = new Vector2(x, y), RotationOrScale = s, Color = c, Alignment = a, FontId = MFDTheme.FONT });
            }

            static void Box(MySpriteDrawFrame f, float x, float y, float w, float h, Color c)
            {
                f.Add(new MySprite { Type = SpriteType.TEXTURE, Data = MFDTheme.SQ, Position = new Vector2(x, y), Size = new Vector2(w, h), Color = c, Alignment = TextAlignment.CENTER });
            }
        }
    }
}
