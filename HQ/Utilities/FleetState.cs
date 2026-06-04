using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // The fused tactical picture — HQ's authoritative model of the fleet and the threat
        // environment, built from every jet's STATUS ping and every relayed CONTACT. This is
        // what the jet currently throws away; the station's whole value is fusing it here.
        //
        //  * Jets are keyed by EntityId (5 s timeout).
        //  * Contacts are deduped by TARGET EntityId across all reporting jets — the freshest
        //    observation wins — and decay after 30 s of silence.
        //  * Recompute() derives range-to-HQ + INBOUND per entry and the TACSIT level, and
        //    publishes ready-to-render SortedThreats / SortedJets for the UI modules.
        static class FleetState
        {
            public const char KIND_HOSTILE = 'H';
            public const char KIND_NEUTRAL = 'N';
            public const char KIND_UNKNOWN = 'U';
            public static bool IsMapKind(char k) => k == KIND_NEUTRAL || k == KIND_UNKNOWN;

            public const double CONTACT_DECAY = 30.0;   // wall-clock seconds before a contact is pruned
            public const double JET_TIMEOUT   = 5.0;    // wall-clock seconds before a jet drops off
            // Alert thresholds are operator-tunable (HQConfig, read live below).

            public struct JetEntry
            {
                public long Id;
                public string Callsign;
                public long Word;
                public Vector3D Pos, Vel;
                public long TargetId;
                public double SeenAt;
                public double Range;   // to HQ (derived)
                public int Urgency;    // sort key (derived)
            }

            public struct Contact
            {
                public long Id;          // target EntityId
                public char Kind;
                public string Name;
                public Vector3D Pos, Vel;
                public long ObserverId;  // original observer (preserved across relay)
                public int Hop;
                public double ObservedAt; // ElapsedSeconds of the observation (freshness)
                public double SeenAt;     // last time HQ heard about it (decay clock)
                public double Range;      // to HQ (derived)
                public bool Inbound;      // closing on HQ (derived)
            }

            public static readonly Dictionary<long, JetEntry> Jets = new Dictionary<long, JetEntry>();
            public static readonly Dictionary<long, Contact> Contacts = new Dictionary<long, Contact>();

            // Republished each Recompute — UI modules read these directly.
            public static readonly List<JetEntry> SortedJets = new List<JetEntry>();
            public static readonly List<Contact> SortedThreats = new List<Contact>(); // hostiles, nearest first

            public static string Tacsit = "GREEN";
            public static int HostileCount;
            public static int InboundCount;

            private static readonly List<long> _scratch = new List<long>();

            public static void UpsertJet(long id, string callsign, long word, Vector3D pos, Vector3D vel, long targetId)
            {
                Jets[id] = new JetEntry
                {
                    Id = id,
                    Callsign = callsign ?? "",
                    Word = word,
                    Pos = pos,
                    Vel = vel,
                    TargetId = targetId,
                    SeenAt = SystemManager.ElapsedSeconds
                };
            }

            public static void UpsertContact(char kind, long observerId, long targetId,
                Vector3D pos, Vector3D vel, string name, double age, int hop)
            {
                double now = SystemManager.ElapsedSeconds;
                double observedAt = now - age;
                Contact existing;
                if (Contacts.TryGetValue(targetId, out existing) && observedAt < existing.ObservedAt)
                    return; // we already hold a fresher observation of this target

                Contacts[targetId] = new Contact
                {
                    Id = targetId,
                    Kind = kind,
                    Name = name ?? "",
                    Pos = pos,
                    Vel = vel,
                    ObserverId = observerId,
                    Hop = hop,
                    ObservedAt = observedAt,
                    SeenAt = now
                };
            }

            public static void Prune()
            {
                double now = SystemManager.ElapsedSeconds;
                _scratch.Clear();
                foreach (var kv in Jets)
                    if (now - kv.Value.SeenAt > JET_TIMEOUT) _scratch.Add(kv.Key);
                for (int i = 0; i < _scratch.Count; i++) Jets.Remove(_scratch[i]);

                _scratch.Clear();
                foreach (var kv in Contacts)
                    if (now - kv.Value.SeenAt > CONTACT_DECAY) _scratch.Add(kv.Key);
                for (int i = 0; i < _scratch.Count; i++) Contacts.Remove(_scratch[i]);
            }

            // Derive range/inbound + TACSIT, and rebuild the sorted display buffers.
            public static void Recompute(Vector3D hqPos)
            {
                HostileCount = 0;
                InboundCount = 0;
                bool red = false, amber = false;

                SortedThreats.Clear();
                foreach (var kv in Contacts)
                {
                    Contact c = kv.Value;
                    c.Range = VDi(c.Pos, hqPos);
                    Vector3D to = hqPos - c.Pos;
                    double closing = to.LengthSquared() > 1 ? VD(c.Vel, VN(to)) : 0;
                    c.Inbound = closing > HQConfig.InboundSpeed;

                    if (c.Kind == KIND_HOSTILE)
                    {
                        HostileCount++;
                        amber = true;
                        if (c.Range < HQConfig.AlertClose) red = true;
                        if (c.Inbound)
                        {
                            InboundCount++;
                            if (c.Range < HQConfig.AlertDefense) red = true;
                        }
                        SortedThreats.Add(c);
                    }
                }
                SortedThreats.Sort((a, b) => a.Range.CompareTo(b.Range));

                SortedJets.Clear();
                foreach (var kv in Jets)
                {
                    JetEntry j = kv.Value;
                    j.Range = VDi(j.Pos, hqPos);
                    j.Urgency = JetUrgency(j.Word);
                    if (StatusWord.Spiked(j.Word)) red = true;
                    if (StatusWord.Bingo(j.Word) || StatusWord.State(j.Word) == 5) amber = true;
                    if (StatusWord.Rwr(j.Word)) amber = true;
                    SortedJets.Add(j);
                }
                SortedJets.Sort((a, b) => a.Urgency != b.Urgency ? b.Urgency.CompareTo(a.Urgency) : a.Range.CompareTo(b.Range));

                Tacsit = red ? "RED" : amber ? "AMBER" : "GREEN";
            }

            // Callsign with a stable fallback when the jet's grid name didn't arrive.
            public static string CallSign(JetEntry j)
            {
                return j.Callsign.Length > 0 ? j.Callsign : ("J" + (j.Id % 10000));
            }

            // Higher = more urgent. spiked > bingo > defending/rwr > engaging > cruise.
            static int JetUrgency(long w)
            {
                if (StatusWord.Spiked(w)) return 4;
                if (StatusWord.Bingo(w) || StatusWord.State(w) == 5) return 3;
                if (StatusWord.State(w) == 3 || StatusWord.Rwr(w)) return 2;
                if (StatusWord.State(w) == 2) return 1;
                return 0;
            }
        }
    }
}
