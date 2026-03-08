using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities.Blocks;
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
    ///   5-12m:  30fps (render every 2nd update)
    ///   12m+:   vanilla (~6fps, no immediate render)
    /// </summary>
    [HarmonyPatch(typeof(MyTextPanelComponent), nameof(MyTextPanelComponent.UpdateSpriteCollection))]
    internal static class ImmediateClientRenderPatch
    {
        private static readonly MethodInfo EnsureGeneratedTextureMethod =
            AccessTools.Method(typeof(MyTextPanelComponent), "EnsureGeneratedTexture");

        private static readonly MethodInfo UpdateSpritesTextureMethod =
            AccessTools.Method(typeof(MyTextPanelComponent), "UpdateSpritesTexture");

        private static readonly ConcurrentDictionary<MyTextPanelComponent, long> LastRenderTick =
            new ConcurrentDictionary<MyTextPanelComponent, long>();

        private const double FullRateDistSq = 5.0 * 5.0;
        private const double HalfRateDistSq = 12.0 * 12.0;
        private const int CleanupIntervalTicks = 3600;
        private static long _lastCleanupTick;

        static ImmediateClientRenderPatch()
        {
            if (EnsureGeneratedTextureMethod == null)
                MyLog.Default.WriteLine("LcdBoosterClient: EnsureGeneratedTexture method not found — render patch inactive.");
            if (UpdateSpritesTextureMethod == null)
                MyLog.Default.WriteLine("LcdBoosterClient: UpdateSpritesTexture method not found — render patch inactive.");
        }

        static void Postfix(MyTextPanelComponent __instance)
        {
            if (Sync.IsDedicated)
                return;

            if (__instance.ContentType != ContentType.SCRIPT)
                return;

            if (EnsureGeneratedTextureMethod == null || UpdateSpritesTextureMethod == null)
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
                if (now - last < 2)
                    return;
                LastRenderTick[__instance] = now;

                // Periodic cleanup of dead panel references
                if (now - _lastCleanupTick >= CleanupIntervalTicks)
                {
                    _lastCleanupTick = now;
                    var blockField = AccessTools.Field(typeof(MyTextPanelComponent), "m_block");
                    foreach (var kvp in LastRenderTick)
                    {
                        var block = blockField?.GetValue(kvp.Key) as Sandbox.Game.Entities.Cube.MyTerminalBlock;
                        if (block == null || block.MarkedForClose)
                            LastRenderTick.TryRemove(kvp.Key, out _);
                    }
                }
            }

            EnsureGeneratedTextureMethod.Invoke(__instance, null);
            UpdateSpritesTextureMethod.Invoke(__instance, null);
        }
    }
}
