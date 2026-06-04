using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // ── DataLink V2 station service (background) ──
        // Transport + presentation over FleetState. Polls jet STATUS + relayed CONTACTs into the
        // fused picture, relays deduped contacts hop+1 on the HQ antenna, and broadcasts the
        // STATION screen (operator orders + an auto TACSIT / THREATS / WING readout) plus HQ's
        // own STATUS ping. Cadence + relay-enable + alert thresholds come from HQConfig.
        //
        // Envelope (shared with the jet, docs/datalink-v2.md):
        //   MyTuple<int Tag, long Sender, Vector3D Pos, Vector3D Vel, long Packed,
        //           MyTuple<long IdB, double Num, int Misc, string Text>>
        static class DatalinkHQ
        {
            const int TAG_STATUS  = 0;
            const int TAG_CONTACT = 1;
            const int TAG_STATION = 2;
            const int TAG_ZONE    = 3;
            const int MAX_HOPS = 3;
            const double RELAY_INTERVAL   = 0.2;  // contact relay cadence (matches the jet)
            const double KEYFRAME_SECONDS = 5.0;  // resend an unmoved contact at least this often
            const double ZONE_INTERVAL = 0.2;     // zone packet round-robin cadence (bandwidth-cheap)
            const int    ZONE_BATCH = 24;         // zone vertex packets per cadence tick
            const int    TOMB_SENDS = 30;         // times a deleted zone's tombstone is re-broadcast
            const string ZONE_SENTINEL = "===ZONES===";  // CustomData: orders above, zone names below
            const double ZONE_CD_INTERVAL = 0.5;  // how often we sync the CustomData zone block

            const string DEFAULT_NEWS =
                "#NYINAH CORP TACNET\n" +
                "#ORDERS\n" +
                ">VIPER=RTB + rearm\n" +
                ">HORNET=hold CAP WP-2\n" +
                "#INTEL\n" +
                "Carrier ETA 12m\n" +
                "~FREQ RED // GLHF";

            private static Program _p;
            private static Station _s;
            private static IMyBroadcastListener _listener;
            private static readonly StringBuilder _sb = new StringBuilder();
            private static readonly Dictionary<long, SentEntry> _sent = new Dictionary<long, SentEntry>();
            private static readonly List<long> _scratch = new List<long>();
            private static double _stationAccum;
            private static double _relayAccum;
            private static double _zoneAccum;
            private static double _cdAccum;                                        // CustomData sync timer
            private static int _zi, _vi;                                           // zone round-robin cursor
            private static readonly Dictionary<int, int> _tomb = new Dictionary<int, int>(); // id -> remaining tombstone sends
            private static readonly List<int> _ti = new List<int>();               // scratch for tombstone ids
            private static readonly HashSet<int> _cdIds = new HashSet<int>();      // ids present in the CustomData block

            struct SentEntry { public Vector3D Pos; public double LastSent; public double LastKeyframe; }

            // ── Operator command (set by the ORDERS module) ──
            // Banner shown on the STATION screen; order-type overrides the auto TACSIT type.
            public static string CommandBanner = "";
            public static int CommandOrderType = 0;
            public static void SetCommand(string banner, int orderType)
            {
                CommandBanner = banner ?? "";
                CommandOrderType = orderType;
            }

            // ── Public read-model for the UI ──
            public static int JetCount => FleetState.Jets.Count;
            public static int ContactCount => FleetState.Contacts.Count;
            public static int RelayCount => _sent.Count;
            public static string Channel => IGC_CHANNEL;
            public static string Tacsit => FleetState.Tacsit;
            public static bool LinkOk => _s != null && _s.Antenna != null;

            public static void Initialize(Program program, Station station)
            {
                _p = program;
                _s = station;
                _listener = program.IGC.RegisterBroadcastListener(IGC_CHANNEL);
                HQConfig.Load(program);
                if (SW(program.Me.CustomData))
                    program.Me.CustomData = DEFAULT_NEWS;
            }

            public static void Tick()
            {
                Poll();
                FleetState.Prune();
                FleetState.Recompute(_s.Position);

                double dt = SystemManager.DeltaSeconds;
                if (HQConfig.Relay)
                {
                    _relayAccum += dt;
                    if (_relayAccum >= RELAY_INTERVAL) { _relayAccum = 0; Relay(); }
                }
                _zoneAccum += dt;
                if (_zoneAccum >= ZONE_INTERVAL) { _zoneAccum = 0; BroadcastZones(); }
                _cdAccum += dt;
                if (_cdAccum >= ZONE_CD_INTERVAL) { _cdAccum = 0; SyncZoneCustomData(); }
                _stationAccum += dt;
                double interval = HQConfig.BroadcastHz > 0.01 ? 1.0 / HQConfig.BroadcastHz : 1.0;
                if (_stationAccum >= interval) { _stationAccum = 0; Broadcast(); }
            }

            static void Poll()
            {
                long me = _p.Me.EntityId;
                while (_listener.HasPendingMessage)
                {
                    MyIGCMessage msg = _listener.AcceptMessage();
                    var tn = msg.Data as MyTuple<int, long, Vector3D, Vector3D, long, MyTuple<long, double, int, string>>?;
                    if (tn == null) continue;
                    var t = tn.Value;
                    if (t.Item2 == me) continue;
                    var inner = t.Item6;

                    if (t.Item1 == TAG_STATUS)
                    {
                        FleetState.UpsertJet(t.Item2, inner.Item4, t.Item5, t.Item3, t.Item4, inner.Item1);
                        continue;
                    }

                    if (t.Item1 != TAG_CONTACT) continue; // STATION (other stations) ignored

                    long observerId = t.Item5;
                    long targetId = inner.Item1;
                    if (targetId == 0 || observerId == 0) continue;
                    double age = inner.Item2;
                    if (age < 0 || age > FleetState.CONTACT_DECAY) continue;
                    int misc = inner.Item3;
                    int hop = misc & 15;
                    if (hop > MAX_HOPS) continue;
                    char kind = (char)(misc >> 4);
                    if (kind != FleetState.KIND_HOSTILE && !FleetState.IsMapKind(kind)) continue;

                    FleetState.UpsertContact(kind, observerId, targetId, t.Item3, t.Item4, inner.Item4, age, hop);
                }
            }

            // Re-broadcast each fused contact one hop further out, throttled so we only spend
            // bandwidth on movers / keyframes. Keyed by target id (HQ runs one fused stream
            // per target, unlike a jet which may relay several observers' views).
            static void Relay()
            {
                double now = SystemManager.ElapsedSeconds;
                long me = _p.Me.EntityId;
                foreach (var kv in FleetState.Contacts)
                    RelayContact(kv.Value, now, me);
                PruneSent();
            }

            static void RelayContact(FleetState.Contact c, double now, long me)
            {
                if (c.Hop >= MAX_HOPS) return;
                double age = now - c.ObservedAt;
                if (age > FleetState.CONTACT_DECAY) return;
                if (!ShouldSend(c.Id, c.Pos, now)) return;

                _p.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    MyTuple.Create(TAG_CONTACT, me, c.Pos, c.Vel, c.ObserverId,
                        MyTuple.Create(c.Id, age, ((int)c.Kind << 4) | (c.Hop + 1), c.Name ?? "")));
            }

            static bool ShouldSend(long targetId, Vector3D pos, double now)
            {
                SentEntry s;
                if (_sent.TryGetValue(targetId, out s))
                {
                    bool keyframe = now - s.LastKeyframe >= KEYFRAME_SECONDS;
                    bool moved = (s.Pos - pos).LengthSquared() > 0.01;
                    if (!keyframe && !moved) return false;
                    if (now - s.LastSent < RELAY_INTERVAL) return false;
                    s.Pos = pos; s.LastSent = now;
                    if (keyframe) s.LastKeyframe = now;
                    _sent[targetId] = s;
                    return true;
                }
                _sent[targetId] = new SentEntry { Pos = pos, LastSent = now, LastKeyframe = now };
                return true;
            }

            static void PruneSent()
            {
                _scratch.Clear();
                foreach (var kv in _sent)
                    if (!FleetState.Contacts.ContainsKey(kv.Key)) _scratch.Add(kv.Key);
                for (int i = 0; i < _scratch.Count; i++) _sent.Remove(_scratch[i]);
            }

            static void Broadcast()
            {
                Vector3D pos = _s.Position;

                _p.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    MyTuple.Create(TAG_STATUS, _p.Me.EntityId, pos, Vector3D.Zero, 0L,
                        MyTuple.Create(0L, 0.0, 0, "HQ")));

                // Order type: an explicit operator command wins; else ALERT (2) on TACSIT RED.
                int orderType = CommandOrderType != 0 ? CommandOrderType
                              : (FleetState.Tacsit == "RED" ? 2 : 0);
                _p.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    MyTuple.Create(TAG_STATION, _p.Me.EntityId, pos, Vector3D.Zero, 0L,
                        MyTuple.Create(0L, 0.0, orderType, BuildText())));
            }

            // STATION screen: operator orders verbatim, the active command banner, then an auto
            // readout. Compact (the jet's DL page clips lines to ~20 chars). Exact contact
            // positions ride the CONTACT relay stream, not here.
            static string BuildText()
            {
                _sb.Clear();

                // Only the part of CustomData ABOVE the zone sentinel is the broadcast orders screen;
                // anything below is the operator's zone-name block (parsed, never transmitted).
                // {TOKENS} in the orders text are substituted with live values each broadcast.
                string cd = _p.Me.CustomData ?? "";
                int si = cd.IndexOf(ZONE_SENTINEL);
                string orders = SubstituteVars((si >= 0 ? cd.Substring(0, si) : cd).Trim());
                if (orders.Length > 0) _sb.Append(orders).Append('\n');
                if (CommandBanner.Length > 0) _sb.Append('!').Append(CommandBanner).Append('\n');

                _sb.Append("-\n");
                _sb.Append("#TACSIT ").Append(FleetState.Tacsit).Append('\n');

                var jets = FleetState.SortedJets;
                int alerts = 0;
                for (int i = 0; i < jets.Count && alerts < 4; i++)
                {
                    long w = jets[i].Word;
                    if (StatusWord.Spiked(w)) { _sb.Append('!').Append(FleetState.CallSign(jets[i])).Append(" SPIKED\n"); alerts++; }
                    else if (StatusWord.Bingo(w) || StatusWord.State(w) == 5) { _sb.Append('!').Append(FleetState.CallSign(jets[i])).Append(" BINGO\n"); alerts++; }
                }
                var thr = FleetState.SortedThreats;
                if (FleetState.InboundCount > 0)
                    for (int i = 0; i < thr.Count; i++)
                        if (thr[i].Inbound) { _sb.Append("!INBOUND ").Append(SpriteHelpers.FormatRange(thr[i].Range)).Append('\n'); break; }

                _sb.Append("#THREATS ").Append(FleetState.HostileCount).Append('\n');
                for (int i = 0; i < thr.Count && i < 3; i++)
                {
                    _sb.Append('>').Append(Clip(thr[i].Name, 11, "BANDIT")).Append(' ')
                       .Append(SpriteHelpers.FormatRange(thr[i].Range));
                    if (thr[i].Inbound) _sb.Append(" <");
                    _sb.Append('\n');
                }
                int other = FleetState.Contacts.Count - FleetState.HostileCount;
                if (other > 0) _sb.Append("~+").Append(other).Append(" unk\n");

                _sb.Append("#WING ").Append(jets.Count).Append('\n');
                for (int i = 0; i < jets.Count && i < 12; i++)
                {
                    long w = jets[i].Word;
                    _sb.Append('>').Append(FleetState.CallSign(jets[i]))
                       .Append("=F").Append(StatusWord.Fuel(w))
                       .Append(" H").Append(StatusWord.Integ(w))
                       .Append(' ').Append(StatusWord.Missiles(w)).Append("m ")
                       .Append(StatusWord.StateStr(StatusWord.State(w))).Append('\n');
                }

                // Operator-drawn zones — names ride the STATION text so EVERY jet lists them with
                // zero jet code (the geometry plot is the optional Tag-3 stream, consumed only by a
                // plot-capable jet). Compact: the jet's DL page clips lines.
                var zs = ZoneStore.Zones;
                if (zs.Count > 0)
                {
                    _sb.Append("#ZONES ").Append(zs.Count).Append('\n');
                    for (int i = 0; i < zs.Count && i < 8; i++)
                    {
                        Zone z = zs[i];
                        _sb.Append('>').Append(Clip(z.Name, 12, "ZONE")).Append(' ').Append(ZoneKindAbbrev(z.Kind));
                        if (z.Shape == ZoneShape.Circle) _sb.Append(' ').Append(SpriteHelpers.FormatRange(z.Radius));
                        _sb.Append('\n');
                    }
                }
                return _sb.ToString();
            }

            // ── Zone wire broadcast (Tag 3) ──
            // Round-robin one vertex per packet across all zones; bandwidth is not a constraint so we
            // retransmit continuously (no acks — jets rebuild the picture + tolerate loss). A minimal
            // jet reads center(Vel)+radius(Num)+name(Text)+kind(Misc) → a named circle; a full jet
            // reassembles polygons from the per-vertex Pos keyed by zoneId(Packed) + index(Misc).
            static void BroadcastZones()
            {
                long me = _p.Me.EntityId;

                // Adopt freshly-deleted ids, then emit their tombstones (vertexCount = 0 = delete).
                for (int i = 0; i < ZoneStore.Removed.Count; i++) _tomb[ZoneStore.Removed[i]] = TOMB_SENDS;
                ZoneStore.Removed.Clear();
                if (_tomb.Count > 0)
                {
                    _ti.Clear();
                    foreach (var kv in _tomb) _ti.Add(kv.Key);
                    for (int i = 0; i < _ti.Count; i++)
                    {
                        int id = _ti[i];
                        _p.IGC.SendBroadcastMessage(IGC_CHANNEL,
                            MyTuple.Create(TAG_ZONE, me, Vector3D.Zero, Vector3D.Zero, (long)id,
                                MyTuple.Create(0L, 0.0, 0, "")));
                        int r = _tomb[id] - 1;
                        if (r <= 0) _tomb.Remove(id); else _tomb[id] = r;
                    }
                }

                var zs = ZoneStore.Zones;
                if (zs.Count == 0) { _zi = 0; _vi = 0; return; }
                for (int sent = 0, guard = 0; sent < ZONE_BATCH && guard < ZONE_BATCH * 4; guard++)
                {
                    if (_zi >= zs.Count) { _zi = 0; _vi = 0; }
                    Zone z = zs[_zi];
                    int vc = z.Shape == ZoneShape.Circle ? 1 : z.Verts.Count;
                    if (vc <= 0) { _zi++; _vi = 0; continue; }
                    SendZonePacket(z, _vi, vc, me);
                    sent++;
                    _vi++;
                    if (_vi >= vc) { _zi++; _vi = 0; }
                }
            }

            static void SendZonePacket(Zone z, int vi, int vc, long me)
            {
                Vector3D vert = z.Shape == ZoneShape.Circle
                    ? z.Center
                    : (vi < z.Verts.Count ? z.Verts[vi] : z.Center);
                int misc = (vi & 63) | ((vc & 63) << 6) | (((int)z.Shape & 7) << 12) | (((int)z.Kind & 15) << 15);
                _p.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    MyTuple.Create(TAG_ZONE, me, vert, z.Center, (long)z.Id,
                        MyTuple.Create(0L, z.Radius, misc, z.Name ?? "")));
            }

            // ── CustomData zone block: populate + watch ──
            // Keeps the "<id>=<name>" lines below the ===ZONES=== sentinel in sync with the live zone
            // set: READS the block every cycle (applying operator renames), and (re)WRITES it only
            // when the SET of zone ids changes (a zone was drawn / deleted). It never rewrites when
            // only a name differs — names flow FROM CustomData — so an in-progress edit is never
            // clobbered. The orders text (and any {TOKENS}) above the sentinel is preserved verbatim.
            static void SyncZoneCustomData()
            {
                string cd = _p.Me.CustomData ?? "";
                int si = cd.IndexOf(ZONE_SENTINEL);
                string section = si >= 0 ? cd.Substring(si + ZONE_SENTINEL.Length) : "";

                ZoneStore.ApplyRenames(section);   // pick up operator renames

                var zones = ZoneStore.Zones;
                if (zones.Count == 0)
                {
                    if (si >= 0) _p.Me.CustomData = cd.Substring(0, si).TrimEnd('\n', '\r', ' ', '\t');
                    return;
                }

                // Collect the zone ids the operator's block currently lists.
                _cdIds.Clear();
                string[] lines = section.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq <= 0) continue;
                    int id;
                    if (int.TryParse(lines[i].Substring(0, eq).Trim(), out id)) _cdIds.Add(id);
                }

                // If the id sets already match, leave CustomData untouched (don't clobber edits).
                bool match = _cdIds.Count == zones.Count;
                if (match)
                    for (int i = 0; i < zones.Count; i++)
                        if (!_cdIds.Contains(zones[i].Id)) { match = false; break; }
                if (match) return;

                // Regenerate the block (preserving the orders text above the sentinel).
                string orders = (si >= 0 ? cd.Substring(0, si) : cd).TrimEnd('\n', '\r', ' ', '\t');
                _sb.Clear();
                if (orders.Length > 0) _sb.Append(orders).Append('\n');
                _sb.Append(ZONE_SENTINEL);
                for (int i = 0; i < zones.Count; i++)
                    _sb.Append('\n').Append(zones[i].Id).Append('=').Append(zones[i].Name);
                _p.Me.CustomData = _sb.ToString();
            }

            // ── Live {TOKEN} substitution for the broadcast orders screen ──
            static string SubstituteVars(string s)
            {
                if (s.IndexOf('{') < 0) return s;   // fast path — no tokens
                s = s.Replace("{TACSIT}", FleetState.Tacsit);
                s = s.Replace("{JETS}", FleetState.Jets.Count.ToString());
                s = s.Replace("{HOSTILES}", FleetState.HostileCount.ToString());
                s = s.Replace("{CONTACTS}", FleetState.Contacts.Count.ToString());
                s = s.Replace("{INBOUND}", FleetState.InboundCount.ToString());
                s = s.Replace("{BINGO}", BingoCount().ToString());
                s = s.Replace("{ZONES}", ZoneStore.Zones.Count.ToString());
                s = s.Replace("{RELAY}", _sent.Count.ToString());
                s = s.Replace("{NEAREST}", NearestThreat(true));
                s = s.Replace("{NEARESTNAME}", NearestThreat(false));
                s = s.Replace("{ANT}", ((int)(_s.AntennaRange / 1000.0)) + "km");
                s = s.Replace("{UPTIME}", MMSS(SystemManager.ElapsedSeconds));
                return s;
            }

            static int BingoCount()
            {
                var j = FleetState.SortedJets;
                int n = 0;
                for (int i = 0; i < j.Count; i++)
                    if (StatusWord.Bingo(j[i].Word) || StatusWord.State(j[i].Word) == 5) n++;
                return n;
            }

            static string NearestThreat(bool range)
            {
                var t = FleetState.SortedThreats;
                if (t.Count == 0) return "none";
                return range ? SpriteHelpers.FormatRange(t[0].Range) : Clip(t[0].Name, 12, "BANDIT");
            }

            static string MMSS(double s)
            {
                int t = (int)s;
                return (t / 60) + ":" + (t % 60).ToString("D2");
            }

            static string ZoneKindAbbrev(ZoneKind k)
            {
                switch (k)
                {
                    case ZoneKind.Enemy: return "ENY";
                    case ZoneKind.NoFly: return "NFZ";
                    case ZoneKind.SAM:   return "SAM";
                    case ZoneKind.CAP:   return "CAP";
                    case ZoneKind.Rally: return "RLY";
                    default: return "ZON";
                }
            }
        }
    }
}
