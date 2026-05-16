using System;
using System.Reflection;
using HarmonyLib;
using JetOSRadarFeed;
using LcdBooster;
using NLog;
using Sandbox.ModAPI;
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
        int _tick;
        int _lastHeartbeatSecond;

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
            _tick++;
            UpdateFeature("TerrainAPI", () => _terrain.Update());
            UpdateFeature("LcdBooster", () => _lcd.Update());
            UpdateFeature("JetOSRadarFeed", () => _radar?.Update());
            LogHeartbeat();
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

        void LogHeartbeat()
        {
            int second = _tick / 60;
            if (second == _lastHeartbeatSecond)
                return;
            _lastHeartbeatSecond = second;

            int frame = MyAPIGateway.Session?.GameplayFrameCounter ?? -1;
            Log.Info("JetOSExtensions.Server: heartbeat tick=" + _tick
                + " gameFrame=" + frame
                + " terrainRegistered=" + _terrain.Registered
                + " terrainSubs=" + _terrain.SubscriptionCount
                + " terrainDownloads=" + _terrain.DownloadCount
                + " terrainResponses=" + _terrain.ResponseCount
                + " terrainPlanets=" + _terrain.PlanetCount
                + " radarRegistered=" + (_radar?.PropertyRegistered ?? false)
                + " radarSeq=" + (_radar?.Sequence ?? 0)
                + " radarFeeds=" + (_radar?.ConstructFeedCount ?? 0)
                + " lcdCallSitesPatched=" + _lcd.CallSitesPatched
                + " lcdCallSites=" + _lcd.PatchedCallSiteCount
                + " canardResolved=" + _lcd.CanardResolverReady
                + " canardsTracked=" + _lcd.TrackedCanardCount);
        }
    }
}
