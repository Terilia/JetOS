namespace IngameScript
{
    partial class Program
    {
        // Decoder for the jet's packed STATUS word (built jet-side in DatalinkV2.BuildStatusWord).
        // Bit layout (see docs/datalink-v2.md §4):
        //   [0..6] fuel%  [7..13] battery%  [14..20] integrity%  [21..24] missiles
        //   [25..27] gun bucket  [28..31] state  [32..43] altitude/8  [44..55] flags
        static class StatusWord
        {
            public static int Fuel(long w)     => (int)(w & 127);
            public static int Batt(long w)     => (int)((w >> 7) & 127);
            public static int Integ(long w)    => (int)((w >> 14) & 127);
            public static int Missiles(long w) => (int)((w >> 21) & 15);
            public static int Gun(long w)      => (int)((w >> 25) & 7);
            public static int State(long w)    => (int)((w >> 28) & 15);
            public static int Alt(long w)      => (int)(((w >> 32) & 4095) * 8);
            public static int Flags(long w)    => (int)((w >> 44) & 4095);

            // Flag bits: 0 RWR active · 1 being locked · 2 bingo fuel · 3 altitude warning.
            public static bool Rwr(long w)     => (Flags(w) & 1) != 0;
            public static bool Spiked(long w)  => (Flags(w) & 2) != 0;
            public static bool Bingo(long w)   => (Flags(w) & 4) != 0;
            public static bool AltWarn(long w) => (Flags(w) & 8) != 0;

            // State enum: 1 CRUISE · 2 ENGAGING · 3 DEFENDING · 5 BINGO. 0 = unknown.
            public static string StateStr(int s)
            {
                switch (s)
                {
                    case 1: return "CRU";
                    case 2: return "ENG";
                    case 3: return "DEF";
                    case 5: return "BNG";
                    default: return "---";
                }
            }
        }
    }
}
