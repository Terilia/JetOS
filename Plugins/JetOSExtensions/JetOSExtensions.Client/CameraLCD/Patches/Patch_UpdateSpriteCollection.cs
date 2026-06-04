using HarmonyLib;
using JetOSExtensions.Shared;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using VRage.Game.GUI.TextPanel;

namespace CameraLCD.Patches
{
    [HarmonyPatch(typeof(MyTextPanelComponent), nameof(MyTextPanelComponent.UpdateSpriteCollection))]
    public static class Patch_UpdateSpriteCollection
    {
        [HarmonyPostfix]
        public static void Postfix(MyTextPanelComponent __instance, MySerializableSpriteCollection sprites)
        {
            if (!Plugin.Settings.Enabled) return;

            var block = __instance.m_block as MyTerminalBlock;
            if (block == null) return;

            int area = __instance.m_area;
            int surfaceKey = block is MyTextPanel ? 0 : area;
            bool commonTssSet = __instance.Script == CameraTSS.SCRIPT_ID;
            bool cameraSelected = CameraLcdManager.HasDisplay(block.EntityId, area);
            bool camovSurface =
                cameraSelected ||
                CamovSurfaceProtocol.UsesForcedMode(block.CustomData, surfaceKey, commonTssSet, cameraSelected);

            if (!camovSurface) return;

            CameraLcdManager.CaptureExternalSprites(block.EntityId, area, sprites);
        }
    }
}
