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
            static int refreshTick;

            static double BINGO_FUEL => SystemManager.GetConfigValue("bingo_fuel");
            static double LOW_FUEL => SystemManager.GetConfigValue("low_fuel");

            static readonly Color C_OK = new Color(50, 255, 50);
            static readonly Color C_DMG = Color.Yellow;
            static readonly Color C_CRIT = Color.Red;
            static readonly Color C_DEAD = new Color(139, 0, 0);
            static readonly Color C_DIM = new Color(85, 85, 85);
            static readonly Color C_VAL = new Color(170, 170, 170);
            static readonly Color C_BG = new Color(20, 20, 20);

            // Sprite helpers to eliminate boilerplate
            static void Txt(MySpriteDrawFrame f, string d, float x, float y, float s, Color c, TextAlignment a = TextAlignment.CENTER)
            {
                f.Add(new MySprite { Type = SpriteType.TEXT, Data = d, Position = new Vector2(x, y), RotationOrScale = s, Color = c, Alignment = a, FontId = "Monospace" });
            }

            static void Box(MySpriteDrawFrame f, float x, float y, float w, float h, Color c)
            {
                f.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x, y), Size = new Vector2(w, h), Color = c, Alignment = TextAlignment.CENTER });
            }

            public static void Render(MySpriteDrawFrame frame, RectangleF area,
                Program program, Jet jet, RadarControlModule radarModule, HUDModule hud = null)
            {
                if (refreshTick <= 0 || gridBlocks.Count == 0)
                {
                    gridBlocks.Clear();
                    program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(gridBlocks);
                    refreshTick = 60;
                    if (originalBlockCount == 0) originalBlockCount = gridBlocks.Count;
                    RebuildSpriteCache(area);
                }
                else refreshTick--;

                for (int i = 0; i < cachedSprites.Count; i++)
                    frame.Add(cachedSprites[i]);

                DrawMslPips(frame, jet._bays);
                DrawBlockCount(frame, area);
                DrawFlightData(frame, area, hud, jet);
                DrawFuelBar(frame, area, jet.tanks);
                DrawGMeter(frame, area, hud);
            }

            static void RebuildSpriteCache(RectangleF area)
            {
                var blocks = gridBlocks;
                if (blocks.Count == 0) return;
                cachedSprites.Clear();

                int minX = int.MaxValue, maxX = int.MinValue;
                int minZ = int.MaxValue, maxZ = int.MinValue;
                foreach (var b in blocks)
                {
                    var p = b.Position;
                    if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                    if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
                }

                int w = maxX - minX + 1, h = maxZ - minZ + 1;
                bool[,] occ = new bool[w, h];
                float[,] integrity = new float[w, h];
                bool[,] functional = new bool[w, h];

                for (int x = 0; x < w; x++)
                    for (int z = 0; z < h; z++) { integrity[x, z] = 1f; functional[x, z] = true; }

                foreach (var b in blocks)
                {
                    int x = b.Position.X - minX, z = b.Position.Z - minZ;
                    occ[x, z] = true;
                    var slim = b.CubeGrid.GetCubeBlock(b.Position);
                    if (slim != null)
                    {
                        float mi = slim.MaxIntegrity;
                        float r = mi > 0 ? (mi - slim.CurrentDamage) / mi : 0f;
                        if (r < integrity[x, z]) integrity[x, z] = r;
                    }
                    if (!b.IsFunctional) functional[x, z] = false;
                }

                // Grid fits inside margins: left 55 (fuel), right 40 (g-meter), top 55 (header), bottom 105 (20% clear)
                float gL = 55f, gR = area.Width - 40f, gT = 55f, gB = area.Height - 105f;
                float cs = Math.Min((gR - gL) / w, (gB - gT) / h);
                cs = Math.Min(cs, 16f); // cap cell size
                Vector2 center = new Vector2((gL + gR) / 2f, (gT + gB) / 2f);
                Vector2 topLeft = center - new Vector2(w * cs, h * cs) / 2f;

                for (int x = 0; x < w; x++)
                {
                    for (int z = 0; z < h; z++)
                    {
                        if (!occ[x, z]) continue;
                        bool outline = false;
                        if (x == 0 || !occ[x - 1, z]) outline = true;
                        else if (x == w - 1 || !occ[x + 1, z]) outline = true;
                        else if (z == 0 || !occ[x, z - 1]) outline = true;
                        else if (z == h - 1 || !occ[x, z + 1]) outline = true;
                        if (!outline) continue;

                        Color c;
                        if (!functional[x, z]) c = C_DEAD;
                        else if (integrity[x, z] < 0.30f) c = C_CRIT;
                        else if (integrity[x, z] < 0.80f) c = C_DMG;
                        else c = C_OK;

                        Vector2 dp = topLeft + new Vector2(x * cs + cs / 2f, (h - 1 - z) * cs + cs / 2f);
                        cachedSprites.Add(new MySprite
                        {
                            Type = SpriteType.TEXTURE, Data = "SquareSimple",
                            Position = dp, Size = new Vector2(cs * 5f, cs * 2f),
                            Color = c, Alignment = TextAlignment.CENTER
                        });
                    }
                }
            }

            static void DrawMslPips(MySpriteDrawFrame f, List<IMyShipMergeBlock> bays)
            {
                if (bays == null || bays.Count == 0) return;
                Txt(f, "MSL", 12f, 8f, 0.35f, C_DIM, TextAlignment.LEFT);
                for (int i = 0; i < bays.Count; i++)
                {
                    float px = 12f + i * 18f, py = 26f;
                    bool rdy = bays[i] != null && bays[i].IsConnected;
                    Box(f, px + 7f, py + 7f, 14f, 14f, rdy ? Color.Lime : new Color(51, 51, 51));
                    if (rdy) Box(f, px + 7f, py + 7f, 10f, 10f, new Color(20, 100, 20));
                }
            }

            static void DrawBlockCount(MySpriteDrawFrame f, RectangleF area)
            {
                int cur = gridBlocks.Count, orig = originalBlockCount > 0 ? originalBlockCount : cur;
                Color c = cur >= orig ? new Color(100, 100, 100) : cur > orig * 0.7 ? C_DMG : C_CRIT;
                Txt(f, $"{cur}/{orig}", area.Width / 2f, 6f, 0.45f, c);
            }

            static void DrawFlightData(MySpriteDrawFrame f, RectangleF area, HUDModule hud, Jet jet)
            {
                float rx = area.Width - 6f, y = 8f, lh = 18f;
                if (hud != null)
                {
                    FVal(f, rx, y, "SPD", $"{hud.smoothedVelocity:F0} kph", C_VAL);
                    FVal(f, rx, y + lh, "ALT", $"{hud.smoothedAltitude:F0} m", hud.smoothedAltitude < 200 ? C_CRIT : C_VAL);
                    double aoa = hud.smoothedAoA;
                    FVal(f, rx, y + lh * 2, "AoA", $"{aoa:F1}\u00B0", Math.Abs(aoa) > 15 ? C_CRIT : Math.Abs(aoa) > 10 ? C_DMG : C_VAL);
                    FVal(f, rx, y + lh * 3, "MCH", $"{hud.mach:F2}", C_VAL);
                    FVal(f, rx, y + lh * 4, "THR", $"{hud.smoothedThrottle:F0}%", hud.smoothedThrottle < 20 ? C_CRIT : Color.Lime);
                }

                // GUN
                float gy = y + lh * 5 + 4f;
                int ammo = jet.GetTotalGunAmmo();
                Color gc = ammo <= 0 ? C_CRIT : ammo < 500 ? C_DMG : Color.Lime;
                Txt(f, "GUN", rx - 100f, gy, 0.35f, C_DIM, TextAlignment.LEFT);
                float bx = rx - 60f;
                Box(f, bx + 20f, gy + 6f, 40f, 8f, new Color(26, 26, 26));
                float pct = Math.Min(ammo / 2400f, 1f);
                if (pct > 0.01f) Box(f, bx + 20f * pct, gy + 6f, 40f * pct, 8f, gc);
                Txt(f, ammo.ToString(), rx, gy - 2f, 0.4f, gc, TextAlignment.RIGHT);
            }

            static void FVal(MySpriteDrawFrame f, float rx, float y, string lbl, string val, Color vc)
            {
                Txt(f, $"{lbl} {val}", rx, y, 0.45f, vc, TextAlignment.RIGHT);
            }

            static void DrawFuelBar(MySpriteDrawFrame f, RectangleF area, List<IMyGasTank> tanks)
            {
                if (tanks == null || tanks.Count == 0) return;
                double cap = 0, filled = 0;
                foreach (var t in tanks)
                    if (t.BlockDefinition.SubtypeId.Contains("Hydrogen"))
                    { cap += t.Capacity; filled += t.Capacity * t.FilledRatio; }
                if (cap <= 0) return;

                double pct = filled / cap;
                float bx = 27f, top = 60f, bot = area.Height - 70f, bh = bot - top;
                Color fc = pct < BINGO_FUEL ? C_CRIT : pct < LOW_FUEL ? C_DMG : Color.Lime;

                Txt(f, $"{pct * 100:F0}%", bx, top - 18f, 0.5f, fc);
                Box(f, bx, top + bh / 2f, 16f, bh + 2f, new Color(50, 128, 50));
                Box(f, bx, top + bh / 2f, 14f, bh, C_BG);

                float fh = bh * (float)pct;
                if (fh > 1f) Box(f, bx, top + bh - fh / 2f, 14f, fh, fc);

                if (pct > 0.01)
                {
                    double tr = pct * 600;
                    Txt(f, $"{(int)(tr / 60):D2}:{(int)(tr % 60):D2}", bx + 11f, top + bh / 2f - 8f, 0.35f, new Color(150, 150, 150), TextAlignment.LEFT);
                }
                Txt(f, pct < BINGO_FUEL ? "BINGO" : "FUEL", bx, bot + 4f, 0.4f, fc);
            }

            static void DrawGMeter(MySpriteDrawFrame f, RectangleF area, HUDModule hud)
            {
                if (hud == null) return;
                float mx = area.Width - 20f, top = 145f, bh = 100f, cy = top + bh / 2f;
                double g = hud.smoothedGForces, pk = hud.peakGForce;

                Txt(f, "+9", mx, top - 16f, 0.35f, C_DIM);
                Box(f, mx, cy, 14f, bh + 2f, new Color(51, 51, 51));
                Box(f, mx, cy, 12f, bh, new Color(10, 10, 10));
                Box(f, mx, cy, 12f, 1f, new Color(68, 68, 68));

                float half = bh / 2f;
                float gc = (float)MathHelper.Clamp(g, -3, 9);
                Color fc = g > 7 ? C_CRIT : g > 5 ? C_DMG : g < -1 ? new Color(102, 136, 255) : Color.Lime;

                if (gc >= 0)
                {
                    float fh = half * gc / 9f;
                    if (fh > 1f) Box(f, mx, cy - fh / 2f, 10f, fh, fc);
                }
                else
                {
                    float fh = half * Math.Abs(gc) / 3f;
                    if (fh > 1f) Box(f, mx, cy + fh / 2f, 10f, fh, fc);
                }

                Txt(f, "-3", mx, top + bh + 2f, 0.35f, C_DIM);
                Color gvc = Math.Abs(g) > 7 ? C_CRIT : Math.Abs(g) > 5 ? C_DMG : C_VAL;
                Txt(f, $"{g:F1}G", mx, top + bh + 18f, 0.45f, gvc);
                Txt(f, $"pk {pk:F1}", mx, top + bh + 36f, 0.3f, C_DIM);
            }
        }
    }
}
