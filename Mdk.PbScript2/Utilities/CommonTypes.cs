using System;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class RWRWarning
        {
            public Vector3D Position;
            public Vector3D Velocity;
            public string Name;
            public bool IsIncoming; // True if enemy is on intercept course and pursuing
            public int RWRIndex; // Which RWR unit detected this (0-based)

            public RWRWarning(Vector3D pos, Vector3D vel, string name, bool incoming, int rwrIdx)
            {
                Position = pos;
                Velocity = vel;
                Name = name;
                IsIncoming = incoming;
                RWRIndex = rwrIdx;
            }
        }

    }
}
