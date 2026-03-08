using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NLog;
using Sandbox;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Replication;
using Sandbox.Game.Replication.StateGroups;
using SpaceEngineers.Game.EntityComponents.Blocks;
using VRage.Game.GUI.TextPanel;
using VRage.Network;

namespace LcdBooster
{
    /// <summary>
    /// Make LCD-related state groups high priority.
    /// Causes ScheduleStateGroupSync to use SendInterval/16, increasing sync frequency
    /// for LCD Sync&lt;T&gt; properties (content metadata, font data, script data).
    /// </summary>
    [HarmonyPatch(typeof(MyPropertySyncStateGroup), nameof(MyPropertySyncStateGroup.IsHighPriority), MethodType.Getter)]
    internal static class IsHighPriorityPatch
    {
        private static readonly PropertyInfo InstanceProp =
            AccessTools.Property(typeof(MyEventProxyEntityComponentReplicable), "Instance");

        static void Postfix(MyPropertySyncStateGroup __instance, ref bool __result)
        {
            if (__result || InstanceProp == null)
                return;

            IMyReplicable owner = __instance.Owner;
            if (!(owner is MyEventProxyEntityComponentReplicable))
                return;

            object instance = InstanceProp.GetValue(owner);
            if (instance is MyMultiTextPanelComponent || instance is MyLcdSurfaceComponent)
                __result = true;
        }
    }

    /// <summary>
    /// Send sprites immediately when a PB dispatches them, instead of waiting
    /// for the 10-tick UpdateAfterSimulation cycle. Full keyframe every 10s.
    /// </summary>
    [HarmonyPatch(typeof(MyTextPanelComponent), "DispatchSprites")]
    internal static class ImmediateSpriteSendPatch
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static readonly MethodInfo SendSpriteQueueMethod =
            AccessTools.Method(typeof(MyTextPanelComponent), "SendSpriteQueue");

        private static readonly FieldInfo AreSpritesDirtyField =
            AccessTools.Field(typeof(MyTextPanelComponent), "m_areSpritesDirty");

        private static readonly FieldInfo LastSpriteQueueField =
            AccessTools.Field(typeof(MyTextPanelComponent), "m_lastSpriteQueue");

        private static readonly ConcurrentDictionary<MyTextPanelComponent, long> LastKeyframeTick =
            new ConcurrentDictionary<MyTextPanelComponent, long>();

        private const long KeyframeIntervalTicks = 600;
        private const int CleanupIntervalTicks = 3600; // clean dead refs every 60s
        private static long _lastCleanupTick;

        static ImmediateSpriteSendPatch()
        {
            if (SendSpriteQueueMethod == null)
                LogManager.GetCurrentClassLogger().Warn("LcdBooster: SendSpriteQueue method not found — ImmediateSpriteSendPatch will be inactive.");
            if (AreSpritesDirtyField == null)
                LogManager.GetCurrentClassLogger().Warn("LcdBooster: m_areSpritesDirty field not found — ImmediateSpriteSendPatch will be inactive.");
        }

        static void Postfix(MyTextPanelComponent __instance)
        {
            if (!Sync.IsServer)
                return;

            if (SendSpriteQueueMethod == null || AreSpritesDirtyField == null)
                return;

            bool dirty = (bool)AreSpritesDirtyField.GetValue(__instance);
            if (!dirty)
                return;

            long now = Sandbox.Game.World.MySession.Static?.GameplayFrameCounter ?? 0;

            // Periodic keyframe: clear delta state so GetDelta sends all sprites
            long lastKeyframe = LastKeyframeTick.GetOrAdd(__instance, 0);
            if (now - lastKeyframe >= KeyframeIntervalTicks)
            {
                LastKeyframeTick[__instance] = now;
                LastSpriteQueueField?.SetValue(__instance, default(MySpriteCollection));
            }

            SendSpriteQueueMethod.Invoke(__instance, null);

            // Periodically clean up entries for destroyed panels
            if (now - _lastCleanupTick >= CleanupIntervalTicks)
            {
                _lastCleanupTick = now;
                CleanupDeadEntries();
            }
        }

        private static readonly FieldInfo BlockField =
            AccessTools.Field(typeof(MyTextPanelComponent), "m_block");

        private static void CleanupDeadEntries()
        {
            foreach (var kvp in LastKeyframeTick)
            {
                var block = BlockField?.GetValue(kvp.Key) as Sandbox.Game.Entities.Cube.MyTerminalBlock;
                if (block == null || block.MarkedForClose)
                    LastKeyframeTick.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Triple the state sync packets per client per tick from 7 to 21.
    /// </summary>
    [HarmonyPatch(typeof(MyReplicationServer), "FilterStateSync")]
    internal static class TripleStateSyncPacketsPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool patched = false;
            foreach (var instruction in instructions)
            {
                if (!patched && instruction.opcode == OpCodes.Ldc_I4_7)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4, 21);
                    patched = true;
                }
                else
                {
                    yield return instruction;
                }
            }

            if (!patched)
                LogManager.GetCurrentClassLogger().Warn("LcdBooster: TripleStateSyncPacketsPatch did not find ldc.i4.7 — FilterStateSync may have changed.");
        }
    }

    /// <summary>
    /// Remove the one-at-a-time streaming gate in MyStreamingEntityStateGroup.ProcessWrite.
    ///
    /// Vanilla: ProcessWrite sends a no-op if the previous streaming packet hasn't been
    /// ACKed yet (checks LastSent.HasValue). This makes grid streaming serial.
    ///
    /// Fix: Prefix clears LastSent before each Serialize so ProcessWrite always sends the
    /// next data part. Postfix marks the group as not-dirty once all data has been sent.
    /// </summary>
    [HarmonyPatch(typeof(MyReplicationServer), "SendStreamingEntry")]
    internal static class StreamingPipelinePatch
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // Lazily resolved reflection targets — cached after first successful resolution
        private static FieldInfo _clientStreamDataField;
        private static FieldInfo _lastSentField;
        private static FieldInfo _remainingBitsField;
        private static FieldInfo _incompleteField;
        private static FieldInfo _dirtyField;
        private static FieldInfo _forceSendField;
        private static FieldInfo _stateEntryGroupField;
        private static FieldInfo _clientStateField;
        private static PropertyInfo _endpointIdProp;
        private static MethodInfo _dictTryGetMethod;
        private static bool _resolvedGroup, _resolvedScd, _resolvedClient;
        private static bool _reflectionFailed;

        private static object GetStreamClientData(object stateGroup, object client)
        {
            if (_reflectionFailed || stateGroup == null || client == null)
                return null;

            try
            {
                // Resolve group-level reflection once
                if (!_resolvedGroup)
                {
                    _resolvedGroup = true;
                    _clientStreamDataField = stateGroup.GetType().GetField("m_clientStreamData",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (_clientStreamDataField == null)
                    {
                        Log.Warn("LcdBooster: StreamingPipelinePatch — m_clientStreamData field not found.");
                        _reflectionFailed = true;
                        return null;
                    }
                }

                // Resolve client-level reflection once
                if (!_resolvedClient)
                {
                    _resolvedClient = true;
                    _clientStateField = client.GetType().GetField("State");
                    if (_clientStateField != null)
                    {
                        var stateObj = _clientStateField.GetValue(client);
                        _endpointIdProp = stateObj?.GetType().GetProperty("EndpointId");
                    }
                    if (_clientStateField == null || _endpointIdProp == null)
                    {
                        Log.Warn("LcdBooster: StreamingPipelinePatch — Client.State.EndpointId not found.");
                        _reflectionFailed = true;
                        return null;
                    }
                }

                var clientState = _clientStateField.GetValue(client);
                if (clientState == null) return null;
                var endpoint = _endpointIdProp.GetValue(clientState);
                if (endpoint == null) return null;

                var dict = _clientStreamDataField.GetValue(stateGroup);
                if (dict == null) return null;

                if (_dictTryGetMethod == null)
                    _dictTryGetMethod = dict.GetType().GetMethod("TryGetValue");

                var args = new object[] { endpoint, null };
                bool found = (bool)_dictTryGetMethod.Invoke(dict, args);
                return found ? args[1] : null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LcdBooster: StreamingPipelinePatch reflection error.");
                _reflectionFailed = true;
                return null;
            }
        }

        private static void ResolveScdFields(object scd)
        {
            if (_resolvedScd) return;
            _resolvedScd = true;
            var t = scd.GetType();
            _lastSentField = t.GetField("LastSent");
            _remainingBitsField = t.GetField("RemainingBits");
            _incompleteField = t.GetField("Incomplete");
            _dirtyField = t.GetField("Dirty");
            _forceSendField = t.GetField("ForceSend");

            if (_lastSentField == null || _remainingBitsField == null ||
                _incompleteField == null || _dirtyField == null)
            {
                Log.Warn("LcdBooster: StreamingPipelinePatch — StreamClientData fields not fully resolved.");
                _reflectionFailed = true;
            }
        }

        static void Prefix(object client, object entry)
        {
            if (_reflectionFailed) return;

            try
            {
                if (_stateEntryGroupField == null)
                    _stateEntryGroupField = entry.GetType().GetField("Group");

                var stateGroup = _stateEntryGroupField?.GetValue(entry);
                var scd = GetStreamClientData(stateGroup, client);
                if (scd == null) return;

                if (!_resolvedScd) ResolveScdFields(scd);

                _lastSentField?.SetValue(scd, (byte?)null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LcdBooster: StreamingPipelinePatch.Prefix failed.");
                _reflectionFailed = true;
            }
        }

        static void Postfix(object client, object entry)
        {
            if (_reflectionFailed) return;

            try
            {
                var stateGroup = _stateEntryGroupField?.GetValue(entry);
                var scd = GetStreamClientData(stateGroup, client);
                if (scd == null) return;

                long remaining = (long)_remainingBitsField.GetValue(scd);
                bool incomplete = (bool)_incompleteField.GetValue(scd);

                if (remaining == 0L && !incomplete)
                {
                    _dirtyField.SetValue(scd, false);
                    _forceSendField.SetValue(scd, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LcdBooster: StreamingPipelinePatch.Postfix failed.");
                _reflectionFailed = true;
            }
        }
    }
}
