using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;

namespace IngameScript
{
    partial class Program
    {
        // Central chokepoint for every sprite the MFD helpers emit. Routes sprites to the
        // currently-active surface frame AND optionally tees them into a capture list so
        // we can replay them next tick with per-sprite transforms (the "shader" effect).
        //
        // Usage:
        //   SpriteBus.Begin(frame, captureList);   // captureList may be null
        //   helpers call SpriteBus.Add(sprite)
        //   SpriteBus.End();
        //   frame.Dispose();
        //
        // Any code paths that still use frame.Add directly (HorizonRenderer on the HUD
        // glass, GridVisualization's cached-sprite replay on MFD-2) will simply not be
        // captured — that's fine, neither surface uses the transition system.
        static class SpriteBus
        {
            private static MySpriteDrawFrame _frame;
            private static bool _active;
            private static List<MySprite> _capture;

            public static void Begin(MySpriteDrawFrame frame, List<MySprite> captureInto = null)
            {
                _frame = frame;
                _active = true;
                _capture = captureInto;
            }

            public static void Add(MySprite sprite)
            {
                if (!_active) return;
                _frame.Add(sprite);
                if (_capture != null) _capture.Add(sprite);
            }

            // Writes directly to the current frame without capturing. Used by the
            // transition replay so replayed sprites don't leak into next tick's capture.
            public static void AddRaw(MySprite sprite)
            {
                if (!_active) return;
                _frame.Add(sprite);
            }

            public static void End()
            {
                _active = false;
                _capture = null;
            }
        }
    }
}
