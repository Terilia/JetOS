using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
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

            static bool TryGetTargetPosition(Jet jet, out Vector3D pos)
            {
                pos = default(Vector3D);
                if (jet != null)
                {
                    var selected = jet.GetSelectedEnemy();
                    if (selected.HasValue)
                    {
                        pos = selected.Value.Position;
                        return true;
                    }
                }
                return NavigationHelper.TryParseGps(SystemManager.GetCustomDataValue("Cached"), out pos);
            }

            static bool TryGetTargetData(Jet jet, out Vector3D pos, out Vector3D vel)
            {
                pos = default(Vector3D);
                vel = VZ;
                if (jet != null)
                {
                    var selected = jet.GetSelectedEnemy();
                    if (selected.HasValue)
                    {
                        pos = selected.Value.Position;
                        vel = selected.Value.Velocity;
                        return true;
                    }
                }
                return NavigationHelper.TryParseGps(SystemManager.GetCustomDataValue("Cached"), out pos);
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

                SystemManager.SetCustomDataValue("Topdown", topdown ? "true" : "false");
                SystemManager.SetCustomDataValue("AntiAir", "true");
                SystemManager.SetCustomDataValue("Cached", gps);

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
                    catch (Exception e)
                    {
                        program?.Echo($"Bay {i} fire failed: {e.Message}");
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

                    SystemManager.SetCustomDataValue("Topdown", topdown ? "true" : "false");
                    SystemManager.SetCustomDataValue("AntiAir", "true");
                    SystemManager.SetCustomDataValue("Cached", gps);
                    SystemManager.SetCustomDataValue(bayNum.ToString(), gps);

                    try
                    {
                        bays[i].ApplyAction("Fire");
                    }
                    catch (Exception e)
                    {
                        program?.Echo($"Bay {i} fire failed: {e.Message}");
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

            public const string WEAPON_HOTKEYS = "5: Fire Next Available Bay\n";

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
                    options.Add(
                        string.Format(
                            "{0}{1} {2} {3}",
                            colorChar,
                            baySymbol,
                            bays[i]?.CustomName ?? "Unknown Bay",
                            bayStatus
                        )
                    );
                }
            }
        }
    }
}
