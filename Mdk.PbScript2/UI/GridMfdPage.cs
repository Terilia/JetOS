using VRage.Game.GUI.TextPanel;
using MSDF = VRage.Game.GUI.TextPanel.MySpriteDrawFrame;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Wraps the surface-2 (extra screen) status visualization in the standard MFD chrome.
        // The actual rendering stays in GridVisualization — this just supplies the chrome
        // metadata and forwards the content rect.
        class GridMfdPage : MfdPage
        {
            readonly Program _program;
            readonly Jet _jet;
            readonly RadarControlModule _radar;
            readonly HUDModule _hud;

            public GridMfdPage(Program program, Jet jet, RadarControlModule radar, HUDModule hud)
            { _program = program; _jet = jet; _radar = radar; _hud = hud; }

            public override string HeaderRight => "STATUS";

            public override void RenderContent(MSDF frame, RectangleF area, Vector2 surfaceSize)
            {
                GridVisualization.Render(frame, surfaceSize, area, _program, _jet, _radar, _hud);
            }
        }
    }
}
