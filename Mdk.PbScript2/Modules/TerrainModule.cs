using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using MSDF = VRage.Game.GUI.TextPanel.MySpriteDrawFrame;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class TerrainModule : ProgramModule
        {
            Jet jet;
            static readonly int[] ZS = { 1, 2, 5, 10, 15, 20 };
            static readonly string[] ZL = { "1km", "2km", "5km", "10km", "15km", "20km" };
            int zoom = 2;
            const int FD = 16, SD = 8, SS = 6;
            const float HP = 1.5708f;

            // 4 thresholds: warm = above you (danger), cool = below (safe)
            static readonly double[] TH = { -500, 0, 200, 800 };
            static readonly Color[] TC = {
                Cr(220, 40, 40),   // -500: terrain far above — bright red
                Cr(180, 180, 50),  // 0: at your altitude — yellow
                Cr(48, 130, 48),   // 200: below — green
                Cr(20, 55, 20) };  // 800: far below — dim green (terrain shape)
            static readonly Color CH = Cr(180, 50, 40, 120); // CFIT hatch
            static readonly short[] _ths = { (short)TH[0], (short)TH[1], (short)TH[2], (short)TH[3] };

            static short[] _cl;
            static short _clMin, _clMax;

            public TerrainModule(Program p, Jet j) : base(p) { jet = j; name = "Terrain Map"; }
            public override string[] GetOptions() => new string[] { "Back to Main Menu" };
            public override void ExecuteOption(int i) { if (i == 0) SystemManager.ReturnToMainMenu(); }
            public override bool HandleNavigation(bool u)
            { if (u && zoom > 0) zoom--; else if (!u && zoom < ZS.Length - 1) zoom++; return true; }
            public override MfdPage GetPage() => new TerrainMfdPage(this);

            // ═══ FULL-SCREEN MAP RENDER (called by TerrainMfdPage.RenderContent) ═══
            // Receives the post-chrome content rect. The screen-coord origin is still 0,0
            // because sprites position absolutely on the surface — we use SystemManager's
            // surface size for total bounds, but only draw inside `area`.
            internal void DrawMap(MSDF frame, RectangleF area, float surfaceW, float surfaceH)
            {
                float sw = surfaceW, sh = surfaceH, px = sw * 0.019f;
                float cy = area.Position.Y;
                float cb = area.Position.Y + area.Height;

                if (jet.CachedGravity.LengthSquared() < 0.01)
                { MFDFrame.Txt(frame, "NO PLANET", sw / 2f, sh / 2f, 0.7f, MFDTheme.DIM_TEXT, MFDTheme.AC); return; }
                if (!TerrainData.Available)
                { MFDFrame.Txt(frame, "NO TERRAIN", sw / 2f, sh / 2f, 0.55f, MFDTheme.DIM_TEXT, MFDTheme.AC); return; }
                float zy = cy + 2f;
                MFDFrame.Txt(frame, "1\u25B2 ZOOM 2\u25BC", px, zy, 0.35f, MFDTheme.DIM_TEXT);
                MFDFrame.Txt(frame, ZL[zoom], sw - px, zy, 0.4f, MFDTheme.ACCENT, MFDTheme.AR);
                float dy = zy + 6f, dl = sw * 0.35f, ds = (sw * 0.30f) / (ZS.Length - 1);
                for (int i = 0; i < ZS.Length; i++)
                { float dx = dl + i * ds; bool s = i == zoom; MFDFrame.Rect(frame, dx, dy, s ? 6f : 3f, s ? 6f : 3f, s ? MFDTheme.ACCENT : MFDTheme.BORDER); }
                if (!TerrainData.Ready)
                {
                    if (TerrainData.Loading)
                    {
                        int pct = (int)(TerrainData.DownloadProgress * 100);
                        string bar = "".PadLeft(pct / 5, '#').PadRight(20, '.');
                        MFDFrame.Txt(frame, "TERRAIN DOWNLOAD", sw / 2f, sh / 2f - 16f, 0.45f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                        MFDFrame.Txt(frame, $"[{bar}] {pct}%", sw / 2f, sh / 2f + 4f, 0.4f, MFDTheme.ACCENT, MFDTheme.AC);
                    }
                    else
                        MFDFrame.Txt(frame, "NO DATA", sw / 2f, sh / 2f, 0.55f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                    return;
                }

                float mt = zy + 14f, ma = Mn(sw - px * 2, cb - 16f - mt);
                float ml = (sw - ma) / 2f;
                MFDFrame.Txt(frame, "FWD", ml + ma / 2f, mt - 10f, 0.3f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);

                Vector3D sp = GP(jet._cockpit);
                int sr, sc; double fr, fc;
                TerrainData.W2GF(sp, out sr, out sc, out fr, out fc);
                Vector3D jF, jR; JA(jet, out jF, out jR);
                double sa = TerrainData.Alt(sp);

                float cel = ma / (FD - 1);
                FillCl(FD, sr, sc, fr, fc, ZS[zoom], sa, jF, jR, 0);
                DrawContours(frame, ml, mt, cel, FD, 2f, 0, 4);

                SpriteHelpers.DrawRectangleOutline(frame, ml, mt, ma, ma, 1f, MFDTheme.BORDER);
                float cx = ml + ma / 2f, ccy = mt + ma / 2f;
                SpriteHelpers.DrawCircleOutline(frame, V2(cx, ccy), ma * 0.25f, Cr(MFDTheme.BORDER, 0.4f), 1f);
                SpriteHelpers.Sp(frame, TEXTURE_TRIANGLE, cx, ccy, 10f, 10f, MFDTheme.BRIGHT_TEXT);
                float fy = mt + ma + 2f;
                double agl = TerrainData.AGL(sp);
                Color ac = agl < 50 ? TC[2] : agl < 200 ? TC[3] : MFDTheme.STATUS_VAL;
                MFDFrame.Txt(frame, $"AGL {agl:F0}m", px, fy, 0.4f, ac);
                int vm = FD * ZS[zoom] * (int)TerrainData.CellSize;
                MFDFrame.Txt(frame, vm >= 1000 ? $"{vm / 1000f:F1}km" : $"{vm}m", sw - px, fy, 0.35f, MFDTheme.DIM_TEXT, MFDTheme.AR);
            }

            // Page wrapper — supplies chrome metadata, delegates content draw to DrawMap.
            class TerrainMfdPage : MfdPage
            {
                readonly TerrainModule _mod;
                public TerrainMfdPage(TerrainModule m) { _mod = m; }
                public override string HeaderRight => "TERRAIN MAP";
                public override bool ShowFooterNav => true;
                public override bool ShowBreadcrumb => true;
                public override string BreadcrumbPath => "TERRAIN MAP";
                public override void RenderContent(MSDF frame, RectangleF area, Vector2 surfaceSize)
                {
                    _mod.DrawMap(frame, area, surfaceSize.X, surfaceSize.Y);
                }
            }

            // ═══ SIDEBAR — direct render every call ═══
            public static void RenderMinimap(MSDF frame, RectangleF area, Jet jet)
            {
                if (jet._cockpit == null || !TerrainData.Ready || jet.CachedGravity.LengthSquared() < 0.01) return;
                float ox = area.Position.X, oy = area.Position.Y, sw = area.Width, sh = area.Height;
                float gt = oy + 14f, ga = Mn(sw, sh - 30f), gl = ox + (sw - ga) / 2f;
                Vector3D sp = GP(jet._cockpit);
                int sr, sc; double fr, fc;
                TerrainData.W2GF(sp, out sr, out sc, out fr, out fc);
                Vector3D jF, jR; JA(jet, out jF, out jR);
                double sa = TerrainData.Alt(sp);

                float cel = ga / (SD - 1);
                FillCl(SD, sr, sc, fr, fc, SS, sa, jF, jR, 0);
                DrawContours(frame, gl, gt, cel, SD, 1.5f, 0, 4);

                float cx = gl + ga / 2f, cy = gt + ga / 2f;
                SpriteHelpers.Sp(frame, TEXTURE_TRIANGLE, cx, cy, 8f, 8f, MFDTheme.BRIGHT_TEXT);
                SpriteHelpers.DrawRectangleOutline(frame, gl, gt, ga, ga, 1f, MFDTheme.BORDER);
                double agl = TerrainData.AGL(sp);
                Color ac = agl < 50 ? TC[2] : agl < 200 ? TC[3] : MFDTheme.STATUS_VAL;
                MFDFrame.Txt(frame, $"AGL {agl:F0}m", ox + sw / 2f, gt + ga + 6f, 0.4f, ac, MFDTheme.AC);
                float vk = SD * SS * (float)TerrainData.CellSize / 1000f;
                MFDFrame.Txt(frame, $"{vk:F1}km", ox + sw / 2f, gt + ga + 22f, 0.3f, MFDTheme.DIM_TEXT, MFDTheme.AC);
            }

            // ═══ SHARED CORE ═══

            static void FillCl(int d, int sr, int sc, double fracR, double fracC,
                int stride, double sa, Vector3D jF, Vector3D jR, int fwdBias)
            {
                int tot = d * d;
                if (_cl == null || _cl.Length < tot) _cl = new short[tot];
                int half = d / 2;
                int shipR = half + fwdBias;
                Vector3D cF = TerrainData.GridFwd, cR = TerrainData.GridRight;
                double sff = stride * VD(jF, cF), sfr = stride * VD(jF, cR);
                double srf = stride * VD(jR, cF), srr = stride * VD(jR, cR);
                short mn = short.MaxValue, mx = short.MinValue;
                for (int r = 0; r < d; r++)
                {   int dr = shipR - r;
                    double rf = dr * sff - half * srf + fracR, rr = dr * sfr - half * srr + fracC;
                    for (int c = 0; c < d; c++)
                    {   // Floor-to-int (not truncate) for correct negative rounding
                        int ir = (int)rf; if (rf < ir) ir--;
                        int ic = (int)rr; if (rr < ic) ic--;
                        double v = sa - TerrainData.Surf(sr + ir, sc + ic);
                        short sv = (short)(v > 32000 ? 32000 : v < -32000 ? -32000 : v);
                        _cl[r * d + c] = sv;
                        if (sv < mn) mn = sv; if (sv > mx) mx = sv;
                        rf += srf; rr += srr;
                    }
                }
                _clMin = mn; _clMax = mx;
            }

            const int MAX_SPRITES = 350;

            // Threshold-major contour renderer. Each threshold gets full front-to-back
            // grid coverage before the next, so danger contours (terrain above/at altitude)
            // always complete. Shape contours degrade gracefully under sprite budget.
            static void DrawContours(MSDF frame, float mx, float my, float cs, int d, float lt,
                int tStart, int tCount)
            {
                int g = d - 1;
                int tEnd = tStart + tCount;
                if (tEnd > _ths.Length) tEnd = _ths.Length;

                // Pre-filter thresholds entirely outside the grid's clearance range
                while (tStart < tEnd && _clMin >= _ths[tStart]) tStart++;
                while (tEnd > tStart && _clMax < _ths[tEnd - 1]) tEnd--;
                if (tStart >= tEnd) return;

                int spriteCount = 0;
                for (int t = tStart; t < tEnd; t++)
                {
                    short th = _ths[t];
                    if (_clMin >= th || _clMax < th) continue;
                    Color col = TC[t];

                    for (int r = 0; r < g; r++)
                    {
                        int ri = r * d, ri1 = (r + 1) * d;
                        for (int c = 0; c < g; c++)
                        {
                            short v0 = _cl[ri + c], v1 = _cl[ri + c + 1];
                            short v2 = _cl[ri1 + c], v3 = _cl[ri1 + c + 1];

                            short mn = v0, mx2 = v0;
                            if (v1 < mn) mn = v1; if (v1 > mx2) mx2 = v1;
                            if (v2 < mn) mn = v2; if (v2 > mx2) mx2 = v2;
                            if (v3 < mn) mn = v3; if (v3 > mx2) mx2 = v3;
                            if (mn >= th || mx2 < th) continue;

                            int m = 0;
                            if (v0 >= th) m |= 1; if (v1 >= th) m |= 2;
                            if (v2 >= th) m |= 4; if (v3 >= th) m |= 8;
                            if (m == 0 || m == 15) continue;

                            float px = mx + c * cs, py = my + r * cs;
                            switch (m)
                            {
                                case 1: case 14:
                                    AF(frame, V2(px + Lp(v0, v1, th) * cs, py),
                                       V2(px, py + Lp(v0, v2, th) * cs), lt, col); break;
                                case 2: case 13:
                                    AF(frame, V2(px + Lp(v0, v1, th) * cs, py),
                                       V2(px + cs, py + Lp(v1, v3, th) * cs), lt, col); break;
                                case 3: case 12:
                                    AF(frame, V2(px, py + Lp(v0, v2, th) * cs),
                                       V2(px + cs, py + Lp(v1, v3, th) * cs), lt, col); break;
                                case 4: case 11:
                                    AF(frame, V2(px, py + Lp(v0, v2, th) * cs),
                                       V2(px + Lp(v2, v3, th) * cs, py + cs), lt, col); break;
                                case 5: case 10:
                                    AF(frame, V2(px + Lp(v0, v1, th) * cs, py),
                                       V2(px + Lp(v2, v3, th) * cs, py + cs), lt, col); break;
                                case 7: case 8:
                                    AF(frame, V2(px + cs, py + Lp(v1, v3, th) * cs),
                                       V2(px + Lp(v2, v3, th) * cs, py + cs), lt, col); break;
                                case 6:
                                    AF(frame, V2(px + Lp(v0, v1, th) * cs, py),
                                       V2(px, py + Lp(v0, v2, th) * cs), lt, col);
                                    AF(frame, V2(px + cs, py + Lp(v1, v3, th) * cs),
                                       V2(px + Lp(v2, v3, th) * cs, py + cs), lt, col);
                                    spriteCount++; break;
                                case 9:
                                    AF(frame, V2(px + Lp(v0, v1, th) * cs, py),
                                       V2(px + cs, py + Lp(v1, v3, th) * cs), lt, col);
                                    AF(frame, V2(px, py + Lp(v0, v2, th) * cs),
                                       V2(px + Lp(v2, v3, th) * cs, py + cs), lt, col);
                                    spriteCount++; break;
                            }
                            if (++spriteCount >= MAX_SPRITES) return;
                        }
                    }
                }
            }

            static float Lp(short a, short b, short t)
            { int d = b - a; if (d > -1 && d < 1) return 0.5f; float v = (float)(t - a) / d; return v < 0f ? 0f : v > 1f ? 1f : v; }

            static void AF(MSDF f, Vector2 a, Vector2 b, float t, Color c)
            { Vector2 d = b - a; float ls = d.X * d.X + d.Y * d.Y;
                if (ls < 0.25f) return; float l = (float)Math.Sqrt(ls);
                Vector2 mid = (a + b) * 0.5f;
                Sq(mid.X, mid.Y, t, l, c, (float)At2(d.Y, d.X) - HP); }

            static void JA(Jet j, out Vector3D jF, out Vector3D jR)
            { Vector3D u = VN(-j.CachedGravity); jF = WF(j._cockpit);
                jF = jF - VD(jF, u) * u;
                if (jF.LengthSquared() < 0.01) { Vector3D r = WR(j._cockpit);
                    r = r - VD(r, u) * u; jF = r.LengthSquared() > 0.01 ? VX(u, VN(r)) : WF(j._cockpit); }
                jF = VN(jF); jR = VX(jF, u);
                jR = jR.LengthSquared() > 0.01 ? VN(jR) : WR(j._cockpit); }
        }
    }
}
