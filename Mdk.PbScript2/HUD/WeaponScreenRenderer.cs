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
                    Color dimColor = new Color(100, 100, 100);

                    // Black background
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, screenHeight / 2f),
                        Size = new Vector2(screenWidth, screenHeight),
                        Color = Color.Black,
                        Alignment = TextAlignment.CENTER
                    });

                    // --- Title bar ---
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
                        Data = "TARGET LIST",
                        Position = new Vector2(screenWidth / 2f, panelY),
                        RotationOrScale = 0.75f,
                        Color = titleColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    panelY += 45f;

                    // --- Selected Target Detail Box ---
                    float detailBoxHeight = 110f;
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY + detailBoxHeight / 2f),
                        Size = new Vector2(screenWidth - margin * 2, detailBoxHeight),
                        Color = panelBgColor,
                        Alignment = TextAlignment.CENTER
                    });
                    SpriteHelpers.DrawRectangleOutline(frame, margin, panelY, screenWidth - margin * 2, detailBoxHeight, 1f, borderColor);

                    var selected = myjet.GetSelectedEnemy();
                    if (selected.HasValue)
                    {
                        DrawSelectedTargetDetail(frame, selected.Value, shooterPosition, currentVelocity, margin, panelY, screenWidth);
                    }
                    else
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "NO TGT",
                            Position = new Vector2(screenWidth / 2f, panelY + detailBoxHeight / 2f - 12f),
                            RotationOrScale = 1.0f,
                            Color = dimColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "Monospace"
                        });
                    }

                    panelY += detailBoxHeight + 10f;

                    // --- Separator ---
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY),
                        Size = new Vector2(screenWidth - margin * 4, 2f),
                        Color = borderColor,
                        Alignment = TextAlignment.CENTER
                    });

                    panelY += 10f;

                    // --- Enemy List ---
                    var enemies = myjet.GetEnemiesSortedByDistance();
                    if (enemies.Count > 0)
                    {
                        DrawEnemyList(frame, enemies, selected, shooterPosition, margin, panelY, screenWidth, screenHeight);
                    }
                    else
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "NO CONTACTS",
                            Position = new Vector2(screenWidth / 2f, panelY + 10f),
                            RotationOrScale = 0.6f,
                            Color = dimColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "Monospace"
                        });
                    }

                    // --- Missile TOF at bottom ---
                    if (activeMissiles.Count > 0)
                    {
                        float tofY = screenHeight - (activeMissiles.Count * 20f + 35f);

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenWidth / 2f, tofY - 5f),
                            Size = new Vector2(screenWidth - margin * 4, 2f),
                            Color = borderColor,
                            Alignment = TextAlignment.CENTER
                        });

                        tofY += 5f;

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "MSL IN FLIGHT",
                            Position = new Vector2(screenWidth / 2f, tofY),
                            RotationOrScale = 0.55f,
                            Color = headerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                        tofY += 20f;

                        DrawMissileTOFToScreen(frame, screenWidth / 2f, tofY);
                    }
                }
            }

            private void DrawSelectedTargetDetail(MySpriteDrawFrame frame, Jet.EnemyContact contact, Vector3D shooterPosition, Vector3D currentVelocity, float margin, float panelY, float screenWidth)
            {
                const float TEXT_SCALE = 0.6f;
                const float LINE_HEIGHT = 18f;
                float textX = margin + 8f;
                float textY = panelY + 6f;
                float rightX = screenWidth - margin - 8f;

                // Row 1: Name + tags
                string name = contact.Name;
                if (string.IsNullOrEmpty(name)) name = "UNKNOWN";
                if (name.Length > 16) name = name.Substring(0, 16);

                // Build tag string
                string tags = "";
                if (myjet.isPinnedSelected) tags += "PIN ";
                if (radarControl != null && radarControl.IsTrackLocked) tags += "STT";
                else tags += "TWS";

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = name,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = 0.7f,
                    Color = HUD_WARNING,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = tags,
                    Position = new Vector2(rightX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = radarControl != null && radarControl.IsTrackLocked ? HUD_PRIMARY : HUD_EMPHASIS,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "Monospace"
                });

                textY += LINE_HEIGHT + 4f;

                // Row 2: Range
                double range = Vector3D.Distance(shooterPosition, contact.Position);
                string rangeText = range >= 1000 ? $"RNG: {range / 1000:F2} km" : $"RNG: {range:F0} m";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = rangeText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                // Bearing
                double bearing = CalculateBearingToTarget(contact.Position, shooterPosition);
                string bearingText = $"BRG: {bearing:F0}\u00B0";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = bearingText,
                    Position = new Vector2(rightX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "Monospace"
                });

                textY += LINE_HEIGHT;

                // Row 3: Closure rate + contact age
                Vector3D toTarget = contact.Position - shooterPosition;
                double dist = toTarget.Length();
                Vector3D relVel = currentVelocity - contact.Velocity;
                double closureRate = 0;
                if (dist > 0.1)
                    closureRate = Vector3D.Dot(relVel, toTarget / dist);

                string closureText = $"Vc: {closureRate:F0} m/s";
                Color closureColor = closureRate > 0 ? HUD_PRIMARY : HUD_EMPHASIS;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = closureText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = closureColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                double ageSec = contact.AgeSeconds;
                string ageText = ageSec < 1 ? "AGE: <1s" : $"AGE: {ageSec:F0}s";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = ageText,
                    Position = new Vector2(rightX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = myjet.GetEnemyContactColor(contact),
                    Alignment = TextAlignment.RIGHT,
                    FontId = "Monospace"
                });

                textY += LINE_HEIGHT;

                // Row 4: Speed + source
                double tgtSpeed = contact.Velocity.Length();
                string speedText = $"SPD: {tgtSpeed:F0} m/s";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = speedText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_INFO,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                string sourceText = contact.SourceIndex == 0 ? "SRC: RDR" : $"SRC: RWR{contact.SourceIndex}";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = sourceText,
                    Position = new Vector2(rightX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_INFO,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "Monospace"
                });
            }

            private void DrawEnemyList(MySpriteDrawFrame frame, List<Jet.EnemyContact> enemies, Jet.EnemyContact? selected, Vector3D shooterPosition, float margin, float startY, float screenWidth, float screenHeight)
            {
                const float LINE_HEIGHT = 20f;
                const float TEXT_SCALE = 0.55f;
                float textX = margin + 6f;
                float textY = startY;

                // Reserve space for missile TOF at bottom
                float bottomReserve = activeMissiles.Count > 0 ? (activeMissiles.Count * 20f + 45f) : 10f;
                int maxRows = (int)((screenHeight - startY - bottomReserve) / LINE_HEIGHT);
                maxRows = Math.Min(maxRows, 10);

                for (int i = 0; i < Math.Min(maxRows, enemies.Count); i++)
                {
                    var contact = enemies[i];
                    bool isSelected = IsContactSelected(contact, selected);

                    // Highlight bar for selected entry
                    if (isSelected)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenWidth / 2f, textY + LINE_HEIGHT / 2f - 1f),
                            Size = new Vector2(screenWidth - margin * 2, LINE_HEIGHT),
                            Color = new Color(30, 50, 30, 180),
                            Alignment = TextAlignment.CENTER
                        });
                    }

                    Color contactColor = isSelected ? HUD_PRIMARY : myjet.GetEnemyContactColor(contact);

                    // Selection marker
                    string marker = isSelected ? "\u25C9" : "\u25CB";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = marker,
                        Position = new Vector2(textX, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = contactColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "Monospace"
                    });

                    // Name (with P prefix for pinned)
                    string name = contact.Name;
                    if (string.IsNullOrEmpty(name)) name = "UNKNOWN";

                    bool isPinned = myjet.pinnedRaycastTarget.HasValue &&
                        ((contact.EntityId != 0 && contact.EntityId == myjet.pinnedRaycastTarget.Value.EntityId) ||
                         (!string.IsNullOrEmpty(contact.Name) && contact.Name == myjet.pinnedRaycastTarget.Value.Name));

                    if (isPinned) name = "P " + name;
                    if (name.Length > 12) name = name.Substring(0, 12);

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = name,
                        Position = new Vector2(textX + 14f, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = contactColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "Monospace"
                    });

                    // Range
                    double range = Vector3D.Distance(shooterPosition, contact.Position);
                    string rangeText = range >= 1000 ? $"{range / 1000:F1}km" : $"{range:F0}m";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = rangeText,
                        Position = new Vector2(screenWidth - margin - 50f, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = contactColor,
                        Alignment = TextAlignment.RIGHT,
                        FontId = "Monospace"
                    });

                    // Age color bar
                    float barX = screenWidth - margin - 45f;
                    float barWidth = 35f;
                    float barHeight = 6f;
                    float ageFrac = MathHelper.Clamp(1f - (float)(contact.AgeSeconds / 180.0), 0f, 1f);
                    Color ageColor = myjet.GetEnemyContactColor(contact);

                    // Bar background
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(barX + barWidth / 2f, textY + LINE_HEIGHT / 2f - 1f),
                        Size = new Vector2(barWidth, barHeight),
                        Color = new Color(30, 30, 30),
                        Alignment = TextAlignment.CENTER
                    });

                    // Bar fill
                    float fillWidth = barWidth * ageFrac;
                    if (fillWidth > 1f)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(barX + fillWidth / 2f, textY + LINE_HEIGHT / 2f - 1f),
                            Size = new Vector2(fillWidth, barHeight - 1f),
                            Color = ageColor,
                            Alignment = TextAlignment.CENTER
                        });
                    }

                    textY += LINE_HEIGHT;
                }

                // Show count if there are more contacts
                if (enemies.Count > maxRows)
                {
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = $"+{enemies.Count - maxRows} more",
                        Position = new Vector2(screenWidth / 2f, textY),
                        RotationOrScale = 0.45f,
                        Color = new Color(100, 100, 100),
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    });
                }
            }

            private bool IsContactSelected(Jet.EnemyContact contact, Jet.EnemyContact? selected)
            {
                if (!selected.HasValue) return false;
                var sel = selected.Value;

                if (contact.EntityId != 0 && sel.EntityId != 0)
                    return contact.EntityId == sel.EntityId;

                if (!string.IsNullOrEmpty(contact.Name) && !string.IsNullOrEmpty(sel.Name))
                    return contact.Name == sel.Name;

                return Vector3D.Distance(contact.Position, sel.Position) < 50.0;
            }

            private double CalculateBearingToTarget(Vector3D targetPos, Vector3D shooterPos)
            {
                if (cockpit == null) return 0;

                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D worldUp;
                if (gravity.LengthSquared() > 1e-6)
                    worldUp = -Vector3D.Normalize(gravity);
                else
                    worldUp = Vector3D.Up;

                Vector3D toTarget = targetPos - shooterPos;
                Vector3D toTargetHorizontal = Vector3D.Reject(toTarget, worldUp);
                if (toTargetHorizontal.LengthSquared() < 1e-8) return 0;
                toTargetHorizontal.Normalize();

                Vector3D forwardHorizontal = Vector3D.Reject(cockpit.WorldMatrix.Forward, worldUp);
                if (forwardHorizontal.LengthSquared() < 1e-8) return 0;
                forwardHorizontal.Normalize();

                Vector3D rightHorizontal = Vector3D.Cross(forwardHorizontal, worldUp);

                double fwdComponent = Vector3D.Dot(toTargetHorizontal, forwardHorizontal);
                double rightComponent = Vector3D.Dot(toTargetHorizontal, rightHorizontal);

                double bearingRad = Math.Atan2(rightComponent, fwdComponent);
                double bearingDeg = MathHelper.ToDegrees(bearingRad);
                if (bearingDeg < 0) bearingDeg += 360.0;

                return bearingDeg;
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
