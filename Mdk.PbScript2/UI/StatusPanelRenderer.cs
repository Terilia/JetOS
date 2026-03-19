using System;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class StatusPanelRenderer
        {
            // ── LCG random ──
            static uint rngState = 0xDEAD;
            static uint Rng()
            {
                rngState = rngState * 1103515245 + 12345;
                return (rngState >> 16) & 0x7FFF;
            }

            // ── Slideshow controller ──
            const int SLIDE_COUNT = 5;
            static int activeSlide;
            static int slideTick;
            static int transitionTick;
            static bool satOffline;
            static int offlineTick;

            // ── Slide 0: Sat recon state ──
            const int MV = 400, MR = 16, MM = 7;
            static float[] vPX = new float[MV], vPY = new float[MV];
            static int[] rS = new int[MR], rN = new int[MR], rL = new int[MR];
            static int rCnt, vCnt;
            static float[] mX = new float[MM], mY = new float[MM];
            static int[] mT = new int[MM];
            static string[] mNm = new string[MM], mIn = new string[MM], mIn2 = new string[MM];
            static int mCnt;
            static string satName, satCoord;
            static int lastOp = -1, nameIdx;
            static uint mapSeed;
            static float camX, camY, camZoom, camTgtX, camTgtY, camTgtZ;
            static int camState, camMarker, camDwell;

            // ── Slide 1: SIGINT state ──
            static int[] sigW0 = new int[22], sigW1 = new int[22];
            static int sigPhase, sigTimer, sigFrag;

            // ── Slide 2: Countdown state ──
            static int cdSec, cdSub, cdCodeIdx, cdFlash;

            // ── Slide 3: Exfil state ──
            static int exFile, exPhase, exPurge;
            static float[] exProg = new float[4];
            static int[] exFIdx = new int[4]; // file name indices (stored at init)
            static float exSpeed;              // download speed (stored at init)
            static int exTgtIdx;               // target index (stored at init)

            // ── Slide 4: Asset state ──
            static string[] astName = new string[6];
            static int[] astStat = new int[6]; // 0=active,1=compromised,2=dark
            static int astTimer, astTarget;

            // ── String tables ──
            static readonly string[] OPS = { "OP NIGHTFALL", "SECTOR 7-A", "ZONE BRAVO",
                "RECON ALPHA", "AREA 12-C", "OP DARKWATER", "ZONE ECHO", "OP RED COAST",
                "SECTOR 3-F", "RECON DELTA", "OP IRON TIDE", "AREA 9-K" };
            static readonly string[] NAMES = { "ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO",
                "FOXTROT", "GOLF", "HOTEL", "KILO", "LIMA", "OSCAR", "PAPA", "SIERRA", "TANGO" };
            static readonly string[] INTEL_S = { "2x SAM-6", "AA BTY", "3x MLRS", "C2 NODE",
                "RADAR", "HQ+COMMS", "SUPPLY DEP", "FUEL DUMP", "AMMO CACHE", "AIRSTRIP",
                "DOCK", "BUNKER", "MOTOR POOL", "EW SUITE", "2x PATROL" };
            static readonly string[] SIG_FREQ = { "138.200 MHz", "7.415 kHz", "243.000 MHz",
                "462.575 MHz", "121.500 MHz", "156.800 MHz" };
            static readonly string[] SIG_FRAG = { "CONFIRM ASSET ENRT", "PKG SECURED",
                "GRID REF 4-7", "WINDOW 0200-0400Z", "DENY DENY DENY", "EXTRACTION NEG",
                "AUTH CODE WHISKEY", "TARGET ACQUIRED", "ABORT ABORT", "BACKUP FREQ 7.4" };
            static readonly string[] CD_NAMES = { "SILENT THUNDER", "BROKEN ARROW", "IRON CURTAIN",
                "DARK WINTER", "RED PHOENIX", "GHOST VEIL", "COLD HAMMER", "NIGHT SHADE" };
            static readonly string[] EX_FILES = { "sat_telemetry.db", "comms_log.tar",
                "personnel.csv", "patrol_rte.gpx", "radar_cfg.bin", "crypto.aes",
                "drone_vid.h264", "access.dat" };
            static readonly string[] EX_TGTS = { "CENTCOM-EAST", "NAVAL-OPS-7",
                "AIR-CTRL-N", "SIGINT-HUB-3", "LOGNET-PRIME" };
            static readonly string[] AST_NAMES = { "VIPER", "JACKAL", "CONDOR", "SPARROW",
                "COBRA", "FALCON", "MANTIS", "RAPTOR", "HOUND", "SPECTER", "TALON", "WRAITH" };

            static readonly Color SBG = new Color(5, 8, 5);
            static readonly Color CST = new Color(22, 55, 32);
            static readonly Color ELV = new Color(16, 42, 26);
            static readonly Color PKC = new Color(30, 65, 40);
            static readonly Color AMBER = new Color(140, 100, 35);
            static readonly Color MGREEN = new Color(45, 120, 55);

            // ════════════════════════════════════════
            // PUBLIC ENTRY POINT
            // ════════════════════════════════════════
            public static void Render(MySpriteDrawFrame frame, RectangleF area, Jet jet, HUDModule hud, int tick)
            {
                if (jet == null || jet._cockpit == null) return;
                float x = area.Position.X, y = area.Position.Y;
                float w = area.Width, areaH = area.Height;
                float gap = 6f, resH = 36f;

                double fuelPct, fuelSec;
                jet.GetFuelStatus(out fuelPct, out fuelSec);
                if (jet.tanks.Count > 0)
                { DrawResCard(frame, x, y, w, resH, "H2 FUEL", (float)fuelPct, FmtTime(fuelSec)); y += resH + gap; }

                float curMWh, maxMWh, netDrain;
                jet.GetBatteryStatus(out curMWh, out maxMWh, out netDrain);
                if (jet.batteries.Count > 0)
                {
                    float bp = maxMWh > 0 ? curMWh / maxMWh : 0f;
                    string bt = netDrain > 0.001f ? FmtTime(curMWh / netDrain * 3600) : netDrain < -0.001f ? "CHRG" : "---";
                    DrawResCard(frame, x, y, w, resH, "BATTERY", bp, bt); y += resH + gap;
                }

                float engH = 90f, remaining = area.Position.Y + areaH - y;
                if (remaining > engH + gap + 50f)
                { DrawEngCard(frame, x, y, w, engH, jet); y += engH + gap; }

                float satH = area.Position.Y + areaH - y;
                if (satH > 50f)
                    DrawSlideshow(frame, x, y, w, satH, tick, jet);
            }

            // ════════════════════════════════════════
            // SLIDESHOW CONTROLLER
            // ════════════════════════════════════════
            static void DrawSlideshow(MySpriteDrawFrame frame, float x, float y, float w, float h, int tick, Jet jet)
            {
                bool flying = jet.GetVelocity() > 5.0;
                if (flying && !satOffline) { satOffline = true; offlineTick = 0; }
                else if (!flying && satOffline) { satOffline = false; offlineTick = 0; }
                if (satOffline) offlineTick++;

                // Transition black frame
                if (transitionTick > 0)
                {
                    Rect(frame, x + w / 2f, y + h / 2f, w, h, SBG);
                    SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, new Color(14, 26, 16));
                    transitionTick--;
                    if (transitionTick == 0) InitSlide(activeSlide);
                    return;
                }

                slideTick++;
                bool done = false;

                switch (activeSlide)
                {
                    case 0: done = DrawSatSlide(frame, x, y, w, h, tick); break;
                    case 1: done = DrawSigintSlide(frame, x, y, w, h, tick); break;
                    case 2: done = DrawCountdownSlide(frame, x, y, w, h, tick); break;
                    case 3: done = DrawExfilSlide(frame, x, y, w, h, tick); break;
                    case 4: done = DrawAssetSlide(frame, x, y, w, h, tick); break;
                }

                if (satOffline) DrawOffline(frame, x, y, w, h);

                if (!satOffline && done)
                {
                    activeSlide = (activeSlide + 1) % SLIDE_COUNT;
                    slideTick = 0;
                    transitionTick = 3;
                }
            }

            static void InitSlide(int slide)
            {
                slideTick = 0;
                switch (slide)
                {
                    case 0: GenMap(); break;
                    case 1:
                        for (int i = 0; i < 22; i++) { sigW0[i] = 1; sigW1[i] = 1; }
                        sigPhase = 0; sigTimer = 60 + (int)(Rng() % 60); sigFrag = 0;
                        break;
                    case 2:
                        cdSec = 180 + (int)(Rng() % 120); cdSub = 0;
                        cdCodeIdx = (int)(Rng() % (uint)CD_NAMES.Length); cdFlash = 0;
                        break;
                    case 3:
                        exFile = 0; exPhase = 0; exPurge = 0;
                        exSpeed = 0.006f + (Rng() % 8) * 0.001f;
                        exTgtIdx = (int)(Rng() % (uint)EX_TGTS.Length);
                        for (int i = 0; i < 4; i++) { exProg[i] = 0f; exFIdx[i] = (int)(Rng() % (uint)EX_FILES.Length); }
                        break;
                    case 4:
                        for (int i = 0; i < 6; i++)
                        { astName[i] = AST_NAMES[(int)(Rng() % (uint)AST_NAMES.Length)] + "-" + (Rng() % 9 + 1); astStat[i] = 0; }
                        astTimer = 180 + (int)(Rng() % 180); astTarget = (int)(Rng() % 6);
                        break;
                }
            }

            static void DrawOffline(MySpriteDrawFrame frame, float x, float y, float w, float h)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, new Color(2, 3, 2));
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, new Color(14, 26, 16));
            }

            // ════════════════════════════════════════
            // SLIDE 0: SAT RECON MAP
            // ════════════════════════════════════════
            static bool DrawSatSlide(MySpriteDrawFrame frame, float x, float y, float w, float h, int tick)
            {
                float vpCX = x + w / 2f, vpCY = y + h / 2f + 4f;
                float vL = x, vR = x + w, vT = y, vB = y + h;
                if (rCnt == 0) GenMap();

                if (!satOffline)
                {
                    camDwell--;
                    if (camDwell <= 0)
                    {
                        if (camState == 0)
                        {
                            camState = 1; camMarker = 0;
                            if (mCnt > 0) { camTgtX = mX[0]; camTgtY = mY[0]; camTgtZ = 3.0f + (Rng() % 20) * 0.15f; }
                            camDwell = 280;
                        }
                        else
                        {
                            camMarker++;
                            if (camMarker >= mCnt) return true; // slide done
                            camTgtX = mX[camMarker]; camTgtY = mY[camMarker];
                            camTgtZ = 3.0f + (Rng() % 20) * 0.15f;
                            camDwell = 240 + (int)(Rng() % 80);
                        }
                    }
                    float dx = camTgtX - camX, dy = camTgtY - camY;
                    float spd = (float)Math.Sqrt(dx * dx + dy * dy) > 5f ? 0.04f : 0.025f;
                    camX += dx * spd; camY += dy * spd;
                    camZoom += (camTgtZ - camZoom) * 0.02f;
                }

                float sc = Math.Min(w, h) / 110f * camZoom;
                Rect(frame, x + w / 2f, y + h / 2f, w, h, SBG);

                // Grid
                Color gridC = new Color(8, 14, 8, 16);
                for (float gv = (float)Math.Floor((camY - h / sc * 0.6f) / 20f) * 20f; gv <= camY + h / sc * 0.6f; gv += 20f)
                { float gy = vpCY + (gv - camY) * sc; if (gy > vT + 1 && gy < vB - 1) Rect(frame, x + w / 2f, gy, w, 0.5f, gridC); }
                for (float gv = (float)Math.Floor((camX - w / sc * 0.6f) / 20f) * 20f; gv <= camX + w / sc * 0.6f; gv += 20f)
                { float gx = vpCX + (gv - camX) * sc; if (gx > vL + 1 && gx < vR - 1) Rect(frame, gx, y + h / 2f, 0.5f, h, gridC); }

                // Contours
                Color[] lvlC = { CST, ELV, PKC }; float[] lvlW = { 1.0f, 0.7f, 0.5f };
                for (int lv = 0; lv < 3; lv++)
                    for (int ri = 0; ri < rCnt; ri++)
                    {
                        if (rL[ri] != lv) continue;
                        int s = rS[ri], n = rN[ri];
                        for (int p = 0; p < n; p++)
                        {
                            int i0 = s + p, i1 = s + (p + 1) % n;
                            float x0 = vpCX + (vPX[i0] - camX) * sc, y0 = vpCY + (vPY[i0] - camY) * sc;
                            float x1 = vpCX + (vPX[i1] - camX) * sc, y1 = vpCY + (vPY[i1] - camY) * sc;
                            if ((x0 < vL && x1 < vL) || (x0 > vR && x1 > vR) || (y0 < vT && y1 < vT) || (y0 > vB && y1 > vB)) continue;
                            SpriteHelpers.AddLineSprite(frame, new Vector2(x0, y0), new Vector2(x1, y1), lvlW[lv], lvlC[lv]);
                        }
                    }

                if (!satOffline) Rect(frame, x + w / 2f, y + 14f + ((tick * 0.5f) % (h - 14f)), w, 1f, new Color(10, 20, 12, 16));

                // Threat rings
                for (int i = 0; i < mCnt; i++)
                {
                    if (mT[i] != 1) continue;
                    float sx = vpCX + (mX[i] - camX) * sc, sy = vpCY + (mY[i] - camY) * sc;
                    float tr = 16f * sc / camZoom;
                    if (sx + tr < vL || sx - tr > vR || sy + tr < vT || sy - tr > vB) continue;
                    for (int p = 0; p < 12; p += 2)
                    {
                        float a0 = p * 6.2832f / 12, a1 = (p + 1) * 6.2832f / 12;
                        SpriteHelpers.AddLineSprite(frame, new Vector2(sx + (float)Cs(a0) * tr, sy + (float)Sn(a0) * tr),
                            new Vector2(sx + (float)Cs(a1) * tr, sy + (float)Sn(a1) * tr), 0.6f, new Color(140, 100, 35, 50));
                    }
                }

                // Markers
                float tsc = Cl(camZoom * 0.12f, 0.16f, 0.5f);
                for (int i = 0; i < mCnt; i++)
                {
                    float sx = vpCX + (mX[i] - camX) * sc, sy = vpCY + (mY[i] - camY) * sc;
                    if (sx < vL - 20 || sx > vR + 20 || sy < vT - 10 || sy > vB + 10) continue;
                    Color mc = mT[i] == 1 ? AMBER : MGREEN;
                    Txt(frame, "+", sx, sy - tsc * 8f, tsc * 1.2f, mc, MFDTheme.AC);
                    Txt(frame, mNm[i] ?? "", sx, sy + tsc * 12f, tsc, mc, MFDTheme.AC);
                    if (camState == 1 && camMarker == i)
                    {
                        Color ic = new Color(mc.R, mc.G, mc.B, 180);
                        if (mIn[i] != null) Txt(frame, mIn[i], sx, sy + tsc * 42f, tsc * 0.8f, ic, MFDTheme.AC);
                        if (mIn2[i] != null) Txt(frame, mIn2[i], sx, sy + tsc * 66f, tsc * 0.8f, ic, MFDTheme.AC);
                    }
                }

                // Edge masks + border + header
                float mp = 5f;
                Rect(frame, x + w / 2f, vT - mp / 2f + 0.5f, w + 2f, mp, SBG);
                Rect(frame, x + w / 2f, vB + mp / 2f - 0.5f, w + 2f, mp, SBG);
                Rect(frame, vL - mp / 2f + 0.5f, y + h / 2f, mp, h + 2f, SBG);
                Rect(frame, vR + mp / 2f - 0.5f, y + h / 2f, mp, h + 2f, SBG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, new Color(14, 26, 16));
                Txt(frame, satName ?? "", x + 4f, y + 2f, 0.24f, MFDTheme.CORP_GOLD, MFDTheme.AL);
                Txt(frame, satCoord ?? "", x + w - 4f, y + 2f, 0.18f, new Color(18, 36, 20), MFDTheme.AR);
                return false;
            }

            // ════════════════════════════════════════
            // SLIDE 1: SIGINT INTERCEPT
            // ════════════════════════════════════════
            static bool DrawSigintSlide(MySpriteDrawFrame frame, float x, float y, float w, float h, int tick)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, SBG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, new Color(14, 26, 16));
                Txt(frame, "SIGINT INTERCEPT", x + 4f, y + 2f, 0.24f, MFDTheme.CORP_GOLD, MFDTheme.AL);
                bool recOn = (tick / 30) % 2 == 0;
                Txt(frame, recOn ? "●REC" : " REC", x + w - 4f, y + 2f, 0.2f,
                    recOn ? new Color(180, 50, 40) : MFDTheme.DIM_TEXT, MFDTheme.AR);
                Txt(frame, SIG_FREQ[(slideTick / 900) % SIG_FREQ.Length], x + 4f, y + 12f, 0.2f, MFDTheme.DIM_TEXT, MFDTheme.AL);

                if (!satOffline)
                {
                    // Scroll waveforms every 3 ticks
                    if (slideTick % 3 == 0)
                    {
                        for (int i = 0; i < 21; i++) { sigW0[i] = sigW0[i + 1]; sigW1[i] = sigW1[i + 1]; }
                        bool burst = sigPhase == 1;
                        sigW0[21] = burst ? (int)(Rng() % 13) + 3 : (int)(Rng() % 2) + 1;
                        sigW1[21] = burst ? (int)(Rng() % 11) + 2 : (int)(Rng() % 2) + 1;
                    }
                    sigTimer--;
                    if (sigTimer <= 0)
                    {
                        if (sigPhase == 0) { sigPhase = 1; sigTimer = 90 + (int)(Rng() % 60); }
                        else { sigPhase = 0; sigTimer = 60 + (int)(Rng() % 90); sigFrag = (sigFrag + 1) % SIG_FRAG.Length; }
                    }
                }

                // Draw waveforms (bars grow upward from baseline)
                float barW = (w - 10f) / 22f;
                float base0 = y + 40f, base1 = y + 62f;
                // Baseline separator lines
                Rect(frame, x + w / 2f, base0 + 1f, w - 10f, 0.5f, MFDTheme.BORDER);
                Rect(frame, x + w / 2f, base1 + 1f, w - 10f, 0.5f, MFDTheme.BORDER);
                Txt(frame, "CH1", x + 4f, y + 22f, 0.16f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                Txt(frame, "CH2", x + 4f, y + 44f, 0.16f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                for (int i = 0; i < 22; i++)
                {
                    float bx = x + 5f + i * barW + barW / 2f;
                    float bh0 = sigW0[i] * 1.3f, bh1 = sigW1[i] * 1.1f;
                    Color bc = sigPhase == 1 ? MFDTheme.ACCENT : MFDTheme.DIM_TEXT;
                    Rect(frame, bx, base0 - bh0 / 2f, barW - 1f, bh0, bc);
                    Rect(frame, bx, base1 - bh1 / 2f, barW - 1f, bh1, new Color(bc.R, (int)(bc.G * 0.7f), bc.B, bc.A));
                }

                // Decoded fragment
                float fragY = y + h * 0.62f;
                if (sigFrag > 0 || sigPhase == 1)
                {
                    int fi = sigPhase == 1 ? sigFrag : Math.Max(0, sigFrag - 1);
                    Txt(frame, "\"" + SIG_FRAG[fi % SIG_FRAG.Length] + "\"", x + w / 2f, fragY, 0.26f, MFDTheme.NORMAL_TEXT, MFDTheme.AC);
                }
                else
                    Txt(frame, "[MONITORING...]", x + w / 2f, fragY, 0.22f, MFDTheme.DIM_TEXT, MFDTheme.AC);

                // Signal strength bar (single, simpler)
                float mY = y + h - 12f;
                Txt(frame, "SIG:", x + 4f, mY - 3f, 0.18f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                float mW = w - 36f, mBx = x + 30f;
                float mFill = sigPhase == 1 ? 0.7f + (Rng() % 20) * 0.015f : 0.08f + (Rng() % 10) * 0.005f;
                Rect(frame, mBx + mW / 2f, mY, mW, 3f, new Color(6, 10, 6));
                float fmw = mW * Cl(mFill, 0f, 1f);
                if (fmw > 0.5f) Rect(frame, mBx + fmw / 2f, mY, fmw, 3f, sigPhase == 1 ? MFDTheme.ACCENT : MFDTheme.DIM_TEXT);

                return slideTick >= 900;
            }

            // ════════════════════════════════════════
            // SLIDE 2: COUNTDOWN TIMER
            // ════════════════════════════════════════
            static bool DrawCountdownSlide(MySpriteDrawFrame frame, float x, float y, float w, float h, int tick)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, SBG);

                // Pulsing border when low
                int ba = cdSec < 30 ? (int)(120 + 100 * Sn(tick * 0.15)) : 80;
                Color bc = cdSec < 30 ? new Color(140, 60, 20, ba) : new Color(14, 26, 16);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, bc);

                Txt(frame, "OPERATION", x + w / 2f, y + 6f, 0.22f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                Txt(frame, CD_NAMES[cdCodeIdx % CD_NAMES.Length], x + w / 2f, y + 18f, 0.32f, MFDTheme.CORP_GOLD, MFDTheme.AC);

                if (!satOffline)
                {
                    cdSub++;
                    if (cdSub >= 60) { cdSub = 0; cdSec--; }
                    if (cdSec <= 0)
                    {
                        cdFlash = 30; cdSec = 180 + (int)(Rng() % 120);
                        cdCodeIdx = (int)(Rng() % (uint)CD_NAMES.Length);
                    }
                    if (cdFlash > 0) cdFlash--;
                }

                // Flash effect on zero
                if (cdFlash > 20)
                    Rect(frame, x + w / 2f, y + h / 2f, w, h, new Color(140, 100, 35, (cdFlash - 20) * 15));

                // Big countdown with blinking colon
                string sep = (cdSub < 30) ? ":" : " ";
                string timeStr = $"T-{cdSec / 60:D2}{sep}{cdSec % 60:D2}";
                Txt(frame, timeStr, x + w / 2f, y + h * 0.32f, 0.55f, MFDTheme.BRIGHT_TEXT, MFDTheme.AC);

                // Classification + status
                Txt(frame, "// CLASSIFIED //", x + w / 2f, y + h * 0.58f, 0.2f, new Color(80, 40, 20), MFDTheme.AC);
                Txt(frame, "MISSION CLOCK ACTIVE", x + w / 2f, y + h - 14f, 0.18f, MFDTheme.DIM_TEXT, MFDTheme.AC);

                // Seconds indicator dots
                int dotCount = cdSub / 10; // 0-5 dots filling up each second
                for (int d = 0; d < 6; d++)
                {
                    Color dc = d < dotCount ? MFDTheme.ACCENT : new Color(10, 18, 10);
                    Rect(frame, x + w * 0.3f + d * 6f, y + h * 0.72f, 3f, 3f, dc);
                }

                return slideTick >= 720;
            }

            // ════════════════════════════════════════
            // SLIDE 3: DATA EXFILTRATION
            // ════════════════════════════════════════
            static bool DrawExfilSlide(MySpriteDrawFrame frame, float x, float y, float w, float h, int tick)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, SBG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, new Color(14, 26, 16));
                Txt(frame, "DATA EXFIL", x + 4f, y + 2f, 0.24f, MFDTheme.CORP_GOLD, MFDTheme.AL);
                Txt(frame, "TGT: " + EX_TGTS[exTgtIdx % EX_TGTS.Length], x + 4f, y + 13f, 0.2f, MFDTheme.DIM_TEXT, MFDTheme.AL);

                if (!satOffline && exPhase == 0)
                {
                    exProg[exFile] += exSpeed;
                    if (exProg[exFile] >= 1f)
                    {
                        exProg[exFile] = 1f; exFile++;
                        if (exFile >= 4) { exPhase = 1; exPurge = 0; }
                    }
                }
                if (exPhase == 1 && !satOffline) exPurge++;

                float rowH = (h - 38f) / 5f;
                float rowY = y + 26f;
                // Pick 4 file names deterministically from seed
                for (int i = 0; i < 4; i++)
                {
                    float ry = rowY + i * rowH;
                    bool done2 = exProg[i] >= 1f;
                    bool active = i == exFile && exPhase == 0;
                    Color tc = done2 ? MFDTheme.DIM_TEXT : active ? MFDTheme.NORMAL_TEXT : new Color(30, 50, 30);
                    Txt(frame, EX_FILES[exFIdx[i]], x + 4f, ry, 0.22f, tc, MFDTheme.AL);

                    if (done2)
                        Txt(frame, "OK", x + w - 4f, ry, 0.22f, MFDTheme.ACCENT, MFDTheme.AR);
                    else if (active)
                    {
                        // Percentage
                        Txt(frame, $"{(int)(exProg[i] * 100)}%", x + w - 4f, ry, 0.2f, MFDTheme.STATUS_VAL, MFDTheme.AR);
                        // Progress bar
                        float barX2 = x + 4f, barW2 = w - 8f, barY2 = ry + 11f;
                        Rect(frame, barX2 + barW2 / 2f, barY2, barW2, 2f, new Color(8, 14, 8));
                        float fw = barW2 * Cl(exProg[i], 0f, 1f);
                        if (fw > 0.5f) Rect(frame, barX2 + fw / 2f, barY2, fw, 2f, MFDTheme.ACCENT);
                    }
                }

                // Status line
                float statusY = rowY + 4 * rowH + 4f;
                if (exPhase == 1)
                {
                    string ps = exPurge > 120 ? "EXFIL COMPLETE" : "PURGING TRACES" + new string('.', (exPurge / 20) % 4);
                    Txt(frame, ps, x + w / 2f, statusY, 0.22f, exPurge > 120 ? MFDTheme.ACCENT : AMBER, MFDTheme.AC);
                }
                else
                    Txt(frame, $"{exFile}/4 FILES", x + w / 2f, statusY, 0.2f, MFDTheme.DIM_TEXT, MFDTheme.AC);

                return slideTick >= 900;
            }

            // ════════════════════════════════════════
            // SLIDE 4: ASSET TRACKER
            // ════════════════════════════════════════
            static bool DrawAssetSlide(MySpriteDrawFrame frame, float x, float y, float w, float h, int tick)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, SBG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, new Color(14, 26, 16));
                Txt(frame, "ASSET STATUS", x + 4f, y + 2f, 0.24f, MFDTheme.CORP_GOLD, MFDTheme.AL);

                if (!satOffline)
                {
                    astTimer--;
                    if (astTimer <= 0)
                    {
                        int t = astTarget;
                        if (astStat[t] == 0) { astStat[t] = 1; astTimer = 90; }
                        else if (astStat[t] == 1) { astStat[t] = 2; astTimer = 120; }
                        else
                        {
                            astName[t] = AST_NAMES[(int)(Rng() % (uint)AST_NAMES.Length)] + "-" + (Rng() % 9 + 1);
                            astStat[t] = 0;
                            astTarget = (int)(Rng() % 6);
                            astTimer = 180 + (int)(Rng() % 240);
                        }
                    }
                }

                float rowH = (h - 28f) / 7f;
                int opCount = 0;
                for (int i = 0; i < 6; i++)
                {
                    float ry = y + 16f + i * rowH;
                    string dot, status;
                    Color dc, tc;
                    if (astStat[i] == 0) { dot = "●"; dc = MFDTheme.ACCENT; tc = MFDTheme.NORMAL_TEXT; status = "ACTIVE"; opCount++; }
                    else if (astStat[i] == 1)
                    {
                        bool blink = (tick / 15) % 2 == 0;
                        dot = "●"; dc = blink ? new Color(180, 50, 40) : new Color(80, 25, 20);
                        tc = AMBER; status = "COMP";
                    }
                    else { dot = "○"; dc = new Color(20, 30, 20); tc = new Color(20, 30, 20); status = "DARK"; }

                    Txt(frame, dot, x + 4f, ry, 0.24f, dc, MFDTheme.AL);
                    Txt(frame, astName[i] ?? "---", x + 18f, ry, 0.24f, tc, MFDTheme.AL);
                    Txt(frame, status, x + w - 4f, ry, 0.2f, tc, MFDTheme.AR);
                    // Row separator
                    Rect(frame, x + w / 2f, ry + rowH - 1f, w - 8f, 0.5f, new Color(10, 18, 10));
                }

                Txt(frame, $"{opCount}/6 OPERATIONAL", x + w / 2f, y + h - 12f, 0.22f,
                    opCount >= 5 ? MFDTheme.ACCENT : opCount >= 3 ? AMBER : new Color(180, 50, 40), MFDTheme.AC);

                return slideTick >= 720;
            }

            // ════════════════════════════════════════
            // NOISE (5-octave value noise for coastlines)
            // ════════════════════════════════════════
            static float NHash(int ix, int iy, uint s)
            {
                uint h2 = (uint)ix * 374761393u + (uint)iy * 668265263u + s;
                h2 = (h2 ^ (h2 >> 13)) * 1274126177u;
                return ((h2 ^ (h2 >> 16)) & 0xFFFFu) / 65535f;
            }

            static float VNoise(float x, float y, uint s)
            {
                int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
                float fx = x - ix, fy = y - iy;
                fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
                float a = NHash(ix, iy, s), b = NHash(ix + 1, iy, s);
                float c = NHash(ix, iy + 1, s), d = NHash(ix + 1, iy + 1, s);
                return (a + (b - a) * fx) * (1f - fy) + (c + (d - c) * fx) * fy;
            }

            static float FNoise(float x, float y, uint s)
            {
                return VNoise(x, y, s) * 0.4f + VNoise(x * 2f, y * 2f, s + 31u) * 0.25f
                    + VNoise(x * 4f, y * 4f, s + 67u) * 0.15f + VNoise(x * 8f, y * 8f, s + 113u) * 0.12f
                    + VNoise(x * 16f, y * 16f, s + 151u) * 0.08f;
            }

            // ════════════════════════════════════════
            // ISLAND GENERATION
            // ════════════════════════════════════════
            static void AddRing(float cx, float cy, float baseR, float spine, float aspect, int pts, int lvl)
            {
                if (rCnt >= MR || vCnt + pts > MV) return;
                rS[rCnt] = vCnt; rN[rCnt] = pts; rL[rCnt] = lvl;
                for (int p = 0; p < pts; p++)
                {
                    float a = p * 6.2832f / pts;
                    float sX = cx + (float)Cs(a) * baseR * 0.5f, sY = cy + (float)Sn(a) * baseR * 0.5f;
                    float r = baseR * (0.4f + FNoise(sX * 0.11f, sY * 0.11f, mapSeed + (uint)lvl * 137u) * 0.85f);
                    r *= (1f + (1f - aspect) * (float)Cs(2.0 * (a - spine)));
                    if (r < 1.5f) r = 1.5f;
                    vPX[vCnt] = cx + (float)Cs(a) * r; vPY[vCnt] = cy + (float)Sn(a) * r; vCnt++;
                }
                rCnt++;
            }

            static void GenMap()
            {
                rCnt = 0; vCnt = 0; mCnt = 0;
                int oi; do { oi = (int)(Rng() % (uint)OPS.Length); } while (oi == lastOp);
                lastOp = oi; satName = OPS[oi];
                satCoord = $"{Rng() % 70 + 10:D2}N {Rng() % 160 + 10:D3}E";
                mapSeed = Rng() * 1000u + Rng();
                int islands = (int)(Rng() % 2) + 1;
                for (int isl = 0; isl < islands; isl++)
                {
                    float clx = (int)(Rng() % 50) - 25, cly = (int)(Rng() % 35) - 17;
                    float spine = ((int)(Rng() % 628) - 314) * 0.01f, aspect = 0.5f + (Rng() % 35) * 0.01f;
                    float cR = 16f + (Rng() % 14);
                    AddRing(clx, cly, cR * 1.15f, spine, aspect, 32, 0);
                    AddRing(clx, cly, cR, spine, aspect, 36, 0);
                    AddRing(clx, cly, cR * 0.65f, spine, aspect, 28, 1);
                    AddRing(clx, cly, cR * 0.4f, spine, aspect, 20, 1);
                    AddRing(clx, cly, cR * 0.2f, spine, aspect, 14, 2);
                    if (Rng() % 2 == 0)
                    {
                        float oA = (Rng() % 628) * 0.01f, oD = cR * 1.5f + 5f + (Rng() % 10);
                        AddRing(clx + (float)Cs(oA) * oD, cly + (float)Sn(oA) * oD, 4f + (Rng() % 5), (Rng() % 628) * 0.01f, 0.7f, 18, 0);
                    }
                }
                int nm = (int)(Rng() % 3) + 4;
                for (int m = 0; m < nm && mCnt < MM; m++)
                {
                    int bi = (int)(Rng() % (uint)Math.Max(1, vCnt));
                    mX[mCnt] = vPX[bi] + (int)(Rng() % 6) - 3; mY[mCnt] = vPY[bi] + (int)(Rng() % 4) - 2;
                    mT[mCnt] = m < 2 ? 1 : (int)(Rng() % 2);
                    mNm[mCnt] = NAMES[nameIdx % NAMES.Length]; nameIdx++;
                    mIn[mCnt] = INTEL_S[(int)(Rng() % (uint)INTEL_S.Length)];
                    mIn2[mCnt] = INTEL_S[(int)(Rng() % (uint)INTEL_S.Length)]; mCnt++;
                }
                camX = 0; camY = 0; camZoom = 0.85f; camTgtX = 0; camTgtY = 0; camTgtZ = 0.85f;
                camState = 0; camMarker = 0; camDwell = 120;
            }

            // ════════════════════════════════════════
            // ENGINE + RESOURCE CARDS
            // ════════════════════════════════════════
            static void DrawEngCard(MySpriteDrawFrame frame, float x, float y, float w, float h, Jet jet)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, MFDTheme.BORDER_LIGHT);
                Txt(frame, "THRUST", x + w / 2f, y + 2f, 0.32f, MFDTheme.DIM_TEXT_MID, MFDTheme.AC);
                float midX = x + w / 2f, colW = (w - 16f) / 2f, top = y + 16f, colH = h - 20f;
                DrawEngCol(frame, x + 4f, top, colW, colH, jet.leftEngines, jet.leftAB, "ENG L");
                DrawEngCol(frame, midX + 4f, top, colW, colH, jet.rightEngines, jet.rightAB, "ENG R");
            }

            static void DrawEngCol(MySpriteDrawFrame frame, float x, float y, float w, float colH,
                System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyThrust> eng,
                System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyThrust> ab, string label)
            {
                int fn, tot; Jet.GetEngineHealth(eng, out fn, out tot);
                float curKN, maxKN; Jet.GetEngineThrust(eng, out curKN, out maxKN);
                float abCur, abMax; Jet.GetEngineThrust(ab, out abCur, out abMax);
                float tMax = maxKN + abMax, tCur = curKN + abCur;
                float pct = tMax > 0 ? tCur / tMax : 0f; bool dmg = fn < tot;
                Txt(frame, label, x, y, 0.32f, MFDTheme.MID_TEXT, MFDTheme.AL);
                Txt(frame, $"{fn}/{tot}", x + w, y, 0.3f, dmg ? MFDTheme.WARN : MFDTheme.ACCENT, MFDTheme.AR);
                float bx = x + 2f, bw = w - 4f, bt = y + 14f, bh = colH - 28f;
                if (bh < 6f) bh = 6f;
                Rect(frame, bx + bw / 2f, bt + bh / 2f, bw, bh, MFDTheme.BAR_TRACK);
                SpriteHelpers.DrawRectangleOutline(frame, bx, bt, bw, bh, 0.5f, MFDTheme.BORDER);
                if (dmg && fn > 0) { float ch = bh * Cl((float)fn / tot, 0f, 1f); Rect(frame, bx + bw / 2f, bt + bh - ch / 2f, bw, ch, new Color(12, 22, 12)); if (ch > 1f && ch < bh - 1f) Rect(frame, bx + bw / 2f, bt + bh - ch, bw + 2f, 1f, MFDTheme.WARN); }
                else if (!dmg) Rect(frame, bx + bw / 2f, bt + bh / 2f, bw, bh, new Color(12, 22, 12));
                float fh = bh * Cl(pct, 0f, 1f);
                if (fh > 0.5f) Rect(frame, bx + bw / 2f, bt + bh - fh / 2f, bw, fh, abCur > 0.1f ? MFDTheme.WARN : MFDTheme.BAR_FILL);
                Txt(frame, tMax > 0 ? $"{tCur:F0}/{tMax:F0}" : "---", x + w / 2f, bt + bh + 1f, 0.28f, MFDTheme.STATUS_VAL, MFDTheme.AC);
                if (tMax > 0) Txt(frame, "kN", x + w / 2f, bt + bh + 11f, 0.24f, MFDTheme.DIM_TEXT, MFDTheme.AC);
            }

            static void DrawResCard(MySpriteDrawFrame frame, float x, float y, float w, float h, string title, float pct, string timeStr)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, MFDTheme.BORDER_LIGHT);
                Txt(frame, title, x + 4f, y + 2f, 0.32f, MFDTheme.DIM_TEXT_MID, MFDTheme.AL);
                Txt(frame, $"{(int)(pct * 100)}%", x + w - 4f, y + 1f, 0.38f, MFDTheme.STATUS_VAL, MFDTheme.AR);
                float by = y + 14f, bw = w - 8f, bh = 4f, bx = x + 4f;
                Rect(frame, bx + bw / 2f, by + bh / 2f, bw, bh, MFDTheme.BAR_TRACK);
                SpriteHelpers.DrawRectangleOutline(frame, bx, by, bw, bh, 0.5f, MFDTheme.BORDER);
                float fw = bw * Cl(pct, 0f, 1f);
                if (fw > 0.5f) Rect(frame, bx + fw / 2f, by + bh / 2f, fw, bh, MFDTheme.BAR_FILL);
                Txt(frame, "REMAIN", bx, by + bh + 2f, 0.28f, MFDTheme.DIM_TEXT, MFDTheme.AL);
                Txt(frame, timeStr, bx + bw, by + bh + 2f, 0.28f, MFDTheme.STATUS_VAL, MFDTheme.AR);
            }

            static string FmtTime(double s) { return s <= 0 ? "---" : $"{(int)(s / 60):D2}:{(int)(s % 60):D2}"; }

            static void Rect(MySpriteDrawFrame f, float cx, float cy, float w, float h, Color c)
            { f.Add(new MySprite { Type = MFDTheme.TX, Data = MFDTheme.SQ, Position = new Vector2(cx, cy), Size = new Vector2(w, h), Color = c, Alignment = MFDTheme.AC }); }

            static void Txt(MySpriteDrawFrame f, string d, float x, float y, float s, Color c, TextAlignment a = MFDTheme.AL)
            { f.Add(new MySprite { Type = MFDTheme.TT, Data = d, Position = new Vector2(x, y), RotationOrScale = s, Color = c, Alignment = a, FontId = MFDTheme.FONT }); }
        }
    }
}
