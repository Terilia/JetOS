using System;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static Vector3D VN(Vector3D v) => Vector3D.Normalize(v);
        static double VD(Vector3D a, Vector3D b) => Vector3D.Dot(a, b);
        static Vector3D VX(Vector3D a, Vector3D b) => Vector3D.Cross(a, b);
        static double VDi(Vector3D a, Vector3D b) => Vector3D.Distance(a, b);
        static Vector3D VTN(Vector3D v, MatrixD m) => Vector3D.TransformNormal(v, m);
        static double Sn(double v) => Math.Sin(v);
        static double Cs(double v) => Math.Cos(v);
        static double At2(double y, double x) => Math.Atan2(y, x);
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
    }
}
