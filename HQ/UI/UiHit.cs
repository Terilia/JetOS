using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        // Immediate-mode click regions for the MFD menu so the global mouse cursor can click menu
        // rows. UIController.DrawMenuList registers a region per visible row each render;
        // SystemManager hit-tests on a primary click. Rects are in absolute MFD-surface px.
        static class UiHit
        {
            struct Row { public float X, Y, W, H; public int Index; }
            static readonly List<Row> _rows = new List<Row>();

            public static void Clear() { _rows.Clear(); }
            public static void AddRow(float x, float y, float w, float h, int index)
            {
                _rows.Add(new Row { X = x, Y = y, W = w, H = h, Index = index });
            }
            public static int Hit(float px, float py)
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    Row r = _rows[i];
                    if (px >= r.X && px <= r.X + r.W && py >= r.Y && py <= r.Y + r.H) return r.Index;
                }
                return -1;
            }
        }
    }
}
