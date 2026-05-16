using HarmonyLib;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Entities.Blocks;

namespace CameraLCD.Patches
{
    /// <summary>
    /// Prevent MyTextPanelComponent.ReleaseTexture from destroying the offscreen
    /// texture for surfaces we manage. Destroy+recreate under the same name causes
    /// the mesh material's texture handle to resolve to a stale resource — user
    /// symptom: persistent black screen after walk-away/return, even though our
    /// Draw successfully writes camera view to the (new, same-named) texture.
    ///
    /// Keeping the texture alive across scene-cull cycles avoids the handle-churn
    /// entirely. The shadow's Dispose path (block destroyed, marker removed)
    /// unregisters from CameraLcdManager BEFORE the block's teardown, so real
    /// releases from MyTextPanel.Closing still work — HasDisplay returns false at
    /// that point and this patch falls through.
    /// </summary>
    [HarmonyPatch(typeof(MyTextPanelComponent), nameof(MyTextPanelComponent.ReleaseTexture))]
    public static class Patch_ReleaseTexture
    {
        [HarmonyPrefix]
        public static bool Prefix(MyTextPanelComponent __instance)
        {
            if (!Plugin.Settings.Enabled) return true;

            var block = __instance.m_block as MyTerminalBlock;
            if (block == null) return true;

            int area = __instance.m_area;
            if (!CameraLcdManager.HasDisplay(block.EntityId, area)) return true;

            // Skip SE's release — keep texture alive.
            return false;
        }
    }
}
