using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Wing roster — per-jet telemetry triage from FleetState, urgency-sorted (spiked/bingo/
        // defending float to the top). Each row: callsign, state chip, fuel/integrity/missiles,
        // a fuel bar, and range to HQ. Read-only custom page.
        class RosterModule : ProgramModule
        {
            public RosterModule(Program program) : base(program) { name = "WING"; }

            public override MfdPage GetPage() => new WingPage();
            public override string[] GetOptions() => new string[0];
            public override void ExecuteOption(int index) { }
            public override bool HandleNavigation(bool isUp) => true;

            class WingPage : MfdPage
            {
                public override string HeaderRight => "WING";
                public override bool ShowFooterNav => true;
                public override bool ShowBreadcrumb => true;
                public override string BreadcrumbPath => "WING";
                public override string FooterRight => "ROSTER";
                public override bool HasSidebar => true;
                public override void RenderSidebar(RectangleF area) => StationPanel.Render(area);

                public override void RenderContent(RectangleF area, Vector2 ss)
                {
                    float k = ss.Y / 512f;
                    float x = area.Position.X, y = area.Position.Y, w = area.Width;

                    var jets = FleetState.SortedJets;
                    int bingo = 0;
                    for (int i = 0; i < jets.Count; i++)
                        if (StatusWord.Bingo(jets[i].Word) || StatusWord.State(jets[i].Word) == 5) bingo++;

                    SpriteHelpers.Tt("WING " + jets.Count, x + 6f * k, y, 0.4f * k, MFDTheme.MID_TEXT, MFDTheme.AL);
                    if (bingo > 0)
                        SpriteHelpers.Tt(bingo + " BINGO", x + w - 6f * k, y + 1f * k, 0.36f * k, MFDTheme.WARN, MFDTheme.AR);
                    y += 18f * k;

                    if (jets.Count == 0)
                    {
                        SpriteHelpers.Tt("NO WING CONTACT", x + w / 2f, y + 10f * k, 0.4f * k, MFDTheme.DIM_TEXT, MFDTheme.AC);
                        return;
                    }

                    float rowH = 28f * k;
                    float bottom = area.Position.Y + area.Height;
                    int maxRows = (int)((bottom - y) / rowH);
                    int shown = Mn(jets.Count, Mx(0, maxRows));
                    for (int i = 0; i < shown; i++) { DrawJet(x, y, w, rowH, k, jets[i]); y += rowH; }
                    if (jets.Count > shown)
                        SpriteHelpers.Tt("+" + (jets.Count - shown) + " MORE", x + 6f * k, y + 2f * k, 0.32f * k, MFDTheme.DIM_TEXT, MFDTheme.AL);
                }

                static void DrawJet(float x, float y, float w, float rowH, float k, FleetState.JetEntry j)
                {
                    long word = j.Word;
                    Color uc = UrgColor(word);
                    int fuel = StatusWord.Fuel(word);

                    Sq(x + w / 2f, y + rowH / 2f, w, rowH - 2f * k, MFDTheme.PANEL_BG);
                    Sq(x + 2f * k, y + rowH / 2f, 3f * k, rowH - 3f * k, uc);

                    SpriteHelpers.Tt(FleetState.CallSign(j), x + 10f * k, y + 3f * k, 0.4f * k, MFDTheme.BRIGHT_TEXT, MFDTheme.AL);
                    SpriteHelpers.Tt(StatusWord.StateStr(StatusWord.State(word)), x + w - 6f * k, y + 3f * k, 0.34f * k, uc, MFDTheme.AR);

                    string stats = "F" + fuel + "  H" + StatusWord.Integ(word) + "  " + StatusWord.Missiles(word) + "m";
                    SpriteHelpers.Tt(stats, x + 10f * k, y + 15f * k, 0.32f * k, MFDTheme.NORMAL_TEXT, MFDTheme.AL);
                    SpriteHelpers.Tt(SpriteHelpers.FormatRange(j.Range), x + w - 6f * k, y + 16f * k, 0.3f * k, MFDTheme.DIM_TEXT_MID, MFDTheme.AR);

                    float by = y + rowH - 4f * k, bw = w - 14f * k, bx = x + 8f * k;
                    Sq(bx + bw / 2f, by, bw, 2.5f * k, MFDTheme.BAR_TRACK);
                    float fw = bw * Cl(fuel / 100f, 0f, 1f);
                    Color fc = fuel < 20 ? MFDTheme.DANGER : fuel < 50 ? MFDTheme.WARN : MFDTheme.ACCENT;
                    if (fw > 1f) Sq(bx + fw / 2f, by, fw, 2.5f * k, fc);
                }

                static Color UrgColor(long w)
                {
                    if (StatusWord.Spiked(w)) return MFDTheme.DANGER;
                    int st = StatusWord.State(w);
                    if (StatusWord.Bingo(w) || st == 5) return MFDTheme.WARN;
                    if (st == 3 || StatusWord.Rwr(w)) return MFDTheme.WARN;
                    if (st == 2) return MFDTheme.ACCENT;
                    return MFDTheme.MID_TEXT;
                }
            }
        }
    }
}
