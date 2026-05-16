using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class DatalinkV2
        {
            const string IGC_CHANNEL = "JETOS_DL";
            const int KIND_FRIEND = 0;
            const int KIND_TARGET_LEGACY = 1;
            const double BROADCAST_INTERVAL = 0.2;
            const double FRIEND_TIMEOUT = 2.0;
            const double LOCAL_OBSERVATION_WINDOW = 3.0;
            const double KEYFRAME_SECONDS = 5.0;
            const int MAX_HOPS = 3;

            static readonly System.Globalization.NumberFormatInfo _nfi =
                new System.Globalization.NumberFormatInfo { NumberDecimalSeparator = ".", NumberGroupSeparator = "" };

            static IMyBroadcastListener _listener;
            static readonly List<Datalink.FriendlyStatus> _friends = new List<Datalink.FriendlyStatus>();
            static readonly List<RelayContact> _relays = new List<RelayContact>();
            static readonly List<SentContact> _sent = new List<SentContact>();
            static readonly StringBuilder _sb = new StringBuilder(192);
            static double _broadcastAccum = BROADCAST_INTERVAL;

            struct RelayContact
            {
                public char Kind;
                public long ObserverId;
                public long TargetId;
                public Vector3D Position;
                public Vector3D Velocity;
                public string Name;
                public double ObservedAt;
                public int HopCount;
            }

            struct SentContact
            {
                public long TargetId;
                public long ObserverId;
                public Vector3D Position;
                public double LastSent;
                public double LastKeyframe;
            }

            public static void Tick(Program program, Jet jet)
            {
                Poll(program, jet);
                Broadcast(program, jet);
                PruneFriends();
                PruneRelays();
            }

            public static List<Datalink.FriendlyStatus> GetActiveFriendlies()
            {
                PruneFriends();
                return _friends;
            }

            static void Broadcast(Program program, Jet jet)
            {
                if (jet._cockpit == null) return;
                _broadcastAccum += SystemManager.DeltaSeconds;
                if (_broadcastAccum < BROADCAST_INTERVAL) return;
                _broadcastAccum = 0;

                double now = SystemManager.ElapsedSeconds;
                program.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    MyTuple.Create(KIND_FRIEND, program.Me.EntityId, 0L, jet.CockpitPosition, jet.CockpitVelocity, ""));

                for (int i = 0; i < jet.enemyList.Count; i++)
                {
                    var c = jet.enemyList[i];
                    if (c.EntityId == 0 || c.SourceIndex < 0 || c.AgeSeconds > LOCAL_OBSERVATION_WINDOW) continue;
                    SendContactIfDue(program, RadarContactV2.KIND_HOSTILE, program.Me.EntityId, c.EntityId,
                        c.Position, c.Velocity, c.Name, 0, 0, now);
                }

                var maps = MapContactStoreV2.GetActive();
                for (int i = 0; i < maps.Count; i++)
                {
                    var c = maps[i];
                    if (c.ObserverId != program.Me.EntityId || c.AgeSeconds > LOCAL_OBSERVATION_WINDOW) continue;
                    SendContactIfDue(program, c.Kind, c.ObserverId, c.Id, c.Position, c.Velocity, c.Name, 0, 0, now);
                }

                for (int i = 0; i < _relays.Count; i++)
                {
                    var r = _relays[i];
                    if (r.ObserverId == program.Me.EntityId || r.HopCount >= MAX_HOPS) continue;
                    double age = now - r.ObservedAt;
                    if (age > RadarContactV2.CONTACT_DECAY_SECONDS) continue;
                    SendContactIfDue(program, r.Kind, r.ObserverId, r.TargetId, r.Position, r.Velocity, r.Name,
                        age, r.HopCount + 1, now);
                }
            }

            static void Poll(Program program, Jet jet)
            {
                if (_listener == null)
                    _listener = program.IGC.RegisterBroadcastListener(IGC_CHANNEL);

                while (_listener.HasPendingMessage)
                {
                    MyIGCMessage msg = _listener.AcceptMessage();
                    if (msg.Data is string)
                    {
                        ReadV2Packet(program, jet, msg.Data as string);
                        continue;
                    }

                    if (!(msg.Data is MyTuple<int, long, long, Vector3D, Vector3D, string>)) continue;
                    var t = (MyTuple<int, long, long, Vector3D, Vector3D, string>)msg.Data;
                    if (t.Item2 == program.Me.EntityId) continue;
                    if (t.Item1 == KIND_FRIEND)
                        UpsertFriend(new Datalink.FriendlyStatus { Id = t.Item2, Position = t.Item4, SeenAt = SystemManager.ElapsedSeconds });
                    else if (t.Item1 == KIND_TARGET_LEGACY && t.Item3 != 0 && t.Item4.LengthSquared() >= 1.0)
                        jet.UpdateOrAddEnemy(t.Item4, t.Item5, t.Item6, RadarContactV2.SRC_DATALINK, t.Item3);
                }
            }

            static void ReadV2Packet(Program program, Jet jet, string payload)
            {
                if (SE(payload) || !payload.StartsWith("J2|")) return;
                string[] p = payload.Split('|');
                if (p.Length < 14) return;

                char kind = SE(p[1]) ? '\0' : p[1][0];
                long observerId, senderId, targetId;
                double px, py, pz, vx, vy, vz, age;
                int hop;
                if (!long.TryParse(p[2], out observerId) || observerId == program.Me.EntityId) return;
                if (!long.TryParse(p[3], out senderId) || senderId == program.Me.EntityId) return;
                if (!long.TryParse(p[4], out targetId) || targetId == 0) return;
                if (!TryD(p[5], out px) || !TryD(p[6], out py) || !TryD(p[7], out pz)) return;
                if (!TryD(p[8], out vx) || !TryD(p[9], out vy) || !TryD(p[10], out vz)) return;
                if (!TryD(p[11], out age) || age < 0 || age > RadarContactV2.CONTACT_DECAY_SECONDS) return;
                if (!int.TryParse(p[12], out hop) || hop < 0 || hop > MAX_HOPS) return;

                string name = p[13];
                Vector3D pos = new Vector3D(px, py, pz);
                Vector3D vel = new Vector3D(vx, vy, vz);
                if (kind != RadarContactV2.KIND_HOSTILE && !RadarContactV2.IsMapKind(kind))
                    return;

                if (!UpsertRelay(kind, observerId, targetId, pos, vel, name, age, hop))
                    return;

                if (kind == RadarContactV2.KIND_HOSTILE)
                    jet.UpdateOrAddEnemy(pos, vel, name, RadarContactV2.SRC_DATALINK, targetId, age);
                else if (RadarContactV2.IsMapKind(kind))
                    MapContactStoreV2.Update(kind, targetId, pos, vel, name, observerId, hop, age);
            }

            static void SendContactIfDue(Program program, char kind, long observerId, long targetId,
                Vector3D position, Vector3D velocity, string name, double ageSeconds, int hopCount, double now)
            {
                if (targetId == 0 || observerId == 0) return;
                if (!ShouldSend(observerId, targetId, position, now)) return;
                program.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    FormatContact(kind, observerId, program.Me.EntityId, targetId, position, velocity, name, ageSeconds, hopCount));
            }

            static string FormatContact(char kind, long observerId, long senderId, long targetId,
                Vector3D position, Vector3D velocity, string name, double ageSeconds, int hopCount)
            {
                _sb.Clear();
                _sb.Append("J2|").Append(kind).Append('|').Append(observerId).Append('|').Append(senderId).Append('|').Append(targetId);
                AppendV(position.X); AppendV(position.Y); AppendV(position.Z);
                AppendV(velocity.X); AppendV(velocity.Y); AppendV(velocity.Z);
                _sb.Append('|').Append(ageSeconds.ToString("0.###", _nfi)).Append('|').Append(hopCount).Append('|').Append(CleanName(name));
                return _sb.ToString();
            }

            static void AppendV(double value)
            {
                _sb.Append('|').Append(value.ToString("0.###", _nfi));
            }

            static string CleanName(string name)
            {
                if (SE(name)) return "";
                return name.Replace('|', ' ');
            }

            static bool ShouldSend(long observerId, long targetId, Vector3D position, double now)
            {
                for (int i = 0; i < _sent.Count; i++)
                {
                    SentContact s = _sent[i];
                    if (s.ObserverId != observerId || s.TargetId != targetId) continue;
                    bool keyframe = now - s.LastKeyframe >= KEYFRAME_SECONDS;
                    bool moved = (s.Position - position).LengthSquared() > 0.01;
                    if (!keyframe && !moved) return false;
                    if (now - s.LastSent < BROADCAST_INTERVAL) return false;
                    s.Position = position;
                    s.LastSent = now;
                    if (keyframe) s.LastKeyframe = now;
                    _sent[i] = s;
                    return true;
                }

                _sent.Add(new SentContact { ObserverId = observerId, TargetId = targetId, Position = position, LastSent = now, LastKeyframe = now });
                return true;
            }

            static bool UpsertRelay(char kind, long observerId, long targetId, Vector3D position, Vector3D velocity, string name, double age, int hop)
            {
                double observedAt = SystemManager.ElapsedSeconds - age;
                for (int i = 0; i < _relays.Count; i++)
                {
                    var r = _relays[i];
                    if (r.ObserverId != observerId || r.TargetId != targetId) continue;
                    if (observedAt < r.ObservedAt) return false;
                    r.Kind = kind;
                    r.Position = position;
                    r.Velocity = velocity;
                    r.Name = name;
                    r.ObservedAt = observedAt;
                    r.HopCount = hop;
                    _relays[i] = r;
                    return true;
                }

                _relays.Add(new RelayContact
                {
                    Kind = kind,
                    ObserverId = observerId,
                    TargetId = targetId,
                    Position = position,
                    Velocity = velocity,
                    Name = name,
                    ObservedAt = observedAt,
                    HopCount = hop
                });
                return true;
            }

            static void UpsertFriend(Datalink.FriendlyStatus status)
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

            static void PruneRelays()
            {
                double now = SystemManager.ElapsedSeconds;
                for (int i = _relays.Count - 1; i >= 0; i--)
                    if (now - _relays[i].ObservedAt > RadarContactV2.CONTACT_DECAY_SECONDS)
                        _relays.RemoveAt(i);
            }

            static bool TryD(string s, out double value)
            {
                return double.TryParse(s, System.Globalization.NumberStyles.Float, _nfi, out value);
            }
        }
    }
}
