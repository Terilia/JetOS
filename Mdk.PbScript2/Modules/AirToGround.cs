using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
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
            public AirToGround(Program program, Jet jet) : base(program)
            {
                missileBays = jet._bays;
                baySelected = new bool[missileBays.Count];
                LoadTopdownState();
                name = "Air To Ground";
            }
            private void LoadTopdownState()
            {
                var customDataLines = ParentProgram.Me.CustomData.Split(
                    new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (var line in customDataLines)
                {
                    if (line.StartsWith("Topdown:"))
                    {
                        isTopdownEnabled = line.EndsWith("true");
                        break;
                    }
                }
            }

            // SAFETY: Ensure baySelected array matches missileBays count
            private void EnsureBayArraySynced()
            {
                if (baySelected == null || baySelected.Length != missileBays.Count)
                {
                    var oldArray = baySelected;
                    baySelected = new bool[missileBays.Count];

                    // Preserve old selections if possible
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
                EnsureBayArraySynced(); // Safety check

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
                UpdateTopdownCustomData();
            }
            private void UpdateTopdownCustomData()
            {
                var customDataLines = ParentProgram.Me.CustomData.Split(
                    new[] { '\n' },
                    StringSplitOptions.None
                );
                bool found = false;
                for (int i = 0; i < customDataLines.Length; i++)
                {
                    if (customDataLines[i].StartsWith("Topdown:"))
                    {
                        customDataLines[i] = "Topdown:" + (isTopdownEnabled ? "true" : "false");
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var lines = new List<string>(customDataLines);
                    lines.Add("Topdown:" + (isTopdownEnabled ? "true" : "false"));
                    customDataLines = lines.ToArray();
                }
                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
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
                            // Continue to next bay instead of crashing
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
                        if (bay == null)
                        {
                            continue;
                        }
                        bool isOn = bay.Enabled;
                        if (isOn)
                        {
                            bay.Enabled = false;
                        }
                        else
                        {
                            bay.Enabled = true;
                        }
                    }
                }
            }
            private void ExecuteBombardment()
            {
                var cachedData = ParentProgram.Me.CustomData
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(line => line.StartsWith("Cached:GPS:"));
                if (cachedData == null)
                {
                    return;
                }
                var parts = cachedData.Split(':');
                if (parts.Length < 6)
                {
                    return;
                }
                double x,
                    y,
                    z;
                if (
                    !double.TryParse(parts[3], out x)
                    || !double.TryParse(parts[4], out y)
                    || !double.TryParse(parts[5], out z)
                )
                {
                    return;
                }
                var centralTarget = new Vector3D(x, y, z);
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
                        var customDataLines = ParentProgram.Me.CustomData.Split(
                            new[] { '\n' },
                            StringSplitOptions.RemoveEmptyEntries
                        );
                        var cachedData = customDataLines.FirstOrDefault(
                            line => line.StartsWith("Cached:GPS:")
                        );

                        if (cachedData == null)
                        {
                            ParentProgram.Echo("No cached GPS data for missile fire");
                            return;
                        }

                        var parts = cachedData.Split(':');
                        if (parts.Length < 6)
                        {
                            ParentProgram.Echo("Invalid GPS data format");
                            return;
                        }

                        double x,
                            y,
                            z;
                        if (
                            !double.TryParse(parts[3], out x)
                            || !double.TryParse(parts[4], out y)
                            || !double.TryParse(parts[5], out z)
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
                var customDataLines = ParentProgram.Me.CustomData
                    .Split(new[] { '\n' }, StringSplitOptions.None)
                    .ToList();
                string cacheLabel = string.Format("Cache{0}:", bayIndex);
                int cacheIndex = customDataLines.FindIndex(line => line.StartsWith(cacheLabel));
                if (cacheIndex != -1)
                {
                    customDataLines[cacheIndex] = string.Format("{0}{1}", cacheLabel, gpsData);
                }
                else
                {
                    customDataLines.Add(string.Format("{0}{1}", cacheLabel, gpsData));
                }
                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                SystemManager.MarkCustomDataDirty();
            }
            private void TransferCacheToSlots()
            {
                try
                {
                    var customDataLines = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.None)
                        .ToList();
                    for (int i = 0; i < customDataLines.Count; i++)
                    {
                        string cacheLabel = string.Format("Cache{0}:", i);
                        int cacheIndex = customDataLines.FindIndex(
                            line => line.StartsWith(cacheLabel)
                        );
                        if (cacheIndex != -1)
                        {
                            var cacheLine = customDataLines[cacheIndex];
                            var cacheContent = cacheLine.Substring(cacheLabel.Length).Trim();
                            if (!string.IsNullOrEmpty(cacheContent))
                            {
                                string targetLabel = string.Format("{0}:", i);
                                int targetIndex = customDataLines.FindIndex(
                                    line => line.StartsWith(targetLabel)
                                );
                                if (targetIndex != -1)
                                {
                                    customDataLines[targetIndex] = string.Format(
                                        "{0} {1}",
                                        targetLabel,
                                        cacheContent
                                    );
                                }
                                else
                                {
                                    customDataLines.Add(
                                        string.Format("{0} {1}", targetLabel, cacheContent)
                                    );
                                }
                                customDataLines[cacheIndex] = cacheLabel;
                            }
                        }
                    }
                    ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                }
                catch (Exception e)
                {
                    ParentProgram.Echo($"TransferCacheToSlots error: {e.Message}");
                }
            }
            private List<Vector3D> CalculateTargetPositions(Vector3D centralTarget)
            {
                var targets = new List<Vector3D>();
                int selectedBayCount = baySelected.Count(b => b);

                if (selectedBayCount == 0)
                {
                    return targets;
                }

                // Define the directions for the cross: +X, -X, +Z, -Z
                Vector3D[] directions = new Vector3D[]
                {
                    new Vector3D(1, 0, 0), // +X
                    new Vector3D(-1, 0, 0), // -X
                    new Vector3D(0, 0, 1), // +Z
                    new Vector3D(0, 0, -1) // -Z
                };

                double spacing = 4.0; // Distance between each target along the axis

                // Calculate how many targets per direction
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
