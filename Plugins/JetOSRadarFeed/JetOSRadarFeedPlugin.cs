using VRage.Plugins;
using VRage.Utils;

namespace JetOSRadarFeed
{
    public sealed class JetOSRadarFeedPlugin : IPlugin
    {
        public void Init(object gameInstance)
        {
            MyLog.Default.WriteLine("JetOSRadarFeed: Pulsar shim loaded; radar feed terminal property is Torch/server-only.");
        }

        public void Update()
        {
        }

        public void Dispose()
        {
            MyLog.Default.WriteLine("JetOSRadarFeed: Pulsar shim disposed.");
        }
    }
}
