namespace IngameScript
{
    partial class Program
    {
        // Orders & Comms — operator command vocabulary. Free-text orders/news live in the PB's
        // Custom Data (broadcast verbatim on top of the STATION screen); this menu issues the
        // small machine-readable vocabulary that sets the broadcast order-type + a banner the
        // whole fleet sees. (Auto-orders are intentionally out of scope.)
        class OrdersModule : ProgramModule
        {
            public OrdersModule(Program program) : base(program) { name = "ORDERS"; }

            public override string[] GetOptions()
            {
                string cmd = DatalinkHQ.CommandBanner.Length > 0 ? DatalinkHQ.CommandBanner : "(none)";
                return new[]
                {
                    "CMD: " + cmd,
                    "RECALL ALL",
                    "RTB ALL",
                    "WEAPONS FREE",
                    "WEAPONS HOLD",
                    "CLEAR ORDER",
                };
            }

            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 1: DatalinkHQ.SetCommand("RECALL ALL", 3); break;   // order-type 3 = recall
                    case 2: DatalinkHQ.SetCommand("RTB ALL", 1); break;      // order-type 1 = order
                    case 3: DatalinkHQ.SetCommand("WEAPONS FREE", 1); break;
                    case 4: DatalinkHQ.SetCommand("WEAPONS HOLD", 1); break;
                    case 5: DatalinkHQ.SetCommand("", 0); break;             // clear
                }
            }

            public override string GetHotkeys() => "ORDERS TEXT = PB DATA";
        }
    }
}
