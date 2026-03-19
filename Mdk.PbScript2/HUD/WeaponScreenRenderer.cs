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
                    float sw = weaponScreen.SurfaceSize.X;
                    float sh = weaponScreen.SurfaceSize.Y;
                    float margin = sw * 0.019f;

                    // Draw shared MFD chrome
                    float contentY = MFDFrame.DrawChrome(frame, sw, sh, headerRight: "WEAPONS", drawFooterNav: false);
                    float contentBot = MFDFrame.ContentBottom(sh);
                    float padX = margin + 4f;

                    // ── Section: TARGET LIST ──
                    float secY = contentY + 6f;
                    DrawWpnSectionTitle(frame, sw, secY, "TARGET LIST");
                    secY += 16f;

                    // ── Selected target detail box ──
                    float detailH = 95f;
                    MFDFrame.Rect(frame, sw / 2f, secY + detailH / 2f, sw - margin * 2, detailH, MFDTheme.PANEL_BG);
                    SpriteHelpers.DrawRectangleOutline(frame, margin, secY, sw - margin * 2, detailH, 1f, MFDTheme.BORDER_LIGHT);

                    var selected = myjet.GetSelectedEnemy();
                    if (selected.HasValue)
                    {
                        DrawSelectedTargetDetail(frame, selected.Value, shooterPosition, currentVelocity, margin, secY, sw);
                    }
                    else
                    {
                        MFDFrame.Txt(frame, "NO TGT", sw / 2f, secY + detailH / 2f - 12f, 1.0f,
                            MFDTheme.DIM_TEXT, MFDTheme.AC);
                    }

                    secY += detailH + 8f;

                    // ── Separator ──
                    MFDFrame.Rect(frame, sw / 2f, secY, sw - margin * 4, 1f, MFDTheme.BORDER);
                    secY += 8f;

                    // ── Enemy list ──
                    var enemies = myjet.GetEnemiesSortedByDistance();
                    if (enemies.Count > 0)
                    {
                        DrawEnemyList(frame, enemies, selected, shooterPosition, margin, secY, sw, contentBot);
                    }
                    else
                    {
                        MFDFrame.Txt(frame, "NO CONTACTS", sw / 2f, secY + 10f, 0.6f,
                            MFDTheme.DIM_TEXT, MFDTheme.AC);
                    }

                    // ── Missile TOF at bottom ──
                    if (activeMissiles.Count > 0)
                    {
                        float tofY = contentBot - (activeMissiles.Count * 20f + 30f);

                        MFDFrame.Rect(frame, sw / 2f, tofY - 5f, sw - margin * 4, 1f, MFDTheme.BORDER);
                        tofY += 5f;

                        MFDFrame.Txt(frame, "MSL IN FLIGHT", sw / 2f, tofY, 0.55f,
                            MFDTheme.STATUS_RDY, MFDTheme.AC);
                        tofY += 20f;

                        DrawMissileTOFToScreen(frame, sw / 2f, tofY);
                    }
                }
            }

            private static void DrawWpnSectionTitle(MySpriteDrawFrame frame, float sw, float y, string text)
            {
                float margin = sw * 0.019f;
                float textW = text.Length * sw * 0.012f;
                float cx = sw / 2f;
                float halfGap = textW / 2f + 8f;
                float lineLeft = margin;
                float lineRight = sw - margin;

                float leftW = cx - halfGap - lineLeft;
                if (leftW > 2f)
                    MFDFrame.Rect(frame, lineLeft + leftW / 2f, y + 5f, leftW, 1f, MFDTheme.BORDER);
                float rightStart = cx + halfGap;
                float rightW = lineRight - rightStart;
                if (rightW > 2f)
                    MFDFrame.Rect(frame, rightStart + rightW / 2f, y + 5f, rightW, 1f, MFDTheme.BORDER);

                MFDFrame.Txt(frame, text, cx, y, 0.45f, MFDTheme.MID_TEXT, MFDTheme.AC);
            }

            private void DrawSelectedTargetDetail(MySpriteDrawFrame frame, Jet.EnemyContact contact, Vector3D shooterPosition, Vector3D currentVelocity, float margin, float panelY, float screenWidth)
            {
                float textX = margin + 8f;
                float textY = panelY + 6f;
                float rightX = screenWidth - margin - 8f;

                // Row 1: Name + track mode badge
                string name = contact.Name;
                if (string.IsNullOrEmpty(name)) name = "UNKNOWN";
                if (name.Length > 14) name = name.Substring(0, 14);

                MFDFrame.Txt(frame, name, textX, textY, 0.7f, MFDTheme.BRIGHT_TEXT);

                bool isSTT = radarControl != null && radarControl.IsTrackLocked;
                string badgeText = isSTT ? "STT" : "TWS";
                Color badgeColor = isSTT ? MFDTheme.ACCENT : MFDTheme.STATUS_RDY;

                float badgeWidth = 30f;
                float badgeHeight = 14f;
                float badgeX = rightX - badgeWidth / 2f;
                float badgeY = textY + 4f;
                SpriteHelpers.DrawRectangleOutline(frame, badgeX - badgeWidth / 2f, badgeY - badgeHeight / 2f, badgeWidth, badgeHeight, 1f, badgeColor);
                MFDFrame.Txt(frame, badgeText, badgeX, badgeY - 7f, 0.45f, badgeColor, MFDTheme.AC);

                // Divider under name
                textY += 20f;
                MFDFrame.Rect(frame, screenWidth / 2f, textY, screenWidth - margin * 2 - 12f, 1f, MFDTheme.BORDER);
                textY += 5f;

                // Row 2: Range + closure
                double range = VDi(shooterPosition, contact.Position);
                string rangeText = range >= 1000 ? $"{range / 1000:F2} km" : $"{range:F0} m";

                Vector3D toTarget = contact.Position - shooterPosition;
                double dist = toTarget.Length();
                Vector3D relVel = currentVelocity - contact.Velocity;
                double closureRate = 0;
                if (dist > 0.1)
                    closureRate = VD(relVel, toTarget / dist);

                MFDFrame.Txt(frame, rangeText, textX, textY, 0.8f, MFDTheme.ACCENT);

                string closureLabel = closureRate > 10 ? "HOT" : closureRate < -10 ? "COLD" : "---";
                string closureText = $"{Math.Abs(closureRate):F0} {closureLabel}";
                Color closureColor = closureRate > 10 ? MFDTheme.WARN : closureRate < -10 ? new Color(80, 110, 200) : MFDTheme.DIM_TEXT_MID;
                MFDFrame.Txt(frame, closureText, rightX, textY, 0.65f, closureColor, MFDTheme.AR);

                textY += 20f;

                // Row 3: BRG + SPD
                double bearing = CalculateBearingToTarget(contact.Position, shooterPosition);
                double tgtSpeed = contact.Velocity.Length();

                MFDFrame.Txt(frame, "BRG", textX, textY, 0.5f, MFDTheme.DIM_TEXT);
                MFDFrame.Txt(frame, $"{bearing:F0}\u00B0", screenWidth / 2f - 8f, textY, 0.5f, MFDTheme.STATUS_VAL, MFDTheme.AR);
                MFDFrame.Txt(frame, "SPD", screenWidth / 2f + 8f, textY, 0.5f, MFDTheme.DIM_TEXT);
                MFDFrame.Txt(frame, $"{tgtSpeed:F0} m/s", rightX, textY, 0.5f, MFDTheme.STATUS_VAL, MFDTheme.AR);

                textY += 16f;

                // Row 4: Source + tracking timeline
                string sourceText = contact.SourceIndex == 0 ? "RDR" : $"RWR{contact.SourceIndex}";
                MFDFrame.Txt(frame, sourceText, textX, textY, 0.45f, MFDTheme.DIM_TEXT);

                float timelineX = screenWidth / 2f - 10f;
                float timelineY = textY + 4f;
                float timelineWidth = rightX - timelineX;
                DrawTrackingTimeline(frame, contact, timelineX, timelineY, timelineWidth, 8f, 30);
            }

            private void DrawTrackingTimeline(MySpriteDrawFrame frame, Jet.EnemyContact contact, float x, float y, float width, float height, int columns)
            {
                uint history = contact.GetDisplayHistory();
                float colWidth = width / columns;

                // Background
                MFDFrame.Rect(frame, x + width / 2f, y + height / 2f, width, height, MFDTheme.BAR_TRACK);

                // Batch consecutive same-state columns
                int runStart = 0;
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
                        Color segC = runIsOk ? MFDTheme.ACCENT : new Color(180, 50, 40);

                        MFDFrame.Rect(frame, segX + segW / 2f, segY, segW, segH, segC);

                        runStart = i;
                        if (i < columns)
                            runIsOk = currentIsOk;
                    }
                }
            }

            private void DrawEnemyList(MySpriteDrawFrame frame, List<Jet.EnemyContact> enemies, Jet.EnemyContact? selected, Vector3D shooterPosition, float margin, float startY, float screenWidth, float contentBot)
            {
                const float LINE_HEIGHT = 20f;
                const float TEXT_SCALE = 0.55f;
                float textX = margin + 6f;
                float textY = startY;

                float bottomReserve = activeMissiles.Count > 0 ? (activeMissiles.Count * 20f + 45f) : 10f;
                int maxRows = (int)((contentBot - startY - bottomReserve) / LINE_HEIGHT);
                maxRows = Math.Min(maxRows, 10);

                for (int i = 0; i < Math.Min(maxRows, enemies.Count); i++)
                {
                    var contact = enemies[i];
                    bool isSelected = IsContactSelected(contact, selected);

                    if (isSelected)
                    {
                        MFDFrame.Rect(frame, screenWidth / 2f, textY + LINE_HEIGHT / 2f - 1f,
                            screenWidth - margin * 2, LINE_HEIGHT, MFDTheme.SEL_FILL);
                        // Left accent
                        MFDFrame.Rect(frame, margin + 1f, textY + LINE_HEIGHT / 2f - 1f,
                            2f, LINE_HEIGHT, MFDTheme.ACCENT);
                    }

                    Color contactColor = isSelected ? MFDTheme.BRIGHT_TEXT : myjet.GetEnemyContactColor(contact);

                    string marker = isSelected ? "\u25C9" : "\u25CB";
                    MFDFrame.Txt(frame, marker, textX, textY, TEXT_SCALE, contactColor);

                    string cName = contact.Name;
                    if (string.IsNullOrEmpty(cName)) cName = "UNKNOWN";
                    if (cName.Length > 12) cName = cName.Substring(0, 12);
                    MFDFrame.Txt(frame, cName, textX + 14f, textY, TEXT_SCALE, contactColor);

                    double range = VDi(shooterPosition, contact.Position);
                    string rangeText = SpriteHelpers.FormatRange(range);
                    MFDFrame.Txt(frame, rangeText, screenWidth - margin - 50f, textY, TEXT_SCALE,
                        contactColor, MFDTheme.AR);

                    float timelineX = screenWidth - margin - 45f;
                    float timelineW = 40f;
                    DrawTrackingTimeline(frame, contact, timelineX, textY + LINE_HEIGHT / 2f - 3f, timelineW, 6f, 15);

                    textY += LINE_HEIGHT;
                }

                if (enemies.Count > maxRows)
                {
                    MFDFrame.Txt(frame, $"+{enemies.Count - maxRows} more", screenWidth / 2f, textY, 0.45f,
                        MFDTheme.DIM_TEXT, MFDTheme.AC);
                }
            }

            private bool IsContactSelected(Jet.EnemyContact contact, Jet.EnemyContact? selected)
            {
                if (!selected.HasValue) return false;
                return contact.Matches(selected.Value);
            }

            private double CalculateBearingToTarget(Vector3D targetPos, Vector3D shooterPos)
            {
                if (cockpit == null) return 0;

                Vector3D gravity = myjet.CachedGravity;
                Vector3D worldUp;
                if (gravity.LengthSquared() > 1e-6)
                    worldUp = -VN(gravity);
                else
                    worldUp = Vector3D.Up;

                Vector3D toTarget = targetPos - shooterPos;
                Vector3D toTargetHorizontal = Vector3D.Reject(toTarget, worldUp);
                if (toTargetHorizontal.LengthSquared() < 1e-8) return 0;
                toTargetHorizontal.Normalize();

                Vector3D forwardHorizontal = Vector3D.Reject(cockpit.WorldMatrix.Forward, worldUp);
                if (forwardHorizontal.LengthSquared() < 1e-8) return 0;
                forwardHorizontal.Normalize();

                Vector3D rightHorizontal = VX(forwardHorizontal, worldUp);

                double fwdComponent = VD(toTargetHorizontal, forwardHorizontal);
                double rightComponent = VD(toTargetHorizontal, rightHorizontal);

                double bearingRad = At2(rightComponent, fwdComponent);
                double bearingDeg = ToDeg(bearingRad);
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
                        Color tofColor = timeRemaining < 3 ? MFDTheme.WARN : MFDTheme.STATUS_RDY;

                        MFDFrame.Txt(frame, tofText, centerX, startY + i * LINE_HEIGHT, TEXT_SCALE,
                            tofColor, MFDTheme.AC);
                    }
                }
            }

            // Gun Control Overlay (rendered on HUD surface, not weapon screen — kept as-is)
            private void DrawGunControlOverlay(MySpriteDrawFrame frame)
            {
                var gunControl = SystemManager.GetGunControl();
                if (gunControl == null || !gunControl.IsControlEnabled)
                    return;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;
                float viewportMin = Math.Min(surfaceSize.X, surfaceSize.Y);

                float coneRadius = viewportMin * 0.25f;

                SpriteHelpers.DrawCircleOutline(frame, center, coneRadius, new Color(100, 100, 100, 150), 2f);

                string statusText = "GUN AUTO-TRACK";
                Color statusColor = Color.Cyan;

                if (gunControl.IsLeftCalibrating || gunControl.IsRightCalibrating)
                {
                    statusText = "CALIBRATING...";
                    statusColor = Color.Yellow;
                }

                frame.Add(new MySprite()
                {
                    Type = MFDTheme.TT,
                    Data = statusText,
                    Position = new Vector2(center.X, center.Y - coneRadius - 30f),
                    RotationOrScale = 0.6f,
                    Color = statusColor,
                    Alignment = MFDTheme.AC,
                    FontId = MFDTheme.FONT_W
                });

                Vector2 leftIndicatorPos = new Vector2(center.X - coneRadius - 40f, center.Y);
                DrawTurretIndicator(frame, leftIndicatorPos, "L", gunControl.IsLeftTracking, gunControl.IsLeftCalibrating);

                Vector2 rightIndicatorPos = new Vector2(center.X + coneRadius + 40f, center.Y);
                DrawTurretIndicator(frame, rightIndicatorPos, "R", gunControl.IsRightTracking, gunControl.IsRightCalibrating);

                if (gunControl.IsLeftTracking && gunControl.IsRightTracking)
                {
                    frame.Add(new MySprite()
                    {
                        Type = MFDTheme.TT,
                        Data = "FIRE",
                        Position = new Vector2(center.X, center.Y + coneRadius + 20f),
                        RotationOrScale = 1.0f,
                        Color = Color.Red,
                        Alignment = MFDTheme.AC,
                        FontId = MFDTheme.FONT_W
                    });

                    int flashPhase = (radarSweepTick / 5) % 2;
                    if (flashPhase == 0)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = MFDTheme.TX,
                            Data = TEXTURE_CIRCLE_SOLID,
                            Position = center,
                            Size = new Vector2(20f, 20f),
                            Color = Color.Red,
                            Alignment = MFDTheme.AC
                        });
                    }
                }
                else if (gunControl.IsLeftTracking || gunControl.IsRightTracking)
                {
                    frame.Add(new MySprite()
                    {
                        Type = MFDTheme.TT,
                        Data = "TRACKING",
                        Position = new Vector2(center.X, center.Y + coneRadius + 20f),
                        RotationOrScale = 0.7f,
                        Color = Color.Yellow,
                        Alignment = MFDTheme.AC,
                        FontId = MFDTheme.FONT_W
                    });
                }
                else
                {
                    frame.Add(new MySprite()
                    {
                        Type = MFDTheme.TT,
                        Data = "SEARCHING",
                        Position = new Vector2(center.X, center.Y + coneRadius + 20f),
                        RotationOrScale = 0.6f,
                        Color = Color.Gray,
                        Alignment = MFDTheme.AC,
                        FontId = MFDTheme.FONT_W
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
                    bgColor = new Color(0, 100, 0, 200);
                    textColor = MFDTheme.ACCENT;
                    statusChar = "X";
                }
                else
                {
                    bgColor = new Color(30, 30, 30, 200);
                    textColor = MFDTheme.DIM_TEXT_MID;
                    statusChar = "O";
                }

                frame.Add(new MySprite()
                {
                    Type = MFDTheme.TX,
                    Data = TEXTURE_CIRCLE_SOLID,
                    Position = position,
                    Size = new Vector2(35f, 35f),
                    Color = bgColor,
                    Alignment = MFDTheme.AC
                });

                frame.Add(new MySprite()
                {
                    Type = MFDTheme.TT,
                    Data = label,
                    Position = position + new Vector2(0f, -18f),
                    RotationOrScale = 0.5f,
                    Color = textColor,
                    Alignment = MFDTheme.AC,
                    FontId = MFDTheme.FONT_W
                });

                frame.Add(new MySprite()
                {
                    Type = MFDTheme.TT,
                    Data = statusChar,
                    Position = position + new Vector2(0f, -5f),
                    RotationOrScale = 0.8f,
                    Color = textColor,
                    Alignment = MFDTheme.AC,
                    FontId = MFDTheme.FONT_W
                });
            }
        }
    }
}
