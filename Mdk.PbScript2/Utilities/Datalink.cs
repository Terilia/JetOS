using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class Datalink
        {
            public struct FriendlyStatus
            {
                public long Id;
                public Vector3D Position;
                public double SeenAt;
            }

            public static List<FriendlyStatus> GetActiveFriendlies()
            {
                return DatalinkV2.GetActiveFriendlies();
            }
        }
    }
}
