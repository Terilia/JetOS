using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // ZONES — the operator-drawn regions program. Selecting it enters "ZONE mode"
        // (SystemManager.ZoneActive): the stitched mouse cursor goes live, the MFD shows this tool
        // palette (left canvas) + zone list, and the MAP shows the world plot + draft overlay +
        // cursor (right canvas). The mouse drives the cursor; left-click (= click-gun fire) places
        // vertices / a circle / selects zones, or presses a palette button. Toolbar 5-8 mirror the
        // main tools so the editor stays usable without a mouse; W/S zoom the map (ZoneEditor.Tick).
        class ZonesModule : ProgramModule
        {
            public ZonesModule(Program program) : base(program) { name = "ZONES"; }

            public override MfdPage GetPage() => new ZonesPage();
            public override string[] GetOptions() => new string[0];
            public override void ExecuteOption(int index) { }
            // The cursor owns navigation while in ZONE mode — swallow up/down.
            public override bool HandleNavigation(bool isUp) => true;
            // Back undoes/cancels an in-progress draft; only exits the module when idle.
            public override bool HandleBack()
            {
                if (ZoneEditor.Placing) { ZoneEditor.Undo(); return true; }
                return false;
            }
            // Toolbar fallbacks (no mouse needed): 5 poly, 6 circle, 7 kind, 8 close.
            public override void HandleSpecialFunction(int key)
            {
                switch (key)
                {
                    case 5: ZoneEditor.DoAction(ZoneEditor.A_NEWPOLY); break;
                    case 6: ZoneEditor.DoAction(ZoneEditor.A_NEWCIRCLE); break;
                    case 7: ZoneEditor.DoAction(ZoneEditor.A_KIND); break;
                    case 8: ZoneEditor.DoAction(ZoneEditor.A_CLOSE); break;
                }
            }
            public override string GetHotkeys() => "5POLY 6CIRC 7KIND 8CLOSE";

            class ZonesPage : MfdPage
            {
                public override string HeaderRight => "ZONE";
                public override bool ShowFooterNav => true;
                public override bool ShowBreadcrumb => true;
                public override string BreadcrumbPath => "ZONES";
                public override string FooterRight => "ZONE EDITOR";

                public override void RenderContent(RectangleF area, Vector2 ss)
                {
                    float k = ss.Y / 512f;
                    float x = area.Position.X, y = area.Position.Y, w = area.Width;
                    ZoneEditor.ClearButtons();

                    // Status + active kind.
                    SpriteHelpers.Tt(ZoneEditor.StatusLine(), x + w / 2f, y, 0.34f * k, MFDTheme.MID_TEXT, MFDTheme.AC);
                    y += 20f * k;

                    // Tool buttons (2 columns).
                    float gap = 4f * k, bw = (w - gap) / 2f, bh = 26f * k;
                    float c0 = x, c1 = x + bw + gap;
                    Button(c0, y, bw, bh, k, "NEW POLY", ZoneEditor.A_NEWPOLY);
                    Button(c1, y, bw, bh, k, "NEW CIRCLE", ZoneEditor.A_NEWCIRCLE); y += bh + gap;
                    Button(c0, y, bw, bh, k, "KIND " + ZoneEditor.KindName(ZoneEditor.Kind), ZoneEditor.A_KIND);
                    Button(c1, y, bw, bh, k, "CLOSE", ZoneEditor.A_CLOSE); y += bh + gap;
                    Button(c0, y, bw, bh, k, "UNDO", ZoneEditor.A_UNDO);
                    Button(c1, y, bw, bh, k, "DELETE", ZoneEditor.A_DELETE); y += bh + gap;
                    Button(c0, y, bw, bh, k, "SAVE", ZoneEditor.A_SAVE);
                    SpriteHelpers.Tt(ZoneStore.Zones.Count + "/" + ZoneStore.MAX_ZONES,
                        c1 + bw / 2f, y + bh * 0.28f, 0.3f * k, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                    y += bh + gap + 6f * k;

                    // Zone list (clickable rows → select).
                    SpriteHelpers.Tt("ZONES", x + 2f * k, y, 0.3f * k, MFDTheme.DIM_TEXT_MID, MFDTheme.AL);
                    y += 16f * k;
                    var zs = ZoneStore.Zones;
                    float rh = 17f * k, bottom = area.Position.Y + area.Height;
                    for (int i = 0; i < zs.Count && y + rh <= bottom; i++)
                    {
                        ZoneRow(x, y, w, rh, k, i, zs[i]);
                        y += rh;
                    }

                    // (cursor + click diagnostic are drawn globally by UIController.Render)
                }

                void Button(float bx, float by, float bw, float bh, float k, string label, int action)
                {
                    Sq(bx + bw / 2f, by + bh / 2f, bw, bh, Hover(bx, by, bw, bh) ? MFDTheme.SEL_FILL : MFDTheme.PANEL_BG);
                    bool hv = Hover(bx, by, bw, bh);
                    SpriteHelpers.DrawRectangleOutline(bx, by, bw, bh, 1f, hv ? MFDTheme.ACCENT : MFDTheme.BORDER);
                    SpriteHelpers.Tt(label, bx + bw / 2f, by + bh * 0.28f, 0.3f * k,
                        hv ? MFDTheme.BRIGHT_TEXT : MFDTheme.NORMAL_TEXT, MFDTheme.AC);
                    ZoneEditor.AddButton(new RectangleF(bx, by, bw, bh), action);
                }

                void ZoneRow(float x, float y, float w, float rh, float k, int i, Zone z)
                {
                    bool sel = ZoneEditor.IsSelected(z);
                    if (Hover(x, y, w, rh) || sel) Sq(x + w / 2f, y + rh / 2f, w, rh, MFDTheme.SEL_FILL);
                    Sq(x + 3f * k, y + rh / 2f, 3f * k, rh - 4f * k, MapView.ZoneColor(z.Kind));
                    SpriteHelpers.Tt(Clip(z.Name, 14, "ZONE"), x + 9f * k, y + rh * 0.15f, 0.3f * k,
                        sel ? MFDTheme.BRIGHT_TEXT : MFDTheme.NORMAL_TEXT, MFDTheme.AL);
                    string meta = "#" + z.Id + "  " + (z.Shape == ZoneShape.Circle ? SpriteHelpers.FormatRange(z.Radius) : (z.Verts.Count + "pt"));
                    SpriteHelpers.Tt(meta, x + w - 4f * k, y + rh * 0.15f, 0.28f * k, MFDTheme.DIM_TEXT_MID, MFDTheme.AR);
                    ZoneEditor.AddButton(new RectangleF(x, y, w, rh), 100 + i);
                }

                // Cursor-over-rect test in MFD-surface coords (cursor Y clamped to the MFD height, to
                // match how the cursor is drawn when the two surfaces differ in size).
                static bool Hover(float bx, float by, float bw, float bh)
                {
                    if (!MouseCursor.Visible || Canvas.OnRight(MouseCursor.X)) return false;
                    float cy = Cl(MouseCursor.Y, 0f, Canvas.LH);
                    return MouseCursor.X >= bx && MouseCursor.X <= bx + bw && cy >= by && cy <= by + bh;
                }
            }
        }
    }
}
