using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class MissileBayHelper
        {
            public const string IGC_CHANNEL_PREFIX = "JETOS_MSL_";
            const string IGC_STATUS_CHANNEL = "JETOS_MSL_STAT";
            const double STATUS_TIMEOUT = 8.0;
            static IMyBroadcastListener _statusListener;
            static readonly List<MissileStatus> _missileStatus = new List<MissileStatus>();

            public struct MissileStatus
            {
                public int Bay;
                public Vector3D Position;
                public Vector3D Velocity;
                public double Eta;
                public bool Acquired;
                public bool ActiveTrackingUnlocked;
                public double SeenAt;
            }

            public static bool IsBayReady(IMyShipMergeBlock bay)
            {
                return bay != null && bay.IsConnected;
            }

            public static void ToggleBaySelection(bool[] baySelected, int bayIndex)
            {
                if (bayIndex >= 0 && bayIndex < baySelected.Length)
                {
                    baySelected[bayIndex] = !baySelected[bayIndex];
                }
            }

            public static void ToggleSelectedBays(List<IMyShipMergeBlock> bays, bool[] baySelected)
            {
                for (int i = 0; i < bays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        var bay = bays[i];
                        if (bay != null)
                        {
                            bay.Enabled = !bay.Enabled;
                        }
                    }
                }
            }

            static int ExtractBayNumber(IMyShipMergeBlock bay, int fallback)
            {
                if (bay == null) return fallback;
                var parts = bay.CustomName.Split(' ');
                int number;
                if (parts.Length > 1 && int.TryParse(parts[1], out number))
                    return number;
                return fallback;
            }

            public static int GetBayNumber(IMyShipMergeBlock bay, int fallback)
            {
                return ExtractBayNumber(bay, fallback);
            }

            static void UpsertStatus(MissileStatus s)
            {
                for (int i = 0; i < _missileStatus.Count; i++)
                {
                    if (_missileStatus[i].Bay == s.Bay)
                    {
                        _missileStatus[i] = s;
                        return;
                    }
                }
                _missileStatus.Add(s);
            }

            static void PruneStatus()
            {
                double now = SystemManager.ElapsedSeconds;
                for (int i = _missileStatus.Count - 1; i >= 0; i--)
                    if (now - _missileStatus[i].SeenAt > STATUS_TIMEOUT)
                        _missileStatus.RemoveAt(i);
            }

            public static void PollMissileStatus(Program program)
            {
                if (program == null) return;
                if (_statusListener == null)
                    _statusListener = program.IGC.RegisterBroadcastListener(IGC_STATUS_CHANNEL);
                while (_statusListener.HasPendingMessage)
                {
                    MyIGCMessage msg = _statusListener.AcceptMessage();
                    if (msg.Data is MyTuple<int, Vector3D, Vector3D, double, bool, bool>)
                    {
                        var t = (MyTuple<int, Vector3D, Vector3D, double, bool, bool>)msg.Data;
                        UpsertStatus(new MissileStatus
                        {
                            Bay = t.Item1,
                            Position = t.Item2,
                            Velocity = t.Item3,
                            Eta = t.Item4,
                            Acquired = t.Item5,
                            ActiveTrackingUnlocked = t.Item6,
                            SeenAt = SystemManager.ElapsedSeconds
                        });
                    }
                    else if (msg.Data is MyTuple<int, Vector3D, Vector3D, double, bool>)
                    {
                        var t = (MyTuple<int, Vector3D, Vector3D, double, bool>)msg.Data;
                        UpsertStatus(new MissileStatus
                        {
                            Bay = t.Item1,
                            Position = t.Item2,
                            Velocity = t.Item3,
                            Eta = t.Item4,
                            Acquired = t.Item5,
                            ActiveTrackingUnlocked = false,
                            SeenAt = SystemManager.ElapsedSeconds
                        });
                    }
                }
                PruneStatus();
            }

            public static List<MissileStatus> GetActiveMissileStatus()
            {
                PruneStatus();
                return _missileStatus;
            }

            public static bool TryGetMissileStatus(int bay, out MissileStatus status)
            {
                PruneStatus();
                for (int i = 0; i < _missileStatus.Count; i++)
                {
                    if (_missileStatus[i].Bay == bay)
                    {
                        status = _missileStatus[i];
                        return true;
                    }
                }
                status = default(MissileStatus);
                return false;
            }

            public static string FormatEta(double eta)
            {
                if (eta < 0 || eta > 99) return "--";
                return ((int)(eta + 0.999)).ToString();
            }

            static bool TryGetTargetPosition(Jet jet, out Vector3D pos)
            {
                Vector3D vel;
                return TryGetTargetData(jet, out pos, out vel);
            }

            static bool TryGetTargetData(Jet jet, out Vector3D pos, out Vector3D vel)
            {
                pos = default(Vector3D);
                vel = VZ;
                if (jet == null)
                    return false;

                var selected = jet.GetSelectedEnemy();
                if (selected.HasValue)
                {
                    pos = selected.Value.Position;
                    vel = selected.Value.Velocity;
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Writes the launch-time CustomData that the missile reads during CheckForGPSAndStart:
            /// Topdown / AntiAir flags plus a per-bay GPS slot (1:GPS:..., 2:GPS:...). Missiles fly
            /// straight at the target — no cone/salvo/approach setup is written since the geometry
            /// asks for tighter turns than the missile can actually pull.
            /// </summary>
            static void WriteLaunchSetup(
                List<IMyShipMergeBlock> bays,
                bool[] baySelected,
                Jet jet,
                bool topdown)
            {
                Vector3D targetPos;
                if (!TryGetTargetPosition(jet, out targetPos))
                    return;

                string gps = NavigationHelper.FormatGps(targetPos);

                SystemManager.SetCustomDataValue(CD_TOPDOWN, topdown ? S_TRUE : S_FALSE);
                SystemManager.SetCustomDataValue(CD_ANTI_AIR, S_TRUE);
                SystemManager.SetCustomDataValue(CD_CACHED, gps);

                for (int i = 0; i < bays.Count; i++)
                {
                    if (i >= baySelected.Length || !baySelected[i] || !IsBayReady(bays[i]))
                        continue;
                    int bayNum = ExtractBayNumber(bays[i], i + 1);
                    SystemManager.SetCustomDataValue(bayNum.ToString(), gps);
                }
            }

            public static void FireSelectedBays(
                List<IMyShipMergeBlock> bays,
                bool[] baySelected,
                Program program,
                Jet jet,
                bool topdown)
            {
                Vector3D targetPos;
                if (!TryGetTargetPosition(jet, out targetPos))
                    return;

                WriteLaunchSetup(bays, baySelected, jet, topdown);

                for (int i = 0; i < bays.Count; i++)
                {
                    if (i >= baySelected.Length || !baySelected[i])
                        continue;
                    var bay = bays[i];
                    if (bay == null || !bay.IsConnected)
                        continue;
                    try
                    {
                        bay.ApplyAction("Fire");
                    }
                    catch
                    {
                        program?.Echo($"B{i} fire");
                    }
                }
            }

            /// <summary>
            /// Quick-fire: pick the first connected bay and launch without needing a selection.
            /// Re-writes the launch setup for just that bay so the slot GPS is correct.
            /// </summary>
            static void FireNextAvailableBay(
                List<IMyShipMergeBlock> bays,
                Program program,
                Jet jet,
                bool topdown)
            {
                Vector3D targetPos;
                if (!TryGetTargetPosition(jet, out targetPos))
                    return;

                for (int i = 0; i < bays.Count; i++)
                {
                    if (!IsBayReady(bays[i])) continue;

                    string gps = NavigationHelper.FormatGps(targetPos);
                    int bayNum = ExtractBayNumber(bays[i], i + 1);

                    SystemManager.SetCustomDataValue(CD_TOPDOWN, topdown ? S_TRUE : S_FALSE);
                    SystemManager.SetCustomDataValue(CD_ANTI_AIR, S_TRUE);
                    SystemManager.SetCustomDataValue(CD_CACHED, gps);
                    SystemManager.SetCustomDataValue(bayNum.ToString(), gps);

                    try
                    {
                        bays[i].ApplyAction("Fire");
                    }
                    catch
                    {
                        program?.Echo($"B{i} fire");
                    }
                    return;
                }
            }

            /// <summary>
            /// Per-tick target stream on JETOS_MSL_&lt;bayNumber&gt;. We always pass Vector3D.Zero for the
            /// approach offset so the missile flies straight at the target — the cone geometry would
            /// demand sharper turns than the missile can actually pull at the waypoint.
            /// </summary>
            public static void BroadcastTargetUpdates(
                Program program,
                Jet jet,
                List<IMyShipMergeBlock> bays)
            {
                if (program == null || jet == null || bays.Count == 0)
                    return;

                Vector3D targetPos, targetVel;
                if (!TryGetTargetData(jet, out targetPos, out targetVel))
                    return;

                for (int i = 0; i < bays.Count; i++)
                {
                    int bayNum = ExtractBayNumber(bays[i], i + 1);
                    var payload = MyTuple.Create(targetPos, targetVel, VZ);
                    program.IGC.SendBroadcastMessage(IGC_CHANNEL_PREFIX + bayNum, payload);
                }
            }

            public const string WEAPON_HOTKEYS = "5 FIRE\n";

            public static void HandleWeaponHotkey(
                int key,
                List<IMyShipMergeBlock> bays,
                Program program,
                Jet jet,
                bool topdown)
            {
                if (key == 5)
                {
                    FireNextAvailableBay(bays, program, jet, topdown);
                }
            }

            static char ColorToChar(int r, int g, int b)
            {
                const double BIT_SPACING = 255.0 / 7.0;
                return (char)(
                    0xe100
                    + ((int)Rd(r / BIT_SPACING) << 6)
                    + ((int)Rd(g / BIT_SPACING) << 3)
                    + (int)Rd(b / BIT_SPACING)
                );
            }

            public static void BuildBayOptionList(List<string> options, List<IMyShipMergeBlock> bays, bool[] baySelected)
            {
                for (int i = 0; i < bays.Count; i++)
                {
                    string baySymbol = (i < baySelected.Length && baySelected[i]) ? "[X]" : "[ ]";
                    string bayStatus = bays[i]?.IsConnected == true ? "[ON]" : "[OFF]";
                    var mergeBlock = bays[i] as IMyShipMergeBlock;
                    bool isConnected = mergeBlock != null && mergeBlock.IsConnected;
                    char colorChar = ColorToChar(isConnected ? 0 : 255, isConnected ? 255 : 0, 0);
                    options.Add($"{colorChar}{baySymbol} {bays[i]?.CustomName ?? "Bay?"} {bayStatus}");
                }
            }
        }
    }
}
