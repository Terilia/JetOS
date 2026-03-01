using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        partial class HUDModule
        {
            private void RenderWeaponScreen(double heading, double altitude, Vector3D currentVelocity, Vector3D shooterPosition)
            {
                if (weaponScreen == null) return;

                using (var frame = weaponScreen.DrawFrame())
                {
                    float screenWidth = weaponScreen.SurfaceSize.X;
                    float screenHeight = weaponScreen.SurfaceSize.Y;
                    float margin = 10f;
                    float panelY = 25f;
                    Color titleColor = new Color(200, 180, 50);
                    Color headerColor = new Color(50, 180, 200);
                    Color borderColor = new Color(60, 120, 60);
                    Color panelBgColor = new Color(20, 20, 20, 180);

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, screenHeight / 2f),
                        Size = new Vector2(screenWidth, screenHeight),
                        Color = Color.Black,
                        Alignment = TextAlignment.CENTER
                    });

                    float titleHeight = 35f;
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY + 5f),
                        Size = new Vector2(screenWidth - margin * 2, titleHeight),
                        Color = new Color(30, 30, 30, 200),
                        Alignment = TextAlignment.CENTER
                    });

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "WEAPON SYSTEMS",
                        Position = new Vector2(screenWidth / 2f, panelY),
                        RotationOrScale = 0.75f,
                        Color = titleColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    panelY += 45f;

                    float weaponPanelHeight = 100f;
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY + weaponPanelHeight / 2f),
                        Size = new Vector2(screenWidth - margin * 2, weaponPanelHeight),
                        Color = panelBgColor,
                        Alignment = TextAlignment.CENTER
                    });

                    DrawWeaponStatusPanelToScreen(frame, myjet, margin, panelY, screenWidth - margin * 2);

                    panelY += weaponPanelHeight + 15f;

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY),
                        Size = new Vector2(screenWidth - margin * 4, 2f),
                        Color = borderColor,
                        Alignment = TextAlignment.CENTER
                    });

                    panelY += 15f;

                    if (activeMissiles.Count > 0)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "MISSILES IN FLIGHT",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            RotationOrScale = 0.65f,
                            Color = headerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                        panelY += 30f;

                        DrawMissileTOFToScreen(frame, screenWidth / 2f, panelY);
                        panelY += activeMissiles.Count * 20f + 25f;
                    }

                    int occupiedSlotCount = 0;
                    for (int i = 0; i < myjet.targetSlots.Length; i++)
                    {
                        if (myjet.targetSlots[i].IsOccupied) occupiedSlotCount++;
                    }
                    if (occupiedSlotCount > 1)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            Size = new Vector2(screenWidth - margin * 4, 2f),
                            Color = borderColor,
                            Alignment = TextAlignment.CENTER
                        });

                        panelY += 15f;

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "TARGET TRACKING",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            RotationOrScale = 0.65f,
                            Color = headerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                        panelY += 30f;

                        Vector3D[] occupiedPositions = new Vector3D[occupiedSlotCount];
                        int posIndex = 0;
                        for (int i = 0; i < myjet.targetSlots.Length; i++)
                        {
                            if (myjet.targetSlots[i].IsOccupied)
                            {
                                occupiedPositions[posIndex++] = myjet.targetSlots[i].Position;
                            }
                        }
                        DrawMultiTargetPanelToScreen(frame, occupiedPositions, shooterPosition, margin, panelY, screenWidth - margin * 2);
                    }
                }
            }

            private void DrawWeaponStatusPanelToScreen(MySpriteDrawFrame frame, Jet myjet, float panelX, float panelY, float panelWidth)
            {
                const float PANEL_HEIGHT = 90f;
                const float TEXT_SCALE = 0.7f;
                const float LINE_HEIGHT = 18f;

                SpriteHelpers.DrawRectangleOutline(frame, panelX, panelY, panelWidth, PANEL_HEIGHT, 2f, HUD_PRIMARY);

                float textX = panelX + 10f;
                float textY = panelY + 10f;

                // Gun status with ammo count
                int gunCount = myjet.GetGunCount();
                int ammoCount = myjet.GetTotalGunAmmo();

                // Determine gun status text and color
                string gunStatus;
                Color gunColor;
                if (gunCount == 0)
                {
                    gunStatus = "NO GUNS";
                    gunColor = HUD_WARNING;
                }
                else if (ammoCount == 0)
                {
                    gunStatus = "GUN EMPTY";
                    gunColor = HUD_WARNING;
                }
                else if (ammoCount < 100)
                {
                    gunStatus = "GUN LOW";
                    gunColor = HUD_EMPHASIS;
                }
                else
                {
                    gunStatus = "GUN READY";
                    gunColor = HUD_PRIMARY;
                }

                string weaponText = $"WPN: {gunStatus}";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = weaponText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = gunColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                textY += LINE_HEIGHT;

                // Show actual ammo count with visual bar
                string ammoText = ammoCount > 0 ? $"RND: {ammoCount}" : "RND: ---";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = ammoText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = gunColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                // Draw ammo bar (visual indicator)
                if (ammoCount > 0)
                {
                    float barX = textX + 65f;
                    float barY = textY + 3f;
                    float barMaxWidth = 60f;
                    float barHeight = 10f;
                    float ammoPercent = MathHelper.Clamp(ammoCount / 500f, 0f, 1f); // Assume 500 max
                    float barWidth = barMaxWidth * ammoPercent;

                    // Background
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(barX + barMaxWidth / 2f, barY + barHeight / 2f),
                        Size = new Vector2(barMaxWidth, barHeight),
                        Color = new Color(40, 40, 40),
                        Alignment = TextAlignment.CENTER
                    });

                    // Filled portion
                    if (barWidth > 0)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(barX + barWidth / 2f, barY + barHeight / 2f),
                            Size = new Vector2(barWidth, barHeight - 2f),
                            Color = gunColor,
                            Alignment = TextAlignment.CENTER
                        });
                    }
                }

                textY += LINE_HEIGHT;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "MSL:",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                float baySquareSize = 10f;
                float bayStartX = textX + 35f;
                float bayY = textY + 2f;

                int maxBays = Math.Min(8, myjet._bays.Count);
                for (int i = 0; i < maxBays; i++)
                {
                    bool bayLoaded = myjet._bays[i].IsConnected;
                    Color bayColor = bayLoaded ? HUD_PRIMARY : HUD_WARNING;

                    float bayX = bayStartX + (i % 4) * (baySquareSize + 3f);
                    float currentBayY = bayY + (i / 4) * (baySquareSize + 3f);

                    if (bayLoaded)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(bayX + baySquareSize/2, currentBayY + baySquareSize/2),
                            Size = new Vector2(baySquareSize, baySquareSize),
                            Color = bayColor,
                            Alignment = TextAlignment.CENTER
                        });
                    }
                    else
                    {
                        SpriteHelpers.DrawRectangleOutline(frame, bayX, currentBayY, baySquareSize, baySquareSize, 1f, bayColor);
                    }
                }

                textY += LINE_HEIGHT * 2;
                bool radarTracking = myjet.targetSlots[myjet.activeSlotIndex].IsOccupied;
                string podStatus = radarTracking ? "LOCK \u2713" : "----";
                Color podColor = radarTracking ? HUD_PRIMARY : HUD_WARNING;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"POD: {podStatus}",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = podColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });
            }

            // Cached target labels to avoid per-frame string allocation
            private static readonly string[] _targetLabels = { "\u25C9 PRI", "\u25CB T1", "\u25CB T2", "\u25CB T3", "\u25CB T4" };

            private void DrawMultiTargetPanelToScreen(MySpriteDrawFrame frame, Vector3D[] targetPositions, Vector3D shooterPosition, float panelX, float startY, float panelWidth)
            {
                if (targetPositions == null || targetPositions.Length < 1) return;

                const float LINE_HEIGHT = 22f;
                const float TEXT_SCALE = 0.7f;

                float textX = panelX + 10f;
                float textY = startY;

                for (int i = 0; i < Math.Min(5, targetPositions.Length); i++)
                {
                    double range = Vector3D.Distance(shooterPosition, targetPositions[i]);
                    bool isPrimary = (i == 0);

                    Color targetColor = isPrimary ? HUD_WARNING : HUD_RADAR_FRIENDLY;

                    string targetText = i < _targetLabels.Length ? _targetLabels[i] : "\u25CB T?";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = targetText,
                        Position = new Vector2(textX, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "Monospace"
                    });

                    float barMaxWidth = 80f;
                    float barHeight = 8f;
                    float barX = textX + 50f;
                    float barWidth = MathHelper.Clamp((float)(range / 15000.0 * barMaxWidth), 2f, barMaxWidth);

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(barX, textY + 5f),
                        Size = new Vector2(barWidth, barHeight),
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT
                    });

                    string rangeText = range >= 1000 ? $"{range/1000:F1}km" : $"{range:F0}m";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = rangeText,
                        Position = new Vector2(barX + barMaxWidth + 10f, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "Monospace"
                    });

                    textY += LINE_HEIGHT;
                }
            }

            private void DrawMissileTOFToScreen(MySpriteDrawFrame frame, float centerX, float startY)
            {
                if (activeMissiles.Count == 0) return;

                const float TEXT_SCALE = 0.7f;
                const float LINE_HEIGHT = 20f;

                activeMissiles.RemoveAll(m => (totalElapsedTime - m.LaunchTime).TotalSeconds > m.EstimatedTOF + 5);

                for (int i = 0; i < Math.Min(5, activeMissiles.Count); i++)
                {
                    var missile = activeMissiles[i];
                    double timeRemaining = missile.EstimatedTOF - (totalElapsedTime - missile.LaunchTime).TotalSeconds;

                    if (timeRemaining > 0)
                    {
                        string tofText = $"MSL {missile.BayIndex + 1}: {timeRemaining:F1}s \u2192 TGT";
                        Color tofColor = timeRemaining < 3 ? HUD_WARNING : HUD_EMPHASIS;

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = tofText,
                            Position = new Vector2(centerX, startY + i * LINE_HEIGHT),
                            RotationOrScale = TEXT_SCALE,
                            Color = tofColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "Monospace"
                        });
                    }
                }
            }

            // --- Gun Control Overlay ---
            private void DrawGunControlOverlay(MySpriteDrawFrame frame)
            {
                var gunControl = SystemManager.GetGunControl();
                if (gunControl == null || !gunControl.IsControlEnabled)
                    return;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;
                float viewportMin = Math.Min(surfaceSize.X, surfaceSize.Y);

                // Gun control aiming zone circle (15 degree cone boundary)
                float coneRadius = viewportMin * 0.25f;  // Visual size of 15 degree cone

                // Draw boundary circle
                SpriteHelpers.DrawCircleOutline(frame, center, coneRadius, new Color(100, 100, 100, 150), 2f);

                // Draw status text at top
                string statusText = "GUN AUTO-TRACK";
                Color statusColor = Color.Cyan;

                if (gunControl.IsLeftCalibrating || gunControl.IsRightCalibrating)
                {
                    statusText = "CALIBRATING...";
                    statusColor = Color.Yellow;
                }

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = statusText,
                    Position = new Vector2(center.X, center.Y - coneRadius - 30f),
                    RotationOrScale = 0.6f,
                    Color = statusColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });

                // Draw left turret indicator
                Vector2 leftIndicatorPos = new Vector2(center.X - coneRadius - 40f, center.Y);
                DrawTurretIndicator(frame, leftIndicatorPos, "L", gunControl.IsLeftTracking, gunControl.IsLeftCalibrating);

                // Draw right turret indicator
                Vector2 rightIndicatorPos = new Vector2(center.X + coneRadius + 40f, center.Y);
                DrawTurretIndicator(frame, rightIndicatorPos, "R", gunControl.IsRightTracking, gunControl.IsRightCalibrating);

                // If both turrets are locked, show FIRE indicator
                if (gunControl.IsLeftTracking && gunControl.IsRightTracking)
                {
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "FIRE",
                        Position = new Vector2(center.X, center.Y + coneRadius + 20f),
                        RotationOrScale = 1.0f,
                        Color = Color.Red,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    // Draw flashing reticle in center when locked
                    int flashPhase = (currentTick / 5) % 2;
                    if (flashPhase == 0)
                    {
                        // Draw targeting reticle
                        float reticleSize = 20f;
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "Circle",
                            Position = center,
                            Size = new Vector2(reticleSize, reticleSize),
                            Color = Color.Red,
                            Alignment = TextAlignment.CENTER
                        });
                    }
                }
                else if (gunControl.IsLeftTracking || gunControl.IsRightTracking)
                {
                    // One turret locked
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "TRACKING",
                        Position = new Vector2(center.X, center.Y + coneRadius + 20f),
                        RotationOrScale = 0.7f,
                        Color = Color.Yellow,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });
                }
                else
                {
                    // No lock
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "SEARCHING",
                        Position = new Vector2(center.X, center.Y + coneRadius + 20f),
                        RotationOrScale = 0.6f,
                        Color = Color.Gray,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });
                }
            }

            private void DrawTurretIndicator(MySpriteDrawFrame frame, Vector2 position, string label, bool isLocked, bool isCalibrating)
            {
                Color bgColor;
                Color textColor;
                string statusChar;

                if (isCalibrating)
                {
                    bgColor = new Color(100, 100, 0, 200);
                    textColor = Color.Yellow;
                    statusChar = "?";
                }
                else if (isLocked)
                {
                    bgColor = new Color(0, 150, 0, 200);
                    textColor = Color.Lime;
                    statusChar = "X";
                }
                else
                {
                    bgColor = new Color(50, 50, 50, 200);
                    textColor = Color.Gray;
                    statusChar = "O";
                }

                // Background circle
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = position,
                    Size = new Vector2(35f, 35f),
                    Color = bgColor,
                    Alignment = TextAlignment.CENTER
                });

                // Label
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = label,
                    Position = position + new Vector2(0f, -18f),
                    RotationOrScale = 0.5f,
                    Color = textColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });

                // Status
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = statusChar,
                    Position = position + new Vector2(0f, -5f),
                    RotationOrScale = 0.8f,
                    Color = textColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
        }
    }
}
