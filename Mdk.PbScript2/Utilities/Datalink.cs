using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class Datalink
        {
            // Unified datalink record. One field-set reused for friends, stations,
            // relays and sent-dedupe entries (the old FriendlyStatus / StationStatus /
            // RelayContact / SentContact). Field meaning by use-site:
            //   Id       : friend jetId / station id / relay+sent observerId
            //   Word     : friend StatusWord / station Flags
            //   TargetId : friend currentTarget / station WaypointId / relay+sent targetId
            //   Text     : friend Callsign / station Text / relay Name
            //   Num      : station Ttl / sent LastKeyframe
            //   Misc     : station OrderType / relay HopCount
            //   SeenAt   : friend/station SeenAt / relay ObservedAt / sent LastSent
            public struct Node
            {
                public long Id;
                public Vector3D Position;
                public Vector3D Velocity;
                public double SeenAt;
                public long Word;
                public long TargetId;
                public string Text;
                public double Num;
                public int Misc;
                public char Kind;
                public Vector3D[] Verts; // TAG_ZONE polygon vertices (1 entry = circle); null otherwise
            }
        }
    }
}
