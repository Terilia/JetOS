namespace IngameScript
{
    partial class Program
    {
        // The stitched virtual canvas: the HQ MFD (left) and HQ MAP (right) treated as ONE
        // coordinate space so a single cursor can glide across both physical LCDs. A sprite can't
        // span two surfaces, so "stitching" is bookkeeping: virtual-x 0..LW lands on the left
        // surface, LW..LW+RW on the right. The cursor is drawn (and hit-tested) on whichever
        // surface its virtual-x falls in, remapped to that surface's local coords.
        //
        // Requires the two LCDs mounted side-by-side (MFD left / MAP right). If no HQ MAP block
        // exists the canvas degrades to the MFD alone (HasRight = false).
        static class Canvas
        {
            public static float LW, LH;   // left surface (MFD) size
            public static float RW, RH;   // right surface (MAP) size
            public static float W, H;     // virtual canvas: W = LW + RW, H = max(LH, RH)
            public static bool HasRight;  // a dedicated HQ MAP surface is present

            // Refresh sizes each tick (cheap — SurfaceSize is a field read).
            public static void Sync(Station st)
            {
                if (st.Mfd != null) { LW = SX(st.Mfd); LH = SY(st.Mfd); }
                else { LW = 512f; LH = 512f; }

                if (st.Map != null) { RW = SX(st.Map); RH = SY(st.Map); HasRight = true; }
                else { RW = 0f; RH = 0f; HasRight = false; }

                W = LW + RW;
                H = Mx(LH, RH);
            }

            // True if the virtual-x lands on the right (MAP) surface.
            public static bool OnRight(float vx) { return HasRight && vx >= LW; }
        }
    }
}
