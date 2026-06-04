namespace IngameScript
{
    partial class Program
    {
        // Tactical Map controls. The live map renders on the dedicated HQ MAP screen (or, with
        // none present, full-frame on this MFD when MAP is selected). This page is the
        // keyboard/legend control panel; pan/zoom also work directly from the command seat
        // (mouse + W/S) whenever it's manned. Blips persist (last-known) — CLEAR drops stale ones.
        class MapModule : ProgramModule
        {
            public MapModule(Program program) : base(program) { name = "MAP"; }

            public override string[] GetOptions()
            {
                bool dedicated = SystemManager.Station.Map != null;
                return new[]
                {
                    "RECENTER ON HQ",
                    "ZOOM IN",
                    "ZOOM OUT",
                    "LABELS " + (MapView.ShowLabels ? "ON" : "OFF"),
                    "TERRAIN " + (MapView.ShowTerrain ? "ON" : "OFF"),
                    "SEEKER " + (MapView.SeekerOn ? "[X]" : "[ ]"),
                    "CLEAR STALE (" + MapView.StaleCount + ")",
                    "SCALE " + MapView.ScaleLabel,
                    "OUT " + (dedicated ? "HQ MAP" : "THIS MFD"),
                    "- LEGEND -",
                    "HQ gold  JET grn",
                    "HOSTILE red  UNK gry",
                };
            }

            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0: MapView.Recenter(); break;
                    case 1: MapView.ZoomIn(); break;
                    case 2: MapView.ZoomOut(); break;
                    case 3: MapView.ShowLabels = !MapView.ShowLabels; break;
                    case 4: MapView.ShowTerrain = !MapView.ShowTerrain; break;
                    case 5: MapView.ToggleSeeker(); break;
                    case 6: MapView.ClearStale(); break;
                }
            }

            // Toolbar control so the map is drivable without a mouse (esp. the MFD fallback).
            public override void HandleSpecialFunction(int key)
            {
                if (key == 5) MapView.ZoomOut();
                else if (key == 6) MapView.ZoomIn();
                else if (key == 7) MapView.Recenter();
                // key 8 (seeker toggle) is handled globally in SystemManager — not here, to avoid a
                // double-toggle.
            }

            public override string GetHotkeys() => "5/6 ZM 7 CTR 8 SEEK";
        }
    }
}
