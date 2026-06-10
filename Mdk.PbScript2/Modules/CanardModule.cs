using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Canard AoA-damping logic. No longer a menu module — owned + ticked by
        // ConfigurationModule; on/off + gain are config params (CFG_CANARD_AUTO / CFG_CANARD_GAIN).
        class CanardModule
        {
            Jet jet;
            IMyTerminalBlock canardL, canardR;
            const float coupling = 0.4f;

            const string CANARD_L = "Canard L [Ani]";
            const string CANARD_R = "Canard R [Ani]";

            public CanardModule(Program program, Jet jet)
            {
                this.jet = jet;
                FindCanards(program.GridTerminalSystem);
            }

            void FindCanards(IMyGridTerminalSystem grid)
            {
                canardL = grid.GetBlockWithName(CANARD_L);
                canardR = grid.GetBlockWithName(CANARD_R);
            }

            internal bool HasCanards()
            {
                return canardL != null && canardR != null;
            }

            // True while the canard logic is actively commanding the stabs.
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

            public void Tick()
            {
                bool active = SystemManager.GetConfigValue(CFG_CANARD_AUTO) > 0.5f;
                if (!active || !HasCanards())
                {
                    SetCanards(0, 0); // don't leave canards frozen mid-deflection (deadband makes this free)
                    if (OwnsStabs)
                    {
                        SetStabs(jet.offset);
                        OwnsStabs = false;
                    }
                    return;
                }

                float gain = SystemManager.GetConfigValue(CFG_CANARD_GAIN);
                float aoa = (float)SystemManager.GetSmoothedAoA();
                float beta = ComputeBeta();

                float aoaL = aoa + coupling * beta;
                float aoaR = aoa - coupling * beta;
                float desiredL = -gain * aoaL;
                float desiredR = -gain * aoaR;
                float deflL = Cl(desiredL, -45f, 45f);
                float deflR = Cl(desiredR, -45f, 45f);

                SetCanards(deflL, deflR);

                float spillL = desiredL - deflL;
                float spillR = desiredR - deflR;
                float spillover = (spillL + spillR) * 0.5f;
                if (Ab(spillover) > 0.1f)
                {
                    SetStabs(jet.offset + spillover);
                    OwnsStabs = true;
                }
                else if (OwnsStabs)
                {
                    SetStabs(jet.offset);
                    OwnsStabs = false;
                }
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
