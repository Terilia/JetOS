using System;
using System.Collections.Concurrent;
using HarmonyLib;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.World;
using VRage.Utils;

namespace CameraLCD.Patches
{
    /// <summary>
    /// Prevent MyTextPanelComponent.UpdateAfterSimulation from entering its "out of
    /// render range" branch on surfaces we manage.
    ///
    /// Vanilla behavior on that branch:
    ///     if (!isInRange && !m_staticContent && isWorking) {
    ///         ReleaseTexture();
    ///         SetDefaultTexture(isWorking);  // binds "Online"/"Offline"
    ///         return;
    ///     }
    /// This runs every simulation tick. Because UpdateAfterSimulation runs on the sim
    /// thread and CameraTSS.Draw runs on the render thread, SE's "Online" binding wins
    /// the race in many frames and the user sees the default LCD text instead of the
    /// camera view — even though our Draw path reports success.
    ///
    /// We cap SE's isInRange at true when CAMOV owns the surface AND the player is
    /// within our own Range. Beyond our Range the shadow Draw is already gated
    /// OutOfRange and we've already proactively released our texture, so we let SE's
    /// vanilla release path run normally — no point keeping distant bases' LCDs
    /// simulation-active when we don't need them.
    ///
    /// The distance check is cached per-surface with a ~200ms staleness so the
    /// per-tick sim overhead stays flat regardless of managed-LCD count.
    /// </summary>
    [HarmonyPatch(typeof(MyTextPanelComponent), nameof(MyTextPanelComponent.UpdateAfterSimulation))]
    public static class Patch_UpdateAfterSimulation
    {
        private const int CacheStalenessMs = 200;

        private struct Entry
        {
            public int TickMs;
            public bool InRange;
        }

        private static readonly ConcurrentDictionary<long, Entry> _cache = new ConcurrentDictionary<long, Entry>();
        private static readonly ConcurrentDictionary<long, bool> _seen = new ConcurrentDictionary<long, bool>();

        [HarmonyPrefix]
        public static void Prefix(MyTextPanelComponent __instance, bool isWorking, ref bool isInRange)
        {
            if (!Plugin.Settings.Enabled) return;

            var block = __instance.m_block as MyTerminalBlock;
            if (block == null) return;

            int area = __instance.m_area;
            if (!CameraLcdManager.HasDisplay(block.EntityId, area)) return;

            // One-shot fire log per surface so we can confirm this prefix actually runs.
            long key0 = (block.EntityId << 8) | (uint)(area & 0xFF);
            if (_seen.TryAdd(key0, true))
            {
                MyLog.Default.WriteLine(
                    $"CAMOV: UpdateAfterSim prefix fired lcd={block.EntityId} area={area} " +
                    $"isWorking={isWorking} isInRange={isInRange}");
            }

            if (isInRange) return;

            long key = key0;
            int now = Environment.TickCount;

            if (!_cache.TryGetValue(key, out var entry) || unchecked(now - entry.TickMs) >= CacheStalenessMs)
            {
                var cam = MySector.MainCamera;
                bool within = cam != null &&
                    cam.GetDistanceFromPoint(block.WorldMatrix.Translation) <= Plugin.Settings.Range;
                entry = new Entry { TickMs = now, InRange = within };
                _cache[key] = entry;
            }

            if (entry.InRange) isInRange = true;
        }

        // Postfix runs on the sim thread immediately after SE's UpdateAfterSimulation
        // finishes. Regardless of which branch SE took (Online/Offline default, script
        // update, early-return on null Render), we rebind the mesh material to our
        // offscreen RTV. This is the "do what SE does, but own the final binding"
        // approach — we stop fighting SE's races across sim vs render threads by making
        // the last ChangeRenderTexture in each sim tick ours.
        //
        // Skip when the block is not working so the user still sees "Offline" when
        // they intentionally turn the block off.
        [HarmonyPostfix]
        public static void Postfix(MyTextPanelComponent __instance, bool isWorking, bool isInRange)
        {
            if (!Plugin.Settings.Enabled) return;
            if (!isWorking) return;

            var block = __instance.m_block as MyTerminalBlock;
            if (block == null) return;

            int area = __instance.m_area;
            if (!CameraLcdManager.HasDisplay(block.EntityId, area)) return;

            try
            {
                if (__instance.Render == null) return;
                if (!__instance.m_textureGenerated)
                    __instance.EnsureGeneratedTexture();
                if (!__instance.m_textureGenerated) return;
                __instance.ChangeRenderTexture(area, __instance.GetRenderTextureName(), isForced: true);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine(
                    $"CAMOV: UpdateAfterSim postfix threw lcd={block.EntityId} area={area}: {ex.Message}");
            }
        }
    }
}
