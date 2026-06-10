using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Definitions;
using VRage.Game.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace HollowEarth
{
    internal static class Geo
    {
        public const int SIZE = 262144;
        public const double HALF = SIZE / 2;        // shell centre in storage voxels (== world origin)
        public const double R = 75000.0;            // inner radius -> 150 km
        public const double AMP = 150.0;            // terrain relief (m)
        public const double THICK = 60.0;           // crust depth
        public const double FREQ = 40.0;

        public static double Hash(int x, int y, int z)
        {
            int n = x * 374761393 + y * 668265263 + z * 1274126177;
            n = (n ^ (n >> 13)) * 1274126177;
            return ((n ^ (n >> 16)) & 0x7fffffff) / 2147483647.0;
        }
        static double Smooth(double t) { return t * t * (3.0 - 2.0 * t); }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double Noise(double x, double y, double z)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
            double xf = x - xi, yf = y - yi, zf = z - zi, u = Smooth(xf), v = Smooth(yf), w = Smooth(zf);
            double a = Lerp(Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), u), Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), u), v);
            double b = Lerp(Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), u), Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), u), v);
            return Lerp(a, b, w);
        }
        static double Fbm(double x, double y, double z)
        {
            double s = 0, a = 0.5, f = 1.0, m = 0;
            for (int i = 0; i < 4; i++) { s += a * Noise(x * f, y * f, z * f); m += a; a *= 0.5; f *= 2.0; }
            return s / m;
        }
        public static double SurfaceR(double nx, double ny, double nz)
        {
            return R + (Fbm(nx * FREQ, ny * FREQ, nz * FREQ) - 0.5) * 2.0 * AMP;
        }
    }

    [MyStorageDataProvider(770077)]
    public class HollowEarthProvider : IMyStorageDataProvider
    {
        bool _mat;
        byte _stone, _grass, _dirt, _iron, _nickel, _cobalt, _silicon, _silver, _gold, _uran;

        public int SerializedSize { get { return 0; } }
        public void WriteTo(Stream stream) { }
        public void ReadFrom(int storageVersion, Stream stream, int size, ref bool isOldFormat) { }
        public void DebugDraw(ref MatrixD worldMatrix) { }
        public void ReindexMaterials(Dictionary<byte, byte> oldToNewIndexMap) { }
        public void Close() { }
        public void PostProcess(VrVoxelMesh mesh, MyStorageDataTypeFlags dataTypes) { }
        public bool Intersect(ref LineD line, out double startOffset, out double endOffset) { startOffset = 0; endOffset = 1; return true; }
        public ContainmentType Intersect(BoundingBoxI box, int lod) { return ContainmentType.Intersects; }

        public void ReadRange(MyStorageData target, MyStorageDataTypeFlags dataType, ref Vector3I writeOffset, int lodIndex, ref Vector3I minInLod, ref Vector3I maxInLod)
        {
            if ((dataType & MyStorageDataTypeFlags.Content) != MyStorageDataTypeFlags.None)
                ReadContent(target, ref writeOffset, lodIndex, ref minInLod, ref maxInLod, false);
            else
                ReadMaterial(target, ref writeOffset, lodIndex, ref minInLod, ref maxInLod, false, false);
        }

        public void ReadRange(ref MyVoxelDataRequest req, bool detectOnly = false)
        {
            if ((req.RequestedData & MyStorageDataTypeFlags.Content) != MyStorageDataTypeFlags.None)
                req.Flags = ReadContent(req.Target, ref req.Offset, req.Lod, ref req.MinInLod, ref req.MaxInLod, detectOnly);
            else
                req.Flags = ReadMaterial(req.Target, ref req.Offset, req.Lod, ref req.MinInLod, ref req.MaxInLod, detectOnly, (req.RequestFlags & MyVoxelRequestFlags.ConsiderContent) != 0);
        }

        static void RadialBounds(int lvs, ref Vector3I min, ref Vector3I max, out double rmin, out double rmax)
        {
            double loX = min.X * (double)lvs - Geo.HALF, hiX = (max.X + 1) * (double)lvs - Geo.HALF;
            double loY = min.Y * (double)lvs - Geo.HALF, hiY = (max.Y + 1) * (double)lvs - Geo.HALF;
            double loZ = min.Z * (double)lvs - Geo.HALF, hiZ = (max.Z + 1) * (double)lvs - Geo.HALF;
            double cx = loX > 0 ? loX : (hiX < 0 ? hiX : 0), cy = loY > 0 ? loY : (hiY < 0 ? hiY : 0), cz = loZ > 0 ? loZ : (hiZ < 0 ? hiZ : 0);
            rmin = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            double fx = Math.Max(Math.Abs(loX), Math.Abs(hiX)), fy = Math.Max(Math.Abs(loY), Math.Abs(hiY)), fz = Math.Max(Math.Abs(loZ), Math.Abs(hiZ));
            rmax = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        }

        static byte SdToContent(double sd)
        {
            if (sd < -1) sd = -1; else if (sd > 1) sd = 1;
            return (byte)((sd / -2.0 + 0.5) * 255.0);
        }

        MyVoxelRequestFlags ReadContent(MyStorageData target, ref Vector3I off, int lod, ref Vector3I min, ref Vector3I max, bool detectOnly)
        {
            int lvs = 1 << lod;
            double rmin, rmax; RadialBounds(lvs, ref min, ref max, out rmin, out rmax);
            double bandLo = Geo.R - Geo.AMP, bandHi = Geo.R + Geo.THICK + Geo.AMP, margin = lvs * 2;
            if (rmax < bandLo - margin || rmin > bandHi + margin)
            {
                if (!detectOnly) target.BlockFillContent(off, off + (max - min), 0);
                return MyVoxelRequestFlags.EmptyData;
            }
            if (detectOnly) return (MyVoxelRequestFlags)0;
            Vector3I c, p;
            for (c.Z = min.Z; c.Z <= max.Z; c.Z++)
                for (c.Y = min.Y; c.Y <= max.Y; c.Y++)
                    for (c.X = min.X; c.X <= max.X; c.X++)
                    {
                        double sx = c.X * (double)lvs - Geo.HALF, sy = c.Y * (double)lvs - Geo.HALF, sz = c.Z * (double)lvs - Geo.HALF;
                        double r = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                        double inv = r > 1e-6 ? 1.0 / r : 0.0;
                        double surf = Geo.SurfaceR(sx * inv, sy * inv, sz * inv);
                        double depth = r - surf;
                        double d = depth < 0 ? -depth : (depth > Geo.THICK ? depth - Geo.THICK : -Math.Min(depth, Geo.THICK - depth));
                        p = off + (c - min);
                        target.Content(ref p, SdToContent(d / lvs));
                    }
            return (MyVoxelRequestFlags)0;
        }

        MyVoxelRequestFlags ReadMaterial(MyStorageData target, ref Vector3I off, int lod, ref Vector3I min, ref Vector3I max, bool detectOnly, bool considerContent)
        {
            if (!_mat) ResolveMaterials();
            int lvs = 1 << lod;
            double rmin, rmax; RadialBounds(lvs, ref min, ref max, out rmin, out rmax);
            double bandLo = Geo.R - Geo.AMP, bandHi = Geo.R + Geo.THICK + Geo.AMP, margin = lvs * 2;
            if (rmax < bandLo - margin || rmin > bandHi + margin)
            {
                if (!detectOnly) { if (considerContent) target.BlockFillMaterialConsiderContent(off, off + (max - min), _stone); else target.BlockFillMaterial(off, off + (max - min), _stone); }
                return MyVoxelRequestFlags.EmptyData;
            }
            if (detectOnly) return (MyVoxelRequestFlags)0;
            Vector3I c, p;
            for (c.Z = min.Z; c.Z <= max.Z; c.Z++)
                for (c.Y = min.Y; c.Y <= max.Y; c.Y++)
                    for (c.X = min.X; c.X <= max.X; c.X++)
                    {
                        p = off + (c - min);
                        if (considerContent && target.Content(ref p) == 0) { target.Material(ref p, byte.MaxValue); continue; }
                        double sx = c.X * (double)lvs - Geo.HALF, sy = c.Y * (double)lvs - Geo.HALF, sz = c.Z * (double)lvs - Geo.HALF;
                        double r = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                        double inv = r > 1e-6 ? 1.0 / r : 0.0;
                        double surf = Geo.SurfaceR(sx * inv, sy * inv, sz * inv);
                        double depth = r - surf;
                        byte m = depth < 1.5 ? _grass : (depth < 8.0 ? _dirt : Ore((int)sx, (int)sy, (int)sz));
                        target.Material(ref p, m);
                    }
            return (MyVoxelRequestFlags)0;
        }

        byte Ore(int x, int y, int z)
        {
            if (Geo.Hash(x >> 4, y >> 4, z >> 4) < 0.80) return _stone;
            double pick = Geo.Hash(x >> 5, y >> 5, z >> 5);
            if (pick < 0.42) return _iron;
            if (pick < 0.66) return _nickel;
            if (pick < 0.80) return _cobalt;
            if (pick < 0.90) return _silicon;
            if (pick < 0.955) return _silver;
            if (pick < 0.99) return _gold;
            return _uran;
        }

        void ResolveMaterials()
        {
            _stone = Find("Stone");
            _grass = Or(Find("Grass"), _stone); _dirt = Or(Find("Soil"), _stone);
            _iron = Or(Find("Iron"), _stone); _nickel = Or(Find("Nickel"), _stone); _cobalt = Or(Find("Cobalt"), _stone);
            _silicon = Or(Find("Silicon"), _stone); _silver = Or(Find("Silver"), _stone); _gold = Or(Find("Gold"), _stone); _uran = Or(Find("Uran"), _stone);
            _mat = true;
        }
        static byte Or(byte a, byte b) { return a == 255 ? b : a; }
        static byte Find(string c)
        {
            foreach (var d in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
                if (d.Id.SubtypeName.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0) return (byte)d.Index;
            return 255;
        }
    }

    public class OutwardGravity : IMyModAPINaturalGravityImplementation
    {
        public Vector3D Center; public double Limit; public float Accel;
        public Vector3 GetWorldGravity(Vector3D p) { Vector3D d = p - Center; double l = d.Length(); return l < 1.0 ? Vector3.Zero : (Vector3)(d / l) * Accel; }
        public Vector3 GetWorldGravityNormalized(Vector3D p) { Vector3D d = p - Center; double l = d.Length(); return l < 1.0 ? Vector3.Zero : (Vector3)(d / l); }
        public bool IsPositionInRange(Vector3D p) { return (p - Center).LengthSquared() < Limit * Limit; }
        public float GetGravityMultiplier(Vector3D p) { return 1f; }
        public void GetProxyAABB(out BoundingBoxD aabb) { aabb = new BoundingBoxD(Center - Limit, Center + Limit); }
        public double? DoesTrajectoryIntersectNaturalGravity(RayD t, double raySize) { return null; }
        public float GetGravityLimit() { return (float)Limit; }
        public void OnPositionChanged(Vector3D p) { Center = p; }
    }
}
