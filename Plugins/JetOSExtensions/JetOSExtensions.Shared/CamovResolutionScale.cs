using System;

namespace JetOSExtensions.Shared
{
    public static class CamovResolutionScale
    {
        public const int DefaultPercent = 100;
        public static readonly int[] AllowedPercents = { 100, 150, 200, 400 };

        public static int NormalizePercent(int percent)
        {
            for (int i = 0; i < AllowedPercents.Length; i++)
            {
                if (AllowedPercents[i] == percent)
                    return percent;
            }

            return DefaultPercent;
        }

        public static int ScaleDimension(int value, int percent)
        {
            int normalized = NormalizePercent(percent);
            return Math.Max(1, (int)Math.Ceiling(value * normalized / 100d));
        }

        public static string FormatLabel(int percent)
        {
            switch (NormalizePercent(percent))
            {
                case 150:
                    return "1.5x";
                case 200:
                    return "2x";
                case 400:
                    return "4x";
                default:
                    return "1x";
            }
        }
    }
}
