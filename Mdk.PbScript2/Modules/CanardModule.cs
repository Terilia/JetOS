using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.GUI.TextPanel;
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
            float gain = 1.5f;
            const float coupling = 0.4f;
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

            internal bool HasCanards()
            {
                return canardL != null && canardR != null;
            }

            internal string StatusText
            {
                get { return !HasCanards() ? "NO BLOCKS" : active ? "AUTO" : "OFF"; }
            }

            internal float Gain { get { return gain; } }
            internal float DisplayCmdL { get { return active && HasCanards() ? lastCmdL : 0f; } }
            internal float DisplayCmdR { get { return active && HasCanards() ? lastCmdR : 0f; } }
            internal float DisplayBeta { get { return active && HasCanards() ? lastBeta : 0f; } }
            internal bool SpillActive { get { return active && OwnsStabs; } }
            internal float StabCmd { get { return lastStabCmd; } }

            public override MfdPage GetPage() => new CanardsMfdPage(this);

            public override string[] GetOptions()
            {
                return new string[]
                {
                    string.Format("Canards [{0}]", StatusText),
                    string.Format("Gain- [{0:F1}]", gain),
                    string.Format("Gain+ [{0:F1}]", gain)
                };
            }

            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0:
                        if (!HasCanards()) FindCanards(ParentProgram.GridTerminalSystem);
                        if (HasCanards())
                        {
                            active = !active;
                            if (!active)
                            {
                                SetCanards(0f, 0f);
                                lastCmdL = 0f;
                                lastCmdR = 0f;
                                if (OwnsStabs)
                                {
                                    SetStabs(jet.offset);
                                    OwnsStabs = false;
                                }
                            }
                        }
                        break;
                    case 1:
                        gain = Mx(gain - 0.5f, 0.5f);
                        break;
                    case 2:
                        gain = Mn(gain + 0.5f, 5f);
                        break;
                }
            }

            public override string GetHotkeys()
            {
                return "CANARD CONTROL";
            }

            // True while this module is actively commanding the stabs.
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
                    lastBeta = 0f;
                    if (OwnsStabs)
                    {
                        SetStabs(jet.offset);
                        OwnsStabs = false;
                    }
                    return;
                }

                float aoa = (float)SystemManager.GetSmoothedAoA();
                float beta = ComputeBeta();
                lastBeta = beta;

                float aoaL = aoa + coupling * beta;
                float aoaR = aoa - coupling * beta;
                float desiredL = -gain * aoaL;
                float desiredR = -gain * aoaR;
                float deflL = Cl(desiredL, -45f, 45f);
                float deflR = Cl(desiredR, -45f, 45f);

                SetCanards(deflL, deflR);
                lastCmdL = deflL;
                lastCmdR = deflR;

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

        class CanardsMfdPage : MenuMfdPage
        {
            readonly CanardModule _module;

            public CanardsMfdPage(CanardModule module) : base(module)
            {
                _module = module;
            }

            public override void RenderMenuSupplement(MySpriteDrawFrame frame, RectangleF menuArea,
                Vector2 surfaceSize, int selectedIndex)
            {
                float rowH = surfaceSize.Y * 0.079f * 0.5f;
                float top = menuArea.Position.Y + rowH * 3f + surfaceSize.Y * 0.018f;
                float h = menuArea.Position.Y + menuArea.Height - top;
                if (h < 90f) return;

                float x = menuArea.Position.X;
                float w = menuArea.Width;
                DrawCanardOverlay(frame, x, top, w, h);
            }

            void DrawCanardOverlay(MySpriteDrawFrame f, float x, float y, float w, float h)
            {
                SpriteHelpers.Bx(f, x + w / 2f, y + h / 2f, w, h, Cr(4, 8, 4));
                SpriteHelpers.DrawRectangleOutline(f, x, y, w, h, 1f, MFDTheme.BORDER);
                SpriteHelpers.Bx(f, x + w / 2f, y, w, 1f, MFDTheme.GOLD_LINE);

                SpriteHelpers.Tt(f, "CANARD TILT", x + 8f, y + 8f, 0.46f, MFDTheme.CORP_GOLD, MFDTheme.AL);
                SpriteHelpers.Tt(f, "NEUTRAL + L/R", x + w - 8f, y + 10f, 0.32f, MFDTheme.DIM_TEXT, MFDTheme.AR);

                float cx = x + w * 0.42f;
                float cy = y + h * 0.49f;
                float len = Mn(w * 0.64f, h * 0.78f);
                float bladeLen = len * 0.84f;
                float bladeH = Mx(8f, Mn(16f, h * 0.070f));

                DrawAngleGuide(f, cx, cy, len, -45f, Cr(MFDTheme.BORDER_LIGHT, 0.75f));
                DrawAngleGuide(f, cx, cy, len, 45f, Cr(MFDTheme.BORDER_LIGHT, 0.75f));
                SpriteHelpers.Bx(f, cx, cy, len, 1f, Cr(MFDTheme.DIM_TEXT, 0.85f));
                SpriteHelpers.Bx(f, cx, cy, 1f, h * 0.52f, Cr(MFDTheme.GOLD_LINE, 0.65f));

                DrawCenteredBlade(f, cx, cy, bladeLen, bladeH * 0.55f, 0f, Cr(MFDTheme.DIM_TEXT, 0.42f));
                DrawCenteredBlade(f, cx, cy, bladeLen, bladeH, _module.DisplayCmdL, Cr(110, 205, 110, 178));
                DrawCenteredBlade(f, cx, cy, bladeLen, bladeH, _module.DisplayCmdR, Cr(190, 164, 82, 166));
                DrawBladeLabel(f, cx, cy, bladeLen, _module.DisplayCmdL, "L", Cr(110, 205, 110), -1f);
                DrawBladeLabel(f, cx, cy, bladeLen, _module.DisplayCmdR, "R", Cr(190, 164, 82), 1f);
                SpriteHelpers.Sp(f, TEXTURE_CIRCLE, cx, cy, 18f, 18f, MFDTheme.CORP_GOLD);
                SpriteHelpers.Sp(f, TEXTURE_CIRCLE_SOLID, cx, cy, 6f, 6f, MFDTheme.BRIGHT_TEXT);

                SpriteHelpers.Tt(f, "+45", x + 8f, y + h * 0.20f, 0.34f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(f, "0", x + 8f, cy + 2f, 0.34f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(f, "-45", x + 8f, y + h * 0.76f, 0.34f, MFDTheme.DIM_TEXT, MFDTheme.AL);

                float rx = x + w - 8f;
                float my = y + 38f;
                DrawMetric(f, rx, my, "L", _module.DisplayCmdL.ToString("+0.0;-0.0;0.0"), Cr(110, 205, 110));
                DrawMetric(f, rx, my + 28f, "R", _module.DisplayCmdR.ToString("+0.0;-0.0;0.0"), Cr(190, 164, 82));
                DrawMetric(f, rx, my + 56f, "BETA", _module.DisplayBeta.ToString("+0.0;-0.0;0.0"), MFDTheme.STATUS_VAL);

                string spill = _module.SpillActive ? "SPILL " + _module.StabCmd.ToString("+0.0;-0.0;0.0") : "SPILL no";
                SpriteHelpers.Tt(f, spill, x + 8f, y + h - 20f, 0.34f,
                    _module.SpillActive ? MFDTheme.WARN : MFDTheme.DIM_TEXT, MFDTheme.AL);
            }

            static void DrawMetric(MySpriteDrawFrame f, float right, float y, string label, string value, Color c)
            {
                SpriteHelpers.Tt(f, label, right - 72f, y, 0.30f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                SpriteHelpers.Tt(f, value, right, y + 1f, 0.38f, c, MFDTheme.AR);
            }

            static void DrawAngleGuide(MySpriteDrawFrame f, float cx, float cy, float len, float deg, Color c)
            {
                float r = ToRad(deg);
                Vector2 d = V2((float)Cs(r), (float)Sn(r));
                SpriteHelpers.AddLineSprite(f, V2(cx, cy) - d * (len * 0.5f), V2(cx, cy) + d * (len * 0.5f), 1f, c);
            }

            static void DrawCenteredBlade(MySpriteDrawFrame f, float cx, float cy, float len, float h, float deg, Color c)
            {
                float rot = -ToRad(Cl(deg, -45f, 45f));
                SpriteHelpers.Bx(f, cx, cy, len, h, c, rot);
                SpriteHelpers.Bx(f, cx, cy, 7f, h + 7f, Cr(c, 0.55f), rot);
            }

            static void DrawBladeLabel(MySpriteDrawFrame f, float cx, float cy, float len, float deg, string label, Color c, float yOffsetSign)
            {
                float rot = -ToRad(Cl(deg, -45f, 45f));
                float px = cx + (float)Cs(rot) * (len * 0.52f);
                float py = cy + (float)Sn(rot) * (len * 0.52f) + yOffsetSign * 8f;
                SpriteHelpers.Tt(f, label, px, py - 8f, 0.38f, c, MFDTheme.AC);
            }
        }
    }
}
