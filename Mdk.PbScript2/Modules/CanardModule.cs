using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class CanardModule : ProgramModule
        {
            Jet jet;
            IMyTerminalBlock canardL, canardR;
            bool active = false;
            float manualDeflection = 0f;
            bool manualMode = false;
            float gain = 1.5f;
            float coupling = 0.4f;
            float lastCmdL = 0f;
            float lastCmdR = 0f;
            float lastStabCmd = 0f;
            float lastBeta = 0f;

            const string CANARD_L = "Canard L [Ani]";
            const string CANARD_R = "Canard R [Ani]";

            public CanardModule(Program program, Jet jet) : base(program)
            {
                this.jet = jet;
                name = "Canards";
                FindCanards(program.GridTerminalSystem);
            }

            void FindCanards(IMyGridTerminalSystem grid)
            {
                canardL = grid.GetBlockWithName(CANARD_L);
                canardR = grid.GetBlockWithName(CANARD_R);
            }

            string BlockInfo(IMyTerminalBlock b)
            {
                if (b == null) return "(missing)";
                return string.Format("{0} [{1:F1}]", b.CustomName, b.GetValueFloat(TRIM));
            }

            public override string[] GetOptions()
            {
                string status = !HasCanards() ? "NO BLOCKS"
                    : !active ? "OFF"
                    : manualMode ? "MANUAL"
                    : "AUTO";

                float curL = canardL != null ? canardL.GetValueFloat(TRIM) : 0f;
                float curR = canardR != null ? canardR.GetValueFloat(TRIM) : 0f;

                return new string[]
                {
                    string.Format("Canards [{0}]", status),
                    "Mode: Auto (AoA->0)",
                    "Mode: Manual",
                    string.Format("Manual Defl [{0:F0}]", manualDeflection),
                    string.Format("Gain+ [{0:F1}]", gain),
                    "Gain-",
                    string.Format("Coupling+ [{0:F2}]", coupling),
                    "Coupling-",
                    "Rescan Blocks",
                    string.Format("L: {0}", BlockInfo(canardL)),
                    string.Format("R: {0}", BlockInfo(canardR)),
                    "--- TRIM ---",
                    string.Format("Cmd L:{0:F1} R:{1:F1}  Cur L:{2:F1} R:{3:F1}", lastCmdL, lastCmdR, curL, curR),
                    string.Format("Stab Cmd: {0:F1}  Spill: {1}", lastStabCmd, OwnsStabs ? "YES" : "no"),
                    string.Format("Beta: {0:F1}  Pilot Trim: {1}", lastBeta, jet.offset),
                    "Back to Main Menu"
                };
            }

            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0:
                        if (HasCanards())
                        {
                            active = !active;
                            if (!active)
                            {
                                SetCanards(0f, 0f);
                                if (OwnsStabs)
                                {
                                    SetStabs(jet.offset);
                                    OwnsStabs = false;
                                }
                            }
                        }
                        break;
                    case 1: manualMode = false; break;
                    case 2: manualMode = true; break;
                    case 3:
                        manualDeflection += 5f;
                        if (manualDeflection > 45f) manualDeflection = -45f;
                        break;
                    case 4: gain = Mn(gain + 0.5f, 5f); break;
                    case 5: gain = Mx(gain - 0.5f, 0.5f); break;
                    case 6: coupling = Mn(coupling + 0.05f, 1f); break;
                    case 7: coupling = Mx(coupling - 0.05f, 0f); break;
                    case 8: FindCanards(ParentProgram.GridTerminalSystem); break;
                    case 15: SystemManager.ReturnToMainMenu(); break;
                }
            }

            public override bool HandleNavigation(bool isUp)
            {
                if (manualMode && active)
                {
                    manualDeflection += isUp ? 1f : -1f;
                    manualDeflection = Cl(manualDeflection, -45f, 45f);
                    return true;
                }
                return false;
            }

            // True while this module is actively commanding the stabs
            internal static bool OwnsStabs { get; private set; }

            float ComputeBeta()
            {
                var cockpit = jet._cockpit;
                if (cockpit == null) return 0f;
                Vector3D vel = LV(cockpit);
                if (vel.LengthSquared() < 1.0) return 0f;
                Vector3D velDir = VN(vel);
                double sinBeta = VD(velDir, WR(cockpit));
                return (float)(As(Cl(sinBeta, -1, 1)) * (180.0 / PI));
            }

            public override void Tick()
            {
                if (!active || !HasCanards())
                {
                    if (OwnsStabs)
                    {
                        SetStabs(jet.offset);
                        OwnsStabs = false;
                    }
                    return;
                }

                float desiredL, desiredR;
                if (manualMode)
                {
                    desiredL = manualDeflection;
                    desiredR = manualDeflection;
                }
                else
                {
                    float aoa = (float)SystemManager.GetSmoothedAoA();
                    float beta = ComputeBeta();
                    lastBeta = beta;

                    float aoaL = aoa + coupling * beta;
                    float aoaR = aoa - coupling * beta;

                    desiredL = -gain * aoaL;
                    desiredR = -gain * aoaR;
                }

                float deflL = Cl(desiredL, -45f, 45f);
                float deflR = Cl(desiredR, -45f, 45f);
                SetCanards(deflL, deflR);
                lastCmdL = deflL;
                lastCmdR = deflR;

                // Spillover: average of both sides' excess into stabs
                float spillL = desiredL - deflL;
                float spillR = desiredR - deflR;
                float spillover = (spillL + spillR) * 0.5f;
                if (Ab(spillover) > 0.1f)
                {
                    lastStabCmd = jet.offset + spillover;
                    SetStabs(lastStabCmd);
                    OwnsStabs = true;
                }
                else if (OwnsStabs)
                {
                    lastStabCmd = jet.offset;
                    SetStabs(lastStabCmd);
                    OwnsStabs = false;
                }
            }

            bool HasCanards()
            {
                return canardL != null && canardR != null;
            }

            void SetCanards(float degreesL, float degreesR)
            {
                SetTrim(canardL, -degreesL);
                SetTrim(canardR, degreesR);
            }

            void SetStabs(float baseOffset)
            {
                foreach (var s in jet.rightstab)
                {
                    float cur = s.GetValueFloat(TRIM);
                    if (Ab(cur - baseOffset) > 0.1f)
                        s.SetValue<float>(TRIM, baseOffset);
                }
                foreach (var s in jet.leftstab)
                {
                    float cur = s.GetValueFloat(TRIM);
                    float target = -baseOffset;
                    if (Ab(cur - target) > 0.1f)
                        s.SetValue<float>(TRIM, target);
                }
            }

            static void SetTrim(IMyTerminalBlock block, float target)
            {
                if (block == null || !block.IsFunctional) return;
                float current = block.GetValueFloat(TRIM);
                if (Ab(current - target) > 0.1f)
                    block.SetValue<float>(TRIM, target);
            }
        }
    }
}
