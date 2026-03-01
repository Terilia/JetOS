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
            private static List<IMyTerminalBlock> gridBlocks = new List<IMyTerminalBlock>();
            private static List<MySprite> cachedSprites = new List<MySprite>();
            private static int blockCacheRefreshTick = 0;
            private const int BLOCK_CACHE_REFRESH_INTERVAL = 60;

            public static void Render(MySpriteDrawFrame frame, RectangleF renderArea,
                Program program, Jet jet, RadarControlModule radarModule)
            {
                // Periodically refresh block cache instead of every frame
                if (blockCacheRefreshTick <= 0 || gridBlocks.Count == 0)
                {
                    gridBlocks.Clear();
                    program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(gridBlocks);
                    blockCacheRefreshTick = BLOCK_CACHE_REFRESH_INTERVAL;
                }
                else
                {
                    blockCacheRefreshTick--;
                }

                var blocks = gridBlocks;
                if (blocks.Count == 0) return;

                if (blockcount == 0 || blockcount != blocks.Count)
                {
                    blockcount = blocks.Count;

                    int minX = int.MaxValue, maxX = int.MinValue;
                    int minZ = int.MaxValue, maxZ = int.MinValue;
                    cachedSprites.Clear();
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
                    foreach (var block in blocks)
                    {
                        int x = block.Position.X - minX;
                        int z = block.Position.Z - minZ;
                        occupancyGrid[x, z] = true;
                    }

                    float padding = 10f;
                    float cellSizeX = (renderArea.X - padding * 2) / width;
                    float cellSizeY = (renderArea.Y - padding * 2) / height;
                    float cellSize = Math.Min(cellSizeX, cellSizeY) * 10;

                    Vector2 boxSize = new Vector2(width * cellSize, height * cellSize);
                    Vector2 renderCenter =
                        renderArea.Position
                        + new Vector2(renderArea.Size.X * 0.01f, renderArea.Size.Y * 0.12f)
                        + renderArea.Size / 2f;
                    Vector2 boxTopLeft = renderCenter - (boxSize / 2f);

                    Vector2I[] directions = new Vector2I[]
                    {
                        new Vector2I(1, 0),
                        new Vector2I(-1, 0),
                        new Vector2I(0, 1),
                        new Vector2I(0, -1)
                    };

                    MySprite targettext = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "Man Fire:" + jet.manualfire,
                        Position = new Vector2(220, 40),
                        RotationOrScale = 1f,
                        Color = Color.White,
                        Alignment = TextAlignment.RIGHT,
                        FontId = "White"
                    };
                    cachedSprites.Add(targettext);

                    // RWR status display
                    string rwrStatusText = "RWR: ";
                    Color rwrColor = Color.Gray;
                    if (radarModule != null && radarModule.IsRWREnabled)
                    {
                        if (radarModule.IsThreat)
                        {
                            rwrStatusText += "THREAT!";
                            rwrColor = Color.Red;
                        }
                        else
                        {
                            rwrStatusText += "Scan";
                            rwrColor = Color.Green;
                        }
                    }
                    else
                    {
                        rwrStatusText += "OFF";
                        rwrColor = Color.Gray;
                    }

                    MySprite rwrStatusSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = rwrStatusText,
                        Position = new Vector2(220, 60),
                        RotationOrScale = 1f,
                        Color = rwrColor,
                        Alignment = TextAlignment.RIGHT,
                        FontId = "White"
                    };
                    cachedSprites.Add(rwrStatusSprite);

                    // Draw only outline blocks
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

                            // Rotate 90 degrees clockwise
                            int localX = z;
                            int localZ = width - x - 1;

                            Vector2 drawPos = boxTopLeft + new Vector2(localX * cellSize, localZ * cellSize);

                            cachedSprites.Add(new MySprite()
                            {
                                Type = SpriteType.TEXTURE,
                                Data = "SquareSimple",
                                Position = drawPos + new Vector2(cellSize / 2f, cellSize / 2f),
                                Size = new Vector2(cellSize * 5f, cellSize * 2f),
                                Color = Color.LightGray,
                                Alignment = TextAlignment.CENTER
                            });
                        }
                    }
                }

                for (int i = 0; i < cachedSprites.Count; i++)
                {
                    frame.Add(cachedSprites[i]);
                }

                // Add fuel ring to the same frame
                DrawFuelRing(frame, renderArea, jet.tanks);
            }

            private static void DrawFuelRing(MySpriteDrawFrame frame, RectangleF renderArea, List<IMyGasTank> tanks)
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
                const double BINGO_FUEL_PERCENT = 0.20;
                const double LOW_FUEL_PERCENT = 0.35;

                float screenWidth = renderArea.Width;
                float screenHeight = renderArea.Height;
                Vector2 center = new Vector2(screenWidth * 0.25f, screenHeight * 0.25f);
                float radius = Math.Min(screenWidth, screenHeight) * 0.18f;
                float arcThickness = 5f;
                float arcSpan = 180f;

                Color fuelColor;
                if (fuelPercent < BINGO_FUEL_PERCENT)
                    fuelColor = Color.Red;
                else if (fuelPercent < LOW_FUEL_PERCENT)
                    fuelColor = Color.Yellow;
                else
                    fuelColor = Color.Lime;

                int segments = 30;
                float startAngle = 90f - arcSpan / 2f;
                float filledAngle = startAngle + (float)(fuelPercent * arcSpan);

                for (int i = 0; i < segments; i++)
                {
                    float angle1 = startAngle + (arcSpan / segments) * i;
                    float angle2 = startAngle + (arcSpan / segments) * (i + 1);

                    float rad1 = MathHelper.ToRadians(angle1);
                    float rad2 = MathHelper.ToRadians(angle2);

                    Vector2 p1 = center + new Vector2((float)Math.Cos(rad1) * radius, (float)Math.Sin(rad1) * radius);
                    Vector2 p2 = center + new Vector2((float)Math.Cos(rad2) * radius, (float)Math.Sin(rad2) * radius);

                    Color segmentColor = angle2 <= filledAngle ? fuelColor : new Color(fuelColor, 0.2f);

                    Vector2 direction = p2 - p1;
                    float length = direction.Length();
                    if (length > 0)
                    {
                        direction.Normalize();
                        float rotation = (float)Math.Atan2(direction.Y, direction.X);

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = (p1 + p2) / 2f,
                            Size = new Vector2(length, arcThickness),
                            RotationOrScale = rotation,
                            Color = segmentColor,
                            Alignment = TextAlignment.CENTER
                        });
                    }
                }

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(radius * 1.2f, radius * 1.2f),
                    Color = new Color(0, 0, 0, 200),
                    Alignment = TextAlignment.CENTER
                });

                string fuelText = $"{fuelPercent * 100:F0}%";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = fuelText,
                    Position = new Vector2(center.X, center.Y - 8f),
                    RotationOrScale = 0.7f,
                    Color = fuelColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });

                string statusText = fuelPercent < BINGO_FUEL_PERCENT ? "BINGO" : "FUEL";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = statusText,
                    Position = new Vector2(center.X, center.Y + 12f),
                    RotationOrScale = 0.45f,
                    Color = fuelColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });

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
                        Position = new Vector2(center.X, center.Y + radius + 15f),
                        RotationOrScale = 0.4f,
                        Color = new Color(150, 150, 150),
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    });
                }
            }
        }
    }
}
