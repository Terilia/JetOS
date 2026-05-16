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
        bool _canardLoggedOnce;

        public void Init(object gameInstance)
        {
            MyLog.Default.WriteLine("JetOSExtensions.Client: init start.");
            if (Plugin.Settings.DebugLogging)
            {
                MyLog.Default.WriteLine("JetOSExtensions.Client: CAMOV camera LCD + PB overlay feature present.");
                MyLog.Default.WriteLine("JetOSExtensions.Client: CAMOV client lifecycle diagnostics enabled.");
                MyLog.Default.WriteLine("JetOSExtensions.Client: 60 FPS LCD client patch feature present.");
                MyLog.Default.WriteLine("JetOSExtensions.Client: [Ani] canard animation fix feature present.");
                MyLog.Default.WriteLine("JetOSExtensions.Client: radar shim present; server-only property '" + RadarFeedProtocol.PropertyName + "' will not be registered on the client.");
            }

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            int patchCount = 0;
            foreach (var method in _harmony.GetPatchedMethods())
            {
                if (Plugin.Settings.DebugLogging)
                    MyLog.Default.WriteLine("JetOSExtensions.Client: patched " + method.DeclaringType?.FullName + "." + method.Name);
                patchCount++;
            }

            MyLog.Default.WriteLine("JetOSExtensions.Client: init complete; patchCount=" + patchCount + ".");
        }

        public void Update()
        {
            _cameraLcd.Update();
            UpdateCanards();
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

    }
}
