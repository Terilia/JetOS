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
            public long LastSeenTicks;

            public RWRWarning(Vector3D pos, Vector3D vel, string name, bool incoming, int rwrIdx)
            {
                Position = pos;
                Velocity = vel;
                Name = name;
                IsIncoming = incoming;
                RWRIndex = rwrIdx;
                LastSeenTicks = DateTime.Now.Ticks;
            }

            public double AgeSeconds()
            {
                return (DateTime.Now.Ticks - LastSeenTicks) / (double)TimeSpan.TicksPerSecond;
            }
        }

        public class Player
        {
            public float PositionX;
            public float PositionY;
            public Player(float x, float y)
            {
                PositionX = x;
                PositionY = y;
            }
        }

        public class Obstacle
        {
            public float PositionX;
            public float PositionY;
            public float Speed;
            public int Length;
            public Color Color;
            public Obstacle(float x, float y, float speed, int length, Color color)
            {
                PositionX = x;
                PositionY = y;
                Speed = speed;
                Length = length;
                Color = color;
            }
        }

        struct Vector2I
        {
            public int X;
            public int Y;

            public Vector2I(int x, int y)
            {
                X = x;
                Y = y;
            }
        }
    }
}
