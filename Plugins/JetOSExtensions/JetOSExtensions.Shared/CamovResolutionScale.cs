using System;

namespace JetOSExtensions.Shared
{
    public static class CamovResolutionScale
    {
        public const int DefaultPercent = 100;
        public const int MinPercent = 100;
        public const int MaxPercent = 400;
        public const int Step = 25;

        public static int NormalizePercent(int percent)
        {
            int clamped = Math.Max(MinPercent, Math.Min(MaxPercent, percent));
            int rounded = (int)(Math.Round((double)clamped / Step) * Step);
            return Math.Max(MinPercent, Math.Min(MaxPercent, rounded));
        }

        public static int ScaleDimension(int value, int percent)
        {
            int normalized = NormalizePercent(percent);
            return Math.Max(1, (int)Math.Ceiling(value * normalized / 100d));
        }

        public static string FormatLabel(int percent)
        {
            int p = NormalizePercent(percent);
            int whole = p / 100;
            int frac = p % 100;
            if (frac == 0) return whole + "x";
            if (frac == 50) return whole + ".5x";
            if (frac == 25) return whole + ".25x";
            if (frac == 75) return whole + ".75x";
            return whole + "." + frac + "x";
        }

        public static string FormatLabelWithDimensions(int percent, int baseWidth, int baseHeight)
        {
            int p = NormalizePercent(percent);
            int w = ScaleDimension(baseWidth, p);
            int h = ScaleDimension(baseHeight, p);
            return FormatLabel(p) + " (" + w + "x" + h + ")";
        }
    }
}
