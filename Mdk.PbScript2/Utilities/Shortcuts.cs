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
        static bool SE(string s) => string.IsNullOrEmpty(s);
        static bool SW(string s) => string.IsNullOrWhiteSpace(s);
        static void PrepSurface(IMyTextSurface s)
        {
            if (s == null) return;
            s.ContentType = ContentType.SCRIPT;
            s.Script = "";
            s.BackgroundColor = Color.Transparent;
            s.FontColor = Color.Black;
            s.FontSize = 0.1f;
            s.TextPadding = 0f;
            s.Alignment = MFDTheme.AC;
        }
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
        const string S_TRUE = "true";
        const string S_FALSE = "false";
        const string S_HYDROGEN = "Hydrogen";
        const string CD_TOPDOWN = "Topdown";
        const string CD_ANTI_AIR = "AntiAir";
        const string CD_CACHED = "Cached";
        const string CD_CONFIG = "Config:";
        const string CD_RWR_COUNT = "RWRCount";
        const string TERRAIN_API = "TerrainAPI";
        const string CFG_ALTITUDE_WARNING = "altitude_warning";
        const string CFG_SPEED_WARNING = "speed_warning";
        const string CFG_BINGO_FUEL = "bingo_fuel";
        const string CFG_LOW_FUEL = "low_fuel";
        const string CFG_GUN_KP = "gun_kp";
        const string CFG_GUN_MAX_RPM = "gun_max_rpm";
        const string CFG_GUN_LOCK_THRESHOLD = "gun_lock_threshold";
        const string CFG_GUN_MAX_RANGE = "gun_max_range";
        const string CFG_GUN_MUZZLE_VELOCITY = "gun_muzzle_velocity";
        const string CFG_HUD_RADAR = "hud_radar";
        const string CFG_HUD_GUN_FUNNEL = "hud_gun_funnel";
        const string CFG_HUD_TARGET_BRACKETS = "hud_target_brackets";
        const string CFG_HUD_GFORCE = "hud_gforce";
        const string CFG_HUD_AOA = "hud_aoa";
        const string CFG_HUD_FPM = "hud_fpm";
        const string CFG_HUD_COMPASS = "hud_compass";
        const string CFG_HUD_BREAKAWAY = "hud_breakaway";
        const string CFG_HUD_THEME = "hud_theme";
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
        const string TEX_ROLL_POINTER   = "JetOS_RollPointer";
        const string TEX_BANK_ARC       = "JetOS_BankArc";
        const string TEX_AOA_BRACKET    = "JetOS_AoABracket";
        const string TEX_BORESIGHT      = "JetOS_Boresight";
        const string TEX_HDG_CHEVRON    = "JetOS_HeadingChevron";

        // Tape markers
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

        // Radar minimap
        const string TEX_RANGE_RING     = "JetOS_RangeRing";
        const string TEX_OWN_SHIP       = "JetOS_OwnShip";

        // Weapons & bays
        const string TEX_BAY_EMPTY      = "JetOS_Bay_Empty";
        const string TEX_BAY_LOADED     = "JetOS_Bay_Loaded";

        // Status indicators
        const string TEX_FUEL_TANK      = "JetOS_FuelTank";
        const string TEX_BATTERY        = "JetOS_Battery";
        const string TEX_STATUS_DOT     = "JetOS_StatusDot";

        // MFD chrome / menu
        const string TEX_MFD_CORNER     = "JetOS_MFD_Corner";
        const string TEX_NAV_ARROW      = "JetOS_NavArrow";

        // Footer / action glyphs
        const string TEX_GLYPH_CROSS    = "JetOS_Glyph_Cross";

        // ── Batch B additions ──
        const string TEX_AIRCRAFT_SYM   = "JetOS_AircraftSymbol";
        const string TEX_LOCK_DIAMOND   = "JetOS_LockDiamond";
        const string TEX_MASTER_CAUTION = "JetOS_MasterCaution";
        const string TEX_MASTER_WARNING = "JetOS_MasterWarning";
        const string TEX_HATCH          = "JetOS_HatchPattern";
    }
}
