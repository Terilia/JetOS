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
    /// Uses a cached delegate instead of PropertyInfo.GetValue for the Instance property.
    /// </summary>
    [HarmonyPatch(typeof(MyPropertySyncStateGroup), nameof(MyPropertySyncStateGroup.IsHighPriority), MethodType.Getter)]
    internal static class IsHighPriorityPatch
    {
        private static readonly Func<MyEventProxyEntityComponentReplicable, object> GetInstance;
        private static readonly bool Broken;

        static IsHighPriorityPatch()
        {
            try
            {
                var getter = AccessTools.PropertyGetter(typeof(MyEventProxyEntityComponentReplicable), "Instance");
                if (getter != null)
                    GetInstance = AccessTools.MethodDelegate<Func<MyEventProxyEntityComponentReplicable, object>>(getter);
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Warn(ex, "LcdBooster: IsHighPriorityPatch — could not create Instance delegate.");
            }

            Broken = GetInstance == null;
        }

        static void Postfix(MyPropertySyncStateGroup __instance, ref bool __result)
        {
            if (__result || Broken)
                return;

            var owner = __instance.Owner as MyEventProxyEntityComponentReplicable;
            if (owner == null)
                return;

            var instance = GetInstance(owner);
            if (instance is MyMultiTextPanelComponent || instance is MyLcdSurfaceComponent)
                __result = true;
        }
    }

    /// <summary>
    /// Smart sprite send with delegate-based field access to eliminate reflection overhead.
    ///
    /// Active panel:  SendSpriteQueue every tick, keyframe every 120 ticks.
    /// Idle panel:    SendSpriteQueue every 10 ticks (change detection), keyframe every 120 ticks.
    /// Transition:    3 consecutive empty deltas → idle. Any actual send → active.
    /// </summary>
    [HarmonyPatch(typeof(MyTextPanelComponent), "DispatchSprites")]
    internal static class ImmediateSpriteSendPatch
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // Direct field/method accessors — no reflection, no boxing on every call
        private static readonly AccessTools.FieldRef<MyTextPanelComponent, bool> DirtyRef;
        private static readonly AccessTools.FieldRef<MyTextPanelComponent, MySpriteCollection> LastQueueRef;
        private static readonly Action<MyTextPanelComponent> CallSendSpriteQueue;
        private static readonly FieldInfo BlockField;  // cold path only (tag check + cleanup)
        private static readonly bool Broken;

        private static readonly ConcurrentDictionary<MyTextPanelComponent, PanelState> Panels =
            new ConcurrentDictionary<MyTextPanelComponent, PanelState>();

        private const string HfpsTag = "[HFPS]";
        private const int TagCheckIntervalTicks = 60;
        private const int IdleThreshold = 3;
        private const int IdleCheckInterval = 10;
        private const long KeyframeIntervalTicks = 120;
        private const int CleanupIntervalTicks = 3600;
        private static long _lastCleanupTick;

        private class PanelState
        {
            public long LastKeyframeTick;
            public int EmptyDeltaCount;
            public Sandbox.Game.Entities.Cube.MyTerminalBlock Block;
            public bool IsTagged;
            public long LastTagCheckTick;
        }

        static ImmediateSpriteSendPatch()
        {
            bool ok = true;
            try
            {
                DirtyRef = AccessTools.FieldRefAccess<MyTextPanelComponent, bool>("m_areSpritesDirty");
                LastQueueRef = AccessTools.FieldRefAccess<MyTextPanelComponent, MySpriteCollection>("m_lastSpriteQueue");

                var sendMethod = AccessTools.Method(typeof(MyTextPanelComponent), "SendSpriteQueue");
                if (sendMethod != null)
                    CallSendSpriteQueue = AccessTools.MethodDelegate<Action<MyTextPanelComponent>>(sendMethod);

                BlockField = AccessTools.Field(typeof(MyTextPanelComponent), "m_block");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "LcdBooster: ImmediateSpriteSendPatch — accessor creation failed.");
                ok = false;
            }

            if (DirtyRef == null || LastQueueRef == null || CallSendSpriteQueue == null || BlockField == null)
                ok = false;

            Broken = !ok;
            if (Broken)
                Log.Warn("LcdBooster: ImmediateSpriteSendPatch inactive — not all accessors resolved.");
        }

        static void Postfix(MyTextPanelComponent __instance)
        {
            if (!Sync.IsServer || Broken)
                return;

            // Direct field read — no FieldInfo.GetValue, no boxing
            ref bool dirty = ref DirtyRef(__instance);
            if (!dirty)
                return;

            long now = Sandbox.Game.World.MySession.Static?.GameplayFrameCounter ?? 0;
            var state = Panels.GetOrAdd(__instance, _ => new PanelState());

            if (!CheckTagged(state, __instance, now))
                return;

            bool isIdle = state.EmptyDeltaCount >= IdleThreshold;
            bool isKeyframeTick = (now - state.LastKeyframeTick) >= KeyframeIntervalTicks;

            if (isIdle && !isKeyframeTick && (now % IdleCheckInterval != 0))
            {
                dirty = false;  // direct field write — no FieldInfo.SetValue
                return;
            }

            // Single ref to the struct field — reads/writes go directly to object memory
            ref MySpriteCollection lastQueue = ref LastQueueRef(__instance);

            if (isKeyframeTick)
            {
                state.LastKeyframeTick = now;
                lastQueue = default;  // zero the struct in place — no boxing
            }

            var beforeSprites = lastQueue.Sprites;

            CallSendSpriteQueue(__instance);  // delegate call — no MethodInfo.Invoke

            var afterSprites = lastQueue.Sprites;

            if (!ReferenceEquals(beforeSprites, afterSprites))
                state.EmptyDeltaCount = 0;
            else
                state.EmptyDeltaCount = Math.Min(state.EmptyDeltaCount + 1, IdleThreshold);

            if (now - _lastCleanupTick >= CleanupIntervalTicks)
            {
                _lastCleanupTick = now;
                CleanupDeadEntries();
            }
        }

        private static bool CheckTagged(PanelState state, MyTextPanelComponent panel, long now)
        {
            if (now - state.LastTagCheckTick < TagCheckIntervalTicks)
                return state.IsTagged;

            state.LastTagCheckTick = now;

            if (state.Block == null)
                state.Block = BlockField.GetValue(panel) as Sandbox.Game.Entities.Cube.MyTerminalBlock;

            state.IsTagged = state.Block?.CustomName?.ToString()?.Contains(HfpsTag) == true;
            return state.IsTagged;
        }

        private static void CleanupDeadEntries()
        {
            foreach (var kvp in Panels)
            {
                var block = kvp.Value.Block ?? BlockField.GetValue(kvp.Key) as Sandbox.Game.Entities.Cube.MyTerminalBlock;
                if (block == null || block.MarkedForClose)
                    Panels.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Triple the state sync packets per client per tick from 7 to 21.
    /// Zero runtime overhead — transpiler rewrites IL at patch time.
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
    ///
    /// Note: Uses FieldInfo for runtime-typed internal structs. These run per streaming
    /// entry (lower frequency than per-panel patches), so the reflection cost is acceptable.
    /// </summary>
    [HarmonyPatch(typeof(MyReplicationServer), "SendStreamingEntry")]
    internal static class StreamingPipelinePatch
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

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

        // Reusable args array — avoids allocating new object[2] per call
        [ThreadStatic] private static object[] _dictArgs;

        private static object GetStreamClientData(object stateGroup, object client)
        {
            if (_reflectionFailed || stateGroup == null || client == null)
                return null;

            try
            {
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

                if (_dictArgs == null) _dictArgs = new object[2];
                _dictArgs[0] = endpoint;
                _dictArgs[1] = null;
                bool found = (bool)_dictTryGetMethod.Invoke(dict, _dictArgs);
                var result = found ? _dictArgs[1] : null;
                _dictArgs[0] = null;  // don't hold references
                _dictArgs[1] = null;
                return result;
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
