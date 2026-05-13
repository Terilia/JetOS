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
            static readonly int[] ZS = { 1, 2, 5, 10, 15, 20, 35, 50, 67 };
            int zoom = 2;
            const int FD = 16, SD = 8, SS = 6;
            const float HP = 1.5708f;
            const float FRIENDLY_CONTACT_SCALE = 1.18f;

            // 4 thresholds: warm = above you (danger), cool = below (safe)
            static readonly double[] TH = { -500, 0, 200, 800 };
            static readonly Color[] TC = {
                Cr(220, 40, 40),   // -500: terrain far above — bright red
                Cr(180, 180, 50),  // 0: at your altitude — yellow
                Cr(48, 130, 48),   // 200: below — green
                Cr(20, 55, 20) };  // 800: far below — dim green (terrain shape)
            static readonly short[] _ths = { (short)TH[0], (short)TH[1], (short)TH[2], (short)TH[3] };

            static short[] _cl, _el;
            static short _clMin, _clMax;
            static readonly int[] _hmI = new int[5];
            static readonly short[] _hmV = new short[5];

            public TerrainModule(Program p, Jet j) : base(p) { jet = j; name = "Terrain"; }
            public override string[] GetOptions() => new string[] { "Back" };
            public override void ExecuteOption(int i) { if (i == 0) SystemManager.ReturnToMainMenu(); }
            public override bool HandleNavigation(bool u)
            { if (u && zoom > 0) zoom--; else if (!u && zoom < ZS.Length - 1) zoom++; return true; }
            public override MfdPage GetPage() => new TerrainMfdPage(this);

            // ═══ FULL-SCREEN MAP RENDER (called by TerrainMfdPage.RenderContent) ═══
            // Receives the post-chrome content rect. The screen-coord origin is still 0,0
            // because sprites position absolutely on the surface — we use SystemManager's
            // surface size for total bounds, but only draw inside `area`.
            internal void DrawMap(MySpriteDrawFrame frame, RectangleF area, float surfaceW, float surfaceH)
            {
                float sw = surfaceW, sh = surfaceH, px = sw * 0.019f;
                float cy = area.Position.Y;
                float cb = area.Position.Y + area.Height;

                if (jet.CachedGravity.LengthSquared() < 0.01)
                { MFDFrame.Txt(frame, "NO PLNT", sw / 2f, sh / 2f, 0.7f, MFDTheme.DIM_TEXT, MFDTheme.AC); return; }
                if (!TerrainData.Available)
                { MFDFrame.Txt(frame, "NO TER", sw / 2f, sh / 2f, 0.55f, MFDTheme.DIM_TEXT, MFDTheme.AC); return; }
                float zy = cy + 2f;
                MFDFrame.Txt(frame, "1/2 ZOOM", px, zy, 0.35f, MFDTheme.DIM_TEXT);
                MFDFrame.Txt(frame, MapLbl(ZS[zoom]), sw - px, zy, 0.4f, MFDTheme.ACCENT, MFDTheme.AR);
                float dy = zy + 6f, dl = sw * 0.35f, ds = (sw * 0.30f) / (ZS.Length - 1);
                for (int i = 0; i < ZS.Length; i++)
                { float dx = dl + i * ds; bool s = i == zoom; MFDFrame.Rect(frame, dx, dy, s ? 6f : 3f, s ? 6f : 3f, s ? MFDTheme.ACCENT : MFDTheme.BORDER); }
                if (!TerrainData.Ready)
                {
                    if (TerrainData.Loading)
                    {
                        int pct = (int)(TerrainData.DownloadProgress * 100);
                        string bar = "".PadLeft(pct / 5, '#').PadRight(20, '.');
                        MFDFrame.Txt(frame, "TER DL", sw / 2f, sh / 2f - 16f, 0.45f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                        MFDFrame.Txt(frame, $"[{bar}] {pct}%", sw / 2f, sh / 2f + 4f, 0.4f, MFDTheme.ACCENT, MFDTheme.AC);
                    }
                    else
                        MFDFrame.Txt(frame, "NO DATA", sw / 2f, sh / 2f, 0.55f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                    return;
                }

                float profH = 30f;
                float mt = zy + 14f, ma = Mn(sw - px * 2, cb - mt - profH - 6f);
                float ml = (sw - ma) / 2f;
                MFDFrame.Txt(frame, "FWD", ml + ma / 2f, mt - 10f, 0.3f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);

                Vector3D sp = jet.CockpitPosition;
                int sr, sc; double fr, fc;
                TerrainData.W2GF(sp, out sr, out sc, out fr, out fc);
                Vector3D jF, jR; JA(jet, out jF, out jR);
                double sa = TerrainData.Alt(sp);

                float cel = ma / (FD - 1);
                FillCl(FD, sr, sc, fr, fc, ZS[zoom], sa, jF, jR, 0);
                DrawContours(frame, ml, mt, cel, FD, 2f, 0, 4);

                SpriteHelpers.DrawRectangleOutline(frame, ml, mt, ma, ma, 1f, MFDTheme.BORDER);
                float cx = ml + ma / 2f, ccy = mt + ma / 2f;
                float ppm = ma / ((FD - 1) * ZS[zoom] * (float)TerrainData.CellSize);
                DrawRange(frame, cx, ccy, ma);
                DrawHeightMarkers(frame, ml, mt, cel, FD);
                DrawFlightPath(frame, cx, ccy, ma, ppm, jet.CockpitVelocity, jF, jR);
                DrawContacts(frame, cx, ccy, ma, ppm, sp, jF, jR, jet);
                DrawMissiles(frame, cx, ccy, ma, ppm, sp, jF, jR);
                DrawFriendlyJets(frame, cx, ccy, ma, ppm, sp, jF, jR);
                DrawProfile(frame, ml, mt + ma + 4f, ma, profH - 7f, sp, jet.CockpitVelocity, jF, VN(-jet.CachedGravity), ZS[zoom]);
                SpriteHelpers.DrawCircleOutline(frame, V2(cx, ccy), ma * 0.25f, Cr(MFDTheme.BORDER, 0.4f), 1f);
                SpriteHelpers.Sp(frame, TEXTURE_TRIANGLE, cx, ccy, 10f, 10f, MFDTheme.BRIGHT_TEXT);
                float fy = mt + ma + profH - 2f;
                double agl = TerrainData.AGL(sp);
                Color ac = agl < 50 ? TC[2] : agl < 200 ? TC[3] : MFDTheme.STATUS_VAL;
                MFDFrame.Txt(frame, $"AGL {agl:F0}m", px, fy, 0.4f, ac);
                MFDFrame.Txt(frame, MapLbl(ZS[zoom]), sw - px, fy, 0.35f, MFDTheme.DIM_TEXT, MFDTheme.AR);
            }

            // Page wrapper — supplies chrome metadata, delegates content draw to DrawMap.
            class TerrainMfdPage : MfdPage
            {
                readonly TerrainModule _mod;
                public TerrainMfdPage(TerrainModule m) { _mod = m; }
                public override string HeaderRight => "TER";
                public override bool ShowFooterNav => true;
                public override bool ShowBreadcrumb => true;
                public override string BreadcrumbPath => "TER";
                public override void RenderContent(MySpriteDrawFrame frame, RectangleF area, Vector2 surfaceSize)
                {
                    _mod.DrawMap(frame, area, surfaceSize.X, surfaceSize.Y);
                }
            }

            // ═══ SIDEBAR — direct render every call ═══
            public static void RenderMinimap(MySpriteDrawFrame frame, RectangleF area, Jet jet)
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
                if (_el == null || _el.Length < tot) _el = new short[tot];
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
                        double sf = TerrainData.Surf(sr + ir, sc + ic);
                        double v = sa - sf, ev = sf - TerrainData.MeanR;
                        short sv = (short)(v > 32000 ? 32000 : v < -32000 ? -32000 : v);
                        _cl[r * d + c] = sv;
                        _el[r * d + c] = (short)(ev > 32000 ? 32000 : ev < -32000 ? -32000 : ev);
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
            static void DrawContours(MySpriteDrawFrame frame, float mx, float my, float cs, int d, float lt,
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

            static void AF(MySpriteDrawFrame f, Vector2 a, Vector2 b, float t, Color c)
            { Vector2 d = b - a; float ls = d.X * d.X + d.Y * d.Y;
                if (ls < 0.25f) return; float l = (float)Math.Sqrt(ls);
                Vector2 mid = (a + b) * 0.5f;
                Sq(mid.X, mid.Y, t, l, c, (float)At2(d.Y, d.X) - HP); }

            static void DrawRange(MySpriteDrawFrame f, float cx, float cy, float ma)
            {
                Color c = Cr(MFDTheme.BORDER, 0.32f);
                SpriteHelpers.Sp(f, TEX_RANGE_RING, cx, cy, ma * 0.532f, ma * 0.532f, c);
                SpriteHelpers.Sp(f, TEX_RANGE_RING, cx, cy, ma * 1.065f, ma * 1.065f, c);
                AF(f, V2(cx - ma / 2f, cy), V2(cx + ma / 2f, cy), 1f, Cr(MFDTheme.BORDER, 0.28f));
                AF(f, V2(cx, cy - ma / 2f), V2(cx, cy + ma / 2f), 1f, Cr(MFDTheme.BORDER, 0.28f));
            }

            static Vector2 ClipMap(float cx, float cy, float dx, float dy, float h)
            {
                float k = 1f, ax = Ab(dx), ay = Ab(dy);
                if (ax > h) k = Mn(k, h / ax);
                if (ay > h) k = Mn(k, h / ay);
                return V2(cx + dx * k, cy + dy * k);
            }

            static string MapLbl(int stride)
            {
                double m = (FD - 1) * stride * TerrainData.CellSize;
                if (m >= 100000) return ((int)Rd(m / 10000) * 10).ToString() + "km";
                return m >= 10000 ? ((int)Rd(m / 1000)).ToString() + "km" :
                    m >= 1000 ? (m / 1000.0).ToString("F1") + "km" : ((int)m).ToString() + "m";
            }

            static Color ClrC(double cl)
            {
                return cl < 0 ? TC[0] : cl < 80 ? TC[1] : cl < 250 ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
            }

            static string HtT(double h)
            {
                return Ab(h) >= 1000 ? (h / 1000.0).ToString("F1") + "k" : h.ToString("F0");
            }

            static void DrawHeightMarkers(MySpriteDrawFrame f, float mx, float my, float cs, int d)
            {
                for (int i = 0; i < _hmI.Length; i++) { _hmI[i] = -1; _hmV[i] = short.MinValue; }
                int half = d / 2;
                for (int r = 1; r < d - 1; r++)
                    for (int c = 1; c < d - 1; c++)
                    {
                        if (Ab(r - half) < 2 && Ab(c - half) < 2) continue;
                        int idx = r * d + c;
                        short ev = _el[idx];
                        if (ev < _el[idx - 1] || ev < _el[idx + 1] || ev < _el[idx - d] || ev < _el[idx + d]) continue;
                        bool near = false;
                        for (int n = 0; n < _hmI.Length; n++)
                            if (_hmI[n] >= 0)
                            { int rr = _hmI[n] / d, cc = _hmI[n] - rr * d, dr = rr - r, dc = cc - c;
                                if (dr * dr + dc * dc < 9) near = true; }
                        if (near) continue;
                        for (int k = 0; k < _hmI.Length; k++)
                            if (ev > _hmV[k])
                            {
                                for (int m = _hmI.Length - 1; m > k; m--) { _hmI[m] = _hmI[m - 1]; _hmV[m] = _hmV[m - 1]; }
                                _hmI[k] = idx; _hmV[k] = ev; k = _hmI.Length;
                            }
                    }

                for (int i = 0; i < _hmI.Length; i++)
                {
                    int idx = _hmI[i];
                    if (idx < 0) continue;
                    int r = idx / d, c = idx - r * d;
                    float px = mx + c * cs, py = my + r * cs;
                    Color col = ClrC(_cl[idx]);
                    AF(f, V2(px - 4f, py), V2(px + 4f, py), 1f, Cr(col, 0.75f));
                    MFDFrame.Txt(f, HtT(_hmV[i]), px, py + 2f, 0.28f, col, MFDTheme.AC);
                }
            }

            static void DrawFlightPath(MySpriteDrawFrame f, float cx, float cy, float ma, float ppm, Vector3D v, Vector3D jf, Vector3D jr)
            {
                float fd = (float)VD(v, jf), rd = (float)VD(v, jr);
                if (fd * fd + rd * rd < 4f) return;
                float dx = rd * ppm, dy = -fd * ppm;
                float l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l < 0.1f) return;
                float tl = Cl(l * 4f, 8f, 24f);
                Vector2 p = V2(cx, cy);
                Vector2 q = V2(cx - dx / l * tl, cy - dy / l * tl);
                AF(f, q, p, 1.2f, Cr(MFDTheme.ACCENT, 0.55f));
            }

            static void DrawContacts(MySpriteDrawFrame f, float cx, float cy, float ma, float ppm, Vector3D sp, Vector3D jf, Vector3D jr, Jet j)
            {
                var sel = j.GetSelectedEnemy();
                for (int i = 0; i < j.enemyList.Count; i++)
                {
                    var e = j.enemyList[i];
                    bool s = sel.HasValue && e.Matches(sel.Value);
                    Color c = e.IsStale ? MFDTheme.DIM_TEXT : s ? MFDTheme.BRIGHT_TEXT : j.GetEnemyContactColor(e);
                    DrawMapContact(f, cx, cy, ma, ppm, sp, jf, jr, e.Position, SE(e.Name) ? "TGT" : e.Name,
                        s ? TEX_C_HOSTILE : TEX_C_UNKNOWN, c, s, s);
                }
            }

            static void DrawMissiles(MySpriteDrawFrame f, float cx, float cy, float ma, float ppm, Vector3D sp, Vector3D jf, Vector3D jr)
            {
                var ms = MissileBayHelper.GetActiveMissileStatus();
                float h = ma / 2f;
                for (int i = 0; i < ms.Count; i++)
                {
                    var m = ms[i];
                    Vector3D to = m.Position - sp;
                    float dx = (float)VD(to, jr) * ppm, dy = -(float)VD(to, jf) * ppm;
                    Vector2 p = ClipMap(cx, cy, dx, dy, h - 4f);
                    float vx = (float)VD(m.Velocity, jr) * ppm;
                    float vy = -(float)VD(m.Velocity, jf) * ppm;
                    float vl = (float)Math.Sqrt(vx * vx + vy * vy);
                    if (vl > 0.1f)
                    {
                        float tl = Cl(vl * 4f, 6f, 18f);
                        Vector2 q = V2(p.X - vx / vl * tl, p.Y - vy / vl * tl);
                        AF(f, q, p, 1.1f, Cr(MFDTheme.DANGER, 0.62f));
                    }
                    float a = vl > 0.1f ? (float)At2(vy, vx) + HP : 0f;
                    Sq(p.X + 1f, p.Y + 1f, 7f, 3f, Cr(0, 0, 0, 180), a);
                    Sq(p.X, p.Y, 8f, 4f, Cr(MFDTheme.BRIGHT_TEXT, 0.42f), a);
                    Sq(p.X, p.Y, 6f, 2f, MFDTheme.DANGER, a);
                    MFDFrame.Txt(f, m.Bay.ToString(), p.X + 6f, p.Y - 6f, 0.32f,
                        m.ActiveTrackingUnlocked ? MFDTheme.ACCENT :
                        m.Acquired ? MFDTheme.BRIGHT_TEXT : MFDTheme.WARN);
                    if (m.ActiveTrackingUnlocked)
                        MFDFrame.Txt(f, "AI", p.X + 6f, p.Y + 3f, 0.26f, MFDTheme.ACCENT);
                }
            }

            static void DrawFriendlyJets(MySpriteDrawFrame f, float cx, float cy, float ma, float ppm, Vector3D sp, Vector3D jf, Vector3D jr)
            {
                var friends = FriendlyJetTelemetry.GetActiveFriends();
                Color blue = Cr(70, 150, 255);
                for (int i = 0; i < friends.Count; i++)
                {
                    var friend = friends[i];
                    DrawMapContact(f, cx, cy, ma, ppm, sp, jf, jr, friend.Position, FriendlyLabel(friend.Id),
                        TEX_C_FRIENDLY, blue, true, false, FRIENDLY_CONTACT_SCALE);
                }
            }

            static void DrawMapContact(MySpriteDrawFrame f, float cx, float cy, float ma, float ppm, Vector3D sp, Vector3D jf, Vector3D jr,
                Vector3D pos, string label, string sprite, Color c, bool showInfo, bool selected, float sizeScale = 1f)
            {
                float h = ma / 2f;
                Vector3D to = pos - sp;
                float dx = (float)VD(to, jr) * ppm, dy = -(float)VD(to, jf) * ppm;
                bool off = Ab(dx) > h || Ab(dy) > h;
                Vector2 p = ClipMap(cx, cy, dx, dy, h - 3f);
                float z = (selected ? 15f : off ? 10f : 11f) * sizeScale;
                SpriteHelpers.Sp(f, sprite, p.X, p.Y, z, z, c);
                if (!showInfo) return;
                string n = SE(label) ? "TGT" : label;
                if (n.Length > 9) n = n.Substring(0, 9);
                bool l = p.X < cx;
                float tx = p.X + (l ? 8f : -8f);
                var a = l ? MFDTheme.AL : MFDTheme.AR;
                MFDFrame.Txt(f, n, tx, p.Y + 7f, 0.3f, c, a);
                MFDFrame.Txt(f, SpriteHelpers.FormatRange(VDi(sp, pos)) + " " + AD(TerrainData.Alt(pos) - TerrainData.Alt(sp)), tx, p.Y + 18f, 0.25f, c, a);
            }

            static string FriendlyLabel(long id)
            {
                string s = id.ToString();
                return "FR " + (s.Length > 4 ? s.Substring(s.Length - 4) : s);
            }

            static string AD(double v)
            {
                string s = v >= 0 ? "+" : "";
                return s + (Ab(v) >= 1000 ? (v / 1000.0).ToString("F1") + "k" : v.ToString("F0"));
            }

            static void DrawProfile(MySpriteDrawFrame f, float x, float y, float w, float h, Vector3D sp, Vector3D v, Vector3D jf, Vector3D up, int stride)
            {
                MFDFrame.Rect(f, x + w / 2f, y + h / 2f, w, h, Cr(2, 5, 2, 150));
                SpriteHelpers.DrawRectangleOutline(f, x, y, w, h, 1f, Cr(MFDTheme.BORDER, 0.55f));
                Vector3D d = v - VD(v, up) * up;
                if (d.LengthSquared() < 4) d = jf; else d = VN(d);
                Vector2 prev = V2(0, 0);
                bool has = false;
                const int N = 14;
                double step = stride * TerrainData.CellSize;
                for (int i = 0; i < N; i++)
                {
                    Vector3D p = sp + d * step * (i + 1);
                    int r, c; TerrainData.W2G(p, out r, out c);
                    double agl = TerrainData.Alt(p) - TerrainData.Surf(r, c);
                    float px = x + w * i / (N - 1);
                    float py = y + h - Cl((float)(agl / 900.0), 0f, 1f) * h;
                    Color col = agl < 0 ? TC[0] : agl < 80 ? TC[1] : agl < 250 ? MFDTheme.WARN : MFDTheme.STATUS_VAL;
                    if (has) AF(f, prev, V2(px, py), 1.3f, col);
                    prev = V2(px, py); has = true;
                }
                MFDFrame.Txt(f, "FWD AGL", x + 2f, y - 1f, 0.25f, MFDTheme.DIM_TEXT);
            }

            static void JA(Jet j, out Vector3D jF, out Vector3D jR)
            { Vector3D u = VN(-j.CachedGravity); jF = j.CockpitMatrix.Forward;
                jF = jF - VD(jF, u) * u;
                if (jF.LengthSquared() < 0.01) { Vector3D r = j.CockpitMatrix.Right;
                    r = r - VD(r, u) * u; jF = r.LengthSquared() > 0.01 ? VX(u, VN(r)) : j.CockpitMatrix.Forward; }
                jF = VN(jF); jR = VX(jF, u);
                jR = jR.LengthSquared() > 0.01 ? VN(jR) : j.CockpitMatrix.Right; }
        }
    }
}
