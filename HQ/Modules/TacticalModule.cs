using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Tactical / Threat board — the sensor-fusion centerpiece. Renders FleetState's deduped
        // hostiles (nearest first) under a TACSIT banner, with INBOUND flagging and a
        // neutral/unknown count. Read-only custom page.
        class TacticalModule : ProgramModule
        {
            public TacticalModule(Program program) : base(program) { name = "TAC"; }

            public override MfdPage GetPage() => new TacticalPage();
            public override string[] GetOptions() => new string[0];
            public override void ExecuteOption(int index) { }
            // Read-only board — swallow up/down so the menu cursor doesn't wander.
            public override bool HandleNavigation(bool isUp) => true;

            class TacticalPage : MfdPage
            {
                public override string HeaderRight => "TAC";
                public override bool ShowFooterNav => true;
                public override bool ShowBreadcrumb => true;
                public override string BreadcrumbPath => "TAC";
                public override string FooterRight => "FLEET FUSION";
                public override bool HasSidebar => true;
                public override void RenderSidebar(RectangleF area) => StationPanel.Render(area);

                public override void RenderContent(RectangleF area, Vector2 ss)
                {
                    float k = ss.Y / 512f;
                    float x = area.Position.X, y = area.Position.Y, w = area.Width;

                    // TACSIT banner.
                    Color tc = MFDTheme.TacsitColor(FleetState.Tacsit);
                    float bh = 24f * k;
                    Sq(x + w / 2f, y + bh / 2f, w, bh, Cr(tc, 0.15f));
                    SpriteHelpers.DrawRectangleOutline(x, y, w, bh, 1f, tc);
                    SpriteHelpers.Tt("TACSIT " + FleetState.Tacsit, x + 6f * k, y + 5f * k, 0.5f * k, tc, MFDTheme.AL);
                    SpriteHelpers.Tt(FleetState.HostileCount + "H  " + FleetState.InboundCount + " IN",
                        x + w - 6f * k, y + 6f * k, 0.4f * k, MFDTheme.NORMAL_TEXT, MFDTheme.AR);
                    y += bh + 8f * k;

                    SpriteHelpers.Tt("THREATS", x + 6f * k, y, 0.36f * k, MFDTheme.MID_TEXT, MFDTheme.AL);
                    y += 18f * k;

                    var thr = FleetState.SortedThreats;
                    if (thr.Count == 0)
                    {
                        SpriteHelpers.Tt("NO THREATS TRACKED", x + w / 2f, y + 10f * k, 0.4f * k, MFDTheme.DIM_TEXT, MFDTheme.AC);
                        return;
                    }

                    float rowH = 22f * k;
                    float bottom = area.Position.Y + area.Height;
                    int maxRows = (int)((bottom - y - 14f * k) / rowH);
                    int shown = Mn(Mn(thr.Count, Mx(0, maxRows)), 10);
                    for (int i = 0; i < shown; i++) { DrawThreat(x, y, w, rowH, k, thr[i]); y += rowH; }
                    if (thr.Count > shown)
                        SpriteHelpers.Tt("+" + (thr.Count - shown) + " MORE", x + 6f * k, y + 3f * k, 0.32f * k, MFDTheme.DIM_TEXT, MFDTheme.AL);
                }

                static void DrawThreat(float x, float y, float w, float rowH, float k, FleetState.Contact c)
                {
                    Color rc = c.Inbound ? MFDTheme.DANGER : MFDTheme.NORMAL_TEXT;
                    Sq(x + w / 2f, y + rowH / 2f, w, rowH - 2f * k, MFDTheme.PANEL_BG);
                    Sq(x + 2f * k, y + rowH / 2f, 3f * k, rowH - 3f * k, rc);
                    SpriteHelpers.Sp(TEX_C_HOSTILE, x + 13f * k, y + rowH / 2f, 10f * k, 10f * k, rc);
                    SpriteHelpers.Tt(Clip(c.Name, 12, "BANDIT"), x + 22f * k, y + 3f * k, 0.36f * k,
                        c.Inbound ? MFDTheme.BRIGHT_TEXT : MFDTheme.NORMAL_TEXT, MFDTheme.AL);
                    string r = SpriteHelpers.FormatRange(c.Range) + (c.Inbound ? "  INB" : "");
                    SpriteHelpers.Tt(r, x + w - 6f * k, y + 3f * k, 0.34f * k, rc, MFDTheme.AR);
                }
            }
        }
    }
}
