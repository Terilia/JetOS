using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        partial class HUDModule
        {
            private void DrawSpeedIndicatorF18StyleKph(double currentSpeedKph)
            {
                currentSpeedKph = Mx(0, currentSpeedKph);

                float screenWidth = SX(hud);
                float screenHeight = SY(hud);
                float centerY = screenHeight / 2.25f;

                float tapeLeftMargin = 10f;
                float tapeNumberMargin = 10f;

                float tapeLineX = tapeLeftMargin;
                float digitalSpeedBoxWidth = 80f;
                float digitalSpeedBoxHeight = 30f;
                float digitalSpeedBoxX = tapeLineX + tapeNumberMargin;

                DrawVerticalTape(currentSpeedKph, tapeLineX, centerY,
                    800 / SPEED_KPH_UNITS_PER_TAPE_HEIGHT, SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f,
                    SPEED_TICK_INTERVAL, SPEED_MAJOR_TICK_INTERVAL, 1f, true, false);

                // Semi-transparent background behind speed box
                SpriteHelpers.Bx(digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130, digitalSpeedBoxWidth, digitalSpeedBoxHeight, Cr(0, 0, 0, 128));

                SpriteHelpers.DrawRectangleOutline(digitalSpeedBoxX, centerY - digitalSpeedBoxHeight / 2f - 130, digitalSpeedBoxWidth, digitalSpeedBoxHeight, 1f, HUD_PRIMARY);

                string currentSpeedText = currentSpeedKph.ToString("000");
                SpriteHelpers.Tt(currentSpeedText, digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130 - digitalSpeedBoxHeight / 2f, 0.8f, HUD_PRIMARY);

                // Mach number below speed box
                string machText = $"M {mach:F2}";
                SpriteHelpers.Tt(machText, digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130 + digitalSpeedBoxHeight / 2f + 3f, 0.5f, HUD_SECONDARY);

                // Tape index sprite — points right toward the speed tape selected-value line.
                // Sprite default points left; rotate 180° to point right. Apex offset = +size/4.
                SpriteHelpers.Sp(TEX_TAPE_INDEX, 7f, centerY, 14f, 14f, HUD_PRIMARY, (float)PI);
            }

            private void DrawCompass(double heading)
            {
                float centerX = SX(hud) / 2f;
                float compassY = 40f;
                float compassWidth = SX(hud) * 0.9f;
                float compassHeight = 30f;
                float viewAngle = 90f;
                float halfViewAngle = viewAngle / 2f;
                int increment = 20;

                float headingScale = compassWidth / viewAngle;

                for (int markerHeading = 0; markerHeading < 360; markerHeading += increment)
                {
                    double deltaHeading = ((markerHeading - heading + 540) % 360) - 180;

                    if (deltaHeading >= -halfViewAngle && deltaHeading <= halfViewAngle)
                    {
                        float markerX = centerX + (float)deltaHeading * headingScale;

                        bool isMajorTick = (markerHeading % 90 == 0);

                        float markerLineHeight = isMajorTick ? compassHeight * 0.7f : compassHeight * 0.4f;
                        Color markerColor = isMajorTick ? HUD_SECONDARY : HUD_PRIMARY;

                        SpriteHelpers.Bx(markerX, compassY, 2f, markerLineHeight, markerColor);

                        string label = isMajorTick ? GetCompassDirection(markerHeading) : markerHeading.ToString();
                        SpriteHelpers.Tt(label, markerX, compassY + compassHeight / 2f + 5f, 0.7f, markerColor, MFDTheme.AC, MFDTheme.FONT_W);
                    }
                }

                SpriteHelpers.Sp(TEX_HDG_CHEVRON, centerX, compassY - compassHeight / 2f - 6f, 22f, 22f, HUD_EMPHASIS);

                // Digital heading readout box
                float headingBoxWidth = 50f;
                float headingBoxHeight = 22f;
                float headingBoxY = compassY + compassHeight / 2f + 20f;

                SpriteHelpers.Bx(centerX, headingBoxY + headingBoxHeight / 2f, headingBoxWidth, headingBoxHeight, Cr(0, 0, 0, 128));
                SpriteHelpers.DrawRectangleOutline(centerX - headingBoxWidth / 2f, headingBoxY,
                    headingBoxWidth, headingBoxHeight, 1f, HUD_PRIMARY);

                string headingText = ((int)((heading % 360 + 360) % 360)).ToString("D3");
                SpriteHelpers.Tt(headingText, centerX, headingBoxY + 1f, 0.65f, HUD_PRIMARY);
            }

            private string GetCompassDirection(double heading)
            {
                if (heading >= 337.5 || heading < 22.5) return "N";
                else if (heading >= 22.5 && heading < 67.5) return "NE";
                else if (heading >= 67.5 && heading < 112.5) return "E";
                else if (heading >= 112.5 && heading < 157.5) return "SE";
                else if (heading >= 157.5 && heading < 202.5) return "S";
                else if (heading >= 202.5 && heading < 247.5) return "SW";
                else if (heading >= 247.5 && heading < 292.5) return "W";
                else return "NW";
            }

            private void DrawAltitudeIndicatorF18Style(double currentAltitude, double displayVerticalVelocity)
            {
                // VVI from gravity-projected velocity (computed in UpdateFlightData)
                double verticalVelocity = displayVerticalVelocity;

                float screenWidth = SX(hud);
                float screenHeight = SY(hud);
                float centerY = screenHeight / 2f;

                float tapeRightMargin = 10f;
                float tapeNumberMargin = 10f;

                float tapeLineX = screenWidth - tapeRightMargin;
                float digitalAltBoxWidth = 80f;
                float digitalAltBoxHeight = 30f;
                float digitalAltBoxX = tapeLineX - tapeNumberMargin - digitalAltBoxWidth;

                DrawVerticalTape(currentAltitude, tapeLineX, centerY,
                    PIXELS_PER_ALTITUDE_UNIT, ALTITUDE_UNITS_PER_TAPE_HEIGHT / 2f,
                    TICK_INTERVAL, MAJOR_TICK_INTERVAL, -1f, false, true);

                // Semi-transparent background behind altitude box
                float altBoxTopLeftX = digitalAltBoxX - 20;
                float altBoxTopLeftY = centerY - digitalAltBoxHeight - 225 / 2f;
                SpriteHelpers.Bx(altBoxTopLeftX + digitalAltBoxWidth / 2f, altBoxTopLeftY + digitalAltBoxHeight / 2f, digitalAltBoxWidth, digitalAltBoxHeight, Cr(0, 0, 0, 128));

                SpriteHelpers.DrawRectangleOutline(altBoxTopLeftX, altBoxTopLeftY, digitalAltBoxWidth, digitalAltBoxHeight, 1f, HUD_PRIMARY);

                string currentAltitudeText = currentAltitude.ToString("0000");
                SpriteHelpers.Tt(currentAltitudeText, digitalAltBoxX - 20 + digitalAltBoxWidth / 2f, centerY - 140, 0.8f, HUD_PRIMARY);

                // Tape index sprite (left-pointing by default) — points left toward the altitude tape.
                SpriteHelpers.Sp(TEX_TAPE_INDEX, screenWidth - 7f, centerY, 14f, 14f, HUD_PRIMARY);

                // VVI (Vertical Velocity Indicator) below altitude box
                Color vviColor = Ab(verticalVelocity) > 30 ? HUD_EMPHASIS : HUD_PRIMARY;
                string vviArrow = verticalVelocity > 1 ? "\u25B2" : verticalVelocity < -1 ? "\u25BC" : "\u25C6";
                string vviText = $"{vviArrow} {verticalVelocity,4:F0}";
                SpriteHelpers.Tt(vviText, digitalAltBoxX - 20 + digitalAltBoxWidth / 2f, altBoxTopLeftY + digitalAltBoxHeight + 5f, 0.5f, vviColor);
            }

            private void DrawVerticalTape(double value, float lineX, float centerY,
                float pixelsPerUnit, float halfSpan, float tickInterval, float majorInterval,
                float side, bool clampBottom, bool hideNegativeTicks)
            {
                SpriteHelpers.Bx(lineX, centerY, 2f, TAPE_HEIGHT_PIXELS, HUD_PRIMARY);
                float top = (float)value + halfSpan;
                float bottom = (float)value - halfSpan;
                if (clampBottom) bottom = Mx(0, bottom);
                float start = (float)(Math.Floor(bottom / tickInterval) * tickInterval);
                if (start < bottom) start += tickInterval;
                if (clampBottom) start = Mx(0, start);
                float tapeTopY = centerY - TAPE_HEIGHT_PIXELS / 2f;
                float tapeBottomY = centerY + TAPE_HEIGHT_PIXELS / 2f;

                for (float mark = start; mark <= top + tickInterval * 0.5f; mark += tickInterval)
                {
                    if (clampBottom && mark < 0) continue;
                    float yPos = centerY + (float)(value - mark) * pixelsPerUnit;
                    if (yPos < tapeTopY - 1f || yPos > tapeBottomY + 1f) continue;
                    bool isMajorTick = Ab(mark % majorInterval) < tickInterval * 0.1f;
                    if (clampBottom && Ab(mark) < tickInterval * 0.1f) isMajorTick = true;
                    float tickLength = isMajorTick ? 15f : 10f;
                    if (!hideNegativeTicks || mark >= 0)
                        SpriteHelpers.Bx(lineX + side * tickLength / 2f, yPos, tickLength, 2f, HUD_PRIMARY);
                    if (isMajorTick)
                        SpriteHelpers.Tt(mark.ToString("F0"), lineX + side * (tickLength + 10f),
                            yPos - 7.5f, 0.5f, HUD_PRIMARY, side > 0 ? MFDTheme.AL : MFDTheme.AR);
                }
            }

            private void DrawGForceIndicator(double gForces, double peakGForce)
            {
                const float PADDING = 10f;
                const float TEXT_SCALE = 0.8f;
                const float LINE_HEIGHT = 20f;

                string gForceText = $"G: {gForces:F1}";
                SpriteHelpers.Tt(gForceText, PADDING, SY(hud) - PADDING - LINE_HEIGHT, TEXT_SCALE, HUD_PRIMARY, MFDTheme.AL, MFDTheme.FONT_W);

                string peakGText = $"Max G: {peakGForce:F1}";
                SpriteHelpers.Tt(peakGText, PADDING, SY(hud) - PADDING - LINE_HEIGHT * 2, TEXT_SCALE, HUD_PRIMARY, MFDTheme.AL, MFDTheme.FONT_W);
            }

            private void DrawAOAIndexer(double aoa, Vector3D acceleration, double velocity)
            {
                const float INDEXER_X = 100f;
                float indexerY = SY(hud) / 2f;
                const float SYMBOL_SIZE = 18f;

                const double OPTIMAL_AOA_MIN = 8.0;
                const double OPTIMAL_AOA_MAX = 15.0;

                // Calculate stall percentage (using absolute AoA)
                double absAoA = Ab(aoa);
                double stallPercent = absAoA / STALL_AOA;

                // Determine stall warning level
                int currentStallLevel = STALL_LEVEL_NORMAL;

                if (stallPercent >= 1.0)
                    currentStallLevel = STALL_LEVEL_STALL;
                else if (stallPercent >= STALL_WARNING_PERCENT)
                    currentStallLevel = STALL_LEVEL_WARNING;
                else if (stallPercent >= STALL_CAUTION_PERCENT)
                    currentStallLevel = STALL_LEVEL_CAUTION;

                // Low airspeed makes stall more dangerous - lower thresholds when slow
                // Below 100 m/s, reduce stall threshold proportionally
                if (velocity < 100 && velocity > 1)
                {
                    double speedFactor = velocity / 100.0;
                    double adjustedStallAoA = STALL_AOA * speedFactor;
                    double adjustedStallPercent = absAoA / Mx(adjustedStallAoA, 5.0);

                    if (adjustedStallPercent >= 1.0)
                        currentStallLevel = STALL_LEVEL_STALL;
                    else if (adjustedStallPercent >= STALL_WARNING_PERCENT && currentStallLevel < STALL_LEVEL_WARNING)
                        currentStallLevel = STALL_LEVEL_WARNING;
                    else if (adjustedStallPercent >= STALL_CAUTION_PERCENT && currentStallLevel < STALL_LEVEL_CAUTION)
                        currentStallLevel = STALL_LEVEL_CAUTION;
                }

                Color indexerColor;
                string spriteType;

                if (aoa < OPTIMAL_AOA_MIN)
                {
                    indexerColor = HUD_PRIMARY;
                    spriteType = TEXTURE_TRIANGLE;
                    SpriteHelpers.Sp(spriteType, INDEXER_X, indexerY, SYMBOL_SIZE, SYMBOL_SIZE, indexerColor);
                }
                else if (aoa > OPTIMAL_AOA_MAX)
                {
                    // Override color based on stall level
                    if (currentStallLevel == STALL_LEVEL_STALL)
                        indexerColor = HUD_WARNING;
                    else if (currentStallLevel == STALL_LEVEL_WARNING)
                        indexerColor = Cr(255, 128, 0); // Orange
                    else if (currentStallLevel == STALL_LEVEL_CAUTION)
                        indexerColor = HUD_EMPHASIS;
                    else
                        indexerColor = HUD_WARNING;

                    spriteType = TEXTURE_TRIANGLE;
                    SpriteHelpers.Sp(spriteType, INDEXER_X, indexerY, SYMBOL_SIZE, SYMBOL_SIZE, indexerColor, MathHelper.Pi);
                }
                else
                {
                    indexerColor = HUD_EMPHASIS;
                    SpriteHelpers.Sp(TEXTURE_CIRCLE_SOLID, INDEXER_X, indexerY, SYMBOL_SIZE * 0.8f, SYMBOL_SIZE * 0.8f, indexerColor);
                }

                // Draw stall warning indicators
                if (currentStallLevel != STALL_LEVEL_NORMAL)
                {
                    DrawStallWarning(currentStallLevel, absAoA);
                }

                double energyRate = acceleration.Length();
                string energySymbol = energyRate > 5 ? "+" : energyRate < -5 ? "-" : "=";
                Color energyColor = energyRate > 5 ? HUD_PRIMARY : energyRate < -5 ? HUD_WARNING : HUD_EMPHASIS;

                SpriteHelpers.Tt($"E{energySymbol}", INDEXER_X, indexerY + 25f, 0.5f, energyColor);
            }

            private void DrawStallWarning(int level, double currentAoA)
            {

                Vector2 center = SS(hud) / 2f;
                float textY = center.Y - 80f;

                Color warningColor;
                string warningText;
                float textScale;
                bool flash = false;

                switch (level)
                {
                    case 1: // Caution
                        warningColor = HUD_EMPHASIS;
                        warningText = "AOA";
                        textScale = 0.8f;
                        break;
                    case 2: // Warning
                        warningColor = Cr(255, 128, 0); // Orange
                        warningText = "HIGH AOA";
                        textScale = 0.9f;
                        flash = Anim.Blink(0.33);
                        break;
                    case 3: // Stall
                        warningColor = HUD_WARNING;
                        warningText = "STALL";
                        textScale = 1.2f;
                        flash = Anim.Blink(0.17);
                        break;
                    default:
                        return;
                }

                if (level < 3 || flash) // Always show for caution/warning, flash for stall
                {
                    // Warning text
                    SpriteHelpers.Tt(warningText, center.X, textY, textScale, warningColor, MFDTheme.AC, MFDTheme.FONT_W);

                    // AoA value
                    string aoaText = $"{currentAoA:F1}\u00B0";
                    SpriteHelpers.Tt(aoaText, center.X, textY + 25f, 0.7f, warningColor);
                }

                // AoA bracket sprite (E-shape — spine + 3 arms). Visible content
                // spans ~44% of canvas width, 50% height, so size 80 → 35×40 visible
                // sitting just left of the AoA indexer at INDEXER_X=100.
                if (level >= 2 && (flash || level < 3))
                {
                    SpriteHelpers.Sp(TEX_AOA_BRACKET, 75f, SY(hud) / 2f, 80f, 80f, warningColor);
                }
            }

            private void DrawLeftInfoBox(
                float centerX,
                float centerY,
                params LabelValue[] extraValues
            )
            {
                const float Y_OFFSET_PER_VALUE = 30f;
                const float X_OFFSET_FACTOR = 0.75f;
                const float Y_OFFSET_FACTOR = 0.5f;
                const float LABEL_COLUMN_OFFSET = 40f;
                const float NUMBER_COLUMN_OFFSET = 40f;
                const float TEXT_SCALE = 0.75f;

                float xoffset = centerX - centerX * X_OFFSET_FACTOR;
                float yoffset = centerY - centerY * Y_OFFSET_FACTOR;
                float labelColumnX = xoffset - LABEL_COLUMN_OFFSET;
                float numberColumnX = xoffset + NUMBER_COLUMN_OFFSET;

                for (int i = 0; i < extraValues.Length; i++)
                {
                    string labelText = extraValues[i].Label;
                    double numericValue = extraValues[i].Value;

                    SpriteHelpers.Tt(labelText, labelColumnX, yoffset + i * Y_OFFSET_PER_VALUE, TEXT_SCALE, HUD_PRIMARY, MFDTheme.AL, MFDTheme.FONT_W);
                    SpriteHelpers.Tt(numericValue.ToString("F1"), numberColumnX, yoffset + i * Y_OFFSET_PER_VALUE, TEXT_SCALE, HUD_PRIMARY, MFDTheme.AR, MFDTheme.FONT_W);
                }
            }

            private void DrawFlightInfo(
                double throttle
            )
            {
                float t = (float)throttle / 100f;

                const float BAR_W = 14f;
                const float BAR_H = 100f;
                const float BORDER = 1.5f;

                float barX = 5f;
                float barY = SY(hud) - 170f;
                float cx = barX + BAR_W / 2f;

                // Track outline
                SpriteHelpers.DrawRectangleOutline(barX, barY, BAR_W, BAR_H, BORDER, HUD_PRIMARY);

                // Fill from bottom
                float fillH = BAR_H * t;
                if (fillH > 1f)
                {
                    Color fillColor = hydrogenswitch ? HUD_EMPHASIS : HUD_PRIMARY;
                    SpriteHelpers.Bx(cx, barY + BAR_H - fillH / 2f, BAR_W - 2f, fillH, fillColor);
                }

                // MIL gate marker at 80%
                float milY = barY + BAR_H * (1f - THROTTLE_HYDROGEN_THRESHOLD);
                SpriteHelpers.Bx(cx, milY, BAR_W + 4f, 1.5f, HUD_EMPHASIS);
            }

        }
    }
}
