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
        const string TEXTURE_CIRCLE = "CircleHollow";
        const string TEXTURE_TRIANGLE = "Triangle";
        const string TEXTURE_CIRCLE_SOLID = "Circle";
    }
}
