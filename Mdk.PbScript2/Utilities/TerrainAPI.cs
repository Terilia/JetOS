using System;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class TerrainAPI
        {
            // ── Async state machine ──
            // IDLE → POLLING → LOADING → READY  (any → UNAVAILABLE on fatal error)
            enum LoadState { IDLE, POLLING, LOADING, READY, UNAVAILABLE }
            static LoadState _state = LoadState.IDLE;
            static bool _probed;
            static readonly StringBuilder _cmd = new StringBuilder(256);

            // ── Heightmap cache ──
            static short[] _heights;
            static int _w, _h;
            static double _cellSize;
            static double _baseAlt;
            static Vector3D _planetCenter;
            static Vector3D _right;    // grid +col axis in world space
            static Vector3D _forward;  // grid +row axis in world space
            static Vector3D _origin;   // world position the grid is centered on

            // ── Loading progress ──
            static int _loadRow;
            // Rows per tick during chunk loading.
            // 10 rows × 200 cols = 2000 chars ≈ 20K instructions — safe under 50K limit.
            const int CHUNK_ROWS = 10;

            // ── Request parameters ──
            public const int MAP_W = 200;
            public const int MAP_H = 200;
            public const int CELL_M = 50;  // meters per cell → 200×50 = 10 km coverage

            // Re-request when ship moves > 30% of map coverage from origin
            static double _refreshDistSq;

            // ── Public accessors ──
            public static bool IsReady => _state == LoadState.READY;
            public static bool IsLoading => _state == LoadState.POLLING || _state == LoadState.LOADING;
            public static bool IsAvailable => _state != LoadState.UNAVAILABLE;
            public static int Width => _w;
            public static int Height => _h;
            public static double CellSize => _cellSize;
            public static double BaseAltitude => _baseAlt;
            public static Vector3D PlanetCenter => _planetCenter;
            public static Vector3D GridForward => _forward;
            public static Vector3D GridRight => _right;
            internal static string DebugStatus = "not probed";

            // ── One-time probe for plugin presence ──
            public static void Probe(IMyProgrammableBlock me)
            {
                if (_probed) return;
                _probed = true;
                var prop = me.GetProperty("TerrainAPI");
                if (prop == null)
                {
                    _state = LoadState.UNAVAILABLE;
                    DebugStatus = "plugin not found";
                }
                else
                {
                    DebugStatus = "plugin found, idle";
                }
            }

            // ── Request a heightmap (non-blocking, mod computes async) ──
            public static void Request(IMyProgrammableBlock me, Vector3D pos, Vector3D fwd)
            {
                if (_state == LoadState.UNAVAILABLE) return;
                if (!_probed) Probe(me);
                if (_state == LoadState.UNAVAILABLE) return;

                _cmd.Clear();
                _cmd.Append("H;")
                    .Append(pos.X).Append(';').Append(pos.Y).Append(';').Append(pos.Z).Append(';')
                    .Append(fwd.X).Append(';').Append(fwd.Y).Append(';').Append(fwd.Z).Append(';')
                    .Append(MAP_W).Append(';').Append(MAP_H).Append(';').Append(CELL_M);

                try
                {
                    me.SetValue<StringBuilder>("TerrainAPI", _cmd);
                    // Response is "H;OK" — we don't need to read it, just start polling
                    _state = LoadState.POLLING;
                    _origin = pos;
                    double coverage = MAP_W * CELL_M * 0.3;
                    _refreshDistSq = coverage * coverage;
                    DebugStatus = "requested, polling";
                }
                catch
                {
                    _state = LoadState.UNAVAILABLE;
                    DebugStatus = "request failed";
                }
            }

            // ── Tick: call once per game tick to drive async loading ──
            public static void Tick(IMyProgrammableBlock me)
            {
                if (_state == LoadState.POLLING)
                    TickPoll(me);
                else if (_state == LoadState.LOADING)
                    TickLoad(me);
            }

            static void TickPoll(IMyProgrammableBlock me)
            {
                _cmd.Clear();
                _cmd.Append('S');
                try
                {
                    me.SetValue<StringBuilder>("TerrainAPI", _cmd);
                    var resp = me.GetValue<StringBuilder>("TerrainAPI");
                    if (resp == null || resp.Length == 0) return;

                    string s = resp.ToString();
                    // Quick format check: "S;BUSY;..." or "S;READY;..."
                    if (s.Length < 6 || s[0] != 'S' || s[1] != ';') return;

                    if (s[2] == 'B') // BUSY
                    {
                        DebugStatus = "computing " + s.Substring(7);
                        return;
                    }

                    if (s[2] != 'R') return; // not READY

                    // "S;READY;w;h;cell;baseAlt;pcX;pcY;pcZ;rX;rY;rZ;fX;fY;fZ"
                    string[] p = s.Split(';');
                    if (p.Length < 15) return;

                    _w = int.Parse(p[2]);
                    _h = int.Parse(p[3]);
                    _cellSize = double.Parse(p[4]);
                    _baseAlt = double.Parse(p[5]);
                    _planetCenter = new Vector3D(double.Parse(p[6]), double.Parse(p[7]), double.Parse(p[8]));
                    _right = new Vector3D(double.Parse(p[9]), double.Parse(p[10]), double.Parse(p[11]));
                    _forward = new Vector3D(double.Parse(p[12]), double.Parse(p[13]), double.Parse(p[14]));

                    int total = _w * _h;
                    if (_heights == null || _heights.Length < total)
                        _heights = new short[total];

                    _loadRow = 0;
                    _state = LoadState.LOADING;
                    DebugStatus = $"loading 0/{_h}";
                }
                catch { DebugStatus = "poll error"; }
            }

            static void TickLoad(IMyProgrammableBlock me)
            {
                int rows = Math.Min(CHUNK_ROWS, _h - _loadRow);
                if (rows <= 0) { _state = LoadState.READY; DebugStatus = $"ready {_w}x{_h}"; return; }

                _cmd.Clear();
                _cmd.Append("C;").Append(_loadRow).Append(';').Append(rows);

                try
                {
                    me.SetValue<StringBuilder>("TerrainAPI", _cmd);
                    var resp = me.GetValue<StringBuilder>("TerrainAPI");
                    if (resp == null || resp.Length == 0) return;

                    string data = resp.ToString();
                    int baseIdx = _loadRow * _w;
                    int count = Math.Min(data.Length, rows * _w);
                    for (int i = 0; i < count; i++)
                        _heights[baseIdx + i] = (short)((int)data[i] - 32768);

                    _loadRow += rows;
                    if (_loadRow >= _h)
                    {
                        _state = LoadState.READY;
                        DebugStatus = $"ready {_w}x{_h} base={_baseAlt:F0}";
                    }
                    else
                        DebugStatus = $"loading {_loadRow}/{_h}";
                }
                catch { DebugStatus = "chunk error"; }
            }

            // ── Check if ship moved enough to warrant a new request ──
            public static bool NeedsRefresh(Vector3D pos)
            {
                if (_state == LoadState.UNAVAILABLE) return false;
                if (_state == LoadState.IDLE) return true;
                if (_state == LoadState.READY)
                    return (pos - _origin).LengthSquared() > _refreshDistSq;
                return false; // still loading, don't re-request
            }

            // ── World position → grid indices ──
            public static bool WorldToGrid(Vector3D worldPos, out int row, out int col)
            {
                Vector3D offset = worldPos - _origin;
                col = (int)(VD(offset, _right) / _cellSize + _w * 0.5);
                row = (int)(VD(offset, _forward) / _cellSize + _h * 0.5);
                return col >= 0 && col < _w && row >= 0 && row < _h;
            }

            // ── Surface altitude at grid cell (returns baseAlt for out-of-bounds) ──
            public static double SurfaceAlt(int row, int col)
            {
                if (row < 0 || row >= _h || col < 0 || col >= _w) return _baseAlt;
                return _baseAlt + _heights[row * _w + col];
            }

            // ── AGL at world position from cached heightmap ──
            public static double AGL(Vector3D worldPos)
            {
                int row, col;
                if (!WorldToGrid(worldPos, out row, out col)) return double.MaxValue;
                double myAlt = (worldPos - _planetCenter).Length();
                return myAlt - SurfaceAlt(row, col);
            }

            // ── Ship altitude (distance from planet center) ──
            public static double ShipAlt(Vector3D worldPos)
            {
                return (worldPos - _planetCenter).Length();
            }

            // ── Quick synchronous single-point surface query ──
            public static Vector3D? QueryPoint(IMyProgrammableBlock me, Vector3D pos)
            {
                if (_state == LoadState.UNAVAILABLE) return null;
                if (!_probed) Probe(me);
                if (_state == LoadState.UNAVAILABLE) return null;

                _cmd.Clear();
                _cmd.Append(pos.X).Append(';').Append(pos.Y).Append(';').Append(pos.Z);
                try
                {
                    me.SetValue<StringBuilder>("TerrainAPI", _cmd);
                    var resp = me.GetValue<StringBuilder>("TerrainAPI");
                    if (resp == null || resp.Length == 0) return null;
                    string[] p = resp.ToString().Split(';');
                    if (p.Length < 3) return null;
                    return new Vector3D(double.Parse(p[0]), double.Parse(p[1]), double.Parse(p[2]));
                }
                catch { return null; }
            }

            public static void Reset()
            {
                _state = LoadState.IDLE;
                _probed = false;
                DebugStatus = "reset";
            }
        }
    }
}
