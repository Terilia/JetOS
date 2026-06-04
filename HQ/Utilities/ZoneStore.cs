using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public enum ZoneShape { Polygon = 0, Circle = 1 }
        public enum ZoneKind  { Enemy = 0, NoFly = 1, SAM = 2, CAP = 3, Rally = 4 }

        // An operator-drawn named region. Polygons hold their world vertices; circles hold a single
        // center vertex + an explicit Radius. Center/Radius are derived for polygons (centroid +
        // bounding radius) so a minimal consumer can treat any zone as a named circle.
        public class Zone
        {
            public int Id;
            public ZoneShape Shape;
            public ZoneKind Kind;
            public string Name = "";
            public readonly List<Vector3D> Verts = new List<Vector3D>();
            public Vector3D Center;   // derived (polygon) or = Verts[0] (circle)
            public double Radius;     // derived bounding (polygon) or explicit (circle)

            public void Recompute()
            {
                if (Verts.Count == 0) { Center = VZ; if (Shape != ZoneShape.Circle) Radius = 0; return; }
                if (Shape == ZoneShape.Circle) { Center = Verts[0]; return; } // Radius kept explicit
                Vector3D sum = VZ;
                for (int i = 0; i < Verts.Count; i++) sum += Verts[i];
                Center = sum / Verts.Count;
                double r = 0;
                for (int i = 0; i < Verts.Count; i++) { double d = VDi(Verts[i], Center); if (d > r) r = d; }
                Radius = r;
            }
        }

        // The authoritative zone list, persisted in PB Storage (via StorageDoc, key prefix "z.").
        // World coords are stored as whole-meter longs — plenty for tactical zones, and free of any
        // culture/decimal-separator pitfalls.
        static class ZoneStore
        {
            public const int MAX_ZONES = 32;
            public const int MAX_VERTS = 16;

            public static readonly List<Zone> Zones = new List<Zone>();
            // Ids of zones deleted since the last broadcast — DatalinkHQ drains these as wire
            // "tombstones" so plot-capable jets drop them promptly (the #ZONES name list updates
            // on its own).
            public static readonly List<int> Removed = new List<int>();
            static int _nextId = 1;
            static Program _p;

            public static bool Full => Zones.Count >= MAX_ZONES;

            // Give a drafted zone an id + default name and commit it.
            public static bool Add(Zone z)
            {
                if (z == null || Zones.Count >= MAX_ZONES) return false;
                z.Id = _nextId++;
                if (SE(z.Name)) z.Name = "ZONE " + z.Id;
                z.Recompute();
                Zones.Add(z);
                Persist();
                return true;
            }

            public static void Remove(Zone z)
            {
                if (z != null && Zones.Remove(z)) { Removed.Add(z.Id); Persist(); }
            }

            public static Zone ById(int id)
            {
                for (int i = 0; i < Zones.Count; i++) if (Zones[i].Id == id) return Zones[i];
                return null;
            }

            public static Zone ByName(string name)
            {
                for (int i = 0; i < Zones.Count; i++) if (Zones[i].Name == name) return Zones[i];
                return null;
            }

            // Apply operator renames from the CustomData "===ZONES===" block. Each line is
            // "<id>=<name>" or "<oldname>=<name>". Returns true (and re-persists) if anything changed.
            public static bool ApplyRenames(string block)
            {
                if (SW(block)) return false;
                string[] lines = block.Split('\n');
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq <= 0) continue;
                    string left = lines[i].Substring(0, eq).Trim();
                    string nm = San(lines[i].Substring(eq + 1).Trim());
                    if (SE(nm)) continue;
                    Zone z;
                    int id;
                    if (int.TryParse(left, out id)) z = ById(id); else z = ByName(left);
                    if (z != null && z.Name != nm) { z.Name = nm; changed = true; }
                }
                if (changed) Persist();
                return changed;
            }

            // ── Persistence ──
            public static void Load(Program p)
            {
                _p = p;
                Zones.Clear();
                string v;
                if (StorageDoc.TryGet("z._n", out v)) { int n; if (int.TryParse(v, out n)) _nextId = n; }

                var keys = StorageDoc.KeysWithPrefix("z.");
                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i] == "z._n") continue;
                    string raw;
                    if (!StorageDoc.TryGet(keys[i], out raw)) continue;
                    Zone z = Deserialize(keys[i], raw);
                    if (z != null) Zones.Add(z);
                }
            }

            public static void Persist()
            {
                var old = StorageDoc.KeysWithPrefix("z.");
                for (int i = 0; i < old.Count; i++) StorageDoc.Remove(old[i]);
                StorageDoc.Set("z._n", _nextId.ToString());
                for (int i = 0; i < Zones.Count; i++)
                    StorageDoc.Set("z." + Zones[i].Id, Serialize(Zones[i]));
                StorageDoc.Flush();
            }

            // "<shapeKind>;<name>;<geom>[;<radius>]"  — geom = v|v|v (poly) or single v (circle).
            static string Serialize(Zone z)
            {
                int sk = (int)z.Shape * 16 + (int)z.Kind;
                var sb = new StringBuilder();
                sb.Append(sk).Append(';').Append(San(z.Name)).Append(';');
                if (z.Shape == ZoneShape.Circle)
                {
                    AppendV(sb, z.Verts.Count > 0 ? z.Verts[0] : z.Center);
                    sb.Append(';').Append((long)Rd(z.Radius));
                }
                else
                {
                    for (int i = 0; i < z.Verts.Count; i++)
                    {
                        if (i > 0) sb.Append('|');
                        AppendV(sb, z.Verts[i]);
                    }
                }
                return sb.ToString();
            }

            static Zone Deserialize(string key, string raw)
            {
                int id;
                if (!int.TryParse(key.Substring(2), out id)) return null; // "z.<id>"
                string[] p = raw.Split(';');
                if (p.Length < 3) return null;
                int sk;
                if (!int.TryParse(p[0], out sk)) return null;

                Zone z = new Zone();
                z.Id = id;
                z.Shape = (ZoneShape)(sk / 16);
                z.Kind = (ZoneKind)(sk % 16);
                z.Name = p[1];

                if (z.Shape == ZoneShape.Circle)
                {
                    Vector3D c;
                    if (!ParseV(p[2], out c)) return null;
                    z.Verts.Add(c);
                    if (p.Length >= 4) { double r; if (double.TryParse(p[3], out r)) z.Radius = r; }
                }
                else
                {
                    string[] vs = p[2].Split('|');
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Vector3D vv;
                        if (ParseV(vs[i], out vv)) z.Verts.Add(vv);
                    }
                }
                z.Recompute();
                if (id >= _nextId) _nextId = id + 1;
                return z;
            }

            static void AppendV(StringBuilder sb, Vector3D v)
            {
                sb.Append((long)Rd(v.X)).Append(',').Append((long)Rd(v.Y)).Append(',').Append((long)Rd(v.Z));
            }

            static bool ParseV(string s, out Vector3D v)
            {
                v = VZ;
                string[] c = s.Split(',');
                if (c.Length < 3) return false;
                double x, y, z;
                if (!double.TryParse(c[0], out x) || !double.TryParse(c[1], out y) || !double.TryParse(c[2], out z))
                    return false;
                v = new Vector3D(x, y, z);
                return true;
            }

            // Keep names free of the field separators used in Storage / on the wire.
            static string San(string s) => SE(s) ? "ZONE" : s.Replace(';', ' ').Replace('|', ' ');
        }
    }
}
