using System;
using System.Collections.Generic;
using Sandbox.Engine.Voxels;   // MyOctreeStorage (publicized)
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;

// Pulsar client plugin: registers the HollowEarth voxel provider (no whitelist here, unlike script mods)
// and adds the detected outward gravity when the hollow world is loaded.
namespace HollowEarth
{
    public sealed class HollowEarthPlugin : IPlugin
    {
        bool _gravityAdded;

        public void Init(object gameInstance)
        {
            try
            {
                // Make SE able to instantiate our provider from the .vx2 DataProvider chunk (type 770077).
                MyOctreeStorage.RegisterTypes(new[] { typeof(HollowEarthProvider).Assembly });
                MyLog.Default.WriteLine("HollowEarth: voxel provider type registered.");
            }
            catch (Exception e) { MyLog.Default.WriteLine("HollowEarth: register failed: " + e); }
        }

        public void Update()
        {
            try
            {
                if (MyAPIGateway.Session == null) { _gravityAdded = false; return; }
                if (_gravityAdded) return;
                var vm = MyAPIGateway.Session.VoxelMaps;
                if (vm == null || MyAPIGateway.GravityProviderSystem == null) return;
                var list = new List<IMyVoxelBase>();
                vm.GetInstances(list, v => v.StorageName == "HollowEarth");
                if (list.Count == 0) return;
                MyAPIGateway.GravityProviderSystem.AddNaturalModAPI(Vector3D.Zero,
                    new OutwardGravity { Center = Vector3D.Zero, Accel = 9.81f, Limit = 76000.0 });
                _gravityAdded = true;
                MyLog.Default.WriteLine("HollowEarth: outward natural gravity registered.");
            }
            catch (Exception e) { MyLog.Default.WriteLine("HollowEarth: update err: " + e); }
        }

        public void Dispose() { }
        public void OpenConfigDialog() { }
    }
}
