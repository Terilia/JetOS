using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class AirToGround : ProgramModule
        {
            private List<IMyShipMergeBlock> missileBays = new List<IMyShipMergeBlock>();
            private bool[] baySelected;
            private bool isTopdownEnabled = false;
            private Jet myJet;

            public AirToGround(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                missileBays = jet._bays;
                baySelected = new bool[missileBays.Count];
                LoadTopdownState();
                name = "Air To Ground";
            }

            private void LoadTopdownState()
            {
                string value = SystemManager.GetCustomDataValue("Topdown");
                isTopdownEnabled = value == "true";
            }

            // SAFETY: Ensure baySelected array matches missileBays count
            private void EnsureBayArraySynced()
            {
                if (baySelected == null || baySelected.Length != missileBays.Count)
                {
                    var oldArray = baySelected;
                    baySelected = new bool[missileBays.Count];

                    if (oldArray != null)
                    {
                        int copyLength = Math.Min(oldArray.Length, baySelected.Length);
                        for (int i = 0; i < copyLength; i++)
                        {
                            baySelected[i] = oldArray[i];
                        }
                    }
                }
            }

            public override string[] GetOptions()
            {
                EnsureBayArraySynced();

                var options = new List<string>
                {
                    "Fire Selected Bays",
                    "Toggle Selected Bays",
                    "Bombardment",
                    string.Format("Topdown [{0}]", isTopdownEnabled ? "ON" : "OFF"),
                    "PreSelect"
                };
                for (int i = 0; i < missileBays.Count; i++)
                {
                    string baySymbol = (i < baySelected.Length) ? (baySelected[i] ? "[X]" : "[ ]") : "[ ]";
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
                if (index == 3)
                {
                    ToggleTopdownMode();
                }
                else if (index == 0)
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
                    ExecuteBombardment();
                    TransferCacheToSlots();
                }
                else if (index == 4)
                {
                    FireSelectedBays();
                }
                else if (index > 4 && index - 5 < missileBays.Count)
                {
                    ToggleBaySelection(index - 5);
                }
            }

            private void ToggleTopdownMode()
            {
                isTopdownEnabled = !isTopdownEnabled;
                SystemManager.SetCustomDataValue("Topdown", isTopdownEnabled ? "true" : "false");
            }

            private void FireSelectedBays()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
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

            private void ExecuteBombardment()
            {
                var selected = myJet.GetSelectedEnemy();
                if (!selected.HasValue)
                {
                    // Fallback: try GPS from cache
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
                    if (!double.TryParse(parts[2], out x)
                        || !double.TryParse(parts[3], out y)
                        || !double.TryParse(parts[4], out z))
                    {
                        return;
                    }
                    var centralTarget = new Vector3D(x, y, z);
                    ExecuteBombardmentAtTarget(centralTarget);
                    return;
                }

                ExecuteBombardmentAtTarget(selected.Value.Position);
            }

            private void ExecuteBombardmentAtTarget(Vector3D centralTarget)
            {
                var bombardmentTargets = CalculateTargetPositions(centralTarget);
                int targetIndex = 0;
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i] && targetIndex < bombardmentTargets.Count)
                    {
                        var targetPosition = bombardmentTargets[targetIndex];
                        FireMissileFromBayWithGps(i, targetPosition);
                        targetIndex++;
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
                        // Try selected enemy first
                        var selected = myJet.GetSelectedEnemy();
                        if (selected.HasValue)
                        {
                            targetPosition = selected.Value.Position;
                        }
                        else
                        {
                            // Fallback: read from cache
                            string cachedValue = SystemManager.GetCustomDataValue("Cached");
                            if (string.IsNullOrEmpty(cachedValue) || !cachedValue.StartsWith("GPS:"))
                            {
                                ParentProgram.Echo("No cached GPS data for missile fire");
                                return;
                            }
                            var parts = cachedValue.Split(':');
                            if (parts.Length < 5)
                            {
                                ParentProgram.Echo("Invalid GPS data format");
                                return;
                            }
                            double x, y, z;
                            if (!double.TryParse(parts[2], out x)
                                || !double.TryParse(parts[3], out y)
                                || !double.TryParse(parts[4], out z))
                            {
                                return;
                            }
                            targetPosition = new Vector3D(x, y, z);
                        }
                    }
                    string gpsData = string.Format(
                        "GPS:Target:{0}:{1}:{2}:#FF75C9F1:",
                        targetPosition.X,
                        targetPosition.Y,
                        targetPosition.Z
                    );
                    // Write bay-specific GPS via cache
                    string cacheKey = string.Format("Cache{0}", bayIndex);
                    SystemManager.SetCustomDataValue(cacheKey, gpsData);
                    bay.ApplyAction("Fire");
                }
                catch (Exception e)
                {
                    ParentProgram.Echo($"FireMissile error: {e.Message}");
                }
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

            private List<Vector3D> CalculateTargetPositions(Vector3D centralTarget)
            {
                var targets = new List<Vector3D>();
                int selectedBayCount = 0;
                for (int i = 0; i < baySelected.Length; i++)
                {
                    if (baySelected[i]) selectedBayCount++;
                }

                if (selectedBayCount == 0)
                {
                    return targets;
                }

                Vector3D[] directions = new Vector3D[]
                {
                    new Vector3D(1, 0, 0),
                    new Vector3D(-1, 0, 0),
                    new Vector3D(0, 0, 1),
                    new Vector3D(0, 0, -1)
                };

                double spacing = 4.0;
                int directionsCount = directions.Length;
                int targetsPerDirection = selectedBayCount / directionsCount;
                int remainder = selectedBayCount % directionsCount;

                for (int d = 0; d < directionsCount; d++)
                {
                    int count = targetsPerDirection + (d < remainder ? 1 : 0);
                    for (int i = 1; i <= count; i++)
                    {
                        Vector3D offset = directions[d] * (spacing * i);
                        targets.Add(centralTarget + offset);
                    }
                }

                return targets;
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

            public override string GetHotkeys()
            {
                return "5: Fire Next Available Bay\n6: Fire Selected Bays\n7: Toggle Selected Bays\n";
            }
        }
    }
}
