using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class DatalinkV2
        {
            const string IGC_CHANNEL = "JETOS_DL";
            const int TAG_STATUS = 0;
            const int TAG_CONTACT = 1;
            const int TAG_STATION = 2;
            const int TAG_ZONE = 3;
            const double BROADCAST_INTERVAL = 0.2;
            const double STATION_TIMEOUT = 10.0;
            const double FRIEND_TIMEOUT = 2.0;
            const double LOCAL_OBSERVATION_WINDOW = 3.0;
            const double KEYFRAME_SECONDS = 5.0;
            const int MAX_HOPS = 3;

            static IMyBroadcastListener _listener;
            static readonly List<Datalink.Node> _friends = new List<Datalink.Node>();
            static readonly List<Datalink.Node> _relays = new List<Datalink.Node>();
            static readonly List<Datalink.Node> _sent = new List<Datalink.Node>();
            static readonly List<Datalink.Node> _stations = new List<Datalink.Node>();
            // HQ-broadcast zones (Tag 3). Reuses Node: Position=center, Num=radius, Misc=kind, Text=name.
            static readonly List<Datalink.Node> _zones = new List<Datalink.Node>();
            static double _broadcastAccum = BROADCAST_INTERVAL;

            public static void Tick(Program program, Jet jet)
            {
                Poll(program, jet);
                Broadcast(program, jet);
                Prune(_friends, FRIEND_TIMEOUT, false);
                Prune(_relays, RadarContactV2.CONTACT_DECAY_SECONDS, false);
                Prune(_stations, STATION_TIMEOUT, true);
                Prune(_zones, RadarContactV2.CONTACT_DECAY_SECONDS, false);
            }

            public static List<Datalink.Node> GetZones()
            {
                return _zones; // pruned each tick by Tick()
            }

            public static List<Datalink.Node> GetActiveFriendlies()
            {
                Prune(_friends, FRIEND_TIMEOUT, false);
                return _friends;
            }

            public static List<Datalink.Node> GetStations()
            {
                return _stations; // pruned each tick by Tick()
            }

            static void Broadcast(Program program, Jet jet)
            {
                if (jet._cockpit == null) return;
                _broadcastAccum += SystemManager.DeltaSeconds;
                if (_broadcastAccum < BROADCAST_INTERVAL) return;
                _broadcastAccum = 0;

                double now = SystemManager.ElapsedSeconds;
                long me = program.Me.EntityId;
                program.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    Pack(TAG_STATUS, me, jet.CockpitPosition, jet.CockpitVelocity,
                        BuildStatusWord(jet), jet.GetSelectedEnemyId(), 0.0, 0,
                        program.Me.CubeGrid.CustomName ?? ""));

                for (int i = 0; i < jet.enemyList.Count; i++)
                {
                    var c = jet.enemyList[i];
                    if (c.EntityId == 0 || c.SourceIndex < 0 || c.AgeSeconds > LOCAL_OBSERVATION_WINDOW) continue;
                    SendContactIfDue(program, RadarContactV2.KIND_HOSTILE, me, c.EntityId,
                        c.Position, c.Velocity, c.Name, 0, 0, now);
                }

                var maps = MapContactStoreV2.GetActive();
                for (int i = 0; i < maps.Count; i++)
                {
                    var c = maps[i];
                    if (c.ObserverId != me || c.AgeSeconds > LOCAL_OBSERVATION_WINDOW) continue;
                    SendContactIfDue(program, c.Kind, me, c.Id, c.Position, c.Velocity, c.Name, 0, 0, now);
                }

                for (int i = 0; i < _relays.Count; i++)
                {
                    var r = _relays[i];
                    if (r.Id == me || r.Misc >= MAX_HOPS) continue;
                    double age = now - r.SeenAt;
                    if (age > RadarContactV2.CONTACT_DECAY_SECONDS) continue;
                    SendContactIfDue(program, r.Kind, r.Id, r.TargetId, r.Position, r.Velocity, r.Text,
                        age, r.Misc + 1, now);
                }
            }

            static void Poll(Program program, Jet jet)
            {
                if (_listener == null)
                    _listener = program.IGC.RegisterBroadcastListener(IGC_CHANNEL);

                long me = program.Me.EntityId;
                while (_listener.HasPendingMessage)
                {
                    MyIGCMessage msg = _listener.AcceptMessage();
                    var tn = msg.Data as MyTuple<int, long, Vector3D, Vector3D, long, MyTuple<long, double, int, string>>?;
                    if (tn == null) continue;
                    var t = tn.Value;
                    long sender = t.Item2;
                    if (sender == me) continue;
                    var inner = t.Item6;
                    double now = SystemManager.ElapsedSeconds;

                    if (t.Item1 == TAG_STATUS || t.Item1 == TAG_STATION)
                    {
                        // Friends and stations share the same envelope mapping; only the
                        // extra fields differ (STATUS carries velocity, STATION carries
                        // Ttl/OrderType in inner.Item2/Item3).
                        var node = new Datalink.Node
                        {
                            Id = sender,
                            Position = t.Item3,
                            Word = t.Item5,
                            TargetId = inner.Item1,
                            Text = inner.Item4 ?? "",
                            SeenAt = now
                        };
                        if (t.Item1 == TAG_STATUS)
                        {
                            node.Velocity = t.Item4;
                            UpsertById(_friends, node);
                        }
                        else
                        {
                            node.Num = inner.Item2;
                            node.Misc = inner.Item3;
                            UpsertById(_stations, node);
                        }
                        continue;
                    }

                    if (t.Item1 == TAG_ZONE)
                    {
                        // Circle-only plot: ignore per-vertex Pos/index. vc==0 (Misc bits 6..11) is a
                        // tombstone -> drop. Else upsert center(Vel)/radius(Num)/kind(Misc 15..18)/name.
                        if (((inner.Item3 >> 6) & 63) == 0) RemoveById(_zones, t.Item5);
                        else UpsertById(_zones, new Datalink.Node
                        {
                            Id = t.Item5,
                            Position = t.Item4,
                            Num = inner.Item2,
                            Misc = (inner.Item3 >> 15) & 15,
                            Text = inner.Item4 ?? "",
                            SeenAt = now
                        });
                        continue;
                    }

                    if (t.Item1 != TAG_CONTACT) continue;
                    long observerId = t.Item5;
                    if (observerId == me) continue;

                    long targetId = inner.Item1;
                    if (targetId == 0) continue;
                    double age = inner.Item2;
                    if (age < 0 || age > RadarContactV2.CONTACT_DECAY_SECONDS) continue;
                    int misc = inner.Item3;
                    int hop = misc & 15;
                    if (hop > MAX_HOPS) continue;
                    char kind = (char)(misc >> 4);
                    if (kind != RadarContactV2.KIND_HOSTILE && !RadarContactV2.IsMapKind(kind)) continue;

                    string name = inner.Item4 ?? "";
                    Vector3D pos = t.Item3;
                    Vector3D vel = t.Item4;
                    if (!UpsertRelay(kind, observerId, targetId, pos, vel, name, age, hop)) continue;

                    if (kind == RadarContactV2.KIND_HOSTILE)
                        jet.UpdateOrAddEnemy(pos, vel, name, RadarContactV2.SRC_DATALINK, targetId, age);
                    else
                        MapContactStoreV2.Update(kind, targetId, pos, vel, name, observerId, hop, age);
                }
            }

            static void SendContactIfDue(Program program, char kind, long observerId, long targetId,
                Vector3D position, Vector3D velocity, string name, double ageSeconds, int hopCount, double now)
            {
                if (targetId == 0 || observerId == 0) return;
                if (!ShouldSend(observerId, targetId, position, now)) return;
                program.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    Pack(TAG_CONTACT, program.Me.EntityId, position, velocity, observerId,
                        targetId, ageSeconds, ((int)kind << 4) | hopCount, name ?? ""));
            }

            // Single typed envelope shared by every datalink message. Tag selects how the fields are read:
            //   TAG_STATUS  : sender=jetId, pos/vel=jet, packed=statusWord, idB=currentTargetId, text=callsign
            //   TAG_CONTACT : sender=relayer, packed=observerId, idB=targetId, num=age, misc=(kind<<4)|hop, text=name
            // (TAG 2/STATION reserved for the HQ broadcast in a later phase.)
            static MyTuple<int, long, Vector3D, Vector3D, long, MyTuple<long, double, int, string>> Pack(
                int tag, long sender, Vector3D pos, Vector3D vel, long packed, long idB, double num, int misc, string text)
            {
                return MyTuple.Create(tag, sender, pos, vel, packed, MyTuple.Create(idB, num, misc, text));
            }

            // Encode this jet's live state into the TAG_STATUS bit field (see envelope contract):
            //  [0..6] fuel% [7..13] battery% [14..20] integrity% [21..24] missiles [25..27] gun
            //  [28..31] state [32..43] altitude/8 [44..55] flags
            static long BuildStatusWord(Jet jet)
            {
                int fuel = Mn(100, Mx(0, (int)(jet.FuelPct * 100)));
                int batt = Mn(100, Mx(0, (int)(jet.BatteryPct * 100)));

                int ef = 0, et = 0, f, t, d;
                Jet.GetEngineHealth(jet.leftEnginesAll, out f, out t, out d); ef += f; et += t;
                Jet.GetEngineHealth(jet.rightEnginesAll, out f, out t, out d); ef += f; et += t;
                Jet.GetEngineHealth(jet.centerEnginesAll, out f, out t, out d); ef += f; et += t;
                int integ = et > 0 ? ef * 100 / et : 100;

                int missiles = Mn(15, jet._bays != null ? jet._bays.Count : 0);
                int gunBucket = Mn(7, jet.GetTotalGunAmmo() / 20);
                int altDiv8 = Mn(4095, Mx(0, (int)(jet.SurfaceAltitude / 8.0)));

                bool bingo = fuel < 15;
                int state = 1;
                if (jet.GetSelectedEnemyId() != 0) state = 2;
                if (SystemManager.RwrActive) state = 3;
                if (bingo) state = 5;

                int flags = 0;
                if (SystemManager.RwrActive) flags |= 1;
                if (SystemManager.TrackLocked) flags |= 2;
                if (bingo) flags |= 4;
                if (SystemManager.AltitudeWarningActive) flags |= 8;

                return (long)(fuel & 127)
                     | ((long)(batt & 127) << 7)
                     | ((long)(integ & 127) << 14)
                     | ((long)(missiles & 15) << 21)
                     | ((long)(gunBucket & 7) << 25)
                     | ((long)(state & 15) << 28)
                     | ((long)(altDiv8 & 4095) << 32)
                     | ((long)(flags & 4095) << 44);
            }

            static bool ShouldSend(long observerId, long targetId, Vector3D position, double now)
            {
                for (int i = 0; i < _sent.Count; i++)
                {
                    Datalink.Node s = _sent[i];
                    if (s.Id != observerId || s.TargetId != targetId) continue;
                    bool keyframe = now - s.Num >= KEYFRAME_SECONDS;
                    bool moved = (s.Position - position).LengthSquared() > 0.01;
                    if (!keyframe && !moved) return false;
                    if (now - s.SeenAt < BROADCAST_INTERVAL) return false;
                    s.Position = position;
                    s.SeenAt = now;
                    if (keyframe) s.Num = now;
                    _sent[i] = s;
                    return true;
                }

                _sent.Add(new Datalink.Node { Id = observerId, TargetId = targetId, Position = position, SeenAt = now, Num = now });
                return true;
            }

            static bool UpsertRelay(char kind, long observerId, long targetId, Vector3D position, Vector3D velocity, string name, double age, int hop)
            {
                double observedAt = SystemManager.ElapsedSeconds - age;
                for (int i = 0; i < _relays.Count; i++)
                {
                    var r = _relays[i];
                    if (r.Id != observerId || r.TargetId != targetId) continue;
                    if (observedAt < r.SeenAt) return false;
                    r.Kind = kind;
                    r.Position = position;
                    r.Velocity = velocity;
                    r.Text = name;
                    r.SeenAt = observedAt;
                    r.Misc = hop;
                    _relays[i] = r;
                    return true;
                }

                _relays.Add(new Datalink.Node
                {
                    Kind = kind,
                    Id = observerId,
                    TargetId = targetId,
                    Position = position,
                    Velocity = velocity,
                    Text = name,
                    SeenAt = observedAt,
                    Misc = hop
                });
                return true;
            }

            static void UpsertById(List<Datalink.Node> list, Datalink.Node node)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == node.Id)
                    {
                        list[i] = node;
                        return;
                    }
                }
                list.Add(node);
            }

            static void RemoveById(List<Datalink.Node> list, long id)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Id == id) { list.RemoveAt(i); return; }
            }

            // Drop entries whose age exceeds their lifetime. useNum: per-node Num
            // (station TTL, falling back to STATION_TIMEOUT when <=0) overrides life.
            static void Prune(List<Datalink.Node> list, double life, bool useNum)
            {
                double now = SystemManager.ElapsedSeconds;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    double l = useNum && list[i].Num > 0 ? list[i].Num : life;
                    if (now - list[i].SeenAt > l)
                        list.RemoveAt(i);
                }
            }
        }
    }
}
