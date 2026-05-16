using JetOSExtensions.Shared;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{name}: expected '{expected}', got '{actual}'");
}

static void True(bool value, string name)
{
    if (!value)
        throw new Exception($"{name}: expected true");
}

Equal("JetOSRadarFeed", RadarFeedProtocol.PropertyName, "radar terminal property");
Equal("JORAD", RadarFeedProtocol.Header, "radar header");
Equal(3, RadarFeedProtocol.FeedVersion, "radar feed version");
Equal("JORAD|3|42\n", RadarFeedProtocol.EmptyFeed(42), "empty radar feed");
Equal("R|H|42|1|2|3|4|5|6|Target Name", RadarFeedProtocol.FormatContactLine('H', 42, "Target|Name", 1, 2, 3, 4, 5, 6), "contact format sanitizes pipes");

Equal("TerrainAPI", TerrainApiProtocol.PropertyName, "terrain terminal property");
Equal("P;25", TerrainApiProtocol.FormatProbeCommand(25), "probe command");
Equal("C;120;50", TerrainApiProtocol.FormatChunkCommand(120, 50), "chunk command");
True(TerrainApiProtocol.TryParseProbeResponse("P;80;40;25;60000;1;2;3", out TerrainProbe probe), "probe parse succeeds");
Equal(80, probe.Rows, "probe rows");
Equal(40, probe.Columns, "probe columns");
Equal(25d, probe.CellSize, "probe cell");
Equal(60000d, probe.MeanRadius, "probe mean radius");
Equal(1d, probe.PlanetCenterX, "probe center x");
Equal(2d, probe.PlanetCenterY, "probe center y");
Equal(3d, probe.PlanetCenterZ, "probe center z");

Console.WriteLine("JetOSExtensions helper tests passed.");
