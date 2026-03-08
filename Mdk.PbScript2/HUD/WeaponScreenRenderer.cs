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
                    float detailBoxHeight = 95f;
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
                float textX = margin + 8f;
                float textY = panelY + 6f;
                float rightX = screenWidth - margin - 8f;

                // --- Row 1: Name + track mode badge ---
                string name = contact.Name;
                if (string.IsNullOrEmpty(name)) name = "UNKNOWN";
                if (name.Length > 14) name = name.Substring(0, 14);

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

                bool isSTT = radarControl != null && radarControl.IsTrackLocked;
                string badgeText = myjet.isPinnedSelected ? "PIN" : isSTT ? "STT" : "TWS";
                Color badgeColor = isSTT ? HUD_PRIMARY : HUD_EMPHASIS;

                // Badge outline box
                float badgeWidth = 30f;
                float badgeHeight = 14f;
                float badgeX = rightX - badgeWidth / 2f;
                float badgeY = textY + 4f;
                SpriteHelpers.DrawRectangleOutline(frame, badgeX - badgeWidth / 2f, badgeY - badgeHeight / 2f, badgeWidth, badgeHeight, 1f, badgeColor);
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = badgeText,
                    Position = new Vector2(badgeX, badgeY - 7f),
                    RotationOrScale = 0.45f,
                    Color = badgeColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                });

                // Divider line under name
                textY += 20f;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(screenWidth / 2f, textY),
                    Size = new Vector2(screenWidth - margin * 2 - 12f, 1f),
                    Color = new Color(42, 90, 42),
                    Alignment = TextAlignment.CENTER
                });
                textY += 5f;

                // --- Row 2: Range (large) + Closure rate (large) ---
                double range = Vector3D.Distance(shooterPosition, contact.Position);
                string rangeText = range >= 1000 ? $"{range / 1000:F2} km" : $"{range:F0} m";

                Vector3D toTarget = contact.Position - shooterPosition;
                double dist = toTarget.Length();
                Vector3D relVel = currentVelocity - contact.Velocity;
                double closureRate = 0;
                if (dist > 0.1)
                    closureRate = Vector3D.Dot(relVel, toTarget / dist);

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = rangeText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = 0.8f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                string closureLabel = closureRate > 10 ? "HOT" : closureRate < -10 ? "COLD" : "---";
                string closureText = $"{Math.Abs(closureRate):F0} {closureLabel}";
                Color closureColor = closureRate > 10 ? HUD_WARNING : closureRate < -10 ? new Color(100, 130, 255) : new Color(136, 136, 136);
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = closureText,
                    Position = new Vector2(rightX, textY),
                    RotationOrScale = 0.65f,
                    Color = closureColor,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "Monospace"
                });

                textY += 20f;

                // --- Row 3: Secondary data (BRG + SPD) ---
                double bearing = CalculateBearingToTarget(contact.Position, shooterPosition);
                double tgtSpeed = contact.Velocity.Length();
                Color dimColor = new Color(102, 102, 102);
                Color valColor = new Color(170, 170, 170);

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "BRG",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = 0.5f,
                    Color = dimColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"{bearing:F0}\u00B0",
                    Position = new Vector2(screenWidth / 2f - 8f, textY),
                    RotationOrScale = 0.5f,
                    Color = valColor,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "Monospace"
                });
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "SPD",
                    Position = new Vector2(screenWidth / 2f + 8f, textY),
                    RotationOrScale = 0.5f,
                    Color = dimColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"{tgtSpeed:F0} m/s",
                    Position = new Vector2(rightX, textY),
                    RotationOrScale = 0.5f,
                    Color = valColor,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "Monospace"
                });

                textY += 16f;

                // --- Row 4: Source + tracking timeline ---
                string sourceText = contact.SourceIndex == 0 ? "RDR" : $"RWR{contact.SourceIndex}";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = sourceText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = 0.45f,
                    Color = dimColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                // Draw 30-second tracking timeline
                float timelineX = screenWidth / 2f - 10f;
                float timelineY = textY + 4f;
                float timelineWidth = rightX - timelineX;
                DrawTrackingTimeline(frame, contact, timelineX, timelineY, timelineWidth, 8f, 30);
            }

            /// <summary>
            /// Draws a tracking timeline bar. Each column = 1 second.
            /// Green (full height) = update received. Red (half height) = stale.
            /// Batches consecutive same-state columns into single sprites for performance.
            /// </summary>
            private void DrawTrackingTimeline(MySpriteDrawFrame frame, Jet.EnemyContact contact, float x, float y, float width, float height, int columns)
            {
                uint history = contact.GetDisplayHistory();
                float colWidth = width / columns;
                Color okColor = new Color(68, 255, 68);
                Color staleColor = new Color(255, 50, 50);
                Color bgColor = new Color(10, 10, 10);

                // Background
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(x + width / 2f, y + height / 2f),
                    Size = new Vector2(width, height),
                    Color = bgColor,
                    Alignment = TextAlignment.CENTER
                });

                // Batch consecutive same-state columns
                int runStart = 0;
                // Bit (columns-1) = oldest, bit 0 = newest; draw left to right = oldest to newest
                bool runIsOk = ((history >> (columns - 1)) & 1) == 1;

                for (int i = 1; i <= columns; i++)
                {
                    bool currentIsOk = false;
                    if (i < columns)
                        currentIsOk = ((history >> (columns - 1 - i)) & 1) == 1;

                    if (i == columns || currentIsOk != runIsOk)
                    {
                        int runLen = i - runStart;
                        float segX = x + runStart * colWidth;
                        float segW = runLen * colWidth - 1f;
                        if (segW < 1f) segW = 1f;

                        float segH = runIsOk ? height - 2f : (height - 2f) * 0.5f;
                        float segY = runIsOk ? y + 1f + segH / 2f : y + height - 1f - segH / 2f;

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(segX + segW / 2f, segY),
                            Size = new Vector2(segW, segH),
                            Color = runIsOk ? okColor : staleColor,
                            Alignment = TextAlignment.CENTER
                        });

                        runStart = i;
                        if (i < columns)
                            runIsOk = currentIsOk;
                    }
                }
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

                    // Compact tracking timeline (15 columns)
                    float timelineX = screenWidth - margin - 45f;
                    float timelineW = 40f;
                    DrawTrackingTimeline(frame, contact, timelineX, textY + LINE_HEIGHT / 2f - 3f, timelineW, 6f, 15);

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
