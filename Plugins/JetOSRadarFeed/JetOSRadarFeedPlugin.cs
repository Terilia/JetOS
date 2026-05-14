using VRage.Plugins;
using VRage.Utils;

namespace JetOSRadarFeed
{
    public sealed class JetOSRadarFeedPlugin : IPlugin
    {
        RadarFeedEngine _engine;

        public void Init(object gameInstance)
        {
            _engine = new RadarFeedEngine(MyLog.Default.WriteLine);
            MyLog.Default.WriteLine("JetOSRadarFeed: initialized.");
        }

        public void Update()
        {
            _engine?.Update();
        }

        public void Dispose()
        {
            MyLog.Default.WriteLine("JetOSRadarFeed: disposed.");
        }
    }
}
