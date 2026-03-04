using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class AirtoAir : ProgramModule
        {
            private List<IMyShipMergeBlock> missileBays = new List<IMyShipMergeBlock>();
            private bool[] baySelected;
            private bool isAirtoAirenabled = false;
            private RadarTrackingModule radarTracker;
            private Jet myJet;

            private void UpdateCustomDataWithCache(string gpsCoordinates, string cachedSpeed)
            {
                int cachedColonIdx = gpsCoordinates.IndexOf(':');
                if (cachedColonIdx > 0)
                {
                    SystemManager.SetCustomDataValue("Cached", gpsCoordinates.Substring(cachedColonIdx + 1));
                }

                int speedColonIdx = cachedSpeed.IndexOf(':');
                if (speedColonIdx > 0)
                {
                    SystemManager.SetCustomDataValue("CachedSpeed", cachedSpeed.Substring(speedColonIdx + 1));
                }
            }

            public AirtoAir(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                missileBays = jet._bays;
                baySelected = new bool[missileBays.Count];
                name = "Air To Air";

                // Initialize radar tracker with AI blocks (backward compatibility - primary only)
                if (jet._aiFlightBlock != null && jet._aiCombatBlock != null)
                {
                    radarTracker = new RadarTrackingModule(jet._aiFlightBlock, jet._aiCombatBlock);
                }

            }

            public override string[] GetOptions()
            {
                var options = new List<string>
                {
                    "Fire Selected Bays",
                    "Toggle Selected Bays",
                    string.Format("Seeker [{0}]", isAirtoAirenabled ? "ON" : "OFF")
                };

                for (int i = 0; i < missileBays.Count; i++)
                {
                    string baySymbol = baySelected[i] ? "[X]" : "[ ]";
                    string bayStatus = missileBays[i]?.IsConnected == true ? "[ON]" : "[OFF]";
                    var mergeBlock = missileBays[i] as IMyShipMergeBlock;
                    bool isConnected = mergeBlock != null && mergeBlock.IsConnected;
                    char colorChar = ColorToChar(isConnected ? 0 : 255, isConnected ? 255 : 0, 0);
                    options.Add(
                        string.Format(
                            "{0}{1} {2} {3}",
                            colorChar,
                            baySymbol,
                            missileBays[i]?.CustomName ?? "Unknown Bay",
                            bayStatus
                        )
                    );
                }
                return options.ToArray();
            }

            public override void ExecuteOption(int index)
            {
                if (index == 0)
                {
                    FireSelectedBays();
                    TransferCacheToSlots();
                }
                else if (index == 1)
                {
                    ToggleSelectedBays();
                }
                else if (index == 2)
                {
                    ToggleSensor();
                    ToggleAirtoAirMode();
                }
                else
                {
                    int bayOffset = 3;
                    if (index >= bayOffset && index - bayOffset < missileBays.Count)
                    {
                        ToggleBaySelection(index - bayOffset);
                    }
                }
            }

            private void ToggleAirtoAirMode()
            {
                isAirtoAirenabled = !isAirtoAirenabled;
                UpdateTopdownCustomData();
            }

            private void ToggleSensor()
            {
                if (isAirtoAirenabled)
                {
                    if (radarTracker != null)
                    {
                        if (radarTracker.L_CombatBLock != null)
                        {
                            radarTracker.L_CombatBLock.Enabled = false;
                            radarTracker.L_CombatBLock.ApplyAction("ActivateBehavior_Off");
                        }
                        if (radarTracker.L_FlightBlock != null)
                        {
                            radarTracker.L_FlightBlock.Enabled = false;
                            radarTracker.L_FlightBlock.ApplyAction("ActivateBehavior_Off");
                        }
                    }
                }
                else
                {
                    if (radarTracker != null)
                    {
                        if (radarTracker.L_CombatBLock != null)
                        {
                            radarTracker.L_CombatBLock.Enabled = true;
                            radarTracker.L_CombatBLock.UpdateTargetInterval = 4;
                            radarTracker.L_CombatBLock.SearchEnemyComponent.TargetingLockOptions = VRage.Game.ModAPI.Ingame.MyGridTargetingRelationFiltering.Enemy;
                            radarTracker.L_CombatBLock.SelectedAttackPattern = 3;
                            radarTracker.L_CombatBLock.SetValue<long>("OffensiveCombatIntercept_GuidanceType", 0);
                            radarTracker.L_CombatBLock.SetValueBool("OffensiveCombatIntercept_OverrideCollisionAvoidance", true);
                            radarTracker.L_CombatBLock.ApplyAction("ActivateBehavior_On");
                            radarTracker.L_CombatBLock.ApplyAction("SetTargetingGroup_Weapons");
                            radarTracker.L_CombatBLock.ApplyAction("SetTargetPriority_Closest");
                        }
                        if (radarTracker.L_FlightBlock != null)
                        {
                            radarTracker.L_FlightBlock.Enabled = false;
                            radarTracker.L_FlightBlock.MinimalAltitude = 10;
                            radarTracker.L_FlightBlock.PrecisionMode = false;
                            radarTracker.L_FlightBlock.SpeedLimit = 400;
                            radarTracker.L_FlightBlock.AlignToPGravity = false;
                            radarTracker.L_FlightBlock.CollisionAvoidance = false;
                            radarTracker.L_FlightBlock.ApplyAction("ActivateBehavior_On");
                        }
                    }
                }
            }

            private void UpdateTopdownCustomData()
            {
                SystemManager.SetCustomDataValue("AntiAir", isAirtoAirenabled ? "true" : "false");
            }

            public override void Tick()
            {
                // ===== ALWAYS: auto-select and GPS sync from RadarControlModule contacts =====
                if (!myJet.HasSelectedEnemy() && myJet.enemyList.Count > 0)
                {
                    var closest = myJet.GetClosestNEnemies(1);
                    if (closest.Count > 0)
                    {
                        myJet.SelectEnemy(closest[0]);
                    }
                }

                if (myJet.HasSelectedEnemy())
                {
                    SystemManager.UpdateActiveTargetGPS();
                }

                // ===== SEEKER OFF: skip active tracking and sounds =====
                if (!isAirtoAirenabled)
                {
                    return;
                }

                // ===== SEEKER ON: active radar tracker + weapon tones =====
                if (radarTracker != null)
                {
                    radarTracker.UpdateTracking(SystemManager.currentTick);
                }

                if (radarTracker != null)
                {
                    if (radarTracker.IsTracking)
                    {
                        SoundManager.RequestWeapon("AIM9Lock", SoundManager.PRIORITY_LOCK, 300);
                    }
                    else
                    {
                        SoundManager.RequestWeapon("AIM9Search", SoundManager.PRIORITY_SEARCH, 300);
                    }
                }
            }

            private void FireSelectedBays()
            {
                var selectedBays = new StringBuilder("Firing bays: ");
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        selectedBays.Append(missileBays[i]?.CustomName + " ");
                        FireMissileFromBayWithGps(i);
                    }
                }
            }

            private void FireNextAvailableBay()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    var bay = missileBays[i];
                    if (IsBayReadyToFire(bay))
                    {
                        try
                        {
                            FireMissileFromBayWithGps(i);
                            return;
                        }
                        catch (Exception e)
                        {
                            ParentProgram.Echo($"Bay {i} fire failed: {e.Message}");
                        }
                    }
                }
            }

            private bool IsBayReadyToFire(IMyShipMergeBlock bay)
            {
                if (bay == null)
                {
                    return false;
                }
                return bay.IsConnected;
            }

            private void ToggleBaySelection(int bayIndex)
            {
                if (bayIndex >= 0 && bayIndex < baySelected.Length)
                {
                    baySelected[bayIndex] = !baySelected[bayIndex];
                }
            }

            private void ToggleSelectedBays()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        var bay = missileBays[i];
                        if (bay != null)
                        {
                            bay.Enabled = !bay.Enabled;
                        }
                    }
                }
            }

            private void FireMissileFromBayWithGps(
                int bayIndex,
                Vector3D targetPosition = default(Vector3D)
            )
            {
                try
                {
                    var bay = missileBays[bayIndex];
                    if (bay == null || !bay.IsConnected)
                    {
                        return;
                    }
                    if (targetPosition.Equals(default(Vector3D)))
                    {
                        string cachedValue = SystemManager.GetCustomDataValue("Cached");
                        if (string.IsNullOrEmpty(cachedValue) || !cachedValue.StartsWith("GPS:"))
                        {
                            return;
                        }
                        var parts = cachedValue.Split(':');
                        if (parts.Length < 5)
                        {
                            return;
                        }
                        double x, y, z;
                        if (
                            !double.TryParse(parts[2], out x)
                            || !double.TryParse(parts[3], out y)
                            || !double.TryParse(parts[4], out z)
                        )
                        {
                            return;
                        }
                        targetPosition = new Vector3D(x, y, z);
                    }
                    string gpsData = string.Format(
                        "GPS:Target:{0}:{1}:{2}:#FF75C9F1:",
                        targetPosition.X,
                        targetPosition.Y,
                        targetPosition.Z
                    );
                    UpdateCustomDataWithGpsData(bayIndex, gpsData);
                    bay.ApplyAction("Fire");
                }
                catch (Exception e)
                {
                    ParentProgram.Echo($"FireMissile error: {e.Message}");
                }
            }

            private void UpdateCustomDataWithGpsData(int bayIndex, string gpsData)
            {
                string cacheKey = string.Format("Cache{0}", bayIndex);
                SystemManager.SetCustomDataValue(cacheKey, gpsData);
            }

            private void TransferCacheToSlots()
            {
                for (int i = 0; i < missileBays.Count; i++)
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

            private static char ColorToChar(int r, int g, int b)
            {
                const double BIT_SPACING = 255.0 / 7.0;
                return (char)(
                    0xe100
                    + ((int)Math.Round(r / BIT_SPACING) << 6)
                    + ((int)Math.Round(g / BIT_SPACING) << 3)
                    + (int)Math.Round(b / BIT_SPACING)
                );
            }

            public override void HandleSpecialFunction(int key)
            {
                if (key == 5)
                {
                    FireNextAvailableBay();
                    TransferCacheToSlots();
                }
                if (key == 7)
                {
                    TransferCacheToSlots();
                }
            }

            public string hotkeytext =
                "5: Fire Next Available Bay\n6: Fire Selected Bays\n7: Toggle Selected Bays\n";
            public override string GetHotkeys()
            {
                return hotkeytext;
            }
        }
    }
}
