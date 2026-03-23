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
                        int copyLength = Mn(oldArray.Length, baySelected.Length);
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
                MissileBayHelper.BuildBayOptionList(options, missileBays, baySelected);
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
                    MissileBayHelper.FireSelectedBays(missileBays, baySelected, ParentProgram);
                    MissileBayHelper.TransferCacheToSlots(missileBays.Count);
                }
                else if (index == 1)
                {
                    MissileBayHelper.ToggleSelectedBays(missileBays, baySelected);
                }
                else if (index == 2)
                {
                    ExecuteBombardment();
                    MissileBayHelper.TransferCacheToSlots(missileBays.Count);
                }
                else if (index == 4)
                {
                    MissileBayHelper.FireSelectedBays(missileBays, baySelected, ParentProgram);
                }
                else if (index > 4 && index - 5 < missileBays.Count)
                {
                    MissileBayHelper.ToggleBaySelection(baySelected, index - 5);
                }
            }

            private void ToggleTopdownMode()
            {
                isTopdownEnabled = !isTopdownEnabled;
                SystemManager.SetCustomDataValue("Topdown", isTopdownEnabled ? "true" : "false");
            }

            private void ExecuteBombardment()
            {
                var selected = myJet.GetSelectedEnemy();
                if (!selected.HasValue)
                {
                    // Fallback: try GPS from cache
                    Vector3D centralTarget;
                    if (!NavigationHelper.TryParseGps(SystemManager.GetCustomDataValue("Cached"), out centralTarget))
                        return;
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
                        MissileBayHelper.FireMissileFromBay(missileBays, i, targetPosition, ParentProgram, myJet);
                        targetIndex++;
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

            public override void HandleSpecialFunction(int key)
            {
                MissileBayHelper.HandleWeaponHotkey(key, missileBays, ParentProgram);
            }

            public override string GetHotkeys()
            {
                return MissileBayHelper.WEAPON_HOTKEYS;
            }
        }
    }
}
