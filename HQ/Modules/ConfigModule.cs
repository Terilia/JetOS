namespace IngameScript
{
    partial class Program
    {
        // Config / Diagnostics — live station health (jets, contacts, relay tracks, instruction
        // count, uptime) plus the operator-tunable thresholds (HQConfig, persisted in PB Storage).
        // Rows at/after SETTINGS_START are adjustable: 5 = decrease, 6 = increase, 3 = toggle relay.
        class ConfigModule : ProgramModule
        {
            const int SETTINGS_START = 5;

            public ConfigModule(Program program) : base(program) { name = "CFG"; }

            public override string[] GetOptions()
            {
                return new[]
                {
                    "JETS " + FleetState.Jets.Count + "  CTC " + FleetState.Contacts.Count,
                    "TACSIT " + FleetState.Tacsit + "  " + FleetState.HostileCount + "H",
                    "RELAY TRK " + DatalinkHQ.RelayCount,
                    "IC " + SystemManager.IC + "  PK " + SystemManager.IP,
                    "UP " + MMSS(SystemManager.ElapsedSeconds),
                    $"ALERT CLOSE {HQConfig.AlertClose / 1000.0:0.#}km",
                    $"ALERT DEF {HQConfig.AlertDefense / 1000.0:0.#}km",
                    $"INBOUND {HQConfig.InboundSpeed:0} m/s",
                    $"BCAST {HQConfig.BroadcastHz:0.#} Hz",
                    "RELAY " + (HQConfig.Relay ? "ON" : "OFF"),
                    $"JET RNG {HQConfig.JetRange / 1000.0:0.#}km",
                };
            }

            public override void ExecuteOption(int index)
            {
                if (index == SETTINGS_START + 4) { HQConfig.Relay = !HQConfig.Relay; HQConfig.Save(); }
            }

            public override void HandleSpecialFunction(int key)
            {
                if (key != 5 && key != 6) return;
                int idx = SystemManager.currentMenuIndex - SETTINGS_START;
                if (idx < 0) return;
                double dir = key == 6 ? 1 : -1;
                switch (idx)
                {
                    case 0: HQConfig.AlertClose = Cl(HQConfig.AlertClose + dir * 500, 500, 30000); break;
                    case 1: HQConfig.AlertDefense = Cl(HQConfig.AlertDefense + dir * 500, 500, 30000); break;
                    case 2: HQConfig.InboundSpeed = Cl(HQConfig.InboundSpeed + dir, 1, 50); break;
                    case 3: HQConfig.BroadcastHz = Cl(HQConfig.BroadcastHz + dir * 0.5, 0.5, 5); break;
                    case 4: HQConfig.Relay = !HQConfig.Relay; break;
                    case 5: HQConfig.JetRange = Cl(HQConfig.JetRange + dir * 500, 0, 100000); break;
                    default: return;
                }
                HQConfig.Save();
            }

            public override string GetHotkeys() => "5- 6+ ADJ  3 TOGGLE";

            static string MMSS(double s)
            {
                int t = (int)s;
                return (t / 60) + ":" + (t % 60).ToString("D2");
            }
        }
    }
}
