using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class RadarContactV2
        {
            public const int SRC_DATALINK = -1;
            public const int SRC_ONBOARD_STT = 0;
            public const int SRC_RADARFEED_V2 = 100;
            public const char KIND_HOSTILE = 'H';
            public const char KIND_NEUTRAL = 'N';
            public const char KIND_UNKNOWN = 'U';
            public const double CONTACT_DECAY_SECONDS = 30;

            public static bool IsMapKind(char kind)
            {
                return kind == KIND_NEUTRAL || kind == KIND_UNKNOWN;
            }
        }

        public interface IRadarLockStatus
        {
            bool IsTrackLocked { get; }
            bool HasRwrThreat { get; }
        }

        public struct MapContactV2
        {
            public long Id;
            public char Kind;
            public Vector3D Position;
            public Vector3D Velocity;
            public string Name;
            public double LastSeen;
            public long ObserverId;
            public int HopCount;

            public MapContactV2(char kind, long id, Vector3D position, Vector3D velocity, string name, long observerId, int hopCount)
            {
                Kind = kind;
                Id = id;
                Position = position;
                Velocity = velocity;
                Name = name;
                ObserverId = observerId;
                HopCount = hopCount;
                LastSeen = Jet.GameSeconds;
            }

            public double AgeSeconds
            {
                get { return Jet.GameSeconds - LastSeen; }
            }
        }
    }
}
