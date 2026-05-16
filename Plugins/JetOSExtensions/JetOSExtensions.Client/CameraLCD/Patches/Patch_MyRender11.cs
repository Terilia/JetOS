using System;
using HarmonyLib;
using VRage.Utils;
using VRageRender;

namespace CameraLCD.Patches
{
    [HarmonyPatch]
    public static class Patch_MyRender11
    {
        [HarmonyPatch(typeof(MyRender11), nameof(MyRender11.DrawGameScene))]
        [HarmonyPrefix]
        public static void MyRender11_DrawGameScene_Prefix()
        {
            // Defensive try/catch: if our code throws here, Harmony would abort the
            // prefix and DrawGameScene never runs → whole-game black screen. Log and
            // swallow instead so any bug stays isolated to the LCD overlay.
            try
            {
                if (!Plugin.Settings.Enabled)
                    return;

                if (MyRender11.m_screenshot.HasValue)
                    return;

                CameraLcdManager.Draw();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"CAMOV: Patch_MyRender11 prefix threw: {ex}");
            }
        }
    }
}
