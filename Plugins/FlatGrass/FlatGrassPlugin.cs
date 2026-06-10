using System;
using System.Collections.Generic;
using Sandbox.Engine.Voxels;   // MyOctreeStorage (publicized)
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;

// Pulsar client plugin: registers the FlatGrass voxel provider (type 770078) so SE can
// instantiate it from the .vx2 DataProvider chunk, and adds uniform downward gravity once
// the flat-grass voxel is loaded. Twin of HollowEarthPlugin.
namespace FlatGrass
{
    public sealed class FlatGrassPlugin : IPlugin
    {
        bool _gravityAdded;

        public void Init(object gameInstance)
        {
            try
            {
                MyOctreeStorage.RegisterTypes(new[] { typeof(FlatGrassProvider).Assembly });
                MyLog.Default.WriteLine("FlatGrass: voxel provider type registered.");
            }
            catch (Exception e) { MyLog.Default.WriteLine("FlatGrass: register failed: " + e); }
        }

        // Provider-only: the FlatGrass voxel does NOT add gravity. The world supplies it
        // (the SixLegFlatGravity mod's uniform field), so there is exactly one source — no
        // doubling. UniformGravity below is kept for reference but unused.
        public void Update() { }

        public void Dispose() { }
        public void OpenConfigDialog() { }
    }
}
