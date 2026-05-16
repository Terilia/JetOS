using System;
using HarmonyLib;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Cube;

namespace CameraLCD.Patches
{
    /// <summary>
    /// Suppress vanilla's sprite-to-texture rasterize for surfaces we manage.
    ///
    /// Vanilla flow for PB sprites: MyTextPanelComponent.UpdateSpritesTexture →
    /// MyRenderComponentScreenAreas.RenderSpritesToTexture → MyRenderProxy.RenderOffscreenTexture →
    /// MyOffscreenRenderer (async, 1-frame deferred) → clears the LCD offscreen RTV + draws
    /// sprites. That clear wipes the camera image CameraTSS.Draw wrote the same frame.
    ///
    /// Skipping vanilla for our displays leaves the camera RTV untouched by the async
    /// pipeline; CameraTSS.CamovComposite handles sprite overlay synchronously. Non-managed
    /// surfaces are unaffected.
    /// </summary>
    [HarmonyPatch(typeof(MyRenderComponentScreenAreas), nameof(MyRenderComponentScreenAreas.RenderSpritesToTexture))]
    public static class Patch_RenderSpritesToTexture
    {
        [HarmonyPrefix]
        public static bool Prefix(MyRenderComponentScreenAreas __instance, int area)
        {
            if (!Plugin.Settings.Enabled) return true;
            var block = __instance.m_entity as MyTerminalBlock;
            if (block == null) return true;
            if (!CameraLcdManager.HasDisplay(block.EntityId, area)) return true;
            return false;
        }
    }
}
