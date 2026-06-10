using System;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class BallisticsCalculator
        {
            public static bool CalculateInterceptPoint(
                Vector3D shooterPosition,
                Vector3D shooterVelocity,
                double muzzleSpeed,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D gravity,
                out Vector3D interceptPoint,
                out double timeToIntercept,
                out Vector3D aimPoint)
            {
                interceptPoint = VZ;
                timeToIntercept = -1;
                aimPoint = VZ;

                Vector3D D = targetPosition - shooterPosition;
                Vector3D V_rel = targetVelocity - shooterVelocity;
                double S = muzzleSpeed;
                double S2 = S * S;

                double qA = V_rel.LengthSquared() - S2;
                double qB = 2.0 * VD(D, V_rel);
                double qC = D.LengthSquared();

                double t = -1;
                // Relative threshold — qA's magnitude is ~S² (≈1e6), an absolute 1e-6 never fires
                if (Ab(qA) < 1e-6 * S2)
                {
                    if (Ab(qB) > 1e-6)
                        t = -qC / qB;
                }
                else
                {
                    double discriminant = qB * qB - 4 * qA * qC;
                    if (discriminant >= 0)
                    {
                        double sqrtDisc = Math.Sqrt(discriminant);
                        double t1 = (-qB - sqrtDisc) / (2 * qA);
                        double t2 = (-qB + sqrtDisc) / (2 * qA);

                        if (t1 > 0.001 && t2 > 0.001) t = Mn(t1, t2);
                        else if (t1 > 0.001) t = t1;
                        else if (t2 > 0.001) t = t2;
                    }
                }

                if (t <= 0) return false;

                timeToIntercept = t;
                interceptPoint = targetPosition + targetVelocity * t;
                aimPoint = shooterPosition + D + V_rel * t - 0.5 * gravity * t * t;

                return true;
            }
        }
    }
}
