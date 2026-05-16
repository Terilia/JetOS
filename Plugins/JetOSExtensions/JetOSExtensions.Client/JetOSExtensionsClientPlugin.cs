using System;
using System.Reflection;
using CameraLCD;
using HarmonyLib;
using JetOSExtensions.Shared;
using LcdBoosterClient;
using VRage.Plugins;
using VRage.Utils;

namespace JetOSExtensions.Client
{
    public sealed class JetOSExtensionsClientPlugin : IPlugin
    {
        const string HarmonyId = "com.terilia.jetos.extensions.client";

        readonly Plugin _cameraLcd = new Plugin();
        Harmony _harmony;
        uint _tick;
        uint _lastHeartbeatSecond;
        bool _canardLoggedOnce;

        public void Init(object gameInstance)
        {
            MyLog.Default.WriteLine("JetOSExtensions.Client: dev build init start.");
            MyLog.Default.WriteLine("JetOSExtensions.Client: CAMOV camera LCD + PB overlay feature present.");
            MyLog.Default.WriteLine("JetOSExtensions.Client: 60 FPS LCD client patch feature present.");
            MyLog.Default.WriteLine("JetOSExtensions.Client: [Ani] canard animation fix feature present.");
            MyLog.Default.WriteLine("JetOSExtensions.Client: radar shim present; server-only property '" + RadarFeedProtocol.PropertyName + "' will not be registered on the client.");

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            int patchCount = 0;
            foreach (var method in _harmony.GetPatchedMethods())
            {
                MyLog.Default.WriteLine("JetOSExtensions.Client: patched " + method.DeclaringType?.FullName + "." + method.Name);
                patchCount++;
            }

            MyLog.Default.WriteLine("JetOSExtensions.Client: dev build init complete; patchCount=" + patchCount + ".");
        }

        public void Update()
        {
            _tick++;
            _cameraLcd.Update();
            UpdateCanards();
            LogHeartbeat();
        }

        public void OpenConfigDialog()
        {
            _cameraLcd.OpenConfigDialog();
        }

        public void Dispose()
        {
            MyLog.Default.WriteLine("JetOSExtensions.Client: dispose start.");
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            MyLog.Default.WriteLine("JetOSExtensions.Client: disposed.");
        }

        void UpdateCanards()
        {
            try
            {
                CanardAnimFix.Update();
            }
            catch (Exception ex)
            {
                if (_canardLoggedOnce)
                    return;
                _canardLoggedOnce = true;
                MyLog.Default.WriteLine("JetOSExtensions.Client: CanardAnimFix.Update crash: " + ex);
            }
        }

        void LogHeartbeat()
        {
            uint second = _tick / 60;
            if (second == _lastHeartbeatSecond)
                return;
            _lastHeartbeatSecond = second;

            MyLog.Default.WriteLine("JetOSExtensions.Client: heartbeat tick=" + _tick
                + " camovEnabled=" + Plugin.Settings.Enabled
                + " camovRange=" + Plugin.Settings.Range
                + " camovRatio=" + Plugin.Settings.Ratio
                + " lcd60fpsPatch=loaded"
                + " canardFix=active"
                + " radarClientProperty=not-registered");
        }
    }
}
