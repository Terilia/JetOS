using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class TerrainRenderer
        {
            const int SIDE = 14;
            const int SIDE_STRIDE = 3;
            const int RECOMPUTE = 15;
            const float HP = 1.5708f; // π/2

            // Sprite cache — heavy computation runs every RECOMPUTE ticks,
            // replay is near-free on all other ticks.
            static List<MySprite> _sp = new List<MySprite>(256);
            static int _lastTick = -99;
            static int _lastCtx = -1;

            // Work buffer
            static double[] _clr;

            // Contour thresholds and their colors
            static readonly double[] TH = { 0, 50, 150 };
            static readonly Color[] TC = {
                new Color(180, 50, 40),
                new Color(160, 140, 40),
                new Color(64, 140, 48),
            };
            static readonly Color CFIT_H = new Color(180, 50, 40, 120);
            static readonly Color CLOSE_H = new Color(160, 140, 40, 80);

            /// <summary>
            /// Draws smooth contour lines with marching squares.
            /// Heading-up: map rotates with the jet via rotated cache sampling.
            /// Sprites are cached and replayed between recomputation ticks.
            /// ctx: 0=fullscreen module, 1=sidebar (triggers recompute on switch).
            /// </summary>
            public static void DrawContours(MySpriteDrawFrame frame,
                float mx, float my, float cs, int disp,
                int sRow, int sCol, int stride, double sAlt,
                float lt, Vector3D jF, Vector3D jR, int ctx)
            {
                int tick = SystemManager.currentTick;
                if (ctx != _lastCtx || tick - _lastTick >= RECOMPUTE)
                {
                    _lastCtx = ctx;
                    _lastTick = tick;
                    Compute(mx, my, cs, disp, sRow, sCol, stride, sAlt, lt, jF, jR);
                }
                for (int i = 0; i < _sp.Count; i++)
                    frame.Add(_sp[i]);
            }

            static void Compute(float mx, float my, float cs, int d,
                int sRow, int sCol, int stride, double sAlt,
                float lt, Vector3D jF, Vector3D jR)
            {
                _sp.Clear();
                int total = d * d;
                if (_clr == null || _clr.Length < total)
                    _clr = new double[total];

                int half = d / 2;

                // Rotation matrix: jet display axes → cache grid axes
                Vector3D cF = TerrainAPI.GridForward, cR = TerrainAPI.GridRight;
                double sff = stride * VD(jF, cF), sfr = stride * VD(jF, cR);
                double srf = stride * VD(jR, cF), srr = stride * VD(jR, cR);

                // Fill clearance buffer with heading-up rotated sampling
                for (int r = 0; r < d; r++)
                {
                    int dr = half - r;
                    for (int c = 0; c < d; c++)
                    {
                        int dc = c - half;
                        int cr = sRow + (int)(dr * sff + dc * srf);
                        int cc = sCol + (int)(dr * sfr + dc * srr);
                        _clr[r * d + c] = sAlt - TerrainAPI.SurfaceAlt(cr, cc);
                    }
                }

                // Marching squares for each contour threshold
                int g = d - 1;
                for (int t = 0; t < TH.Length; t++)
                {
                    double th = TH[t];
                    Color col = TC[t];

                    for (int r = 0; r < g; r++)
                    {
                        for (int c = 0; c < g; c++)
                        {
                            double v0 = _clr[r * d + c];
                            double v1 = _clr[r * d + c + 1];
                            double v2 = _clr[(r + 1) * d + c];
                            double v3 = _clr[(r + 1) * d + c + 1];

                            int m = 0;
                            if (v0 >= th) m |= 1;
                            if (v1 >= th) m |= 2;
                            if (v2 >= th) m |= 4;
                            if (v3 >= th) m |= 8;
                            if (m == 0 || m == 15) continue;

                            float px = mx + c * cs, py = my + r * cs;
                            Vector2 eT = new Vector2(px + Lrp(v0, v1, th) * cs, py);
                            Vector2 eR = new Vector2(px + cs, py + Lrp(v1, v3, th) * cs);
                            Vector2 eB = new Vector2(px + Lrp(v2, v3, th) * cs, py + cs);
                            Vector2 eL = new Vector2(px, py + Lrp(v0, v2, th) * cs);

                            switch (m)
                            {
                                case 1: case 14: AL(eT, eL, lt, col); break;
                                case 2: case 13: AL(eT, eR, lt, col); break;
                                case 3: case 12: AL(eL, eR, lt, col); break;
                                case 4: case 11: AL(eL, eB, lt, col); break;
                                case 5: case 10: AL(eT, eB, lt, col); break;
                                case 7: case 8:  AL(eR, eB, lt, col); break;
                                case 6: AL(eT, eL, lt, col); AL(eR, eB, lt, col); break;
                                case 9: AL(eT, eR, lt, col); AL(eL, eB, lt, col); break;
                            }
                        }
                    }
                }

                // CFIT hatching — sparse red X-marks
                for (int r = 0; r < d; r++)
                    for (int c = 0; c < d; c++)
                    {
                        if (_clr[r * d + c] >= 0 || (r + c) % 3 != 0) continue;
                        float px = mx + c * cs + cs * 0.5f, py = my + r * cs + cs * 0.5f;
                        float sz = cs * 0.8f;
                        _sp.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ,
                            Position = new Vector2(px, py), Size = new Vector2(sz, lt),
                            Color = CFIT_H, Alignment = MFDTheme.AC, RotationOrScale = 0.785f });
                        _sp.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ,
                            Position = new Vector2(px, py), Size = new Vector2(sz, lt),
                            Color = CFIT_H, Alignment = MFDTheme.AC, RotationOrScale = -0.785f });
                    }

                // Close hatching — sparse amber diagonals
                for (int r = 0; r < d; r++)
                    for (int c = 0; c < d; c++)
                    {
                        double cl = _clr[r * d + c];
                        if (cl < 0 || cl >= 50 || (r + c) % 4 != 0) continue;
                        float px = mx + c * cs + cs * 0.5f, py = my + r * cs + cs * 0.5f;
                        _sp.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ,
                            Position = new Vector2(px, py), Size = new Vector2(cs * 0.8f, lt * 0.7f),
                            Color = CLOSE_H, Alignment = MFDTheme.AC, RotationOrScale = 0.785f });
                    }
            }

            static float Lrp(double a, double b, double th)
            {
                double d = b - a;
                if (d > -0.01 && d < 0.01) return 0.5f;
                float v = (float)((th - a) / d);
                return v < 0f ? 0f : v > 1f ? 1f : v;
            }

            static void AL(Vector2 a, Vector2 b, float t, Color c)
            {
                Vector2 d = b - a;
                float len = d.Length();
                if (len < 0.5f) return;
                _sp.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ,
                    Position = (a + b) * 0.5f, Size = new Vector2(t, len),
                    Color = c, Alignment = MFDTheme.AC,
                    RotationOrScale = (float)At2(d.Y, d.X) - HP });
            }

            // ── Gravity-plane projected jet axes (shared helper) ──
            public static void JetAxes(Jet jet, out Vector3D jF, out Vector3D jR)
            {
                Vector3D up = VN(-jet.CachedGravity);
                jF = jet._cockpit.WorldMatrix.Forward;
                jF = jF - VD(jF, up) * up;
                if (jF.LengthSquared() > 0.01) jF = VN(jF);
                jR = jet._cockpit.WorldMatrix.Right;
                jR = jR - VD(jR, up) * up;
                if (jR.LengthSquared() > 0.01) jR = VN(jR);
            }

            // ── Sidebar render ──
            public static void Render(MySpriteDrawFrame frame, RectangleF area, Jet jet)
            {
                if (jet._cockpit == null) return;
                float sw = area.Width, sh = area.Height;
                float ox = area.Position.X, oy = area.Position.Y;

                MFDFrame.Txt(frame, "TERRAIN", ox + sw / 2f, oy, 0.45f, MFDTheme.MID_TEXT, MFDTheme.AC);

                if (jet.CachedGravity.LengthSquared() < 0.01)
                { MFDFrame.Txt(frame, "NO PLANET", ox + sw / 2f, oy + sh / 2f - 8f, 0.45f, MFDTheme.DIM_TEXT, MFDTheme.AC); return; }
                if (!TerrainAPI.IsAvailable)
                { MFDFrame.Txt(frame, "N/A", ox + sw / 2f, oy + sh / 2f - 8f, 0.5f, MFDTheme.DIM_TEXT, MFDTheme.AC); return; }
                if (!TerrainAPI.IsReady)
                { MFDFrame.Txt(frame, TerrainAPI.IsLoading ? "LOADING" : "WAIT", ox + sw / 2f, oy + sh / 2f - 8f, 0.45f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC); return; }

                float gridTop = oy + 18f;
                float gridAvail = Math.Min(sw, sh - 40f);
                float gridLeft = ox + (sw - gridAvail) / 2f;
                float cell = gridAvail / SIDE;

                Vector3D shipPos = jet._cockpit.GetPosition();
                int sRow, sCol;
                TerrainAPI.WorldToGrid(shipPos, out sRow, out sCol);

                Vector3D jF, jR;
                JetAxes(jet, out jF, out jR);

                DrawContours(frame, gridLeft, gridTop, cell, SIDE,
                    sRow, sCol, SIDE_STRIDE, TerrainAPI.ShipAlt(shipPos),
                    1.5f, jF, jR, 1);

                float cx = gridLeft + gridAvail / 2f, cy = gridTop + gridAvail / 2f;
                SpriteHelpers.Sp(frame, "Triangle", cx, cy, 8f, 8f, MFDTheme.BRIGHT_TEXT);
                SpriteHelpers.DrawRectangleOutline(frame, gridLeft, gridTop,
                    gridAvail, gridAvail, 1f, MFDTheme.BORDER);

                // Enemy contacts
                float mPerCell = SIDE_STRIDE * (float)TerrainAPI.CellSize;
                float ppm = cell / mPerCell;
                var enemies = jet.enemyList;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    Vector3D off = e.Position - shipPos;
                    float ex = cx + (float)VD(off, jR) * ppm;
                    float ey = cy - (float)VD(off, jF) * ppm;
                    if (ex < gridLeft || ex > gridLeft + gridAvail ||
                        ey < gridTop || ey > gridTop + gridAvail) continue;

                    Color ec = jet.GetEnemyContactColor(e);
                    SpriteHelpers.Sp(frame, "Circle", ex, ey, 14f, 14f, ec);
                    SpriteHelpers.Sp(frame, "Circle", ex, ey, 10f, 10f, MFDTheme.BG);
                    SpriteHelpers.Sp(frame, "Triangle", ex, ey, 6f, 6f, ec);
                }

                double agl = TerrainAPI.AGL(shipPos);
                Color aglC = agl < 100 ? TC[0] : agl < 200 ? TC[1] : MFDTheme.STATUS_VAL;
                MFDFrame.Txt(frame, $"AGL {agl:F0}m", ox + sw / 2f,
                    gridTop + gridAvail + 6f, 0.4f, aglC, MFDTheme.AC);

                float viewKm = SIDE * SIDE_STRIDE * (float)TerrainAPI.CellSize / 1000f;
                MFDFrame.Txt(frame, $"{viewKm:F1}km", ox + sw / 2f,
                    gridTop + gridAvail + 22f, 0.3f, MFDTheme.DIM_TEXT, MFDTheme.AC);
            }
        }
    }
}
