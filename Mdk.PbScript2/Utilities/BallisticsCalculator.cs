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
                int maxIterations,
                out Vector3D interceptPoint,
                out double timeToIntercept,
                out Vector3D aimPoint,
                Vector3D targetAcceleration = default(Vector3D))
            {
                interceptPoint = VZ;
                timeToIntercept = -1;
                aimPoint = VZ;

                Vector3D D = targetPosition - shooterPosition;
                Vector3D V_rel = targetVelocity - shooterVelocity;
                double S = muzzleSpeed;
                double S2 = S * S;

                // No gravity on bullet — SE projectiles travel in straight lines
                Vector3D A_net = targetAcceleration;
                double a4 = 0.25 * A_net.LengthSquared();
                double a3 = VD(V_rel, A_net);
                double a2 = V_rel.LengthSquared() + VD(D, A_net) - S2;
                double a1 = 2.0 * VD(D, V_rel);
                double a0 = D.LengthSquared();

                double qA = V_rel.LengthSquared() - S2;
                double qB = 2.0 * VD(D, V_rel);
                double qC = D.LengthSquared();

                double t = -1;
                if (Ab(qA) < 1e-6)
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

                // Save the pure-quadratic (no-accel) solution as a fallback
                // in case Newton converges to the wrong root of the full quartic.
                double tQuadratic = t;

                const double tolerance = 0.0001;

                for (int i = 0; i < maxIterations; i++)
                {
                    double t2 = t * t;
                    double t3 = t2 * t;
                    double t4 = t3 * t;

                    double f = a4 * t4 + a3 * t3 + a2 * t2 + a1 * t + a0;
                    double fPrime = 4 * a4 * t3 + 3 * a3 * t2 + 2 * a2 * t + a1;

                    if (Ab(fPrime) < 1e-10)
                        break;

                    double step = f / fPrime;
                    double tNew = t - step;

                    // Bound the step to [t*0.1, t*2.0] to prevent Newton from
                    // jumping to a distant root or going negative.
                    if (tNew <= t * 0.1) tNew = t * 0.1;
                    else if (tNew > t * 2.0) tNew = t * 2.0;

                    double delta = Ab(tNew - t);
                    t = tNew;

                    if (delta < tolerance)
                        break;
                }

                if (t <= 0) t = tQuadratic;

                Vector3D requiredMuzzleVel = D / t + V_rel + 0.5 * A_net * t;
                double actualSpeed = requiredMuzzleVel.Length();

                // If the acceleration-aware solution drifts from muzzle speed,
                // fall back to the pure-quadratic solution (target moves in straight line).
                bool useQuadratic = false;
                if (Ab(actualSpeed - muzzleSpeed) / muzzleSpeed > 0.05)
                {
                    t = tQuadratic;
                    requiredMuzzleVel = D / t + V_rel; // no accel term
                    actualSpeed = requiredMuzzleVel.Length();
                    if (Ab(actualSpeed - muzzleSpeed) / muzzleSpeed > 0.05)
                        return false;
                    useQuadratic = true;
                }

                timeToIntercept = t;
                interceptPoint = useQuadratic
                    ? targetPosition + targetVelocity * t
                    : targetPosition + targetVelocity * t + 0.5 * targetAcceleration * t * t;

                Vector3D aimDirection = requiredMuzzleVel / actualSpeed;
                double distanceToIntercept = (interceptPoint - shooterPosition).Length();
                aimPoint = shooterPosition + aimDirection * distanceToIntercept;

                return true;
            }
        }
    }
}
