using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
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
            private Jet myJet;

            public AirtoAir(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                missileBays = jet._bays;
                baySelected = new bool[missileBays.Count];
                name = "Air To Air";
            }

            public override string[] GetOptions()
            {
                var options = new List<string>
                {
                    "Fire Selected Bays",
                    "Toggle Selected Bays",
                    string.Format("Seeker [{0}]", isAirtoAirenabled ? "ON" : "OFF")
                };

                MissileBayHelper.BuildBayOptionList(options, missileBays, baySelected);
                return options.ToArray();
            }

            public override void ExecuteOption(int index)
            {
                if (index == 0)
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
                    ToggleAirtoAirMode();
                }
                else
                {
                    int bayOffset = 3;
                    if (index >= bayOffset && index - bayOffset < missileBays.Count)
                    {
                        MissileBayHelper.ToggleBaySelection(baySelected, index - bayOffset);
                    }
                }
            }

            private void ToggleAirtoAirMode()
            {
                isAirtoAirenabled = !isAirtoAirenabled;
                UpdateTopdownCustomData();
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

                // ===== SEEKER ON: weapon tones based on centralized radar lock =====
                bool hasLock = myJet.radarControl != null && myJet.radarControl.IsTrackLocked;

                if (hasLock)
                {
                    SoundManager.RequestWeapon("AIM9Lock", SoundManager.PRIORITY_LOCK, 300);
                }
                else
                {
                    SoundManager.RequestWeapon("AIM9Search", SoundManager.PRIORITY_SEARCH, 300);
                }
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
