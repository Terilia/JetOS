using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class FriendlyJetTelemetry
        {
            public const string IGC_CHANNEL = "JETOS_JET_STAT";
            const double BROADCAST_INTERVAL = 0.2;
            const double STATUS_TIMEOUT = 2.0;

            static IMyBroadcastListener _listener;
            static readonly List<FriendlyJetStatus> _friends = new List<FriendlyJetStatus>();
            static double _broadcastAccum = BROADCAST_INTERVAL;

            public struct FriendlyJetStatus
            {
                public long Id;
                public Vector3D Position;
                public Vector3D Velocity;
                public double SeenAt;
            }

            public static void Tick(Program program, Jet jet)
            {
                if (program == null || jet == null) return;
                Poll(program);
                Broadcast(program, jet);
                Prune();
            }

            static void Broadcast(Program program, Jet jet)
            {
                if (jet._cockpit == null) return;
                _broadcastAccum += SystemManager.DeltaSeconds;
                if (_broadcastAccum < BROADCAST_INTERVAL) return;
                _broadcastAccum = 0;

                var payload = MyTuple.Create(program.Me.EntityId, jet.CockpitPosition, jet.CockpitVelocity);
                program.IGC.SendBroadcastMessage(IGC_CHANNEL, payload);
            }

            static void Poll(Program program)
            {
                if (_listener == null)
                    _listener = program.IGC.RegisterBroadcastListener(IGC_CHANNEL);

                while (_listener.HasPendingMessage)
                {
                    MyIGCMessage msg = _listener.AcceptMessage();
                    if (!(msg.Data is MyTuple<long, Vector3D, Vector3D>)) continue;
                    var t = (MyTuple<long, Vector3D, Vector3D>)msg.Data;
                    if (t.Item1 == program.Me.EntityId) continue;
                    Upsert(new FriendlyJetStatus
                    {
                        Id = t.Item1,
                        Position = t.Item2,
                        Velocity = t.Item3,
                        SeenAt = SystemManager.ElapsedSeconds
                    });
                }
            }

            static void Upsert(FriendlyJetStatus status)
            {
                for (int i = 0; i < _friends.Count; i++)
                {
                    if (_friends[i].Id == status.Id)
                    {
                        _friends[i] = status;
                        return;
                    }
                }
                _friends.Add(status);
            }

            static void Prune()
            {
                double now = SystemManager.ElapsedSeconds;
                for (int i = _friends.Count - 1; i >= 0; i--)
                    if (now - _friends[i].SeenAt > STATUS_TIMEOUT)
                        _friends.RemoveAt(i);
            }

            public static List<FriendlyJetStatus> GetActiveFriends()
            {
                Prune();
                return _friends;
            }
        }
    }
}
