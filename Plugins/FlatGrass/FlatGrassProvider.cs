using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Definitions;
using VRage.Game.ModAPI;
using VRage.Voxels;
using VRageMath;

// On-the-fly voxel provider for a FLAT GRASS plane (gm_flatgrass). Twin of HollowEarthProvider
// but the surface is a horizontal plane instead of a radial shell, so SE's clipmap streams a
// flat grassy ground at every LOD with no stored voxels. The .vx2 (Tools/voxelgen/provider_vx2.py,
// type_id 770078, size 16384) carries only the DataProvider chunk; this class fills content+material.
//
// SPARSE like HollowEarth (this is what makes the clipmap stream instead of choke): the ground is a
// thin CRUST -- solid only in the band [-CRUST, 0]; everything above 0 and everything below -CRUST
// returns EmptyData, so the clipmap skips the bulk and only meshes the thin slab near the camera.
//   content : solid in the crust band, empty above/below (slab signed distance).
//   material: Grass in the top 1.5 m, Soil to 8 m, Stone to the crust bottom.
//   gravity : NONE here -- the world supplies it (SixLegFlatGravity mod). Provider-only.
namespace FlatGrass
{
    internal static class Geo
    {
        public const int SIZE = 16384;            // storage edge: 16 km plane (~5 km in all directions + margin)
        public const double HALF = SIZE / 2;       // storage voxel mapped to the surface plane (world Y=0)
        public const double CRUST = 128.0;         // solid ground depth (m) below the surface; empty below that
        public const double GRASS_DEPTH = 1.5;     // m of Grass at the surface
        public const double SOIL_DEPTH = 8.0;      // m of Soil below the grass
    }

    [MyStorageDataProvider(770078)]
    public class FlatGrassProvider : IMyStorageDataProvider
    {
        bool _mat;
        byte _grass, _soil, _stone;

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

        // World-Y span (metres, surface plane at 0) covered by a request region at this LOD.
        static void YBounds(int lvs, ref Vector3I min, ref Vector3I max, out double loY, out double hiY)
        {
            loY = min.Y * (double)lvs - Geo.HALF;
            hiY = (max.Y + 1) * (double)lvs - Geo.HALF;
        }

        static byte SdToContent(double sd)
        {
            if (sd < -1) sd = -1; else if (sd > 1) sd = 1;
            return (byte)((sd / -2.0 + 0.5) * 255.0);   // negative => solid (255), positive => empty (0)
        }

        MyVoxelRequestFlags ReadContent(MyStorageData target, ref Vector3I off, int lod, ref Vector3I min, ref Vector3I max, bool detectOnly)
        {
            int lvs = 1 << lod;
            double loY, hiY; YBounds(lvs, ref min, ref max, out loY, out hiY);
            double margin = lvs * 2;
            // entirely above the surface OR entirely below the crust -> empty (the clipmap skips it)
            if (loY > margin || hiY < -Geo.CRUST - margin)
            {
                if (!detectOnly) target.BlockFillContent(off, off + (max - min), 0);
                return MyVoxelRequestFlags.EmptyData;
            }
            if (detectOnly) return (MyVoxelRequestFlags)0;
            Vector3I c, p;
            for (c.Z = min.Z; c.Z <= max.Z; c.Z++)
                for (c.Y = min.Y; c.Y <= max.Y; c.Y++)
                {
                    double worldY = c.Y * (double)lvs - Geo.HALF;
                    double sd = Math.Max(-Geo.CRUST - worldY, worldY);   // slab SDF: <0 inside crust band, >0 outside
                    byte content = SdToContent(sd / lvs);
                    for (c.X = min.X; c.X <= max.X; c.X++)
                    {
                        p = off + (c - min);
                        target.Content(ref p, content);
                    }
                }
            return (MyVoxelRequestFlags)0;
        }

        MyVoxelRequestFlags ReadMaterial(MyStorageData target, ref Vector3I off, int lod, ref Vector3I min, ref Vector3I max, bool detectOnly, bool considerContent)
        {
            if (!_mat) ResolveMaterials();
            int lvs = 1 << lod;
            double loY, hiY; YBounds(lvs, ref min, ref max, out loY, out hiY);
            double margin = lvs * 2;
            if (loY > margin || hiY < -Geo.CRUST - margin)   // outside the crust band -> uniform (content is empty here)
            {
                if (!detectOnly) { if (considerContent) target.BlockFillMaterialConsiderContent(off, off + (max - min), _stone); else target.BlockFillMaterial(off, off + (max - min), _stone); }
                return MyVoxelRequestFlags.EmptyData;
            }
            if (detectOnly) return (MyVoxelRequestFlags)0;
            Vector3I c, p;
            for (c.Z = min.Z; c.Z <= max.Z; c.Z++)
                for (c.Y = min.Y; c.Y <= max.Y; c.Y++)
                {
                    double depth = -(c.Y * (double)lvs - Geo.HALF);   // metres below the surface plane
                    byte m = depth < Geo.GRASS_DEPTH ? _grass : (depth < Geo.SOIL_DEPTH ? _soil : _stone);
                    for (c.X = min.X; c.X <= max.X; c.X++)
                    {
                        p = off + (c - min);
                        if (considerContent && target.Content(ref p) == 0) { target.Material(ref p, byte.MaxValue); continue; }
                        target.Material(ref p, m);
                    }
                }
            return (MyVoxelRequestFlags)0;
        }

        void ResolveMaterials()
        {
            _stone = Find("Stone");
            _grass = Or(Find("Grass"), _stone);
            _soil = Or(Find("Soil"), _stone);
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
}
