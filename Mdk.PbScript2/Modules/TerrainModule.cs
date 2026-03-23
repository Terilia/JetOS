using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
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
            const int FD = 24, SD = 12, SS = 6, RC = 18;
            const float HP = 1.5708f;

            // 6 symmetric thresholds: warm = above you (danger), cool = below (safe)
            static readonly double[] TH = { -500, -100, -20, 0, 200, 800 };
            static readonly Color[] TC = {
                Cr(220, 40, 40),   // -500: terrain far above — bright red
                Cr(180, 60, 40),   // -100: terrain above — red
                Cr(200, 160, 40),  // -20: barely above — amber
                Cr(140, 180, 60),  // 0: at your altitude — yellow-green
                Cr(48, 130, 48),   // 200: below — green
                Cr(20, 55, 20) };  // 800: far below — dim green (terrain shape)
            static readonly Color CH = Cr(180, 50, 40, 120); // CFIT hatch

            // Fullscreen: multi-tick (4 phases) with double sprite lists
            static List<MySprite> _fA = new List<MySprite>(256), _fB = new List<MySprite>(256);
            static int _fGen = -1, _fPh = -1, _fTk;
            // Fullscreen phase state
            static float _fpmx, _fpmy, _fpcs, _fplt;
            static int _fpd, _fpsr, _fpsc, _fpst;
            static double _fpsa;
            static Vector3D _fpjF, _fpjR;

            // Sidebar: single-tick with double sprite lists
            static List<MySprite> _sA = new List<MySprite>(128), _sB = new List<MySprite>(128);
            static int _sGen = -1, _sTk;

            static double[] _cl;

            public TerrainModule(Program p, Jet j) : base(p) { jet = j; name = "Terrain Map"; }
            public override bool HasCustomScreen => true;
            public override string[] GetOptions() => new string[] { "Back to Main Menu" };
            public override void ExecuteOption(int i) { if (i == 0) SystemManager.ReturnToMainMenu(); }
            public override bool HandleNavigation(bool u)
            { if (u && zoom > 0) zoom--; else if (!u && zoom < ZS.Length - 1) zoom++; return true; }

            static void SB(List<MySprite> s, float x, float y, float w, float h, Color c, float r = 0)
            { s.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ, Position = V2(x, y),
                Size = V2(w, h), Color = c, Alignment = MFDTheme.AC, RotationOrScale = r }); }
            static void ST(List<MySprite> s, string d, float x, float y, float sc, Color c)
            { s.Add(new MySprite { Type = MFDTheme.TT, Data = d, Position = V2(x, y),
                RotationOrScale = sc, Color = c, Alignment = MFDTheme.AC, FontId = MFDTheme.FONT }); }

            // ═══ FULL-SCREEN RENDER ═══
            public override void RenderCustomScreen(MySpriteDrawFrame frame, RectangleF area)
            {
                float sw = area.Width, sh = area.Height, px = sw * 0.019f;
                float cy = MFDFrame.DrawChrome(frame, sw, sh, headerRight: "TERRAIN MAP");
                float cb = MFDFrame.ContentBottom(sh);
                float bh = sh * 0.044f;
                MFDFrame.Rect(frame, sw / 2f, cy + bh / 2f, sw, bh, MFDTheme.BC_BG);
                MFDFrame.Rect(frame, sw / 2f, cy + bh, sw, 1f, MFDTheme.BC_BORDER);
                float bs = sh * 0.00055f * 1.1f;
                MFDFrame.Txt(frame, "SYSTEM MENU", px, cy + bh * 0.15f, bs, MFDTheme.DIM_TEXT);
                MFDFrame.Txt(frame, ">", px + sw * 0.16f, cy + bh * 0.15f, bs, MFDTheme.BORDER);
                MFDFrame.Txt(frame, "TERRAIN MAP", px + sw * 0.18f, cy + bh * 0.15f, bs, MFDTheme.NORMAL_TEXT);
                cy += bh + 2f;
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
                { MFDFrame.Txt(frame, TerrainData.Loading ? "LOADING..." : "SCANNING...", sw / 2f, sh / 2f, 0.55f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC); return; }

                float mt = zy + 16f, ma = Mn(sw - px * 2, cb - 30f - mt);
                float ml = (sw - ma) / 2f, cel = ma / FD;
                MFDFrame.Txt(frame, "FWD", ml + ma / 2f, mt - 10f, 0.3f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);

                Vector3D sp = jet._cockpit.GetPosition();
                int sr, sc; TerrainData.W2G(sp, out sr, out sc);
                Vector3D jF, jR; JA(jet, out jF, out jR);
                int tk = SystemManager.currentTick;
                int gen = TerrainData.Gen;

                // Start new multi-tick cycle if stale or generation changed
                if (_fPh < 0 && (tk - _fTk >= RC || _fGen != gen))
                {
                    _fPh = 0; _fGen = gen; _fTk = tk;
                    _fpmx = ml; _fpmy = mt; _fpcs = cel; _fpd = FD;
                    _fpsr = sr; _fpsc = sc; _fpst = ZS[zoom];
                    _fpsa = TerrainData.Alt(sp); _fplt = 2f; _fpjF = jF; _fpjR = jR;
                }
                // Advance one phase per tick
                if (_fPh >= 0)
                {
                    RunPhase(_fB, ref _fPh, _fpmx, _fpmy, _fpcs, _fpd, _fpsr, _fpsc, _fpst, _fpsa, _fplt, _fpjF, _fpjR);
                    if (_fPh < 0) { var t = _fA; _fA = _fB; _fB = t; }
                }

                for (int i = 0; i < _fA.Count; i++) frame.Add(_fA[i]);
                SpriteHelpers.DrawRectangleOutline(frame, ml, mt, ma, ma, 1f, MFDTheme.BORDER);
                float cx = ml + ma / 2f, ccy = mt + ma / 2f;
                SpriteHelpers.DrawCircleOutline(frame, V2(cx, ccy), ma * 0.25f, Cr(MFDTheme.BORDER, 0.4f), 1f);
                SpriteHelpers.Sp(frame, "Triangle", cx, ccy, 10f, 10f, MFDTheme.BRIGHT_TEXT);
                float fy = mt + ma + 2f;
                double agl = TerrainData.AGL(sp);
                Color ac = agl < 50 ? TC[2] : agl < 200 ? TC[3] : MFDTheme.STATUS_VAL;
                MFDFrame.Txt(frame, $"AGL {agl:F0}m", px, fy, 0.4f, ac);
                int vm = FD * ZS[zoom] * (int)TerrainData.CellSize;
                MFDFrame.Txt(frame, vm >= 1000 ? $"{vm / 1000f:F1}km" : $"{vm}m", sw - px, fy, 0.35f, MFDTheme.DIM_TEXT, MFDTheme.AR);
            }

            // ═══ SIDEBAR HOOK — single-tick, returns cached sprites ═══
            public static List<MySprite> GetMinimap(RectangleF area, Jet jet)
            {
                if (jet._cockpit == null || !TerrainData.Ready || jet.CachedGravity.LengthSquared() < 0.01) return null;
                float ox = area.Position.X, oy = area.Position.Y, sw = area.Width, sh = area.Height;
                float gt = oy + 18f, ga = Mn(sw, sh - 40f), gl = ox + (sw - ga) / 2f, cel = ga / SD;
                Vector3D sp = jet._cockpit.GetPosition();
                int sr, sc; TerrainData.W2G(sp, out sr, out sc);
                Vector3D jF, jR; JA(jet, out jF, out jR);
                int tk = SystemManager.currentTick;
                int gen = TerrainData.Gen;

                if (tk - _sTk >= RC || _sGen != gen)
                {
                    _sGen = gen; _sTk = tk;
                    _sB.Clear();
                    FillCl(_sB, SD, sr, sc, SS, TerrainData.Alt(sp), jF, jR);
                    March(_sB, 0, TH.Length, gl, gt, cel, SD, 1.5f);
                    Hatch(_sB, gl, gt, cel, SD, 1.5f);
                    // Chrome
                    float cx = gl + ga / 2f, cy = gt + ga / 2f;
                    _sB.Add(new MySprite { Type = MFDTheme.TX, Data = "Triangle",
                        Position = V2(cx, cy), Size = V2(8f, 8f),
                        Color = MFDTheme.BRIGHT_TEXT, Alignment = MFDTheme.AC });
                    SB(_sB, gl + ga / 2f, gt, ga, 1f, MFDTheme.BORDER);
                    SB(_sB, gl + ga / 2f, gt + ga, ga, 1f, MFDTheme.BORDER);
                    SB(_sB, gl, gt + ga / 2f, 1f, ga, MFDTheme.BORDER);
                    SB(_sB, gl + ga, gt + ga / 2f, 1f, ga, MFDTheme.BORDER);
                    double agl = TerrainData.AGL(sp);
                    Color ac = agl < 50 ? TC[2] : agl < 200 ? TC[3] : MFDTheme.STATUS_VAL;
                    ST(_sB, $"AGL {agl:F0}m", ox + sw / 2f, gt + ga + 6f, 0.4f, ac);
                    float vk = SD * SS * (float)TerrainData.CellSize / 1000f;
                    ST(_sB, $"{vk:F1}km", ox + sw / 2f, gt + ga + 22f, 0.3f, MFDTheme.DIM_TEXT);
                    var t = _sA; _sA = _sB; _sB = t;
                }
                return _sA;
            }

            // ═══ SHARED CORE ═══

            static void FillCl(List<MySprite> sp, int d, int sr, int sc, int stride, double sa, Vector3D jF, Vector3D jR)
            {
                int tot = d * d;
                if (_cl == null || _cl.Length < tot) _cl = new double[tot];
                int half = d / 2;
                Vector3D cF = TerrainData.GridFwd, cR = TerrainData.GridRight;
                double sff = stride * VD(jF, cF), sfr = stride * VD(jF, cR);
                double srf = stride * VD(jR, cF), srr = stride * VD(jR, cR);
                for (int r = 0; r < d; r++)
                { int dr = half - r; for (int c = 0; c < d; c++)
                    { int dc = c - half; _cl[r * d + c] = sa - TerrainData.Surf(
                        sr + (int)(dr * sff + dc * srf), sc + (int)(dr * sfr + dc * srr)); } }
            }

            static void March(List<MySprite> sp, int from, int to, float mx, float my, float cs, int d, float lt)
            {
                int g = d - 1;
                for (int t = from; t < to; t++)
                { double th = TH[t]; Color col = TC[t];
                    for (int r = 0; r < g; r++) for (int c = 0; c < g; c++)
                    {
                        double v0 = _cl[r * d + c], v1 = _cl[r * d + c + 1], v2 = _cl[(r + 1) * d + c], v3 = _cl[(r + 1) * d + c + 1];
                        int m = 0; if (v0 >= th) m |= 1; if (v1 >= th) m |= 2; if (v2 >= th) m |= 4; if (v3 >= th) m |= 8;
                        if (m == 0 || m == 15) continue;
                        float px = mx + c * cs, py = my + r * cs;
                        Vector2 eT = V2(px + Lp(v0, v1, th) * cs, py), eR = V2(px + cs, py + Lp(v1, v3, th) * cs);
                        Vector2 eB = V2(px + Lp(v2, v3, th) * cs, py + cs), eL = V2(px, py + Lp(v0, v2, th) * cs);
                        switch (m)
                        {
                            case 1: case 14: AL(sp, eT, eL, lt, col); break;
                            case 2: case 13: AL(sp, eT, eR, lt, col); break;
                            case 3: case 12: AL(sp, eL, eR, lt, col); break;
                            case 4: case 11: AL(sp, eL, eB, lt, col); break;
                            case 5: case 10: AL(sp, eT, eB, lt, col); break;
                            case 7: case 8: AL(sp, eR, eB, lt, col); break;
                            case 6: AL(sp, eT, eL, lt, col); AL(sp, eR, eB, lt, col); break;
                            case 9: AL(sp, eT, eR, lt, col); AL(sp, eL, eB, lt, col); break;
                        }
                    }
                }
            }

            static void Hatch(List<MySprite> sp, float mx, float my, float cs, int d, float lt)
            {
                // CFIT hatching only: terrain meaningfully above you (clearance < -20)
                for (int r = 0; r < d; r++) for (int c = 0; c < d; c++)
                {
                    if (_cl[r * d + c] >= -20 || (r + c) % 3 != 0) continue;
                    float px = mx + c * cs + cs * 0.5f, py = my + r * cs + cs * 0.5f;
                    SB(sp, px, py, cs * 0.8f, lt, CH, 0.785f);
                    SB(sp, px, py, cs * 0.8f, lt, CH, -0.785f);
                }
            }

            // ═══ MULTI-TICK FULLSCREEN ENGINE (4 phases) ═══
            // Phase 0: fill clearance buffer
            // Phase 1: march thresholds 0-1
            // Phase 2: march thresholds 2-3
            // Phase 3: march thresholds 4-5 + hatch → done
            static void RunPhase(List<MySprite> sp, ref int phase,
                float mx, float my, float cs, int d, int sr, int sc, int stride,
                double sa, float lt, Vector3D jF, Vector3D jR)
            {
                switch (phase)
                {
                    case 0: sp.Clear(); FillCl(sp, d, sr, sc, stride, sa, jF, jR); phase = 1; break;
                    case 1: March(sp, 0, 2, mx, my, cs, d, lt); phase = 2; break;
                    case 2: March(sp, 2, 4, mx, my, cs, d, lt); phase = 3; break;
                    case 3: March(sp, 4, TH.Length, mx, my, cs, d, lt); Hatch(sp, mx, my, cs, d, lt); phase = -1; break;
                }
            }

            static float Lp(double a, double b, double t)
            { double d = b - a; if (d > -0.01 && d < 0.01) return 0.5f; float v = (float)((t - a) / d); return v < 0f ? 0f : v > 1f ? 1f : v; }

            static void AL(List<MySprite> sp, Vector2 a, Vector2 b, float t, Color c)
            { Vector2 d = b - a; float l = d.Length(); if (l < 0.5f) return;
                sp.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ, Position = (a + b) * 0.5f,
                    Size = V2(t, l), Color = c, Alignment = MFDTheme.AC, RotationOrScale = (float)At2(d.Y, d.X) - HP }); }

            static void JA(Jet j, out Vector3D jF, out Vector3D jR)
            { Vector3D u = VN(-j.CachedGravity); jF = j._cockpit.WorldMatrix.Forward;
                jF = jF - VD(jF, u) * u;
                if (jF.LengthSquared() < 0.01) { Vector3D r = j._cockpit.WorldMatrix.Right;
                    r = r - VD(r, u) * u; jF = r.LengthSquared() > 0.01 ? VX(u, VN(r)) : j._cockpit.WorldMatrix.Forward; }
                jF = VN(jF); jR = VX(jF, u);
                jR = jR.LengthSquared() > 0.01 ? VN(jR) : j._cockpit.WorldMatrix.Right; }
        }
    }
}
