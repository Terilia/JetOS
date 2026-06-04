using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Virtual mouse cursor over the stitched MFD+MAP canvas.
        //
        // SE gives a PB no cursor position and no mouse-button state — only per-tick mouse DELTA
        // (RotationIndicator) and the keyboard axes (MoveIndicator). So we synthesize the cursor by
        // integrating the delta, and we synthesize a LEFT-CLICK by watching the "HQ Click Gun"
        // weapon's ammo count drop the tick it fires (left-click fires the seat's selected toolbar
        // weapon). Secondary (undo/cancel) = the C key. If no click gun is present, Space stands in
        // for the primary click. All input is zero unless the operator is seated (RotationIndicator
        // is suppressed while any terminal/menu is open) — so the cursor is a seated-only activity.
        static class MouseCursor
        {
            // ── Tunables (Z1 is for feeling these out in-game) ──
            const float SENS = 0.55f;                 // canvas px per RotationIndicator unit
            static readonly bool HOLD_TO_AIM = false; // when true the cursor only moves while W is held (curbs camera drift)
            static readonly bool INVERT_Y = false;    // flip vertical if up/down feels reversed
            public static float X, Y;        // virtual canvas coords
            public static bool  Visible;
            public static bool  PrimaryClick, PrimaryHeld;     // left-click = Space: edge + held
            public static bool  SecondaryClick, SecondaryHeld; // C key: edge + held

            static bool _prevPrimary, _prevC;
            static bool _init;

            // Re-seat the cursor + re-baseline the click detectors when the cursor (re)activates.
            // Baseline from the CURRENT input state so a key already held on entry (Space/C) doesn't
            // read as a fresh edge and fire a spurious click.
            static void Activate(Station st)
            {
                Canvas.Sync(st);
                X = Canvas.HasRight ? Canvas.LW + Canvas.RW * 0.5f : Canvas.W * 0.5f;
                Y = Canvas.H * 0.5f;
                Vector3 mov = st.Move;
                _prevPrimary = mov.Y > 0.5f;   // Space already held → not a new click
                _prevC = mov.Y < -0.5f;        // C already held → not a new click
                _init = true;
            }

            // Called every non-ZONE tick so re-entry re-centers.
            public static void Deactivate()
            {
                Visible = false;
                PrimaryClick = PrimaryHeld = SecondaryClick = SecondaryHeld = false;
                _init = false;
            }

            public static void Tick(Station st, double dt)
            {
                Canvas.Sync(st);
                if (!st.SeatControlled)
                {
                    Visible = false;
                    PrimaryClick = PrimaryHeld = SecondaryClick = SecondaryHeld = false;
                    return;
                }
                if (!_init) Activate(st);
                Visible = true;

                Vector2 rot = st.Rot;   // X = mouse pitch Δ, Y = mouse yaw Δ
                Vector3 mov = st.Move;  // X = A/D, Y = C/Space, Z = S/W (W = -Z)

                bool aim = !HOLD_TO_AIM || mov.Z < -0.5f;
                if (aim)
                {
                    X = Cl(X + rot.Y * SENS, 0f, Canvas.W);
                    float dy = rot.X * SENS;
                    if (INVERT_Y) dy = -dy;
                    Y = Cl(Y + dy, 0f, Canvas.H);
                }

                // ── Primary click = Space, secondary = C (both edge-triggered) ──
                bool space = mov.Y > 0.5f;
                PrimaryHeld = space;
                PrimaryClick = space && !_prevPrimary;
                _prevPrimary = space;

                bool c = mov.Y < -0.5f;
                SecondaryHeld = c;
                SecondaryClick = c && !_prevC;
                _prevC = c;
            }

            // ── Cursor draw (called inside each surface's open SpriteBus frame) ──
            public static void DrawLeft(float sw, float sh)
            {
                if (!Visible || Canvas.OnRight(X)) return;
                DrawAt(Cl(X, 0f, sw), Cl(Y, 0f, sh), sh / 512f);
            }

            public static void DrawRight(float sw, float sh)
            {
                if (!Visible || !Canvas.OnRight(X)) return;
                DrawAt(Cl(X - Canvas.LW, 0f, sw), Cl(Y, 0f, sh), sh / 512f);
            }

            // A small open crosshair (4 ticks + center diamond), gold while the button is held.
            static void DrawAt(float x, float y, float k)
            {
                Color c = PrimaryHeld ? MFDTheme.WARN : MFDTheme.BRIGHT_TEXT;
                float r = 9f * k, g = 3f * k, t = 1.6f * k;
                SpriteHelpers.AddLineSprite(V2(x - r, y), V2(x - g, y), t, c);
                SpriteHelpers.AddLineSprite(V2(x + g, y), V2(x + r, y), t, c);
                SpriteHelpers.AddLineSprite(V2(x, y - r), V2(x, y - g), t, c);
                SpriteHelpers.AddLineSprite(V2(x, y + g), V2(x, y + r), t, c);
                SpriteHelpers.Sp(TEX_LOCK_DIAMOND, x, y, 4f * k, 4f * k, c);
            }
        }
    }
}
