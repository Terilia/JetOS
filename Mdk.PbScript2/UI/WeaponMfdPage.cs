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

            public override string HeaderRight => "WPN";

            public override void RenderContent(RectangleF area, Vector2 surfaceSize)
            {
                _hud.RenderWeaponContent(area, surfaceSize);
            }
        }
    }
}
