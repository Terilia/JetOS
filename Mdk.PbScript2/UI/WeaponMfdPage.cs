using VRage.Game.GUI.TextPanel;
using MSDF = VRage.Game.GUI.TextPanel.MySpriteDrawFrame;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Wraps surface-2 (weapons screen) in standard MFD chrome. Content rendering
        // stays inside HUDModule (it owns the radar/missile state).
        class WeaponMfdPage : MfdPage
        {
            readonly HUDModule _hud;
            public WeaponMfdPage(HUDModule hud) { _hud = hud; }

            public override string HeaderRight => "WEAPONS";

            public override void RenderContent(MSDF frame, RectangleF area, Vector2 surfaceSize)
            {
                _hud.RenderWeaponContent(frame, area, surfaceSize);
            }
        }
    }
}
