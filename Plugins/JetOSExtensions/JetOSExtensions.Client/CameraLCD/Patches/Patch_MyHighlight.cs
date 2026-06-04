using System.Reflection;
using HarmonyLib;
using VRage.Render11.RenderContext;

namespace CameraLCD.Patches
{
    [HarmonyPatch]
    public static class Patch_MyHighlight
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method("VRageRender.MyHighlight:CopyDepthStencil");
        }

        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !CameraViewRenderer.IsDrawing;
        }
    }
}
