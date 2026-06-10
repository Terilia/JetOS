using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Hold-C radial selector on the HUD glass (HL:Alyx style). Hold C, flick the
        // mouse toward a slice, release to toggle it. A tap with no flick keeps the
        // old C behavior (manualfire toggle). While open, SystemManager.HudCfg hides
        // the optional HUD elements to clear room.
        static class RadialMenu
        {
            public static bool Active;
            static float vx, vy;     // accumulated mouse flick (screen coords, +y down)
            static int sel = -1;
            const float DEAD = 8f;   // accumulation needed before a slice arms
            const float RANGE = 45f; // flick vector cap
            const int N = 5;         // slices: 0 GEAR (top), then clockwise
            const float SECT = 360f / N;

            public static void Tick(Program p, IMyCockpit cp, Jet jet)
            {
                bool held = cp != null && cp.IsUnderControl && cp.MoveIndicator.Y < -0.5f;
                if (held && !Active) { vx = 0; vy = 0; sel = -1; }
                if (!held && Active)
                {
                    if (sel >= 0) Execute(p, sel);
                    else jet.manualfire = !jet.manualfire; // tap = legacy C behavior
                }
                Active = held;
                if (!held) return;

                Vector2 ri = cp.RotationIndicator;
                vx += ri.Y;
                vy += ri.X;
                float m = (float)Math.Sqrt(vx * vx + vy * vy);
                if (m > RANGE) { vx *= RANGE / m; vy *= RANGE / m; }
                sel = m < DEAD ? -1
                    : (int)(((ToDeg(At2(vy, vx)) + 90.0 + SECT / 2 + 720.0) % 360.0) / SECT) % N;
            }

            static void Execute(Program p, int i)
            {
                switch (i)
                {
                    case 0: ReverseGroup(p, "Gear"); break;
                    case 1: SystemManager.ToggleConfig(CFG_GUN_AUTO); break;
                    case 2: ToggleLights(p); break;
                    case 3: SoundManager.WarnMute = !SoundManager.WarnMute; break;
                    default: SystemManager.ToggleConfig(CFG_CANARD_AUTO); break;
                }
            }

            // Gear = rotors/hinges — extend/retract is the "Reverse" action, not on/off.
            static void ReverseGroup(Program p, string name)
            {
                var g = p.GridTerminalSystem.GetBlockGroupWithName(name);
                if (g == null) return;
                var bl = new List<IMyTerminalBlock>();
                g.GetBlocks(bl);
                for (int k = 0; k < bl.Count; k++)
                    try { bl[k].ApplyAction("Reverse"); } catch { }
            }

            static void ToggleLights(Program p)
            {
                var bl = new List<IMyFunctionalBlock>();
                p.GridTerminalSystem.GetBlocksOfType(bl,
                    b => b.CustomName.Contains("CLight") && b.IsSameConstructAs(p.Me));
                if (bl.Count == 0) return;
                bool on = !bl[0].Enabled;
                for (int k = 0; k < bl.Count; k++) bl[k].Enabled = on;
            }

            // Labels say what releasing C will DO, not the current state.
            static string Act(bool on) => on ? "TURN OFF" : "TURN ON";

            static string Label(int i)
            {
                switch (i)
                {
                    case 0: return "CYCLE\nGEAR";
                    case 1: return "GUN TRACK\n" + Act(SystemManager.GetConfigValue(CFG_GUN_AUTO) > 0.5f);
                    case 2: return "TOGGLE\nLIGHTS";
                    case 3: return "WARN MUTE\n" + Act(SoundManager.WarnMute);
                    default: return "CANARDS\n" + Act(SystemManager.GetConfigValue(CFG_CANARD_AUTO) > 0.5f);
                }
            }

            // True pie fill: a fan of thin triangles tiling the slice exactly — each spans
            // SECT/SEG degrees with its apex at the hub, so together they cover the sector
            // area out to the ring (chord edge ~2% inside the arc, corners just touching it).
            static void DrawSlice(Vector2 c, float centerRad, float rad)
            {
                const int SEG = 3;
                float step = ToRad(SECT) / SEG;
                float w = 2f * rad * (float)Math.Tan(step / 2f);
                Color f = Cr(HUDModule.HUD_PRIMARY, 0.18f);
                for (int k = 0; k < SEG; k++)
                {
                    float a = centerRad + step * (k - (SEG - 1) / 2f);
                    Vector2 d = V2((float)Cs(a), (float)Sn(a));
                    SqT(TEXTURE_TRIANGLE, c.X + d.X * rad / 2f, c.Y + d.Y * rad / 2f,
                        w, rad, f, (float)At2(-d.X, d.Y));
                    if (k > 0)
                    {
                        // thin square laid along the seam to the previous segment — the
                        // tiled triangles leave a hairline gap there
                        float sa = a - step / 2f;
                        Vector2 sd = V2((float)Cs(sa), (float)Sn(sa));
                        Sq(c.X + sd.X * rad / 2f, c.Y + sd.Y * rad / 2f,
                            4f, rad * 0.97f, f, (float)At2(-sd.X, sd.Y));
                    }
                }
            }

            public static void Draw(Vector2 c, float minDim)
            {
                if (!Active) return;
                float r = minDim * 0.30f;
                Color dim = Cr(HUDModule.HUD_SECONDARY, 0.9f);
                SqT(TEX_RANGE_RING, c.X, c.Y, r * 2.2f, r * 2.2f, dim);

                for (int i = 0; i < N; i++)
                {
                    // boundary spoke
                    double b = ToRad(-90f + SECT * (i + 0.5f));
                    Vector2 db = V2((float)Cs(b), (float)Sn(b));
                    SpriteHelpers.AddLineSprite(c + db * (r * 0.15f), c + db * (r * 1.05f), 1.5f, dim);

                    double a = ToRad(-90f + SECT * i);
                    Vector2 d = V2((float)Cs(a), (float)Sn(a));
                    bool hot = sel == i;
                    if (hot) DrawSlice(c, (float)a, r * 1.04f);
                    float s = hot ? 0.6f : 0.45f;
                    string l = Label(i);
                    Tx(l, c.X + d.X * r * 0.68f,
                        c.Y + d.Y * r * 0.68f - (l.IndexOf('\n') >= 0 ? 17f : 9f) * s, s,
                        hot ? HUDModule.HUD_EMPHASIS : HUDModule.HUD_PRIMARY, MFDTheme.AC, null);
                }

                // flick pointer
                Sq(c.X + vx / RANGE * r * 0.55f, c.Y + vy / RANGE * r * 0.55f, 6f, 6f, HUDModule.HUD_EMPHASIS);
            }
        }
    }
}
