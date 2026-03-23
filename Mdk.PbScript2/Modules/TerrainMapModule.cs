using System;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class TerrainMapModule : ProgramModule
        {
            Jet jet;
            IMyCockpit cockpit;

            static readonly int[] ZOOM_STRIDE = { 1, 2, 3, 5 };
            static readonly string[] ZOOM_LABEL = { "2km", "4km", "6km", "10km" };
            int zoomLevel = 1;

            const int DISP = 24;

            public TerrainMapModule(Program program, Jet jet) : base(program)
            {
                this.jet = jet;
                this.cockpit = jet._cockpit;
                name = "Terrain Map";
            }

            public override bool HasCustomScreen => true;
            public override string[] GetOptions() => new string[] { "Back to Main Menu" };
            public override void ExecuteOption(int index)
            {
                if (index == 0) SystemManager.ReturnToMainMenu();
            }

            public override bool HandleNavigation(bool isUp)
            {
                if (isUp && zoomLevel > 0) zoomLevel--;
                else if (!isUp && zoomLevel < ZOOM_STRIDE.Length - 1) zoomLevel++;
                return true;
            }

            public override void RenderCustomScreen(MySpriteDrawFrame frame, RectangleF area)
            {
                float sw = area.Width, sh = area.Height;
                float padX = sw * 0.019f;

                float contentY = MFDFrame.DrawChrome(frame, sw, sh, headerRight: "TERRAIN MAP");
                float contentBot = MFDFrame.ContentBottom(sh);

                // Breadcrumb
                float bcH = sh * 0.044f;
                MFDFrame.Rect(frame, sw / 2f, contentY + bcH / 2f, sw, bcH, MFDTheme.BC_BG);
                MFDFrame.Rect(frame, sw / 2f, contentY + bcH, sw, 1f, MFDTheme.BC_BORDER);
                float bcScale = sh * 0.00055f * 1.1f;
                MFDFrame.Txt(frame, "SYSTEM MENU", padX, contentY + bcH * 0.15f, bcScale, MFDTheme.DIM_TEXT);
                MFDFrame.Txt(frame, ">", padX + sw * 0.16f, contentY + bcH * 0.15f, bcScale, MFDTheme.BORDER);
                MFDFrame.Txt(frame, "TERRAIN MAP", padX + sw * 0.18f, contentY + bcH * 0.15f, bcScale, MFDTheme.NORMAL_TEXT);
                contentY += bcH + 2f;

                if (jet.CachedGravity.LengthSquared() < 0.01)
                { MFDFrame.Txt(frame, "NO PLANET", sw / 2f, sh / 2f - 12f, 0.7f, MFDTheme.DIM_TEXT, MFDTheme.AC); return; }
                if (!TerrainAPI.IsAvailable)
                {
                    MFDFrame.Txt(frame, "TERRAIN API UNAVAILABLE", sw / 2f, sh / 2f - 20f, 0.55f, MFDTheme.DIM_TEXT, MFDTheme.AC);
                    return;
                }

                // Zoom bar
                float zoomY = contentY + 2f;
                MFDFrame.Txt(frame, "1\u25B2 ZOOM 2\u25BC", padX, zoomY, 0.35f, MFDTheme.DIM_TEXT);
                MFDFrame.Txt(frame, ZOOM_LABEL[zoomLevel], sw - padX, zoomY, 0.4f, MFDTheme.ACCENT, MFDTheme.AR);
                float dotY = zoomY + 6f;
                float dotLeft = sw * 0.35f;
                float dotSpacing = (sw * 0.30f) / (ZOOM_STRIDE.Length - 1);
                for (int i = 0; i < ZOOM_STRIDE.Length; i++)
                {
                    float dx = dotLeft + i * dotSpacing;
                    bool sel = i == zoomLevel;
                    MFDFrame.Rect(frame, dx, dotY, sel ? 6f : 3f, sel ? 6f : 3f,
                        sel ? MFDTheme.ACCENT : MFDTheme.BORDER);
                }

                if (!TerrainAPI.IsReady)
                { MFDFrame.Txt(frame, TerrainAPI.IsLoading ? "LOADING..." : "SCANNING...", sw / 2f, sh / 2f, 0.55f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC); return; }

                // Map area (square, centered)
                float mapTop = zoomY + 16f;
                float mapBot = contentBot - 30f;
                float mapAvail = Math.Min(sw - padX * 2, mapBot - mapTop);
                float mapLeft = (sw - mapAvail) / 2f;
                float cell = mapAvail / DISP;

                float cx = mapLeft + mapAvail / 2f;
                MFDFrame.Txt(frame, "FWD", cx, mapTop - 10f, 0.3f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);

                Vector3D shipPos = cockpit.GetPosition();
                int sRow, sCol;
                TerrainAPI.WorldToGrid(shipPos, out sRow, out sCol);
                double shipAlt = TerrainAPI.ShipAlt(shipPos);

                Vector3D jF, jR;
                TerrainRenderer.JetAxes(jet, out jF, out jR);

                // Contour lines (cached, heading-up, marching squares)
                TerrainRenderer.DrawContours(frame, mapLeft, mapTop, cell, DISP,
                    sRow, sCol, ZOOM_STRIDE[zoomLevel], shipAlt, 2f, jF, jR, 0);

                // Grid outline + range ring + ship marker
                SpriteHelpers.DrawRectangleOutline(frame, mapLeft, mapTop,
                    mapAvail, mapAvail, 1f, MFDTheme.BORDER);
                float ccx = mapLeft + mapAvail / 2f, ccy = mapTop + mapAvail / 2f;
                SpriteHelpers.DrawCircleOutline(frame, new Vector2(ccx, ccy),
                    mapAvail * 0.25f, new Color(MFDTheme.BORDER, 0.4f), 1f);
                SpriteHelpers.Sp(frame, "Triangle", ccx, ccy, 10f, 10f, MFDTheme.BRIGHT_TEXT);

                // Footer
                float footY = mapTop + mapAvail + 2f;
                double agl = TerrainAPI.AGL(shipPos);
                Color aglC = agl < 100 ? new Color(180, 50, 40) : agl < 200 ? new Color(160, 140, 40) : MFDTheme.STATUS_VAL;
                MFDFrame.Txt(frame, $"AGL {agl:F0}m", padX, footY, 0.4f, aglC);
                int viewM = DISP * ZOOM_STRIDE[zoomLevel] * (int)TerrainAPI.CellSize;
                MFDFrame.Txt(frame, viewM >= 1000 ? $"{viewM / 1000f:F1}km" : $"{viewM}m",
                    sw - padX, footY, 0.35f, MFDTheme.DIM_TEXT, MFDTheme.AR);

                // Legend
                float legY = footY + 14f;
                Leg(frame, padX, legY, new Color(180, 50, 40), "CFIT");
                Leg(frame, padX + 48f, legY, new Color(160, 140, 40), "<50");
                Leg(frame, padX + 92f, legY, new Color(64, 140, 48), "<150");
            }

            static void Leg(MySpriteDrawFrame frame, float x, float y, Color c, string l)
            {
                MFDFrame.Rect(frame, x + 4f, y + 4f, 8f, 8f, c);
                MFDFrame.Txt(frame, l, x + 11f, y - 2f, 0.28f, MFDTheme.DIM_TEXT);
            }
        }
    }
}
