using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class Datalink
        {
            const string IGC_CHANNEL = "JETOS_DL";
            const int SOURCE_INDEX = -1;
            const int KIND_FRIEND = 0;
            const int KIND_TARGET = 1;
            const double BROADCAST_INTERVAL = 0.2;
            const double FRIEND_TIMEOUT = 2.0;
            const double MAX_TARGET_AGE = 3.0;

            static IMyBroadcastListener _listener;
            static readonly List<FriendlyStatus> _friends = new List<FriendlyStatus>();
            static double _broadcastAccum = BROADCAST_INTERVAL;

            public struct FriendlyStatus
            {
                public long Id;
                public Vector3D Position;
                public double SeenAt;
            }

            public static void Tick(Program program, Jet jet)
            {
                Poll(program, jet);
                Broadcast(program, jet);
            }

            static void Broadcast(Program program, Jet jet)
            {
                if (jet._cockpit == null) return;
                _broadcastAccum += SystemManager.DeltaSeconds;
                if (_broadcastAccum < BROADCAST_INTERVAL) return;
                _broadcastAccum = 0;

                program.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    MyTuple.Create(KIND_FRIEND, program.Me.EntityId, 0L, jet.CockpitPosition, jet.CockpitVelocity, ""));

                for (int i = 0; i < jet.enemyList.Count; i++)
                {
                    var c = jet.enemyList[i];
                    if (c.SourceIndex < 0 || c.AgeSeconds > MAX_TARGET_AGE) continue;
                    program.IGC.SendBroadcastMessage(IGC_CHANNEL,
                        MyTuple.Create(KIND_TARGET, program.Me.EntityId, c.EntityId, c.Position, c.Velocity, c.Name));
                }
            }

            static void Poll(Program program, Jet jet)
            {
                if (_listener == null)
                    _listener = program.IGC.RegisterBroadcastListener(IGC_CHANNEL);

                while (_listener.HasPendingMessage)
                {
                    MyIGCMessage msg = _listener.AcceptMessage();
                    if (!(msg.Data is MyTuple<int, long, long, Vector3D, Vector3D, string>)) continue;
                    var t = (MyTuple<int, long, long, Vector3D, Vector3D, string>)msg.Data;
                    if (t.Item2 == program.Me.EntityId) continue;
                    if (t.Item1 == KIND_FRIEND)
                        UpsertFriend(new FriendlyStatus { Id = t.Item2, Position = t.Item4, SeenAt = SystemManager.ElapsedSeconds });
                    else if (t.Item1 == KIND_TARGET && t.Item4.LengthSquared() >= 1.0)
                        jet.UpdateOrAddEnemy(t.Item4, t.Item5, t.Item6, SOURCE_INDEX, t.Item3);
                }
            }

            static void UpsertFriend(FriendlyStatus status)
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

            static void PruneFriends()
            {
                double now = SystemManager.ElapsedSeconds;
                for (int i = _friends.Count - 1; i >= 0; i--)
                    if (now - _friends[i].SeenAt > FRIEND_TIMEOUT)
                        _friends.RemoveAt(i);
            }

            public static List<FriendlyStatus> GetActiveFriendlies()
            {
                PruneFriends();
                return _friends;
            }
        }
    }
}
