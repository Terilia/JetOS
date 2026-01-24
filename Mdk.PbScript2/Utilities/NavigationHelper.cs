using Sandbox.ModAPI.Ingame;
using System;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public static class NavigationHelper
        {
            public static double CalculateHeading(IMyCockpit cockpit)
            {
                if (cockpit == null) return 0;

                // 1. Define World Up direction (opposite to gravity or World Y+)
                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D worldUp;
                bool hasGravity = gravity.LengthSquared() > 1e-6; // Add tolerance

                if (hasGravity)
                {
                    worldUp = -Vector3D.Normalize(gravity);
                }
                else
                {
                    // No natural gravity: Use world Y+ as Up reference.
                    // You might adjust this based on desired zero-G compass behavior (e.g., use grid's Up).
                    worldUp = Vector3D.Up; // World Y+
                }

                // 2. Get Cockpit's Forward Direction
                Vector3D forwardVector = cockpit.WorldMatrix.Forward;

                // 3. Project Forward onto the Horizontal Plane (perpendicular to worldUp)
                Vector3D forwardHorizontal = Vector3D.Reject(forwardVector, worldUp);

                // Check if projected vector is too small (pointing nearly straight up/down)
                if (forwardHorizontal.LengthSquared() < 1e-8) // Use a small tolerance
                {
                    // Heading is undefined when looking straight up or down relative to worldUp.
                    // Return 0, or potentially previous heading, or derive from Right vector.
                    return 0;
                }
                forwardHorizontal.Normalize(); // Ensure it's a unit vector

                // 4. Define World North and East on the Horizontal Plane
                // Assumes World Z- is global North direction reference.
                Vector3D worldNorthRef = new Vector3D(0, 0, -1);

                // Project world North onto the horizontal plane
                Vector3D northHorizontal = Vector3D.Reject(worldNorthRef, worldUp);
                Vector3D eastHorizontal;
                // Handle edge case: If worldUp is aligned with worldNorthRef (e.g., at poles)
                if (northHorizontal.LengthSquared() < 1e-8)
                {
                    // North is ambiguous, use world East (X+) as primary reference instead
                    Vector3D worldEastRef = new Vector3D(1, 0, 0);
                    eastHorizontal = Vector3D.Normalize(Vector3D.Reject(worldEastRef, worldUp));
                    // Define horizontal North as 90 degrees left of horizontal East
                    northHorizontal = Vector3D.Cross(worldUp, eastHorizontal);
                    // No need to normalize northHorizontal if worldUp and eastHorizontal are unit/orthogonal
                }
                else
                {
                    northHorizontal.Normalize();
                    eastHorizontal = eastHorizontal = Vector3D.Cross(northHorizontal, worldUp);

                }


                // Horizontal East is perpendicular to horizontal North and world Up

                // eastHorizontal should already be normalized if worldUp and northHorizontal are unit/orthogonal.

                // 5. Calculate Components & Angle with Atan2
                // Get the coordinates of forwardHorizontal relative to the North/East horizontal axes
                double northComponent = Vector3D.Dot(forwardHorizontal, northHorizontal);
                double eastComponent = Vector3D.Dot(forwardHorizontal, eastHorizontal);

                // Atan2(y, x) gives the angle counter-clockwise from the positive X-axis.
                // We want the angle from North (northComponent) towards East (eastComponent).
                // So, Y = East component, X = North component.
                double headingRadians = Math.Atan2(eastComponent, northComponent);

                // 6. Convert to Degrees [0, 360)
                double headingDegrees = MathHelper.ToDegrees(headingRadians);
                if (headingDegrees < 0)
                {
                    headingDegrees += 360.0;
                }

                return headingDegrees;
            }
        }
    }
}
