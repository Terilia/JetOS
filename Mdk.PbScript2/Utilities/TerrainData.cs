using System;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        /// <summary>
        /// Downloads entire planet heightmap on compile via TerrainAPI,
        /// then provides instant offline lookups forever.
        /// Protocol: P;cellSize → grid info, C;offset;count → height chunks.
        /// </summary>
        static class TerrainData
        {
            const int CHUNK = 5000;
            const int HOFF = 32768;
            const double DEFAULT_CELL = 200;

            static readonly System.Globalization.NumberFormatInfo _nfi =
                new System.Globalization.NumberFormatInfo
                { NumberDecimalSeparator = ".", NumberGroupSeparator = "" };

            // State
            static bool _probed, _off;
            static StringBuilder _sb;
            static readonly StringBuilder _cmd = new StringBuilder(64);

            // Planet grid
            static short[] _grid;
            static int _rows, _cols, _total;
            static double _meanR, _cellSize;
            static Vector3D _pc;
            static int _offset;
            static bool _ready, _downloading;
            // Tangent vectors (recomputed each tick when ready)
            static Vector3D _gridFwd, _gridRight;

            // Public API
            public static bool Available => !_off;
            public static bool Ready => _ready;
            public static bool Loading => _downloading;
            public static double CellSize => _cellSize > 0 ? _cellSize : DEFAULT_CELL;
            public static double MeanR => _meanR;
            public static Vector3D GridFwd => _gridFwd;
            public static Vector3D GridRight => _gridRight;
            public static float DownloadProgress => _total > 0 ? (float)_offset / _total : 0f;

            public static void Probe(IMyProgrammableBlock me)
            {
                if (_probed) return;
                _probed = true;
                if (me.GetProperty(TERRAIN_API) == null) _off = true;
            }

            /// <summary>
            /// Sends P;cellSize to the plugin, parses grid dimensions,
            /// allocates the height array, and starts the download.
            /// </summary>
            public static void Init(IMyProgrammableBlock me)
            {
                if (_off) return;
                if (!_probed) { Probe(me); if (_off) return; }

                _cmd.Clear();
                _cmd.Append("P;").Append(DEFAULT_CELL);
                me.SetValue<StringBuilder>(TERRAIN_API, _cmd);
                _sb = me.GetValue<StringBuilder>(TERRAIN_API);
                if (_sb == null) return;

                string resp = _sb.ToString();
                if (resp.Length < 3 || resp[0] == 'E') return;

                // Parse: P;rows;cols;cellSize;meanRadius;pcX;pcY;pcZ
                string[] p = resp.Split(';');
                if (p.Length < 8) return;

                try
                {
                    _rows = int.Parse(p[1]);
                    _cols = int.Parse(p[2]);
                    _cellSize = double.Parse(p[3], _nfi);
                    _meanR = double.Parse(p[4], _nfi);
                    _pc = new Vector3D(
                        double.Parse(p[5], _nfi),
                        double.Parse(p[6], _nfi),
                        double.Parse(p[7], _nfi));
                }
                catch { return; }

                _total = _rows * _cols;
                _grid = new short[_total];
                _offset = 0;
                _downloading = true;
            }

            public static void Tick(IMyProgrammableBlock me, Vector3D shipPos)
            {
                if (_off) return;

                if (_downloading && _grid != null)
                {
                    DownloadChunk(me);
                    return;
                }

                if (_ready)
                    UpdateTangents(shipPos);
            }

            static void DownloadChunk(IMyProgrammableBlock me)
            {
                _cmd.Clear();
                _cmd.Append("C;").Append(_offset).Append(';').Append(CHUNK);
                me.SetValue<StringBuilder>(TERRAIN_API, _cmd);
                // Re-fetch response SB — plugin may replace it after SetValue
                _sb = me.GetValue<StringBuilder>(TERRAIN_API);
                if (_sb == null || _sb.Length < 2) return;

                string resp = _sb.ToString();
                int nl = resp.IndexOf('\n');
                if (nl < 0) return;

                int avail = resp.Length - nl - 1;
                int count = Mn(avail, _total - _offset);
                for (int i = 0; i < count; i++)
                    _grid[_offset + i] = (short)((int)resp[nl + 1 + i] - HOFF);

                _offset += count;
                if (_offset >= _total)
                {
                    _ready = true;
                    _downloading = false;
                }
            }

            /// <summary>
            /// Computes north/east tangent vectors at ship position for
            /// FillCl grid-to-world projection. East axis is scaled to
            /// compensate for equirectangular longitude distortion.
            /// </summary>
            static void UpdateTangents(Vector3D shipPos)
            {
                Vector3D dir = VN(shipPos - _pc);
                double lat = As(dir.Y);
                double lon = At2(dir.Z, dir.X);

                double sinLat = Sn(lat), cosLat = Cs(lat);
                double sinLon = Sn(lon), cosLon = Cs(lon);

                // North (row+): unit tangent along latitude increase
                _gridFwd = new Vector3D(-sinLat * cosLon, cosLat, -sinLat * sinLon);

                // East (col+): scaled for equirectangular distortion
                // At equator colScale=1, at higher latitudes colScale>1
                // so FillCl steps more cols to cover the same physical distance
                double colScale = cosLat > 0.01
                    ? (double)_cols / (2.0 * _rows * cosLat) : 1.0;
                _gridRight = new Vector3D(-sinLon * colScale, 0, cosLon * colScale);
            }

            // ── Lookup API ──

            /// <summary>
            /// Converts world position to planet grid row/col via lat/lon.
            /// Always returns true (planet-wide grid covers everything).
            /// </summary>
            public static bool W2G(Vector3D wp, out int row, out int col)
            {
                Vector3D dir = VN(wp - _pc);
                double lat = As(dir.Y);
                double lon = At2(dir.Z, dir.X);

                row = (int)((lat / PI + 0.5) * _rows);
                col = (int)((lon / (2.0 * PI) + 0.5) * _cols);

                if (row < 0) row = 0; else if (row >= _rows) row = _rows - 1;
                col = ((col % _cols) + _cols) % _cols;
                return true;
            }

            /// <summary>
            /// W2G with fractional sub-cell position for smooth contour scrolling.
            /// fracR/fracC are 0..1 offsets within the cell.
            /// </summary>
            public static void W2GF(Vector3D wp, out int row, out int col, out double fracR, out double fracC)
            {
                Vector3D dir = VN(wp - _pc);
                double lat = As(dir.Y);
                double lon = At2(dir.Z, dir.X);

                double er = (lat / PI + 0.5) * _rows;
                double ec = (lon / (2.0 * PI) + 0.5) * _cols;

                row = (int)er; if (er < row) row--;
                col = (int)ec; if (ec < col) col--;
                fracR = er - row;
                fracC = ec - col;

                if (row < 0) { row = 0; fracR = 0; }
                else if (row >= _rows) { row = _rows - 1; fracR = 0; }
                col = ((col % _cols) + _cols) % _cols;
            }

            /// <summary>Surface radius at grid cell (clamped row, wrapped col).</summary>
            public static double Surf(int r, int c)
            {
                if (r < 0) r = 0; else if (r >= _rows) r = _rows - 1;
                c = ((c % _cols) + _cols) % _cols;
                return _meanR + _grid[r * _cols + c];
            }

            public static double Alt(Vector3D wp) { return (wp - _pc).Length(); }

            public static double AGL(Vector3D wp)
            {
                int r, c;
                W2G(wp, out r, out c);
                return Alt(wp) - Surf(r, c);
            }
        }
    }
}
