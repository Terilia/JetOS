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
                currentSpeedKph = Math.Max(0, currentSpeedKph);
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

                var tapeLine = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(tapeLineX, centerY),
                    Size = new Vector2(tapeWidth, TAPE_HEIGHT_PIXELS),
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(tapeLine);

                float tapeTopSpeed = (float)currentSpeedKph + (SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f);
                float tapeBottomSpeed = (float)currentSpeedKph - (SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f);
                tapeBottomSpeed = Math.Max(0, tapeBottomSpeed);

                float startTickSpeed = (float)(Math.Floor(tapeBottomSpeed / SPEED_TICK_INTERVAL) * SPEED_TICK_INTERVAL);
                if (startTickSpeed < tapeBottomSpeed)
                    startTickSpeed += SPEED_TICK_INTERVAL;
                startTickSpeed = Math.Max(0, startTickSpeed);

                for (float speedMark = startTickSpeed; speedMark <= tapeTopSpeed + (SPEED_TICK_INTERVAL * 0.5f); speedMark += SPEED_TICK_INTERVAL)
                {
                    if (speedMark < 0) continue;

                    float yOffset = (float)(currentSpeedKph - speedMark) * PIXELS_PER_SPEED_UNIT;
                    float yPos = centerY + yOffset;

                    float tapeTopY = centerY - TAPE_HEIGHT_PIXELS / 2f;
                    float tapeBottomY = centerY + TAPE_HEIGHT_PIXELS / 2f;

                    if (yPos >= tapeTopY - 1f && yPos <= tapeBottomY + 1f)
                    {
                        bool isMajorTick = Math.Abs(speedMark % SPEED_MAJOR_TICK_INTERVAL) < (SPEED_TICK_INTERVAL * 0.1f);
                        if (Math.Abs(speedMark) < (SPEED_TICK_INTERVAL * 0.1f)) isMajorTick = true;

                        float currentTickLength = isMajorTick ? majorTickLength : tickLength;

                        var tickMark = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(tapeLineX + currentTickLength / 2f, yPos),
                            Size = new Vector2(currentTickLength, tapeWidth),
                            Color = HUD_PRIMARY,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(tickMark);

                        if (isMajorTick)
                        {
                            string speedText = speedMark.ToString("F0");
                            var numberLabel = new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = speedText,
                                Position = new Vector2(tapeLineX + currentTickLength + tapeNumberMargin, yPos - 7.5f),
                                RotationOrScale = 0.5f,
                                Color = HUD_PRIMARY,
                                Alignment = TextAlignment.LEFT,
                                FontId = FONT
                            };
                            frame.Add(numberLabel);
                        }
                    }
                }

                SpriteHelpers.DrawRectangleOutline(frame, digitalSpeedBoxX, centerY - digitalSpeedBoxHeight / 2f - 130, digitalSpeedBoxWidth, digitalSpeedBoxHeight, 1f, HUD_PRIMARY);

                string currentSpeedText = currentSpeedKph.ToString("F0");
                var speedLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = currentSpeedText,
                    Position = new Vector2(digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130 - digitalSpeedBoxHeight / 2f),
                    RotationOrScale = 0.8f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER,
                    FontId = FONT
                };
                frame.Add(speedLabel);

                var caret = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = ">",
                    Position = new Vector2(digitalSpeedBoxX - 10f, centerY - 7.5f),
                    RotationOrScale = 0.5f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.RIGHT,
                    FontId = FONT
                };
                frame.Add(caret);
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

                        var markerLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(markerX, compassY),
                            Size = new Vector2(2f, markerLineHeight),
                            Color = markerColor,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(markerLine);

                        string label = isMajorTick ? GetCompassDirection(markerHeading) : markerHeading.ToString();
                        float textScale = 0.7f;

                        var markerText = new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            Position = new Vector2(markerX, compassY + compassHeight / 2f + 5f),
                            RotationOrScale = textScale,
                            Color = markerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        };
                        frame.Add(markerText);
                    }
                }

                var headingIndicator = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Triangle",
                    Position = new Vector2(centerX, compassY - compassHeight / 2f - 6f),
                    Size = new Vector2(12f, 10f),
                    Color = HUD_EMPHASIS,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = (float)Math.PI
                };
                frame.Add(headingIndicator);
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
                UpdateAltitudeHistory(currentAltitude, currentTime);
                double verticalVelocity = CalculateVerticalVelocity(currentAltitude, currentTime);

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

                var tapeLine = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(tapeLineX, centerY),
                    Size = new Vector2(tapeWidth, TAPE_HEIGHT_PIXELS),
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(tapeLine);

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
                        bool isMajorTick = Math.Abs(altMark % MAJOR_TICK_INTERVAL) < (TICK_INTERVAL * 0.1f);
                        float currentTickLength = isMajorTick ? majorTickLength : tickLength;
                        if (altMark >= 0)
                        {
                            var tickMark = new MySprite()
                            {
                                Type = SpriteType.TEXTURE,
                                Data = "SquareSimple",
                                Position = new Vector2(tapeLineX - currentTickLength / 2f, yPos),
                                Size = new Vector2(currentTickLength, tapeWidth),
                                Color = HUD_PRIMARY,
                                Alignment = TextAlignment.CENTER
                            };
                            frame.Add(tickMark);
                        }

                        if (isMajorTick)
                        {
                            string altText = altMark.ToString("F0");
                            var numberLabel = new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = altText,
                                Position = new Vector2(tapeLineX - currentTickLength - tapeNumberMargin, yPos - 7.5f),
                                RotationOrScale = 0.5f,
                                Color = HUD_PRIMARY,
                                Alignment = TextAlignment.RIGHT,
                                FontId = FONT
                            };
                            frame.Add(numberLabel);
                        }
                    }
                }

                SpriteHelpers.DrawRectangleOutline(frame, digitalAltBoxX - 20, centerY - digitalAltBoxHeight - 225 / 2f, digitalAltBoxWidth, digitalAltBoxHeight, 1f, HUD_PRIMARY);

                string currentAltitudeText = currentAltitude.ToString("F0");
                var altitudeLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = currentAltitudeText,
                    Position = new Vector2(digitalAltBoxX - 20 + digitalAltBoxWidth / 2f, centerY - 140),
                    RotationOrScale = 0.8f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER,
                    FontId = FONT
                };
                frame.Add(altitudeLabel);

                var caret = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "<",
                    Position = new Vector2(digitalAltBoxX + digitalAltBoxWidth + 15f, centerY - 7.5f),
                    RotationOrScale = 0.5f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = FONT
                };
                frame.Add(caret);
            }

            private void DrawGForceIndicator(MySpriteDrawFrame frame, double gForces, double peakGForce)
            {
                const float PADDING = 10f;
                const float TEXT_SCALE = 0.8f;
                const float LINE_HEIGHT = 20f;

                string gForceText = $"G: {gForces:F1}";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = gForceText,
                    Position = new Vector2(PADDING, hud.SurfaceSize.Y - PADDING - LINE_HEIGHT),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                });

                string peakGText = $"Max G: {peakGForce:F1}";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = peakGText,
                    Position = new Vector2(PADDING, hud.SurfaceSize.Y - PADDING - LINE_HEIGHT * 2),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                });
            }

            private void DrawAOAIndexer(MySpriteDrawFrame frame, double aoa, Vector3D acceleration, double velocity)
            {
                const float INDEXER_X = 100f;
                float indexerY = hud.SurfaceSize.Y / 2f;
                const float SYMBOL_SIZE = 18f;

                const double OPTIMAL_AOA_MIN = 8.0;
                const double OPTIMAL_AOA_MAX = 15.0;

                // Calculate stall percentage (using absolute AoA)
                double absAoA = Math.Abs(aoa);
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
                    double adjustedStallPercent = absAoA / Math.Max(adjustedStallAoA, 5.0);

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

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = spriteType,
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE, SYMBOL_SIZE),
                        RotationOrScale = 0f,
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
                else if (aoa > OPTIMAL_AOA_MAX)
                {
                    // Override color based on stall level
                    if (currentStallLevel == STALL_LEVEL_STALL)
                        indexerColor = HUD_WARNING;
                    else if (currentStallLevel == STALL_LEVEL_WARNING)
                        indexerColor = new Color(255, 128, 0); // Orange
                    else if (currentStallLevel == STALL_LEVEL_CAUTION)
                        indexerColor = HUD_EMPHASIS;
                    else
                        indexerColor = HUD_WARNING;

                    spriteType = "Triangle";

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = spriteType,
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE, SYMBOL_SIZE),
                        RotationOrScale = MathHelper.Pi,
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
                else
                {
                    indexerColor = HUD_EMPHASIS;

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE * 0.8f, SYMBOL_SIZE * 0.8f),
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
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

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"E{energySymbol}",
                    Position = new Vector2(INDEXER_X, indexerY + 25f),
                    RotationOrScale = 0.5f,
                    Color = energyColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                });
            }

            /// <summary>
            /// Draws stall warning overlays based on severity level.
            /// </summary>
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
                        warningColor = new Color(255, 128, 0); // Orange
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
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = warningText,
                        Position = new Vector2(center.X, textY),
                        RotationOrScale = textScale,
                        Color = warningColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    // AoA value
                    string aoaText = $"{currentAoA:F1}\u00B0";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = aoaText,
                        Position = new Vector2(center.X, textY + 25f),
                        RotationOrScale = 0.7f,
                        Color = warningColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    });
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
                        SpriteHelpers.AddLineSprite(frame, new Vector2(bracketX - 15f, bracketY),
                                          new Vector2(bracketX - 15f, bracketY + bracketHeight), 3f, warningColor);
                        SpriteHelpers.AddLineSprite(frame, new Vector2(bracketX - 15f, bracketY),
                                          new Vector2(bracketX - 5f, bracketY), 3f, warningColor);
                        SpriteHelpers.AddLineSprite(frame, new Vector2(bracketX - 15f, bracketY + bracketHeight),
                                          new Vector2(bracketX - 5f, bracketY + bracketHeight), 3f, warningColor);
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

                    var labelSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = labelText,
                        Position = new Vector2(labelColumnX, yoffset + i * Y_OFFSET_PER_VALUE),
                        RotationOrScale = TEXT_SCALE,
                        Color = HUD_PRIMARY,
                        Alignment = TextAlignment.LEFT,
                        FontId = "White"
                    };
                    frame.Add(labelSprite);

                    var valueSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = numericValue.ToString("F1"),
                        Position = new Vector2(numberColumnX, yoffset + i * Y_OFFSET_PER_VALUE),
                        RotationOrScale = TEXT_SCALE,
                        Color = HUD_PRIMARY,
                        Alignment = TextAlignment.RIGHT,
                        FontId = "White"
                    };
                    frame.Add(valueSprite);
                }
            }

            private void DrawFlightInfo(
                MySpriteDrawFrame frame,
                double velocityKPH,
                double gForces,
                double heading,
                double altitude,
                double aoa,
                double throttle,
                double mach
            )
            {
                float infoX = hud.SurfaceSize.X - hud.SurfaceSize.X / 2;
                float boxPadding = 5f;
                float textHeight = 30f;

                var surface = hud;
                var textScale = 0.75f;

                Vector2 maxTextSize = surface.MeasureStringInPixels(
                    new StringBuilder("SPD: 00.0 kph"),
                    "White",
                    textScale
                );
                float maxWidth = maxTextSize.X + boxPadding * 2;

                DrawThrottleBarWithBox(
                    frame,
                    surface,
                    (float)throttle / 100,
                    new Vector2(5, hud.SurfaceSize.Y - textHeight - 140),
                    80,
                    8,
                    textScale
                );
            }

            private void DrawThrottleBarWithBox(
                MySpriteDrawFrame frame,
                IMyTextSurface surface,
                float throttle,
                Vector2 position,
                float boxPadding,
                float maxWidth,
                float barHeight,
                Color barColor = default(Color),
                Color boxColor = default(Color),
                float lineThickness = 2f
            )
            {
                barColor = barColor == default(Color) ? Color.Lime : barColor;
                boxColor = boxColor == default(Color) ? Color.Lime : boxColor;

                Vector2 boxSize = new Vector2(
                    maxWidth + boxPadding * 1f,
                    barHeight + boxPadding * 1.5f
                );
                boxSize.X = boxSize.X / 4;

                Vector2 boxTopLeft = position;

                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(0, lineThickness / 2),
                        Size = new Vector2(boxSize.X, lineThickness),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(0, boxSize.Y - lineThickness / 2),
                        Size = new Vector2(boxSize.X, lineThickness),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(lineThickness / 2, boxSize.Y / 2),
                        Size = new Vector2(lineThickness, boxSize.Y),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(boxSize.X - lineThickness / 2, boxSize.Y / 2),
                        Size = new Vector2(lineThickness, boxSize.Y),
                        Color = boxColor
                    }
                );

                float filledHeight = barHeight * throttle;
                Vector2 filledSize = new Vector2(maxWidth * 100, filledHeight * boxSize.Y * 1.25f);
                barColor = throttle > THROTTLE_HYDROGEN_THRESHOLD ? HUD_EMPHASIS : HUD_PRIMARY;

                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(0, (boxSize.Y - boxPadding / 33 - lineThickness / 2 - filledSize.Y / 2) * 1.025f),
                        Size = new Vector2(boxSize.X, filledSize.Y * 1.05f),
                        Color = barColor
                    }
                );
            }

            private void UpdateAltitudeHistory(double currentAltitude, TimeSpan currentTime)
            {
                altitudeHistory.Enqueue(new AltitudeTimePoint(currentTime, currentAltitude));
                while (altitudeHistory.Count > 1 && currentTime - altitudeHistory.Peek().Time > historyDuration)
                {
                    altitudeHistory.Dequeue();
                }
            }

            private double CalculateVerticalVelocity(double currentAltitude, TimeSpan currentTime)
            {
                if (altitudeHistory.Count < 2)
                {
                    return 0;
                }

                AltitudeTimePoint oldestData = altitudeHistory.Peek();
                TimeSpan oldestTime = oldestData.Time;
                double oldestAltitude = oldestData.Altitude;

                TimeSpan timeDifference = currentTime - oldestTime;
                if (timeDifference.TotalSeconds < 0.01)
                {
                    return 0;
                }

                double altitudeChange = currentAltitude - oldestAltitude;
                double vvi = altitudeChange / timeDifference.TotalSeconds;

                return vvi;
            }
        }
    }
}
