using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Right-hand status sidebar shared by the main menu and every module menu — the HQ
        // analogue of the jet's StatusPanelRenderer. Shows live datalink/station health.
        static class StationPanel
        {
            public static void Render(RectangleF area)
            {
                float x = area.Position.X, y = area.Position.Y, w = area.Width, h = area.Height;

                // Panel backing + header strip.
                Sq(x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(x, y, w, h, 1f, MFDTheme.BORDER_LIGHT);
                Sq(x + w / 2f, y + 9f, w, 18f, MFDTheme.HEADER_BG);
                Tx("DATALINK", x + w / 2f, y + 3f, 0.36f, MFDTheme.CORP_GOLD, MFDTheme.AC, null);

                float ry = y + 26f;
                Row(ref ry, x, w, "CH", DatalinkHQ.Channel, MFDTheme.STATUS_VAL);
                Row(ref ry, x, w, "JETS", DatalinkHQ.JetCount.ToString(),
                    DatalinkHQ.JetCount > 0 ? MFDTheme.STATUS_RDY : MFDTheme.DIM_TEXT_MID);
                Row(ref ry, x, w, "CTC", DatalinkHQ.ContactCount.ToString(),
                    DatalinkHQ.ContactCount > 0 ? MFDTheme.WARN : MFDTheme.DIM_TEXT_MID);

                // TACSIT chiclet.
                ry += 4f;
                Color tc = MFDTheme.TacsitColor(DatalinkHQ.Tacsit);
                Sq(x + 6f + 32f, ry + 7f, 64f, 14f, Cr(tc, 0.18f));
                SpriteHelpers.DrawRectangleOutline(x + 6f, ry, 64f, 14f, 0.5f, tc);
                Tx("TACSIT", x + 6f, ry - 9f, 0.26f, MFDTheme.DIM_TEXT_MID, MFDTheme.AL, null);
                Tx(DatalinkHQ.Tacsit, x + 6f + 32f, ry, 0.32f, tc, MFDTheme.AC, null);
                ry += 22f;

                Row(ref ry, x, w, "ANT",
                    DatalinkHQ.LinkOk ? (int)(SystemManager.Station.AntennaRange / 1000.0) + "km" : "OFFLINE",
                    DatalinkHQ.LinkOk ? MFDTheme.STATUS_VAL : MFDTheme.DANGER);
            }

            static void Row(ref float y, float x, float w, string key, string val, Color valColor)
            {
                Tx(key, x + 6f, y, 0.32f, MFDTheme.DIM_TEXT_MID, MFDTheme.AL, null);
                Tx(val, x + w - 6f, y, 0.32f, valColor, MFDTheme.AR, null);
                Sq(x + w / 2f, y + 16f, w - 10f, 1f, MFDTheme.ROW_DIVIDER);
                y += 20f;
            }
        }
    }
}
