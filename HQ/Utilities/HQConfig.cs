namespace IngameScript
{
    partial class Program
    {
        // Operator-tunable station settings, persisted in the PB's Storage string (NOT Custom
        // Data — that is reserved for the free-text orders screen). Read live by FleetState
        // (alert thresholds) and DatalinkHQ (broadcast cadence + relay enable). Edited from the
        // CFG module. Survives world save/load via Storage.
        static class HQConfig
        {
            public static double AlertClose   = 4000.0; // hostile this close to HQ => TACSIT RED
            public static double AlertDefense = 8000.0; // inbound hostile inside this => TACSIT RED
            public static double InboundSpeed = 5.0;    // m/s closing toward HQ to count as INBOUND
            public static double BroadcastHz  = 1.0;    // STATION broadcasts per second
            public static bool   Relay        = true;   // re-broadcast fused contacts on the antenna
            public static double JetRange     = 10000.0;// assumed friendly-jet antenna range (map coverage bubbles)

            private static Program _p;

            // Storage is now shared (StorageDoc): read/write only our own keys so ZoneStore can
            // coexist. StorageDoc.Init must have run first (SystemManager.Initialize).
            public static void Load(Program p)
            {
                _p = p;
                string v;
                if (StorageDoc.TryGet("ac", out v)) Db(v, ref AlertClose);
                if (StorageDoc.TryGet("ad", out v)) Db(v, ref AlertDefense);
                if (StorageDoc.TryGet("is", out v)) Db(v, ref InboundSpeed);
                if (StorageDoc.TryGet("bh", out v)) Db(v, ref BroadcastHz);
                if (StorageDoc.TryGet("rl", out v)) Relay = v == "1";
                if (StorageDoc.TryGet("jr", out v)) Db(v, ref JetRange);
            }

            public static void Save()
            {
                if (_p == null) return;
                StorageDoc.Set("ac", AlertClose.ToString());
                StorageDoc.Set("ad", AlertDefense.ToString());
                StorageDoc.Set("is", InboundSpeed.ToString());
                StorageDoc.Set("bh", BroadcastHz.ToString());
                StorageDoc.Set("rl", Relay ? "1" : "0");
                StorageDoc.Set("jr", JetRange.ToString());
                StorageDoc.Flush();
            }

            static void Db(string s, ref double field)
            {
                double d;
                if (double.TryParse(s, out d)) field = d;
            }
        }
    }
}
