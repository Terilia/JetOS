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
        static int Cl(int v, int min, int max) => MathHelper.Clamp(v, min, max);
        static double Cl(double v, double min, double max) => MathHelper.Clamp(v, min, max);
        static float ToDeg(float r) => MathHelper.ToDegrees(r);
        static double ToDeg(double r) => MathHelper.ToDegrees((float)r);
        static float ToRad(float d) => MathHelper.ToRadians(d);
        static double ToRad(double d) => MathHelper.ToRadians((float)d);
    }
}
