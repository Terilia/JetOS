using System;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class TerrainData
        {
            enum S { IDLE, POLL, LOAD, READY, OFF }
            static S _s; static bool _p;
            static readonly StringBuilder _c = new StringBuilder(256);

            struct HM { public short[] h; public int w, ht; public double cs, ba;
                public Vector3D pc, rf, rr, og; }
            static HM _a, _n;
            static bool _hd;
            static int _gen; // increments on each swap — renderers detect & recompute

            static int _lr; const int CK = 3;
            public const int MW = 400, MH = 400, MC = 50;

            public static bool Ready => _hd;
            public static bool Loading => _s == S.POLL || _s == S.LOAD;
            public static bool Available => _s != S.OFF;
            public static double CellSize => _a.cs;
            public static Vector3D GridFwd => _a.rf;
            public static Vector3D GridRight => _a.rr;
            public static int Gen => _gen;

            public static void Probe(IMyProgrammableBlock me)
            { if (_p) return; _p = true; if (me.GetProperty("TerrainAPI") == null) _s = S.OFF; }

            public static void Request(IMyProgrammableBlock me, Vector3D center, Vector3D fwd)
            {
                if (_s == S.OFF) return; if (!_p) Probe(me); if (_s == S.OFF) return;
                _c.Clear(); _c.Append("H;")
                    .Append(center.X).Append(';').Append(center.Y).Append(';').Append(center.Z).Append(';')
                    .Append(fwd.X).Append(';').Append(fwd.Y).Append(';').Append(fwd.Z).Append(';')
                    .Append(MW).Append(';').Append(MH).Append(';').Append(MC);
                try { me.SetValue<StringBuilder>("TerrainAPI", _c);
                    _s = S.POLL; _n.og = center;
                } catch { _s = S.OFF; }
            }

            public static void Tick(IMyProgrammableBlock me)
            { if (_s == S.POLL) Poll(me); else if (_s == S.LOAD) Load(me); }

            static void Poll(IMyProgrammableBlock me)
            {
                _c.Clear(); _c.Append('S');
                try { me.SetValue<StringBuilder>("TerrainAPI", _c);
                    var r = me.GetValue<StringBuilder>("TerrainAPI");
                    if (r == null || r.Length < 6) return; string s = r.ToString();
                    if (s[0] != 'S' || s[1] != ';' || s[2] != 'R') return;
                    string[] p = s.Split(';'); if (p.Length < 15) return;
                    _n.w = int.Parse(p[2]); _n.ht = int.Parse(p[3]);
                    _n.cs = double.Parse(p[4]); _n.ba = double.Parse(p[5]);
                    _n.pc = new Vector3D(double.Parse(p[6]), double.Parse(p[7]), double.Parse(p[8]));
                    _n.rr = new Vector3D(double.Parse(p[9]), double.Parse(p[10]), double.Parse(p[11]));
                    _n.rf = new Vector3D(double.Parse(p[12]), double.Parse(p[13]), double.Parse(p[14]));
                    int t = _n.w * _n.ht;
                    if (_n.h == null || _n.h.Length < t) _n.h = new short[t];
                    _lr = 0; _s = S.LOAD; } catch { }
            }

            static void Load(IMyProgrammableBlock me)
            {
                int n = Mn(CK, _n.ht - _lr); if (n <= 0) { Swap(); return; }
                _c.Clear(); _c.Append("C;").Append(_lr).Append(';').Append(n);
                try { me.SetValue<StringBuilder>("TerrainAPI", _c);
                    var r = me.GetValue<StringBuilder>("TerrainAPI");
                    if (r == null || r.Length == 0) return; string d = r.ToString();
                    int b = _lr * _n.w, cnt = Mn(d.Length, n * _n.w);
                    for (int i = 0; i < cnt; i++) _n.h[b + i] = (short)((int)d[i] - 32768);
                    _lr += n; if (_lr >= _n.ht) Swap();
                } catch { }
            }

            static void Swap() { _a = _n; _hd = true; _gen++; _s = S.READY; }

            /// <summary>Predictive refresh: checks where ship WILL be in 15s.</summary>
            public static bool NeedsRefresh(Vector3D pos, Vector3D vel)
            {
                if (_s == S.OFF) return false;
                if (!_hd && _s == S.IDLE) return true;
                if (_s != S.READY) return false;
                // Check predicted position 15 seconds ahead
                Vector3D future = pos + vel * 15;
                double edge = MW * MC * 0.35; // trigger at 35% of coverage
                return (future - _a.og).LengthSquared() > edge * edge;
            }

            public static bool W2G(Vector3D wp, out int row, out int col)
            { Vector3D o = wp - _a.og; col = (int)(VD(o, _a.rr) / _a.cs + _a.w * 0.5);
                row = (int)(VD(o, _a.rf) / _a.cs + _a.ht * 0.5);
                return col >= 0 && col < _a.w && row >= 0 && row < _a.ht; }

            public static double Surf(int r, int c)
            { return (r < 0 || r >= _a.ht || c < 0 || c >= _a.w) ? _a.ba : _a.ba + _a.h[r * _a.w + c]; }

            public static double Alt(Vector3D wp) { return (wp - _a.pc).Length(); }

            public static double AGL(Vector3D wp)
            { int r, c; return W2G(wp, out r, out c) ? Alt(wp) - Surf(r, c) : double.MaxValue; }
        }
    }
}
