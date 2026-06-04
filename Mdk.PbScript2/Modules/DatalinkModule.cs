namespace IngameScript
{
    partial class Program
    {
        // DL viewer (in-jet) — lean menu-list form. The jet can't afford a custom full-frame
        // render page (~620 chars over budget), so it lists HQ's broadcast lines via the default
        // menu page: orders/news + auto roster when an HQ is in range, else a local readout.
        // Leading markup directive chars (#>!~-) are stripped so lines read clean. The RICH
        // full-frame "browser" (headers/rows/alerts/bars) is the Terminal script's job — it has
        // its own 100K budget and reads the same JETOS_DL feed. See docs/datalink-v2.md.
        class DatalinkModule : ProgramModule
        {
            readonly Jet jet;

            public DatalinkModule(Program program, Jet jet) : base(program)
            {
                this.jet = jet;
                name = "DL";
            }

            public override void ExecuteOption(int index) { }

            public override string[] GetOptions()
            {
                var s = DatalinkV2.GetStations();
                Datalink.Node hq = new Datalink.Node();
                for (int i = 0; i < s.Count; i++)
                    if (hq.Id == 0 || s[i].SeenAt > hq.SeenAt) hq = s[i];

                if (hq.Id == 0)
                    return new string[]
                    {
                        "NO HQ LINK",
                        Datalink.GetActiveFriendlies().Count + "W  " + MapContactStoreV2.GetActive().Count + "C"
                    };

                string[] lines = (hq.Text ?? "").Split('\n');
                string[] o = new string[lines.Length + 1];
                o[0] = "HQ  " + (int)(VDi(jet.CockpitPosition, hq.Position) / 1000.0) + " km";
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i];
                    string t = l.Length > 0 && "#>!~-".IndexOf(l[0]) >= 0 ? l.Substring(1).Trim() : l;
                    o[i + 1] = Clip(t, 20, ""); // keep lines inside the menu column so they don't overlap the sidebar
                }
                return o;
            }
        }
    }
}
