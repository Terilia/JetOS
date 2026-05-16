using System.Collections.Concurrent;
using System.Reflection;
using CameraLCD;
using HarmonyLib;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;

namespace LcdBoosterClient
{
    /// <summary>
    /// Render LCD textures with distance-based refresh rate LOD:
    ///   0-5m:   60fps (immediate render every update)
    ///   5-12m:  15fps (render every 4th update)
    ///   12m+:   vanilla (~6fps, no immediate render)
    ///
    /// Uses cached method delegates instead of MethodInfo.Invoke for the render calls.
    /// </summary>
    [HarmonyPatch(typeof(MyTextPanelComponent), nameof(MyTextPanelComponent.UpdateSpriteCollection))]
    internal static class ImmediateClientRenderPatch
    {
        // Cached method delegates — eliminates MethodInfo.Invoke overhead
        private static readonly System.Action<MyTextPanelComponent> CallEnsureTexture;
        private static readonly System.Func<MyTextPanelComponent, bool> CallUpdateSprites;

        // Cached field ref for cleanup — was re-resolved via AccessTools.Field() every cycle!
        private static readonly FieldInfo BlockField;

        private static readonly bool Broken;

        private static readonly ConcurrentDictionary<MyTextPanelComponent, long> LastRenderTick =
            new ConcurrentDictionary<MyTextPanelComponent, long>();
        private static readonly ConcurrentDictionary<MyTextPanelComponent, bool> CamovSkipLogged =
            new ConcurrentDictionary<MyTextPanelComponent, bool>();

        private const double FullRateDistSq = 5.0 * 5.0;
        private const double HalfRateDistSq = 12.0 * 12.0;
        private const int CleanupIntervalTicks = 3600;
        private static long _lastCleanupTick;

        static ImmediateClientRenderPatch()
        {
            bool ok = true;
            try
            {
                var ensureMethod = AccessTools.Method(typeof(MyTextPanelComponent), "EnsureGeneratedTexture");
                var updateMethod = AccessTools.Method(typeof(MyTextPanelComponent), "UpdateSpritesTexture");

                if (ensureMethod != null)
                    CallEnsureTexture = AccessTools.MethodDelegate<System.Action<MyTextPanelComponent>>(ensureMethod);
                if (updateMethod != null)
                    CallUpdateSprites = AccessTools.MethodDelegate<System.Func<MyTextPanelComponent, bool>>(updateMethod);

                BlockField = AccessTools.Field(typeof(MyTextPanelComponent), "m_block");
            }
            catch
            {
                ok = false;
            }

            if (CallEnsureTexture == null || CallUpdateSprites == null)
                ok = false;

            Broken = !ok;
            if (Broken)
                MyLog.Default.WriteLine("LcdBoosterClient: render patch inactive — methods not resolved.");
        }

        static void Postfix(MyTextPanelComponent __instance)
        {
            if (Sync.IsDedicated || Broken)
                return;

            if (__instance.ContentType != ContentType.SCRIPT)
                return;

            if (__instance.Script == CameraTSS.SCRIPT_ID)
            {
                LogCamovSkip(__instance);
                return;
            }

            if (__instance.Render == null)
                return;

            var camera = MySector.MainCamera;
            if (camera == null)
                return;

            double distSq = Vector3D.DistanceSquared(camera.Position, __instance.WorldPosition);

            if (distSq > HalfRateDistSq)
                return;

            if (distSq > FullRateDistSq)
            {
                long now = MySession.Static?.GameplayFrameCounter ?? 0;
                long last = LastRenderTick.GetOrAdd(__instance, 0);
                if (now - last < 4)
                    return;
                LastRenderTick[__instance] = now;

                if (now - _lastCleanupTick >= CleanupIntervalTicks)
                {
                    _lastCleanupTick = now;
                    CleanupDeadEntries();
                }
            }

            CallEnsureTexture(__instance);
            CallUpdateSprites(__instance);
        }

        private static void CleanupDeadEntries()
        {
            if (BlockField == null) return;
            foreach (var kvp in LastRenderTick)
            {
                var block = BlockField.GetValue(kvp.Key) as Sandbox.Game.Entities.Cube.MyTerminalBlock;
                if (block == null || block.MarkedForClose)
                    LastRenderTick.TryRemove(kvp.Key, out _);
            }

            foreach (var kvp in CamovSkipLogged)
            {
                var block = BlockField.GetValue(kvp.Key) as Sandbox.Game.Entities.Cube.MyTerminalBlock;
                if (block == null || block.MarkedForClose)
                    CamovSkipLogged.TryRemove(kvp.Key, out _);
            }
        }

        private static void LogCamovSkip(MyTextPanelComponent panel)
        {
            if (!Plugin.Settings.DebugLogging)
                return;

            if (!CamovSkipLogged.TryAdd(panel, true))
                return;

            var block = BlockField?.GetValue(panel) as MyTerminalBlock;
            MyLog.Default.WriteLine(
                $"CAMOV CLIENT: lcdbooster-skip lcd={block?.EntityId ?? 0} area={panel.Area} " +
                $"lcdName=\"{block?.CustomName}\" script={panel.Script}");
        }
    }
}
