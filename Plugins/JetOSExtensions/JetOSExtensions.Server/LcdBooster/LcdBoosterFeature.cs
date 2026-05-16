using System;
using System.Collections.Generic;
using HarmonyLib;
using NLog;
using Sandbox.Engine.Multiplayer;
using Torch.API;
using VRage.Network;

namespace LcdBooster
{
    public sealed class LcdBoosterFeature
    {
        internal static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private bool _callSitesPatched;
        private int _patchedCallSiteCount;

        public bool CallSitesPatched => _callSitesPatched;
        public int PatchedCallSiteCount => _patchedCallSiteCount;
        public int TrackedCanardCount => CanardAngleSync.TrackedCount;
        public bool CanardResolverReady => CanardAngleSync.Resolved;

        public void Init(ITorchBase torch)
        {
            Log.Info("JetOSExtensions.Server: LCD booster feature loaded.");
        }

        public void Update()
        {
            if (!_callSitesPatched)
            {
                if (MyMultiplayer.Static?.ReplicationLayer != null)
                {
                    PatchCallSiteDistances();
                    _callSitesPatched = true;
                }
            }

            CanardAngleSync.Update();
        }

        /// <summary>
        /// Increase DistanceRadiusSquared on OnUpdateSpriteCollection CallSites
        /// from 32m (1024) to 64m (4096).
        /// </summary>
        private void PatchCallSiteDistances()
        {
            try
            {
                var replicationLayer = MyMultiplayer.Static.ReplicationLayer;

                var typeTableField = AccessTools.Field(typeof(MyReplicationLayerBase), "m_typeTable");
                var typeTable = (MyTypeTable)typeTableField.GetValue(replicationLayer);

                var typeLookupField = AccessTools.Field(typeof(MyTypeTable), "m_typeLookup");
                var typeLookup = (Dictionary<Type, MySynchronizedTypeInfo>)typeLookupField.GetValue(typeTable);

                var idToEventField = AccessTools.Field(typeof(MyEventTable), "m_idToEvent");
                var distanceField = AccessTools.Field(typeof(CallSite), nameof(CallSite.DistanceRadiusSquared));

                int patchedCount = 0;
                float newDistanceSquared = 64f * 64f;

                foreach (var kvp in typeLookup)
                {
                    if (kvp.Value?.EventTable == null)
                        continue;

                    var events = (Dictionary<uint, CallSite>)idToEventField.GetValue(kvp.Value.EventTable);
                    foreach (var callSite in events.Values)
                    {
                        if (callSite.MethodInfo.Name == "OnUpdateSpriteCollection" && callSite.HasDistanceRadius)
                        {
                            distanceField.SetValue(callSite, newDistanceSquared);
                            patchedCount++;
                        }
                    }
                }

                _patchedCallSiteCount = patchedCount;
                Log.Info($"LcdBooster: Patched {patchedCount} OnUpdateSpriteCollection CallSite(s) to 64m range.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LcdBooster: Failed to patch CallSite distances.");
            }
        }

        public void Dispose()
        {
        }
    }
}
