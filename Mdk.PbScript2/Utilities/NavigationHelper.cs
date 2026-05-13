using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public static class NavigationHelper
        {
            public static bool TryParseGps(string gpsString, out Vector3D result)
            {
                result = default(Vector3D);
                if (SE(gpsString) || !gpsString.StartsWith("GPS:"))
                    return false;

                var parts = gpsString.Split(':');
                if (parts.Length < 5)
                    return false;

                double x, y, z;
                if (!double.TryParse(parts[2], out x)
                    || !double.TryParse(parts[3], out y)
                    || !double.TryParse(parts[4], out z))
                    return false;

                result = new Vector3D(x, y, z);
                return true;
            }

            public static string FormatGps(Vector3D position)
            {
                return $"GPS:Target:{position.X}:{position.Y}:{position.Z}:#FF75C9F1:";
            }

            public static double GetAspectAngleDeg(Vector3D velocity, Vector3D relativePos)
            {
                Vector3D direction = velocity;
                if (direction.LengthSquared() > 0)
                    direction = VN(direction);
                Vector3D toTarget = VN(relativePos);
                double angle = At2(VX(direction, toTarget).Length(), VD(direction, toTarget));
                return ToDeg(angle);
            }
        }
    }
}
