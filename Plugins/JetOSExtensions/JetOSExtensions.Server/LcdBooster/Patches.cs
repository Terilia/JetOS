using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetOSExtensions.Shared;
using NLog;
using Sandbox;
using Sandbox.Game.Entities;
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
        private static readonly AccessTools.FieldRef<MyTextPanelComponent, MySpriteCollection> QueueRef;
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
                QueueRef = AccessTools.FieldRefAccess<MyTextPanelComponent, MySpriteCollection>("m_spriteQueue");
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

            if (DirtyRef == null || QueueRef == null || LastQueueRef == null || CallSendSpriteQueue == null || BlockField == null)
                ok = false;

            Broken = !ok;
            if (Broken)
                Log.Warn("LcdBooster: ImmediateSpriteSendPatch inactive — not all accessors resolved.");
        }

        static void Postfix(MyTextPanelComponent __instance, [HarmonyArgument(0)] MySpriteDrawFrame drawFrame)
        {
            if (!Sync.IsServer || Broken)
                return;

            // Direct field read — no FieldInfo.GetValue, no boxing
            ref bool dirty = ref DirtyRef(__instance);
            bool forcedScriptSprites = TryQueueForcedScriptSprites(__instance, drawFrame, ref dirty);
            if (!dirty)
                return;

            long now = Sandbox.Game.World.MySession.Static?.GameplayFrameCounter ?? 0;
            var state = Panels.GetOrAdd(__instance, _ => new PanelState());

            if (!forcedScriptSprites && !CheckTagged(state, __instance, now))
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

        private static bool TryQueueForcedScriptSprites(MyTextPanelComponent panel, MySpriteDrawFrame drawFrame, ref bool dirty)
        {
            bool commonTssSet = panel.Script == CamovSurfaceProtocol.CameraDisplayScriptId;
            if (dirty || !commonTssSet)
                return false;

            var block = BlockField.GetValue(panel) as Sandbox.Game.Entities.Cube.MyTerminalBlock;
            if (block == null)
                return false;

            int surfaceKey = block is MyTextPanel ? 0 : panel.Area;
            bool cameraSelected = HasResolvableCameraSelection(block, surfaceKey);
            if (!CamovSurfaceProtocol.UsesForcedMode(block.CustomData, surfaceKey, commonTssSet, cameraSelected))
                return false;

            ref MySpriteCollection queue = ref QueueRef(panel);
            queue = drawFrame.ToCollection();
            dirty = true;
            return true;
        }

        private static bool HasResolvableCameraSelection(Sandbox.Game.Entities.Cube.MyTerminalBlock block, int surfaceKey)
        {
            string cameraName = CamovSurfaceProtocol.GetCameraSelectionName(block.CustomData, surfaceKey);
            if (!string.IsNullOrWhiteSpace(cameraName))
                return TryFindMechanicallyConnectedCamera(block.CubeGrid, cameraName);

            using (var reader = new System.IO.StringReader(block.CustomData))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line) &&
                        TryFindMechanicallyConnectedCamera(block.CubeGrid, line.Trim()))
                        return true;
                }
            }

            return false;
        }

        private static bool TryFindMechanicallyConnectedCamera(MyCubeGrid grid, string customName)
        {
            if (grid == null || string.IsNullOrWhiteSpace(customName))
                return false;

            if (TryFindMatch(grid, customName))
                return true;

            var mechanicalGroup = MyCubeGridGroups.Static.Mechanical.GetGroup(grid);
            if (mechanicalGroup != null)
            {
                foreach (var node in mechanicalGroup.Nodes)
                {
                    if (node.NodeData != grid && TryFindMatch(node.NodeData, customName))
                        return true;
                }
            }

            return false;
        }

        private static bool TryFindMatch(MyCubeGrid grid, string customName)
        {
            foreach (var fatBlock in grid.GetFatBlocks())
            {
                var cameraBlock = fatBlock as MyCameraBlock;
                if (cameraBlock != null &&
                    string.Equals(cameraBlock.CustomName?.ToString(), customName, StringComparison.Ordinal))
                    return true;
            }
            return false;
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

        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly ConcurrentDictionary<Type, FieldInfo> ClientStreamDataFields =
            new ConcurrentDictionary<Type, FieldInfo>();
        private static readonly ConcurrentDictionary<Type, FieldInfo> EntryGroupFields =
            new ConcurrentDictionary<Type, FieldInfo>();
        private static readonly ConcurrentDictionary<Type, MethodInfo> DictTryGetMethods =
            new ConcurrentDictionary<Type, MethodInfo>();
        private static readonly ConcurrentDictionary<Type, StreamClientDataFields> StreamClientDataFieldSets =
            new ConcurrentDictionary<Type, StreamClientDataFields>();
        private static readonly ConcurrentDictionary<string, bool> LoggedMissingMembers =
            new ConcurrentDictionary<string, bool>();
        private static bool _reflectionFailed;

        private sealed class StreamClientDataFields
        {
            public FieldInfo LastSent;
            public FieldInfo RemainingBits;
            public FieldInfo Incomplete;
            public FieldInfo Dirty;
            public FieldInfo ForceSend;
        }

        // Reusable args array — avoids allocating new object[2] per call
        [ThreadStatic] private static object[] _dictArgs;

        private static object GetStreamClientData(object stateGroup, object client)
        {
            if (_reflectionFailed || stateGroup == null || client == null)
                return null;

            try
            {
                var clientStateField = client.GetType().GetField("State", InstanceFields);
                if (clientStateField == null)
                {
                    WarnMissingMember(client.GetType(), "State");
                    return null;
                }

                var clientState = clientStateField.GetValue(client);
                if (clientState == null) return null;
                var endpointIdProp = clientState.GetType().GetProperty("EndpointId", InstanceFields);
                if (endpointIdProp == null)
                {
                    WarnMissingMember(clientState.GetType(), "EndpointId");
                    return null;
                }

                var endpoint = endpointIdProp.GetValue(clientState);
                if (endpoint == null) return null;

                FieldInfo clientStreamDataField;
                if (!TryGetCachedField(ClientStreamDataFields, stateGroup.GetType(), "m_clientStreamData", out clientStreamDataField))
                    return null;

                var dict = clientStreamDataField.GetValue(stateGroup);
                if (dict == null) return null;

                MethodInfo tryGetMethod;
                if (!TryGetCachedTryGetValue(dict.GetType(), out tryGetMethod))
                    return null;

                if (_dictArgs == null) _dictArgs = new object[2];
                _dictArgs[0] = endpoint;
                _dictArgs[1] = null;
                bool found = (bool)tryGetMethod.Invoke(dict, _dictArgs);
                var result = found ? _dictArgs[1] : null;
                _dictArgs[0] = null;  // don't hold references
                _dictArgs[1] = null;
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LcdBooster: StreamingPipelinePatch reflection error.");
                return null;
            }
        }

        private static bool TryGetCachedField(ConcurrentDictionary<Type, FieldInfo> cache, Type type, string name, out FieldInfo field)
        {
            if (cache.TryGetValue(type, out field))
                return true;

            field = type.GetField(name, InstanceFields);
            if (field == null)
            {
                WarnMissingMember(type, name);
                return false;
            }

            cache.TryAdd(type, field);
            return true;
        }

        private static bool TryGetCachedTryGetValue(Type dictionaryType, out MethodInfo method)
        {
            if (DictTryGetMethods.TryGetValue(dictionaryType, out method))
                return true;

            method = dictionaryType.GetMethod("TryGetValue");
            if (method == null)
            {
                WarnMissingMember(dictionaryType, "TryGetValue");
                return false;
            }

            DictTryGetMethods.TryAdd(dictionaryType, method);
            return true;
        }

        private static bool TryGetStreamClientDataFields(object scd, out StreamClientDataFields fields)
        {
            Type type = scd.GetType();
            if (StreamClientDataFieldSets.TryGetValue(type, out fields))
                return true;

            fields = new StreamClientDataFields
            {
                LastSent = type.GetField("LastSent", InstanceFields),
                RemainingBits = type.GetField("RemainingBits", InstanceFields),
                Incomplete = type.GetField("Incomplete", InstanceFields),
                Dirty = type.GetField("Dirty", InstanceFields),
                ForceSend = type.GetField("ForceSend", InstanceFields)
            };

            if (fields.LastSent == null || fields.RemainingBits == null ||
                fields.Incomplete == null || fields.Dirty == null)
            {
                WarnMissingMember(type, "StreamClientData required fields");
                return false;
            }

            StreamClientDataFieldSets.TryAdd(type, fields);
            return true;
        }

        private static void WarnMissingMember(Type type, string member)
        {
            string key = type.FullName + "::" + member;
            if (LoggedMissingMembers.TryAdd(key, true))
                Log.Warn("LcdBooster: StreamingPipelinePatch — " + member + " not found on " + type.FullName + ".");
        }

        static void Prefix(object client, object entry)
        {
            if (_reflectionFailed) return;

            try
            {
                FieldInfo stateEntryGroupField;
                if (!TryGetCachedField(EntryGroupFields, entry.GetType(), "Group", out stateEntryGroupField))
                    return;

                var stateGroup = stateEntryGroupField.GetValue(entry);
                var scd = GetStreamClientData(stateGroup, client);
                if (scd == null) return;

                StreamClientDataFields fields;
                if (!TryGetStreamClientDataFields(scd, out fields))
                    return;

                fields.LastSent.SetValue(scd, (byte?)null);
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
                FieldInfo stateEntryGroupField;
                if (!TryGetCachedField(EntryGroupFields, entry.GetType(), "Group", out stateEntryGroupField))
                    return;

                var stateGroup = stateEntryGroupField.GetValue(entry);
                var scd = GetStreamClientData(stateGroup, client);
                if (scd == null) return;

                StreamClientDataFields fields;
                if (!TryGetStreamClientDataFields(scd, out fields))
                    return;

                long remaining = (long)fields.RemainingBits.GetValue(scd);
                bool incomplete = (bool)fields.Incomplete.GetValue(scd);

                if (remaining == 0L && !incomplete)
                {
                    fields.Dirty.SetValue(scd, false);
                    fields.ForceSend?.SetValue(scd, false);
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
