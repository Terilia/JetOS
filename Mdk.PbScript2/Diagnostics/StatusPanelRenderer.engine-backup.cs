using System;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class StatusPanelRenderer
        {
            // Mini engine schematic colors
            static readonly Color C_ENG_BODY = new Color(20, 35, 20);
            static readonly Color C_ENG_LINE = new Color(40, 70, 40);

            // Afterburner visual delay
            const int AB_VISUAL_DELAY = 2;
            static int abActiveTicks = 0;
            static bool lastAbState = false;

            // Smoothed velocity for flame animation (EMA)
            static double smoothedFlameVelocity = 0;

            // Maneuver drift: velocity history for turn detection
            const int VEL_HISTORY_SIZE = 30;
            static Vector3D[] velHistory = new Vector3D[VEL_HISTORY_SIZE];
            static int velHistoryIdx = 0;
            static int velHistoryCount = 0;
            static float smoothedYawDrift = 0f;
            static float smoothedPitchDrift = 0f;

            // Golden ratio for particle spacing
            const float PHI = 1.6180339887f;

            public static void Render(MySpriteDrawFrame frame, RectangleF area, Jet jet, HUDModule hud, int tick)
            {
                if (jet == null || jet._cockpit == null) return;

                float x = area.Position.X;
                float y = area.Position.Y;
                float w = area.Width;
                float areaH = area.Height;
                float gap = 6f;

                float resH = 36f;
                int resCount = 0;
                if (jet.tanks.Count > 0) resCount++;
                if (jet.batteries.Count > 0) resCount++;

                float resTotal = resCount * (resH + gap);
                float maxPropH = areaH - resTotal;
                float propH = maxPropH * 0.75f;
                if (propH < 50f) propH = 50f;

                float thr = hud != null ? hud.throttlecontrol : 0f;
                bool abRaw = hud != null && hud.hydrogenswitch;

                if (abRaw)
                {
                    if (!lastAbState) abActiveTicks = 0;
                    abActiveTicks++;
                }
                else
                {
                    abActiveTicks = 0;
                }
                lastAbState = abRaw;
                bool abVisual = abRaw && abActiveTicks > AB_VISUAL_DELAY;

                double rawVel = jet.GetVelocity();
                smoothedFlameVelocity = smoothedFlameVelocity * 0.95 + rawVel * 0.05;

                // Compute maneuver drift from velocity history
                if (jet._cockpit != null)
                {
                    Vector3D currentVel = jet._cockpit.GetShipVelocities().LinearVelocity;
                    MatrixD cm = jet._cockpit.WorldMatrix;

                    velHistory[velHistoryIdx] = currentVel;
                    velHistoryIdx = (velHistoryIdx + 1) % VEL_HISTORY_SIZE;
                    if (velHistoryCount < VEL_HISTORY_SIZE) velHistoryCount++;

                    if (velHistoryCount >= VEL_HISTORY_SIZE)
                    {
                        Vector3D oldVel = velHistory[velHistoryIdx];
                        Vector3D deltaVel = currentVel - oldVel;

                        float lateralG = -(float)Vector3D.Dot(deltaVel, cm.Right);
                        float verticalG = -(float)Vector3D.Dot(deltaVel, cm.Up);

                        float targetYaw = lateralG * 2.5f;
                        float targetPitch = verticalG * 1.5f;

                        smoothedYawDrift = smoothedYawDrift * 0.85f + targetYaw * 0.15f;
                        smoothedPitchDrift = smoothedPitchDrift * 0.85f + targetPitch * 0.15f;

                        smoothedYawDrift = MathHelper.Clamp(smoothedYawDrift, -12f, 12f);
                        smoothedPitchDrift = MathHelper.Clamp(smoothedPitchDrift, -8f, 8f);
                    }
                }

                DrawPropulsionCard(frame, x, y, w, propH, jet, thr, abVisual, tick, smoothedFlameVelocity);
                y += propH + gap;

                double fuelPct, fuelSec;
                jet.GetFuelStatus(out fuelPct, out fuelSec);
                if (jet.tanks.Count > 0)
                {
                    DrawResourceCard(frame, x, y, w, resH, "H2 FUEL",
                        (float)fuelPct, FormatTime(fuelSec));
                    y += resH + gap;
                }

                float curMWh, maxMWh, netDrain;
                jet.GetBatteryStatus(out curMWh, out maxMWh, out netDrain);
                if (jet.batteries.Count > 0)
                {
                    float battPct = maxMWh > 0 ? curMWh / maxMWh : 0f;
                    string battTime = "---";
                    if (netDrain > 0.001f)
                    {
                        double hrs = curMWh / netDrain;
                        battTime = FormatTime(hrs * 3600);
                    }
                    else if (netDrain < -0.001f)
                    {
                        battTime = "CHRG";
                    }
                    DrawResourceCard(frame, x, y, w, resH, "BATTERY", battPct, battTime);
                }
            }

            static void DrawPropulsionCard(MySpriteDrawFrame frame, float x, float y, float w, float h,
                Jet jet, float thr, bool ab, int tick, double velocity)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, MFDTheme.BORDER_LIGHT);

                Txt(frame, "PROPULSION", x + w / 2f, y + 2f, 0.32f, MFDTheme.DIM_TEXT, TextAlignment.CENTER);

                float colW = (w - 14f) / 2f;
                float colTop = y + 14f;
                float colH = h - 18f;

                DrawEngineCol(frame, x + 4f, colTop, colW, colH, jet.leftEngines, jet.leftAB, "ENG L", thr, ab, tick, velocity);
                DrawEngineCol(frame, x + w - colW - 4f, colTop, colW, colH, jet.rightEngines, jet.rightAB, "ENG R", thr, ab, tick, velocity);
            }

            // ── 3D turbine disc projected from side view ──
            static void DrawTurbineDisc(MySpriteDrawFrame frame, float schX, float cy, float radius,
                int numBlades, float rotAngle, float bladeChord,
                int bladeR, int bladeG, int bladeB, int bladeA,
                int hubR, int hubG, int hubB, bool damaged)
            {
                if (damaged) return;
                float hubRad = radius * 0.18f;

                // Sort blades by depth: draw back blades, then hub, then front blades
                // Back blades (sinA < 0)
                for (int b = 0; b < numBlades; b++)
                {
                    float bAngle = rotAngle + (float)b / numBlades * MathHelper.TwoPi;
                    float cosA = (float)Math.Cos(bAngle);
                    float sinA = (float)Math.Sin(bAngle);
                    if (sinA >= 0) continue; // front blade, skip for now

                    float tipX = cosA * radius;
                    float hubTipX = cosA * hubRad;
                    float apparentH = 0.8f + Math.Abs(sinA) * bladeChord * 1.5f;
                    float depthAlpha = 0.3f + (sinA + 1f) * 0.35f;
                    int alpha = (int)(bladeA * depthAlpha);
                    float bladeLen = Math.Abs(tipX - hubTipX);
                    float bladeCX = schX + (tipX + hubTipX) / 2f;
                    if (bladeLen > 0.3f)
                        Rect(frame, bladeCX, cy, bladeLen, apparentH,
                            new Color(Math.Max(0, bladeR - 15), Math.Max(0, bladeG - 15), Math.Max(0, bladeB - 10), alpha));
                }

                // Hub
                Rect(frame, schX, cy, hubRad * 1.6f, hubRad * 1.6f, new Color(hubR, hubG, hubB, 140));
                Rect(frame, schX, cy, hubRad * 0.6f, hubRad * 0.6f, new Color(Math.Min(255, hubR + 20), Math.Min(255, hubG + 20), Math.Min(255, hubB + 15), 100));

                // Front blades (sinA >= 0)
                for (int b = 0; b < numBlades; b++)
                {
                    float bAngle = rotAngle + (float)b / numBlades * MathHelper.TwoPi;
                    float cosA = (float)Math.Cos(bAngle);
                    float sinA = (float)Math.Sin(bAngle);
                    if (sinA < 0) continue;

                    float tipX = cosA * radius;
                    float hubTipX = cosA * hubRad;
                    float apparentH = 0.8f + Math.Abs(sinA) * bladeChord * 1.5f;
                    float depthAlpha = 0.3f + (sinA + 1f) * 0.35f;
                    int alpha = (int)(bladeA * depthAlpha);
                    float bladeLen = Math.Abs(tipX - hubTipX);
                    float bladeCX = schX + (tipX + hubTipX) / 2f;
                    if (bladeLen > 0.3f)
                        Rect(frame, bladeCX, cy, bladeLen, apparentH,
                            new Color(bladeR, bladeG, bladeB, alpha));
                }
            }

            static void DrawEngineCol(MySpriteDrawFrame frame, float x, float y, float w, float availH,
                System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyThrust> eng,
                System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyThrust> abEng,
                string label, float thr, bool ab, int tick, double velocity)
            {
                int fn, tot; Jet.GetEngineHealth(eng, out fn, out tot);
                float curKN, maxKN; Jet.GetEngineThrust(eng, out curKN, out maxKN);
                float abCur, abMax; Jet.GetEngineThrust(abEng, out abCur, out abMax);
                float totalMax = maxKN + abMax;
                float totalCur = curKN + abCur;
                float thrustPct = totalMax > 0 ? totalCur / totalMax : 0f;

                Color hpC = fn >= tot ? MFDTheme.ACCENT : MFDTheme.WARN;

                // ── Label + health ──
                Txt(frame, label, x, y, 0.35f, MFDTheme.MID_TEXT, TextAlignment.LEFT);
                Txt(frame, $"{fn}/{tot}", x + w, y, 0.32f, hpC, TextAlignment.RIGHT);

                // ── Engine schematic layout ──
                float schTop = y + 13f;
                float schH = (availH - 30f) * 0.75f;
                if (schH < 10f) schH = 10f;
                float schW = 18f;
                float schX = x + w / 2f;
                float seed = label == "ENG L" ? 0f : 1.7f;

                // ── Intake funnel ──
                float intakeH = schH * 0.08f;
                float intakeW = schW * 1.3f;
                Rect(frame, schX, schTop + intakeH / 2f, intakeW, intakeH, C_ENG_BODY);
                Rect(frame, schX, schTop, intakeW, 0.5f, C_ENG_LINE);

                // ── Compressor body ──
                float bodyTop = schTop + intakeH;
                float bodyH = schH * 0.45f;
                Rect(frame, schX, bodyTop + bodyH / 2f, schW, bodyH, C_ENG_BODY);
                int segs = Math.Max(Math.Min(tot, 5), 2);
                float segH = bodyH / segs;
                for (int s = 1; s < segs; s++)
                    Rect(frame, schX, bodyTop + s * segH, schW - 2f, 0.5f, C_ENG_LINE);

                // Health: damaged segments blink
                for (int s = 0; s < segs; s++)
                {
                    bool dmg = (s * tot / segs) >= fn;
                    if (dmg && (tick / 10) % 2 == 0)
                        Rect(frame, schX, bodyTop + s * segH + segH / 2f, schW - 2f, segH - 1f, new Color(60, 20, 15));
                }
                SpriteHelpers.DrawRectangleOutline(frame, schX - schW / 2f, bodyTop, schW, bodyH, 0.5f, C_ENG_LINE);

                // ── Combustion chamber ──
                float combTop = bodyTop + bodyH;
                float combH = schH * 0.15f;
                float combW = schW + 2f;
                int glowR = (int)(C_ENG_BODY.R + (55 - C_ENG_BODY.R) * thrustPct);
                int glowG = (int)(C_ENG_BODY.G + (40 - C_ENG_BODY.G) * thrustPct);
                int glowB = (int)(C_ENG_BODY.B + (15 - C_ENG_BODY.B) * thrustPct);
                Rect(frame, schX, combTop + combH / 2f, combW, combH, new Color(glowR, glowG, Math.Max(0, glowB)));
                SpriteHelpers.DrawRectangleOutline(frame, schX - combW / 2f, combTop, combW, combH, 0.5f, C_ENG_LINE);

                // ── Central shaft ──
                float shaftTop = schTop + intakeH * 0.3f;
                float shaftBot = combTop + combH * 0.9f;
                Rect(frame, schX, (shaftTop + shaftBot) / 2f, 1.5f, shaftBot - shaftTop, new Color(35, 55, 35, 100));
                Rect(frame, schX, (shaftTop + shaftBot) / 2f, 0.5f, shaftBot - shaftTop, new Color(55, 80, 55, 70));

                // ── 3D Compressor blade discs ──
                float spinSpeed = 0.04f + thrustPct * 0.22f;
                float stageRadius = (schW - 4f) / 2f;
                for (int s = 0; s < segs; s++)
                {
                    bool dmg = (s * tot / segs) >= fn;
                    float stageCY = bodyTop + s * segH + segH / 2f;
                    int numBlades = 12 + s * 4;
                    int direction = (s % 2 == 0) ? 1 : -1;
                    float chord = 2.5f - s * 0.3f;
                    float rot = tick * spinSpeed * direction + s * 0.9f + seed;
                    DrawTurbineDisc(frame, schX, stageCY, stageRadius, numBlades, rot, chord,
                        70, 120, 70, 180, 45, 75, 45, dmg);
                }

                // ── 3D Turbine discs in combustion chamber ──
                float turbRadius = (combW - 4f) / 2f;
                float turbSpinSpeed = spinSpeed * 0.7f;
                for (int ts = 0; ts < 2; ts++)
                {
                    float turbCY = combTop + combH * (0.3f + ts * 0.4f);
                    float heat = 1f - ts * 0.3f;
                    int hR = (int)(80 + thrustPct * 70 * heat);
                    int hG = (int)(65 + thrustPct * 30 * heat);
                    int dir = (ts % 2 == 0) ? 1 : -1;
                    float rot = tick * turbSpinSpeed * dir + ts * 1.5f + seed + 3f;
                    DrawTurbineDisc(frame, schX, turbCY, turbRadius, 8, rot, 2.0f,
                        hR, hG, 40, 160, Math.Max(0, hR - 20), Math.Max(0, hG - 15), 30, false);
                }

                // ── Nozzle ──
                float nozzTop = combTop + combH;
                float nozzH = schH * 0.12f;
                float nozzWTop = schW;
                float nozzWBot = schW * 0.5f;
                for (int i = 0; i < 3; i++)
                {
                    float nt = (float)i / 3f;
                    float slW = MathHelper.Lerp(nozzWTop, nozzWBot, nt);
                    float slY = nozzTop + nozzH * nt + nozzH / 6f;
                    Rect(frame, schX, slY, slW, nozzH / 3f, new Color(15, 22, 15));
                }

                // ═══ Air particles: approach → funnel → spiral through stages → combustion ═══
                float velFactor = (float)MathHelper.Clamp(velocity / 100.0, 0.0, 1.0);
                float flowBotY = combTop + combH;
                float approachTopY = schTop - 20f;
                float flowTotalH = flowBotY - approachTopY;
                int numParticles = 48;

                // Air always flows forward — minimum idle speed prevents stall/reversal
                {
                    float pSpeed = Math.Max(0.003f, 0.003f + thrustPct * 0.018f);

                    for (int p = 0; p < numParticles; p++)
                    {
                        float rawPhase = (tick * pSpeed + p * PHI) % 1f;
                        float warpedPhase = rawPhase * rawPhase * (3f - 2f * rawPhase);
                        float py = approachTopY + warpedPhase * flowTotalH;

                        float px;
                        float pDepth;

                        if (py < schTop)
                        {
                            // APPROACH: scattered above, funnel toward intake
                            float approachT = MathHelper.Clamp((schTop - py) / 20f, 0f, 1f);
                            float laneAngle = p * PHI * 3.7f;
                            float lane = (float)Math.Sin(laneAngle) * 0.9f;
                            float entryOffset = (float)Math.Cos(laneAngle * 1.3f + 0.7f) * 0.4f;
                            float farX = schX + (lane + entryOffset) * schW * 1.8f * 0.5f;
                            float intakeSlot = lane * intakeW * 0.35f;
                            float nearX = schX + intakeSlot;
                            px = MathHelper.Lerp(nearX, farX, approachT * approachT);
                            pDepth = 0.5f;
                        }
                        else
                        {
                            // INSIDE ENGINE: spiral with blade rotation
                            float relY = py - bodyTop;
                            int stageIdx = (int)(relY / segH);
                            bool inCompressor = (relY >= 0 && stageIdx < segs);

                            float spiralAngle;
                            if (inCompressor)
                            {
                                int dir = (stageIdx % 2 == 0) ? 1 : -1;
                                float stageSpeedMul = 1f + stageIdx * 0.25f;
                                float stageRot = tick * spinSpeed * stageSpeedMul * dir + stageIdx * 0.9f + seed;
                                spiralAngle = stageRot + p * (MathHelper.TwoPi / numParticles);
                            }
                            else
                            {
                                spiralAngle = tick * spinSpeed * 1.5f + p * 1.3f + warpedPhase * 5f;
                            }

                            float maxR = (schW - 3f) / 2f;
                            float depthProgress = MathHelper.Clamp((py - bodyTop) / (flowBotY - bodyTop), 0f, 1f);
                            float compressionFactor = 1f - depthProgress * 0.6f;
                            float radiusWobble = 0.6f + (float)Math.Sin(p * 2.7f + tick * 0.05f) * 0.2f;
                            float spiralR = maxR * radiusWobble * compressionFactor;

                            // Maneuver drift affects air inside
                            float driftInfluence = depthProgress * 0.6f;
                            px = schX + (float)Math.Cos(spiralAngle) * spiralR + smoothedYawDrift * driftInfluence;
                            pDepth = (float)Math.Sin(spiralAngle);
                        }

                        // Size: grows with compression
                        bool insideEng = py >= bodyTop;
                        float stageDepth = insideEng ? MathHelper.Clamp((py - bodyTop) / (flowBotY - bodyTop), 0f, 1f) : 0f;
                        float compressionSize = 1f + stageDepth * 0.8f;
                        float pW = (1.0f + (pDepth + 1f) * 0.4f + (p % 2) * 0.3f) * compressionSize;
                        float pH = (1.2f + velFactor * 1.0f) * compressionSize;

                        // Alpha
                        float edgeFade;
                        if (warpedPhase < 0.1f) edgeFade = warpedPhase / 0.1f;
                        else if (warpedPhase > 0.85f) edgeFade = (1f - warpedPhase) / 0.15f;
                        else edgeFade = 1f;
                        float depthBright = 0.5f + (pDepth + 1f) * 0.25f;
                        int pAlpha = (int)((0.2f + thrustPct * 0.8f) * 130f * edgeFade * depthBright);

                        if (pAlpha > 4)
                        {
                            float heatProgress = Math.Max(0f, (warpedPhase - 0.15f) / 0.85f);
                            int pR = (int)(55 + heatProgress * 45);
                            int pG = (int)(130 + (p % 3) * 15 - heatProgress * 25);
                            int pB = (int)(55 + heatProgress * 15);
                            Rect(frame, px, py, pW, pH, new Color(pR, pG, pB, pAlpha));
                        }
                    }
                }

                // ═══ Exhaust plume ═══
                float exhaTop = nozzTop + nozzH;
                float maxPlumeH = schH * 0.3f;
                float nozzExitW = nozzWBot * 0.8f;

                int flameStage = 0;
                if (ab && abCur > 0.1f) flameStage = 2;
                else if (thrustPct > 0.70f) flameStage = 1;

                if (thrustPct > 0.01f)
                {
                    float driftX = smoothedYawDrift;
                    float driftY = smoothedPitchDrift;

                    // Plume height scales linearly with thrust — no sudden jumps between stages
                    // AB gets extra length, but the base is always proportional
                    float basePlumeH = maxPlumeH * thrustPct * (1f + velFactor * 0.25f);
                    if (flameStage == 2) basePlumeH *= 1.3f; // AB stretch
                    int tongues = thrustPct > 0.5f ? 3 : 2;
                    int slices = flameStage == 2 ? 10 : (thrustPct > 0.4f ? 12 : 8);

                    // Outer glow envelope
                    for (int sl = 0; sl < 6; sl++)
                    {
                        float gt = (float)sl / 5f;
                        float gs1 = (float)Math.Sin(tick * 0.23 + sl * 1.7 + seed);
                        float gY = exhaTop + basePlumeH * gt * 0.8f + 1f + driftY * gt * gt;
                        float gX = schX + driftX * gt * gt + gs1 * 0.5f;
                        float gW = nozzExitW * (1.2f - gt * 0.8f) + gs1 * 0.5f;
                        gW *= 1f - velFactor * 0.15f * gt;
                        int gA = (int)(30 * (1f - gt * gt));
                        if (gA < 3) continue;
                        Color gc = flameStage == 2
                            ? new Color((int)MathHelper.Lerp(120, 60, gt), (int)MathHelper.Lerp(70, 30, gt), 10, gA)
                            : new Color(20, (int)MathHelper.Lerp(50, 25, gt), (int)MathHelper.Lerp(90, 40, gt), gA);
                        Rect(frame, gX, gY, gW, basePlumeH / 5f, gc);
                    }

                    // Flame tongues
                    for (int tongue = 0; tongue < tongues; tongue++)
                    {
                        float tseed = seed + tongue * 2.17f;
                        float tongueBaseX = 0f;
                        if (tongues == 3) tongueBaseX = (tongue - 1) * nozzExitW * 0.2f;
                        else if (tongues == 2) tongueBaseX = (tongue - 0.5f) * nozzExitW * 0.15f;
                        float tongueLenMul = tongue == tongues / 2 ? 1f : 0.75f + (float)Math.Sin(tick * 0.13 + tseed) * 0.1f;

                        for (int sl = 0; sl < slices; sl++)
                        {
                            float t = (float)sl / (slices - 1);
                            float s1 = (float)Math.Sin(tick * 0.41 + sl * 0.9 + tseed);
                            float s2 = (float)Math.Sin(tick * 0.67 + sl * 1.3 + tseed * 1.4);
                            float s3 = (float)Math.Sin(tick * 0.23 + sl * 2.1 + tseed * 0.7);

                            float tw = nozzExitW * 0.35f * (1f - t * 0.85f);
                            tw *= 1f - velFactor * 0.2f * t;
                            tw += tw * s2 * 0.15f;
                            tw = Math.Max(tw, 0.3f);

                            float slH = basePlumeH * tongueLenMul / slices * 1.3f;
                            slH += slH * s1 * 0.08f;
                            slH = Math.Max(slH, 0.3f);

                            float wobbleDamp = 1f - velFactor * 0.5f;
                            float wx = tongueBaseX + s3 * tw * 0.4f * wobbleDamp + s1 * 1.2f * wobbleDamp;
                            wx += driftX * t * t;

                            float slY = exhaTop + basePlumeH * tongueLenMul * t * 0.85f + slH * 0.3f;
                            slY += s1 * 1.5f + driftY * t * t;
                            float slX = schX + wx;

                            int cR, cG, cB, cA;
                            switch (flameStage)
                            {
                                case 2:
                                    cR = (int)MathHelper.Lerp(255, 140, t); cG = (int)MathHelper.Lerp(220, 50, t);
                                    cB = (int)MathHelper.Lerp(130, 10, t); cA = (int)MathHelper.Lerp(200, 30, t * t);
                                    if (t < 0.2f) { cR = Math.Min(255, cR + (int)(s2 * 20)); cG = Math.Min(255, cG + (int)(s2 * 15)); }
                                    break;
                                case 1:
                                    if (t < 0.12f) { cR = (int)MathHelper.Lerp(210, 140, t / 0.12f); cG = (int)MathHelper.Lerp(235, 190, t / 0.12f); cB = 255; cA = 210; }
                                    else if (t < 0.45f) { float mt = (t - 0.12f) / 0.33f; cR = (int)MathHelper.Lerp(140, 40, mt); cG = Math.Min(255, (int)MathHelper.Lerp(190, 110, mt) + (int)(s2 * 8)); cB = Math.Min(255, (int)MathHelper.Lerp(255, 220, mt) + (int)(s1 * 6)); cA = (int)MathHelper.Lerp(200, 150, mt); }
                                    else { float mt = (t - 0.45f) / 0.55f; cR = (int)MathHelper.Lerp(40, 15, mt); cG = (int)MathHelper.Lerp(110, 50, mt); cB = (int)MathHelper.Lerp(220, 110, mt); cA = (int)MathHelper.Lerp(150, 20, mt * mt); }
                                    break;
                                default:
                                    cR = (int)MathHelper.Lerp(50, 15, t); cG = (int)MathHelper.Lerp(100, 40, t);
                                    cB = (int)MathHelper.Lerp(180, 80, t); cA = (int)MathHelper.Lerp(120, 15, t * t);
                                    break;
                            }
                            Rect(frame, slX, slY, tw, slH, new Color(cR, cG, cB, cA));
                        }
                    }

                    // Hot core
                    for (int c = 0; c < 4; c++)
                    {
                        float ct = (float)c / 3f;
                        float cs1 = (float)Math.Sin(tick * 0.5 + c * 1.3 + seed);
                        float coreY = exhaTop + basePlumeH * ct * 0.5f + 1f + driftY * ct * ct;
                        float coreX = schX + driftX * ct * ct + cs1 * 0.3f;
                        float coreW = nozzExitW * 0.15f * (1f - ct * 0.6f);
                        float coreH = basePlumeH * 0.15f;
                        int coreA = (int)(180 * (1f - ct));
                        Color coreC = flameStage == 2 ? new Color(255, 240, 180, coreA)
                                    : flameStage == 1 ? new Color(200, 230, 255, coreA)
                                    : new Color(100, 150, 220, (int)(coreA * 0.5f));
                        Rect(frame, coreX, coreY, coreW, coreH, coreC);
                    }

                    // Nozzle glow
                    float glowPulse = (float)Math.Sin(tick * 0.37 + seed) * 0.1f;
                    float glowSize = nozzExitW * (1.0f + glowPulse);
                    Color glowC = flameStage == 2 ? new Color(200, 150, 40, 110)
                                : flameStage == 1 ? new Color(120, 170, 230, 90)
                                : new Color(50, 90, 160, 50);
                    Rect(frame, schX, exhaTop + 1f, glowSize, 2.5f, glowC);
                }

                // ── Thrust bar ──
                float barY = y + availH - 14f;
                float barH2 = 3f;
                Rect(frame, x + w / 2f, barY + barH2 / 2f, w, barH2, MFDTheme.BAR_TRACK);
                SpriteHelpers.DrawRectangleOutline(frame, x, barY, w, barH2, 0.5f, MFDTheme.BORDER);
                float fillW = w * thrustPct;
                if (fillW > 0.5f)
                    Rect(frame, x + fillW / 2f, barY + barH2 / 2f, fillW, barH2, MFDTheme.BAR_FILL);

                string thrTxt = totalMax > 0 ? $"{totalCur:F0}/{totalMax:F0}kN" : "---";
                Txt(frame, thrTxt, x + w, barY + barH2 + 1f, 0.28f, MFDTheme.STATUS_VAL, TextAlignment.RIGHT);
            }

            static void DrawResourceCard(MySpriteDrawFrame frame, float x, float y, float w, float h,
                string title, float pct, string timeStr)
            {
                Rect(frame, x + w / 2f, y + h / 2f, w, h, MFDTheme.PANEL_BG);
                SpriteHelpers.DrawRectangleOutline(frame, x, y, w, h, 1f, MFDTheme.BORDER_LIGHT);

                Txt(frame, title, x + 4f, y + 2f, 0.32f, MFDTheme.DIM_TEXT_MID, TextAlignment.LEFT);
                string pctStr = $"{(int)(pct * 100)}%";
                Txt(frame, pctStr, x + w - 4f, y + 1f, 0.38f, MFDTheme.STATUS_VAL, TextAlignment.RIGHT);

                float barY = y + 14f;
                float barW = w - 8f;
                float barH = 4f;
                float barX = x + 4f;
                Rect(frame, barX + barW / 2f, barY + barH / 2f, barW, barH, MFDTheme.BAR_TRACK);
                SpriteHelpers.DrawRectangleOutline(frame, barX, barY, barW, barH, 0.5f, MFDTheme.BORDER);
                float fillW = barW * MathHelper.Clamp(pct, 0f, 1f);
                if (fillW > 0.5f)
                    Rect(frame, barX + fillW / 2f, barY + barH / 2f, fillW, barH, MFDTheme.BAR_FILL);

                Txt(frame, "REMAIN", barX, barY + barH + 2f, 0.28f, MFDTheme.DIM_TEXT, TextAlignment.LEFT);
                Txt(frame, timeStr, barX + barW, barY + barH + 2f, 0.28f, MFDTheme.STATUS_VAL, TextAlignment.RIGHT);
            }

            static string FormatTime(double totalSeconds)
            {
                if (totalSeconds <= 0) return "---";
                int mins = (int)(totalSeconds / 60);
                int secs = (int)(totalSeconds % 60);
                return $"{mins:D2}:{secs:D2}";
            }

            static void Rect(MySpriteDrawFrame f, float cx, float cy, float w, float h, Color c)
            {
                f.Add(new MySprite { Type = SpriteType.TEXTURE, Data = MFDTheme.SQ,
                    Position = new Vector2(cx, cy), Size = new Vector2(w, h),
                    Color = c, Alignment = TextAlignment.CENTER });
            }

            static void Txt(MySpriteDrawFrame f, string d, float x, float y, float s, Color c, TextAlignment a = TextAlignment.LEFT)
            {
                f.Add(new MySprite { Type = SpriteType.TEXT, Data = d,
                    Position = new Vector2(x, y), RotationOrScale = s,
                    Color = c, Alignment = a, FontId = MFDTheme.FONT });
            }
        }
    }
}
