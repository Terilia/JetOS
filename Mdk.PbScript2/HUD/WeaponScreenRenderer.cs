using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        partial class HUDModule
        {
            // Lock-acquisition flash state — set when IsTrackLocked transitions false→true.
            private bool _wasLocked = false;
            private double _lockAcquiredAt = -1;
            private const double LOCK_FLASH_DURATION = 0.20;
            private string _lastSelectedContactKey = "";
            private double _selectedChangedAt = -1;
            private bool[] _lastBayReady = new bool[0];
            private double[] _bayChangedAt = new double[0];
            private const double TARGET_PANEL_FLASH_DURATION = 0.22;
            private const double BAY_READY_FLASH_DURATION = 0.22;

            // Renders weapon-screen content into the supplied frame/area. Chrome is drawn
            // by the host MfdPage; this method only fills the inner content rect.
            internal void RenderWeaponContent(MySpriteDrawFrame frame, RectangleF contentArea, Vector2 surfaceSize)
            {
                if (cockpit == null) return;
                Vector3D shooterPosition = GP(cockpit);
                Vector3D currentVelocity = LV(cockpit);

                // Detect lock-acquired transition (rising edge only).
                bool nowLocked = radarControl != null && radarControl.IsTrackLocked;
                if (nowLocked && !_wasLocked) _lockAcquiredAt = SystemManager.ElapsedSeconds;
                _wasLocked = nowLocked;

                {
                    float sw = surfaceSize.X;
                    float sh = surfaceSize.Y;
                    float margin = sw * 0.019f;

                    float contentY = contentArea.Position.Y;
                    float contentBot = contentArea.Position.Y + contentArea.Height;
                    float padX = margin + 4f;

                    // ── Section: TARGET LIST ──
                    float secY = contentY + 6f;
                    MFDFrame.Txt(frame, "TGT", sw / 2f, secY, 0.45f, MFDTheme.MID_TEXT, MFDTheme.AC);
                    secY += 16f;

                    // ── Selected target detail box ──
                    float detailH = 95f;
                    var selected = myjet.GetSelectedEnemy();
                    string selectedKey = selected.HasValue ? ContactKey(selected.Value) : "";
                    if (selectedKey != _lastSelectedContactKey)
                    {
                        _lastSelectedContactKey = selectedKey;
                        _selectedChangedAt = SystemManager.ElapsedSeconds;
                    }
                    double selectedT = TransitionT(_selectedChangedAt, TARGET_PANEL_FLASH_DURATION);
                    Color detailBorder = selectedT < 1
                        ? Anim.LerpColor(selected.HasValue ? MFDTheme.ACCENT : MFDTheme.DIM_TEXT_MID,
                            MFDTheme.BORDER_LIGHT, Anim.EaseOut(selectedT))
                        : MFDTheme.BORDER_LIGHT;

                    MFDFrame.Rect(frame, sw / 2f, secY + detailH / 2f, sw - margin * 2, detailH, MFDTheme.PANEL_BG);
                    SpriteHelpers.DrawRectangleOutline(frame, margin, secY, sw - margin * 2, detailH, 1f, detailBorder);

                    if (selected.HasValue)
                    {
                        DrawSelectedTargetDetail(frame, selected.Value, shooterPosition, currentVelocity, margin, secY, sw);
                    }
                    else
                    {
                        Color noTargetColor = selectedT < 1
                            ? Anim.LerpColor(MFDTheme.DIM_TEXT_MID, MFDTheme.DIM_TEXT, Anim.EaseOut(selectedT))
                            : MFDTheme.DIM_TEXT;
                        MFDFrame.Txt(frame, "NO TGT", sw / 2f, secY + detailH / 2f - 12f, 1.0f,
                            noTargetColor, MFDTheme.AC);
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
                        MFDFrame.Txt(frame, "NO TGT", sw / 2f, secY + 10f, 0.6f,
                            MFDTheme.DIM_TEXT, MFDTheme.AC);
                    }

                    // ── Bottom section: bay strip ──
                    int bayCount = myjet._bays != null ? myjet._bays.Count : 0;
                    float bayH = bayCount > 8 ? 76f : bayCount > 0 ? 52f : 0f;
                    float bottomY = contentBot - bayH;

                    if (bayCount > 0)
                    {
                        DrawBayStrip(frame, sw / 2f, bottomY + bayH / 2f, myjet._bays);
                    }
                }
            }

            private void DrawSelectedTargetDetail(MySpriteDrawFrame frame, Jet.EnemyContact contact, Vector3D shooterPosition, Vector3D currentVelocity, float margin, float panelY, float screenWidth)
            {
                float textX = margin + 8f;
                float textY = panelY + 6f;
                float rightX = screenWidth - margin - 8f;

                // Row 1: Name + track mode badge
                string name = contact.Name;
                if (SE(name)) name = "UNK";
                if (name.Length > 14) name = name.Substring(0, 14);

                bool stale = contact.IsStale;
                Color nameColor = stale ? MFDTheme.DIM_TEXT_MID : MFDTheme.BRIGHT_TEXT;
                // Lock-acquired flash: lerp from ACCENT (bright green pulse) back to nameColor over the flash window.
                if (!stale && _lockAcquiredAt >= 0)
                {
                    double t = (SystemManager.ElapsedSeconds - _lockAcquiredAt) / LOCK_FLASH_DURATION;
                    if (t < 1) nameColor = Anim.LerpColor(MFDTheme.ACCENT, nameColor, Anim.EaseOut(t));
                }
                MFDFrame.Txt(frame, name, textX, textY, 0.7f, nameColor);

                bool isSTT = radarControl != null && radarControl.IsTrackLocked;
                string badgeText = stale ? "STALE" : isSTT ? "STT" : "TWS";
                Color badgeColor = stale ? MFDTheme.DIM_TEXT : isSTT ? MFDTheme.ACCENT : MFDTheme.STATUS_RDY;

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
                string closureText = $"{Ab(closureRate):F0} {closureLabel}";
                Color closureColor = closureRate > 10 ? MFDTheme.WARN : closureRate < -10 ? Cr(80, 110, 200) : MFDTheme.DIM_TEXT_MID;
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
                        Color segC = runIsOk ? MFDTheme.ACCENT : MFDTheme.DANGER;

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

                int maxRows = (int)((contentBot - startY - 10f) / LINE_HEIGHT);
                maxRows = Mn(maxRows, 10);

                for (int i = 0; i < Mn(maxRows, enemies.Count); i++)
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

                    Color contactColor = contact.IsStale
                        ? (isSelected ? MFDTheme.DIM_TEXT_MID : MFDTheme.DIM_TEXT)
                        : (isSelected ? MFDTheme.BRIGHT_TEXT : myjet.GetEnemyContactColor(contact));

                    string marker = isSelected ? "\u25C9" : "\u25CB";
                    MFDFrame.Txt(frame, marker, textX, textY, TEXT_SCALE, contactColor);

                    string cName = contact.Name;
                    if (SE(cName)) cName = "UNK";
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
                        MFDFrame.Txt(frame, $"+{enemies.Count - maxRows}", screenWidth / 2f, textY, 0.45f,
                        MFDTheme.DIM_TEXT, MFDTheme.AC);
                }
            }

            private bool IsContactSelected(Jet.EnemyContact contact, Jet.EnemyContact? selected)
            {
                if (!selected.HasValue) return false;
                return contact.Matches(selected.Value);
            }

            private static string ContactKey(Jet.EnemyContact contact)
            {
                if (contact.EntityId != 0) return contact.EntityId.ToString();
                return (contact.Name ?? "") + ":" + contact.SourceIndex.ToString();
            }

            private static double TransitionT(double startedAt, double duration)
            {
                if (startedAt < 0 || duration <= 0) return 1;
                double t = (SystemManager.ElapsedSeconds - startedAt) / duration;
                if (t < 0) return 0;
                if (t > 1) return 1;
                return t;
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

                Vector3D forwardHorizontal = Vector3D.Reject(WF(cockpit), worldUp);
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

            // Bay status strip \u2014 5:4-ish bay icons, filled when missile attached.
            private void DrawBayStrip(MySpriteDrawFrame frame, float centerX, float y, List<IMyShipMergeBlock> bays)
            {
                int n = Mn(bays.Count, 12);
                if (n == 0) return;
                EnsureBayTransitionState(bays, n);
                bool twoRows = n > 8;
                int cols = twoRows ? 6 : n;
                float W = twoRows ? 40f : 54f, H = twoRows ? 28f : 42f, SP = twoRows ? 4f : 6f, RG = 4f;
                float totalW = cols * W + (cols - 1) * SP;
                float startX = centerX - totalW / 2f + W / 2f;
                float topY = y - (twoRows ? H + RG / 2f : 0f);
                for (int i = 0; i < n; i++)
                {
                    bool loaded = MissileBayHelper.IsBayReady(bays[i]);
                    if (loaded != _lastBayReady[i])
                    {
                        _lastBayReady[i] = loaded;
                        _bayChangedAt[i] = SystemManager.ElapsedSeconds;
                    }

                    string tex = loaded ? TEX_BAY_LOADED : TEX_BAY_EMPTY;
                    Color c = loaded ? MFDTheme.STATUS_RDY : MFDTheme.DIM_TEXT_MID;
                    double t = TransitionT(_bayChangedAt[i], BAY_READY_FLASH_DURATION);
                    if (t < 1)
                    {
                        Color flash = loaded ? MFDTheme.ACCENT : MFDTheme.WARN;
                        c = Anim.LerpColor(flash, c, Anim.EaseOut(t));
                    }
                    float x = startX + (i % cols) * (W + SP);
                    float by = twoRows ? topY + (i / cols) * (H + RG) : y;
                    SpriteHelpers.Sp(frame, tex, x, by, W, H, c);
                    int bayNum = MissileBayHelper.GetBayNumber(bays[i], i + 1);
                    MissileBayHelper.MissileStatus ms;
                    if (!loaded && MissileBayHelper.TryGetMissileStatus(bayNum, out ms))
                    {
                        string eta = MissileBayHelper.FormatEta(ms.Eta);
                        Color tc = ms.Eta >= 0 && ms.Eta < 5 ? MFDTheme.DANGER :
                            ms.ActiveTrackingUnlocked ? MFDTheme.BRIGHT_TEXT :
                            ms.Acquired ? MFDTheme.ACCENT : MFDTheme.WARN;
                        float ts = twoRows ? 0.72f : 1.0f;
                        float ty = by - (twoRows ? 10f : 14f);
                        MFDFrame.Txt(frame, eta, x + 1f, ty + 1f, ts, Cr(0, 0, 0, 210), MFDTheme.AC);
                        MFDFrame.Txt(frame, eta, x, ty, ts, tc, MFDTheme.AC);
                        if (ms.ActiveTrackingUnlocked)
                        {
                            float labelY = by + (twoRows ? 8f : 12f);
                            MFDFrame.Txt(frame, "AI", x + 1f, labelY + 1f, 0.34f, Cr(0, 0, 0, 220), MFDTheme.AC);
                            MFDFrame.Txt(frame, "AI", x, labelY, 0.34f, MFDTheme.ACCENT, MFDTheme.AC);
                        }
                    }
                }
            }

            private void EnsureBayTransitionState(List<IMyShipMergeBlock> bays, int n)
            {
                if (_lastBayReady != null && _lastBayReady.Length == n
                    && _bayChangedAt != null && _bayChangedAt.Length == n)
                    return;

                _lastBayReady = new bool[n];
                _bayChangedAt = new double[n];
                for (int i = 0; i < n; i++)
                {
                    _lastBayReady[i] = MissileBayHelper.IsBayReady(bays[i]);
                    _bayChangedAt[i] = -1;
                }
            }

            // Gun Control Overlay (rendered on HUD surface, not weapon screen — kept as-is)
            private void DrawGunControlOverlay(MySpriteDrawFrame frame)
            {
                var gunControl = SystemManager.GetGunControl();
                if (gunControl == null || !gunControl.IsControlEnabled)
                    return;

                Vector2 surfaceSize = SS(hud);
                Vector2 center = surfaceSize / 2f;
                float viewportMin = Mn(surfaceSize.X, surfaceSize.Y);

                float coneRadius = viewportMin * 0.25f;

                SpriteHelpers.DrawCircleOutline(frame, center, coneRadius, Cr(100, 100, 100, 150), 2f);

                SpriteHelpers.Tt(frame, "GUN AUTO", center.X, center.Y - coneRadius - 30f, 0.6f, HUD_PRIMARY, MFDTheme.AC, MFDTheme.FONT_W);

                Vector2 leftIndicatorPos = V2(center.X - coneRadius - 40f, center.Y);
                DrawTurretIndicator(frame, leftIndicatorPos, "L", gunControl.IsLeftTracking);

                Vector2 rightIndicatorPos = V2(center.X + coneRadius + 40f, center.Y);
                DrawTurretIndicator(frame, rightIndicatorPos, "R", gunControl.IsRightTracking);

                if (gunControl.IsLeftTracking && gunControl.IsRightTracking)
                {
                    SpriteHelpers.Tt(frame, "FIRE", center.X, center.Y + coneRadius + 20f, 1.0f, HUD_WARNING, MFDTheme.AC, MFDTheme.FONT_W);

                    if (Anim.Blink(0.17))
                    {
                        SpriteHelpers.Sp(frame, TEXTURE_CIRCLE_SOLID, center.X, center.Y, 20f, 20f, HUD_WARNING);
                    }
                }
                else if (gunControl.IsLeftTracking || gunControl.IsRightTracking)
                {
                    SpriteHelpers.Tt(frame, "TRACK", center.X, center.Y + coneRadius + 20f, 0.7f, HUD_EMPHASIS, MFDTheme.AC, MFDTheme.FONT_W);
                }
                else
                {
                    SpriteHelpers.Tt(frame, "SRCH", center.X, center.Y + coneRadius + 20f, 0.6f, HUD_SECONDARY, MFDTheme.AC, MFDTheme.FONT_W);
                }
            }

            private void DrawTurretIndicator(MySpriteDrawFrame frame, Vector2 position, string label, bool isLocked)
            {
                Color bgColor;
                Color textColor;
                string statusChar;

                if (isLocked)
                {
                    bgColor = Cr(0, 100, 0, 200);
                    textColor = MFDTheme.ACCENT;
                    statusChar = "X";
                }
                else
                {
                    bgColor = Cr(30, 30, 30, 200);
                    textColor = MFDTheme.DIM_TEXT_MID;
                    statusChar = "O";
                }

                SpriteHelpers.Sp(frame, TEXTURE_CIRCLE_SOLID, position.X, position.Y, 35f, 35f, bgColor);
                SpriteHelpers.Tt(frame, label, position.X, position.Y - 18f, 0.5f, textColor, MFDTheme.AC, MFDTheme.FONT_W);
                SpriteHelpers.Tt(frame, statusChar, position.X, position.Y - 5f, 0.8f, textColor, MFDTheme.AC, MFDTheme.FONT_W);
            }
        }
    }
}
