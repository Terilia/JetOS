using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class AeroRecorderModule : ProgramModule
        {
            Jet jet;
            const int BUF = 1200, SKIP = 3;
            // Percentile metrics: 0=V(m/s) 1=H(m ASL) 2=P(d/s) 3=Y(d/s) 4=R(d/s) 5=G
            const int MC = 6;
            // Speed bins: 50 m/s wide, 12 bins covering 0-600 m/s
            const int BINS = 12;
            const float BW = 50f;

            float[][] _b;
            int _bi, _bn, _tk;
            bool _rec;
            float _mass;
            Vector3D _pv;
            bool _hp;

            // Speed-binned accumulators: drag/lift/AoA sums + count
            double[] _dS, _lS, _aS;
            int[] _bC;

            // Dump: -1=idle, 0..MC-1=sorting, MC=echo
            int _dp;
            float[] _st;
            float[,] _pc; // [metric, 5] = min/p50/p99/p999/max

            List<IMyThrust> _th;
            long _tht;

            public bool IsActive => _rec || _dp >= 0;

            public AeroRecorderModule(Program p, Jet j) : base(p)
            {
                jet = j;
                name = "Aero Recorder";
                _b = new float[MC][];
                for (int i = 0; i < MC; i++) _b[i] = new float[BUF];
                _pc = new float[MC, 5];
                _st = new float[BUF];
                _dS = new double[BINS]; _lS = new double[BINS];
                _aS = new double[BINS]; _bC = new int[BINS];
                _th = new List<IMyThrust>();
                _dp = -1;
            }

            public override string[] GetOptions() => new string[]
            {
                _rec ? "Stop Recording (" + _bn + ")" : "Start Recording",
                _dp >= 0 ? "Dumping..." : "Dump Data",
                "Reset",
                "Back to Main Menu"
            };

            public override void ExecuteOption(int i)
            {
                switch (i)
                {
                    case 0:
                        _rec = !_rec;
                        if (_rec) DoReset();
                        break;
                    case 1:
                        if (_bn > 0 && _dp < 0) { _rec = false; _dp = 0; }
                        break;
                    case 2:
                        _rec = false; DoReset(); _dp = -1;
                        break;
                    case 3:
                        SystemManager.ReturnToMainMenu();
                        break;
                }
            }

            void DoReset()
            {
                _bi = 0; _bn = 0; _hp = false; _tk = 0;
                for (int i = 0; i < BINS; i++)
                { _dS[i] = 0; _lS[i] = 0; _aS[i] = 0; _bC[i] = 0; }
            }

            public override void Tick()
            {
                if (_dp >= 0 && _dp < MC) { SortOne(_dp); _dp++; return; }
                if (_dp == MC) { Emit(); _dp = -1; return; }
                if (!_rec || jet._cockpit == null) return;
                if (++_tk % SKIP != 0) return;
                Record();
            }

            void Record()
            {
                var ck = jet._cockpit;
                var sv = ck.GetShipVelocities();
                Vector3D v = sv.LinearVelocity, w = sv.AngularVelocity;
                double spd = v.Length();
                if (spd < 1) { _hp = true; _pv = v; return; }

                _mass = ck.CalculateShipMass().TotalMass;
                MatrixD wm = ck.WorldMatrix;

                // Angular rates in local frame (deg/s)
                double pr = VD(w, wm.Right) * 57.2958;
                double yr = VD(w, wm.Up) * 57.2958;
                double rr = VD(w, wm.Forward) * 57.2958;

                // AoA from local velocity
                double lvY = VD(v, wm.Up);
                double lvZ = VD(v, wm.Forward);
                double aoa = At2(-lvY, lvZ) * 57.2958;

                // Altitude ASL (above mean sea level)
                double alt = 0;
                if (TerrainData.Ready)
                    alt = TerrainData.Alt(ck.GetPosition()) - TerrainData.MeanR;

                // Aero force decomposition (needs previous velocity)
                float df = 0, lf = 0, gf = 0;
                if (_hp)
                {
                    double dt = SKIP / 60.0;
                    Vector3D acc = (v - _pv) / dt;
                    Vector3D grav = ck.GetNaturalGravity();

                    // Refresh thruster list every 2 seconds
                    if (_tht == 0 || Jet.GameTicks - _tht > 120)
                    {
                        _tht = Jet.GameTicks;
                        _th.Clear();
                        ParentProgram.GridTerminalSystem.GetBlocksOfType(_th,
                            t => t.CubeGrid == ck.CubeGrid);
                    }

                    // Sum all active thrust
                    Vector3D thr = Vector3D.Zero;
                    for (int i = 0; i < _th.Count; i++)
                    {
                        var t = _th[i];
                        if (t.IsWorking && t.CurrentThrust > 0)
                            thr += t.WorldMatrix.Backward * t.CurrentThrust;
                    }

                    // aeroForce = mass * (accel - gravity) - thrust
                    Vector3D aF = (acc - grav) * _mass - thr;
                    Vector3D vn = v / spd;
                    double adv = VD(aF, vn);
                    df = (float)(-adv); // drag: positive = decelerating
                    lf = (float)(aF - adv * vn).Length(); // lift: perpendicular
                    gf = (float)((acc - grav).Length() / 9.81);

                    // Accumulate into speed bin
                    int bin = (int)(spd / BW);
                    if (bin >= BINS) bin = BINS - 1;
                    _dS[bin] += df;
                    _lS[bin] += lf;
                    _aS[bin] += aoa;
                    _bC[bin]++;
                }

                _pv = v; _hp = true;

                // Percentile buffer: V, H, P, Y, R, G
                int bi = _bi;
                _b[0][bi] = (float)spd;
                _b[1][bi] = (float)alt;
                _b[2][bi] = (float)pr;
                _b[3][bi] = (float)yr;
                _b[4][bi] = (float)rr;
                _b[5][bi] = gf;
                _bi = (_bi + 1) % BUF;
                if (_bn < BUF) _bn++;
            }

            void SortOne(int m)
            {
                int s = _bn < BUF ? 0 : _bi;
                for (int i = 0; i < _bn; i++) _st[i] = _b[m][(s + i) % BUF];
                Array.Sort(_st, 0, _bn);
                _pc[m, 0] = _st[0];
                _pc[m, 1] = _st[_bn / 2];
                _pc[m, 2] = _st[Mn((int)(_bn * 0.99), _bn - 1)];
                _pc[m, 3] = _st[Mn((int)(_bn * 0.999), _bn - 1)];
                _pc[m, 4] = _st[_bn - 1];
            }

            void Emit()
            {
                // Header: N=samples M=mass(kg) BW=bin width(m/s) T=seconds
                // Percentiles: V=speed(m/s) H=alt ASL(m) P/Y/R=rates(d/s) G=gforce
                // Bins: BN=count BD=avg drag(kN) BL=avg lift(kN) BA=avg AoA(deg)
                string[] L = { "V", "H", "P", "Y", "R", "G" };
                var sb = new StringBuilder(500);
                sb.Append("AERO N").Append(_bn)
                  .Append(" M").Append((int)_mass)
                  .Append(" BW").Append((int)BW)
                  .Append(" T").Append(_bn * SKIP / 60).Append('\n');

                for (int m = 0; m < MC; m++)
                {
                    sb.Append(L[m]);
                    for (int p = 0; p < 5; p++)
                    {
                        sb.Append(p == 0 ? ' ' : '/');
                        float val = _pc[m, p];
                        if (m <= 1) sb.Append((int)val);
                        else if (m <= 4) sb.Append((int)val);
                        else sb.Append(val.ToString("F1"));
                    }
                    sb.Append('\n');
                }

                // Speed-binned averages
                sb.Append("BN");
                for (int i = 0; i < BINS; i++)
                    sb.Append(i == 0 ? ' ' : '/').Append(_bC[i]);
                sb.Append('\n');

                sb.Append("BD");
                for (int i = 0; i < BINS; i++)
                {
                    sb.Append(i == 0 ? ' ' : '/');
                    sb.Append(_bC[i] > 0 ? (_dS[i] / _bC[i] / 1000).ToString("F1") : "-");
                }
                sb.Append('\n');

                sb.Append("BL");
                for (int i = 0; i < BINS; i++)
                {
                    sb.Append(i == 0 ? ' ' : '/');
                    sb.Append(_bC[i] > 0 ? (_lS[i] / _bC[i] / 1000).ToString("F1") : "-");
                }
                sb.Append('\n');

                sb.Append("BA");
                for (int i = 0; i < BINS; i++)
                {
                    sb.Append(i == 0 ? ' ' : '/');
                    sb.Append(_bC[i] > 0 ? ((int)(_aS[i] / _bC[i])).ToString() : "-");
                }

                ParentProgram.Echo(sb.ToString());
                _hp = false;
            }
        }
    }
}
