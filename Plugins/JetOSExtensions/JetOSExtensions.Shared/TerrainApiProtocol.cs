using System;
using System.Globalization;

namespace JetOSExtensions.Shared
{
    public static class TerrainApiProtocol
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public const string PropertyName = "TerrainAPI";
        public const int HeightOffset = 32768;

        public static string FormatProbeCommand(double cellSize)
        {
            return "P;" + cellSize.ToString("G8", Inv);
        }

        public static string FormatChunkCommand(int offset, int count)
        {
            return "C;" + offset.ToString(Inv) + ";" + count.ToString(Inv);
        }

        public static bool TryParseProbeResponse(string response, out TerrainProbe probe)
        {
            probe = default(TerrainProbe);
            if (string.IsNullOrWhiteSpace(response))
                return false;

            string[] parts = response.Split(';');
            if (parts.Length != 8 || parts[0] != "P")
                return false;

            int rows;
            int cols;
            double cellSize;
            double meanRadius;
            double pcX;
            double pcY;
            double pcZ;
            if (!int.TryParse(parts[1], NumberStyles.Integer, Inv, out rows) ||
                !int.TryParse(parts[2], NumberStyles.Integer, Inv, out cols) ||
                !double.TryParse(parts[3], NumberStyles.Float, Inv, out cellSize) ||
                !double.TryParse(parts[4], NumberStyles.Float, Inv, out meanRadius) ||
                !double.TryParse(parts[5], NumberStyles.Float, Inv, out pcX) ||
                !double.TryParse(parts[6], NumberStyles.Float, Inv, out pcY) ||
                !double.TryParse(parts[7], NumberStyles.Float, Inv, out pcZ))
                return false;

            probe = new TerrainProbe(rows, cols, cellSize, meanRadius, pcX, pcY, pcZ);
            return true;
        }
    }

    public readonly struct TerrainProbe
    {
        public TerrainProbe(int rows, int columns, double cellSize, double meanRadius, double planetCenterX, double planetCenterY, double planetCenterZ)
        {
            Rows = rows;
            Columns = columns;
            CellSize = cellSize;
            MeanRadius = meanRadius;
            PlanetCenterX = planetCenterX;
            PlanetCenterY = planetCenterY;
            PlanetCenterZ = planetCenterZ;
        }

        public int Rows { get; }
        public int Columns { get; }
        public double CellSize { get; }
        public double MeanRadius { get; }
        public double PlanetCenterX { get; }
        public double PlanetCenterY { get; }
        public double PlanetCenterZ { get; }
    }
}
