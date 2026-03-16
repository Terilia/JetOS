using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class MissileBayHelper
        {
            public static void FireSelectedBays(List<IMyShipMergeBlock> bays, bool[] baySelected, Program program)
            {
                for (int i = 0; i < bays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        FireMissileFromBay(bays, i, default(Vector3D), program);
                    }
                }
            }

            public static void FireNextAvailableBay(List<IMyShipMergeBlock> bays, Program program)
            {
                for (int i = 0; i < bays.Count; i++)
                {
                    if (IsBayReady(bays[i]))
                    {
                        try
                        {
                            FireMissileFromBay(bays, i, default(Vector3D), program);
                            return;
                        }
                        catch (Exception e)
                        {
                            program.Echo($"Bay {i} fire failed: {e.Message}");
                        }
                    }
                }
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

            public static void TransferCacheToSlots(int bayCount)
            {
                for (int i = 0; i < bayCount; i++)
                {
                    string cacheKey = string.Format("Cache{0}", i);
                    string cacheContent = SystemManager.GetCustomDataValue(cacheKey);

                    if (!string.IsNullOrEmpty(cacheContent))
                    {
                        string slotKey = i.ToString();
                        SystemManager.SetCustomDataValue(slotKey, cacheContent);
                        SystemManager.SetCustomDataValue(cacheKey, "");
                    }
                }
            }

            public static void FireMissileFromBay(
                List<IMyShipMergeBlock> bays,
                int bayIndex,
                Vector3D targetPosition,
                Program program,
                Jet jet = null)
            {
                try
                {
                    var bay = bays[bayIndex];
                    if (bay == null || !bay.IsConnected)
                        return;

                    if (targetPosition.Equals(default(Vector3D)))
                    {
                        // Try selected enemy first
                        if (jet != null)
                        {
                            var selected = jet.GetSelectedEnemy();
                            if (selected.HasValue)
                            {
                                targetPosition = selected.Value.Position;
                            }
                        }

                        // Fallback: read from GPS cache
                        if (targetPosition.Equals(default(Vector3D)))
                        {
                            if (!NavigationHelper.TryParseGps(SystemManager.GetCustomDataValue("Cached"), out targetPosition))
                                return;
                        }
                    }

                    string gpsData = NavigationHelper.FormatGps(targetPosition);
                    string cacheKey = string.Format("Cache{0}", bayIndex);
                    SystemManager.SetCustomDataValue(cacheKey, gpsData);
                    bay.ApplyAction("Fire");
                }
                catch (Exception e)
                {
                    program.Echo($"FireMissile error: {e.Message}");
                }
            }

            public const string WEAPON_HOTKEYS = "5: Fire Next Available Bay\n6: Fire Selected Bays\n7: Toggle Selected Bays\n";

            /// <summary>
            /// Shared hotkey handling for weapon modules (AirToGround and AirtoAir).
            /// </summary>
            public static void HandleWeaponHotkey(int key, List<IMyShipMergeBlock> bays, Program program)
            {
                if (key == 5)
                {
                    FireNextAvailableBay(bays, program);
                    TransferCacheToSlots(bays.Count);
                }
                if (key == 7)
                {
                    TransferCacheToSlots(bays.Count);
                }
            }

            public static char ColorToChar(int r, int g, int b)
            {
                const double BIT_SPACING = 255.0 / 7.0;
                return (char)(
                    0xe100
                    + ((int)Math.Round(r / BIT_SPACING) << 6)
                    + ((int)Math.Round(g / BIT_SPACING) << 3)
                    + (int)Math.Round(b / BIT_SPACING)
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
