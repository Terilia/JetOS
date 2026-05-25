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
Equal(false, CamovSurfaceProtocol.IsForcedSurface("0::Forced", 0), "forced marker requires a camera selection");
True(CamovSurfaceProtocol.HasCameraSelection("0:Eye\r\n1:HUD", 0), "surface camera selection is detected");
Equal(false, CamovSurfaceProtocol.HasCameraSelection("0:\r\n1:HUD", 0), "empty surface camera selection is ignored");
True(CamovSurfaceProtocol.UsesForcedMode("0:Eye\r\n1:HUD", 0, commonTssSet: true, cameraSelected: true), "common TSS with selected camera implies forced mode");
Equal(false, CamovSurfaceProtocol.UsesForcedMode("0:Eye\r\n1:HUD", 0, commonTssSet: true, cameraSelected: false), "common TSS without selected camera does not force");
Equal(false, CamovSurfaceProtocol.UsesForcedMode("0:Eye\r\n1:HUD", 0, commonTssSet: false, cameraSelected: true), "camera selection alone does not force without common TSS");
True(CamovSurfaceProtocol.UsesForcedMode("0:Eye:Forced\r\n1:HUD", 0, commonTssSet: false, cameraSelected: true), "forced marker with selected camera implies forced mode");
Equal(false, CamovSurfaceProtocol.UsesForcedMode("0:Eye:Forced\r\n1:HUD", 0, commonTssSet: false, cameraSelected: false), "forced marker without selected camera does not force runtime mode");

Equal(100, CamovResolutionScale.NormalizePercent(100), "scale keeps 100");
Equal(125, CamovResolutionScale.NormalizePercent(125), "scale keeps 125");
Equal(150, CamovResolutionScale.NormalizePercent(150), "scale keeps 150");
Equal(200, CamovResolutionScale.NormalizePercent(200), "scale keeps 200");
Equal(400, CamovResolutionScale.NormalizePercent(400), "scale keeps 400");
Equal(175, CamovResolutionScale.NormalizePercent(175), "scale keeps 175 (now a valid step)");
Equal(125, CamovResolutionScale.NormalizePercent(137), "scale snaps 137 down to 125");
Equal(150, CamovResolutionScale.NormalizePercent(138), "scale snaps 138 up to 150");
Equal(100, CamovResolutionScale.NormalizePercent(50), "scale clamps below min to 100");
Equal(400, CamovResolutionScale.NormalizePercent(500), "scale clamps above max to 400");
Equal(100, CamovResolutionScale.NormalizePercent(99), "scale clamps 99 to 100");
Equal(400, CamovResolutionScale.NormalizePercent(413), "scale clamps 413 to 400");
Equal(768, CamovResolutionScale.ScaleDimension(512, 150), "scale 512 at 150%");
Equal(1024, CamovResolutionScale.ScaleDimension(512, 200), "scale 512 at 200%");
Equal(640, CamovResolutionScale.ScaleDimension(512, 125), "scale 512 at 125%");
Equal(770, CamovResolutionScale.ScaleDimension(513, 150), "scale rounds fractional pixels up");
Equal("1x", CamovResolutionScale.FormatLabel(100), "label 100%");
Equal("1.25x", CamovResolutionScale.FormatLabel(125), "label 125%");
Equal("1.5x", CamovResolutionScale.FormatLabel(150), "label 150%");
Equal("1.75x", CamovResolutionScale.FormatLabel(175), "label 175%");
Equal("2x", CamovResolutionScale.FormatLabel(200), "label 200%");
Equal("3x", CamovResolutionScale.FormatLabel(300), "label 300%");
Equal("4x", CamovResolutionScale.FormatLabel(400), "label 400%");
Equal("2x (1024x1024)", CamovResolutionScale.FormatLabelWithDimensions(200, 512, 512), "label with dimensions at 2x square");
Equal("1.5x (768x768)", CamovResolutionScale.FormatLabelWithDimensions(150, 512, 512), "label with dimensions at 1.5x square");
Equal("1x (512x512)", CamovResolutionScale.FormatLabelWithDimensions(100, 512, 512), "label with dimensions at 1x square");
Equal("2x (2048x1024)", CamovResolutionScale.FormatLabelWithDimensions(200, 1024, 512), "label with dimensions at 2x wide panel");

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
