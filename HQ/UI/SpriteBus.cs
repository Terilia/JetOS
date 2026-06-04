using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;

namespace IngameScript
{
    partial class Program
    {
        // Central chokepoint for every sprite the MFD helpers emit. Routes sprites to the
        // currently-active surface frame AND optionally tees them into a capture list so we
        // can replay them next tick with per-sprite transforms (the page-transition "shader").
        // Ported from the jet's UI/SpriteBus.cs.
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

            // Writes directly to the current frame without capturing (transition replay).
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
