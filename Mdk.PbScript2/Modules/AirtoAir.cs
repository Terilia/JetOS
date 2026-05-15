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
            private List<IMyShipMergeBlock> missileBays;
            private bool[] baySelected;
            private bool isTopdownEnabled = false;
            private Jet myJet;

            public AirtoAir(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                missileBays = jet._bays;
                baySelected = new bool[missileBays.Count];
                isTopdownEnabled = SystemManager.GetCustomDataValue(CD_TOPDOWN) == S_TRUE;
                // AntiAir is always on — the protocol expects it and we want live target updates regardless of mode.
                SystemManager.SetCustomDataValue(CD_ANTI_AIR, S_TRUE);
                name = "Weapons";
            }

            private void EnsureBayArraySynced()
            {
                if (baySelected == null || baySelected.Length != missileBays.Count)
                {
                    var oldArray = baySelected;
                    baySelected = new bool[missileBays.Count];
                    if (oldArray != null)
                    {
                        int copyLength = Mn(oldArray.Length, baySelected.Length);
                        for (int i = 0; i < copyLength; i++) baySelected[i] = oldArray[i];
                    }
                }
            }

            public override string[] GetOptions()
            {
                EnsureBayArraySynced();

                var options = new List<string>
                {
                    "Fire Sel",
                    "Toggle Sel",
                    $"TD [{(isTopdownEnabled ? "ON" : "OFF")}]",
                };

                MissileBayHelper.BuildBayOptionList(options, missileBays, baySelected);
                return options.ToArray();
            }

            private const int BayOffset = 3;

            public override void ExecuteOption(int index)
            {
                EnsureBayArraySynced();
                switch (index)
                {
                    case 0:
                        MissileBayHelper.FireSelectedBays(
                            missileBays, baySelected, ParentProgram, myJet, isTopdownEnabled);
                        break;
                    case 1:
                        MissileBayHelper.ToggleSelectedBays(missileBays, baySelected);
                        break;
                    case 2:
                        isTopdownEnabled = !isTopdownEnabled;
                        SystemManager.SetCustomDataValue(CD_TOPDOWN, isTopdownEnabled ? S_TRUE : S_FALSE);
                        break;
                    default:
                        if (index >= BayOffset && index - BayOffset < missileBays.Count)
                            MissileBayHelper.ToggleBaySelection(baySelected, index - BayOffset);
                        break;
                }
            }

            public override void Tick()
            {
                MissileBayHelper.PollMissileStatus(ParentProgram);

                SystemManager.UpdateActiveTargetGPS();

                MissileBayHelper.BroadcastTargetUpdates(ParentProgram, myJet, missileBays);
            }

            public override void HandleSpecialFunction(int key)
            {
                MissileBayHelper.HandleWeaponHotkey(
                    key, missileBays, ParentProgram, myJet, isTopdownEnabled);
            }

            public override string GetHotkeys()
            {
                return MissileBayHelper.WEAPON_HOTKEYS;
            }
        }
    }
}
