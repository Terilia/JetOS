using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using JetOSExtensions.Shared;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;
using Torch;
using Torch.API;

namespace TerrainAPI
{
    /// <summary>
    /// Terrain height query API — Torch plugin.
    /// One terminal property: "TerrainAPI" on IMyProgrammableBlock.
    ///
    /// ═══════════════════════════════════════════════════════════════
    ///  COMMANDS  (Set then Get on "TerrainAPI")
    /// ═══════════════════════════════════════════════════════════════
    ///
    /// ── SUBSCRIBE (server-side rendered terrain map) ──────────────
    ///
    ///   Set: "S;w;h;cell"                        subscribe, default palette
    ///        "S;w;h;cell;palette"                 custom palette chars (low→high)
    ///        "S;w;h;cell;palette;V"               voxel mode (includes digging)
    ///        "S;w;h;cell;palette;V;2"             voxel mode, LOD 2
    ///        "S;w;h;cell;R"                       raw height mode (see below)
    ///        "S;w;h;cell;R;V"                     raw + voxel
    ///        "S;w;h;cell;R;V;2"                   raw + voxel + LOD
    ///
    ///     w,h   = map dimensions in characters (cols × rows)
    ///     cell  = meters between samples
    ///     Plugin reads PB WorldMatrix each tick for position/orientation.
    ///     Re-renders only when PB moves ≥ cell/2 or rotates.
    ///
    ///   Get (palette mode): LCD-ready text (w chars/line, h lines, '\n' sep).
    ///        Each char is a palette character representing relative terrain height.
    ///        Center cell shows '+' as the PB position marker.
    ///
    ///   Get (R mode): raw heights, same encoding as H command.
    ///        "baseAlt;pcX;pcY;pcZ;rX;rY;rZ;fX;fY;fZ\n"
    ///        followed immediately by w*h height chars.
    ///        Each char encodes one height: meters = (int)char - 32768
    ///        PB finds data start at first '\n' + 1.
    ///
    ///   PB usage (constructor — run once):
    ///     Me.SetValue&lt;StringBuilder&gt;("TerrainAPI", new StringBuilder("S;80;40;10"));
    ///     _terrainSb = Me.GetValue&lt;StringBuilder&gt;("TerrainAPI");
    ///
    ///   PB usage (Main — every tick, ~3 instructions):
    ///     lcd.WriteText(_terrainSb);
    ///
    ///   Set: "U"                                 unsubscribe
    ///
    /// ── POINT QUERY (synchronous, for per-frame collision checks) ──
    ///
    ///   Set: "x;y;z"                      one point, heightmap
    ///        "x1;y1;z1;x2;y2;z2;..."      N points, heightmap
    ///        "V;x;y;z"                     voxel mode (includes digging)
    ///        "V;2;x;y;z"                   voxel mode, LOD 2
    ///   Get: "sx;sy;sz"                    surface point(s), world coords
    ///        "sx1;sy1;sz1;sx2;sy2;sz2;..."
    ///
    /// ── HEIGHTMAP (synchronous, all data in one response) ──────────
    ///
    ///   Set: "H;cx;cy;cz;fx;fy;fz;w;h;cell"         heightmap mode
    ///        "H;cx;cy;cz;fx;fy;fz;w;h;cell;V"        voxel mode
    ///        "H;cx;cy;cz;fx;fy;fz;w;h;cell;V;2"      voxel LOD 2
    ///     cx/cy/cz = grid center (world), fx/fy/fz = forward dir
    ///     w,h = grid dimensions, cell = meters between samples
    ///
    ///   Get: "w;h;cell;baseAlt;pcX;pcY;pcZ;rX;rY;rZ;fX;fY;fZ;"
    ///        followed immediately by w*h height chars.
    ///        Each char encodes one height: meters = (int)char - 32768
    ///        Row-major order (col varies fastest).
    ///
    ///   ERROR: "E;message"
    ///
    /// ═══════════════════════════════════════════════════════════════
    ///  PERFORMANCE
    /// ═══════════════════════════════════════════════════════════════
    ///
    ///   Point query: ~100ns per point, synchronous, 60fps safe.
    ///   200×200 heightmap: ~4ms.  400×400: ~16ms.
    ///   Subscribe mode: ~0 PB instructions (server renders, PB passes through).
    ///
    /// </summary>
    public sealed class TerrainApiFeature
    {
        static readonly Logger Log = LogManager.GetCurrentClassLogger();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        const string PROP = TerrainApiProtocol.PropertyName;
        const int MAX_BATCH = 4096;
        const int MAX_DIM = 2048;
        const int HEIGHT_OFFSET = 32768;
        const string DEFAULT_PALETTE = " .:-=+*#%@";

        readonly Dictionary<long, StringBuilder> _res = new Dictionary<long, StringBuilder>();
        readonly Dictionary<long, SubInfo> _subs = new Dictionary<long, SubInfo>();
        readonly List<long> _deadSubs = new List<long>();
        readonly List<MyPlanet> _planets = new List<MyPlanet>();
        int _ptick;
        MyStorageData _vd;
        bool _registered;

        public bool Registered => _registered;
        public int SubscriptionCount => _subs.Count;
        public int DownloadCount => _downloads.Count;
        public int PlanetCount => _planets.Count;
        public int ResponseCount => _res.Count;

        class SubInfo
        {
            public int W, H;
            public double Cell;
            public string Palette;
            public bool Voxel;
            public int Lod;
            public Vector3D LastPos;
            public Vector3D LastFwd;
            public double MoveThrSq;     // squared distance to trigger re-render
            public double[] HeightsBuf;  // reusable buffer to avoid GC

            // Edge cache for R mode — avoids full resample on movement
            public double[] CachedAlts;  // w*h absolute altitudes from planet center
            public double[] ShiftBuf;    // temp buffer for grid shifting
            public Vector3D CacheCenter; // grid center when cache was built
            public Vector3D CacheRight;  // grid right axis when cache was built
            public Vector3D CacheFwd;    // grid forward axis when cache was built
            public Vector3D CachePc;     // planet center when cache was built
            public bool CacheValid;
        }

        // Planet download state per PB
        class PlanetDL
        {
            public MyPlanet Planet;
            public Vector3D Pc;       // planet center
            public double MeanRadius;
            public int Rows, Cols;
            public double CellSize;
        }

        readonly Dictionary<long, PlanetDL> _downloads = new Dictionary<long, PlanetDL>();
        const int MAX_CHUNK = 20000;

        public void Init(ITorchBase torch)
        {
            Log.Info("JetOSExtensions.Server: TerrainAPI feature loaded.");
        }

        public void Update()
        {
            // ── One-time property registration ──
            if (!_registered)
            {
                if (MyAPIGateway.TerminalControls == null) return;

                try
                {
                    var prop = MyAPIGateway.TerminalControls
                        .CreateProperty<StringBuilder, IMyProgrammableBlock>(PROP);
                    prop.Getter = Get;
                    prop.Setter = Set;
                    prop.SupportsMultipleBlocks = false;
                    MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(prop);
                    Log.Info("TerrainAPI: Property registered.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "TerrainAPI: Registration failed.");
                }

                _registered = true;
            }

            // ── Process subscriptions ──
            if (_subs.Count == 0) return;

            _deadSubs.Clear();
            foreach (var kvp in _subs)
            {
                IMyEntity entity;
                try { entity = MyAPIGateway.Entities.GetEntityById(kvp.Key); }
                catch { entity = null; }

                if (entity == null || entity.Closed)
                {
                    _deadSubs.Add(kvp.Key);
                    continue;
                }

                var sub = kvp.Value;
                var mat = entity.WorldMatrix;
                var pos = mat.Translation;
                var fwd = mat.Forward;

                // Skip re-render if position and orientation haven't changed enough
                if (Vector3D.DistanceSquared(pos, sub.LastPos) < sub.MoveThrSq
                    && Vector3D.Dot(fwd, sub.LastFwd) > 0.9998)
                    continue;

                sub.LastPos = pos;
                sub.LastFwd = fwd;

                var sb = Buf(kvp.Key);
                sb.Clear();
                RenderMap(sb, pos, fwd, sub);
            }

            for (int i = 0; i < _deadSubs.Count; i++)
            {
                _subs.Remove(_deadSubs[i]);
                _res.Remove(_deadSubs[i]);
            }
        }

        // ── Property handler ────────────────────────────────────────

        void Set(IMyTerminalBlock blk, StringBuilder val)
        {
            var sb = Buf(blk.EntityId);
            sb.Clear();

            if (val == null || val.Length == 0)
            {
                sb.Append("E;Empty");
                return;
            }

            try
            {
                var req = val.ToString();
                char first = req[0];

                if (first == 'S')
                    HandleSubscribe(blk, sb, req);
                else if (first == 'U')
                    HandleUnsubscribe(blk, sb);
                else if (first == 'P')
                    HandlePlanetInit(blk, sb, req);
                else if (first == 'C')
                    HandleChunkRequest(blk, sb, req);
                else if (first == 'H')
                    HandleHeightmapRequest(sb, req);
                else
                    HandlePointQuery(sb, req);
            }
            catch (Exception ex)
            {
                sb.Clear().Append("E;").Append(ex.Message);
            }
        }

        StringBuilder Get(IMyTerminalBlock blk)
        {
            StringBuilder sb;
            return _res.TryGetValue(blk.EntityId, out sb) ? sb : null;
        }

        // ── Subscribe / Unsubscribe ─────────────────────────────────

        void HandleSubscribe(IMyTerminalBlock blk, StringBuilder res, string req)
        {
            // S;w;h;cell[;palette][;V[;lod]]
            var p = req.Split(';');
            if (p.Length < 4)
            {
                res.Append("E;Need S;w;h;cell");
                return;
            }

            int w = int.Parse(p[1]);
            int h = int.Parse(p[2]);
            double cell = Pd(p[3]);

            if (w < 1 || h < 1 || w > MAX_DIM || h > MAX_DIM)
            {
                res.Append("E;Dimensions 1-").Append(MAX_DIM);
                return;
            }
            if (cell < 0.5)
            {
                res.Append("E;Cell>=0.5m");
                return;
            }

            string palette = DEFAULT_PALETTE;
            bool vox = false;
            int lod = 0;
            int idx = 4;

            // Optional palette (any non-"V" string at position 4)
            if (idx < p.Length && p[idx].Length > 0 && p[idx] != "V")
            {
                palette = p[idx];
                idx++;
            }

            // Optional voxel mode
            if (idx < p.Length && p[idx] == "V")
            {
                vox = true;
                idx++;
                if (idx < p.Length)
                {
                    int tryLod;
                    if (int.TryParse(p[idx], out tryLod) && tryLod >= 0 && tryLod <= 10)
                        lod = tryLod;
                }
            }

            var sub = new SubInfo
            {
                W = w,
                H = h,
                Cell = cell,
                Palette = palette,
                Voxel = vox,
                Lod = lod,
                LastPos = new Vector3D(double.MaxValue), // force first render
                LastFwd = Vector3D.Zero,
                MoveThrSq = cell * cell * 0.25, // re-render when moved ≥ cell/2
                HeightsBuf = new double[w * h],
                CachedAlts = new double[w * h],
                ShiftBuf = new double[w * h]
            };

            _subs[blk.EntityId] = sub;

            // Render first frame immediately so GetValue has content right away
            var mat = blk.WorldMatrix;
            sub.LastPos = mat.Translation;
            sub.LastFwd = mat.Forward;
            RenderMap(res, mat.Translation, mat.Forward, sub);

            Log.Info($"TerrainAPI: Subscribed {blk.EntityId} ({w}x{h} cell={cell})");
        }

        void HandleUnsubscribe(IMyTerminalBlock blk, StringBuilder res)
        {
            _subs.Remove(blk.EntityId);
            res.Append("OK");
            Log.Info($"TerrainAPI: Unsubscribed {blk.EntityId}");
        }

        // ── Server-side terrain map rendering ───────────────────────

        void RenderMap(StringBuilder res, Vector3D center, Vector3D fwd, SubInfo sub)
        {
            var planet = Closest(center);
            if (planet == null)
            {
                res.Append("E;NoPlanet");
                return;
            }

            var pc = planet.PositionComp.GetPosition();

            // Build local horizontal grid axes
            var up = Vector3D.Normalize(center - pc);
            var right = Vector3D.Cross(fwd, up);
            if (right.LengthSquared() < 1e-6)
                right = Vector3D.Cross(Vector3D.Forward, up);
            right = Vector3D.Normalize(right);
            var gfwd = Vector3D.Cross(up, right);

            int w = sub.W, h = sub.H;
            double cell = sub.Cell;
            double halfW = (w - 1) * 0.5;
            double halfH = (h - 1) * 0.5;
            // ── Raw height mode (R) with edge caching ──
            if (sub.Palette == "R")
            {
                // Can we reuse cached altitudes? (same planet, same orientation)
                bool canReuse = sub.CacheValid
                    && Vector3D.DistanceSquared(pc, sub.CachePc) < 1.0
                    && Vector3D.Dot(right, sub.CacheRight) > 0.9998
                    && Vector3D.Dot(gfwd, sub.CacheFwd) > 0.9998;

                if (canReuse)
                {
                    // Project movement onto old grid axes to get cell shift
                    var delta = center - sub.CacheCenter;
                    int sx = (int)Math.Round(Vector3D.Dot(delta, sub.CacheRight) / cell);
                    int sy = (int)Math.Round(Vector3D.Dot(delta, sub.CacheFwd) / cell);

                    if (sx == 0 && sy == 0)
                    {
                        // Within same cell — re-encode from cache with new baseAlt
                    }
                    else if (Math.Abs(sx) < w && Math.Abs(sy) < h)
                    {
                        // Shift cache, sample only new edge cells
                        ShiftAndSampleEdges(sub, planet, pc, right, gfwd, center, sx, sy);
                    }
                    else
                    {
                        // Moved too far — full resample
                        FullSampleR(sub, planet, pc, right, gfwd, center);
                    }
                }
                else
                {
                    FullSampleR(sub, planet, pc, right, gfwd, center);
                }

                sub.CacheCenter = center;
                sub.CacheRight = right;
                sub.CacheFwd = gfwd;
                sub.CachePc = pc;
                sub.CacheValid = true;

                EncodeR(res, sub, planet, pc, center, right, gfwd);
                return;
            }

            var heights = sub.HeightsBuf;

            // Sample all terrain heights
            double min = double.MaxValue, max = double.MinValue;

            for (int row = 0; row < h; row++)
            {
                double dy = (row - halfH) * cell;
                for (int col = 0; col < w; col++)
                {
                    double dx = (col - halfW) * cell;
                    var samplePos = center + right * dx + gfwd * dy;
                    var surf = sub.Voxel
                        ? SurfaceVoxel(planet, samplePos, sub.Lod)
                        : planet.GetClosestSurfacePointGlobal(samplePos);
                    double alt = (surf - pc).Length();
                    heights[row * w + col] = alt;
                    if (alt < min) min = alt;
                    if (alt > max) max = alt;
                }
            }

            // Map heights to palette chars and build LCD text
            var pal = sub.Palette;
            int pLen = pal.Length;
            double range = max - min;
            if (range < 0.01) range = 1.0; // flat terrain — avoid div-by-zero

            int cCol = (int)halfW;
            int cRow = (int)halfH;

            res.EnsureCapacity(w * h + h);
            for (int row = 0; row < h; row++)
            {
                for (int col = 0; col < w; col++)
                {
                    if (row == cRow && col == cCol)
                    {
                        res.Append('+');
                    }
                    else
                    {
                        double norm = (heights[row * w + col] - min) / range;
                        int pi = (int)(norm * (pLen - 1) + 0.5);
                        if (pi < 0) pi = 0;
                        else if (pi >= pLen) pi = pLen - 1;
                        res.Append(pal[pi]);
                    }
                }
                res.Append('\n');
            }
        }

        // ── Planet download ──────────────────────────────────────────

        void HandlePlanetInit(IMyTerminalBlock blk, StringBuilder res, string req)
        {
            // P;cellSize
            var p = req.Split(';');
            if (p.Length < 2) { res.Append("E;Need P;cellSize"); return; }

            double cellSize = Pd(p[1]);
            if (cellSize < 10) { res.Append("E;Cell>=10m"); return; }

            var pos = blk.WorldMatrix.Translation;
            var planet = Closest(pos);
            if (planet == null) { res.Append("E;NoPlanet"); return; }

            var pc = planet.PositionComp.GetPosition();

            // Sample 6 cardinal directions to estimate mean radius
            double sum = 0;
            var dirs = new[] {
                Vector3D.Right, Vector3D.Left, Vector3D.Up,
                Vector3D.Down, Vector3D.Forward, Vector3D.Backward
            };
            foreach (var d in dirs)
                sum += (planet.GetClosestSurfacePointGlobal(pc + d * 100000) - pc).Length();
            double meanR = sum / dirs.Length;

            int rows = (int)Math.Ceiling(Math.PI * meanR / cellSize);
            int cols = (int)Math.Ceiling(2.0 * Math.PI * meanR / cellSize);
            if (rows < 1) rows = 1;
            if (cols < 1) cols = 1;

            _downloads[blk.EntityId] = new PlanetDL
            {
                Planet = planet, Pc = pc, MeanRadius = meanR,
                Rows = rows, Cols = cols, CellSize = cellSize
            };

            // Response: P;rows;cols;cellSize;meanRadius;pcX;pcY;pcZ
            res.Append("P;");
            res.Append(rows).Append(';');
            res.Append(cols).Append(';');
            Ap(res, cellSize); res.Append(';');
            Ap(res, meanR); res.Append(';');
            Ap(res, pc.X); res.Append(';');
            Ap(res, pc.Y); res.Append(';');
            Ap(res, pc.Z);

            Log.Info($"TerrainAPI: Planet download initiated for {blk.EntityId} — {rows}x{cols} ({rows * cols} pts)");
        }

        void HandleChunkRequest(IMyTerminalBlock blk, StringBuilder res, string req)
        {
            // C;offset;count
            var p = req.Split(';');
            if (p.Length < 3) { res.Append("E;Need C;offset;count"); return; }

            int offset = int.Parse(p[1]);
            int count = int.Parse(p[2]);
            if (count > MAX_CHUNK) count = MAX_CHUNK;

            PlanetDL dl;
            if (!_downloads.TryGetValue(blk.EntityId, out dl))
            {
                res.Append("E;No download — send P;cellSize first");
                return;
            }

            if (dl.Planet == null || dl.Planet.Closed)
            {
                _downloads.Remove(blk.EntityId);
                res.Append("E;Planet gone");
                return;
            }

            int total = dl.Rows * dl.Cols;
            if (offset >= total) { res.Append("E;Done"); return; }
            if (offset + count > total) count = total - offset;

            var planet = dl.Planet;
            var pc = dl.Pc;
            double R = dl.MeanRadius;
            int cols = dl.Cols;
            int rows = dl.Rows;

            // Header: offset;count\n
            res.Append(offset).Append(';').Append(count).Append('\n');
            res.EnsureCapacity(res.Length + count);

            for (int i = 0; i < count; i++)
            {
                int idx = offset + i;
                int row = idx / cols;
                int col = idx % cols;

                // Lat/lon from grid cell (cell center)
                double lat = -Math.PI / 2.0 + (row + 0.5) * Math.PI / rows;
                double lon = -Math.PI + (col + 0.5) * 2.0 * Math.PI / cols;

                // World direction from planet center
                double cosLat = Math.Cos(lat);
                var dir = new Vector3D(
                    cosLat * Math.Cos(lon),
                    Math.Sin(lat),
                    cosLat * Math.Sin(lon));

                // Sample terrain surface
                var surf = planet.GetClosestSurfacePointGlobal(pc + dir * (R + 5000));
                double alt = (surf - pc).Length();
                int delta = (int)Math.Round(alt - R);
                res.Append((char)(Math.Max(-32000, Math.Min(32000, delta)) + HEIGHT_OFFSET));
            }
        }

        // ── R mode cache helpers ─────────────────────────────────────

        double SampleAlt(MyPlanet planet, Vector3D pc, Vector3D pos, SubInfo sub)
        {
            var surf = sub.Voxel
                ? SurfaceVoxel(planet, pos, sub.Lod)
                : planet.GetClosestSurfacePointGlobal(pos);
            return (surf - pc).Length();
        }

        void FullSampleR(SubInfo sub, MyPlanet planet, Vector3D pc,
            Vector3D right, Vector3D gfwd, Vector3D center)
        {
            int w = sub.W, h = sub.H;
            double cell = sub.Cell;
            double halfW = (w - 1) * 0.5;
            double halfH = (h - 1) * 0.5;
            var alts = sub.CachedAlts;

            for (int row = 0; row < h; row++)
            {
                double dy = (row - halfH) * cell;
                for (int col = 0; col < w; col++)
                {
                    double dx = (col - halfW) * cell;
                    alts[row * w + col] = SampleAlt(planet, pc,
                        center + right * dx + gfwd * dy, sub);
                }
            }
        }

        void ShiftAndSampleEdges(SubInfo sub, MyPlanet planet, Vector3D pc,
            Vector3D right, Vector3D gfwd, Vector3D center, int sx, int sy)
        {
            int w = sub.W, h = sub.H;
            double cell = sub.Cell;
            double halfW = (w - 1) * 0.5;
            double halfH = (h - 1) * 0.5;
            var src = sub.CachedAlts;
            var dst = sub.ShiftBuf;

            // Copy overlapping cells: new(col,row) ← old(col+sx, row+sy)
            for (int row = 0; row < h; row++)
            {
                int oldRow = row + sy;
                if (oldRow < 0 || oldRow >= h) continue;
                for (int col = 0; col < w; col++)
                {
                    int oldCol = col + sx;
                    if (oldCol < 0 || oldCol >= w) continue;
                    dst[row * w + col] = src[oldRow * w + oldCol];
                }
            }

            // Swap buffers — dst is now the working cache
            sub.CachedAlts = dst;
            sub.ShiftBuf = src;
            var alts = sub.CachedAlts;

            // Sample new columns exposed by horizontal shift
            int colStart, colEnd;
            if (sx > 0) { colStart = w - sx; colEnd = w; }
            else if (sx < 0) { colStart = 0; colEnd = -sx; }
            else { colStart = 0; colEnd = 0; }

            for (int row = 0; row < h; row++)
            {
                double dy = (row - halfH) * cell;
                for (int col = colStart; col < colEnd; col++)
                {
                    double dx = (col - halfW) * cell;
                    alts[row * w + col] = SampleAlt(planet, pc,
                        center + right * dx + gfwd * dy, sub);
                }
            }

            // Sample new rows exposed by vertical shift (excluding already-sampled columns)
            int rowStart, rowEnd;
            if (sy > 0) { rowStart = h - sy; rowEnd = h; }
            else if (sy < 0) { rowStart = 0; rowEnd = -sy; }
            else { rowStart = 0; rowEnd = 0; }

            int rColStart, rColEnd;
            if (sx > 0) { rColStart = 0; rColEnd = w - sx; }
            else if (sx < 0) { rColStart = -sx; rColEnd = w; }
            else { rColStart = 0; rColEnd = w; }

            for (int row = rowStart; row < rowEnd; row++)
            {
                double dy = (row - halfH) * cell;
                for (int col = rColStart; col < rColEnd; col++)
                {
                    double dx = (col - halfW) * cell;
                    alts[row * w + col] = SampleAlt(planet, pc,
                        center + right * dx + gfwd * dy, sub);
                }
            }
        }

        void EncodeR(StringBuilder res, SubInfo sub, MyPlanet planet, Vector3D pc,
            Vector3D center, Vector3D right, Vector3D gfwd)
        {
            var baseSurf = sub.Voxel
                ? SurfaceVoxel(planet, center, sub.Lod)
                : planet.GetClosestSurfacePointGlobal(center);
            double baseAlt = (baseSurf - pc).Length();

            // Header: baseAlt;pcX;pcY;pcZ;rX;rY;rZ;fX;fY;fZ + newline
            Ap(res, baseAlt); res.Append(';');
            Ap(res, pc.X); res.Append(';');
            Ap(res, pc.Y); res.Append(';');
            Ap(res, pc.Z); res.Append(';');
            Ap(res, right.X); res.Append(';');
            Ap(res, right.Y); res.Append(';');
            Ap(res, right.Z); res.Append(';');
            Ap(res, gfwd.X); res.Append(';');
            Ap(res, gfwd.Y); res.Append(';');
            Ap(res, gfwd.Z); res.Append('\n');

            // Height data from cache — encoding is cheap, sampling was the bottleneck
            int total = sub.W * sub.H;
            var alts = sub.CachedAlts;
            res.EnsureCapacity(res.Length + total);
            for (int i = 0; i < total; i++)
            {
                int delta = (int)Math.Round(alts[i] - baseAlt);
                res.Append((char)(Math.Max(-32000, Math.Min(32000, delta)) + HEIGHT_OFFSET));
            }
        }

        // ── Point query (synchronous) ───────────────────────────────

        void HandlePointQuery(StringBuilder res, string req)
        {
            var parts = req.Split(';');
            int idx = 0;
            bool vox = false;
            int lod = 0;

            if (parts[0] == "V")
            {
                vox = true;
                idx = 1;
                if (idx < parts.Length)
                {
                    int tryLod;
                    if (int.TryParse(parts[idx], out tryLod) && tryLod >= 0 && tryLod <= 10
                        && (parts.Length - idx - 1) % 3 == 0 && (parts.Length - idx - 1) >= 3)
                    {
                        lod = tryLod;
                        idx++;
                    }
                }
            }

            int remaining = parts.Length - idx;
            if (remaining < 3 || remaining % 3 != 0)
            {
                res.Append("E;Need x;y;z triples");
                return;
            }

            int count = remaining / 3;
            if (count > MAX_BATCH)
            {
                res.Append("E;Max ").Append(MAX_BATCH);
                return;
            }

            for (int i = idx; i + 2 < parts.Length; i += 3)
            {
                var pos = new Vector3D(Pd(parts[i]), Pd(parts[i + 1]), Pd(parts[i + 2]));
                var planet = Closest(pos);

                if (planet == null)
                {
                    if (i > idx) res.Append(';');
                    res.Append("NaN;NaN;NaN");
                    continue;
                }

                var surf = vox
                    ? SurfaceVoxel(planet, pos, lod)
                    : planet.GetClosestSurfacePointGlobal(pos);

                if (i > idx) res.Append(';');
                Ap(res, surf.X); res.Append(';');
                Ap(res, surf.Y); res.Append(';');
                Ap(res, surf.Z);
            }
        }

        // ── Heightmap request (synchronous, all data in one response) ──

        void HandleHeightmapRequest(StringBuilder res, string req)
        {
            // H;cx;cy;cz;fx;fy;fz;w;h;cell[;V[;lod]]
            var p = req.Split(';');
            if (p.Length < 10)
            {
                res.Append("E;Need H;cx;cy;cz;fx;fy;fz;w;h;cell");
                return;
            }

            var center = new Vector3D(Pd(p[1]), Pd(p[2]), Pd(p[3]));
            var fwd    = new Vector3D(Pd(p[4]), Pd(p[5]), Pd(p[6]));
            int w      = int.Parse(p[7]);
            int h      = int.Parse(p[8]);
            double cell = Pd(p[9]);
            bool vox   = p.Length > 10 && p[10] == "V";
            int lod    = p.Length > 11 ? int.Parse(p[11]) : 0;

            if (w < 1 || h < 1 || w > MAX_DIM || h > MAX_DIM)
            {
                res.Append("E;Dimensions 1-").Append(MAX_DIM);
                return;
            }
            if (cell < 0.5)
            {
                res.Append("E;Cell>=0.5m");
                return;
            }

            var planet = Closest(center);
            if (planet == null)
            {
                res.Append("E;NoPlanet");
                return;
            }

            var pc = planet.PositionComp.GetPosition();

            // Grid axes on horizontal plane
            var up = Vector3D.Normalize(center - pc);
            var right = Vector3D.Cross(fwd, up);
            if (right.LengthSquared() < 1e-6)
                right = Vector3D.Cross(Vector3D.Forward, up);
            right = Vector3D.Normalize(right);
            var gfwd = Vector3D.Cross(up, right);

            // Base altitude at center
            var baseSurf = vox
                ? SurfaceVoxel(planet, center, lod)
                : planet.GetClosestSurfacePointGlobal(center);
            double baseAlt = (baseSurf - pc).Length();

            // Compute all heights synchronously
            var heights = new short[w * h];
            double halfW = (w - 1) * 0.5;
            double halfH = (h - 1) * 0.5;

            for (int row = 0; row < h; row++)
            {
                double dy = (row - halfH) * cell;
                for (int col = 0; col < w; col++)
                {
                    double dx = (col - halfW) * cell;
                    var samplePos = center + right * dx + gfwd * dy;
                    var surf = vox
                        ? SurfaceVoxel(planet, samplePos, lod)
                        : planet.GetClosestSurfacePointGlobal(samplePos);
                    double alt = (surf - pc).Length();
                    int delta = (int)Math.Round(alt - baseAlt);
                    heights[row * w + col] = (short)Math.Max(-32000, Math.Min(32000, delta));
                }
            }

            // Write header: 13 semicolon-delimited fields with trailing semicolon
            res.Append(w).Append(';');
            res.Append(h).Append(';');
            Ap(res, cell); res.Append(';');
            Ap(res, baseAlt); res.Append(';');
            Ap(res, pc.X); res.Append(';');
            Ap(res, pc.Y); res.Append(';');
            Ap(res, pc.Z); res.Append(';');
            Ap(res, right.X); res.Append(';');
            Ap(res, right.Y); res.Append(';');
            Ap(res, right.Z); res.Append(';');
            Ap(res, gfwd.X); res.Append(';');
            Ap(res, gfwd.Y); res.Append(';');
            Ap(res, gfwd.Z); res.Append(';');

            // Append all height data as chars
            res.EnsureCapacity(res.Length + w * h);
            for (int i = 0; i < heights.Length; i++)
                res.Append((char)(heights[i] + HEIGHT_OFFSET));
        }

        // ── Voxel surface detection ─────────────────────────────────

        Vector3D SurfaceVoxel(MyPlanet planet, Vector3D worldPos, int lod)
        {
            var est = planet.GetClosestSurfacePointGlobal(worldPos);
            var pc  = planet.PositionComp.GetPosition();
            var dir = Vector3D.Normalize(worldPos - pc);
            double eDist = (est - pc).Length();

            var vb = planet as IMyVoxelBase;
            if (vb?.Storage == null || vb.Storage.Closed)
                return est;

            var storage = vb.Storage;
            var corner  = vb.PositionLeftBottomCorner;
            int step    = Math.Max(1, 1 << lod);

            byte atSurf    = ReadVoxel(storage, pc + dir * eDist, corner, lod);
            byte aboveSurf = ReadVoxel(storage, pc + dir * (eDist + step), corner, lod);

            if (atSurf > 127 && aboveSurf <= 127)
                return est;

            double d = eDist;
            if (atSurf <= 127)
            {
                for (int i = 0; i < 64; i++)
                {
                    d -= step;
                    if (ReadVoxel(storage, pc + dir * d, corner, lod) > 127)
                        return pc + dir * (d + step * 0.5);
                }
            }
            else
            {
                for (int i = 0; i < 64; i++)
                {
                    d += step;
                    if (ReadVoxel(storage, pc + dir * d, corner, lod) <= 127)
                        return pc + dir * (d - step * 0.5);
                }
            }

            return est;
        }

        byte ReadVoxel(IMyStorage storage, Vector3D wPos, Vector3D corner, int lod)
        {
            var loc = wPos - corner;
            int div = 1 << lod;
            var c = new Vector3I(
                (int)(loc.X / div),
                (int)(loc.Y / div),
                (int)(loc.Z / div));

            var max = new Vector3I(
                storage.Size.X / div - 1,
                storage.Size.Y / div - 1,
                storage.Size.Z / div - 1);

            c = Vector3I.Clamp(c, Vector3I.Zero, max);

            if (_vd == null)
            {
                _vd = new MyStorageData();
                _vd.Resize(Vector3I.One);
            }

            storage.ReadRange(_vd, MyStorageDataTypeFlags.Content, lod, c, c);
            var zero = Vector3I.Zero;
            return _vd.Content(ref zero);
        }

        // ── Helpers ─────────────────────────────────────────────────

        MyPlanet Closest(Vector3D pos)
        {
            RefreshPlanets();
            MyPlanet best = null;
            double bestDist = double.MaxValue;
            for (int i = 0; i < _planets.Count; i++)
            {
                double d = Vector3D.DistanceSquared(pos, _planets[i].PositionComp.GetPosition());
                if (d < bestDist) { bestDist = d; best = _planets[i]; }
            }
            return best;
        }

        void RefreshPlanets()
        {
            int tick = MyAPIGateway.Session?.GameplayFrameCounter ?? 0;
            if (tick - _ptick < 300 && _planets.Count > 0) return;

            _planets.Clear();
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is MyPlanet);
            foreach (var e in entities)
                _planets.Add((MyPlanet)e);
            _ptick = tick;
        }

        StringBuilder Buf(long key)
        {
            StringBuilder sb;
            if (!_res.TryGetValue(key, out sb))
            {
                sb = new StringBuilder(512);
                _res[key] = sb;
            }
            return sb;
        }

        static double Pd(string s) => double.Parse(s, Inv);
        static void Ap(StringBuilder sb, double v) => sb.Append(v.ToString("G8", Inv));

        public void Dispose()
        {
            _res.Clear();
            _subs.Clear();
            _downloads.Clear();
            _planets.Clear();
        }
    }
}
