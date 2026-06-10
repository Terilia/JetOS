using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Top-down tactical map on a stable gravity-relative horizontal frame. Pan/zoom is driven
        // by the command seat (mouse + W/S); A/D scans a terrain elevation reference. Shared by the
        // dedicated HQ MAP screen and the HQ MFD fallback. SE clips sprites to the surface bounds,
        // so the map paints the whole surface and the thin chrome is drawn on top.
        //
        // Terrain matches the jet's TerrainModule: marching-squares contour lines colored by
        // CLEARANCE (terrain height relative to the reference altitude) — red above the reference,
        // yellow at it, green/dim below. A/D moves the reference so you look up/down the mountains.
        //
        // Blips persist at last-known position (stale = dimmed) so they never vanish; new ones
        // still appear. CLEAR drops stale ones.
        static class MapView
        {
            const float DEFAULT_MPP = 40f;   // meters per pixel at startup (~20 km across)
            const float MIN_MPP = 5f;        // zoomed in (~2.5 km across)
            const float MAX_MPP = 2500f;     // zoomed out (~1280 km across / ~640 km radius on 512px)
            const float SNAP_PX = 4f;        // seeker capture radius (px·k) around screen center — precise
            const double BREAK_ENERGY = 40;  // accumulated mouse energy needed to wiggle free of a lock
            const double BREAK_DECAY = 120;  // energy leak per second (so only a sustained wiggle breaks)
            const float PAN_SENS = 0.6f;     // mouse pixels per RotationIndicator unit (retired with mouse-look pan)
            const float ZOOM_RATE = 1.6f;    // zoom factor per second while holding W/S
            const float REF_RATE = 400f;     // elevation-reference meters per second on A/D
            const float EDGE_PAN = 240f;     // edge-scroll pan speed (px/sec) when the cursor hugs a map edge
            const float HP = 1.5708f;        // π/2 for line-sprite rotation
            const int MAX_TRACKS = 256;
            const int NMAX = 484;            // terrain node cap
            const int MAX_LINES = 320;       // contour line-sprite budget
            const int MAX_ZONE_LINES = 220;  // zone outline line-sprite budget
            const double RESAMPLE_SEC = 0.15;

            public static double PanEast;
            public static double PanNorth;
            public static float MetersPerPixel = DEFAULT_MPP;
            public static double RefElev;            // absolute terrain reference elevation — only A/D moves it
            static bool _refInit;                    // seeded once from the first view's median, then sticky
            public static bool ShowLabels = true;
            public static bool ShowTerrain = true;
            public static bool SeekerOn = false;     // look-to-lock seeker
            static long _lockId;                     // locked track id (0 = none)
            static double _relockAfter;              // suppress auto-lock until this time (after a manual unlock)
            static double _breakEnergy;              // accumulated mouse "wiggle" energy vs the current lock

            static readonly Color MAP_BG  = Cr(4, 8, 6);
            static readonly Color GRID    = Cr(16, 30, 21);
            static readonly Color NEUTRAL = Cr(120, 170, 210);
            static readonly Color UNKNOWN = Cr(150, 150, 150);

            // Contour thresholds on clearance (refAlt − terrain), matching the jet's palette.
            static readonly short[] TH = { -500, 0, 200, 800 };
            static readonly Color[] TC =
            {
                Cr(220, 40, 40),   // terrain far above the reference — bright red
                Cr(180, 180, 50),  // at the reference altitude — yellow
                Cr(48, 130, 48),   // below — green
                Cr(20, 55, 20),    // far below — dim green (terrain shape)
            };

            public static string ScaleLabel => SpriteHelpers.FormatRange(MetersPerPixel) + "/px";

            // ── Persistent last-known track store ──
            class MapTrack
            {
                public Vector3D Pos, Vel;
                public char Kind;
                public string Name;
                public bool Friendly;
                public bool Live;
                public double LastLive;
            }
            static readonly Dictionary<long, MapTrack> _tracks = new Dictionary<long, MapTrack>();

            // ── Terrain node cache (world-anchored; elevation sampled on a throttle, contours
            //    + clearance recomputed every frame so A/D and pan/zoom stay responsive) ──
            static Vector3D[] _nw;   // node world positions
            static float[] _ne;      // node terrain elevation (surf − meanR)
            static Vector2[] _ns;    // node screen positions (per frame)
            static short[] _cl;      // node clearance vs reference (per frame)
            static int _ncols, _nrows;
            static double _emean;
            static double _lastSample = -1;

            public static int StaleCount
            {
                get { int n = 0; foreach (var kv in _tracks) if (!kv.Value.Live) n++; return n; }
            }

            public static void SyncTracks()
            {
                double now = SystemManager.ElapsedSeconds;
                foreach (var kv in _tracks) kv.Value.Live = false;

                foreach (var kv in FleetState.Jets)
                {
                    MapTrack t = Get(kv.Key);
                    t.Friendly = true; t.Live = true;
                    t.Pos = kv.Value.Pos; t.Vel = kv.Value.Vel;
                    t.Name = FleetState.CallSign(kv.Value); t.LastLive = now;
                }
                foreach (var kv in FleetState.Contacts)
                {
                    MapTrack t = Get(kv.Key);
                    t.Friendly = false; t.Live = true;
                    t.Kind = kv.Value.Kind; t.Pos = kv.Value.Pos; t.Vel = kv.Value.Vel;
                    t.Name = kv.Value.Name; t.LastLive = now;
                }

                if (_tracks.Count > MAX_TRACKS) EvictOldestStale();
            }

            static MapTrack Get(long id)
            {
                MapTrack t;
                if (!_tracks.TryGetValue(id, out t)) { t = new MapTrack(); _tracks[id] = t; }
                return t;
            }

            static void EvictOldestStale()
            {
                long worst = 0; double oldest = double.MaxValue; bool found = false;
                foreach (var kv in _tracks)
                    if (!kv.Value.Live && kv.Value.LastLive < oldest) { oldest = kv.Value.LastLive; worst = kv.Key; found = true; }
                if (found) _tracks.Remove(worst);
            }

            public static void ClearStale()
            {
                var rm = new List<long>();
                foreach (var kv in _tracks) if (!kv.Value.Live) rm.Add(kv.Key);
                for (int i = 0; i < rm.Count; i++) _tracks.Remove(rm[i]);
            }

            // ── Map input ──
            // Two pan modes: in SEEKER (key 8 — the center brackets) mode the mouse pans the map
            // under the fixed reticle (look-to-lock targeting, as before). Otherwise the mouse is
            // the global cursor and the map pans by EDGE-SCROLL (cursor hugging an edge). W/S always
            // zoom, A/D always scan the elevation reference.
            public static void UpdateInput(Station st, bool active, bool seeker, float lx, float ly, double dt)
            {
                if (!active) return;
                Vector2 rot = st.Rot;
                Vector3 mov = st.Move;     // X = A(-)/D(+), Z = S(+)/W(-)

                // Seeker lock breaks once enough mouse energy builds against it.
                if (_lockId != 0)
                {
                    _breakEnergy += Ab(rot.X) + Ab(rot.Y) - BREAK_DECAY * dt;
                    if (_breakEnergy < 0) _breakEnergy = 0;
                    if (_breakEnergy > BREAK_ENERGY)
                    { _lockId = 0; _breakEnergy = 0; _relockAfter = SystemManager.ElapsedSeconds + 0.6; }
                }
                else _breakEnergy = 0;

                if (seeker)
                {
                    PanEast += rot.Y * PAN_SENS * MetersPerPixel;
                    PanNorth += -rot.X * PAN_SENS * MetersPerPixel;
                }
                else EdgePan(lx, ly, dt);

                if (mov.Z != 0)
                    MetersPerPixel = Cl(MetersPerPixel * (float)(1.0 + mov.Z * ZOOM_RATE * dt), MIN_MPP, MAX_MPP);
                if (mov.X != 0)
                    RefElev = Cl(RefElev + mov.X * REF_RATE * dt, -20000, 20000);
            }

            // Pan when the cursor hugs a map edge (RTS-style; no click, so it doesn't fire the gun).
            static void EdgePan(float lx, float ly, double dt)
            {
                float rw = Canvas.RW, rh = Canvas.RH;
                if (rw <= 1f || rh <= 1f) return;
                float m = 26f;
                float ex = lx < m ? -1f : lx > rw - m ? 1f : 0f;
                float ey = ly < m ? 1f : ly > rh - m ? -1f : 0f;
                if (ex == 0f && ey == 0f) return;
                double sp = EDGE_PAN * MetersPerPixel * dt;
                PanEast += ex * sp;
                PanNorth += ey * sp;
            }

            public static void Recenter() { PanEast = 0; PanNorth = 0; }   // pan only — leaves RefElev sticky
            public static void ZoomIn()  { MetersPerPixel = Cl(MetersPerPixel * 0.7f, MIN_MPP, MAX_MPP); }
            public static void ZoomOut() { MetersPerPixel = Cl(MetersPerPixel * 1.4f, MIN_MPP, MAX_MPP); }
            public static void ZoomBy(float f) { MetersPerPixel = Cl(MetersPerPixel * f, MIN_MPP, MAX_MPP); }

            // ── Full-surface render (dedicated HQ MAP screen, or HQ MFD fallback) ──
            public static void RenderFull(IMyTextSurface surface)
            {
                if (surface == null) return;
                var frame = surface.DrawFrame();
                SpriteBus.Begin(frame, null);
                try
                {
                    float sw = SX(surface), sh = SY(surface);
                    DrawMap(new RectangleF(0, 0, sw, sh), V2(sw, sh));
                    DrawChrome(sw, sh);
                    if (SystemManager.ZoneActive) ZoneEditor.RenderOverlay(sh / 512f);
                    // Global cursor lives on the map (right half of the canvas).
                    MouseCursor.DrawRight(sw, sh);
                }
                finally { SpriteBus.End(); frame.Dispose(); }
            }

            static void DrawMap(RectangleF area, Vector2 ss)
            {
                Station st = SystemManager.Station;
                float k = ss.Y / 512f;
                float x = area.Position.X, y = area.Position.Y, w = area.Width, h = area.Height;
                float cx = x + w / 2f, cy = y + h / 2f;
                float mpp = MetersPerPixel;

                Sq(cx, cy, w, h, MAP_BG);

                Vector3D up = st.MapUp, east, north;
                Frame(up, out east, out north);

                // Seeker camera-follow: a locked track is held at screen center (camera tracks it).
                MapTrack locked = null;
                if (SeekerOn && _lockId != 0)
                {
                    if (_tracks.TryGetValue(_lockId, out locked))
                    {
                        Vector3D off = locked.Pos - st.Position;
                        PanEast = VD(off, east); PanNorth = VD(off, north);
                    }
                    else _lockId = 0;
                }

                Vector3D center = st.Position + east * PanEast + north * PanNorth;

                // Publish the final view frame for the ZONE editor's screen<->world mapping.
                ViewCenter = center; ViewEast = east; ViewNorth = north; ViewCx = cx; ViewCy = cy;
                ViewReady = true;

                bool terrain = ShowTerrain && TerrainData.Ready;
                if (terrain) DrawTerrain(area, center, east, north, cx, cy, mpp, k);
                else DrawGrid(area, cx, cy, mpp, k);   // grid only when terrain isn't showing

                DrawZones(k);   // operator-drawn regions, beneath the HQ marker + blips

                Vector2 hq = ToScreen(st.Position, center, east, north, cx, cy, mpp);

                // Coverage rings under the blips: HQ real antenna reach (gold) + assumed friendly
                // bubbles (green, radius from CFG since the jets' true range isn't transmitted).
                if (st.AntennaRange > 0)
                    Ring(hq, (float)(st.AntennaRange / mpp), Cr(MFDTheme.CORP_GOLD, 0.5f));
                if (HQConfig.JetRange > 0)
                    foreach (var kv in _tracks)
                    {
                        MapTrack ft = kv.Value;
                        if (!ft.Live || !ft.Friendly) continue;
                        Vector2 fs = ToScreen(ft.Pos, center, east, north, cx, cy, mpp);
                        if (Off(fs, area, 12f * k)) continue;
                        Ring(fs, (float)(HQConfig.JetRange / mpp), Cr(MFDTheme.ACCENT, 0.33f));
                    }

                // Persistent tracks (live full-tint, stale dimmed; never auto-removed).
                foreach (var kv in _tracks)
                {
                    MapTrack t = kv.Value;
                    Vector2 s = ToScreen(t.Pos, center, east, north, cx, cy, mpp);
                    if (Off(s, area, 12f * k)) continue;

                    Color col; string tex;
                    if (t.Friendly) { col = MFDTheme.ACCENT; tex = TEX_C_FRIENDLY; }
                    else if (t.Kind == FleetState.KIND_HOSTILE) { col = MFDTheme.DANGER; tex = TEX_C_HOSTILE; }
                    else if (t.Kind == FleetState.KIND_NEUTRAL) { col = NEUTRAL; tex = TEX_C_UNKNOWN; }
                    else { col = UNKNOWN; tex = TEX_C_UNKNOWN; }
                    if (!t.Live) col = Cr(col, 0.3f);

                    SpriteHelpers.Sp(tex, s.X, s.Y, 11f * k, 11f * k, col);

                    if (t.Live && t.Friendly && t.Vel.LengthSquared() > 1)
                    {
                        Vector3D vd = VN(t.Vel);
                        Vector2 tip = s + V2((float)VD(vd, east), -(float)VD(vd, north)) * 11f * k;
                        SpriteHelpers.AddLineSprite(s, tip, 1.5f * k, col);
                    }
                    if (ShowLabels && !SE(t.Name))
                        SpriteHelpers.Tt(Clip(t.Name, 10, ""), s.X + 8f * k, s.Y - 5f * k, 0.3f * k, col, MFDTheme.AL);
                }

                long ovr = DatalinkHQ.StrikeId;
                if (ovr != 0)
                {
                    MapTrack ot;
                    if (_tracks.TryGetValue(ovr, out ot))
                    {
                        Vector2 os = ToScreen(ot.Pos, center, east, north, cx, cy, mpp);
                        if (!Off(os, area, 12f * k) && Anim.Blink(0.5))
                            Ring(os, 9f * k, MFDTheme.DANGER);
                    }
                }

                SpriteHelpers.Sp(TEX_OWN_SHIP, hq.X, hq.Y, 15f * k, 15f * k, MFDTheme.CORP_GOLD);
                DrawScaleBar(x, y, w, h, k, mpp);

                if (SeekerOn) DrawSeeker(st, center, east, north, up, cx, cy, mpp, k, locked);
            }

            public static void ToggleSeeker() { SeekerOn = !SeekerOn; if (!SeekerOn) _lockId = 0; }

            // Track under the marker: the seeker lock when engaged, else the map track nearest the
            // global cursor (non-friendly only). 0 = nothing marked.
            public static long MarkedTrackId()
            {
                MapTrack t;
                if (SeekerOn && _lockId != 0 && _tracks.TryGetValue(_lockId, out t) && !t.Friendly)
                    return _lockId;

                if (!ViewReady || !MouseCursor.Visible || !Canvas.OnRight(MouseCursor.X)) return 0;
                float lx = MouseCursor.X - Canvas.LW;
                float ly = Cl(MouseCursor.Y, 0f, Canvas.RH);
                float k = ViewCy / 256f;
                long cand = 0; float best = 12f * k;
                foreach (var kv in _tracks)
                {
                    if (kv.Value.Friendly) continue;
                    Vector2 s = WorldToScreen(kv.Value.Pos);
                    float dx = s.X - lx, dy = s.Y - ly;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < best) { best = d; cand = kv.Key; }
                }
                return cand;
            }

            public static bool TryGetTrack(long id, out Vector3D pos, out Vector3D vel, out bool live)
            {
                MapTrack t;
                if (_tracks.TryGetValue(id, out t))
                {
                    pos = t.Pos; vel = t.Vel; live = t.Live;
                    return true;
                }
                pos = VZ; vel = VZ; live = false;
                return false;
            }

            // The seeker gate sits at screen center. Pan a track into it and it auto-locks; the
            // camera then holds that target centered and a popup shows its data top-left. Wiggle
            // the mouse to break free (energy-gated, see UpdateInput).
            static void DrawSeeker(Station st, Vector3D center, Vector3D east, Vector3D north, Vector3D up,
                float cx, float cy, float mpp, float k, MapTrack locked)
            {
                if (locked != null)
                {
                    float prog = (float)(_breakEnergy / BREAK_ENERGY);
                    Color c = locked.Live ? MFDTheme.WARN : MFDTheme.DIM_TEXT_MID;
                    if (prog > 0.01f) c = Anim.LerpColor(c, MFDTheme.DANGER, prog);
                    else if (locked.Live && !Anim.Blink(0.6)) c = Cr(c, 0.5f);
                    float sz = (34f + prog * 14f) * k;          // gate swells as you pull free
                    SpriteHelpers.Sp(TEX_TGT_BRACKET, cx, cy, sz, sz, c);
                    SpriteHelpers.Tt(locked.Live ? "LOCK" : "LOST", cx, cy + 20f * k, 0.3f * k, c, MFDTheme.AC);
                    DrawPopup(locked, st, east, north, up, k);
                    return;
                }

                SpriteHelpers.Sp(TEX_TGT_BRACKET, cx, cy, 30f * k, 30f * k, Cr(MFDTheme.ACCENT, 0.5f));
                if (SystemManager.ElapsedSeconds < _relockAfter) return;

                long cand = 0; float best = SNAP_PX * k;
                foreach (var kv in _tracks)
                {
                    Vector2 s = ToScreen(kv.Value.Pos, center, east, north, cx, cy, mpp);
                    float dx = s.X - cx, dy = s.Y - cy;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < best) { best = d; cand = kv.Key; }
                }
                if (cand != 0) _lockId = cand;
            }

            static void DrawPopup(MapTrack t, Station st, Vector3D east, Vector3D north, Vector3D up, float k)
            {
                Vector3D to = t.Pos - st.Position;
                double rng = to.Length();
                double brg = (ToDeg(At2(VD(to, east), VD(to, north))) + 360) % 360;
                double alt = VD(to, up);
                double spd = t.Vel.Length();
                string kind = t.Friendly ? "FRIENDLY" : t.Kind == FleetState.KIND_HOSTILE ? "HOSTILE"
                            : t.Kind == FleetState.KIND_NEUTRAL ? "NEUTRAL" : "UNKNOWN";
                Color kc = t.Friendly ? MFDTheme.ACCENT : t.Kind == FleetState.KIND_HOSTILE ? MFDTheme.DANGER
                         : t.Kind == FleetState.KIND_NEUTRAL ? NEUTRAL : UNKNOWN;

                float x = 8f * k, y = 26f * k, w = 158f * k, rh = 15f * k;
                int rows = t.Live ? 5 : 6;
                float h = 24f * k + rows * rh;
                Sq(x + w / 2f, y + h / 2f, w, h, Cr(1, 4, 3, 235));
                SpriteHelpers.DrawRectangleOutline(x, y, w, h, 1f, kc);
                Sq(x + w / 2f, y + 9f * k, w, 18f * k, Cr(kc, 0.18f));
                SpriteHelpers.Tt(Clip(SE(t.Name) ? "TARGET" : t.Name, 16, "TARGET"),
                    x + 5f * k, y + 2f * k, 0.38f * k, MFDTheme.BRIGHT_TEXT, MFDTheme.AL);

                float ry = y + 24f * k;
                PopRow(ref ry, x, w, rh, k, "TYPE", kind, kc);
                PopRow(ref ry, x, w, rh, k, "RNG", SpriteHelpers.FormatRange(rng), MFDTheme.NORMAL_TEXT);
                PopRow(ref ry, x, w, rh, k, "BRG", ((int)brg).ToString("000"), MFDTheme.NORMAL_TEXT);
                PopRow(ref ry, x, w, rh, k, "SPD", (int)spd + " m/s", MFDTheme.NORMAL_TEXT);
                PopRow(ref ry, x, w, rh, k, "ALT", (alt >= 0 ? "+" : "") + (int)alt + "m", MFDTheme.NORMAL_TEXT);
                if (!t.Live) PopRow(ref ry, x, w, rh, k, "TRK", "LOST CONTACT", MFDTheme.WARN);
            }

            static void PopRow(ref float y, float x, float w, float rh, float k, string label, string val, Color vc)
            {
                SpriteHelpers.Tt(label, x + 6f * k, y, 0.3f * k, MFDTheme.DIM_TEXT_MID, MFDTheme.AL);
                SpriteHelpers.Tt(val, x + w - 6f * k, y, 0.3f * k, vc, MFDTheme.AR);
                y += rh;
            }

            // ── Terrain (jet-style clearance contours) ──
            static void DrawTerrain(RectangleF area, Vector3D center, Vector3D east, Vector3D north,
                float cx, float cy, float mpp, float k)
            {
                double now = SystemManager.ElapsedSeconds;
                if (_lastSample < 0 || now - _lastSample > RESAMPLE_SEC)
                    Resample(area, center, east, north, cx, cy, mpp, k, now);
                if (_ncols < 2 || _nrows < 2) return;

                // Per-frame: project nodes + compute clearance vs the sticky reference altitude.
                double refElev = RefElev;
                int n = _ncols * _nrows;
                for (int i = 0; i < n; i++)
                {
                    _ns[i] = ToScreen(_nw[i], center, east, north, cx, cy, mpp);
                    double cl = refElev - _ne[i];
                    _cl[i] = (short)(cl > 32000 ? 32000 : cl < -32000 ? -32000 : cl);
                }
                DrawContours(1.4f * k);
            }

            static void Resample(RectangleF area, Vector3D center, Vector3D east, Vector3D north,
                float cx, float cy, float mpp, float k, double now)
            {
                if (_nw == null)
                {
                    _nw = new Vector3D[NMAX]; _ne = new float[NMAX];
                    _ns = new Vector2[NMAX]; _cl = new short[NMAX];
                }

                float w = area.Width, h = area.Height;
                float cellPx = 28f * k;
                int cols = (int)(w / cellPx) + 3, rows = (int)(h / cellPx) + 3;
                while (cols * rows > NMAX) { cellPx *= 1.25f; cols = (int)(w / cellPx) + 3; rows = (int)(h / cellPx) + 3; }

                float startX = area.Position.X - cellPx, startY = area.Position.Y - cellPx;
                double sum = 0; int idx = 0;
                for (int gr = 0; gr < rows; gr++)
                    for (int gc = 0; gc < cols; gc++)
                    {
                        float nx = startX + gc * cellPx, ny = startY + gr * cellPx;
                        Vector3D wp = center + east * ((nx - cx) * mpp) + north * ((cy - ny) * mpp);
                        int r, c; TerrainData.W2G(wp, out r, out c);
                        float ev = (float)(TerrainData.Surf(r, c) - TerrainData.MeanR);
                        _nw[idx] = wp; _ne[idx] = ev; sum += ev; idx++;
                    }
                _ncols = cols; _nrows = rows;
                _emean = idx > 0 ? sum / idx : 0;
                if (!_refInit) { RefElev = _emean; _refInit = true; }   // seed once, then sticky
                _lastSample = now;
            }

            // Marching squares over the node grid, threshold-major (each clearance band fully drawn
            // before the next), bounded by a line-sprite budget. Mirrors the jet's contour cases.
            static void DrawContours(float lt)
            {
                int nd = _ncols, lines = 0;
                for (int t = 0; t < TH.Length; t++)
                {
                    short th = TH[t];
                    Color col = TC[t];
                    for (int r = 0; r < _nrows - 1; r++)
                    {
                        int ri = r * nd, ri1 = ri + nd;
                        for (int c = 0; c < _ncols - 1; c++)
                        {
                            int i0 = ri + c, i1 = i0 + 1, i2 = ri1 + c, i3 = i2 + 1;
                            short v0 = _cl[i0], v1 = _cl[i1], v2 = _cl[i2], v3 = _cl[i3];
                            short mn = v0, mx = v0;
                            if (v1 < mn) mn = v1; if (v1 > mx) mx = v1;
                            if (v2 < mn) mn = v2; if (v2 > mx) mx = v2;
                            if (v3 < mn) mn = v3; if (v3 > mx) mx = v3;
                            if (mn >= th || mx < th) continue;

                            int m = 0;
                            if (v0 >= th) m |= 1; if (v1 >= th) m |= 2;
                            if (v2 >= th) m |= 4; if (v3 >= th) m |= 8;
                            if (m == 0 || m == 15) continue;

                            Vector2 P0 = _ns[i0], P1 = _ns[i1], P2 = _ns[i2], P3 = _ns[i3];
                            switch (m)
                            {
                                case 1: case 14: AF(EP(P0, P1, v0, v1, th), EP(P0, P2, v0, v2, th), lt, col); break;
                                case 2: case 13: AF(EP(P0, P1, v0, v1, th), EP(P1, P3, v1, v3, th), lt, col); break;
                                case 3: case 12: AF(EP(P0, P2, v0, v2, th), EP(P1, P3, v1, v3, th), lt, col); break;
                                case 4: case 11: AF(EP(P0, P2, v0, v2, th), EP(P2, P3, v2, v3, th), lt, col); break;
                                case 5: case 10: AF(EP(P0, P1, v0, v1, th), EP(P2, P3, v2, v3, th), lt, col); break;
                                case 7: case 8: AF(EP(P1, P3, v1, v3, th), EP(P2, P3, v2, v3, th), lt, col); break;
                                case 6:
                                    AF(EP(P0, P1, v0, v1, th), EP(P0, P2, v0, v2, th), lt, col);
                                    AF(EP(P1, P3, v1, v3, th), EP(P2, P3, v2, v3, th), lt, col); lines++; break;
                                case 9:
                                    AF(EP(P0, P1, v0, v1, th), EP(P1, P3, v1, v3, th), lt, col);
                                    AF(EP(P0, P2, v0, v2, th), EP(P2, P3, v2, v3, th), lt, col); lines++; break;
                            }
                            if (++lines >= MAX_LINES) return;
                        }
                    }
                }
            }

            static Vector2 EP(Vector2 a, Vector2 b, short va, short vb, short th)
            {
                return a + (b - a) * Lp(va, vb, th);
            }

            static float Lp(short a, short b, short t)
            {
                int d = b - a; if (d > -1 && d < 1) return 0.5f;
                float v = (float)(t - a) / d; return v < 0f ? 0f : v > 1f ? 1f : v;
            }

            static void AF(Vector2 a, Vector2 b, float t, Color c)
            {
                Vector2 d = b - a; float ls = d.X * d.X + d.Y * d.Y;
                if (ls < 0.25f) return;
                Vector2 mid = (a + b) * 0.5f;
                Sq(mid.X, mid.Y, t, (float)Math.Sqrt(ls), c, (float)At2(d.Y, d.X) - HP);
            }

            // Self-similar grid (shown when terrain isn't): power-of-two spacing so coarse lines are
            // always a subset of fine lines (they never reflow); the finest level fades by zoom.
            static void DrawGrid(RectangleF area, float cx, float cy, float mpp, float k)
            {
                float x = area.Position.X, y = area.Position.Y, w = area.Width, h = area.Height;
                double lvl = Math.Log(64.0 * k * mpp, 2);
                int L = (int)Math.Floor(lvl);
                double sMinor = Math.Pow(2, L);
                Color cMin = Cr(GRID, (float)(1.0 - (lvl - L)) * 0.85f);

                double e0 = PanEast - (w / 2.0) * mpp, e1 = PanEast + (w / 2.0) * mpp;
                for (long i = (long)Math.Ceiling(e0 / sMinor); i <= (long)Math.Floor(e1 / sMinor); i++)
                    Sq(cx + (float)((i * sMinor - PanEast) / mpp), cy, 1f, h, (i & 1) == 0 ? GRID : cMin);

                double n0 = PanNorth - (h / 2.0) * mpp, n1 = PanNorth + (h / 2.0) * mpp;
                for (long j = (long)Math.Ceiling(n0 / sMinor); j <= (long)Math.Floor(n1 / sMinor); j++)
                    Sq(cx, cy - (float)((j * sMinor - PanNorth) / mpp), w, 1f, (j & 1) == 0 ? GRID : cMin);
            }

            static void Frame(Vector3D up, out Vector3D east, out Vector3D north)
            {
                up = VN(up);
                Vector3D refv = Ab(VD(up, new Vector3D(0, 1, 0))) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
                east = VN(VX(up, refv));
                north = VN(VX(east, up));
            }

            static Vector2 ToScreen(Vector3D p, Vector3D center, Vector3D east, Vector3D north,
                float cx, float cy, float mpp)
            {
                Vector3D d = p - center;
                return V2(cx + (float)(VD(d, east) / mpp), cy - (float)(VD(d, north) / mpp));
            }

            // ── View frame, promoted from DrawMap each tick, so the ZONE editor can map the cursor's
            //    map-local screen position back to a world position (and project zones to draw). ──
            public static Vector3D ViewCenter, ViewEast, ViewNorth;
            public static float ViewCx, ViewCy;
            public static bool ViewReady;   // true once DrawMap has published a valid frame this session

            // Map-local screen px -> world (inverse of ToScreen; mirrors Resample's unprojection).
            public static Vector3D ScreenToWorld(float lx, float ly)
            {
                return ViewCenter + ViewEast * ((lx - ViewCx) * MetersPerPixel)
                                  + ViewNorth * ((ViewCy - ly) * MetersPerPixel);
            }

            // World -> map-local screen px using the stored view frame.
            public static Vector2 WorldToScreen(Vector3D w)
            {
                return ToScreen(w, ViewCenter, ViewEast, ViewNorth, ViewCx, ViewCy, MetersPerPixel);
            }

            static bool Off(Vector2 s, RectangleF a, float m)
            {
                return s.X < a.Position.X - m || s.X > a.Position.X + a.Width + m
                    || s.Y < a.Position.Y - m || s.Y > a.Position.Y + a.Height + m;
            }

            static void Ring(Vector2 c, float rpx, Color col)
            {
                if (rpx < 6f) return;
                SpriteHelpers.Sp(TEX_RANGE_RING, c.X, c.Y, rpx * 2f, rpx * 2f, col);
            }

            // Public ring + palette color so the ZONE editor can draw its overlay with the same look.
            public static void DrawRing(Vector2 c, float rpx, Color col) => Ring(c, rpx, col);

            public static Color ZoneColor(ZoneKind kk)
            {
                switch (kk)
                {
                    case ZoneKind.Enemy: return MFDTheme.DANGER;
                    case ZoneKind.NoFly: return MFDTheme.WARN;
                    case ZoneKind.SAM:   return Cr(200, 60, 200);
                    case ZoneKind.CAP:   return MFDTheme.ACCENT;
                    case ZoneKind.Rally: return MFDTheme.CORP_GOLD;
                    default: return MFDTheme.NORMAL_TEXT;
                }
            }

            // Committed zones: circles as range rings, polygons as outlines, name labels. Bounded by
            // MAX_ZONE_LINES so a busy board can't blow the per-tick sprite budget.
            static void DrawZones(float k)
            {
                var zs = ZoneStore.Zones;
                if (zs.Count == 0) return;
                int lines = 0;
                for (int i = 0; i < zs.Count; i++)
                {
                    Zone z = zs[i];
                    Color col = ZoneColor(z.Kind);
                    if (z.Shape == ZoneShape.Circle)
                    {
                        Vector2 cs = WorldToScreen(z.Center);
                        Ring(cs, (float)(z.Radius / MetersPerPixel), Cr(col, 0.6f));
                        if (ShowLabels && !SE(z.Name))
                            SpriteHelpers.Tt(z.Name, cs.X, cs.Y, 0.3f * k, col, MFDTheme.AC);
                    }
                    else
                    {
                        int n = z.Verts.Count;
                        if (n < 2) continue;
                        Vector2 prev = WorldToScreen(z.Verts[n - 1]);
                        for (int v = 0; v < n; v++)
                        {
                            Vector2 cur = WorldToScreen(z.Verts[v]);
                            SpriteHelpers.AddLineSprite(prev, cur, 1.6f * k, Cr(col, 0.7f));
                            prev = cur;
                            if (++lines >= MAX_ZONE_LINES) return;
                        }
                        if (ShowLabels && !SE(z.Name))
                        {
                            Vector2 cc = WorldToScreen(z.Center);
                            SpriteHelpers.Tt(z.Name, cc.X, cc.Y, 0.3f * k, col, MFDTheme.AC);
                        }
                    }
                }
            }

            static void DrawScaleBar(float x, float y, float w, float h, float k, float mpp)
            {
                double nice = NiceStep(70f * mpp);
                float px = (float)(nice / mpp);
                float bx = x + 12f * k, by = y + h - 26f * k;
                Sq(bx + px / 2f, by, px, 2.5f * k, MFDTheme.BRIGHT_TEXT);
                Sq(bx, by, 1.5f * k, 6f * k, MFDTheme.BRIGHT_TEXT);
                Sq(bx + px, by, 1.5f * k, 6f * k, MFDTheme.BRIGHT_TEXT);
                SpriteHelpers.Tt(SpriteHelpers.FormatRange(nice), bx, by - 12f * k, 0.3f * k, MFDTheme.NORMAL_TEXT, MFDTheme.AL);
            }

            static void DrawChrome(float sw, float sh)
            {
                float k = sh / 512f;
                Sq(sw / 2f, 9f * k, sw, 18f * k, MFDTheme.HEADER_BG);
                Sq(sw / 2f, 18f * k, sw, 1f, MFDTheme.GOLD_LINE);
                SpriteHelpers.Tt("NYINAH CORP  TACMAP", 6f * k, 2f * k, 0.42f * k, MFDTheme.CORP_GOLD, MFDTheme.AL);
                Color tc = MFDTheme.TacsitColor(FleetState.Tacsit);
                SpriteHelpers.Tt(FleetState.Tacsit + "  " + ScaleLabel, sw - 6f * k, 3f * k, 0.34f * k, tc, MFDTheme.AR);

                if (ShowTerrain && TerrainData.Ready)
                    SpriteHelpers.Tt("REF " + SpriteHelpers.FormatRange(RefElev),
                        sw / 2f, 3f * k, 0.34f * k, MFDTheme.NORMAL_TEXT, MFDTheme.AC);
                else if (ShowTerrain && TerrainData.Loading)
                    SpriteHelpers.Tt("TERRAIN " + (int)(TerrainData.DownloadProgress * 100) + "%",
                        sw / 2f, 3f * k, 0.32f * k, MFDTheme.WARN, MFDTheme.AC);

                Sq(sw / 2f, sh - 9f * k, sw, 18f * k, MFDTheme.HEADER_BG);
                Sq(sw / 2f, sh - 18f * k, sw, 1f, MFDTheme.BORDER);
                SpriteHelpers.Tt("MOUSE PAN  W/S ZOOM  A/D ELEV", 6f * k, sh - 14f * k, 0.3f * k, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(FleetState.Jets.Count + "J  " + FleetState.Contacts.Count + "C",
                    sw - 6f * k, sh - 14f * k, 0.32f * k, MFDTheme.DIM_TEXT_MID, MFDTheme.AR);

                MFDFrame.DrawScreenBorder(sw, sh);
            }

            static double NiceStep(double target)
            {
                if (target <= 0) return 1;
                double p = Math.Pow(10, Math.Floor(Math.Log10(target)));
                double f = target / p;
                double n = f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10;
                return n * p;
            }
        }
    }
}
