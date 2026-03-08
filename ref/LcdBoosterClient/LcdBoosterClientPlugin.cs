using System;
using System.Reflection;
using HarmonyLib;
using VRage.Plugins;
using VRage.Utils;

namespace LcdBoosterClient
{
    public class LcdBoosterClientPlugin : IPlugin
    {
        private Harmony _harmony;

        public void Init(object gameInstance)
        {
            try
            {
                _harmony = new Harmony("com.lcdboosterclient.patch");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                MyLog.Default.WriteLine("LcdBoosterClient: Patches applied.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"LcdBoosterClient: Init failed: {ex}");
            }
        }

        public void Update() { }

        public void Dispose()
        {
            _harmony?.UnpatchAll("com.lcdboosterclient.patch");
        }
    }
}
