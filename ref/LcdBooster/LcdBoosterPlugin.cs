using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NLog;
using Sandbox.Engine.Multiplayer;
using Torch;
using Torch.API;
using VRage.Network;

namespace LcdBooster
{
    public class LcdBoosterPlugin : TorchPluginBase
    {
        internal static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private Harmony _harmony;
        private bool _callSitesPatched;

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);

            _harmony = new Harmony("com.lcdbooster.patch");

            try
            {
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info("LcdBooster: All Harmony patches applied.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LcdBooster: Failed to apply Harmony patches.");
            }
        }

        public override void Update()
        {
            if (_callSitesPatched)
                return;

            if (MyMultiplayer.Static?.ReplicationLayer == null)
                return;

            PatchCallSiteDistances();
            _callSitesPatched = true;
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

                Log.Info($"LcdBooster: Patched {patchedCount} OnUpdateSpriteCollection CallSite(s) to 64m range.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LcdBooster: Failed to patch CallSite distances.");
            }
        }

        public override void Dispose()
        {
            _harmony?.UnpatchAll("com.lcdbooster.patch");
            base.Dispose();
        }
    }
}
