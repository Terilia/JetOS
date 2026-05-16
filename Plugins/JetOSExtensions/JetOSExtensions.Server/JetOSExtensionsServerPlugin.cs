using System;
using System.Reflection;
using HarmonyLib;
using JetOSRadarFeed;
using LcdBooster;
using NLog;
using TerrainAPI;
using Torch;
using Torch.API;

namespace JetOSExtensions.Server
{
    public sealed class JetOSExtensionsServerPlugin : TorchPluginBase
    {
        const string HarmonyId = "com.terilia.jetos.extensions.server";

        static readonly Logger Log = LogManager.GetCurrentClassLogger();

        readonly TerrainApiFeature _terrain = new TerrainApiFeature();
        readonly LcdBoosterFeature _lcd = new LcdBoosterFeature();
        RadarFeedEngine _radar;
        Harmony _harmony;

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);

            Log.Info("JetOSExtensions.Server: dev build init start.");
            Log.Info("JetOSExtensions.Server: TerrainAPI property feature present.");
            Log.Info("JetOSExtensions.Server: JetOSRadarFeed property feature present.");
            Log.Info("JetOSExtensions.Server: LCD booster server patches feature present.");
            Log.Info("JetOSExtensions.Server: [Ani] canard angle sync feature present.");

            _harmony = new Harmony(HarmonyId);
            try
            {
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                int patchCount = 0;
                foreach (var method in _harmony.GetPatchedMethods())
                {
                    Log.Info("JetOSExtensions.Server: patched " + method.DeclaringType?.FullName + "." + method.Name);
                    patchCount++;
                }
                Log.Info("JetOSExtensions.Server: Harmony patching complete; patchCount=" + patchCount + ".");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "JetOSExtensions.Server: Harmony patching failed.");
            }

            _terrain.Init(torch);
            _lcd.Init(torch);
            _radar = new RadarFeedEngine(message => Log.Info(message));

            Log.Info("JetOSExtensions.Server: dev build init complete.");
        }

        public override void Update()
        {
            UpdateFeature("TerrainAPI", () => _terrain.Update());
            UpdateFeature("LcdBooster", () => _lcd.Update());
            UpdateFeature("JetOSRadarFeed", () => _radar?.Update());
        }

        public override void Dispose()
        {
            Log.Info("JetOSExtensions.Server: dispose start.");
            _radar = null;
            _terrain.Dispose();
            _lcd.Dispose();
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            Log.Info("JetOSExtensions.Server: disposed.");
            base.Dispose();
        }

        void UpdateFeature(string name, Action update)
        {
            try
            {
                update();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "JetOSExtensions.Server: " + name + " update failed.");
            }
        }

    }
}
