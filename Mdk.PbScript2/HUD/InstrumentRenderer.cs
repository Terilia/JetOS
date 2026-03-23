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
            private void DrawSpeedIndicatorF18StyleKph(MySpriteDrawFrame frame, double currentSpeedKph)
            {
                currentSpeedKph = Mx(0, currentSpeedKph);
                const float PIXELS_PER_SPEED_UNIT = 800 / SPEED_KPH_UNITS_PER_TAPE_HEIGHT;

                float screenWidth = hud.SurfaceSize.X;
                float screenHeight = hud.SurfaceSize.Y;
                float centerY = screenHeight / 2.25f;

                float tapeLeftMargin = 10f;
                float tapeNumberMargin = 10f;
                float tapeWidth = 2f;
                float tickLength = 10f;
                float majorTickLength = 15f;

                float tapeLineX = tapeLeftMargin;
                float digitalSpeedBoxWidth = 80f;
                float digitalSpeedBoxHeight = 30f;
                float digitalSpeedBoxX = tapeLineX + tapeNumberMargin;

                SpriteHelpers.Bx(frame, tapeLineX, centerY, tapeWidth, TAPE_HEIGHT_PIXELS, HUD_PRIMARY);

                float tapeTopSpeed = (float)currentSpeedKph + (SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f);
                float tapeBottomSpeed = (float)currentSpeedKph - (SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f);
                tapeBottomSpeed = Mx(0, tapeBottomSpeed);

                float startTickSpeed = (float)(Math.Floor(tapeBottomSpeed / SPEED_TICK_INTERVAL) * SPEED_TICK_INTERVAL);
                if (startTickSpeed < tapeBottomSpeed)
                    startTickSpeed += SPEED_TICK_INTERVAL;
                startTickSpeed = Mx(0, startTickSpeed);

                for (float speedMark = startTickSpeed; speedMark <= tapeTopSpeed + (SPEED_TICK_INTERVAL * 0.5f); speedMark += SPEED_TICK_INTERVAL)
                {
                    if (speedMark < 0) continue;

                    float yOffset = (float)(currentSpeedKph - speedMark) * PIXELS_PER_SPEED_UNIT;
                    float yPos = centerY + yOffset;

                    float tapeTopY = centerY - TAPE_HEIGHT_PIXELS / 2f;
                    float tapeBottomY = centerY + TAPE_HEIGHT_PIXELS / 2f;

                    if (yPos >= tapeTopY - 1f && yPos <= tapeBottomY + 1f)
                    {
                        bool isMajorTick = Ab(speedMark % SPEED_MAJOR_TICK_INTERVAL) < (SPEED_TICK_INTERVAL * 0.1f);
                        if (Ab(speedMark) < (SPEED_TICK_INTERVAL * 0.1f)) isMajorTick = true;

                        float currentTickLength = isMajorTick ? majorTickLength : tickLength;

                        SpriteHelpers.Bx(frame, tapeLineX + currentTickLength / 2f, yPos, currentTickLength, tapeWidth, HUD_PRIMARY);

                        if (isMajorTick)
                        {
                            string speedText = speedMark.ToString("F0");
                            SpriteHelpers.Tt(frame, speedText, tapeLineX + currentTickLength + tapeNumberMargin, yPos - 7.5f, 0.5f, HUD_PRIMARY, MFDTheme.AL);
                        }
                    }
                }

                // Semi-transparent background behind speed box
                SpriteHelpers.Bx(frame, digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130, digitalSpeedBoxWidth, digitalSpeedBoxHeight, Cr(0, 0, 0, 128));

                SpriteHelpers.DrawRectangleOutline(frame, digitalSpeedBoxX, centerY - digitalSpeedBoxHeight / 2f - 130, digitalSpeedBoxWidth, digitalSpeedBoxHeight, 1f, HUD_PRIMARY);

                string currentSpeedText = currentSpeedKph.ToString("F0");
                SpriteHelpers.Tt(frame, currentSpeedText, digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130 - digitalSpeedBoxHeight / 2f, 0.8f, HUD_PRIMARY);

                // Mach number below speed box
                string machText = $"M {mach:F2}";
                SpriteHelpers.Tt(frame, machText, digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130 + digitalSpeedBoxHeight / 2f + 3f, 0.5f, HUD_SECONDARY);

                SpriteHelpers.Tt(frame, ">", digitalSpeedBoxX - 10f, centerY - 7.5f, 0.5f, HUD_PRIMARY, MFDTheme.AR);
            }

            private void DrawCompass(MySpriteDrawFrame frame, double heading)
            {
                float centerX = hud.SurfaceSize.X / 2f;
                float compassY = 40f;
                float compassWidth = hud.SurfaceSize.X * 0.9f;
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

                        SpriteHelpers.Bx(frame, markerX, compassY, 2f, markerLineHeight, markerColor);

                        string label = isMajorTick ? GetCompassDirection(markerHeading) : markerHeading.ToString();
                        SpriteHelpers.Tt(frame, label, markerX, compassY + compassHeight / 2f + 5f, 0.7f, markerColor, MFDTheme.AC, MFDTheme.FONT_W);
                    }
                }

                SpriteHelpers.Sp(frame, "Triangle", centerX, compassY - compassHeight / 2f - 6f, 12f, 10f, HUD_EMPHASIS, (float)PI);

                // Digital heading readout box
                float headingBoxWidth = 50f;
                float headingBoxHeight = 22f;
                float headingBoxY = compassY + compassHeight / 2f + 20f;

                SpriteHelpers.Bx(frame, centerX, headingBoxY + headingBoxHeight / 2f, headingBoxWidth, headingBoxHeight, Cr(0, 0, 0, 128));
                SpriteHelpers.DrawRectangleOutline(frame, centerX - headingBoxWidth / 2f, headingBoxY,
                    headingBoxWidth, headingBoxHeight, 1f, HUD_PRIMARY);

                string headingText = ((int)((heading % 360 + 360) % 360)).ToString("D3");
                SpriteHelpers.Tt(frame, headingText, centerX, headingBoxY + 1f, 0.65f, HUD_PRIMARY);
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

            private void DrawAltitudeIndicatorF18Style(MySpriteDrawFrame frame, double currentAltitude, TimeSpan currentTime)
            {
                // VVI from gravity-projected velocity (computed in UpdateFlightData)
                double verticalVelocity = verticalVelocityMps;

                float screenWidth = hud.SurfaceSize.X;
                float screenHeight = hud.SurfaceSize.Y;
                float centerY = screenHeight / 2f;

                float tapeRightMargin = 10f;
                float tapeNumberMargin = 10f;
                float tapeWidth = 2f;
                float tickLength = 10f;
                float majorTickLength = 15f;

                float tapeLineX = screenWidth - tapeRightMargin;
                float digitalAltBoxWidth = 80f;
                float digitalAltBoxHeight = 30f;
                float digitalAltBoxX = tapeLineX - tapeNumberMargin - digitalAltBoxWidth;

                SpriteHelpers.Bx(frame, tapeLineX, centerY, tapeWidth, TAPE_HEIGHT_PIXELS, HUD_PRIMARY);

                float tapeTopAlt = (float)currentAltitude + (ALTITUDE_UNITS_PER_TAPE_HEIGHT / 2f);
                float tapeBottomAlt = (float)currentAltitude - (ALTITUDE_UNITS_PER_TAPE_HEIGHT / 2f);

                float startTickAlt = (float)(Math.Floor(tapeBottomAlt / TICK_INTERVAL) * TICK_INTERVAL);
                if (startTickAlt < tapeBottomAlt)
                    startTickAlt += TICK_INTERVAL;

                for (float altMark = startTickAlt; altMark <= tapeTopAlt + (TICK_INTERVAL * 0.5f); altMark += TICK_INTERVAL)
                {
                    float yOffset = (float)(currentAltitude - altMark) * PIXELS_PER_ALTITUDE_UNIT;
                    float yPos = centerY + yOffset;

                    float tapeTopY = centerY - TAPE_HEIGHT_PIXELS / 2f;
                    float tapeBottomY = centerY + TAPE_HEIGHT_PIXELS / 2f;

                    if (yPos >= tapeTopY - 1f && yPos <= tapeBottomY + 1f)
                    {
                        bool isMajorTick = Ab(altMark % MAJOR_TICK_INTERVAL) < (TICK_INTERVAL * 0.1f);
                        float currentTickLength = isMajorTick ? majorTickLength : tickLength;
                        if (altMark >= 0)
                        {
                            SpriteHelpers.Bx(frame, tapeLineX - currentTickLength / 2f, yPos, currentTickLength, tapeWidth, HUD_PRIMARY);
                        }

                        if (isMajorTick)
                        {
                            string altText = altMark.ToString("F0");
                            SpriteHelpers.Tt(frame, altText, tapeLineX - currentTickLength - tapeNumberMargin, yPos - 7.5f, 0.5f, HUD_PRIMARY, MFDTheme.AR);
                        }
                    }
                }

                // Semi-transparent background behind altitude box
                float altBoxTopLeftX = digitalAltBoxX - 20;
                float altBoxTopLeftY = centerY - digitalAltBoxHeight - 225 / 2f;
                SpriteHelpers.Bx(frame, altBoxTopLeftX + digitalAltBoxWidth / 2f, altBoxTopLeftY + digitalAltBoxHeight / 2f, digitalAltBoxWidth, digitalAltBoxHeight, Cr(0, 0, 0, 128));

                SpriteHelpers.DrawRectangleOutline(frame, altBoxTopLeftX, altBoxTopLeftY, digitalAltBoxWidth, digitalAltBoxHeight, 1f, HUD_PRIMARY);

                string currentAltitudeText = currentAltitude.ToString("F0");
                SpriteHelpers.Tt(frame, currentAltitudeText, digitalAltBoxX - 20 + digitalAltBoxWidth / 2f, centerY - 140, 0.8f, HUD_PRIMARY);

                SpriteHelpers.Tt(frame, "<", digitalAltBoxX + digitalAltBoxWidth + 15f, centerY - 7.5f, 0.5f, HUD_PRIMARY, MFDTheme.AL);

                // VVI (Vertical Velocity Indicator) below altitude box
                Color vviColor = Ab(verticalVelocity) > 30 ? HUD_EMPHASIS : HUD_PRIMARY;
                string vviArrow = verticalVelocity > 1 ? "\u25B2" : verticalVelocity < -1 ? "\u25BC" : "\u25C6";
                string vviText = $"{vviArrow} {verticalVelocity:F0}";
                SpriteHelpers.Tt(frame, vviText, digitalAltBoxX - 20 + digitalAltBoxWidth / 2f, altBoxTopLeftY + digitalAltBoxHeight + 5f, 0.5f, vviColor);
            }

            private void DrawGForceIndicator(MySpriteDrawFrame frame, double gForces, double peakGForce)
            {
                const float PADDING = 10f;
                const float TEXT_SCALE = 0.8f;
                const float LINE_HEIGHT = 20f;

                string gForceText = $"G: {gForces:F1}";
                SpriteHelpers.Tt(frame, gForceText, PADDING, hud.SurfaceSize.Y - PADDING - LINE_HEIGHT, TEXT_SCALE, HUD_PRIMARY, MFDTheme.AL, MFDTheme.FONT_W);

                string peakGText = $"Max G: {peakGForce:F1}";
                SpriteHelpers.Tt(frame, peakGText, PADDING, hud.SurfaceSize.Y - PADDING - LINE_HEIGHT * 2, TEXT_SCALE, HUD_PRIMARY, MFDTheme.AL, MFDTheme.FONT_W);
            }

            private void DrawAOAIndexer(MySpriteDrawFrame frame, double aoa, Vector3D acceleration, double velocity)
            {
                const float INDEXER_X = 100f;
                float indexerY = hud.SurfaceSize.Y / 2f;
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
                    spriteType = "Triangle";
                    SpriteHelpers.Sp(frame, spriteType, INDEXER_X, indexerY, SYMBOL_SIZE, SYMBOL_SIZE, indexerColor);
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

                    spriteType = "Triangle";
                    SpriteHelpers.Sp(frame, spriteType, INDEXER_X, indexerY, SYMBOL_SIZE, SYMBOL_SIZE, indexerColor, MathHelper.Pi);
                }
                else
                {
                    indexerColor = HUD_EMPHASIS;
                    SpriteHelpers.Sp(frame, TEXTURE_CIRCLE_SOLID, INDEXER_X, indexerY, SYMBOL_SIZE * 0.8f, SYMBOL_SIZE * 0.8f, indexerColor);
                }

                // Draw stall warning indicators
                if (currentStallLevel != STALL_LEVEL_NORMAL)
                {
                    DrawStallWarning(frame, currentStallLevel, absAoA);
                }

                // Update stall warning state for sound system
                stallWarningActive = currentStallLevel == STALL_LEVEL_STALL;

                double energyRate = acceleration.Length();
                string energySymbol = energyRate > 5 ? "+" : energyRate < -5 ? "-" : "=";
                Color energyColor = energyRate > 5 ? HUD_PRIMARY : energyRate < -5 ? HUD_WARNING : HUD_EMPHASIS;

                SpriteHelpers.Tt(frame, $"E{energySymbol}", INDEXER_X, indexerY + 25f, 0.5f, energyColor);
            }

            private void DrawStallWarning(MySpriteDrawFrame frame, int level, double currentAoA)
            {

                Vector2 center = hud.SurfaceSize / 2f;
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
                        flash = (radarSweepTick / 10) % 2 == 0;
                        break;
                    case 3: // Stall
                        warningColor = HUD_WARNING;
                        warningText = "STALL";
                        textScale = 1.2f;
                        flash = (radarSweepTick / 5) % 2 == 0;
                        break;
                    default:
                        return;
                }

                if (level < 3 || flash) // Always show for caution/warning, flash for stall
                {
                    // Warning text
                    SpriteHelpers.Tt(frame, warningText, center.X, textY, textScale, warningColor, MFDTheme.AC, MFDTheme.FONT_W);

                    // AoA value
                    string aoaText = $"{currentAoA:F1}\u00B0";
                    SpriteHelpers.Tt(frame, aoaText, center.X, textY + 25f, 0.7f, warningColor);
                }

                // Draw AoA bracket highlights for stall
                if (level >= 2)
                {
                    float bracketX = 100f;
                    float bracketY = hud.SurfaceSize.Y / 2f - 30f;
                    float bracketHeight = 60f;

                    if (flash || level < 3)
                    {
                        // Left bracket
                        SpriteHelpers.AddLineSprite(frame, V2(bracketX - 15f, bracketY),
                                          V2(bracketX - 15f, bracketY + bracketHeight), 3f, warningColor);
                        SpriteHelpers.AddLineSprite(frame, V2(bracketX - 15f, bracketY),
                                          V2(bracketX - 5f, bracketY), 3f, warningColor);
                        SpriteHelpers.AddLineSprite(frame, V2(bracketX - 15f, bracketY + bracketHeight),
                                          V2(bracketX - 5f, bracketY + bracketHeight), 3f, warningColor);
                    }
                }
            }

            private void DrawLeftInfoBox(
                MySpriteDrawFrame frame,
                double airspeed,
                float centerX,
                float centerY,
                double pixelsPerDegree,
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

                    SpriteHelpers.Tt(frame, labelText, labelColumnX, yoffset + i * Y_OFFSET_PER_VALUE, TEXT_SCALE, HUD_PRIMARY, MFDTheme.AL, MFDTheme.FONT_W);
                    SpriteHelpers.Tt(frame, numericValue.ToString("F1"), numberColumnX, yoffset + i * Y_OFFSET_PER_VALUE, TEXT_SCALE, HUD_PRIMARY, MFDTheme.AR, MFDTheme.FONT_W);
                }
            }

            private void DrawFlightInfo(
                MySpriteDrawFrame frame,
                double throttle
            )
            {
                float t = (float)throttle / 100f;

                const float BAR_W = 14f;
                const float BAR_H = 100f;
                const float BORDER = 1.5f;

                float barX = 5f;
                float barY = hud.SurfaceSize.Y - 170f;
                float cx = barX + BAR_W / 2f;

                // Track outline
                SpriteHelpers.DrawRectangleOutline(frame, barX, barY, BAR_W, BAR_H, BORDER, HUD_PRIMARY);

                // Fill from bottom
                float fillH = BAR_H * t;
                if (fillH > 1f)
                {
                    Color fillColor = hydrogenswitch ? HUD_EMPHASIS : HUD_PRIMARY;
                    SpriteHelpers.Bx(frame, cx, barY + BAR_H - fillH / 2f, BAR_W - 2f, fillH, fillColor);
                }

                // MIL gate marker at 80%
                float milY = barY + BAR_H * (1f - THROTTLE_HYDROGEN_THRESHOLD);
                SpriteHelpers.Bx(frame, cx, milY, BAR_W + 4f, 1.5f, HUD_EMPHASIS);
            }

        }
    }
}
