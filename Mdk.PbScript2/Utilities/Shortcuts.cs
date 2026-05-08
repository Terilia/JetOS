using System;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // ── Sprite emission shortcuts ──
        // ALL MFD sprite construction lives here. Helpers (MFDFrame.Rect, SpriteHelpers.Bx, etc.)
        // forward to these; they in turn route through SpriteBus so the transition system can
        // tee captures. Keeping the verbose `new MySprite { ... }` initializer in exactly one
        // place is critical for minified output size.
        static void Sq(float cx, float cy, float w, float h, Color c) =>
            SpriteBus.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ,
                Position = V2(cx, cy), Size = V2(w, h), Color = c, Alignment = MFDTheme.AC });
        static void Sq(float cx, float cy, float w, float h, Color c, float r) =>
            SpriteBus.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ,
                Position = V2(cx, cy), Size = V2(w, h), Color = c, Alignment = MFDTheme.AC, RotationOrScale = r });
        static void SqT(string tex, float cx, float cy, float w, float h, Color c, float r = 0f) =>
            SpriteBus.Add(new MySprite { Type = MFDTheme.TX, Data = tex,
                Position = V2(cx, cy), Size = V2(w, h), Color = c, Alignment = MFDTheme.AC, RotationOrScale = r });
        static void Tx(string d, float x, float y, float s, Color c, TextAlignment a, string fn) =>
            SpriteBus.Add(new MySprite { Type = MFDTheme.TT, Data = d,
                Position = V2(x, y), RotationOrScale = s, Color = c, Alignment = a, FontId = fn ?? MFDTheme.FONT });

        static Vector3D LV(IMyShipController c) => c.GetShipVelocities().LinearVelocity;
        static Vector3D GP(IMyEntity e) => e.GetPosition();
        static MatrixD WM(IMyCubeBlock b) => b.WorldMatrix;
        static Vector3D WF(IMyCubeBlock b) => b.WorldMatrix.Forward;
        static Vector3D WR(IMyCubeBlock b) => b.WorldMatrix.Right;
        static Vector3D WU(IMyCubeBlock b) => b.WorldMatrix.Up;
        static Vector2 SS(IMyTextSurface s) => s.SurfaceSize;
        static float SX(IMyTextSurface s) => s.SurfaceSize.X;
        static float SY(IMyTextSurface s) => s.SurfaceSize.Y;
        static Vector3D VN(Vector3D v) => Vector3D.Normalize(v);
        static double VD(Vector3D a, Vector3D b) => Vector3D.Dot(a, b);
        static Vector3D VX(Vector3D a, Vector3D b) => Vector3D.Cross(a, b);
        static double VDi(Vector3D a, Vector3D b) => Vector3D.Distance(a, b);
        static Vector3D VTN(Vector3D v, MatrixD m) => Vector3D.TransformNormal(v, m);
        static double Sn(double v) => Math.Sin(v);
        static double Cs(double v) => Math.Cos(v);
        static double As(double v) => Math.Asin(v);
        static double At2(double y, double x) => Math.Atan2(y, x);
        static double Rd(double v) => Math.Round(v);
        static int Sg(double v) => Math.Sign(v);
        static int Sg(float v) => Math.Sign(v);
        static float Cl(float v, float min, float max) => MathHelper.Clamp(v, min, max);
        static double Cl(double v, double min, double max) => MathHelper.Clamp(v, min, max);
        static double ToDeg(double r) => MathHelper.ToDegrees((float)r);
        static float ToRad(float d) => MathHelper.ToRadians(d);
        static Vector2 V2(float x, float y) => new Vector2(x, y);
        static Color Cr(int r, int g, int b) => new Color(r, g, b);
        static Color Cr(int r, int g, int b, int a) => new Color(r, g, b, a);
        static Color Cr(Color c, float a) => new Color(c, a);
        static double Ab(double v) => Math.Abs(v);
        static float Ab(float v) => Math.Abs(v);
        static int Ab(int v) => Math.Abs(v);
        static float Mn(float a, float b) => Math.Min(a, b);
        static double Mn(double a, double b) => Math.Min(a, b);
        static int Mn(int a, int b) => Math.Min(a, b);
        static float Mx(float a, float b) => Math.Max(a, b);
        static double Mx(double a, double b) => Math.Max(a, b);
        static int Mx(int a, int b) => Math.Max(a, b);
        static readonly Vector3D VZ = Vector3D.Zero;
        const double PI = Math.PI;
        const string TRIM = "Trim";
        const string TEXTURE_SQUARE = "SquareSimple";
        const string TEXTURE_SQUARE_HOLLOW = "SquareHollow";
        const string TEXTURE_CIRCLE = "CircleHollow";
        const string TEXTURE_TRIANGLE = "Triangle";
        const string TEXTURE_CIRCLE_SOLID = "Circle";

        // JetOS sprite mod (Workshop testmod 3720997935). All white-on-transparent,
        // tinted by the script's Color argument.
        const string TEXTURE_FPM = "JetOS_FPM";

        // Horizon / flight reference
        const string TEX_PITCH_POS      = "JetOS_PitchRung_Pos";
        const string TEX_PITCH_NEG      = "JetOS_PitchRung_Neg";
        const string TEX_PITCH_ZERO     = "JetOS_PitchRung_Zero";
        const string TEX_PITCH_INV      = "JetOS_PitchRung_Inverted";
        const string TEX_ROLL_POINTER   = "JetOS_RollPointer";
        const string TEX_BANK_ARC       = "JetOS_BankArc";
        const string TEX_AOA_BRACKET    = "JetOS_AoABracket";
        const string TEX_BORESIGHT      = "JetOS_Boresight";
        const string TEX_HDG_CHEVRON    = "JetOS_HeadingChevron";

        // Tape markers
        const string TEX_TAPE_BUG       = "JetOS_TapeBug";
        const string TEX_TAPE_INDEX     = "JetOS_TapeIndex";

        // Gauges
        const string TEX_GMETER_FACE    = "JetOS_GMeterFace";
        const string TEX_GAUGE_NEEDLE   = "JetOS_GaugeNeedle";

        // Targeting
        const string TEX_TGT_BRACKET    = "JetOS_TargetBracket";
        const string TEX_LEAD_PIP       = "JetOS_LeadPip";

        // Radar contacts
        const string TEX_C_HOSTILE      = "JetOS_Contact_Hostile";
        const string TEX_C_FRIENDLY     = "JetOS_Contact_Friendly";
        const string TEX_C_UNKNOWN      = "JetOS_Contact_Unknown";
        const string TEX_WARNING        = "JetOS_Warning";

        // Radar minimap
        const string TEX_RANGE_RING     = "JetOS_RangeRing";
        const string TEX_OWN_SHIP       = "JetOS_OwnShip";
        const string TEX_LOCK_CONE      = "JetOS_LockCone";
        const string TEX_RADAR_SWEEP    = "JetOS_RadarSweep";

        // Weapons & bays
        const string TEX_MISSILE        = "JetOS_Missile";
        const string TEX_BAY_EMPTY      = "JetOS_Bay_Empty";
        const string TEX_BAY_LOADED     = "JetOS_Bay_Loaded";

        // Status indicators
        const string TEX_FUEL_TANK      = "JetOS_FuelTank";
        const string TEX_BATTERY        = "JetOS_Battery";
        const string TEX_STATUS_DOT     = "JetOS_StatusDot";
        const string TEX_STATUS_RING    = "JetOS_StatusRing";

        // MFD chrome / menu
        const string TEX_MFD_CORNER     = "JetOS_MFD_Corner";
        const string TEX_NAV_ARROW      = "JetOS_NavArrow";

        // Module icons (menu prefix glyphs)
        const string TEX_ICON_HUD       = "JetOS_Icon_HUD";
        const string TEX_ICON_RADAR     = "JetOS_Icon_Radar";
        const string TEX_ICON_WEAPONS   = "JetOS_Icon_Weapons";
        const string TEX_ICON_TERRAIN   = "JetOS_Icon_Terrain";
        const string TEX_ICON_CONFIG    = "JetOS_Icon_Config";
        const string TEX_ICON_CANARD    = "JetOS_Icon_Canard";
        const string TEX_ICON_GUN       = "JetOS_Icon_Gun";

        // Status label icons
        const string TEX_ICON_FUEL      = "JetOS_Icon_Fuel";
        const string TEX_ICON_POWER     = "JetOS_Icon_Power";
        const string TEX_ICON_AMMO      = "JetOS_Icon_Ammo";

        // Background patterns
        const string TEX_BG_SCANLINE    = "JetOS_BG_ScanLine";
        const string TEX_BG_GRIDDOT     = "JetOS_BG_GridDot";

        // Footer / action glyphs
        const string TEX_KEY_HINT_BOX   = "JetOS_KeyHint_Box";
        const string TEX_GLYPH_CHECK    = "JetOS_Glyph_Check";
        const string TEX_GLYPH_CROSS    = "JetOS_Glyph_Cross";
        const string TEX_GLYPH_BACK     = "JetOS_Glyph_Back";

        // ── Batch B additions ──
        const string TEX_AIRCRAFT_SYM   = "JetOS_AircraftSymbol";
        const string TEX_LOCK_DIAMOND   = "JetOS_LockDiamond";
        const string TEX_MASTER_CAUTION = "JetOS_MasterCaution";
        const string TEX_MASTER_WARNING = "JetOS_MasterWarning";
        const string TEX_HATCH          = "JetOS_HatchPattern";
        const string TEX_MISSILE_HEAT   = "JetOS_Missile_Heat";
        const string TEX_MISSILE_RADAR  = "JetOS_Missile_Radar";
        const string TEX_NO_SIGNAL      = "JetOS_NoSignal";
    }
}
