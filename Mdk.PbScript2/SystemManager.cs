using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class SystemManager
        {
            private static IMyTextSurface lcdMain;
            private static IMyTextSurface lcdExtra;
            private static IMyTextSurface lcdWeapons; // Third screen for weapon/combat info
            private static List<ProgramModule> modules = new List<ProgramModule>();
            public static int currentMenuIndex = 0;
            public static ProgramModule currentModule;
            private static string[] mainMenuOptions;
            private static Program parentProgram;
            private static UIController uiController;
            private static int lastHandledSpecialTick = -1; // Track the last tick special keys were handled
            public static int currentTick = 0; // Track the current tick
            private static Program.RaycastCameraControl raycastProgram;
            private static Program.HUDModule hudProgram;
            private static Program.ConfigurationModule configModule;
            private static Program.RadarControlModule radarControlModule; // Centralized radar + RWR management
            private static Program.AirtoAir airtoAirModule; // For continuous passive radar scanning
            private static int gpsindex = 0;
            private static List<IMySoundBlock> soundblocks = new List<IMySoundBlock>();
            private static List<IMyThrust> thrusters = new List<IMyThrust>();
            private const int GPS_INDEX_MAX = 4;
            private static String selectedsound;
            private static int lastSoundTick = -500; // Initialize to -500 to allow instant play when damaged
            private static bool isPlayingSound = false;
            private static string previousSelectedSound;
            private static int soundStartTick = 0;
            // Sound state machine variables (Space Engineers requires 1 action per tick)
            private static int soundState = 0; // 0=idle, 1=stopping, 2=selecting, 3=playing
            private static string pendingSoundName = "";
            private static bool altitudeWarningActive = false; // Track warning state with hysteresis
            private static List<IMyLargeGatlingTurret> radars = new List<IMyLargeGatlingTurret>();
            private static Jet _myJet;
            private static long lastTimeTicks = 0;
            private static double accumulatedTime = 0.0;
            private static int tickCount = 0;
            private static double lastFPS = 0.0;
            private static int blockcount = 0;
            private static List<IMyTerminalBlock> gridBlocks = new List<IMyTerminalBlock>(); // Cache the block list
            private static List<Vector2> cachedOutlineDrawPositions = null; // Cache final draw positions
            private static Vector2 cachedOutlineSpriteSize = Vector2.Zero; // Cache sprite size
            private static bool gridStructureDirty = true; // Flag to trigger recalculation
            private static List<MySprite> cachedSprites = new List<MySprite>(); // Cache the draw frame
            private static int blockCacheRefreshTick = 0; // Track when to refresh block cache
            private const int BLOCK_CACHE_REFRESH_INTERVAL = 60; // Refresh every 60 ticks (1 second at 60fps)

            // CustomData caching system - PERFORMANCE OPTIMIZATION
            private static Dictionary<string, string> customDataCache = new Dictionary<string, string>();
            private static bool customDataDirty = true;
            private static string lastCustomDataRaw = "";

            public static void Initialize(Program program)
            {

                _myJet = new Jet(program.GridTerminalSystem);
                var cockpit =
                    program.GridTerminalSystem.GetBlockWithName("JetOS") as IMyTextSurfaceProvider;
                if (cockpit != null)
                {
                    lcdMain = cockpit.GetSurface(0);
                    lcdMain.ContentType = ContentType.SCRIPT;
                    lcdMain.BackgroundColor = Color.Transparent; // Ensure transparency
                    lcdExtra = cockpit.GetSurface(1);
                    lcdExtra.ContentType = ContentType.SCRIPT;
                    lcdExtra.BackgroundColor = Color.Transparent; // Ensure transparency
                    lcdWeapons = cockpit.GetSurface(2); // Third screen for weapon/combat info
                    lcdWeapons.ContentType = ContentType.SCRIPT;
                    lcdWeapons.Script = ""; // Ensure no other script is running
                    lcdWeapons.BackgroundColor = Color.Black; // Black background for weapon screen
                    lcdWeapons.ScriptBackgroundColor = Color.Black;
                    lcdWeapons.ScriptForegroundColor = Color.White; // White text on black background
                    lcdWeapons.FontColor = new Color(25, 217, 140, 255);
                    lcdWeapons.FontSize = 0.1f; // Minimal font size
                    lcdWeapons.TextPadding = 0f; // No padding
                    lcdWeapons.Alignment = TextAlignment.CENTER;

                    for (int i = 0; i < 3; i++)
                    {
                        cockpit.GetSurface(i).FontColor = new Color(25, 217, 140, 255);
                    }
                }
                program.GridTerminalSystem.GetBlocksOfType(
                    soundblocks,
                    b => b.CustomName.Contains("Sound Block Warning")
                );

                thrusters = _myJet._thrusters;
                parentProgram = program;
                modules = new List<ProgramModule>();

                // Initialize centralized radar control FIRST
                radarControlModule = new RadarControlModule(parentProgram, _myJet);
                _myJet.radarControl = radarControlModule; // Set reference in Jet
                modules.Add(radarControlModule);

                modules.Add(new AirToGround(parentProgram, _myJet));
                airtoAirModule = new AirtoAir(parentProgram, _myJet); // Store reference for continuous scanning
                modules.Add(airtoAirModule);

                raycastProgram = new RaycastCameraControl(parentProgram, _myJet);
                hudProgram = new HUDModule(parentProgram, _myJet, lcdWeapons, radarControlModule);
                modules.Add(hudProgram);
                modules.Add(raycastProgram);
                uiController = new UIController(lcdMain, lcdExtra);

                configModule = new ConfigurationModule(parentProgram);
                modules.Add(configModule);

                modules.Add(new LogoDisplay(parentProgram, uiController));
                mainMenuOptions = new string[modules.Count];
                for (int i = 0; i < modules.Count; i++)
                {
                    mainMenuOptions[i] = modules[i].name;
                }
                currentModule = null;

                // Initialize CustomData cache
                ParseCustomData();
            }

            // CustomData Cache Helper Methods - PERFORMANCE OPTIMIZATION
            private static void ParseCustomData()
            {
                string currentData = parentProgram.Me.CustomData;

                // Only parse if CustomData has changed
                if (currentData == lastCustomDataRaw && !customDataDirty)
                    return;

                customDataCache.Clear();
                var lines = currentData.Split('\n');

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    int colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string key = line.Substring(0, colonIndex);
                        string value = line.Substring(colonIndex + 1);
                        customDataCache[key] = value;
                    }
                }

                lastCustomDataRaw = currentData;
                customDataDirty = false;
            }

            private static string GetCustomDataValue(string key)
            {
                ParseCustomData();
                return customDataCache.ContainsKey(key) ? customDataCache[key] : null;
            }

            private static void SetCustomDataValue(string key, string value)
            {
                ParseCustomData();
                customDataCache[key] = value;

                // Rebuild CustomData string
                var sb = new StringBuilder();
                foreach (var kvp in customDataCache)
                {
                    sb.Append(kvp.Key).Append(':').Append(kvp.Value).Append('\n');
                }

                parentProgram.Me.CustomData = sb.ToString();
                lastCustomDataRaw = parentProgram.Me.CustomData;
            }

            private static bool TryGetCustomDataValue(string key, out string value)
            {
                ParseCustomData();
                return customDataCache.TryGetValue(key, out value);
            }

            public static void MarkCustomDataDirty()
            {
                customDataDirty = true;
            }

            public static float GetConfigValue(string configName)
            {
                if (configModule != null)
                    return configModule.GetValue(configName);
                return 0f;
            }

            public static void Main(string argument, UpdateType updateSource)
            {
                currentTick++;
                Vector3D cockpitPosition = _myJet.GetCockpitPosition();
                MatrixD cockpitMatrix = _myJet.GetCockpitMatrix();

                // Radar enable/disable now managed by AirtoAir module

                // Variables to track damage and side
                double velocity = _myJet.GetVelocity();
                double velocityKnots = velocity * 1.94384;
                double altitude;
                selectedsound = null;
                _myJet._cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude);
                double calc = currentTick - lastSoundTick;

                // Hysteresis to prevent flickering: different thresholds for on/off
                if (altitudeWarningActive)
                {
                    // Already active - require climbing to 420m OR slowing to 340 knots to turn off
                    if (velocityKnots < 340 || altitude > 420)
                    {
                        altitudeWarningActive = false;
                        selectedsound = "";
                    }
                    else
                    {
                        selectedsound = "Tief";
                    }
                }
                else
                {
                    // Not active - require dropping to 380m AND speeding to 360 knots to turn on
                    if (velocityKnots > 360 && altitude < 380)
                    {
                        altitudeWarningActive = true;
                        selectedsound = "Tief";
                    }
                    else
                    {
                        selectedsound = "";
                    }
                }

                // Multi-tick Sound State Machine (Space Engineers requires 1 action per tick)
                // State 0: Idle - check if new sound needed
                // State 1: Stopping - call Stop() on all blocks
                // State 2: Selecting - set SelectedSound property
                // State 3: Playing - call Play() on all blocks

                // Execute current state FIRST (if active, this progresses the state machine)
                switch (soundState)
                {
                    case 1: // Stopping
                        foreach (IMySoundBlock soundBlock in soundblocks)
                        {
                            if (soundBlock == null || !soundBlock.IsFunctional)
                                continue;
                            soundBlock.Stop();
                        }
                        soundState = 2; // Next tick: select sound
                        break;

                    case 2: // Selecting
                        foreach (IMySoundBlock soundBlock in soundblocks)
                        {
                            if (soundBlock == null || !soundBlock.IsFunctional)
                                continue;

                            // Ensure block is enabled
                            if (!soundBlock.Enabled)
                                soundBlock.Enabled = true;

                            soundBlock.SelectedSound = pendingSoundName;
                        }

                        if (!string.IsNullOrEmpty(pendingSoundName))
                        {
                            soundState = 3; // Next tick: play
                        }
                        else
                        {
                            // Just stopping, go back to idle
                            soundState = 0;
                            isPlayingSound = false;
                            previousSelectedSound = "";
                        }
                        break;

                    case 3: // Playing
                        foreach (IMySoundBlock soundBlock in soundblocks)
                        {
                            if (soundBlock == null || !soundBlock.IsFunctional)
                                continue;
                            soundBlock.Play();
                        }
                        previousSelectedSound = pendingSoundName;
                        soundStartTick = currentTick;
                        isPlayingSound = true;
                        soundState = 0; // Back to idle
                        break;
                }

                // Now check if we need to start/stop/loop sounds (only when idle)
                if (soundState == 0)
                {
                    if (selectedsound != previousSelectedSound)
                    {
                        // Sound changed
                        if (!string.IsNullOrEmpty(selectedsound))
                        {
                            // Start new sound
                            pendingSoundName = selectedsound;
                            soundState = 1;
                        }
                        else if (isPlayingSound)
                        {
                            // Stop current sound
                            pendingSoundName = "";
                            soundState = 1;
                        }
                    }
                    else if (!string.IsNullOrEmpty(selectedsound) && isPlayingSound)
                    {
                        // Same sound still needed - check if we should loop it
                        // Wait 5 seconds before looping to ensure sound finishes playing
                        if (currentTick - soundStartTick >= 300) // Loop every 5 seconds
                        {
                            pendingSoundName = selectedsound;
                            soundState = 1;
                        }
                    }
                    else if (!string.IsNullOrEmpty(selectedsound) && !isPlayingSound)
                    {
                        // Sound needed but not playing - start it
                        pendingSoundName = selectedsound;
                        soundState = 1;
                    }
                }


                if (currentTick == lastHandledSpecialTick)
                    return;
                lastHandledSpecialTick = currentTick;

                if (string.IsNullOrWhiteSpace(argument))
                {
                    DisplayMenu();
                }
                else
                {
                    // Check for config import command
                    if (argument.StartsWith("ConfigImport:") && configModule != null)
                    {
                        configModule.ImportConfig(argument.Substring(13));
                    }
                    else
                    {
                        HandleInput(argument);
                    }
                }

                if (currentModule != null && currentModule.GetType() == typeof(LogoDisplay))
                {
                    LogoDisplay logoDisplay = currentModule as LogoDisplay;
                    if (logoDisplay != null && logoDisplay.IsActive)
                    {
                        logoDisplay.Tick();
                        return;
                    }
                }

                if (currentModule != null)
                {
                    currentModule.Tick();
                }

                if (raycastProgram != null)
                {
                    raycastProgram.Tick();
                    hudProgram.Tick();
                }

                // Always run centralized radar control (even in main menu)
                // This updates ALL radars and maintains the enemy list
                // Only call if NOT already the active module (to avoid double-tick)
                if (radarControlModule != null && currentModule != radarControlModule)
                {
                    radarControlModule.Tick();
                }

                // Always run passive radar scanning (even in main menu)
                // This builds the enemy contact list for situational awareness
                // Only call if NOT already the active module (to avoid double-tick)
                if (airtoAirModule != null && currentModule != airtoAirModule)
                {
                    // Call tick to run passive scanning logic
                    // The AirtoAir.Tick() method handles passive vs active mode internally
                    airtoAirModule.Tick();
                }

                // Note: RWR threat detection now runs inside RadarControlModule background tick

                HandleSpecialFunctionInputs(argument);

                // 1) How long since we last ran

                if (lastTimeTicks == 0)
                    lastTimeTicks = DateTime.UtcNow.Ticks;

                // 1) Calculate delta time
                long nowTicks = DateTime.UtcNow.Ticks;
                long diffTicks = nowTicks - lastTimeTicks;
                double deltaSeconds = diffTicks / (double)TimeSpan.TicksPerSecond;

                // 2) Accumulate time
                accumulatedTime += deltaSeconds;
                tickCount++;
                // 3) If 1 second passed, compute FPS
                if (accumulatedTime >= 1.0)
                {
                    lastFPS = tickCount / accumulatedTime;

                    // Reset counters
                    accumulatedTime = 0.0;
                    tickCount = 0;
                }

                // 4) Update the lastTime
                lastTimeTicks = nowTicks;

                // 5) Display
            }

            private static void HandleSpecialFunctionInputs(string argument)
            {
                int key;
                if (int.TryParse(argument, out key))
                {
                    if (currentModule != null)
                    {
                        currentModule.HandleSpecialFunction(key);
                    }
                }
            }

            private static void DisplayMenu()
            {
                string[] options =
                    currentModule == null ? mainMenuOptions : currentModule.GetOptions();
                uiController.RenderMainScreen(
                    title: "System Menu",
                    options: options,
                    currentMenuIndex: currentMenuIndex,
                    navigationInstructions: "1: ▲  | 2: ▼ | 3: Select | 4: Back | 5-8: Special | 9: Menu"
                );
                //uiController.RenderExtraScreen(title: "Module Hotkeys",
                //                               content: currentModule?.GetHotkeys() ??
                //                                   "Select a module to view hotkeys."); Commented due to engine recommendations
                var area = new RectangleF(0, 0, 512, 512); // Example render area
                float directionSpacing = 150f; // Vertical spacing between direction groups
                uiController.RenderCustomExtraFrame(
                    (frame, renderArea) =>
                    {
                        // Periodically refresh block cache instead of every frame
                        if (blockCacheRefreshTick <= 0 || gridBlocks.Count == 0)
                        {
                            gridBlocks.Clear();
                            parentProgram.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(gridBlocks);
                            blockCacheRefreshTick = BLOCK_CACHE_REFRESH_INTERVAL;
                        }
                        else
                        {
                            blockCacheRefreshTick--;
                        }

                        // Use cached block list
                        var blocks = gridBlocks;
                        if (blocks.Count == 0) return; // Guard against empty grid

                        if (blockcount == 0 || blockcount != blocks.Count)
                        {
                            blockcount = blocks.Count;

                            // Step 1: Get X/Z bounds
                            int minX = int.MaxValue,
                                maxX = int.MinValue;
                            int minZ = int.MaxValue,
                                maxZ = int.MinValue;
                            cachedSprites.Clear();
                            foreach (var block in blocks)
                            {
                                var pos = block.Position;
                                if (pos.X < minX)
                                    minX = pos.X;
                                if (pos.X > maxX)
                                    maxX = pos.X;
                                if (pos.Z < minZ)
                                    minZ = pos.Z;
                                if (pos.Z > maxZ)
                                    maxZ = pos.Z;
                            }

                            int width = maxX - minX + 1;
                            int height = maxZ - minZ + 1;

                            // Step 2: Build occupancy grid
                            bool[,] occupancyGrid = new bool[width, height];
                            foreach (var block in blocks)
                            {
                                int x = block.Position.X - minX;
                                int z = block.Position.Z - minZ;
                                occupancyGrid[x, z] = true;
                            }

                            // Step 3: Calculate drawing area and scaling
                            float padding = 10f;
                            float cellSizeX = (renderArea.X - padding * 2) / width;
                            float cellSizeY = (renderArea.Y - padding * 2) / height;
                            float cellSize = Math.Min(cellSizeX, cellSizeY) * 10; // Ensure square blocks

                            Vector2 boxSize = new Vector2(width * cellSize, height * cellSize);
                            Vector2 renderCenter =
                                renderArea.Position
                                + new Vector2(renderArea.Size.X * 0.01f, renderArea.Size.Y * 0.12f)
                                + renderArea.Size / 2f;
                            Vector2 boxTopLeft = renderCenter - (boxSize / 2f);

                            // Directions to check neighbors (4 cardinal directions)
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
                                Data = "Manual Fire:" + _myJet.manualfire,
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
                            if (radarControlModule != null && radarControlModule.IsRWREnabled)
                            {
                                if (radarControlModule.IsThreat)
                                {
                                    rwrStatusText += "THREAT!";
                                    rwrColor = Color.Red;
                                }
                                else
                                {
                                    rwrStatusText += "Scanning";
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
                                Position = new Vector2(220, 60),  // Below Manual Fire
                                RotationOrScale = 1f,
                                Color = rwrColor,
                                Alignment = TextAlignment.RIGHT,
                                FontId = "White"
                            };
                            cachedSprites.Add(rwrStatusSprite);
                            // Step 4: Draw only outline blocks
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

                                        if (
                                            nx < 0
                                            || nx >= width
                                            || nz < 0
                                            || nz >= height
                                            || !occupancyGrid[nx, nz]
                                        )
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

                                    Vector2 drawPos =
                                        boxTopLeft + new Vector2(localX * cellSize, localZ * cellSize);

                                    cachedSprites.Add(
                                        new MySprite()
                                        {
                                            Type = SpriteType.TEXTURE,
                                            Data = "SquareSimple",
                                            Position =
                                                drawPos + new Vector2(cellSize / 2f, cellSize / 2f),
                                            Size = new Vector2(cellSize * 5f, cellSize * 2f),
                                            Color = Color.LightGray,
                                            Alignment = TextAlignment.CENTER
                                        }
                                    );
                                }
                            }
                        }
                        for (int i = 0; i < cachedSprites.Count; i++)
                        {
                            frame.Add(cachedSprites[i]);
                        }

                        // Add fuel ring to the same frame (left side)
                        DrawFuelRingOnExtraScreen(frame, renderArea, _myJet.tanks);

                    },
                    area
                );
            }

            // Draw fuel ring on extra screen (integrated with grid visualization)
            private static void DrawFuelRingOnExtraScreen(MySpriteDrawFrame frame, RectangleF renderArea, List<IMyGasTank> tanks)
            {
                if (tanks == null || tanks.Count == 0) return;

                // Calculate total fuel percentage
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

                // Arc parameters - positioned on left side
                float screenWidth = renderArea.Width;
                float screenHeight = renderArea.Height;
                Vector2 center = new Vector2(screenWidth * 0.25f, screenHeight * 0.25f); // Top-left quarter
                float radius = Math.Min(screenWidth, screenHeight) * 0.18f;
                float arcThickness = 5f;
                float arcSpan = 180f; // Semi-circle

                // Color based on fuel level
                Color fuelColor;
                if (fuelPercent < BINGO_FUEL_PERCENT)
                    fuelColor = Color.Red;
                else if (fuelPercent < LOW_FUEL_PERCENT)
                    fuelColor = Color.Yellow;
                else
                    fuelColor = Color.Lime;

                // Draw arc segments
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

                    // Draw line segment
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

                // Draw center circle background
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(radius * 1.2f, radius * 1.2f),
                    Color = new Color(0, 0, 0, 200),
                    Alignment = TextAlignment.CENTER
                });

                // Draw fuel percentage text
                string fuelText = $"{fuelPercent*100:F0}%";
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

                // BINGO warning or FUEL label
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

                // Estimated flight time
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

            private static void HandleInput(string argument)
            {
                switch (argument)
                {
                    case "1":
                        NavigateUp();
                        break;
                    case "2":
                        NavigateDown();
                        break;
                    case "3":
                        ExecuteCurrentOption();
                        break;
                    case "4":
                        DeselectOrGoBack();
                        break;
                    case "9":
                        ReturnToMainMenu();
                        break;
                    case "5":
                        break;
                    case "6":
                        _myJet.offset += -1;
                        break;
                    case "7":
                        _myJet.offset += 1;
                        break;
                    case "8":
                        FlipGPS();
                        break;
                    default:
                        break;
                }
            }
            private static void FlipGPS()
            {
                // Cycle through occupied slots only, skipping empty and stale ones
                int startIndex = _myJet.activeSlotIndex;
                int nextIndex = (startIndex + 1) % _myJet.targetSlots.Length;

                // Search for next occupied AND fresh slot (wrap around once)
                int searchCount = 0;
                const long STALE_THRESHOLD_TICKS = 60 * TimeSpan.TicksPerSecond; // 60 seconds

                while (searchCount < _myJet.targetSlots.Length)
                {
                    if (_myJet.targetSlots[nextIndex].IsOccupied)
                    {
                        // Check if slot is still fresh (< 60 seconds old)
                        long age = DateTime.Now.Ticks - _myJet.targetSlots[nextIndex].TimestampTicks;

                        if (age < STALE_THRESHOLD_TICKS)
                        {
                            // Found fresh occupied slot - make it active
                            _myJet.activeSlotIndex = nextIndex;

                            // Update CustomData GPS for missiles
                            UpdateActiveTargetGPS();
                            return;
                        }
                        else
                        {
                            // Slot is stale, clear it and continue searching
                            _myJet.targetSlots[nextIndex].Clear();
                        }
                    }
                    nextIndex = (nextIndex + 1) % _myJet.targetSlots.Length;
                    searchCount++;
                }

                // If we get here, no fresh targets found
            }

            // Update CustomData with active target GPS for missiles to read
            public static void UpdateActiveTargetGPS()
            {
                if (!_myJet.targetSlots[_myJet.activeSlotIndex].IsOccupied)
                {
                    return; // No active target
                }

                Vector3D targetPos = _myJet.targetSlots[_myJet.activeSlotIndex].Position;
                Vector3D targetVel = _myJet.targetSlots[_myJet.activeSlotIndex].Velocity;

                // Format GPS coordinates (missiles expect this format)
                string gpsCoordinates = $"Cached:GPS:Target:{targetPos.X}:{targetPos.Y}:{targetPos.Z}:#FF75C9F1:";
                string cachedSpeed = $"CachedSpeed:{targetVel.X}:{targetVel.Y}:{targetVel.Z}:#FF75C9F1:";

                // Update CustomData more efficiently - single pass, single allocation
                var customDataLines = parentProgram.Me.CustomData.Split('\n');
                var result = new List<string>(customDataLines.Length + 2);
                bool cachedLineFound = false;
                bool cachedSpeedFound = false;

                for (int i = 0; i < customDataLines.Length; i++)
                {
                    string line = customDataLines[i];
                    if (line.StartsWith("Cached:GPS:") || line.StartsWith("Cached:"))
                    {
                        if (!cachedLineFound)
                        {
                            result.Add(gpsCoordinates);
                            cachedLineFound = true;
                        }
                        // Skip duplicate cached lines
                    }
                    else if (line.StartsWith("CachedSpeed:"))
                    {
                        if (!cachedSpeedFound)
                        {
                            result.Add(cachedSpeed);
                            cachedSpeedFound = true;
                        }
                        // Skip duplicate speed lines
                    }
                    else
                    {
                        result.Add(line);
                    }
                }

                // Add missing entries at the end
                if (!cachedLineFound) result.Add(gpsCoordinates);
                if (!cachedSpeedFound) result.Add(cachedSpeed);

                parentProgram.Me.CustomData = string.Join("\n", result);
                MarkCustomDataDirty();
            }

            // Note: FindEmptyOrOldestSlot is now a static method in Program class
            private static void NavigateUp()
            {
                // Check if current module wants to handle navigation
                if (currentModule != null && currentModule.HandleNavigation(true))
                {
                    return; // Module handled it
                }

                if (currentMenuIndex > 0)
                {
                    currentMenuIndex--;
                }
            }
            private static void NavigateDown()
            {
                // Check if current module wants to handle navigation
                if (currentModule != null && currentModule.HandleNavigation(false))
                {
                    return; // Module handled it
                }

                int totalOptions = (
                    currentModule == null
                        ? mainMenuOptions.Length
                        : currentModule.GetOptions().Length
                );
                if (currentMenuIndex < totalOptions - 1)
                {
                    currentMenuIndex++;
                }
            }
            private static void ExecuteCurrentOption()
            {
                if (currentModule == null)
                {
                    currentModule = modules[currentMenuIndex];
                    currentMenuIndex = 0;
                }
                else
                {
                    currentModule.ExecuteOption(currentMenuIndex);
                }
            }
            private static void DeselectOrGoBack()
            {
                if (currentModule != null)
                {
                    // Check if module wants to handle back button internally
                    if (currentModule.HandleBack())
                    {
                        return; // Module handled it
                    }

                    // Default: exit the module
                    currentModule = null;
                    currentMenuIndex = 0;
                }
            }
            public static void ReturnToMainMenu()
            {
                currentModule = null;
                currentMenuIndex = 0;
            }
            public static IMyTextSurface GetMainLCD()
            {
                return lcdMain;
            }
            public static IMyTextSurface GetExtraLCD()
            {
                return lcdExtra;
            }
        }
    }
}
