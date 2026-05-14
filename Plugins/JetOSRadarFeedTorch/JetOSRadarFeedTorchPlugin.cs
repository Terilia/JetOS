using JetOSRadarFeed;
using NLog;
using Torch;
using Torch.API;

namespace JetOSRadarFeedTorch
{
    public sealed class JetOSRadarFeedTorchPlugin : TorchPluginBase
    {
        static readonly Logger Log = LogManager.GetCurrentClassLogger();
        RadarFeedEngine _engine;

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            _engine = new RadarFeedEngine(message => Log.Info(message));
            Log.Info("JetOSRadarFeedTorch: initialized.");
        }

        public override void Update()
        {
            _engine?.Update();
        }

        public override void Dispose()
        {
            Log.Info("JetOSRadarFeedTorch: disposed.");
            _engine = null;
            base.Dispose();
        }
    }
}
