using System;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class StartupSequence
        {
            // 0=no pilot, 1=press key, 2=booting, 3=done
            static int phase = 0;
            static int t = 0;
            static int waitT = 0;
            const int LEN = 720;

            // Smooth ease-in-out
            static float Sm(float x) { x = x < 0f ? 0f : x > 1f ? 1f : x; return x * x * (3f - 2f * x); }

            // Per-screen power-on tick (left=0 for bios, right=220, hud=290, center=360)
            static int PwrOn(int scr) { return scr == 0 ? 0 : scr == 2 ? 220 : scr == 3 ? 290 : 360; }

            static readonly string[] post = {
                "NC TACTICAL BIOS v4.7", "(C) NYINAH CORP", "",
                "CPU: NC-7700K......OK", "MEM: 32768MB.......OK",
                "NET: MILSAT........OK", "RDR: AESA..........OK",
                "WPN: CTRL..........OK", "", "Init JetOS v2.0..."
            };

            public static bool Tick(Jet jet, double vel, string arg,
                IMyTextSurface m0, IMyTextSurface m1, IMyTextSurface m2)
            {
                if (phase == 3) return false;
                if (vel > 2.0) { phase = 3; return false; }

                // Wait for pilot
                if (phase == 0)
                {
                    if (jet._cockpit != null && jet._cockpit.IsUnderControl) phase = 1;
                    else { Dark(m0); Dark(m1); Dark(m2); Dark(jet.hud); return true; }
                }

                // Wait for keypress
                if (phase == 1)
                {
                    if (!string.IsNullOrEmpty(arg)) { phase = 2; }
                    else
                    { waitT++; WaitScr(m0); WaitScr(m1); WaitScr(m2); WaitScr(jet.hud); return true; }
                }

                // Booting
                t++;
                if (t >= LEN) { phase = 3; return false; }
                Panel(m0, t, 0); Panel(m1, t, 1); Panel(m2, t, 2);
                if (jet.hud != null) HudBoot(jet.hud, t);
                return true;
            }

            static void Dark(IMyTextSurface s)
            {
                if (s == null) return;
                s.ContentType = ContentType.SCRIPT;
                s.ScriptBackgroundColor = new Color(0, 0, 0, 0);
                var f = s.DrawFrame();
                Bx(f, s.SurfaceSize.X / 2f, s.SurfaceSize.Y / 2f, s.SurfaceSize.X, s.SurfaceSize.Y, Color.Black);
                f.Dispose();
            }

            static void Bx(MySpriteDrawFrame f, float x, float y, float w, float h, Color c)
            { MFDFrame.Rect(f, x, y, w, h, c); }

            static void Tx(MySpriteDrawFrame f, string d, float x, float y, float s, Color c,
                TextAlignment a = TextAlignment.CENTER)
            { MFDFrame.Txt(f, d, x, y, s, c, a); }

            static void WaitScr(IMyTextSurface s)
            {
                if (s == null) return;
                float w = s.SurfaceSize.X, h = s.SurfaceSize.Y, cx = w / 2f, cy = h / 2f;
                var f = s.DrawFrame();
                Bx(f, cx, cy, w, h, Color.Black);
                // Subtle crawling scanline
                Bx(f, cx, (waitT * 1.1f) % h, w, 1f, new Color(0, 10, 0));
                // Logo breathes
                int a = (int)(170 + 50 * Sn(waitT * 0.04));
                Tx(f, MFDTheme.NC, cx, cy - 22f, 0.85f, new Color(138, 122, 80, a));
                Tx(f, "TACTICAL SYSTEM", cx, cy + 6f, 0.38f, new Color(74, 122, 74, a / 2));
                // Prompt pulses
                Tx(f, "PRESS KEY", cx, cy + 44f, 0.42f,
                    new Color(64, 160, 64, (int)(90 + 110 * Sn(waitT * 0.07))));
                f.Dispose();
            }

            static void HudBoot(IMyTextSurface s, int t)
            {
                int age = t - 290;
                if (age < 0) { var ef = s.DrawFrame(); ef.Dispose(); return; }
                float w = s.SurfaceSize.X, h = s.SurfaceSize.Y, cx = w / 2f, cy = h / 2f;
                var f = s.DrawFrame();
                float p = Sm(age / 100f);
                int a = (int)(180 * p);
                Color c = new Color(0, a, 0, a);
                float l = 28f * p, g = 7f;
                Bx(f, cx - g - l / 2f, cy, l, 1f, c); Bx(f, cx + g + l / 2f, cy, l, 1f, c);
                Bx(f, cx, cy - g - l / 2f, 1f, l, c); Bx(f, cx, cy + g + l / 2f, 1f, l, c);
                if (p > 0.5f)
                    Tx(f, "JetOS [HFPS]", cx, cy + 22f, 0.55f,
                        new Color(64, 160, 64, (int)(255 * Sm((p - 0.5f) * 2f))));
                f.Dispose();
            }

            static void Panel(IMyTextSurface s, int t, int scr)
            {
                if (s == null) return;
                float w = s.SurfaceSize.X, h = s.SurfaceSize.Y, cx = w / 2f, cy = h / 2f;
                int on = PwrOn(scr), age = t - on;
                var f = s.DrawFrame();
                Bx(f, cx, cy, w, h, Color.Black);

                // Before power-on: only left shows BIOS
                if (age < 0) { if (scr == 0) Post(f, w, h, t); f.Dispose(); return; }

                // CRT power-on (green glow expanding)
                if (scr > 0 && age < 30)
                {
                    float p = Sm(age / 30f);
                    float lw = w * Math.Min(p * 2.5f, 1f), lh = Math.Max(1f, h * Math.Max(0f, (p - 0.2f) / 0.8f));
                    Bx(f, cx, cy, lw + 16f, lh + 10f, new Color(0, (int)(12 * p), 0, (int)(25 * p)));
                    Bx(f, cx, cy, lw, Math.Max(lh, 2f), new Color(0, (int)(190 * p), 0, (int)(210 * p)));
                    f.Dispose(); return;
                }

                // Screen age for loading content
                float la = scr == 0 ? Math.Max(0, t - 170) : Math.Max(0, age - 30);
                float load = Sm(Cl(la / 260f, 0f, 1f));  // 0→1 progress
                float chrome = Sm(Cl((t - 530) / 170f, 0f, 1f));  // chrome blend 0→1

                // Background with subtle pulse
                Bx(f, cx, cy, w, h, new Color((int)(5 + 2 * Sn(la * 0.025)), 8, 5));

                // Scrolling scanlines (fade with chrome)
                int sa = (int)(6 * (1f - chrome));
                if (sa > 0)
                    for (int i = 0; i < 20; i++)
                        Bx(f, cx, (i * h / 20f + la * 1.0f) % h, w, 1f, new Color(0, 0, 0, sa));

                // === Loading content (cross-fades out) ===
                float lFade = 1f - Sm(Cl((chrome - 0.15f) / 0.5f, 0f, 1f));
                if (lFade > 0.01f)
                {
                    int ta = (int)(Sm(Cl(la / 25f, 0f, 1f)) * 255 * lFade);
                    Tx(f, MFDTheme.NC, cx, cy - 22f, 0.95f, new Color(138, 122, 80, ta));
                    Tx(f, "TACTICAL SYSTEM", cx, cy + 5f, 0.42f, new Color(74, 122, 74, (int)(ta * 0.6f)));

                    // Progress bar
                    float bw = w * 0.48f, by = cy + 42f, bl = cx - bw / 2f;
                    int ba = (int)(255 * lFade);
                    Bx(f, cx, by, bw + 2f, 7f, new Color(24, 40, 24, ba));
                    Bx(f, cx, by, bw, 5f, new Color(4, 7, 4, ba));
                    float fw = bw * load;
                    if (fw > 1f) Bx(f, bl + fw / 2f, by, fw, 4f, new Color(22, 60, 22, ba));
                    // Sliding highlights
                    for (int i = 0; i < 3; i++)
                    {
                        float bx = ((la * 2.5f + i * 38) % (bw + 36)) - 18f;
                        if (bx > -12f && bx < bw + 12f)
                        {
                            float px = bl + Cl(bx, 0f, bw - 12f) + 6f;
                            float ea = Cl(bx < 0 ? (bx + 12f) / 12f : bx > bw - 12f ? (bw - bx) / 12f : 1f, 0f, 1f);
                            Bx(f, px, by, 12f, 4f, new Color(64, 160, 64, (int)(190 * ea * lFade)));
                        }
                    }
                    Tx(f, $"{(int)(load * 100)}%", cx + bw / 2f + 14f, by - 4f, 0.32f,
                        new Color(64, 160, 64, ba));
                    Tx(f, "Initializing" + new string('.', (int)(la / 8) % 4),
                        cx, by + 14f, 0.28f, new Color(42, 74, 42, ba));
                }

                // === Chrome (fades in) ===
                if (chrome > 0f)
                {
                    // Corners
                    float cl = Math.Min(w, h) * 0.03f * Sm(Cl(chrome / 0.35f, 0f, 1f));
                    float ci = 4f;
                    if (cl > 0.5f)
                        for (int c = 0; c < 4; c++)
                        {
                            float bx = (c & 1) == 0 ? ci : w - ci, by = (c & 2) == 0 ? ci : h - ci;
                            float dx = (c & 1) == 0 ? 1 : -1, dy = (c & 2) == 0 ? 1 : -1;
                            Bx(f, bx + dx * cl / 2f, by, cl, 1f, MFDTheme.CORNER);
                            Bx(f, bx, by + dy * cl / 2f, 1f, cl, MFDTheme.CORNER);
                        }
                    // Borders
                    float bp = Sm(Cl((chrome - 0.12f) / 0.35f, 0f, 1f));
                    if (bp > 0f)
                    {
                        Bx(f, cx, 1f, w * bp, 2f, MFDTheme.BORDER); Bx(f, cx, h - 1f, w * bp, 2f, MFDTheme.BORDER);
                        Bx(f, 1f, cy, 2f, h * bp, MFDTheme.BORDER); Bx(f, w - 1f, cy, 2f, h * bp, MFDTheme.BORDER);
                    }
                    // Header + footer
                    float hp = Sm(Cl((chrome - 0.3f) / 0.4f, 0f, 1f));
                    if (hp > 0f)
                    {
                        int ha = (int)(255 * hp); float hH = h * 0.069f, fH = h * 0.054f;
                        Bx(f, cx, 15f + hH / 2f, w, hH, new Color(10, 18, 10, ha));
                        Bx(f, cx, 15f + hH + 0.5f, w, 1f, new Color(58, 53, 32, ha));
                        Tx(f, MFDTheme.NC, w * 0.019f, 15f + hH * 0.15f, h * 0.00085f,
                            new Color(138, 122, 80, ha), MFDTheme.AL);
                        Bx(f, cx, h - fH / 2f, w, fH, new Color(10, 18, 10, ha));
                        Bx(f, cx, h - fH, w, 1f, new Color(24, 40, 24, ha));
                    }
                    // System online
                    float rp = Sm(Cl((chrome - 0.55f) / 0.45f, 0f, 1f));
                    if (rp > 0f)
                        Tx(f, "SYSTEM ONLINE", cx, cy, 0.6f, new Color(64, 160, 64, (int)(255 * rp)));
                }
                f.Dispose();
            }

            static void Post(MySpriteDrawFrame f, float w, float h, int t)
            {
                int tc = t * 4, cp = 0;
                for (int i = 0; i < post.Length; i++)
                {
                    if (post[i].Length == 0) { cp++; continue; }
                    int lc = Math.Min(tc - cp, post[i].Length); if (lc <= 0) break;
                    Tx(f, post[i].Substring(0, lc), 12f, 15f + i * 19f, 0.45f,
                        i < 2 ? MFDTheme.CORP_GOLD : MFDTheme.ACCENT, MFDTheme.AL);
                    cp += post[i].Length;
                    if (lc < post[i].Length)
                    {
                        Tx(f, "|/-\\"[t / 3 % 4].ToString(), 12f + lc * 7f, 15f + i * 19f, 0.45f,
                            MFDTheme.ACCENT, MFDTheme.AL);
                        break;
                    }
                }
                if (t > 35)
                    Tx(f, $"Memory: {Math.Min((t - 35) * 250, 32768)} KB OK",
                        12f, h - 22f, 0.32f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                // Cross-fade to black
                if (t > 140)
                    Bx(f, w / 2f, h / 2f, w, h, new Color(0, 0, 0, Math.Min(255, (t - 140) * 8)));
            }
        }
    }
}
