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

True(CamovSurfaceProtocol.IsForcedSurface("0:Eye:Forced\r\n1:HUD", 0), "forced surface detects marker");
True(CamovSurfaceProtocol.IsForcedSurface("0:Eye\r\n1:HUD:forced", 1), "forced surface is case insensitive");
Equal(false, CamovSurfaceProtocol.IsForcedSurface("0:Eye:Forced\r\n1:HUD", 1), "forced surface is per surface");
Equal(false, CamovSurfaceProtocol.IsForcedSurface("10:Eye:Forced", 1), "forced surface prefix is exact");

var spriteBuffer = new List<string> { "old-a", "old-b", "old-c" };
CamovSpriteDeltas.ApplyIndexedDelta(
    spriteBuffer,
    3,
    new[] { (Index: 1, Value: "new-b") },
    item => item.Index,
    item => item.Value);
Equal(3, spriteBuffer.Count, "sprite delta keeps length");
Equal("old-a", spriteBuffer[0], "sprite delta preserves index 0");
Equal("new-b", spriteBuffer[1], "sprite delta updates changed index");
Equal("old-c", spriteBuffer[2], "sprite delta preserves index 2");

CamovSpriteDeltas.ApplyIndexedDelta(
    spriteBuffer,
    2,
    new[] { (Index: 5, Value: "ignored") },
    item => item.Index,
    item => item.Value);
Equal(2, spriteBuffer.Count, "sprite delta shrinks to advertised length");
Equal("new-b", spriteBuffer[1], "sprite delta ignores out of range index");

Console.WriteLine("JetOSExtensions helper tests passed.");
