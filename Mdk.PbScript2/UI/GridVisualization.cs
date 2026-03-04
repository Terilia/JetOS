using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class GridVisualization
        {
            private static int blockcount = 0;
            private static int originalBlockCount = 0;
            private static List<IMyTerminalBlock> gridBlocks = new List<IMyTerminalBlock>();
            private static List<MySprite> cachedSprites = new List<MySprite>();
            private static int blockCacheRefreshTick = 0;
            private const int BLOCK_CACHE_REFRESH_INTERVAL = 60;

            // Fuel thresholds (read from config)
            private static double BINGO_FUEL_PERCENT => SystemManager.GetConfigValue("bingo_fuel");
            private static double LOW_FUEL_PERCENT => SystemManager.GetConfigValue("low_fuel");

            // Damage color thresholds
            private const float HEALTHY_THRESHOLD = 0.80f;
            private const float DAMAGED_THRESHOLD = 0.30f;

            // Colors
            private static readonly Color COLOR_HEALTHY = new Color(50, 255, 50);
            private static readonly Color COLOR_DAMAGED = Color.Yellow;
            private static readonly Color COLOR_CRITICAL = Color.Red;
            private static readonly Color COLOR_NONFUNCTIONAL = new Color(139, 0, 0);

            public static void Render(MySpriteDrawFrame frame, RectangleF renderArea,
                Program program, Jet jet, RadarControlModule radarModule)
            {
                // Periodically refresh block cache
                if (blockCacheRefreshTick <= 0 || gridBlocks.Count == 0)
                {
                    gridBlocks.Clear();
                    program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(gridBlocks);
                    blockCacheRefreshTick = BLOCK_CACHE_REFRESH_INTERVAL;

                    // Track original block count on first build
                    if (originalBlockCount == 0)
                        originalBlockCount = gridBlocks.Count;

                    // Always rebuild sprite cache on refresh (damage can change without block count changing)
                    RebuildSpriteCache(renderArea, program);
                    blockcount = gridBlocks.Count;
                }
                else
                {
                    blockCacheRefreshTick--;
                }

                // Draw cached grid sprites
                for (int i = 0; i < cachedSprites.Count; i++)
                {
                    frame.Add(cachedSprites[i]);
                }

                // Draw fuel bar (dynamic, not cached)
                DrawFuelBar(frame, renderArea, jet.tanks);

                // Draw block health summary (dynamic)
                DrawBlockSummary(frame, renderArea);
            }

            private static void RebuildSpriteCache(RectangleF renderArea, Program program)
            {
                var blocks = gridBlocks;
                if (blocks.Count == 0) return;

                cachedSprites.Clear();

                int minX = int.MaxValue, maxX = int.MinValue;
                int minZ = int.MaxValue, maxZ = int.MinValue;

                foreach (var block in blocks)
                {
                    var pos = block.Position;
                    if (pos.X < minX) minX = pos.X;
                    if (pos.X > maxX) maxX = pos.X;
                    if (pos.Z < minZ) minZ = pos.Z;
                    if (pos.Z > maxZ) maxZ = pos.Z;
                }

                int width = maxX - minX + 1;
                int height = maxZ - minZ + 1;

                bool[,] occupancyGrid = new bool[width, height];
                float[,] integrityGrid = new float[width, height];
                bool[,] functionalGrid = new bool[width, height];

                // Initialize integrity to 1.0 (healthy)
                for (int x = 0; x < width; x++)
                    for (int z = 0; z < height; z++)
                    {
                        integrityGrid[x, z] = 1.0f;
                        functionalGrid[x, z] = true;
                    }

                // Build occupancy, integrity, and functional grids
                foreach (var block in blocks)
                {
                    int x = block.Position.X - minX;
                    int z = block.Position.Z - minZ;
                    occupancyGrid[x, z] = true;

                    // Get slim block for integrity data
                    var slimBlock = block.CubeGrid.GetCubeBlock(block.Position);
                    if (slimBlock != null)
                    {
                        float maxIntegrity = slimBlock.MaxIntegrity;
                        float ratio = maxIntegrity > 0
                            ? (maxIntegrity - slimBlock.CurrentDamage) / maxIntegrity
                            : 0f;
                        // Store worst integrity at this X,Z column
                        if (ratio < integrityGrid[x, z])
                            integrityGrid[x, z] = ratio;
                    }

                    if (!block.IsFunctional)
                        functionalGrid[x, z] = false;
                }

                float padding = 10f;
                float cellSizeX = (renderArea.X - padding * 2) / width;
                float cellSizeY = (renderArea.Y - padding * 2) / height;
                float cellSize = Math.Min(cellSizeX, cellSizeY) * 15;

                Vector2 boxSize = new Vector2(width * cellSize, height * cellSize);
                // Shift slightly left and up from center, with room for fuel bar
                Vector2 renderCenter =
                    renderArea.Position
                    + new Vector2(renderArea.Size.X * 0.05f, renderArea.Size.Y * 0.02f)
                    + renderArea.Size / 2f;
                Vector2 boxTopLeft = renderCenter - (boxSize / 2f);

                Vector2I[] directions = new Vector2I[]
                {
                    new Vector2I(1, 0),
                    new Vector2I(-1, 0),
                    new Vector2I(0, 1),
                    new Vector2I(0, -1)
                };

                // Draw only outline blocks with damage-based coloring
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        if (!occupancyGrid[x, z])
                            continue;

                        bool isOutline = false;
                        foreach (var dir in directions)
                        {
                            int nx = x + dir.X;
                            int nz = z + dir.Y;

                            if (nx < 0 || nx >= width || nz < 0 || nz >= height || !occupancyGrid[nx, nz])
                            {
                                isOutline = true;
                                break;
                            }
                        }

                        if (!isOutline)
                            continue;

                        // Nose-up rotation: grid X -> screen X, grid Z -> screen Y
                        // Forward in SE is -Z, so minZ is nose -> top of screen
                        int localX = x;
                        int localZ = z;

                        // Pick color based on damage state
                        Color blockColor;
                        if (!functionalGrid[x, z])
                            blockColor = COLOR_NONFUNCTIONAL;
                        else if (integrityGrid[x, z] < DAMAGED_THRESHOLD)
                            blockColor = COLOR_CRITICAL;
                        else if (integrityGrid[x, z] < HEALTHY_THRESHOLD)
                            blockColor = COLOR_DAMAGED;
                        else
                            blockColor = COLOR_HEALTHY;

                        Vector2 drawPos = boxTopLeft + new Vector2(localX * cellSize, localZ * cellSize);

                        cachedSprites.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = drawPos + new Vector2(cellSize / 2f, cellSize / 2f),
                            Size = new Vector2(cellSize * 5f, cellSize * 2f),
                            Color = blockColor,
                            Alignment = TextAlignment.CENTER
                        });
                    }
                }
            }

            private static void DrawFuelBar(MySpriteDrawFrame frame, RectangleF renderArea, List<IMyGasTank> tanks)
            {
                if (tanks == null || tanks.Count == 0) return;

                double totalCapacity = 0;
                double totalFilled = 0;
                foreach (var tank in tanks)
                {
                    if (tank.BlockDefinition.SubtypeId.Contains("Hydrogen"))
                    {
                        totalCapacity += tank.Capacity;
                        totalFilled += tank.Capacity * tank.FilledRatio;
                    }
                }

                if (totalCapacity <= 0) return;

                double fuelPercent = totalFilled / totalCapacity;

                // Fuel bar layout - left edge, vertically centered but shorter to stay visible
                float barWidth = 14f;
                float barHeight = renderArea.Height * 0.50f;
                float barX = 20f;
                float barTopY = (renderArea.Height - barHeight) / 2f - 10f;

                // Pick color based on fuel level
                Color fuelColor;
                if (fuelPercent < BINGO_FUEL_PERCENT)
                    fuelColor = Color.Red;
                else if (fuelPercent < LOW_FUEL_PERCENT)
                    fuelColor = Color.Yellow;
                else
                    fuelColor = Color.Lime;

                // Border outline
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(barX + barWidth / 2f, barTopY + barHeight / 2f),
                    Size = new Vector2(barWidth + 2f, barHeight + 2f),
                    Color = new Color(50, 80, 50),
                    Alignment = TextAlignment.CENTER
                });

                // Background (empty portion)
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(barX + barWidth / 2f, barTopY + barHeight / 2f),
                    Size = new Vector2(barWidth, barHeight),
                    Color = new Color(20, 20, 20),
                    Alignment = TextAlignment.CENTER
                });

                // Filled portion (from bottom up)
                float fillHeight = barHeight * (float)fuelPercent;
                if (fillHeight > 1f)
                {
                    float fillTopY = barTopY + barHeight - fillHeight;
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(barX + barWidth / 2f, fillTopY + fillHeight / 2f),
                        Size = new Vector2(barWidth, fillHeight),
                        Color = fuelColor,
                        Alignment = TextAlignment.CENTER
                    });
                }

                // Percentage text above bar
                string pctText = $"{fuelPercent * 100:F0}%";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = pctText,
                    Position = new Vector2(barX + barWidth / 2f, barTopY - 18f),
                    RotationOrScale = 0.5f,
                    Color = fuelColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });

                // FUEL / BINGO label below bar
                string statusText = fuelPercent < BINGO_FUEL_PERCENT ? "BINGO" : "FUEL";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = statusText,
                    Position = new Vector2(barX + barWidth / 2f, barTopY + barHeight + 4f),
                    RotationOrScale = 0.4f,
                    Color = fuelColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });

                // Time remaining estimate
                if (fuelPercent > 0.01)
                {
                    double timeRemaining = (totalFilled / totalCapacity) * 600;
                    int minutes = (int)(timeRemaining / 60);
                    int seconds = (int)(timeRemaining % 60);
                    string timeText = $"{minutes:D2}:{seconds:D2}";

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = timeText,
                        Position = new Vector2(barX + barWidth / 2f, barTopY + barHeight + 22f),
                        RotationOrScale = 0.35f,
                        Color = new Color(150, 150, 150),
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    });
                }
            }

            private static void DrawBlockSummary(MySpriteDrawFrame frame, RectangleF renderArea)
            {
                if (gridBlocks.Count == 0 && originalBlockCount == 0) return;

                int current = gridBlocks.Count;
                int original = originalBlockCount > 0 ? originalBlockCount : current;
                string summaryText = $"BLK: {current}/{original}";

                Color summaryColor;
                if (current >= original)
                    summaryColor = new Color(150, 150, 150);
                else if (current > original * 0.7)
                    summaryColor = Color.Yellow;
                else
                    summaryColor = Color.Red;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = summaryText,
                    Position = new Vector2(renderArea.Width / 2f + renderArea.Width * 0.08f, renderArea.Height - 25f),
                    RotationOrScale = 0.45f,
                    Color = summaryColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                });
            }
        }
    }
}
