using BulletXNA;
using Sandbox.Game.Entities;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.EntityComponents.Blocks;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.VisualScripting.Utils;
using VRageMath;
namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.

        public Program()
        {
            SystemManager.Initialize(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }
        public void Main(string argument, UpdateType updateSource)
        {
            SystemManager.Main(argument, updateSource);
        }
        public class Jet
        {
            // Core blocks
            public IMyCockpit _cockpit;
            public List<IMyThrust> _thrusters;
            public List<IMyThrust> _thrustersbackwards;
            public List<IMySoundBlock> _soundBlocks;
            public List<IMyLargeGatlingTurret> _radars;
            public IMyLargeConveyorTurretBase _radar;
            public List<IMyShipMergeBlock> _bays;
            public List<IMyTerminalBlock> leftstab = new List<IMyTerminalBlock>();
            public List<IMyTerminalBlock> rightstab = new List<IMyTerminalBlock>();
            public IMyTerminalBlock hudBlock;
            public IMyTextSurface hud;
            public List<IMyGasTank> tanks = new List<IMyGasTank>();
            public int offset = 0;
            public bool manualfire = false; // Set to true if you want to fire the guns manually, false if you want to use the radar system
            public List<IMySmallGatlingGun> _gatlings = new List<IMySmallGatlingGun>();
            // Constructor: gather all relevant blocks
            public Jet(IMyGridTerminalSystem grid)
            {
                // Find the cockpit
                _cockpit = grid.GetBlockWithName("Jet Pilot Seat") as IMyCockpit;
                grid.GetBlocksOfType(
                                    _gatlings,
                                    t => t.CubeGrid == _cockpit.CubeGrid 
                                );                
                _thrusters = new List<IMyThrust>();
                grid.GetBlocksOfType(
                    _thrusters,
                    t => t.CubeGrid == _cockpit.CubeGrid && !t.CustomName.Contains("Sci-Fi")
                );

                // Sound blocks with "Sound Block Warning" in name
                _soundBlocks = new List<IMySoundBlock>();
                grid.GetBlocksOfType(
                    _soundBlocks,
                    s => s.CustomName.Contains("Sound Block Warning")
                );

                // Radar turrets
                _radars = new List<IMyLargeGatlingTurret>();
                grid.GetBlocksOfType(_radars, r => r.CustomName.Contains("Radar"));

                _radar = grid.GetBlockWithName("JetNoseRad") as IMyLargeGatlingTurret;

                // bays
                _bays = new List<IMyShipMergeBlock>();
                grid.GetBlocksOfType(_bays, b => b.CustomName.Contains("Bay"));
                _bays.Sort(
                    (a, b) =>
                        ExtractBayNumber(a.CustomName).CompareTo(ExtractBayNumber(b.CustomName))
                );

                grid.GetBlocksOfType(rightstab, g => g.CustomName.Contains("invertedstab")); //invertedstab
                grid.GetBlocksOfType(leftstab, g => g.CustomName.Contains("normalstab")); //normalstab
                _thrustersbackwards = new List<IMyThrust>();
                grid.GetBlocksOfType(
                    _thrustersbackwards,
                    g =>
                        g.CubeGrid == _cockpit.CubeGrid
                        && !g.CustomName.Contains("Sci-Fi")
                        && g.GridThrustDirection == Vector3I.Backward
                );
                hudBlock = grid.GetBlockWithName("Fighter HUD");
                hud = hudBlock as IMyTextSurface;
                grid.GetBlocksOfType(
                    tanks,
                    g => g.CubeGrid == _cockpit.CubeGrid && g.CustomName.Contains("Jet")
                );
                hud.ContentType = ContentType.SCRIPT;
                hud.ScriptBackgroundColor = new Color(0, 0, 0, 0);
                hud.ScriptForegroundColor = Color.White;
            }
            private int ExtractBayNumber(string name)
            {
                var parts = name.Split(' ');
                int number;
                if (parts.Length > 1 && int.TryParse(parts[1], out number))
                {
                    return number;
                }
                return -1;
            }
            // ------------------------------
            // COCKPIT & SHIP INFO
            // ------------------------------

            /// <summary>
            /// True if the cockpit is found and functional.
            /// </summary>
            public bool IsCockpitFunctional => _cockpit != null && _cockpit.IsFunctional;

            /// <summary>
            /// Gets the current velocity in m/s of the ship.
            /// </summary>
            public double GetVelocity()
            {
                if (_cockpit == null)
                    return 0.0;
                return _cockpit.GetShipSpeed(); // m/s
            }

            /// <summary>
            /// Gets the velocity in knots (for your HUD).
            /// </summary>
            public double GetVelocityKnots()
            {
                return GetVelocity() * 1.94384;
            }

            /// <summary>
            /// Attempts to get altitude from cockpit (Surface-level).
            /// </summary>
            public double GetAltitude()
            {
                if (_cockpit == null)
                    return 0.0;
                double altitude = 0.0;
                _cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude);
                return altitude;
            }

            /// <summary>
            /// Cockpit WorldMatrix and Position if needed for calculations.
            /// </summary>
            public Vector3D GetCockpitPosition() => _cockpit?.GetPosition() ?? Vector3D.Zero;
            public MatrixD GetCockpitMatrix() => _cockpit?.WorldMatrix ?? MatrixD.Identity;

            // ------------------------------
            // THRUSTERS
            // ------------------------------

            /// <summary>
            /// Provides read-only access to the thrusters for advanced usage.
            /// </summary>
            public IReadOnlyList<IMyThrust> Thrusters => _thrusters;

            /// <summary>
            /// Example: sets thrust override on all thrusters (0.0 -> 1.0).
            /// </summary>
            public void SetThrustOverride(float percentage)
            {
                percentage = MathHelper.Clamp(percentage, 0f, 1f);
                foreach (var thruster in _thrusters)
                {
                    if (Math.Abs(thruster.ThrustOverridePercentage - percentage) > 0.001f)
                    {
                        thruster.ThrustOverridePercentage = percentage;
                    }
                }
            }



            // ------------------------------
            // RADARS / TURRETS
            // ------------------------------

            /// <summary>
            /// Enables or disables all radar turrets if they exist.
            /// </summary>
            public void SetRadarsEnabled(bool enabled)
            {
                foreach (var radar in _radars)
                {
                    radar.Enabled = enabled;
                }
            }

            /// <summary>
            /// Example: toggles radars at certain ticks or conditions.
            /// (You can call from your main update or a custom method.)
            /// </summary>
            public void ToggleRadars(int currentTick)
            {
                // e.g., turn on until tick 10, off between 600 and 700, etc.
                if (currentTick < 10)
                    SetRadarsEnabled(true);
                else if (currentTick > 600 && currentTick < 700)
                    SetRadarsEnabled(false);
            }
        }

        static class SystemManager
        {
            private static IMyTextSurface lcdMain;
            private static IMyTextSurface lcdExtra;
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
            private static int gpsindex = 0;
            private static List<IMySoundBlock> soundblocks = new List<IMySoundBlock>();
            private static List<IMyThrust> thrusters = new List<IMyThrust>();
            private const int GPS_INDEX_MAX = 4;
            private static String selectedsound;
            private static String module_sound;
            private static int lastSoundTick = -500; // Initialize to -500 to allow instant play when damaged
            private static bool isPlayingSound = false;
            private static string previousSelectedSound;
            private static int soundStartTick = 0;
            private static int soundSetupStep = 0; // 0: Idle, 1: Stop, 2: Set, 3: Play, 4: Wait
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
            private static void RecalculateShipOutline(RectangleF renderArea)
            {
                // Only fetch blocks if the list is empty or flagged as dirty
                // (You might add more sophisticated checks later, e.g., grid block count changes)
                if (gridBlocks.Count == 0 || gridStructureDirty)
                {
                    gridBlocks.Clear();
                    // Use the parentProgram reference to access GridTerminalSystem
                    parentProgram.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(gridBlocks, b => b.CubeGrid == parentProgram.Me.CubeGrid); // Get blocks from the specific grid
                    gridStructureDirty = false; // Reset flag after fetching
                }

                if (gridBlocks.Count == 0)
                {
                    cachedOutlineDrawPositions = new List<Vector2>(); // Ensure list exists but is empty
                    return; // No blocks to draw
                }

                // Step 1: Get X/Z bounds (Only if recalculating)
                int minX = int.MaxValue, maxX = int.MinValue;
                int minZ = int.MaxValue, maxZ = int.MinValue;

                foreach (var block in gridBlocks)
                {
                    var pos = block.Position;
                    if (pos.X < minX) minX = pos.X;
                    if (pos.X > maxX) maxX = pos.X;
                    if (pos.Z < minZ) minZ = pos.Z;
                    if (pos.Z > maxZ) maxZ = pos.Z;
                }

                int width = maxX - minX + 1;
                int height = maxZ - minZ + 1;

                // Avoid division by zero if grid is 1D
                if (width <= 0 || height <= 0)
                {
                    cachedOutlineDrawPositions = new List<Vector2>();
                    return;
                }

                // Step 2: Build occupancy grid (Only if recalculating)
                bool[,] occupancyGrid = new bool[width, height];
                foreach (var block in gridBlocks)
                {
                    int x = block.Position.X - minX;
                    int z = block.Position.Z - minZ;
                    // Check bounds before accessing array
                    if (x >= 0 && x < width && z >= 0 && z < height)
                    {
                        occupancyGrid[x, z] = true;
                    }
                }

                // Step 3: Calculate drawing area and scaling (Only if recalculating)
                float padding = 10f;
                // Use renderArea passed in, maybe adjust scaling factors
                float availableWidth = renderArea.Size.X * 0.4f; // Example: Use 40% of width
                float availableHeight = renderArea.Size.Y * 0.4f; // Example: Use 40% of height
                float cellSizeX = availableWidth / width;
                float cellSizeY = availableHeight / height;
                float cachedCellSize = Math.Min(cellSizeX, cellSizeY); // Use the smaller scale factor

                Vector2 boxSize = new Vector2(width * cachedCellSize, height * cachedCellSize);
                // Adjust center position based on where you want the radar (e.g., bottom right)
                Vector2 renderCenter = renderArea.Position + new Vector2(renderArea.Size.X * 0.8f, renderArea.Size.Y * 0.8f); // Example: Bottom right
                Vector2 boxTopLeft = renderCenter - (boxSize / 2f);

                cachedOutlineSpriteSize = new Vector2(cachedCellSize, cachedCellSize); // Store sprite size

                // Directions to check neighbors
                Vector2I[] directions = new Vector2I[]
                {
                    new Vector2I(1, 0), new Vector2I(-1, 0),
                    new Vector2I(0, 1), new Vector2I(0, -1)
                };

                // Step 4: Calculate and cache outline block positions (Only if recalculating)
                cachedOutlineDrawPositions = new List<Vector2>();
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        if (!occupancyGrid[x, z]) continue;

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

                        if (!isOutline) continue;

                        // Rotate 90 degrees clockwise for drawing
                        int rotatedX = z;
                        int rotatedZ = width - 1 - x;

                        Vector2 drawPos = boxTopLeft + new Vector2(rotatedX * cachedCellSize, rotatedZ * cachedCellSize);
                        cachedOutlineDrawPositions.Add(drawPos + cachedOutlineSpriteSize / 2f); // Cache the final sprite center position
                    }
                }
            }
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

                    for (int i = 0; i < 3; i++)
                    {
                        cockpit.GetSurface(i).FontColor = new Color(25, 217, 140, 255);
                    }
                }
                else { }
                program.GridTerminalSystem.GetBlocksOfType(
                    soundblocks,
                    b => b.CustomName.Contains("Sound Block Warning")
                );

                thrusters = _myJet._thrusters;
                parentProgram = program;

                modules.Add(new AirToGround(parentProgram, _myJet));
                modules.Add(new AirtoAir(parentProgram, _myJet));

                raycastProgram = new RaycastCameraControl(parentProgram, _myJet);
                hudProgram = new HUDModule(parentProgram, _myJet);
                modules.Add(hudProgram);
                modules.Add(raycastProgram);
                uiController = new UIController(lcdMain, lcdExtra);
                modules.Add(new LogoDisplay(parentProgram, uiController));

                modules.Add(new FroggerGameControl(parentProgram, uiController));
                mainMenuOptions = new string[modules.Count];
                for (int i = 0; i < modules.Count; i++)
                {
                    mainMenuOptions[i] = modules[i].name;
                }
                currentModule = null;
            }

            // Add these variables at the class level if not already present

            // Add these variables at the class level if not already present


            public static void Main(string argument, UpdateType updateSource)
            {
                currentTick++;
                Vector3D cockpitPosition = _myJet.GetCockpitPosition();
                MatrixD cockpitMatrix = _myJet.GetCockpitMatrix();
                if (currentTick < 10) //BUG: This is a workaround for the radar to load the first ammunition, as otherwise the radar won't do anything?
                {
                    for (int i = 0; i < radars.Count; i++)
                    {
                        radars[i].Enabled = true;
                    }
                }
                if (currentTick > 600 && currentTick < 700)
                {
                    for (int i = 0; i < radars.Count; i++)
                    {
                        radars[i].Enabled = false;
                    }
                }
                // Variables to track damage and side
                double velocity = _myJet.GetVelocity();
                double velocityKnots = velocity * 1.94384;
                double altitude;
                selectedsound = null;
                _myJet._cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude);
                double calc = currentTick - lastSoundTick;
                if (velocityKnots > 350 && altitude < 400)
                {
                    selectedsound = "Tief";
                }
                else
                {
                    selectedsound = "";
                }

                // Check if the selected sound has changed
                if (selectedsound != previousSelectedSound)
                {
                    if (!string.IsNullOrEmpty(selectedsound))
                    {
                        // Start the sound change sequence
                        soundSetupStep = 1;
                        previousSelectedSound = selectedsound;
                    }
                    else
                    {
                        // If no sound is selected and a sound is playing, stop it
                        if (isPlayingSound)
                        {
                            foreach (IMySoundBlock soundBlock in soundblocks)
                            {
                                soundBlock.Stop();
                                soundBlock.SelectedSound = "";
                            }
                            soundSetupStep = 0;
                            isPlayingSound = false;
                            previousSelectedSound = "";
                        }
                    }
                }

                // Process the sound setup steps
                switch (soundSetupStep)
                {
                    case 1:
                        // Stop the sound
                        foreach (IMySoundBlock soundBlock in soundblocks)
                        {
                            soundBlock.Stop();
                        }
                        soundSetupStep = 2;
                        break;

                    case 2:
                        // Set the new sound
                        foreach (IMySoundBlock soundBlock in soundblocks)
                        {
                            soundBlock.SelectedSound = selectedsound;
                        }
                        soundSetupStep = 3;
                        break;

                    case 3:
                        soundSetupStep = 4;
                        break;
                    case 4:
                        soundSetupStep = 5;
                        break;
                    case 5:
                        soundSetupStep = 6;
                        break;
                    case 6:
                        // Play the sound
                        foreach (IMySoundBlock soundBlock in soundblocks)
                        {
                            soundBlock.Play();
                        }
                        soundSetupStep = 7;
                        soundStartTick = currentTick;
                        isPlayingSound = true;
                        break;

                    case 7:
                        // Wait for the sound to finish (700 ticks)
                        if (currentTick - soundStartTick >= 700)
                        {
                            foreach (IMySoundBlock soundBlock in soundblocks)
                            {
                                soundBlock.Stop();
                            }
                            soundSetupStep = 0;
                            isPlayingSound = false;
                            previousSelectedSound = "";
                        }
                        break;

                    default:
                        // Idle state, do nothing
                        break;
                }

                module_sound = null;

                if (currentTick == lastHandledSpecialTick)
                    return;
                lastHandledSpecialTick = currentTick;

                if (string.IsNullOrWhiteSpace(argument))
                {
                    DisplayMenu();
                }
                else
                {
                    HandleInput(argument);
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
                parentProgram.Echo($"Approx FPS: {lastFPS:F2}");
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
                        float gaugeRadius = 50f; // Radius of the gauge circle
                        float gaugeSpacing = 120f; // Spacing between gauges
                        float startX = 50f; // Starting X position for the gauges
                        float startY = 50f; // Starting Y position for the gauges
                        // Group thrusters by GridThrustDirection
                        var groupedByDirection = thrusters
                            .GroupBy(thruster => thruster.GridThrustDirection)
                            .OrderBy(group => group.Key.ToString()); // Optional: Sort by direction name
                        parentProgram.Echo(thrusters.ToArray().ToString());
                        int directionIndex = 0;

                        foreach (var directionGroup in groupedByDirection)
                        {
                            // Determine direction label
                            string direction = directionGroup.Key.Z < 0 ? "Backward" : "Forward";

                            // Position for the direction label
                            var directionLabelPosition = new Vector2(
                                startX,
                                startY + directionIndex * directionSpacing
                            );

                            // Draw a semi-transparent background rectangle behind the direction label
                            var directionLabelBackground = new MySprite(
                                SpriteType.TEXTURE,
                                "SquareSimple",
                                directionLabelPosition + new Vector2(0f, 0f), // top-left corner (we’ll adjust size below)
                                new Vector2(400f, 40f), // width & height of the background
                                new Color(0, 0, 0, 180), // semi-transparent black
                                alignment: TextAlignment.LEFT
                            );
                            frame.Add(directionLabelBackground);

                            // Draw direction label text
                            var directionLabel = new MySprite(
                                SpriteType.TEXT,
                                $"Direction: {direction}",
                                directionLabelPosition + new Vector2(10f, 5f), // small offsets inside the rectangle
                                null,
                                Color.White,
                                "Debug",
                                TextAlignment.LEFT
                            );
                            frame.Add(directionLabel);

                            // Group by MaxEffectiveThrust, highest first
                            var groupedByMaxThrust = directionGroup
                                .GroupBy(thruster => thruster.MaxEffectiveThrust)
                                .OrderByDescending(group => group.Key);

                            int thrustGroupIndex = 0;
                            foreach (var thrustGroup in groupedByMaxThrust)
                            {
                                // Calculate average thrust percentage for this group
                                float totalThrust = thrustGroup.Sum(
                                    thruster => thruster.CurrentThrust
                                );
                                float totalMaxThrust = thrustGroup.Sum(
                                    thruster => thruster.MaxEffectiveThrust
                                );
                                float averagePercentage =
                                    (totalMaxThrust > 0)
                                        ? (totalThrust / totalMaxThrust) * 100f
                                        : 0f;

                                // Position for the gauge center
                                var gaugeCenter = new Vector2(
                                    startX + 60f + (thrustGroupIndex * gaugeSpacing),
                                    directionLabelPosition.Y + 80f
                                );

                                // Optionally show which thrust group we’re looking at (e.g., by max thrust)
                                var groupLabel = new MySprite(
                                    SpriteType.TEXT,
                                    $"Group: {thrustGroup.Key / 1000f:0.0}kN", // Just an example
                                    gaugeCenter + new Vector2(0, -50f),
                                    null,
                                    Color.LightGray,
                                    "Debug",
                                    TextAlignment.CENTER
                                );
                                frame.Add(groupLabel);

                                // Draw a gray circle as the gauge background
                                var circle = new MySprite(
                                    SpriteType.TEXTURE,
                                    "Circle",
                                    gaugeCenter,
                                    new Vector2(gaugeRadius * 2, gaugeRadius * 2),
                                    Color.Gray
                                );
                                frame.Add(circle);

                                // Dynamically compute a color from green-ish to red-ish based on usage
                                // (This is just one simple approach—tweak as you wish.)
                                float t = MathHelper.Clamp(averagePercentage / 100f, 0f, 1f);
                                Color gaugeColor = new Color(
                                    (int)MathHelper.Lerp(0, 255, t),
                                    (int)MathHelper.Lerp(255, 0, t),
                                    0
                                );

                                // Draw the needle
                                float angle = MathHelper.ToRadians(
                                    (averagePercentage / 100f) * 180f - 90f
                                ); // Map [0..100%] -> [-90..+90 deg]
                                var needle = new MySprite(
                                    SpriteType.TEXTURE,
                                    "SquareSimple",
                                    gaugeCenter,
                                    new Vector2(2, gaugeRadius),
                                    gaugeColor,
                                    alignment: TextAlignment.CENTER
                                );
                                needle.Position =
                                    gaugeCenter
                                    + new Vector2(
                                        (float)Math.Cos(angle - MathHelper.ToRadians(90)),
                                        (float)Math.Sin(angle - MathHelper.ToRadians(90))
                                    ) * (gaugeRadius / 2);
                                needle.RotationOrScale = angle;
                                frame.Add(needle);

                                // Draw the percentage label under the gauge
                                var percentageLabel = new MySprite(
                                    SpriteType.TEXT,
                                    $"{averagePercentage:F1}%",
                                    gaugeCenter + new Vector2(0, gaugeRadius + 20f),
                                    null,
                                    Color.White,
                                    "Debug",
                                    TextAlignment.CENTER
                                );
                                frame.Add(percentageLabel);

                                thrustGroupIndex++;
                            }

                            directionIndex++;
                        }
                        List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();   
                        parentProgram.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks);
                        parentProgram.Echo(blockcount.ToString());
                        if (blockcount == 0 || blockcount != blocks.Count)
                        {
                            blockcount = blocks.Count;
                            parentProgram.Echo("Now");
                            
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
                                + new Vector2(renderArea.Size.X * 0.225f, renderArea.Size.Y * 0.12f)
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

                    },
                    area
                );
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
                        parentProgram.Echo("Invalid input. Use keys 1-9.");
                        break;
                }
            }
            private static void FlipGPS()
            {
                var customDataLines = parentProgram.Me.CustomData.Split(
                    new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                List<string> modifiedLines = new List<string>(customDataLines);
                string cachedLine = modifiedLines.FirstOrDefault(
                    line => line.StartsWith("Cached:")
                );
                if (cachedLine == null)
                {
                    return;
                }
                string cachedGPSData = cachedLine.Substring(7);
                string currentCacheGPSLine = $"CacheGPS{gpsindex}:";
                int currentCacheLineIndex = modifiedLines.FindIndex(
                    line => line.StartsWith(currentCacheGPSLine)
                );
                if (currentCacheLineIndex >= 0)
                {
                    modifiedLines[currentCacheLineIndex] = currentCacheGPSLine + cachedGPSData;
                }
                else
                {
                    modifiedLines.Add(currentCacheGPSLine + cachedGPSData);
                }
                gpsindex = (gpsindex + 1) % GPS_INDEX_MAX;
                string nextCacheGPSLine = $"CacheGPS{gpsindex}:";
                int nextCacheLineIndex = modifiedLines.FindIndex(
                    line => line.StartsWith(nextCacheGPSLine)
                );
                string nextGPSData = "";
                if (nextCacheLineIndex >= 0)
                {
                    nextGPSData = modifiedLines[nextCacheLineIndex].Substring(
                        nextCacheGPSLine.Length
                    );
                }
                int cachedLineIndex = modifiedLines.FindIndex(line => line.StartsWith("Cached:"));
                if (cachedLineIndex >= 0)
                {
                    modifiedLines[cachedLineIndex] = $"Cached:{nextGPSData}";
                }
                else { }
                parentProgram.Me.CustomData = string.Join("\n", modifiedLines);
            }
            private static void NavigateUp()
            {
                if (currentMenuIndex > 0)
                {
                    currentMenuIndex--;
                }
            }
            private static void NavigateDown()
            {
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
        class UIController
        {
            private static readonly Color BORDER_COLOR = new Color(30, 50, 30);
            private static readonly Color BACKGROUND_COLOR = new Color(10, 15, 10);
            private static readonly Color TITLE_COLOR = new Color(100, 150, 100);
            private static readonly Color TEXT_COLOR = new Color(200, 200, 180);
            private static readonly Color HIGHLIGHT_COLOR = new Color(150, 100, 50);
            private static readonly Color NAVIGATION_COLOR = new Color(80, 120, 80);
            private static readonly Color BLACK_BACKGROUND = new Color(0, 0, 0);
            private static readonly Color TITLE_BACKGROUND = new Color(20, 30, 20);
            private const int MAIN_VIEWPORT_HEIGHT = 0;
            private const int CONTENT_PADDING_TOP = 30;
            private const int OPTION_HEIGHT = 23;
            private const int NAVIGATION_INSTRUCTIONS_HEIGHT = 40;
            private const int EXTRA_VIEWPORT_HEIGHT = 40;
            private const int BORDER_THICKNESS = 2;
            private const int PADDING_TOP = 10;
            private const int PADDING_BOTTOM = 10;
            private const float TITLE_SCALE = 0.6f;
            private const float OPTION_SCALE = 0.6f;
            private const float NAVIGATION_SCALE = 0.6f;
            private IMyTextSurface mainScreen;
            private IMyTextSurface extraScreen;
            private RectangleF mainViewport;
            private RectangleF extraViewport;
            private List<UIElement> mainElements = new List<UIElement>();
            private List<UIElement> extraElements = new List<UIElement>();
            public IMyTextSurface MainScreen => mainScreen;
            public IMyTextSurface ExtraScreen => extraScreen;
            public UIController(IMyTextSurface mainScreen, IMyTextSurface extraScreen)
            {
                this.mainScreen = mainScreen;
                this.extraScreen = extraScreen;
                PrepareTextSurfaceForSprites(mainScreen);
                PrepareTextSurfaceForSprites(extraScreen);
                mainViewport = new RectangleF(Vector2.Zero, mainScreen.SurfaceSize);
                extraViewport = new RectangleF(Vector2.Zero, extraScreen.SurfaceSize);

                InitializeUI();
            }
            public void RenderCustomFrame(
                Action<MySpriteDrawFrame, RectangleF> customRender,
                RectangleF area
            )
            {
                var frame = mainScreen.DrawFrame();
                customRender?.Invoke(frame, area);
                frame.Dispose();
            }
            public void RenderCustomExtraFrame(
                Action<MySpriteDrawFrame, RectangleF> customRender,
                RectangleF area
            )
            {
                var frame = extraScreen.DrawFrame();
                customRender?.Invoke(frame, area);
                frame.Dispose();
            }
            public void RenderMainScreen(
                string title,
                string[] options,
                int currentMenuIndex,
                string navigationInstructions
            )
            {
                var frame = mainScreen.DrawFrame();
                mainElements.Clear();

                // Draw the main background
                DrawBackground(frame, mainViewport, BLACK_BACKGROUND);

                // Add extra padding for the title
                float titlePaddingTop = 20f;
                DrawBackground(
                    frame,
                    new RectangleF(
                        new Vector2(0, titlePaddingTop),
                        new Vector2(mainViewport.Width, MAIN_VIEWPORT_HEIGHT)
                    ),
                    TITLE_BACKGROUND
                );

                // Create title container with added padding
                mainElements.Add(
                    new UIContainer(
                        new Vector2(0, titlePaddingTop),
                        new Vector2(mainViewport.Width, MAIN_VIEWPORT_HEIGHT)
                    )
                    {
                        BorderColor = BORDER_COLOR,
                        BorderThickness = BORDER_THICKNESS,
                        Padding = new Vector2(PADDING_TOP, PADDING_BOTTOM)
                    }.AddElement(
                        new UILabel(title, Vector2.Zero)
                        {
                            Scale = TITLE_SCALE,
                            TextColor = TITLE_COLOR
                        }
                    )
                );

                // Calculate total height for content
                float totalHeight = 0;
                foreach (string option in options)
                {
                    int lineCount = option.Split(new[] { '\n' }, StringSplitOptions.None).Length;
                    totalHeight += lineCount * OPTION_HEIGHT * OPTION_SCALE;
                }
                totalHeight += PADDING_BOTTOM;

                // Position content with padding
                var contentPosition = new Vector2(0, CONTENT_PADDING_TOP + titlePaddingTop);
                var contentSize = new Vector2(mainViewport.Width, totalHeight);
                var container = new UIContainer(contentPosition, contentSize)
                {
                    BorderColor = BORDER_COLOR,
                    BorderThickness = BORDER_THICKNESS,
                    Padding = new Vector2(PADDING_TOP, 5)
                };

                // Add options to the container
                float currentY = 0;
                for (int i = 0; i < options.Length; i++)
                {
                    string option = options[i];
                    int lineCount = option.Split(new[] { '\n' }, StringSplitOptions.None).Length;
                    float optionHeight = lineCount * OPTION_HEIGHT;

                    var optionText = new UILabel(option, new Vector2(20, currentY))
                    {
                        Scale = OPTION_SCALE,
                        TextColor = TEXT_COLOR
                    };

                    // Highlight the current option
                    if (i == currentMenuIndex)
                    {
                        AddArrowIndicator(
                            container,
                            new Vector2(5, currentY + optionHeight / 2 - 5)
                        );
                    }

                    container.AddElement(optionText);
                    currentY += optionHeight * OPTION_SCALE;
                }

                // Add the content container
                mainElements.Add(container);

                // Draw navigation instructions
                mainElements.Add(
                    new UIContainer(
                        new Vector2(0, contentSize.Y + titlePaddingTop + 60),
                        new Vector2(mainViewport.Width, NAVIGATION_INSTRUCTIONS_HEIGHT)
                    )
                    {
                        BorderColor = BORDER_COLOR,
                        BorderThickness = BORDER_THICKNESS,
                        Padding = new Vector2(PADDING_TOP, PADDING_BOTTOM)
                    }.AddElement(
                        new UILabel(navigationInstructions, Vector2.Zero)
                        {
                            Scale = NAVIGATION_SCALE,
                            TextColor = NAVIGATION_COLOR
                        }
                    )
                );

                // Draw all elements
                foreach (var element in mainElements)
                {
                    element.Draw(ref frame, mainViewport);
                }

                frame.Dispose();
            }

            public void RenderExtraScreen(string title, string content)
            {
                var frame = extraScreen.DrawFrame();
                extraElements.Clear();
                DrawBackground(frame, extraViewport, BLACK_BACKGROUND);
                DrawBackground(
                    frame,
                    new RectangleF(
                        new Vector2(0, 0),
                        new Vector2(extraViewport.Width, EXTRA_VIEWPORT_HEIGHT)
                    ),
                    TITLE_BACKGROUND
                );
                extraElements.Add(
                    new UIContainer(
                        new Vector2(0, 0),
                        new Vector2(extraViewport.Width, EXTRA_VIEWPORT_HEIGHT)
                    )
                    {
                        BorderColor = BORDER_COLOR,
                        BorderThickness = BORDER_THICKNESS,
                        Padding = new Vector2(PADDING_TOP, PADDING_BOTTOM)
                    }.AddElement(
                        new UILabel(title, Vector2.Zero)
                        {
                            Scale = TITLE_SCALE,
                            TextColor = TITLE_COLOR
                        }
                    )
                );
                int lineCount = content.Split(new[] { '\n' }, StringSplitOptions.None).Length;
                float lineHeight = OPTION_HEIGHT * OPTION_SCALE;
                float contentHeight = (lineCount * lineHeight) + (PADDING_TOP + PADDING_BOTTOM);
                contentHeight = Math.Max(contentHeight, EXTRA_VIEWPORT_HEIGHT + 10);
                extraElements.Add(
                    new UIContainer(
                        new Vector2(0, EXTRA_VIEWPORT_HEIGHT + 10),
                        new Vector2(extraViewport.Width, contentHeight)
                    )
                    {
                        BorderColor = BORDER_COLOR,
                        BorderThickness = BORDER_THICKNESS,
                        Padding = new Vector2(PADDING_TOP, PADDING_BOTTOM)
                    }.AddElement(
                        new UILabel(content, Vector2.Zero)
                        {
                            Scale = OPTION_SCALE,
                            TextColor = TEXT_COLOR,
                            FixedWidth = true
                        }
                    )
                );
                foreach (var element in extraElements)
                {
                    element.Draw(ref frame, extraViewport);
                }
                frame.Dispose();
            }
            private void AddArrowIndicator(UIContainer container, Vector2 position)
            {
                var arrowSprite = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = position + new Vector2(7, 0),
                    Size = new Vector2(10, 10),
                    Color = HIGHLIGHT_COLOR,
                    Alignment = TextAlignment.CENTER
                };
                container.AddElement(new UISquare(position, new Vector2(10, 10), HIGHLIGHT_COLOR));
            }
            private void DrawBackground(MySpriteDrawFrame frame, RectangleF area, Color color)
            {
                var backgroundSprite = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = area.Position + area.Size / 2,
                    Size = area.Size,
                    Color = color,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(backgroundSprite);
            }
            private void PrepareTextSurfaceForSprites(IMyTextSurface textSurface)
            {
                textSurface.ContentType = ContentType.SCRIPT;
                textSurface.Script = ""; // Ensure no other script is running
                textSurface.BackgroundColor = Color.Transparent; // Set to transparent if possible
                textSurface.FontColor = Color.Black; // Ensure font color is not causing any issues
                textSurface.FontSize = 0.1f; // Minimal font size to reduce impact
                textSurface.TextPadding = 0f; // No padding
                textSurface.Alignment = TextAlignment.CENTER;
            }

            private void InitializeUI()
            {
                mainScreen.BackgroundColor = BLACK_BACKGROUND;
                extraScreen.BackgroundColor = BLACK_BACKGROUND;
            }
        }
        abstract class UIElement
        {
            public Vector2 Position { get; set; }
            public float Scale { get; set; } = 1f;
            public Color TextColor { get; set; } = Color.Green;
            public UIElement(Vector2 position)
            {
                Position = position;
            }
            public abstract void Draw(ref MySpriteDrawFrame frame, RectangleF viewport);
        }

        class UILabel : UIElement
        {
            public string Text { get; set; }
            public bool FixedWidth { get; set; } = true; // Default to military precision
            public UILabel(string text, Vector2 position) : base(position)
            {
                Text = text;
            }
            public override void Draw(ref MySpriteDrawFrame frame, RectangleF viewport)
            {
                var lines = Text.Split('\n');
                float lineHeight = 20f * Scale;
                Vector2 startPos = Position + viewport.Position;
                for (int i = 0; i < lines.Length; i++)
                {
                    var textSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = lines[i],
                        Position = startPos + new Vector2(0, i * lineHeight),
                        RotationOrScale = Scale,
                        Color = TextColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = FixedWidth ? "Monospace" : "White"
                    };
                    frame.Add(textSprite);
                }
            }
        }

        class UIContainer : UIElement
        {
            public Vector2 Size { get; set; }
            public List<UIElement> Elements { get; } = new List<UIElement>();
            public Color BorderColor { get; set; } = Color.Green;
            public float BorderThickness { get; set; } = 2f;
            public Vector2 Padding { get; set; } = new Vector2(10f, 10f);

            public UIContainer(Vector2 position, Vector2 size) : base(position)
            {
                Size = size;
            }

            public UIContainer AddElement(UIElement element)
            {
                element.Position += Position + Padding;
                Elements.Add(element);
                return this;
            }

            public override void Draw(ref MySpriteDrawFrame frame, RectangleF viewport)
            {
                DrawBorder(ref frame, viewport);
                foreach (var element in Elements)
                {
                    element.Draw(ref frame, viewport);
                }
            }

            private void DrawBorder(ref MySpriteDrawFrame frame, RectangleF viewport)
            {
                var topLeft = Position + viewport.Position;
                var outerRect = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareHollow",
                    Position = topLeft + Size / 2,
                    Size = Size,
                    Color = BorderColor,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(outerRect);

                var glowingEffect = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = topLeft + Size / 2,
                    Size = Size - new Vector2(BorderThickness, BorderThickness),
                    Color = BorderColor * 0.5f,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(glowingEffect);
            }
        }

        class UISquare : UIElement
        {
            public Vector2 Size { get; set; }
            public Color FillColor { get; set; } = new Color(0, 50, 0); // Dark green for a military look
            public UISquare(Vector2 position, Vector2 size, Color fillColor) : base(position)
            {
                Size = size;
                FillColor = fillColor;
            }

            public override void Draw(ref MySpriteDrawFrame frame, RectangleF viewport)
            {
                var square = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = Position + viewport.Position + Size / 2,
                    Size = Size,
                    Color = FillColor,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(square);
            }
        }

        abstract class ProgramModule
        {
            protected Program ParentProgram;
            public ProgramModule(Program program)
            {
                ParentProgram = program;
            }
            public string name = "program";
            public abstract string[] GetOptions();
            public abstract void ExecuteOption(int index);
            public virtual void HandleSpecialFunction(int key) { }
            public virtual void Tick() { }
            public int currentTick = 0;
            public virtual string GetHotkeys()
            {
                return "";
            }
        }
        class RaycastCameraControl : ProgramModule
        {
            private IMyCameraBlock camera;
            private IMyTextSurfaceProvider cockpit;
            private IMyRemoteControl remoteControl;
            private IMyMotorStator rotor;
            private IMyMotorAdvancedStator hinge;
            private IMyTextSurface lcdTGP;
            private bool trackingActive = false;
            private int animationTicks = 0;
            private bool animating = false;
            private const int maxAnimationTicks = 100;
            private Vector3D currentTargetPosition = Vector3D.Zero;
            public RaycastCameraControl(Program program, Jet jet) : base(program)
            {
                name = "TargetingPod Control";
                camera =
                    program.GridTerminalSystem.GetBlockWithName("Camera Targeting Turret")
                    as IMyCameraBlock;
                if (camera != null)
                {
                    camera.EnableRaycast = true;
                }
                lcdTGP =
                    program.GridTerminalSystem.GetBlockWithName("LCD Targeting Pod")
                    as IMyTextSurface;
                if (lcdTGP != null)
                {
                    lcdTGP.ContentType = ContentType.TEXT_AND_IMAGE;
                }
                remoteControl =
                    program.GridTerminalSystem.GetBlockWithName("Remote Control")
                    as IMyRemoteControl;
                rotor =
                    program.GridTerminalSystem.GetBlockWithName("Targeting Rotor")
                    as IMyMotorStator;
                hinge =
                    program.GridTerminalSystem.GetBlockWithName("Targeting Hinge")
                    as IMyMotorAdvancedStator;
                cockpit = jet._cockpit;
            }
            public override string[] GetOptions()
            {
                string trackingStatus = trackingActive ? "[ON]" : "[OFF]";
                return new[]
                {
                    "Perform Raycast",
                    "Activate TV Screen",
                    $"Toggle GPS Lock {trackingStatus}",
                    "Back to Main Menu"
                };
            }
            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0:
                        ExecuteRaycast();
                        break;
                    case 1:
                        ActivateTVScreen();
                        break;
                    case 2:
                        ToggleGPSLock();
                        break;
                    case 3:
                        SystemManager.ReturnToMainMenu();
                        break;
                }
            }
            public override void Tick()
            {
                if (trackingActive)
                {
                    TrackTarget();
                }
                if (animating)
                {
                    AnimateCrosshair();
                }
            }
            private void ExecuteRaycast()
            {
                if (camera != null && camera.CanScan(35000))
                {
                    MyDetectedEntityInfo hitInfo = camera.Raycast(35000);
                    if (!hitInfo.IsEmpty())
                    {
                        Vector3D target = hitInfo.HitPosition ?? Vector3D.Zero;
                        string gpsCoordinates =
                            "Cached:GPS:Target2:"
                            + target.X
                            + ":"
                            + target.Y
                            + ":"
                            + target.Z
                            + ":#FF75C9F1:";
                        UpdateCustomDataWithCache(gpsCoordinates);
                        DisplayRaycastResult(gpsCoordinates);
                    }
                    else
                    {
                        DisplayRaycastResult("No target detected.");
                    }
                }
                else
                {
                    DisplayRaycastResult("Camera is not ready or cannot perform raycast.");
                }
            }
            private void DisplayRaycastResult(string result)
            {
                if (lcdTGP != null)
                {
                    StringBuilder output = new StringBuilder();
                    output.AppendLine("╔════════════════════╗");
                    output.AppendLine("║    RAYCAST RESULT   ║");
                    output.AppendLine("╠════════════════════╣");
                    output.AppendLine(result);
                    output.AppendLine("╚════════════════════╝");
                    lcdTGP.WriteText(output.ToString());
                }
            }
            private void UpdateCustomDataWithCache(string gpsCoordinates)
            {
                string[] customDataLines = ParentProgram.Me.CustomData.Split('\n');
                bool cachedLineFound = false;
                for (int i = 0; i < customDataLines.Length; i++)
                {
                    if (customDataLines[i].StartsWith("Cached:"))
                    {
                        customDataLines[i] = gpsCoordinates;
                        cachedLineFound = true;
                        break;
                    }
                }
                if (!cachedLineFound)
                {
                    List<string> customDataList = new List<string>(customDataLines);
                    customDataList.Add(gpsCoordinates);
                    customDataLines = customDataList.ToArray();
                }
                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
            }
            private Vector2 center = new Vector2(25, 17);
            private bool isLocked = true;
            private bool animationstarted = false;
            private int maxTicks = 50;
            private void AnimateCrosshair()
            {
                if (!animationstarted)
                {
                    return;
                }
                lcdTGP.ContentType = ContentType.TEXT_AND_IMAGE;
                lcdTGP.Font = "Monospace";
                lcdTGP.FontSize = 0.5f;
                lcdTGP.TextPadding = 0f;
                lcdTGP.Alignment = TextAlignment.LEFT;
                lcdTGP.BackgroundColor = new Color(0, 0, 0);
                StringBuilder output = new StringBuilder();
                for (int i = 0; i < 35; i++)
                {
                    output.AppendLine(new string(' ', 53));
                }
                float progress = (float)animationTicks / maxTicks;
                int horizontalLength = (int)(2 + (isLocked ? 0 : 2 * progress));
                int verticalLength = (int)(1 + (isLocked ? 0 : 1 * progress));
                int leftX = (int)center.X - horizontalLength;
                int rightX = (int)center.X + horizontalLength;
                int topY = (int)center.Y - verticalLength;
                int bottomY = (int)center.Y + verticalLength;
                Color boxColor = isLocked ? new Color(0, 255, 0) : new Color(255, 0, 0);
                lcdTGP.FontColor = boxColor;
                for (int x = leftX; x <= rightX; x++)
                {
                    SetSymbolAtPosition(output, x, topY, '─');
                    SetSymbolAtPosition(output, x, bottomY, '─');
                }
                for (int y = topY; y <= bottomY; y++)
                {
                    SetSymbolAtPosition(output, leftX, y, '│');
                    SetSymbolAtPosition(output, rightX, y, '│');
                }
                SetSymbolAtPosition(output, leftX, topY, isLocked ? '┌' : '╭');
                SetSymbolAtPosition(output, rightX, topY, isLocked ? '┐' : '╮');
                SetSymbolAtPosition(output, leftX, bottomY, isLocked ? '└' : '╰');
                SetSymbolAtPosition(output, rightX, bottomY, isLocked ? '┘' : '╯');
                if (!isLocked)
                {
                    SetSymbolAtPosition(output, leftX - 1, topY, '─');
                    SetSymbolAtPosition(output, rightX + 1, topY, '─');
                    SetSymbolAtPosition(output, leftX - 1, bottomY, '─');
                    SetSymbolAtPosition(output, rightX + 1, bottomY, '─');
                    SetSymbolAtPosition(output, leftX, topY - 1, '│');
                    SetSymbolAtPosition(output, rightX, topY - 1, '│');
                    SetSymbolAtPosition(output, leftX, bottomY + 1, '│');
                    SetSymbolAtPosition(output, rightX, bottomY + 1, '│');
                }
                lcdTGP.WriteText(output.ToString());
                animationTicks++;
                if (animationTicks > maxTicks)
                {
                    animationTicks = 0;
                    isLocked = !isLocked;
                    animationstarted = false;
                }
            }
            private void SetSymbolAtPosition(StringBuilder output, int x, int y, char symbol)
            {
                int lineLength = 54 + 1;
                if (x >= 0 && x < 54 && y >= 0 && y < 35)
                {
                    int index = y * lineLength + x;
                    if (index < output.Length)
                    {
                        output[index] = symbol;
                    }
                }
            }
            private void ActivateTVScreen()
            {
                if (cockpit == null)
                {
                    return;
                }
                IMyTextSurface screen = cockpit.GetSurface(1);
                if (screen == null)
                {
                    return;
                }
                screen.ContentType = ContentType.SCRIPT;
            }
            private void ToggleGPSLock()
            {
                animationstarted = true;
                if (trackingActive)
                {
                    trackingActive = false;
                    animationTicks = 0;
                    animating = true;
                }
                else
                {
                    string customData = ParentProgram.Me.CustomData;
                    Vector3D targetPosition;
                    if (TryParseCachedGPSCoordinates(customData, out targetPosition))
                    {
                        currentTargetPosition = targetPosition;
                        trackingActive = true;
                        animationTicks = 0;
                        animating = true;
                    }
                    else { }
                }
            }
            private void TrackTarget()
            {
                if (remoteControl == null || rotor == null || hinge == null)
                {
                    return;
                }
                string customData = ParentProgram.Me.CustomData;
                Vector3D targetPosition;
                if (TryParseCachedGPSCoordinates(customData, out targetPosition))
                {
                    currentTargetPosition = targetPosition;
                }
                Vector3D cameraPosition = camera.GetPosition();
                Vector3D directionToTarget = VectorMath.SafeNormalize(
                    currentTargetPosition - cameraPosition
                );
                Vector3D cameraForward = -camera.WorldMatrix.Forward;
                double dotProductForward = Vector3D.Dot(cameraForward, directionToTarget);
                double angleToTargetForward = Math.Acos(
                    MathHelper.Clamp(dotProductForward, -1.0, 1.0)
                );
                angleToTargetForward = MathHelper.ToDegrees(angleToTargetForward);
                Vector3D remotePosition = remoteControl.GetPosition();
                MatrixD remoteOrientation = remoteControl.WorldMatrix;
                Vector3D relativeTargetPosition = Vector3D.TransformNormal(
                    currentTargetPosition - remotePosition,
                    MatrixD.Transpose(remoteOrientation)
                );
                float kP_rotor = 0.05f;
                float kP_hinge = 0.05f;
                double dampingFactor = Math.Max(0.05, Math.Min(1.0, angleToTargetForward / 90.0));
                double rotorVelocity = -(kP_rotor * relativeTargetPosition.X) * dampingFactor;
                double hingeVelocity = -(kP_hinge * relativeTargetPosition.Y) * dampingFactor;
                rotorVelocity = MathHelper.Clamp(rotorVelocity, -5.0, 5.0);
                hingeVelocity = MathHelper.Clamp(hingeVelocity, -5.0, 5.0);
                rotor.TargetVelocityRPM = (float)rotorVelocity;
                hinge.TargetVelocityRPM = (float)hingeVelocity;
                if (angleToTargetForward < 2.0)
                {
                    rotor.TargetVelocityRPM = 0f;
                    hinge.TargetVelocityRPM = 0f;
                }
            }
            private bool TryParseCachedGPSCoordinates(string input, out Vector3D targetPosition)
            {
                targetPosition = Vector3D.Zero;
                string[] lines = input.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("Cached:GPS:"))
                    {
                        string[] parts = line.Split(':');
                        if (parts.Length >= 6)
                        {
                            double x,
                                y,
                                z;
                            if (
                                double.TryParse(parts[3], out x)
                                && double.TryParse(parts[4], out y)
                                && double.TryParse(parts[5], out z)
                            )
                            {
                                targetPosition = new Vector3D(x, y, z);
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            public override void HandleSpecialFunction(int key)
            {
                if (key == 5)
                {
                    ExecuteRaycast();
                }
                if (key == 6)
                {
                    ActivateTVScreen();
                }
            }
            public override string GetHotkeys()
            {
                return "5: Perform Raycast\n8: Activate TV Screen\n9: Toggle GPS Lock\n";
            }
        }

        public class PIDController
        {
            public double Kp { get; set; } // Proportional gain
            public double Ki { get; set; } // Integral gain
            public double Kd { get; set; } // Derivative gain

            private double integral;
            private double previousError;
            private double outputMin;
            private double outputMax;

            public PIDController(
                double kp,
                double ki,
                double kd,
                double outputMin = double.MinValue,
                double outputMax = double.MaxValue
            )
            {
                Kp = kp;
                Ki = ki;
                Kd = kd;
                this.outputMin = outputMin;
                this.outputMax = outputMax;
                integral = 0;
                previousError = 0;
            }

            public double Update(double setpoint, double pv, double deltaTime)
            {
                double error = setpoint - pv;

                // Integral term with anti-windup
                integral += error * deltaTime;
                integral = MathHelper.Clamp(integral, -100, 100); // Adjust limits as needed

                // Derivative term
                double derivative = (error - previousError) / deltaTime;

                // PID output
                double output = (Kp * error) + (Ki * integral) + (Kd * derivative);

                // Clamp output
                output = MathHelper.Clamp(output, outputMin, outputMax);

                previousError = error;

                return output;
            }

            public void Reset()
            {
                integral = 0;
                previousError = 0;
            }
        }

        class HUDModule : ProgramModule
        {
            IMyCockpit cockpit;
            IMyTextSurface hud;
            IMyTerminalBlock hudBlock;
            double peakGForce = 0;
            float currentTrim;
            double pitch = 0;
            double roll = 0;
            double velocity;
            double deltaTime;
            const double speedOfSound = 343.0; // Speed of sound in m/s at sea level
            double mach;
            List<IMyTerminalBlock> leftstab = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> rightstab = new List<IMyTerminalBlock>();
            Queue<double> velocityHistory = new Queue<double>();
            private Queue<AltitudeTimePoint> altitudeHistory = new Queue<AltitudeTimePoint>(); Queue<double> gForcesHistory = new Queue<double>();
            Queue<double> aoaHistory = new Queue<double>();
            private List<IMyThrust> thrusters = new List<IMyThrust>();
            private List<IMyGasTank> tanks = new List<IMyGasTank>();
            private List<IMyDoor> airbreaks = new List<IMyDoor>();
            const int smoothingWindowSize = 10;
            double smoothedVelocity = 0;
            double smoothedAltitude = 0;
            double smoothedGForces = 0;
            double smoothedAoA = 0;
            double smoothedThrottle = 0;
            float throttlecontrol = 0f;
            bool hydrogenswitch = false;
            Vector3D previousVelocity = Vector3D.Zero;
            string hotkeytext = "Test";
            Jet myjet;
            private TimeSpan totalElapsedTime = TimeSpan.Zero;

            public HUDModule(Program program, Jet jet) : base(program)
            {
                cockpit = jet._cockpit;
                hudBlock = jet.hudBlock;
                hud = jet.hud;

                rightstab = jet.rightstab;
                leftstab = jet.leftstab;

                thrusters = jet._thrustersbackwards;
                tanks = jet.tanks;
                myjet = jet;
                if (hudBlock == null)
                {
                    return;
                }

                hud = hudBlock as IMyTextSurface;
                if (hud == null)
                {
                    var surfaceProvider = hudBlock as IMyTextSurfaceProvider;
                    if (surfaceProvider != null)
                    {
                        hud = surfaceProvider.GetSurface(0);
                    }
                }

                if (hud == null)
                {
                    return;
                }

                hud.ContentType = ContentType.SCRIPT;
                hud.ScriptBackgroundColor = new Color(0, 0, 0, 0);
                hud.ScriptForegroundColor = Color.White;

                ParentProgram.GridTerminalSystem.GetBlocksOfType(airbreaks);
                name = "HUD Control";
            }

            public override string GetHotkeys()
            {
                return hotkeytext;
            }

            public class CircularBuffer<T> : Queue<T>
            {
                private readonly int _capacity;

                public CircularBuffer(int capacity) : base(capacity)
                {
                    _capacity = capacity;
                }

                public new void Enqueue(T item)
                {
                    base.Enqueue(item);
                    if (Count > _capacity)
                    {
                        Dequeue();
                    }
                }
            }

// --- Constants ---
const int INTERCEPT_ITERATIONS = 10; // Number of iterations for ballistic calculation
    const double MIN_Z_FOR_PROJECTION = 0.1; // Minimum absolute Z value for safe projection
    const string TEXTURE_SQUARE = "SquareSimple";
    const string TEXTURE_CIRCLE = "CircleHollow"; // Assumes you have this texture
    const string TEXTURE_TRIANGLE = "Triangle";

    // --- Assume these are accessible class members or passed in ---
    // IMyCockpit cockpit; // The cockpit block
    // IMyTextSurface hud; // The surface provider (e.g., cockpit HUD)
    // Color pipColor = Color.LimeGreen;
    // Color offScreenColor = Color.Yellow;
    // Color behindColor = Color.Red;
    // Color reticleColor = Color.Cyan;

    // Helper function for drawing lines (assuming SquareSimple texture)
    private void AddLineSprite(MySpriteDrawFrame frame, Vector2 start, Vector2 end, float thickness, Color color)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (length < 0.1f) return; // Don't draw zero-length lines

        Vector2 position = start + delta / 2f; // Center of the line
        float rotation = (float)Math.Atan2(delta.Y, delta.X) - (float)Math.PI / 2f; // Rotation to align the square

        var line = new MySprite()
        {
            Type = SpriteType.TEXTURE,
            Data = TEXTURE_SQUARE,
            Position = position,
            Size = new Vector2(thickness, length),
            Color = color,
            RotationOrScale = rotation,
            Alignment = TextAlignment.CENTER
        };
        frame.Add(line);
    }


    /// <summary>
    /// Calculates the intercept point for a projectile affected by gravity hitting a target moving at constant velocity.
    /// Uses an iterative approach.
    /// </summary>
    /// <returns>True if a solution was found, false otherwise.</returns>
    private bool CalculateInterceptPointIterative(
        Vector3D shooterPosition,
        Vector3D shooterVelocity, // Current shooter velocity (affects initial projectile velocity)
        double projectileSpeed,   // Muzzle speed relative to the shooter
        Vector3D targetPosition,
        Vector3D targetVelocity,
        Vector3D gravity,         // Gravity vector (e.g., new Vector3D(0, -9.81, 0))
        int maxIterations,
        out Vector3D interceptPoint,
        out double timeToIntercept)
    {
        interceptPoint = Vector3D.Zero;
        timeToIntercept = -1;

        Vector3D relativePosition = targetPosition - shooterPosition;
        Vector3D relativeVelocity = targetVelocity - shooterVelocity; // Target velocity relative to shooter

        // Initial guess for timeToIntercept using simple non-gravity calculation
        // Solve |P_tgt + V_rel*t| = S_proj*t -> |P_tgt|^2 + 2*P_tgt.V_rel*t + |V_rel|^2*t^2 = S_proj^2*t^2
        double a = relativeVelocity.LengthSquared() - projectileSpeed * projectileSpeed;
        double b = 2 * Vector3D.Dot(relativePosition, relativeVelocity);
        double c = relativePosition.LengthSquared();
        double t_guess = -1;

        // Use quadratic formula to find initial guess time (ignore gravity for guess)
        if (Math.Abs(a) < 1e-6) // Linear case
        {
            if (Math.Abs(b) > 1e-6) t_guess = -c / b;
        }
        else
        {
            double discriminant = b * b - 4 * a * c;
            if (discriminant >= 0)
            {
                double sqrtDiscriminant = Math.Sqrt(discriminant);
                double t1 = (-b - sqrtDiscriminant) / (2 * a);
                double t2 = (-b + sqrtDiscriminant) / (2 * a);
                if (t1 > 0 && t2 > 0) t_guess = Math.Min(t1, t2);
                else t_guess = Math.Max(t1, t2); // Take the positive one if only one is positive
            }
        }

        if (t_guess <= 0) return false; // No initial solution / target moving away too fast

        timeToIntercept = t_guess;

        // --- Iterative Refinement ---
        for (int i = 0; i < maxIterations; ++i)
        {
            if (timeToIntercept <= 0) break; // Safety check

            // 1. Predict target position at the current estimated time t
            Vector3D predictedTargetPos = targetPosition + targetVelocity * timeToIntercept;

            // 2. Calculate the displacement needed for the projectile
            Vector3D projectileDisplacement = predictedTargetPos - shooterPosition;

            // 3. Calculate the required initial velocity (V_launch) to cover that displacement
            //    accounting for gravity: displacement = V_launch * t + 0.5 * g * t^2
            //    => V_launch = (displacement - 0.5 * g * t^2) / t
            Vector3D requiredLaunchVel = (projectileDisplacement - 0.5 * gravity * timeToIntercept * timeToIntercept) / timeToIntercept;

            // 4. The *direction* is correct, but the *speed* might not match projectileSpeed.
            //    Find the *actual* launch velocity: direction * projectileSpeed + shooterVelocity
            Vector3D launchDirection = Vector3D.Normalize(requiredLaunchVel);
            Vector3D actualLaunchVel = launchDirection * projectileSpeed + shooterVelocity; // Absolute initial velocity

            // 5. Re-calculate timeToIntercept. This is tricky. A common simplification is to use
            //    the distance to the *predicted* target point and the projectile *speed*.
            //    A more complex approach involves solving the kinematic equation again.
            //    Let's use a simpler distance/speed update for the *time* estimate.
            //    Note: This simplification might lose some accuracy compared to a full kinematic solve.

            // Calculate new relative velocity considering the *actual* launch velocity
            Vector3D newRelativeVelocity = targetVelocity - actualLaunchVel;

            // Re-solve the quadratic using the original relative position but the *new* relative velocity
            // This refines the time estimate based on the required launch direction.
            a = newRelativeVelocity.LengthSquared() - projectileSpeed * projectileSpeed; // This 'a' might be less stable, consider alternative time updates if issues arise.
            b = 2 * Vector3D.Dot(relativePosition, newRelativeVelocity);
            c = relativePosition.LengthSquared();

            t_guess = -1;
            if (Math.Abs(a) < 1e-6) { if (Math.Abs(b) > 1e-6) t_guess = -c / b; }
            else
            {
                double discriminant = b * b - 4 * a * c;
                if (discriminant >= 0)
                {
                    double sqrtDiscriminant = Math.Sqrt(discriminant);
                    double t1 = (-b - sqrtDiscriminant) / (2 * a);
                    double t2 = (-b + sqrtDiscriminant) / (2 * a);
                    if (t1 > 0 && t2 > 0) t_guess = Math.Min(t1, t2);
                    else t_guess = Math.Max(t1, t2);
                }
            }

            if (t_guess <= 0)
            {
                // Lost solution during iteration, maybe return last valid? Or fail.
                // Let's fail for now. Consider fallback if needed.
                return false;
            }
            timeToIntercept = t_guess; // Update time for next iteration
        }

        // After iterations, calculate the final intercept point
        interceptPoint = targetPosition + targetVelocity * timeToIntercept;
        return timeToIntercept > 0;
    }

            bool WorldPositionToScreenPosition(Vector3D worldPosition, IMyCockpit cam, IMyTextPanel screen, out Vector2 screenPositionPx)
            {
                screenPositionPx = Vector2.Zero;

                Vector3D cameraPos = cam.GetPosition() + cam.WorldMatrix.Forward * 1; // There is a ~0.25 meter forward offset for the view origin of cameras
                Vector3D screenPosition = screen.GetPosition() + screen.WorldMatrix.Forward * 0.5 * screen.CubeGrid.GridSize;
                Vector3D normal = screen.WorldMatrix.Forward;
                Vector3D cameraToScreen = screenPosition - cameraPos;
                double distanceToScreen = Math.Abs(Vector3D.Dot(cameraToScreen, normal));

                Vector3D viewCenterWorld = distanceToScreen * cam.WorldMatrix.Forward;

                // Project direction onto the screen plane (world coords)
                Vector3D direction = worldPosition - cameraPos;
                Vector3D directionParallel = direction.Dot(normal) * normal;
                double distanceRatio = distanceToScreen / directionParallel.Length();

                Vector3D directionOnScreenWorld = distanceRatio * direction;

                // If we are pointing backwards, ignore
                if (directionOnScreenWorld.Dot(screen.WorldMatrix.Forward) < 0)
                {
                    return false;
                }

                Vector3D planarCameraToScreen = cameraToScreen - Vector3D.Dot(cameraToScreen, normal) * normal;
                directionOnScreenWorld -= planarCameraToScreen;

                // Convert location to be screen local (world coords)
                Vector2 directionOnScreenLocal = new Vector2(
                    (float)directionOnScreenWorld.Dot(screen.WorldMatrix.Right),
                    (float)directionOnScreenWorld.Dot(screen.WorldMatrix.Down));

                // ASSUMPTION:
                // The screen is square
                double screenWidthInMeters = 1f; // My magic number for large grid
                float metersToPx = (float)(screen.TextureSize.X / screenWidthInMeters);

                // Convert dorection to be screen local (pixel coords)
                directionOnScreenLocal *= metersToPx;

                // Get final location on screen
                Vector2 screenCenterPx = screen.TextureSize * 0.5f;
                screenPositionPx = screenCenterPx + directionOnScreenLocal;
                return true;
            }
            private void DrawLeadingPip(
        MySpriteDrawFrame frame,
        IMyCockpit cockpit, // Pass cockpit for world matrix
        IMyTextSurface hud,   // Pass HUD surface for size/center
        Vector3D targetPosition,
        Vector3D targetVelocity,
        Vector3D shooterPosition,
        Vector3D shooterVelocity,
        double projectileSpeed,
        Vector3D gravity,         // World gravity vector
        Color pipColor,           // Customizable colors
        Color offScreenColor,
        Color behindColor,
        Color reticleColor
    )
    {
        if (cockpit == null || hud == null) return; // Safety check
                const float MIN_DISTANCE_FOR_SCALING = 50f;  // Target closer than this uses max pip size (e.g., 500 meters)
                const float MAX_DISTANCE_FOR_SCALING = 3000f; // Target farther than this uses min pip size (e.g., 3000 meters)
                const float MAX_PIP_SIZE_FACTOR = 0.1f;      // Pip size factor at min distance (relative to viewportMinDim)
                const float MIN_PIP_SIZE_FACTOR = 0.001f;     // Pip size factor at max distance (relative to viewportMinDim)

                Vector3D interceptPoint;
        double timeToIntercept;
                bool isAimingAtPip = false; // Initialize the output parameter to false
                                       // Use the iterative solver
                if (!CalculateInterceptPointIterative(
                shooterPosition,
                shooterVelocity,
                projectileSpeed,
                targetPosition,
                targetVelocity,
                gravity,
                INTERCEPT_ITERATIONS,
                out interceptPoint,
                out timeToIntercept
            ))
        {
            // Cannot calculate intercept. Optionally draw something else,
            // like a marker at the current target position, or just return.
            // For now, just return. Consider adding a "No Solution" indicator.
            return;
        }

                // --- Projection onto HUD ---
                Vector3D directionToIntercept = interceptPoint - shooterPosition;
                // MatrixD worldToCockpitMatrix = MatrixD.Transpose(cockpit.WorldMatrix); // OLD
                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix); // TRY THIS INSTEAD
                Vector3D localDirectionToIntercept = Vector3D.TransformNormal(directionToIntercept, worldToCockpitMatrix); // Transform direction
                                                                                                                           // OR if transforming a POINT is needed (less likely for direction vector, but consider):
                                                                                                                           // Vector3D localInterceptPoint = Vector3D.Transform(interceptPoint, worldToCockpitMatrix);
                                                                                                                           // Vector3D localShooterPosition = Vector3D.Transform(shooterPosition, worldToCockpitMatrix);
                                                                                                                           // Vector3D localDirectionToIntercept = localInterceptPoint - localShooterPosition;

                // --- Screen center and size ---
                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;
                float viewportMinDim = Math.Min(surfaceSize.X, surfaceSize.Y);
                float targetMarkerSize = viewportMinDim * 0.02f; // Size of the arms of the 'X'
                                                                 // Define sizes relative to screen dimensions for scalability
                float lineThickness = Math.Max(1f, viewportMinDim * 0.004f);
                // float pipBaseSize = viewportMinDim * 0.03f; // <<< REMOVE OR COMMENT OUT THIS LINE
                float reticleArmLength = viewportMinDim * 0.025f;
                float arrowSize = viewportMinDim * 0.04f;
                float arrowHeadSize = viewportMinDim * 0.025f;
                double distanceToIntercept = Vector3D.Distance(shooterPosition, interceptPoint);
                // Calculate scaling factor (0 = max distance, 1 = min distance)
                float distanceScaleFactor = (float)MathHelper.Clamp((MAX_DISTANCE_FOR_SCALING - distanceToIntercept) / (MAX_DISTANCE_FOR_SCALING - MIN_DISTANCE_FOR_SCALING), 0.0, 1.0);
                // Interpolate between min and max size factors based on distance
                float currentPipSizeFactor = MathHelper.Lerp(MIN_PIP_SIZE_FACTOR, MAX_PIP_SIZE_FACTOR, distanceScaleFactor);
                float dynamicPipSize = viewportMinDim * currentPipSizeFactor; // <<< THIS IS YOUR NEW DYNAMIC SIZE


                // Check if target is behind
                if (localDirectionToIntercept.Z > MIN_Z_FOR_PROJECTION) // Target is behind (positive Z in local coords)
        {
            // Draw "Behind" indicator (e.g., simple Red cross at center)
            AddLineSprite(frame, center - new Vector2(reticleArmLength, 0), center + new Vector2(reticleArmLength, 0), lineThickness, behindColor);
            AddLineSprite(frame, center - new Vector2(0, reticleArmLength), center + new Vector2(0, reticleArmLength), lineThickness, behindColor);
            return; // Don't draw main reticle or pip if target is behind
        }

        // --- Draw Central Reticle (Always visible when target is in front hemisphere) ---
        AddLineSprite(frame, center - new Vector2(reticleArmLength, 0), center + new Vector2(reticleArmLength, 0), lineThickness, reticleColor);
        AddLineSprite(frame, center - new Vector2(0, reticleArmLength), center + new Vector2(0, reticleArmLength), lineThickness, reticleColor);

        // --- Perspective Projection ---
        // Prevent division by zero/very small numbers if target is near perpendicular
        if (Math.Abs(localDirectionToIntercept.Z) < MIN_Z_FOR_PROJECTION)
        {
            // Target is nearly 90 degrees off - treat as off-screen edge case
            localDirectionToIntercept.Z = -MIN_Z_FOR_PROJECTION; // Project as if slightly in front
        }

                // Calculate screen coordinates using perspective projection
                // The factor of surfaceSize.Y / 2 is common for FOV scaling, adjust if needed based on game's projection
                //Todo: X and Y are probably not tthe same length, so the FOV might be different. 

                float scale = surfaceSize.Y / (0.3555f); // Or adjust based on actual FOV / projection method
                float screenX = center.X + (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z) * scale;
        float screenY = center.Y + (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z) * scale; // Y is inverted in screen space
        Vector2 pipScreenPos = new Vector2(screenX, screenY);

                bool isOnScreen = pipScreenPos.X >= 0 && pipScreenPos.X <= surfaceSize.X &&
                                  pipScreenPos.Y >= 0 && pipScreenPos.Y <= surfaceSize.Y;
                float distanceToPip = Vector2.Distance(center, pipScreenPos);
                // float pipRadius = pipBaseSize / 2f; // <<< REMOVE OR COMMENT OUT THIS LINE
                float pipRadius = dynamicPipSize / 2f; // <<< USE THE NEW DYNAMIC SIZE FOR AIMING CHECK
                // Check if the distance is less than or equal to the pip's radius
                if (distanceToPip <= pipRadius)
                {
                    isAimingAtPip = true;
                    // Optional: Change pip color or add another visual cue when aiming
                    // pipSprite.Color = Color.Lime; // Example: Turn pip green
                }
                if (isAimingAtPip)
                {
                    for (int i = 0; i < myjet._gatlings.Count; i++)
                    {
                        myjet._gatlings[i].Enabled = true;
                    }
                }
                else
                {
                    if (myjet.manualfire == false)
                    {
                        for (int i = 0; i < myjet._gatlings.Count; i++)
                        {
                            myjet._gatlings[i].Enabled = false;
                        }
                    }
                }
                if (isOnScreen)
        {
            // --- Draw On-Screen Pip (e.g., Hollow Circle) ---
            var pipSprite = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = TEXTURE_CIRCLE, // Use a hollow circle texture
                Position = pipScreenPos,
                Size = new Vector2(dynamicPipSize, dynamicPipSize), // <<< USE THE NEW DYNAMIC SIZE FOR DRAWING
                                                                    Color = pipColor,
                Alignment = TextAlignment.CENTER
            };
            frame.Add(pipSprite);
                    // --- ADDED: Draw Target Marker and Line if BOTH Pip and Target are On Screen ---
                    Vector2 targetScreenPos = Vector2.Zero; // Initialize
                    const float velocityIndicatorScale = 20f; // Example: Represents 0.3 seconds of travel
                    // --- ADDED: Calculate Projection for the Target's Current Position ---
                    // --- ADDED: Calculate projection for the Target Velocity Indicator ---
                    // 1. Define a point offset from the INTERCEPT point by the target's velocity (scaled by time)
                    //    This represents where the target would be 'velocityIndicatorScale' seconds AFTER intercept.
                    Vector3D targetVelocityEndPointWorld = interceptPoint + targetVelocity * velocityIndicatorScale;

                    // 2. Transform this world point into local cockpit coordinates
                    //    Use Transform for points, TransformNormal for direction vectors
                    // Calculate the proper inverse matrix
                    MatrixD worldToLocalMatrix = MatrixD.Invert(cockpit.WorldMatrix); // <<< KEEP THIS LINE (needed below)                    // Transform the world point to local point using the inverse matrix
                    Vector3D localTargetVelocityEndPoint = Vector3D.Transform(targetVelocityEndPointWorld, worldToLocalMatrix); // <<< REMOVE/COMMENT OUT                    // 3. Project this local point onto the screen
                    // --- MODIFY this section ---
                    Vector2 targetVelEndPointScreenPos = Vector2.Zero; // Initialize
                    bool isVelEndPointProjectable = false; // <<< ADD THIS FLAG

                    if (localTargetVelocityEndPoint.Z < -MIN_Z_FOR_PROJECTION) // Check if it's in front // <<< REMOVE/COMMENT OUT BLOCK
                    {
                        float screenX_vel = center.X + (float)(localTargetVelocityEndPoint.X / -localTargetVelocityEndPoint.Z) * scale;
                        float screenY_vel = center.Y + (float)(-localTargetVelocityEndPoint.Y / -localTargetVelocityEndPoint.Z) * scale; // Y inverted
                        targetVelEndPointScreenPos = new Vector2(screenX_vel, screenY_vel);
                        isVelEndPointProjectable = true;
                    } // <<< REMOVE/COMMENT OUT BLOCK
                      // --- END MODIFY ---
                    Vector3D directionToTarget = targetPosition - shooterPosition;
                    Vector3D localDirectionToTarget = Vector3D.TransformNormal(directionToTarget, worldToLocalMatrix);

                    Vector2 currentTargetScreenPos = Vector2.Zero; // Initialize
                    bool isCurrentTargetProjectable = false; // Flag to check if projection is valid

                    // Check if the current target direction is in front of the cockpit view
                    if (localDirectionToTarget.Z < -MIN_Z_FOR_PROJECTION)
                    {
                        // Prevent division by zero/small numbers if near perpendicular
                        // (Technically handled by the check above, but good practice)
                        // float zClamped = Math.Max(Math.Abs((float)localDirectionToTarget.Z), (float)MIN_Z_FOR_PROJECTION);

                        // Calculate screen coordinates using perspective projection
                        float screenX_tgt = center.X + (float)(localDirectionToTarget.X / -localDirectionToTarget.Z) * scale;
                        float screenY_tgt = center.Y + (float)(-localDirectionToTarget.Y / -localDirectionToTarget.Z) * scale; // Y inverted
                        currentTargetScreenPos = new Vector2(screenX_tgt, screenY_tgt);
                        isCurrentTargetProjectable = true; // Mark as projectable
                    }
                    // Screen center and size
                    float halfMark = targetMarkerSize / 2f;
                    if (isOnScreen)
                    {
                        // Draw Yellow 'X' at the current target's screen position
                        AddLineSprite(frame, currentTargetScreenPos - new Vector2(halfMark, halfMark), currentTargetScreenPos + new Vector2(halfMark, halfMark), lineThickness, Color.Yellow); // Use targetIndicatorColor?
                        AddLineSprite(frame, currentTargetScreenPos - new Vector2(halfMark, -halfMark), currentTargetScreenPos + new Vector2(halfMark, -halfMark), lineThickness, Color.Yellow); // Use targetIndicatorColor?

                        // Draw Yellow line connecting the aiming PIP to the current target 'X'
                        AddLineSprite(frame, pipScreenPos, currentTargetScreenPos, lineThickness, Color.Yellow); // Use targetIndicatorColor?
                    }
                    // ***** AIMING CHECK *****
                    // Calculate distance between screen center (reticle) and pip center

                    // Optional: Add time-to-intercept text
                    // var timeText = MySprite.CreateText($"{timeToIntercept:F1}s", "Debug", Color.White, 0.5f, TextAlignment.CENTER);
                    // timeText.Position = pipScreenPos + new Vector2(0, pipBaseSize * 0.6f); // Position below pip
                    // frame.Add(timeText);
                }
        else
        {
            // --- Draw Off-Screen Indicator (Arrow from edge) ---
            // Calculate direction vector from center to the raw off-screen position
            Vector2 direction = pipScreenPos - center;
            direction.Normalize(); // Make it a unit vector

            // Calculate intersection point with screen edges
            // This is a simplified approach; a more robust one might use line-rect intersection
            float maxDistX = surfaceSize.X / 2f - arrowSize / 2f; // Leave margin for arrow
            float maxDistY = surfaceSize.Y / 2f - arrowSize / 2f;
            float angle = (float)Math.Atan2(direction.Y, direction.X);

            float edgeX = (float)Math.Cos(angle) * maxDistX;
            float edgeY = (float)Math.Sin(angle) * maxDistY;

            // Find which edge is hit first based on aspect ratio and angle
            Vector2 edgePoint;
            if (Math.Abs(edgeX / maxDistX) > Math.Abs(edgeY / maxDistY))
            {
                // Hit left/right edge
                edgePoint = new Vector2(center.X + Math.Sign(edgeX) * maxDistX, center.Y + edgeY * (maxDistX / Math.Abs(edgeX)));
            }
            else
            {
                // Hit top/bottom edge
                edgePoint = new Vector2(center.X + edgeX * (maxDistY / Math.Abs(edgeY)), center.Y + Math.Sign(edgeY) * maxDistY);
            }


            // Clamp point rigorously just in case
            edgePoint.X = MathHelper.Clamp(edgePoint.X, arrowSize / 2f, surfaceSize.X - arrowSize / 2f);
            edgePoint.Y = MathHelper.Clamp(edgePoint.Y, arrowSize / 2f, surfaceSize.Y - arrowSize / 2f);


            // Draw the arrow head (Triangle) pointing inward
            float arrowRotation = (float)Math.Atan2(direction.Y, direction.X); // Point towards center
            var arrowSprite = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = TEXTURE_TRIANGLE, // Use Triangle texture
                Position = edgePoint,    // Position at the clamped edge point
                Size = new Vector2(arrowHeadSize, arrowHeadSize),
                Color = offScreenColor,
                RotationOrScale = arrowRotation + (float)Math.PI / 2f, // Point inward (adjust angle based on Triangle sprite orientation)
                Alignment = TextAlignment.CENTER
            };
            frame.Add(arrowSprite);
        }
    }



            // Class-level PID variables
            // --- Add these constants somewhere accessible ---
            const float RADAR_RANGE_METERS = 15000f; // How many meters the radar edge represents (adjust!)
            const float RADAR_BOX_SIZE_PX = 100f;   // Size of the radar square in pixels (adjust!)
            const float RADAR_BORDER_MARGIN = 10f;  // Margin from screen bottom-left (adjust!)

            private void DrawTopDownRadar(
                MySpriteDrawFrame frame,
                IMyCockpit cockpit,
                IMyTextSurface hud,
                Vector3D targetPosition, // Only need target position for this
                Color radarBgColor,
                Color radarBorderColor,
                Color playerColor,
                Color targetColor, Vector3D targetPosition2, Vector3D targetPosition3, Vector3D targetPosition4, Vector3D targetPosition5
            )
            {
                if (cockpit == null || hud == null) return; // Basic safety check

                Vector2 surfaceSize = hud.SurfaceSize;

                // --- 1. Define Radar Position and Dimensions ---
                // Positioned at bottom-left corner (adjust as needed)
                Vector2 radarOrigin = new Vector2(hud.SurfaceSize.X - hud.SurfaceSize.X * 0.2f -
                    RADAR_BORDER_MARGIN,
                    surfaceSize.Y - RADAR_BOX_SIZE_PX - RADAR_BORDER_MARGIN
                );
                Vector2 radarSize = new Vector2(RADAR_BOX_SIZE_PX, RADAR_BOX_SIZE_PX);
                Vector2 radarCenter = radarOrigin + radarSize / 2f;
                float radarRadius = RADAR_BOX_SIZE_PX / 2f;

                // Border (using helper function or 4 lines)
                DrawRectangleOutline(frame, radarOrigin.X- 5f, radarOrigin.Y-5f, radarSize.X+10f, radarSize.Y + 10f, 1f, radarBorderColor);


                // --- 3. Draw Player Icon (Upward Arrow at Center) ---
                var playerArrow = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = TEXTURE_TRIANGLE, // Assumes Triangle texture points 'up' by default
                    Position = radarCenter,
                    Size = new Vector2(radarRadius * 0.15f, radarRadius * 0.15f), // Adjust size as needed
                    Color = playerColor,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = 0 // Explicitly 0 rotation (points up screen = forward)
                };
                frame.Add(playerArrow);

                // --- 4. Calculate Target Position Relative to Player ---
                Vector3D shooterPosition = cockpit.GetPosition();
                Vector3D targetVectorWorld = targetPosition - shooterPosition;

                // --- NEW: Create a Yaw-Only Rotation Reference ---
                // Get gravity vector to define the 'Up' direction for a stable horizontal plane.
                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D worldUp;

                // If near zero gravity (in space), fallback might be needed.
                // Using the cockpit's current Up might work if not rolling heavily,
                // but gravity is preferred for a true top-down view relative to a planet/station.
                if (gravity.LengthSquared() < 0.01)
                {
                    // Fallback for space: Use the cockpit's current Up vector.
                    // Note: This radar view will rotate if the ship rolls.
                    worldUp = cockpit.WorldMatrix.Up;
                    // Alternative fallback: Assume world Y is up (Vector3D.Up) - might be inconsistent.
                    // worldUp = Vector3D.Up;
                }
                else
                {
                    // Use negative gravity direction as the world's 'Up'.
                    worldUp = Vector3D.Normalize(-gravity);
                }

                // Get the cockpit's forward direction
                Vector3D shipForward = cockpit.WorldMatrix.Forward;

                // Project the ship's forward vector onto the horizontal plane (defined by worldUp)
                // This gives the direction the ship is heading, ignoring pitch.
                Vector3D yawForward = Vector3D.Normalize(Vector3D.Reject(shipForward, worldUp));

                // Handle rare case where ship might be pointing exactly up or down
                if (!yawForward.IsValid() || yawForward.LengthSquared() < 0.1)
                {
                    // If pointing straight up/down, use ship's right vector projected instead,
                    // as forward projection is zero. Then derive forward from that right.
                    Vector3D shipRightProjected = Vector3D.Normalize(Vector3D.Reject(cockpit.WorldMatrix.Right, worldUp));
                    if (!shipRightProjected.IsValid() || shipRightProjected.LengthSquared() < 0.1)
                    {
                        // Extremely rare edge case (e.g., matrix invalid). Default to cockpit forward.
                        yawForward = shipForward;
                    }
                    else
                    {
                        yawForward = Vector3D.Cross(shipRightProjected, worldUp); // Calculate forward from horizontal right and up
                    }

                }


                // Calculate the horizontal 'Right' vector
                Vector3D yawRight = Vector3D.Cross(yawForward, worldUp); // Note: Cross order matters for RH coordinate system

                // Create a rotation matrix representing only the ship's yaw (horizontal heading)
                // We only need the inverse (Transpose) to go from World to this Yaw-Local space.
                MatrixD yawMatrix = MatrixD.Identity;
                yawMatrix.Forward = yawForward;
                yawMatrix.Right = yawRight;
                yawMatrix.Up = worldUp; // Use the consistent worldUp

                MatrixD worldToYawPlaneMatrix = MatrixD.Transpose(yawMatrix);
                Vector3D targetVectorWorld2 = targetPosition2 - shooterPosition;
                Vector3D targetVectorWorld3 = targetPosition3 - shooterPosition;
                Vector3D targetVectorWorld4 = targetPosition4 - shooterPosition;
                Vector3D targetVectorWorld5 = targetPosition5 - shooterPosition;

                // Transform the world target vector into this Yaw-Local space
                Vector3D targetVectorYawLocal = Vector3D.TransformNormal(targetVectorWorld, worldToYawPlaneMatrix);
                Vector3D targetVectorYawLocal2 = Vector3D.TransformNormal(targetVectorWorld2, worldToYawPlaneMatrix);
                Vector3D targetVectorYawLocal3 = Vector3D.TransformNormal(targetVectorWorld3, worldToYawPlaneMatrix);
                Vector3D targetVectorYawLocal4 = Vector3D.TransformNormal(targetVectorWorld4, worldToYawPlaneMatrix);
                Vector3D targetVectorYawLocal5 = Vector3D.TransformNormal(targetVectorWorld5, worldToYawPlaneMatrix);

                // --- 5. Map Yaw-Local Horizontal Coordinates to Radar Screen Coordinates ---
                // targetVectorYawLocal.X = Right/Left relative to HORIZONTAL heading
                // targetVectorYawLocal.Z = Forward/Backward relative to HORIZONTAL heading

                float pixelsPerMeter = radarRadius / RADAR_RANGE_METERS;

                // Calculate raw position on radar (relative to radar center)
                // Use the components from the Yaw-Local vector now.
                // Adjust the sign for Z based on previous testing (if necessary)
                Vector2 targetOffset = new Vector2(
                    (float)targetVectorYawLocal.X * pixelsPerMeter,
                    (float)targetVectorYawLocal.Z * pixelsPerMeter  // Use the Z component from the yaw-local vector
                                                                    // Keep the sign consistent with the fix from before.
                                                                    // If targets were front/back correct before, leave this sign.
                );
                Vector2 targetOffset2 = new Vector2(
    (float)targetVectorYawLocal2.X * pixelsPerMeter,
    (float)targetVectorYawLocal2.Z * pixelsPerMeter  // Use the Z component from the yaw-local vector
                                                    // Keep the sign consistent with the fix from before.
                                                    // If targets were front/back correct before, leave this sign.
); Vector2 targetOffset3 = new Vector2(
                    (float)targetVectorYawLocal3.X * pixelsPerMeter,
                    (float)targetVectorYawLocal3.Z * pixelsPerMeter  // Use the Z component from the yaw-local vector
                                                                    // Keep the sign consistent with the fix from before.
                                                                    // If targets were front/back correct before, leave this sign.
                ); Vector2 targetOffset4 = new Vector2(
                    (float)targetVectorYawLocal4.X * pixelsPerMeter,
                    (float)targetVectorYawLocal4.Z * pixelsPerMeter  // Use the Z component from the yaw-local vector
                                                                    // Keep the sign consistent with the fix from before.
                                                                    // If targets were front/back correct before, leave this sign.
                ); Vector2 targetOffset5 = new Vector2(
                    (float)targetVectorYawLocal5.X * pixelsPerMeter,
                    (float)targetVectorYawLocal5.Z * pixelsPerMeter  // Use the Z component from the yaw-local vector
                                                                    // Keep the sign consistent with the fix from before.
                                                                    // If targets were front/back correct before, leave this sign.
                );
                Vector2 targetRadarPos = radarCenter + targetOffset;
                Vector2 targetRadarPos2 = radarCenter + targetOffset2;
                Vector2 targetRadarPos3 = radarCenter + targetOffset3;
                Vector2 targetRadarPos4 = radarCenter + targetOffset4;
                Vector2 targetRadarPos5 = radarCenter + targetOffset5;

                // --- 6. Clamp Target Icon to Radar Edge if Outside Range ---
                // (Clamping logic remains the same, using the calculated targetOffset)
                float distFromCenter = targetOffset.Length();
                if (distFromCenter > radarRadius)
                {
                    // Handle division by zero if offset is zero length somehow
                    if (distFromCenter > 1e-6)
                    {
                        targetOffset /= distFromCenter; // Normalize (more efficient than Vector2.Normalize())
                    }
                    targetOffset *= radarRadius; // Scale to edge distance
                    targetRadarPos = radarCenter + targetOffset; // Clamp to edge
                }

                // --- 7. Draw Target Icon ---
                // Ensure target position is valid before drawing


                if (targetRadarPos2.IsValid())
                {
                    var targetIcon2 = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_SQUARE, // Simple circle for target
                        Position = targetRadarPos2,
                        Size = new Vector2(radarRadius * 0.1f, radarRadius * 0.1f), // Adjust size
                        Color = Color.DarkGreen,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(targetIcon2);
                }
                if (targetRadarPos3.IsValid())
                {
                    var targetIcon3 = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_SQUARE, // Simple circle for target
                        Position = targetRadarPos3,
                        Size = new Vector2(radarRadius * 0.1f, radarRadius * 0.1f), // Adjust size
                        Color = Color.DarkGreen,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(targetIcon3);
                }
                if (targetRadarPos4.IsValid())
                {
                    var targetIcon4 = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_SQUARE, // Simple circle for target
                        Position = targetRadarPos4,
                        Size = new Vector2(radarRadius * 0.1f, radarRadius * 0.1f), // Adjust size
                        Color = Color.DarkGreen,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(targetIcon4);
                }
                if (targetRadarPos5.IsValid())
                {
                    var targetIcon5 = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_SQUARE, // Simple circle for target
                        Position = targetRadarPos5,
                        Size = new Vector2(radarRadius * 0.1f, radarRadius * 0.1f), // Adjust size
                        Color = Color.DarkGreen,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(targetIcon5);
                }
                if (targetRadarPos.IsValid())
                {
                    var targetIcon = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_SQUARE, // Simple circle for target
                        Position = targetRadarPos,
                        Size = new Vector2(radarRadius * 0.1f, radarRadius * 0.1f), // Adjust size
                        Color = targetColor,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(targetIcon);
                }
            }


            private float integralError = 0f;
            private float previousError = 0f;
            const float PILOT_INPUT_DEADZONE = 0.001f;
            int pidResumeDelayCounter = 0; // Renamed for clarity
            const int MAX_PID_RESUME_DELAY = 60; // Reduced delay, adjust as needed (maybe even 0?)
            bool wasPilotInputActive = false; // Flag to track transition

            // PID constants (Consider loading these once in constructor or setup method)
            private float Kp = 1.2f;
            private float Ki = 0.0024f;
            private float Kd = 0.5f;

            // PID limits
            private const float MaxPIDOutput = 60f;
            private const float MaxAOA = 36f;

            // Constructor or Setup Method (Example - Call this once)
            // public Program() // If this is your main Program class
            // {
            //     LoadPIDConstants();
            // }

            // private void LoadPIDConstants()
            // {
            //      // Load Kp, Ki, Kd from CustomData here ONCE
            //      // Handle potential errors during parsing
            //      // Example (simplified):
            //      string[] lines = Me.CustomData.Split('\n');
            //      foreach (var line in lines) {
            //          if (line.StartsWith("KPI:")) {
            //              string[] parts = line.Split(':');
            //              if (parts.Length >= 4) {
            //                   float.TryParse(parts[1], out Kp);
            //                   float.TryParse(parts[2], out Ki);
            //                   float.TryParse(parts[3], out Kd);
            //                   // Add error handling/logging if parse fails
            //                   break; // Assuming only one KPI line
            //              }
            //          }
            //      }
            //      // Set default values if not found or parse failed
            // }


            private void AdjustStabilizers(double aoa, Jet myjet)
            {
                // Ensure cockpit is valid before accessing properties
                if (cockpit == null)
                {
                    // Handle the error appropriately, maybe disable stabilization
                    return;
                }

                Vector2 pitchyaw = cockpit.RotationIndicator;
                // Echo($"Pilot Input: {pitchyaw.X:F4}"); // Use $ for string interpolation, format output

                // Check if pilot pitch input exists:
                if (Math.Abs(pitchyaw.X) > PILOT_INPUT_DEADZONE)
                {
                    // Pilot is actively controlling pitch

                    // Increment delay counter, but maybe less aggressively?
                    pidResumeDelayCounter += 2; // Original logic had trimdelay++ twice
                    if (pidResumeDelayCounter > MAX_PID_RESUME_DELAY)
                    {
                        pidResumeDelayCounter = MAX_PID_RESUME_DELAY;
                    }

                    // // Option 1: Disable Trim Adjustments during pilot input (like original commented code)
                    // AdjustTrim(rightstab, 0);
                    // AdjustTrim(leftstab, 0);

                    // // Option 2: Freeze PID state (might be smoother than resetting)
                    // // Do nothing here to integralError / previousError

                    wasPilotInputActive = true; // Mark that pilot was active
                }
                else
                {
                    // Pilot is NOT actively controlling pitch

                    // If the pilot JUST stopped controlling, reset the PID state ONCE
                    if (wasPilotInputActive)
                    {
                        integralError = 0f;
                        // We don't necessarily need to reset previousError.
                        // The next PID calculation will establish a new derivative.
                        // Or, set previousError based on the current error *now*? Needs testing.
                        // previousError = MathHelper.Clamp((float)aoa, -MaxAOA, MaxAOA) + myjet.offset; // Option: Initialize previousError

                        wasPilotInputActive = false; // Reset the flag
                                                     // Consider if you want the delay to start counting down *only* after input stops
                                                     // pidResumeDelayCounter = MAX_PID_RESUME_DELAY; // Uncomment to *start* delay countdown now
                    }


                    // Wait for the delay countdown (if any)
                    if (pidResumeDelayCounter > 0) // Changed from > 1 to > 0
                    {
                        pidResumeDelayCounter--;
                        // Option: Still apply zero trim during delay?
                        // AdjustTrim(rightstab, 0);
                        // AdjustTrim(leftstab, 0);
                        return; // Wait until delay finishes
                    }

                    // Delay is over, engage PID trim
                    float targetAoa = MathHelper.Clamp((float)aoa, -MaxAOA, MaxAOA) + myjet.offset;
                    float pidOutput = PIDController(targetAoa); // Pass the target AOA (which is current error if setpoint is 0)

                    // Ensure previousError is initialized correctly on the very first run after reset
                    if (previousError == 0 && integralError == 0)
                    {
                        previousError = targetAoa; // Initialize previousError for first derivative calculation
                    }


                    AdjustTrim(rightstab, pidOutput);
                    AdjustTrim(leftstab, -pidOutput);
                }
            }

            private float PIDController(float currentError)
            {
                // *** IMPORTANT: Load Kp, Ki, Kd ONCE in a setup method/constructor ***
                // Reading from CustomData every tick is very inefficient.
                // Assume Kp, Ki, Kd are already loaded into class variables here.

                // Integral term calculation
                integralError += currentError;
                integralError = MathHelper.Clamp(integralError, -200, 200); // Keep clamping

                // Derivative term calculation
                float derivative = currentError - previousError;

                // PID output calculation
                float proportionalTerm = Kp * currentError;
                float integralTerm = Ki * integralError;
                float derivativeTerm = Kd * derivative;
                float pidOutput = proportionalTerm + integralTerm + derivativeTerm;

                // Clamp PID output
                pidOutput = MathHelper.Clamp(pidOutput, -MaxPIDOutput, MaxPIDOutput);

                // Store current error for next derivative calculation
                previousError = currentError;

                // Optional: Log PID terms for debugging
                // Echo($" P: {proportionalTerm:F2}, I: {integralTerm:F2}, D: {derivativeTerm:F2}");

                return pidOutput;
            }



            private void AdjustTrim(IEnumerable<IMyTerminalBlock> stabilizers, float desiredTrim)
            {
                foreach (var item in stabilizers)
                {
                    currentTrim = item.GetValueFloat("Trim");

                    item.SetValue("Trim", desiredTrim);
                }
            }

            public override void Tick()
            {
                // Return early if cockpit or HUD is missing
                if (cockpit == null || hud == null)
                    return;
                totalElapsedTime += ParentProgram.Runtime.TimeSinceLastRun;
                // Return early if cockpit or HUD is not functional
                if (!cockpit.IsFunctional || !hudBlock.IsFunctional)
                    return;

                // --------------------------------------------------------------------------
                // 1. Retrieve world matrix and direction vectors
                // --------------------------------------------------------------------------
                MatrixD worldMatrix = cockpit.WorldMatrix;
                Vector3D forwardVector = worldMatrix.Forward;
                Vector3D upVector = worldMatrix.Up;
                Vector3D leftVector = worldMatrix.Left;

                // --------------------------------------------------------------------------
                // 2. Gravity checks
                // --------------------------------------------------------------------------
                Vector3D gravity = cockpit.GetNaturalGravity();
                bool inGravity = gravity.LengthSquared() > 0;
                Vector3D gravityDirection = inGravity ? Vector3D.Normalize(gravity) : Vector3D.Zero;

                // --------------------------------------------------------------------------
                // 3. Pitch and roll calculations (only if in gravity)
                // --------------------------------------------------------------------------

                if (inGravity)
                {
                    pitch =
                        Math.Asin(Vector3D.Dot(forwardVector, gravityDirection)) * (180 / Math.PI);
                    roll =
                        Math.Atan2(
                            Vector3D.Dot(leftVector, gravityDirection),
                            Vector3D.Dot(upVector, gravityDirection)
                        ) * (180 / Math.PI);

                    // Normalize roll to [0, 360)
                    if (roll < 0)
                        roll += 360;
                }

                // --------------------------------------------------------------------------
                // 4. Basic speed and Mach calculation
                // --------------------------------------------------------------------------
                velocity = cockpit.GetShipSpeed();
                mach = velocity / speedOfSound;

                // --------------------------------------------------------------------------
                // 5. Acceleration and G-force calculations
                // --------------------------------------------------------------------------
                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;
                deltaTime = ParentProgram.Runtime.TimeSinceLastRun.TotalSeconds;

                // Fallback to ~1/60th of a second if deltaTime is not valid
                if (deltaTime <= 0)
                    deltaTime = 0.0167;

                Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                double gForces = acceleration.Length() / 9.81;
                previousVelocity = currentVelocity;

                // Track peak G-forces
                if (gForces > peakGForce)
                    peakGForce = gForces;

                // --------------------------------------------------------------------------
                // 6. Heading, altitude, angle of attack
                // --------------------------------------------------------------------------
                double heading = CalculateHeading();
                double altitude = GetAltitude();
                double aoa = CalculateAngleOfAttack(
                    cockpit.WorldMatrix.Forward,
                    cockpit.GetShipVelocities().LinearVelocity,
                    upVector
                );

                // --------------------------------------------------------------------------
                // 7. Throttle and other final values
                // --------------------------------------------------------------------------
                double throttle = cockpit.MoveIndicator.Z * -1; // Assuming Z-axis throttle control
                double jumpthrottle = cockpit.MoveIndicator.Y; // Assuming Y-axis for jump throttle
                double velocityKPH = velocity * 3.6; // Convert m/s to KPH

                if (throttle == 1f)
                {
                    throttlecontrol += 0.01f;
                    if (throttlecontrol > 1f)
                        throttlecontrol = 1f;
                    if (throttlecontrol >= 0.8f)
                    {
                        if (!hydrogenswitch)
                        {
                            for (int i = 0; i < tanks.Count; i++)
                            {
                                tanks[i].Enabled = true;
                            }
                            hydrogenswitch = true;
                        }
                    }
                }
                if (throttle == -1f)
                {
                    throttlecontrol -= 0.01f;
                    if (throttlecontrol < 0f)
                        throttlecontrol = 0f;
                    if (throttlecontrol < 0.8f)
                    {
                        if (hydrogenswitch)
                        {
                            for (int i = 0; i < tanks.Count; i++)
                            {
                                tanks[i].Enabled = false;
                            }
                            hydrogenswitch = false;
                        }
                    }
                }

                if (jumpthrottle == 1f)
                {
                    for (int i = 0; i < airbreaks.Count; i++)
                    {
                        airbreaks[i].OpenDoor();
                    }
                }
                if (jumpthrottle == 0f)
                {
                    for (int i = 0; i < airbreaks.Count; i++)
                    {
                        airbreaks[i].CloseDoor();
                    }
                }
                if(jumpthrottle < -0.01f)
                {
                    myjet.manualfire = !myjet.manualfire;
                }
                if(myjet.manualfire)
                {
                    for (int i = 0; i < myjet._gatlings.Count; i++)
                    {
                        myjet._gatlings[i].Enabled = true;
                    }
                }
                for (int i = 0; i < thrusters.Count; i++)
                {
                    float scaledThrottle;

                    if (throttlecontrol <= 0.8f)
                    {
                        // Scale throttlecontrol to fit the range 0.0 to 1.0
                        scaledThrottle = throttlecontrol / 0.8f;
                    }
                    else
                    {
                        // Cap it at 1.0 when throttlecontrol is over 0.8
                        scaledThrottle = 1.0f;
                    }

                    thrusters[i].ThrustOverridePercentage = scaledThrottle;
                }

                throttle = throttlecontrol;

                UpdateSmoothedValues(velocityKPH, altitude, gForces, aoa, throttle);
                AdjustStabilizers(aoa, myjet);

                float centerX = hud.SurfaceSize.X / 2;
                float centerY = hud.SurfaceSize.Y / 2;
                float pixelsPerDegree = hud.SurfaceSize.Y / 16f; // F18-like scaling

                using (var frame = hud.DrawFrame())
                {
                    DrawArtificialHorizon(
                        frame,
                        (float)pitch,
                        (float)roll,
                        centerX,
                        centerY,
                        pixelsPerDegree
                    );
                    DrawFlightPathMarker(
                        frame,
                        currentVelocity,
                        worldMatrix,
                        roll,
                        centerX,
                        centerY,
                        pixelsPerDegree
                    );
                    DrawLeftInfoBox(
                        frame,
                        smoothedVelocity,
                        centerX+30f,
                        centerY + centerY * 1.85f,
                        pixelsPerDegree,
                        new LabelValue("T", myjet.offset)                        
                    );
                    DrawFlightInfo(
                        frame,
                        smoothedVelocity,
                        smoothedGForces,
                        heading,
                        smoothedAltitude,
                        smoothedAoA,
                        smoothedThrottle,
                        mach
                    );
                    DrawSpeedIndicatorF18StyleKph(frame, velocityKPH);
                    //DrawArtificialHorizon(frame, (float)pitch, (float)roll);
                    DrawRadar(frame, myjet,centerX - centerX * 0.70f,
                        centerY + centerY * 0.75f, 70, 30,
                        pixelsPerDegree);
                    DrawCompass(frame, heading);
                    DrawAltitudeIndicatorF18Style(frame, smoothedAltitude, totalElapsedTime);
                    //DrawAOABracket(frame, aoa);
                    //DrawTrim(frame, aoa);
                    //DrawBomb(frame, aoa);
                    DrawGForceIndicator(frame, gForces, peakGForce);
                    var cachedData = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(line => line.StartsWith("Cached:GPS:"));
                    var cachedData2 = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(line => line.StartsWith("CacheGPS0:GPS:"));
                    var cachedData3 = ParentProgram.Me.CustomData
    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
    .FirstOrDefault(line => line.StartsWith("CacheGPS1:GPS:"));
                    var cachedData4 = ParentProgram.Me.CustomData
    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
    .FirstOrDefault(line => line.StartsWith("CacheGPS2:GPS:"));
                    var cachedData5 = ParentProgram.Me.CustomData
    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
    .FirstOrDefault(line => line.StartsWith("CacheGPS3:GPS:"));
                    if (cachedData == null)
                    {
                        return;
                    }
                    var parts = cachedData.Split(':');
                    var parts2 = cachedData2.Split(':');
                    var parts3 = cachedData3.Split(':');
                    var parts4 = cachedData4.Split(':');
                    var parts5 = cachedData5.Split(':');

                    if (parts.Length < 6)
                    {
                        return;
                    }
                    double tarx,
                        tary,
                        tarz;
                    double tarx2,
    tary2,
    tarz2;
                    double tarx3,
    tary3,
    tarz3;
                    double tarx4,
    tary4,
    tarz4;
                    double tarx5,
    tary5,
    tarz5;
                    if (
                        !double.TryParse(parts[3], out tarx)
                        || !double.TryParse(parts[4], out tary)
                        || !double.TryParse(parts[5], out tarz)
                    )
                    {
                        return;
                    }
                    if (
    !double.TryParse(parts2[3], out tarx2)
    || !double.TryParse(parts2[4], out tary2)
    || !double.TryParse(parts2[5], out tarz2)
)
                    {
                        return;
                    }
                    if (
    !double.TryParse(parts3[3], out tarx3)
    || !double.TryParse(parts3[4], out tary3)
    || !double.TryParse(parts3[5], out tarz3)
)
                    {
                        return;
                    }
                    if (
    !double.TryParse(parts4[3], out tarx4)
    || !double.TryParse(parts4[4], out tary4)
    || !double.TryParse(parts4[5], out tarz4)
)
                    {
                        return;
                    }
                    if (
    !double.TryParse(parts5[3], out tarx5)
    || !double.TryParse(parts5[4], out tary5)
    || !double.TryParse(parts5[5], out tarz5)
)
                    {
                        return;
                    }
                    var speecachedData = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(line => line.StartsWith("CachedSpeed:"));
                    if (speecachedData == null)
                    {
                        return;
                    }
                    var speeparts = speecachedData.Split(':');
                    double speex,
                        speey,
                        speez;
                    if (
                        !double.TryParse(speeparts[1], out speex)
                        || !double.TryParse(speeparts[2], out speey)
                        || !double.TryParse(speeparts[3], out speez)
                    )
                    {
                        return;
                    }
                    Vector3D targetPosition = new Vector3D(tarx, tary, tarz); // Replace with actual target GPS
                    Vector3D targetPosition2 = new Vector3D(tarx2, tary2, tarz2); // Replace with actual target GPS

                    Vector3D targetPosition3 = new Vector3D(tarx3, tary3, tarz3); // Replace with actual target GPS

                    Vector3D targetPosition4 = new Vector3D(tarx4, tary4, tarz4); // Replace with actual target GPS

                    Vector3D targetPosition5 = new Vector3D(tarx5, tary5, tarz5); // Replace with actual target GPS

                    Vector3D targetVelocity = new Vector3D(speex, speey, speez); // Replace with actual target speed
                    Vector3D shooterPosition = cockpit.GetPosition(); // Your ship's current position
                    double muzzleVelocity = 910; // Muzzle velocity of your weapon in m/s
                    // Compute the projectile's initial velocity
                    Vector3D shooterForwardDirection = cockpit.WorldMatrix.Forward;
                    Vector3D projectileInitialVelocity =
                        currentVelocity + muzzleVelocity * shooterForwardDirection;
                    DrawTopDownRadar(frame, cockpit, hud, targetPosition, Color.White, Color.Lime, Color.Yellow, Color.Red, targetPosition2, targetPosition3, targetPosition4, targetPosition5);

                    // Call the DrawLeadingPip function
                    DrawLeadingPip(
                        frame,cockpit, hud,
                        targetPosition,
                        targetVelocity,
                        shooterPosition,
                        currentVelocity, 
                        muzzleVelocity,
                        gravityDirection, Color.Red, Color.Yellow, Color.HotPink, Color.White
                    );
       
                }
            }
            private void DrawGForceIndicator(
                MySpriteDrawFrame frame,
                double gForces,
                double peakGForce
            )
            {
                float padding = 10f;
                float textScale = 0.8f;

                string gForceText = $"G: {gForces:F1}";
                var gForceSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = gForceText,
                    Position = new Vector2(padding, hud.SurfaceSize.Y - padding - 20f),
                    RotationOrScale = textScale,
                    Color = Color.Lime,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                };
                frame.Add(gForceSprite);

                // Peak G-force
                string peakGText = $"Max G: {peakGForce:F1}";
                var peakGSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = peakGText,
                    Position = new Vector2(padding, hud.SurfaceSize.Y - padding - 40f),
                    RotationOrScale = textScale,
                    Color = Color.Lime,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                };
                frame.Add(peakGSprite);
            }
            public struct LabelValue
            {
                public string Label;
                public double Value;

                public LabelValue(string label, double value)
                {
                    Label = label;
                    Value = value;
                }
            }
            private void DrawLeftInfoBox(
                MySpriteDrawFrame frame,
                double airspeed,
                float centerX,
                float centerY,
                double pixelsPerDegree,
                params LabelValue[] extraValues
            )
            {
                float yOffsetPerValue = 30f;
                float xoffset = centerX - centerX * 0.75f;
                float yoffset = centerY - centerY * 0.5f;
                // 2) Render each label+value line, padding the label so that the numbers line up
                // 1) Find the longest label, to align all numeric columns
                // Decide on fixed offsets for your two “columns”
                float labelColumnX = xoffset - 40f; // try adjusting
                float numberColumnX = xoffset + 40f; // try adjusting

                for (int i = 0; i < extraValues.Length; i++)
                {
                    string labelText = extraValues[i].Label;
                    double numericValue = extraValues[i].Value;

                    // 1) Draw the label at labelColumnX, left-aligned
                    var labelSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = labelText,
                        Position = new Vector2(labelColumnX, yoffset + i * yOffsetPerValue),
                        RotationOrScale = 0.75f,
                        Color = Color.Lime,
                        Alignment = TextAlignment.LEFT,
                        FontId = "White"
                    };
                    frame.Add(labelSprite);

                    // 2) Draw the numeric value at numberColumnX
                    //    You can use LEFT or RIGHT alignment.
                    //    If you choose RIGHT alignment, the text anchor is at that X,
                    //    and the characters extend to the left of that point.
                    var valueSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = numericValue.ToString("F1"),
                        Position = new Vector2(numberColumnX, yoffset + i * yOffsetPerValue),
                        RotationOrScale = 0.75f,
                        Color = Color.Lime,
                        Alignment = TextAlignment.RIGHT, // or LEFT if you prefer
                        FontId = "White"
                    };
                    frame.Add(valueSprite);
                }
            }
            // --- Define a simple struct to hold Timestamp and Altitude ---
            struct AltitudeTimePoint
            {
                public readonly TimeSpan Time;
                public readonly double Altitude;

                public AltitudeTimePoint(TimeSpan time, double altitude)
                {
                    Time = time;
                    Altitude = altitude;
                }
            }
            private void UpdateAltitudeHistory(double currentAltitude, TimeSpan currentTime)
            {
                // Add current altitude and time using our struct
                altitudeHistory.Enqueue(new AltitudeTimePoint(currentTime, currentAltitude));
                // Remove old entries
                // Use Peek() without Dequeue first to check the condition
                while (altitudeHistory.Count > 1 && currentTime - altitudeHistory.Peek().Time > historyDuration)
                {
                    altitudeHistory.Dequeue();
                }
            }

            private double CalculateVerticalVelocity(double currentAltitude, TimeSpan currentTime)
            {
                // Ensure queue has enough items AND is not empty before peeking
                if (altitudeHistory.Count < 2)
                {
                    return 0; // Not enough data
                }

                // Peek retrieves an AltitudeTimePoint
                AltitudeTimePoint oldestData = altitudeHistory.Peek(); // oldestData is type AltitudeTimePoint

                // Access data using struct properties .Time and .Altitude
                TimeSpan oldestTime = oldestData.Time;         // Correctly accessing .Time
                double oldestAltitude = oldestData.Altitude;   // Correctly accessing .Altitude

                TimeSpan timeDifference = currentTime - oldestTime;
                if (timeDifference.TotalSeconds < 0.01)
                {
                    return 0;
                }

                double altitudeChange = currentAltitude - oldestAltitude;
                double vvi = altitudeChange / timeDifference.TotalSeconds;

                return vvi;
            }
            private TimeSpan historyDuration = TimeSpan.FromSeconds(1); // How far back to calculate VVI from
             private const float TAPE_HEIGHT_PIXELS = 200f; // Total visible height of the tape
             private const float ALTITUDE_UNITS_PER_TAPE_HEIGHT = 1000f; // How many altitude units the full tape height represents
             private const float PIXELS_PER_ALTITUDE_UNIT = TAPE_HEIGHT_PIXELS / ALTITUDE_UNITS_PER_TAPE_HEIGHT;
             private const float TICK_INTERVAL = 100f; // Draw a tick mark every 100 altitude units
             private const float MAJOR_TICK_INTERVAL = 500f; // Draw a number every 500 altitude units
             private Color hudColor = Color.Lime; // Standard HUD color
             private string FONT = "Monospace"; // Use a monospaced font for alignment

            private void DrawAltitudeIndicatorF18Style(MySpriteDrawFrame frame, double currentAltitude, TimeSpan currentTime)
            {
                // --- 1. Update History and Calculate VVI ---
                UpdateAltitudeHistory(currentAltitude, currentTime); // Make sure this is called
                double verticalVelocity = CalculateVerticalVelocity(currentAltitude, currentTime);

                // --- 2. Define Positions & Sizes ---
                float screenWidth = hud.SurfaceSize.X;
                float screenHeight = hud.SurfaceSize.Y;
                float centerY = screenHeight / 2f;

                // Right side positioning (adjust margin as needed)
                float tapeRightMargin = 10f;
                float tapeNumberMargin = 10f; // Space between tape line and numbers
                float tapeWidth = 2f; // Width of the main tape line
                float tickLength = 10f; // Length of the tick marks
                float majorTickLength = 15f; // Length of major tick marks

                float tapeLineX = screenWidth - tapeRightMargin; // X position of the main vertical tape line
                float digitalAltBoxWidth = 80f;
                float digitalAltBoxHeight = 30f;
                float digitalAltBoxX = tapeLineX - tapeNumberMargin - digitalAltBoxWidth; // Box left of the tape line
                float vviTextYOffset = digitalAltBoxHeight * 0.6f; // Place VVI below the altitude box

                // --- 3. Draw Altitude Tape ---
                // Draw main vertical line
                var tapeLine = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple", // Use a simple square texture stretched into a line
                    Position = new Vector2(tapeLineX, centerY),
                    Size = new Vector2(tapeWidth, TAPE_HEIGHT_PIXELS),
                    Color = hudColor,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(tapeLine);

                // Draw Ticks and Numbers
                float tapeTopAlt = (float)currentAltitude + (ALTITUDE_UNITS_PER_TAPE_HEIGHT / 2f);
                float tapeBottomAlt = (float)currentAltitude - (ALTITUDE_UNITS_PER_TAPE_HEIGHT / 2f);

                // Determine the first tick mark value to draw above the bottom
                // Corrected Ceiling calculation for negative altitudes too
                float startTickAlt = (float)(Math.Floor(tapeBottomAlt / TICK_INTERVAL) * TICK_INTERVAL);
                if (startTickAlt < tapeBottomAlt) // Ensure we start at or above the bottom
                    startTickAlt += TICK_INTERVAL;


                for (float altMark = startTickAlt; altMark <= tapeTopAlt + (TICK_INTERVAL * 0.5f); altMark += TICK_INTERVAL) // Add half interval to ensure top tick draws
                {
                    // Prevent potential infinite loops with float inaccuracies if TICK_INTERVAL is 0 or negative
                    if (TICK_INTERVAL <= 0) break;

                    // Calculate Y position relative to the center based on altitude difference
                    float yOffset = (float)(currentAltitude - altMark) * PIXELS_PER_ALTITUDE_UNIT;
                    float yPos = centerY + yOffset;

                    // Check if the tick is within the visible tape area (add small buffer for half line widths)
                    float tapeTopY = centerY - TAPE_HEIGHT_PIXELS / 2f;
                    float tapeBottomY = centerY + TAPE_HEIGHT_PIXELS / 2f;

                    if (yPos >= tapeTopY - 1f && yPos <= tapeBottomY + 1f)
                    {
                        bool isMajorTick = Math.Abs(altMark % MAJOR_TICK_INTERVAL) < (TICK_INTERVAL * 0.1f); // Tolerance relative to tick interval
                        float currentTickLength = isMajorTick ? majorTickLength : tickLength;
                        if(altMark >= 0)
                        {
                            // Draw Tick Mark (line pointing left from the tape line)
                            var tickMark = new MySprite()
                            {
                                Type = SpriteType.TEXTURE,
                                Data = "SquareSimple",
                                Position = new Vector2(tapeLineX - currentTickLength / 2f, yPos),
                                Size = new Vector2(currentTickLength, tapeWidth), // Thin horizontal line
                                Color = hudColor,
                                Alignment = TextAlignment.CENTER // Align to its center
                            };
                            // Clamp tick position to tape bounds vertically if needed (optional aesthetic)
                            // tickMark.Position = new Vector2(tickMark.Position.Value.X, MathHelper.Clamp(yPos, tapeTopY, tapeBottomY));
                            frame.Add(tickMark);
                        }


                        // Draw Number for Major Ticks
                        if (isMajorTick)
                        {
                            string altText = altMark.ToString("F0"); // Format altitude number
                            var numberLabel = new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = altText,
                                Position = new Vector2(tapeLineX - currentTickLength - tapeNumberMargin, yPos - 7.5f), // Position left of the tick
                                RotationOrScale = 0.5f, // Slightly smaller font for tape numbers
                                Color = hudColor,
                                Alignment = TextAlignment.RIGHT, // Align text to the right, so it ends before the margin
                                FontId = FONT
                            };
                            // Clamp number position too if desired
                            // numberLabel.Position = new Vector2(numberLabel.Position.Value.X, MathHelper.Clamp(yPos, tapeTopY, tapeBottomY));
                            frame.Add(numberLabel);
                        }
                    }
                }

                // --- 4. Draw Digital Altitude Readout ---
                // Box (optional, but common)
                // Draw outline using lines (example)
                DrawRectangleOutline(frame, digitalAltBoxX - 20, centerY - digitalAltBoxHeight - 225 / 2f, digitalAltBoxWidth, digitalAltBoxHeight, 1f, hudColor);


                // Altitude Text
                string currentAltitudeText = currentAltitude.ToString("F0");
                var altitudeLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = currentAltitudeText,
                    Position = new Vector2(digitalAltBoxX - 20 + digitalAltBoxWidth / 2f, centerY  - 140), // Centered in the box area
                    RotationOrScale = 0.8f, // Main font size
                    Color = hudColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = FONT
                };
                frame.Add(altitudeLabel);

                // --- 5. Draw Caret ---
                var caret = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "<", // Caret pointing from the box to the tape
                    Position = new Vector2(digitalAltBoxX + digitalAltBoxWidth + 15f, centerY - 7.5f), // Just right of the box
                    RotationOrScale = 0.5f,
                    Color = hudColor,
                    Alignment = TextAlignment.LEFT, // Align left so it starts right next to the box
                    FontId = FONT
                };
                frame.Add(caret);

                
            }
            const float SPEED_MAJOR_TICK_INTERVAL = 50f;   // KPH between major ticks and numbers (e.g., every 100 KPH)
            const float SPEED_TICK_INTERVAL = 25f;          // KPH between minor ticks (e.g., every 20 KPH)
            const float SPEED_KPH_UNITS_PER_TAPE_HEIGHT = 600f; // How many KPH the full tape height represents (e.g., 500 KPH)
            private void DrawSpeedIndicatorF18StyleKph(MySpriteDrawFrame frame, double currentSpeedKph)
            {
                // --- 1. Ensure Speed is Non-Negative (Optional) ---
                currentSpeedKph = Math.Max(0, currentSpeedKph); // Speed is typically non-negative
                const float PIXELS_PER_SPEED_UNIT = 800 / SPEED_KPH_UNITS_PER_TAPE_HEIGHT; // Pixels per KPH
                // --- 2. Define Positions & Sizes (Mirrored to Left) ---
                float screenWidth = hud.SurfaceSize.X; // Assuming 'hud' is your IMyTextSurface
                float screenHeight = hud.SurfaceSize.Y;
                float centerY = screenHeight / 2.25f;

                // Left side positioning
                float tapeLeftMargin = 10f;          // Margin from the left edge
                float tapeNumberMargin = 10f;      // Space between tape line and numbers
                float tapeWidth = 2f;              // Width of the main tape line
                float tickLength = 10f;            // Length of the minor tick marks
                float majorTickLength = 15f;       // Length of major tick marks

                float tapeLineX = tapeLeftMargin;     // X position of the main vertical tape line (at the left margin)
                float digitalSpeedBoxWidth = 80f;
                float digitalSpeedBoxHeight = 30f;
                // Position box to the right of the tape line
                float digitalSpeedBoxX = tapeLineX + tapeNumberMargin;

                // --- 3. Draw Speed Tape ---
                // Draw main vertical line
                var tapeLine = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(tapeLineX, centerY),
                    Size = new Vector2(tapeWidth, TAPE_HEIGHT_PIXELS),
                    Color = hudColor,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(tapeLine);

                // Draw Ticks and Numbers
                float tapeTopSpeed = (float)currentSpeedKph + (SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f);
                float tapeBottomSpeed = (float)currentSpeedKph - (SPEED_KPH_UNITS_PER_TAPE_HEIGHT / 2f);
                tapeBottomSpeed = Math.Max(0, tapeBottomSpeed); // Don't let tape go below 0 KPH visually

                // Determine the first tick mark value to draw above the bottom
                float startTickSpeed = (float)(Math.Floor(tapeBottomSpeed / SPEED_TICK_INTERVAL) * SPEED_TICK_INTERVAL);
                if (startTickSpeed < tapeBottomSpeed) // Ensure we start at or above the bottom
                    startTickSpeed += SPEED_TICK_INTERVAL;
                startTickSpeed = Math.Max(0, startTickSpeed); // Start drawing from 0 KPH if necessary


                for (float speedMark = startTickSpeed; speedMark <= tapeTopSpeed + (SPEED_TICK_INTERVAL * 0.5f); speedMark += SPEED_TICK_INTERVAL)
                {
                    // Prevent potential infinite loops with float inaccuracies if interval is 0 or negative
                    if (SPEED_TICK_INTERVAL <= 0) break;
                    // Don't draw negative speed marks explicitly (already handled by starting at >= 0)
                    if (speedMark < 0) continue;

                    // Calculate Y position relative to the center based on speed difference
                    float yOffset = (float)(currentSpeedKph - speedMark) * PIXELS_PER_SPEED_UNIT;
                    float yPos = centerY + yOffset;

                    // Check if the tick is within the visible tape area (add small buffer)
                    float tapeTopY = centerY - TAPE_HEIGHT_PIXELS / 2f;
                    float tapeBottomY = centerY + TAPE_HEIGHT_PIXELS / 2f;

                    if (yPos >= tapeTopY - 1f && yPos <= tapeBottomY + 1f)
                    {
                        // Use tolerance for floating point comparison for major ticks
                        bool isMajorTick = Math.Abs(speedMark % SPEED_MAJOR_TICK_INTERVAL) < (SPEED_TICK_INTERVAL * 0.1f);
                        // Treat 0 KPH as a major tick if it's not already covered
                        if (Math.Abs(speedMark) < (SPEED_TICK_INTERVAL * 0.1f)) isMajorTick = true;

                        float currentTickLength = isMajorTick ? majorTickLength : tickLength;

                        // Draw Tick Mark (line pointing right from the tape line)
                        var tickMark = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            // Position its center at (tapeLineX + half its length, yPos)
                            Position = new Vector2(tapeLineX + currentTickLength / 2f, yPos),
                            Size = new Vector2(currentTickLength, tapeWidth), // Thin horizontal line
                            Color = hudColor,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(tickMark);

                        // Draw Number for Major Ticks
                        if (isMajorTick)
                        {
                            string speedText = speedMark.ToString("F0"); // Format speed number (integer KPH)
                            var numberLabel = new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = speedText,
                                // Position right of the tick mark
                                Position = new Vector2(tapeLineX + currentTickLength + tapeNumberMargin, yPos - 7.5f), // Adjust Y offset for vertical centering
                                RotationOrScale = 0.5f, // Font size for tape numbers
                                Color = hudColor,
                                Alignment = TextAlignment.LEFT, // Align text to the left, starting after the margin
                                FontId = FONT
                            };
                            frame.Add(numberLabel);
                        }
                    }
                }

                // --- 4. Draw Digital Speed Readout ---
                // Box (optional) - Positioned to the right of the tape
                // Draw outline using lines or a box sprite
                DrawRectangleOutline(frame, digitalSpeedBoxX, centerY - digitalSpeedBoxHeight / 2f - 130, digitalSpeedBoxWidth, digitalSpeedBoxHeight, 1f, hudColor); // Adjust Y offset as needed

                // Speed Text
                string currentSpeedText = currentSpeedKph.ToString("F0"); // Integer KPH
                var speedLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = currentSpeedText,
                    // Centered within the conceptual box area
                    Position = new Vector2(digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130 - digitalSpeedBoxHeight / 2f ), // Adjust Y pos to be centered in the drawn box
                    RotationOrScale = 0.8f, // Main font size
                    Color = hudColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = FONT
                };
                frame.Add(speedLabel);

                // --- 5. Draw Caret ---
                // Caret pointing from the box towards the tape line on the left
                var caret = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = ">", // Caret points towards the tape
                                // Position just left of the digital readout box, aligned vertically with the center
                    Position = new Vector2(digitalSpeedBoxX - 10f, centerY - 7.5f), // Adjust X offset as needed
                    RotationOrScale = 0.5f,
                    Color = hudColor,
                    Alignment = TextAlignment.RIGHT, // Align right so it ends just before the box
                    FontId = FONT
                };
                frame.Add(caret);
            }




            // Helper function to draw a rectangle outline using lines (if needed)
            private void DrawRectangleOutline(MySpriteDrawFrame frame, float x, float y, float width, float height, float lineWidth, Color color)
            {
                // Top line
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x + width / 2f, y), Size = new Vector2(width, lineWidth), Color = color, Alignment = TextAlignment.CENTER });
                // Bottom line
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x + width / 2f, y + height), Size = new Vector2(width, lineWidth), Color = color, Alignment = TextAlignment.CENTER });
                // Left line
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x, y + height / 2f), Size = new Vector2(lineWidth, height), Color = color, Alignment = TextAlignment.CENTER });
                // Right line
                frame.Add(new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x + width, y + height / 2f), Size = new Vector2(lineWidth, height), Color = color, Alignment = TextAlignment.CENTER });
            }
            private void DrawAltitudeIndicator(MySpriteDrawFrame frame, double currentAltitude)
            {
                // Calculate total altitude change over the tick window
                double totalAltitudeChange = 0;
                if (altitudeHistory.Count > 1)
                {
                    double oldestAltitude = altitudeHistory.Peek().Altitude;
                    totalAltitudeChange = currentAltitude - oldestAltitude;
                }

                // Display on HUD
                float scaleX = hud.SurfaceSize.X - hud.SurfaceSize.X * 0.75f;
                float centerY = hud.SurfaceSize.Y / 2;

                // Draw current altitude
                string altitudeText = currentAltitude.ToString("F0");
                var altitudeLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = altitudeText,
                    Position = new Vector2(scaleX, centerY),
                    RotationOrScale = 1f,
                    Color = Color.Lime,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                };
                frame.Add(altitudeLabel);
                if (totalAltitudeChange < 0.1 && totalAltitudeChange > -0.1)
                {
                    return;
                }
                // Draw total altitude change
                string altitudeChangeText =
                    (totalAltitudeChange >= 0 ? "+" : "") + totalAltitudeChange.ToString("F1");
                var altitudeChangeLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = altitudeChangeText,
                    Position = new Vector2(scaleX + 50f, centerY), // Offset to the right
                    RotationOrScale = 0.8f, // Smaller font size
                    Color = totalAltitudeChange >= 0 ? Color.Green : Color.Red,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                };
                frame.Add(altitudeChangeLabel);
            }

            private void UpdateSmoothedValues(
                double velocityKnots,
                double altitude,
                double gForces,
                double aoa,
                double throttle
            )
            {
                velocityHistory.Enqueue(velocityKnots);
                if (velocityHistory.Count > smoothingWindowSize)
                {
                    velocityHistory.Dequeue();
                }
                smoothedVelocity = velocityHistory.Average();
                altitudeHistory.Enqueue(new AltitudeTimePoint(totalElapsedTime, altitude));
                if (altitudeHistory.Count > smoothingWindowSize)
                {
                    altitudeHistory.Dequeue();
                }
                if (altitudeHistory.Any()) // Or altitudeHistory.Count > 0
                {
                    smoothedAltitude = altitudeHistory.Average(point => point.Altitude);
                }
                else
                {
                    // Handle the case where history is empty, perhaps use the current raw altitude
                    // smoothedAltitude = currentAltitude; // Or 0, or some default
                }
                gForcesHistory.Enqueue(gForces);
                if (gForcesHistory.Count > smoothingWindowSize)
                {
                    gForcesHistory.Dequeue();
                }
                smoothedGForces = gForcesHistory.Average() + 1;

                aoaHistory.Enqueue(aoa);
                if (aoaHistory.Count > smoothingWindowSize)
                {
                    aoaHistory.Dequeue();
                }
                smoothedAoA = aoaHistory.Average();

                smoothedThrottle = throttle * 100; // Convert to percentage
            }

            private void DrawFlightInfo(
                MySpriteDrawFrame frame,
                double velocityKPH,
                double gForces,
                double heading,
                double altitude,
                double aoa,
                double throttle,
                double mach
            )
            {
                float padding = 0f;
                float infoX = hud.SurfaceSize.X - hud.SurfaceSize.X / 2;
                float infoY = 60f;
                float boxPadding = 5f; // Padding inside the box
                float textHeight = 30f; // Distance between lines

                var surface = hud; // Assuming `hud` is your surface
                var textScale = 0.75f;

                // Measure the longest text to determine maxWidth
                Vector2 maxTextSize = surface.MeasureStringInPixels(
                    new StringBuilder("SPD: 00.0 kph"),
                    "White",
                    textScale
                );
                float maxWidth = maxTextSize.X + boxPadding * 2;

                DrawThrottleBarWithBox(
                    frame,
                    surface,
                    (float)throttle / 100,
                    new Vector2(5, hud.SurfaceSize.Y - textHeight - 140),
                    80,
                    8,
                    textScale
                );
            }

            private void DrawTextWithManualBox(
                MySpriteDrawFrame frame,
                IMyTextSurface surface,
                string text,
                Vector2 position,
                TextAlignment alignment,
                float boxPadding,
                float maxWidth,
                float textScale
            )
            {
                // Measure the text size
                Vector2 textSize = surface.MeasureStringInPixels(
                    new StringBuilder(text),
                    "White",
                    textScale
                );
                Vector2 boxSize = new Vector2(maxWidth, textSize.Y + boxPadding * 2);

                // Adjust the position so it's the top-left corner of the box
                Vector2 boxTopLeft = position;

                // Define text position based on alignment
                Vector2 textPosition = boxTopLeft + new Vector2(boxPadding, boxPadding);

                if (alignment == TextAlignment.RIGHT)
                {
                    textPosition.X = boxTopLeft.X + maxWidth - boxPadding;
                }

                // Draw the text inside the box
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = text,
                        Position = textPosition, // Position the text based on padding
                        RotationOrScale = textScale,
                        Color = Color.Lime,
                        Alignment = alignment,
                        FontId = "White"
                    }
                );
            }

            private void DrawThrottleBarWithBox(
                MySpriteDrawFrame frame,
                IMyTextSurface surface,
                float throttle,
                Vector2 position,
                float boxPadding,
                float maxWidth,
                float barHeight,
                Color barColor = default(Color),
                Color boxColor = default(Color),
                float lineThickness = 2f
            )
            {
                // Set default colors if not provided
                barColor = barColor == default(Color) ? Color.Lime : barColor;
                boxColor = boxColor == default(Color) ? Color.Lime : boxColor;

                // Calculate box size
                Vector2 boxSize = new Vector2(
                    maxWidth + boxPadding * 1f,
                    barHeight + boxPadding * 1.5f
                );
                boxSize.X = boxSize.X / 4;

                Vector2 boxTopLeft = position;

                // Draw the box (rectangle) around the throttle bar
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(0, lineThickness / 2),
                        Size = new Vector2(boxSize.X, lineThickness),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(0, boxSize.Y - lineThickness / 2),
                        Size = new Vector2(boxSize.X, lineThickness),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = boxTopLeft + new Vector2(lineThickness / 2, boxSize.Y / 2),
                        Size = new Vector2(lineThickness, boxSize.Y),
                        Color = boxColor
                    }
                );
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position =
                            boxTopLeft + new Vector2(boxSize.X - lineThickness / 2, boxSize.Y / 2),
                        Size = new Vector2(lineThickness, boxSize.Y),
                        Color = boxColor
                    }
                );

                // Calculate the filled height based on throttle
                float filledHeight = barHeight * throttle;
                Vector2 filledSize = new Vector2(maxWidth * 100, filledHeight * boxSize.Y * 1.25f);
                barColor = throttle > 0.8f ? Color.Yellow : Color.Lime;

                // Draw the filled throttle bar
                frame.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position =
                            boxTopLeft
                            + new Vector2(
                                0,
                                (boxSize.Y - boxPadding / 33 - lineThickness / 2 - filledSize.Y / 2)
                                    * 1.025f
                            ),
                        Size = new Vector2(boxSize.X, filledSize.Y * 1.05f),
                        Color = barColor
                    }
                );

                // **Add Tick Marks Every 10%**

                // Define the number of ticks (10 intervals for 0% to 100%)
                int numberOfTicks = 4;

                // Define the size and color of the tick marks
                Color tickColor = Color.Yellow; // Color of the tick marks
                float filledHeighttick = barHeight * throttle;
               
            }

            private void DrawFlightPathMarker(
                MySpriteDrawFrame frame,
                Vector3D currentVelocity,
                MatrixD worldMatrix,
                double roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                // Constants
                const double DegToRad = Math.PI / 180.0;
                const float MarkerSize = 20f;
                const float WingLength = 15f;
                const float WingThickness = 2f;
                const float WingOffsetX = 10f; // Additional offset along X-axis

                // Normalize current velocity
                Vector3D velocityDirection = Vector3D.Normalize(currentVelocity);

                // Convert velocity vector from world space to local space
                Vector3D localVelocity = Vector3D.TransformNormal(
                    velocityDirection,
                    MatrixD.Transpose(worldMatrix)
                );

                // Compute yaw and pitch from local velocity (in degrees)
                double velocityYaw =
                    Math.Atan2(localVelocity.X, -localVelocity.Z) * 180.0 / Math.PI;
                double velocityPitch =
                    Math.Atan2(localVelocity.Y, -localVelocity.Z) * 180.0 / Math.PI;

                // Convert roll to radians for rotation
                float rollRad = (float)(roll * DegToRad);

                // 1) Determine how many pixels per degree we move in X/Y
                //    *Using the same 'pixelsPerDegree' you’re using in your pitch ladder*
                //    Negative sign on yaw to match typical HUD conventions
                Vector2 markerOffset = new Vector2(
                    (float)(-velocityYaw * pixelsPerDegree), // X offset
                    (float)(velocityPitch * pixelsPerDegree) // Y offset
                );

                // 2) Rotate marker offset by negative roll to “tilt” with aircraft roll
                Vector2 rotatedOffset = RotatePoint(markerOffset, Vector2.Zero, -rollRad);

                // 3) Final marker position relative to center
                Vector2 markerPosition = new Vector2(centerX, centerY) + rotatedOffset;

                // --- Draw the flight path marker (circle) ---
                var marker = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = markerPosition,
                    Size = new Vector2(MarkerSize, MarkerSize),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(marker);

                // --- Draw “wings” on the marker ---
                // Offsets before roll
                Vector2 leftWingOffset = new Vector2(-WingLength / 2 - WingOffsetX, 0f);
                Vector2 rightWingOffset = new Vector2(WingLength / 2 + WingOffsetX, 0f);

                // Rotate them around the same center
                Vector2 rotatedLeftWingOffset = RotatePoint(leftWingOffset, Vector2.Zero, -rollRad);
                Vector2 rotatedRightWingOffset = RotatePoint(
                    rightWingOffset,
                    Vector2.Zero,
                    -rollRad
                );

                // Left wing
                var leftWing = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = markerPosition + rotatedLeftWingOffset,
                    Size = new Vector2(WingLength, WingThickness),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = -rollRad
                };
                frame.Add(leftWing);

                // Right wing
                var rightWing = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = markerPosition + rotatedRightWingOffset,
                    Size = new Vector2(WingLength, WingThickness),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = -rollRad
                };
                frame.Add(rightWing);
            }

            private double GetPitchFromWorldMatrix(MatrixD matrix)
            {
                Vector3D forward = matrix.Forward;
                return Math.Atan2(forward.Y, -forward.Z) * (180.0 / Math.PI);
            }
            private double CalculateAOAFromFlightPath(Vector3D velocity, MatrixD worldMatrix)
            {
                if (velocity.LengthSquared() < 0.01)
                    return 0;

                // Transform velocity into cockpit-local space
                Vector3D velocityDirection = Vector3D.Normalize(velocity);
                Vector3D localVelocity = Vector3D.TransformNormal(
                    velocityDirection,
                    MatrixD.Transpose(worldMatrix)
                );

                // Velocity pitch (in degrees) — this is what your flight path marker uses
                double velocityPitch =
                    Math.Atan2(localVelocity.Y, -localVelocity.Z) * (180.0 / Math.PI);

                // Nose pitch
                Vector3D forward = worldMatrix.Forward;
                double nosePitch = Math.Atan2(forward.Y, -forward.Z) * (180.0 / Math.PI);

                // AOA = Nose Pitch - Velocity Pitch
                return nosePitch - velocityPitch;
            }

            private void DrawAOABracket(MySpriteDrawFrame frame, double aoa)
            {
                float centerX = hud.SurfaceSize.X / 2;
                float centerY = hud.SurfaceSize.Y / 2;

                float pixelsPerDegreeY = hud.SurfaceSize.Y / 45f;
                float aoaOffsetY = (float)(aoa * pixelsPerDegreeY);

                Vector2 bracketPosition = new Vector2(centerX - 100f, centerY);

                // Display the numeric AOA value
                var aoaText = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"{aoa:F1}°",
                    Position = bracketPosition + new Vector2(-100f, 20),
                    RotationOrScale = 0.6f,
                    Color = Color.White,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White"
                };
                frame.Add(aoaText);
            }

            private void DrawTrim(MySpriteDrawFrame frame, double aoa)
            {
                float centerX = hud.SurfaceSize.X / 2;
                float centerY = hud.SurfaceSize.Y / 2;

                float pixelsPerDegreeY = hud.SurfaceSize.Y / 45f;
                float aoaOffsetY = (float)(aoa * pixelsPerDegreeY);

                Vector2 bracketPosition = new Vector2(centerX - 100f, centerY);

                // Display the numeric AOA value
                var aoaText = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"T|O: {myjet.offset:F1}°",
                    Position = bracketPosition + new Vector2(-100f, -0f),
                    RotationOrScale = 0.6f,
                    Color = Color.White,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White"
                };
                frame.Add(aoaText);

                // Display the numeric AOA value
                var aoaText2 = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"T: {currentTrim:F1}°",
                    Position = bracketPosition + new Vector2(-100f, 40f),
                    RotationOrScale = 0.6f,
                    Color = Color.White,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White"
                };
                frame.Add(aoaText2);
            }
            private void DrawBomb(MySpriteDrawFrame frame, double aoa)
            {
                float centerX = hud.SurfaceSize.X / 2;
                float centerY = hud.SurfaceSize.Y / 2;

                float pixelsPerDegreeY = hud.SurfaceSize.Y / 45f;
                float aoaOffsetY = (float)(aoa * pixelsPerDegreeY);
                Vector2 bracketPosition = new Vector2(centerX - 40f, centerY + 40f);

                var customDataLines = ParentProgram.Me.CustomData.Split(
                    new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                float offsetY = 0f; // Initial vertical offset
                float spacingY = 20f; // Spacing between lines

                double sum = 0;
                int count = 0;

                for (int i = 0; i < customDataLines.Length; i++)
                {
                    string line = customDataLines[i];

                    if (line.StartsWith("DataSlot"))
                    {
                        // Try to extract the second number from the line
                        string[] parts = line.Split(':');
                        double extractedValue;

                        if (parts.Length > 1 && double.TryParse(parts[1], out extractedValue))
                        {
                            sum += extractedValue;
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    double averageValue = sum / count;

                    var aoaText = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = string.Format("Bomb Acc: {0:F1}°", averageValue), // C# 6 compatible formatting
                        Position = bracketPosition + new Vector2(offsetY, 0),
                        RotationOrScale = 0.6f,
                        Color = Color.White,
                        Alignment = TextAlignment.RIGHT,
                        FontId = "White"
                    };

                    frame.Add(aoaText);
                }
            }
            // Helper method to rotate a point around a pivot
            private Vector2 RotatePoint(Vector2 point, Vector2 pivot, float angle)
            {
                float cosTheta = (float)Math.Cos(angle);
                float sinTheta = (float)Math.Sin(angle);
                Vector2 translatedPoint = point - pivot;
                Vector2 rotatedPoint = new Vector2(
                    translatedPoint.X * cosTheta - translatedPoint.Y * sinTheta,
                    translatedPoint.X * sinTheta + translatedPoint.Y * cosTheta
                );
                return rotatedPoint + pivot;
            }
            private void DrawRadar(
    MySpriteDrawFrame frame,
    Jet myjet,
    float boxCenterX,
    float boxCenterY,
    float boxWidth,
    float boxHeight,
    float pixelPerDegree)
            {
                // Half the width/height for positioning convenience:
                float halfWidth = boxWidth * 0.5f;
                float halfHeight = boxHeight * 0.5f;

                // Pulling Azimuth and Elevation from your jet
                double radarX = myjet._radar.Azimuth;    // -1.0 ... +1.0
                double radarY = myjet._radar.Elevation;  // -1.0 ... +1.0

                // --- Draw boundary lines ---------------------------------

                // Top horizontal line
                MySprite topLineSprite = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(boxCenterX, boxCenterY - halfHeight),
                    Size = new Vector2(boxWidth, 2),
                    Color = Color.Lime,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(topLineSprite);

                // Bottom horizontal line
                MySprite bottomLineSprite = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(boxCenterX, boxCenterY + halfHeight),
                    Size = new Vector2(boxWidth, 2),
                    Color = Color.Lime,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(bottomLineSprite);

                // Left vertical line
                MySprite leftLineSprite = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(boxCenterX - halfWidth, boxCenterY),
                    Size = new Vector2(2, boxHeight),
                    Color = Color.Lime,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(leftLineSprite);

                // Right vertical line
                MySprite rightLineSprite = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(boxCenterX + halfWidth, boxCenterY),
                    Size = new Vector2(2, boxHeight),
                    Color = Color.Lime,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(rightLineSprite);
                string targetName = "null";
                if(!myjet._radar.GetTargetedEntity().IsEmpty())
                    targetName = myjet._radar.GetTargetedEntity().Name.ToString();
                MySprite targettext = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "Tar:" + targetName,
                    Position = new Vector2(boxCenterX + 30f, boxCenterY - 40f),
                    RotationOrScale = 0.75f,
                    Color = Color.Lime,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White"
                };
                frame.Add(targettext);

                // --- Draw radar dot --------------------------------------
                // If radarX = -1 => far left; +1 => far right, etc.
                // If radarY = -1 => top; +1 => bottom.
                MySprite radarDot = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(
                        boxCenterX + (float)radarX * halfWidth * -1,
                        boxCenterY + (float)radarY * halfHeight * -1
                    ),
                    Size = new Vector2(4, 4),
                    Color = Color.Lime,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(radarDot);
            }

            private double CalculateHeading()
            {
                return NavigationHelper.CalculateHeading(cockpit);
            }

            private double GetAltitude()
            {
                double altitude;
                if (!cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude))
                {
                    return 0;
                }
                return altitude;
            }

            private double CalculateAngleOfAttack(
                Vector3D forwardVector,
                Vector3D velocity,
                Vector3D upVector
            )
            {
                if (velocity.LengthSquared() < 0.01)
                    return 0;

                Vector3D velocityDirection = Vector3D.Normalize(velocity);

                // Calculate the angle between the velocity and the forward vector in the plane defined by the up vector


                double angleOfAttack =
                    Math.Atan2(
                        Vector3D.Dot(velocityDirection, upVector),
                        Vector3D.Dot(velocityDirection, forwardVector)
                    ) * (180 / Math.PI);

                return angleOfAttack;
            }

            private void DrawArtificialHorizon(
                MySpriteDrawFrame frame,
                float pitch,
                float roll,
                float centerX,
                float centerY,
                float pixelsPerDegree
            )
            {
                List<MySprite> sprites = new List<MySprite>();

                // Typical F-16 pitch lines are drawn every 10 degrees (±80, ±70, ..., ±10)
                // Horizon is at 0, so we skip i == 0
                for (int i = -90; i <= 90; i += 5)
                {
                    if (i == 0)
                        continue; // skip horizon line here, we draw a dedicated line below

                    // Calculate vertical position for this pitch line
                    float markerY = centerY - (i - pitch) * pixelsPerDegree;

                    // Skip if off-screen by a bit
                    if (markerY < -100 || markerY > hud.SurfaceSize.Y + 100)
                        continue;

                    // F-16 style: positive pitch lines angle down, negative pitch lines angle up
                    bool isPositive = (i > 0);

                    // Common line settings
                    float lineWidth = 90f; // total width of the pitch line
                    float lineThickness = 2f; // thickness of the center segment
                    Color lineColor = Color.Lime; // or another color you prefer

                    // 1) MAIN HORIZONTAL SEGMENT
                    float halfWidth = lineWidth * 1.225f; //So it clips a tiny bit
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX * 0.75f, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = TextAlignment.CENTER
                        }
                    );
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX * 1.25f, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = TextAlignment.CENTER
                        }
                    );
                    // 2) ANGLING THE TIPS (“wings”)
                    float tipLength = 12f; // how “long” each angled tip extends
                    float tipThickness = 2f; // thickness of the tip lines
                    // For a “V,” use ±45° angles. Negative pitch inverts the direction
                    float tipAngle = MathHelper.ToRadians(isPositive ? 45f : -45f);

                    // 3) PITCH LABELS
                    string label = Math.Abs(i).ToString();
                    float labelOffsetX = halfWidth + tipLength + 10f; // a bit to the outside
                    float labelOffsetY = 0f;

                    // Left label
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            Position = new Vector2(centerX - labelOffsetX, markerY + 10f),
                            RotationOrScale = 0.8f,
                            Color = lineColor,
                            Alignment = TextAlignment.RIGHT,
                            FontId = "White"
                        }
                    );
                    // Right label
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            Position = new Vector2(centerX + labelOffsetX, markerY + 10f),
                            RotationOrScale = 0.8f,
                            Color = lineColor,
                            Alignment = TextAlignment.LEFT,
                            FontId = "White"
                        }
                    );
                }

                // --- DISTINCT HORIZON LINE (at 0 deg pitch) ---
                float horizonY = centerY + pitch * pixelsPerDegree;
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX * 1.25f, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = Color.LimeGreen,
                        Alignment = TextAlignment.CENTER
                    }
                );
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX * 0.75f, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = Color.LimeGreen,
                        Alignment = TextAlignment.CENTER
                    }
                );
                // --- F18/F16 style center marker, e.g. -^- or similar ---
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "-^-",
                        Position = new Vector2(centerX, centerY - 10),
                        RotationOrScale = 0.8f,
                        Color = Color.Yellow,
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    }
                );

                // --- APPLY ROLL ROTATION to everything ---
                float rollRad = MathHelper.ToRadians(-roll);
                float cosRoll = (float)Math.Cos(rollRad);
                float sinRoll = (float)Math.Sin(rollRad);

                // Use a normal for-loop (not foreach) to transform and add
                for (int s = 0; s < sprites.Count; s++)
                {
                    MySprite sprite = sprites[s];
                    Vector2 pos = sprite.Position ?? Vector2.Zero;
                    Vector2 offset = pos - new Vector2(centerX, centerY);

                    // Rotate around center
                    Vector2 rotated = new Vector2(
                        offset.X * cosRoll - offset.Y * sinRoll,
                        offset.X * sinRoll + offset.Y * cosRoll
                    );

                    sprite.Position = rotated + new Vector2(centerX, centerY);

                    // For TEXTURE sprites, add the roll to the sprite's existing rotation
                    if (sprite.Type == SpriteType.TEXTURE)
                    {
                        float existing = sprite.RotationOrScale;
                        sprite.RotationOrScale = existing + rollRad;
                    }

                    // Store updated sprite back
                    sprites[s] = sprite;

                    // Add sprite to the frame
                    frame.Add(sprite);
                }
            }

            private void DrawCompass(MySpriteDrawFrame frame, double heading)
            {
                // --- Configuration ---
                float centerX = hud.SurfaceSize.X / 2f;
                float compassY = 40f; // Vertical position of the compass center
                float compassWidth = hud.SurfaceSize.X * 0.9f; // How wide the compass bar is on screen
                float compassHeight = 30f; // Height of the background bar
                float viewAngle = 90f;    // The total field of view the compass bar represents (e.g., 90 degrees)
                float halfViewAngle = viewAngle / 2f;
                int increment = 20;      // Degrees between each marker drawn

                // Scale: Pixels per degree across the visible compass width
                float headingScale = compassWidth / viewAngle;


                ParentProgram.Echo($"DrawCompass Heading: {heading:F2}");
                // --- Markers and Labels ---
                // Iterate through possible headings.
                // The loop still iterates 360/increment times, but drawing only happens for visible markers.
                for (int markerHeading = 0; markerHeading < 360; markerHeading += increment)
                {
                    // Calculate the difference between the marker and the current heading.
                    // Normalize the difference to be between -180 and 180 degrees.
                    // Example: If heading = 10, marker = 350, delta = -20. If heading = 350, marker = 10, delta = 20.
                    double deltaHeading = ((markerHeading - heading + 540) % 360) - 180;

                    // Check if the marker falls within the visible range (-halfViewAngle to +halfViewAngle).
                    if (deltaHeading >= -halfViewAngle && deltaHeading <= halfViewAngle)
                    {
                        // Calculate the horizontal screen position for this marker.
                        float markerX = centerX + (float)deltaHeading * headingScale;

                        // Determine properties based on whether it's a cardinal/inter-cardinal direction.
                        bool isMajorTick = (markerHeading % 90 == 0);

                        // Use taller lines for major ticks (N, E, S, W).
                        float markerLineHeight = isMajorTick ? compassHeight * 0.7f : compassHeight * 0.4f;
                        // Use distinct colors for major ticks.
                        Color markerColor = isMajorTick ? Color.Green : Color.Lime;

                        // --- Draw Marker Line ---
                        // One draw call per visible marker line.
                        var markerLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple", // Use a 1x1 white pixel texture if possible, stretched
                            Position = new Vector2(markerX, compassY), // Centered vertically on the background
                            Size = new Vector2(2f, markerLineHeight), // Thin line
                            Color = markerColor,
                            Alignment = TextAlignment.CENTER // Center the line sprite at (markerX, compassY)
                        };
                        frame.Add(markerLine);

                        // --- Draw Text Label ---
                        // Get the appropriate label (e.g., "N", "E", "S", "W" or the degree number).
                        string label = isMajorTick ? GetCompassDirection(markerHeading) : markerHeading.ToString();
                        float textScale = 0.7f; // Adjust text size

                        // One draw call per visible marker label.
                        var markerText = new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = label,
                            // Position text just below the compass bar for clarity
                            Position = new Vector2(markerX, compassY + compassHeight / 2f + 5f), // Offset below the bar
                            RotationOrScale = textScale,
                            Color = markerColor, // Match the line color
                            Alignment = TextAlignment.CENTER,
                            FontId = "White" // Or your preferred font
                        };
                        frame.Add(markerText);
                    }
                }

                // --- Heading Indicator ---
                // A single, fixed indicator showing the center (current heading). One draw call.
                var headingIndicator = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Triangle", // Assumes a triangle texture pointing up by default
                                       // Position it above the center of the compass bar
                    Position = new Vector2(centerX, compassY - compassHeight / 2f - 6f), // Adjust Y pos slightly above
                    Size = new Vector2(12f, 10f), // Adjust size as needed
                    Color = Color.Yellow,
                    Alignment = TextAlignment.CENTER,
                    // Rotate it 180 degrees (PI radians) to point down
                    RotationOrScale = (float)Math.PI
                };
                frame.Add(headingIndicator);
            }

            // --- Compass Direction Helper (Unchanged) ---
            // Kept exactly as you provided it.
            private string GetCompassDirection(double heading)
            {
                // Normalize heading to be within [0, 360) - Handles potential negative inputs if needed, though current loop avoids it.
                // heading = (heading % 360 + 360) % 360;

                // Your original logic - works correctly for cardinal/intercardinal points.
                if (heading >= 337.5 || heading < 22.5) return "N";  // 0
                else if (heading >= 22.5 && heading < 67.5) return "NE"; // 45
                else if (heading >= 67.5 && heading < 112.5) return "E";  // 90
                else if (heading >= 112.5 && heading < 157.5) return "SE"; // 135
                else if (heading >= 157.5 && heading < 202.5) return "S";  // 180
                else if (heading >= 202.5 && heading < 247.5) return "SW"; // 225
                else if (heading >= 247.5 && heading < 292.5) return "W";  // 270
                else return "NW"; // 315 (covers 292.5 to 337.5)
            }

            private void DrawRollIndicator(MySpriteDrawFrame frame, float roll)
            {
                float centerX = (hud.SurfaceSize.X / 2) + hud.SurfaceSize.X * 0.3f;
                float rollY = hud.SurfaceSize.Y - 140f;
                float radius = 30f;
                roll = roll - 180;
                // Display roll value
                var rollText = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Roll: {roll:F0}°",
                    Position = new Vector2(centerX, rollY + radius + 20f),
                    RotationOrScale = 0.8f,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                };
                frame.Add(rollText);
            }

            public override string[] GetOptions()
            {
                return new string[] { "Back to Main Menu" };
            }

            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0:
                        SystemManager.ReturnToMainMenu();
                        break;
                }
            }
        }

        class LogoDisplay : ProgramModule
        {
            private UIController uiController;
            private bool isActive = false;
            private List<Particle> particles;
            private List<DustParticle> dustParticles; // List for dust particles
            private Random random = new Random();
            private const int particleCount = 49;
            private const int dustParticleCount = 120; // Number of dust particles
            private const float particleSpeed = 0.2f;
            private const float dustSpeed = 0.4f; // Slower speed for dust particles
            private const float wobbleIntensity = 0.1f;
            private const float minParticleSize = 2f;
            private const float maxParticleSize = 12f;
            private const float minDustSize = 1f; // Smaller size for dust particles
            private const float maxDustSize = 7f;
            private const float minOpacity = 0.3f;
            private const float maxOpacity = 0.8f;
            private const float linkDistance = 80;
            private const float maxIlluminationDistance = 45f; // Maximum distance for illumination
            private int motivationalIndex = 0;
            private int currentEvilIndex = 0;
            private int tickCounter = 0;
            private int animationcounter = 0;
            private int ticksPerMotivational = 400;
            private int ticksPerEvil = 200;
            private bool showingEvilText = false;
            private List<TrailParticle> trailParticles = new List<TrailParticle>();
            private const float minTrailLifetime = 60f; // Minimum lifetime in ticks (1 second)
            private const float maxTrailLifetime = 120f; // Maximum lifetime in ticks (2 seconds)
            private const int minTrailParticles = 1;
            private const int maxTrailParticles = 4;
            private const float trailParticleSize = 2f;

            private const int ZOOM = 10;
            private const float BRIGHTNESS = 0.8f;
            private float fScale = 1.25f;

            // Utility functions
            float CosRange(float amt, float range, float minimum)
            {
                return (((1.0f + (float)Math.Cos(MathHelper.ToRadians(amt))) * 0.5f) * range)
                    + minimum;
            }

            private List<string> motivationalTexts = new List<string>
            {
                "Innovate for a better\ntomorrow",
                "Success is a journey,\nnot a destination",
                "Every challenge is\nan opportunity",
                "Excellence is not an act,\nbut a habit",
                "Dream big, work hard,\nstay focused",
                "Turn obstacles into\nopportunities",
                "Create your own destiny",
                "Rise above the rest",
                "Strength comes from\nadversity",
                "Your potential is\nlimitless",
                "Believe in yourself,\nachieve the impossible",
                "Be the change you\nwish to see",
                "Push boundaries,\nbreak limits",
                "The future belongs\nto those who prepare",
                "Never settle for less\nthan your best",
                "Lead with vision,\nact with purpose",
                "Transform challenges\ninto victories",
                "Inspire others\nthrough your actions",
                "Greatness is achieved\none step at a time",
                "Persevere and succeed",
                "Rise to the challenge\nand excel",
                "Turn dreams into reality",
                "Make every moment count",
                "Stay relentless in\nthe pursuit of excellence",
                "Success is forged in\nthe fires of hard work",
                "Unleash your inner power",
                "Great things take time,\nstay patient",
                "Success is a journey,\nnot a destination",
                "Embrace every challenge\nas an opportunity",
                "Your effort defines\nyour success",
                "Pursue greatness relentlessly",
                "Strive for progress,\nnot perfection",
                "Commit to your goals\nand achieve greatness",
                "Lead with courage,\nact with integrity"
            };
            private List<string> evilTexts = new List<string>
            {
                "We thrive on\nbribery and power",
                "Control is profit,\nand profit is control",
                "Peace is a myth;\nconflict pays well",
                "Infiltrate and dominate",
                "Power is the ultimate\ncurrency",
                "Chaos breeds opportunity",
                "Fear is the greatest\ntool of control",
                "Silence dissent\nthrough intimidation",
                "Manipulate and conquer",
                "Corruption is a means\nto an end",
                "Betrayal is an art\nwe've perfected",
                "Subjugate the weak\nfor our gain",
                "Violence is an\nacceptable solution",
                "Lies are a necessary\nevil",
                "Exploit every weakness\nto our advantage",
                "Absolute power corrupts\nabsolutely",
                "Profit from the\nsuffering of others",
                "Tyranny is the path\nto true control",
                "Repression ensures\ndominance",
                "Divide and conquer\nto control",
                "Oppression is our\nbusiness model",
                "Deceit is a powerful\nally",
                "Instill fear,\nmaintain control",
                "Ensure loyalty through\nbribery and threat",
                "We control the narrative\nthrough manipulation",
                "Success demands ruthless\nand cunning strategies",
                "Enforce obedience\nwith an iron fist",
                "Compromise integrity\nfor power",
                "Betrayal is our path\nto dominance",
                "Exploitation is our\nprimary strategy",
                "Punish dissent harshly\nto deter rebellion",
                "Deception is a means\nto control the masses",
                "Our will is enforced\nthrough fear",
                "Victory is achieved\nthrough oppression",
                "Moral boundaries are\nsacrificed for power",
                "Corruption is the price\nof ultimate control"
            };

            public bool IsActive
            {
                get { return isActive; }
            }

            public LogoDisplay(Program program, UIController uiController) : base(program)
            {
                name = "ScreenSaver";
                this.uiController = uiController;
                particles = new List<Particle>();
                dustParticles = new List<DustParticle>();

                List<string> snowflakeTextures = new List<string>
                {
                    "Snowflake1",
                    "Snowflake2",
                    "Snowflake3"
                };
                List<Color> snowflakeColors = new List<Color>
                {
                    Color.White,
                    Color.LightBlue,
                    Color.Cyan
                };

                // Initialize snowflake particles
                for (int i = 0; i < particleCount; i++)
                {
                    particles.Add(
                        new Particle(
                            new Vector2(
                                random.Next(0, (int)uiController.MainScreen.SurfaceSize.X),
                                random.Next(0, (int)uiController.MainScreen.SurfaceSize.Y)
                            ),
                            new Vector2(0f, (float)(random.NextDouble() * 0.5 + 0.5))
                                * particleSpeed,
                            (float)random.NextDouble() * (maxOpacity - minOpacity) + minOpacity,
                            (float)random.NextDouble() * (maxParticleSize - minParticleSize)
                                + minParticleSize,
                            snowflakeColors[random.Next(snowflakeColors.Count)],
                            snowflakeTextures[random.Next(snowflakeTextures.Count)],
                            random
                        )
                    );
                }

                // Initialize falling snow dust particles
                for (int i = 0; i < dustParticleCount; i++)
                {
                    dustParticles.Add(
                        new DustParticle(
                            new Vector2(
                                random.Next(0, (int)uiController.MainScreen.SurfaceSize.X),
                                random.Next(0, (int)uiController.MainScreen.SurfaceSize.Y)
                            ),
                            new Vector2(0f, (float)(random.NextDouble() * 0.5 + 0.5)) * dustSpeed,
                            (float)random.NextDouble() * (maxOpacity - minOpacity) + minOpacity,
                            (float)random.NextDouble() * (maxDustSize - minDustSize) + minDustSize,
                            Color.White
                        )
                    );
                }
            }

            public override string[] GetOptions() =>
                new string[] { "Display Christmas Animation", "Back" };

            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0:
                        isActive = true;
                        break;
                    case 1:
                        isActive = false;
                        SystemManager.ReturnToMainMenu();
                        break;
                }
            }

            public override void HandleSpecialFunction(int key)
            {
                if (isActive)
                {
                    isActive = false;
                    SystemManager.ReturnToMainMenu();
                }
            }

            public override void Tick()
            {
                if (isActive)
                {
                    //UpdateParticles();
                    animationcounter++;
                    Vector2 screenSize = uiController.MainScreen.SurfaceSize;
                    uiController.RenderCustomFrame(
                        (frame, area) => RenderParticles(frame, area),
                        new RectangleF(Vector2.Zero, screenSize)
                    );
                    tickCounter++;
                    if (showingEvilText && tickCounter >= ticksPerEvil)
                    {
                        showingEvilText = false;
                        tickCounter = 0;
                    }
                    else if (!showingEvilText && tickCounter >= ticksPerMotivational)
                    {
                        if (random.Next(10) < 1)
                        {
                            showingEvilText = true;
                            currentEvilIndex = random.Next(evilTexts.Count);
                            motivationalIndex = (motivationalIndex + 1) % motivationalTexts.Count;
                        }
                        else
                        {
                            motivationalIndex = (motivationalIndex + 1) % motivationalTexts.Count;
                        }
                        tickCounter = 0;
                    }
                }
            }

            private float NextFloat(float min, float max)
            {
                return (float)(random.NextDouble() * (max - min) + min);
            }

            private void UpdateParticles()
            {
                Vector2 screenSize = uiController.MainScreen.SurfaceSize;

                // Update snowflake particles
                for (int i = 0; i < particles.Count; i++)
                {
                    Particle particle = particles[i];
                    particle.Position += particle.Velocity;

                    if (particle.Position.Y > screenSize.Y)
                    {
                        particle.Position.Y = 0;
                        particle.Position.X = random.Next(0, (int)screenSize.X);
                    }

                    particles[i] = particle;
                }

                // Update dust particles
                for (int i = 0; i < dustParticles.Count; i++)
                {
                    var dust = dustParticles[i];
                    dust.Position += dust.Velocity;

                    if (dust.Position.Y > screenSize.Y)
                    {
                        dust.Position.Y = 0;
                        dust.Position.X = random.Next(0, (int)screenSize.X);
                    }

                    dustParticles[i] = dust;
                }
            }

            private void RenderParticles(MySpriteDrawFrame frame, RectangleF area)
            {
                float time = animationcounter / 60.0f * 1.25f;
                Vector2 resolution = new Vector2(area.Width, area.Height);
                Vector2 center = resolution / 2.0f;

                // Simplified step size and iterations
                for (int x = 0; x < area.Width; x += 9)
                {
                    for (int y = 0; y < area.Height; y += 9)
                    {
                        Vector2 fragCoord = new Vector2(x, y);
                        Vector2 uv = fragCoord / resolution;
                        Vector2 p =
                            (2.0f * fragCoord - resolution) / Math.Max(resolution.X, resolution.Y);

                        fScale = CosRange(time * 5.0f, 1.0f, 0.5f);

                        for (int i = 1; i < ZOOM; i++)
                        {
                            float _i = (float)i;
                            p.X +=
                                0.05f / _i * (float)Math.Sin(_i * p.Y + time * 0.5f) * fScale
                                + 0.01f * (float)Math.Sin(time * 0.3f);
                            p.Y +=
                                0.05f / _i * (float)Math.Sin(_i * p.X + time * 0.4f) * fScale
                                + 0.01f * (float)Math.Cos(time * 0.2f);
                        }

                        Vector3 col = new Vector3(
                            0.5f * (float)Math.Sin(2.0f * p.X) + 0.5f,
                            0.5f * (float)Math.Sin(2.0f * p.Y) + 0.5f,
                            (float)Math.Sin(p.X + p.Y)
                        );
                        col *= BRIGHTNESS;

                        float vignette =
                            1.0f
                            - 2.0f
                                * ((uv.X - 0.5f) * (uv.X - 0.5f) + (uv.Y - 0.5f) * (uv.Y - 0.5f));
                        vignette = MathHelper.Clamp(vignette, 0.0f, 1.0f);

                        var sprite = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = fragCoord + area.Position,
                            Size = new Vector2(9, 9), // Larger size for fewer sprites
                            Color = new Color(col.X * vignette, col.Y * vignette, col.Z * vignette),
                            Alignment = TextAlignment.CENTER
                        };

                        frame.Add(sprite);
                    }
                }
            }

            private struct Particle
            {
                public Vector2 Position;
                public Vector2 Velocity;
                public float Opacity;
                public float Size;
                public Color Color;
                public string Texture;

                public Particle(
                    Vector2 position,
                    Vector2 velocity,
                    float opacity,
                    float size,
                    Color color,
                    string texture,
                    Random random
                )
                {
                    Position = position;
                    Velocity = velocity;
                    Opacity = opacity;
                    Size = size;
                    Color = color;
                    Texture = texture;
                }
            }

            private struct DustParticle
            {
                public Vector2 Position;
                public Vector2 Velocity;
                public float Opacity;
                public float Size;
                public Color Color;

                public DustParticle(
                    Vector2 position,
                    Vector2 velocity,
                    float opacity,
                    float size,
                    Color color
                )
                {
                    Position = position;
                    Velocity = velocity;
                    Opacity = opacity;
                    Size = size;
                    Color = color;
                }
            }

            private struct TrailParticle
            {
                public Vector2 Position;
                public Vector2 Velocity;
                public float Opacity;
                public float Size;
                public Color Color;
                public float Lifetime;

                public TrailParticle(
                    Vector2 position,
                    Vector2 velocity,
                    float opacity,
                    float size,
                    float lifetime,
                    Color color
                )
                {
                    Position = position;
                    Velocity = velocity;
                    Opacity = opacity;
                    Size = size;
                    Lifetime = lifetime;
                    Color = color;
                }
            }
        }

        class AirToGround : ProgramModule
        {
            private List<IMyShipMergeBlock> missileBays = new List<IMyShipMergeBlock>();
            private bool[] baySelected;
            private bool isTopdownEnabled = false;
            public AirToGround(Program program, Jet jet) : base(program)
            {
                missileBays = jet._bays;
                baySelected = new bool[missileBays.Count];
                LoadTopdownState();
                name = "Air To Ground";
            }
            private void LoadTopdownState()
            {
                var customDataLines = ParentProgram.Me.CustomData.Split(
                    new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (var line in customDataLines)
                {
                    if (line.StartsWith("Topdown:"))
                    {
                        isTopdownEnabled = line.EndsWith("true");
                        break;
                    }
                }
            }
            public override string[] GetOptions()
            {
                var options = new List<string>
                {
                    "Fire Selected Bays",
                    "Toggle Selected Bays",
                    "Bombardment",
                    string.Format("Topdown [{0}]", isTopdownEnabled ? "ON" : "OFF"),
                    "PreSelect"
                };
                for (int i = 0; i < missileBays.Count; i++)
                {
                    string baySymbol = baySelected[i] ? "[X]" : "[ ]";
                    string bayStatus = missileBays[i]?.IsConnected == true ? "[ON]" : "[OFF]";
                    var mergeBlock = missileBays[i] as IMyShipMergeBlock;
                    bool isConnected = mergeBlock != null && mergeBlock.IsConnected;
                    char colorChar = ColorToChar(isConnected ? 0 : 255, isConnected ? 255 : 0, 0);
                    options.Add(
                        string.Format(
                            "{0}{1} {2} {3}",
                            colorChar,
                            baySymbol,
                            missileBays[i]?.CustomName ?? "Unknown Bay",
                            bayStatus
                        )
                    );
                }
                return options.ToArray();
            }
            public override void ExecuteOption(int index)
            {
                if (index == 3)
                {
                    ToggleTopdownMode();
                }
                else if (index == 0)
                {
                    FireSelectedBays();
                    TransferCacheToSlots();
                }
                else if (index == 1)
                {
                    ToggleSelectedBays();
                }
                else if (index == 2)
                {
                    ExecuteBombardment();
                    TransferCacheToSlots();
                }
                else if (index == 4)
                {
                    FireSelectedBays();
                }
                else if (index > 4 && index - 5 < missileBays.Count)
                {
                    ToggleBaySelection(index - 5);
                }
            }
            private void ToggleTopdownMode()
            {
                isTopdownEnabled = !isTopdownEnabled;
                UpdateTopdownCustomData();
            }
            private void UpdateTopdownCustomData()
            {
                var customDataLines = ParentProgram.Me.CustomData.Split(
                    new[] { '\n' },
                    StringSplitOptions.None
                );
                bool found = false;
                for (int i = 0; i < customDataLines.Length; i++)
                {
                    if (customDataLines[i].StartsWith("Topdown:"))
                    {
                        customDataLines[i] = "Topdown:" + (isTopdownEnabled ? "true" : "false");
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var lines = new List<string>(customDataLines);
                    lines.Add("Topdown:" + (isTopdownEnabled ? "true" : "false"));
                    customDataLines = lines.ToArray();
                }
                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
            }
            private void FireSelectedBays()
            {
                var selectedBays = new StringBuilder("Firing bays: ");
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        selectedBays.Append(missileBays[i]?.CustomName + " ");
                        FireMissileFromBayWithGps(i);
                    }
                }
            }
            private void FireNextAvailableBay()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    var bay = missileBays[i];
                    if (IsBayReadyToFire(bay))
                    {
                        try
                        {
                            FireMissileFromBayWithGps(i);
                            ;
                            return;
                        }
                        catch (Exception) { }
                    }
                }
            }
            private bool IsBayReadyToFire(IMyShipMergeBlock bay)
            {
                if (bay == null)
                {
                    return false;
                }
                return bay.IsConnected;
            }
            private void ToggleBaySelection(int bayIndex)
            {
                if (bayIndex >= 0 && bayIndex < baySelected.Length)
                {
                    baySelected[bayIndex] = !baySelected[bayIndex];
                }
            }
            private void ToggleSelectedBays()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        var bay = missileBays[i];
                        if (bay == null)
                        {
                            continue;
                        }
                        bool isOn = bay.Enabled;
                        if (isOn)
                        {
                            bay.Enabled = false;
                        }
                        else
                        {
                            bay.Enabled = true;
                        }
                    }
                }
            }
            private void ExecuteBombardment()
            {
                var cachedData = ParentProgram.Me.CustomData
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(line => line.StartsWith("Cached:GPS:"));
                if (cachedData == null)
                {
                    return;
                }
                var parts = cachedData.Split(':');
                if (parts.Length < 6)
                {
                    return;
                }
                double x,
                    y,
                    z;
                if (
                    !double.TryParse(parts[3], out x)
                    || !double.TryParse(parts[4], out y)
                    || !double.TryParse(parts[5], out z)
                )
                {
                    return;
                }
                var centralTarget = new Vector3D(x, y, z);
                var bombardmentTargets = CalculateTargetPositions(centralTarget);
                int targetIndex = 0;
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i] && targetIndex < bombardmentTargets.Count)
                    {
                        var targetPosition = bombardmentTargets[targetIndex];
                        FireMissileFromBayWithGps(i, targetPosition);
                        targetIndex++;
                    }
                }
            }
            private void FireMissileFromBayWithGps(
                int bayIndex,
                Vector3D targetPosition = default(Vector3D)
            )
            {
                try
                {
                    var bay = missileBays[bayIndex];
                    if (bay == null || !bay.IsConnected)
                    {
                        return;
                    }
                    if (targetPosition.Equals(default(Vector3D)))
                    {
                        var customDataLines = ParentProgram.Me.CustomData.Split(
                            new[] { '\n' },
                            StringSplitOptions.RemoveEmptyEntries
                        );
                        var cachedData = customDataLines.FirstOrDefault(
                            line => line.StartsWith("Cached:GPS:")
                        );

                        var parts = cachedData.Split(':');

                        double x,
                            y,
                            z;
                        if (
                            !double.TryParse(parts[3], out x)
                            || !double.TryParse(parts[4], out y)
                            || !double.TryParse(parts[5], out z)
                        )
                        {
                            return;
                        }
                        targetPosition = new Vector3D(x, y, z);
                    }
                    string gpsData = string.Format(
                        "GPS:Target:{0}:{1}:{2}:#FF75C9F1:",
                        targetPosition.X,
                        targetPosition.Y,
                        targetPosition.Z
                    );
                    UpdateCustomDataWithGpsData(bayIndex, gpsData);
                    bay.ApplyAction("Fire");
                }
                catch (Exception) { }
            }
            private void UpdateCustomDataWithGpsData(int bayIndex, string gpsData)
            {
                try
                {
                    var customDataLines = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.None)
                        .ToList();
                    string cacheLabel = string.Format("Cache{0}:", bayIndex);
                    int cacheIndex = customDataLines.FindIndex(line => line.StartsWith(cacheLabel));
                    if (cacheIndex != -1)
                    {
                        customDataLines[cacheIndex] = string.Format("{0}{1}", cacheLabel, gpsData);
                    }
                    else
                    {
                        customDataLines.Add(string.Format("{0}{1}", cacheLabel, gpsData));
                    }
                    ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                }
                catch (Exception)
                {
                    throw;
                }
            }
            private void TransferCacheToSlots()
            {
                try
                {
                    var customDataLines = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.None)
                        .ToList();
                    for (int i = 0; i < customDataLines.Count; i++)
                    {
                        string cacheLabel = string.Format("Cache{0}:", i);
                        int cacheIndex = customDataLines.FindIndex(
                            line => line.StartsWith(cacheLabel)
                        );
                        if (cacheIndex != -1)
                        {
                            var cacheLine = customDataLines[cacheIndex];
                            var cacheContent = cacheLine.Substring(cacheLabel.Length).Trim();
                            if (!string.IsNullOrEmpty(cacheContent))
                            {
                                string targetLabel = string.Format("{0}:", i);
                                int targetIndex = customDataLines.FindIndex(
                                    line => line.StartsWith(targetLabel)
                                );
                                if (targetIndex != -1)
                                {
                                    customDataLines[targetIndex] = string.Format(
                                        "{0} {1}",
                                        targetLabel,
                                        cacheContent
                                    );
                                }
                                else
                                {
                                    customDataLines.Add(
                                        string.Format("{0} {1}", targetLabel, cacheContent)
                                    );
                                }
                                customDataLines[cacheIndex] = cacheLabel;
                            }
                        }
                    }
                    ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                }
                catch (Exception) { }
            }
            private List<Vector3D> CalculateTargetPositions(Vector3D centralTarget)
            {
                var targets = new List<Vector3D>();
                int selectedBayCount = baySelected.Count(b => b);

                if (selectedBayCount == 0)
                {
                    return targets;
                }

                // Define the directions for the cross: +X, -X, +Z, -Z
                Vector3D[] directions = new Vector3D[]
                {
                    new Vector3D(1, 0, 0), // +X
                    new Vector3D(-1, 0, 0), // -X
                    new Vector3D(0, 0, 1), // +Z
                    new Vector3D(0, 0, -1) // -Z
                };

                double spacing = 4.0; // Distance between each target along the axis

                // Calculate how many targets per direction
                int directionsCount = directions.Length;
                int targetsPerDirection = selectedBayCount / directionsCount;
                int remainder = selectedBayCount % directionsCount;

                for (int d = 0; d < directionsCount; d++)
                {
                    int count = targetsPerDirection + (d < remainder ? 1 : 0);
                    for (int i = 1; i <= count; i++)
                    {
                        Vector3D offset = directions[d] * (spacing * i);
                        targets.Add(centralTarget + offset);
                    }
                }

                return targets;
            }

            private static char ColorToChar(int r, int g, int b)
            {
                const double BIT_SPACING = 255.0 / 7.0;
                return (char)(
                    0xe100
                    + ((int)Math.Round(r / BIT_SPACING) << 6)
                    + ((int)Math.Round(g / BIT_SPACING) << 3)
                    + (int)Math.Round(b / BIT_SPACING)
                );
            }
            public override void HandleSpecialFunction(int key)
            {
                if (key == 5)
                {
                    FireNextAvailableBay();
                    TransferCacheToSlots();
                }
                if (key == 7)
                {
                    TransferCacheToSlots();
                }
            }
            public override string GetHotkeys()
            {
                return "5: Fire Next Available Bay\n6: Fire Selected Bays\n7: Toggle Selected Bays\n";
            }
        }

        class AirtoAir : ProgramModule
        {
            private List<IMyShipMergeBlock> missileBays = new List<IMyShipMergeBlock>();
            private bool[] baySelected;
            private List<IMySoundBlock> soundblocks = new List<IMySoundBlock>();
            private bool isAirtoAirenabled = false;
            private List<string> lastPlayedSounds = new List<string>();

            private List<int> lastSoundTickCounters = new List<int>();
            private int tickCounter = 0;
            IMyLargeTurretBase turret;
            private int ticket = 0;
            MyDetectedEntityInfo detectedEntity;
            private enum SearchDirection
            {
                Idle,
                Left,
                Right,
                Up,
                Down
            }

            private const float azimuthStep = 3f; // Degrees to move per step
            private const float elevationStep = 3f; // Degrees to move per step

            // Search pattern properties
            private SearchDirection currentSearchDirection = SearchDirection.Idle;
            private int searchTickCounter = 0;
            private const int ticksPerDirection = 1; // Adjust based on Tick frequency (e.g., 50 ticks = 5 seconds if Tick is called every 100ms)
            private const float azimuthStepRadians = 0.0436332f; // 5 degrees in radians
            private const float elevationStepRadians = 0.0436332f; // 5 degrees in radians

            // Radar constraints in radians
            private const float maxAzimuth = 0.698132f; // 40 degrees in radians
            private const float minAzimuth = -0.698132f; // -40 degrees in radians
            private const float maxElevation = 0.261799f; // 15 degrees in radians
            private const float minElevation = -0.261799f; // -15 degrees in radians

            // Current orientation tracked manually
            private float currentAzimuth = 0f; // Initialize as needed
            private float currentElevation = 0f; // Initialize as needed

            private void UpdateCustomDataWithCache(string gpsCoordinates, string cachedSpeed)
            {
                string[] customDataLines = ParentProgram.Me.CustomData.Split('\n');
                bool cachedLineFound = false;
                bool cachedSpeedFound = false;

                for (int i = 0; i < customDataLines.Length; i++)
                {
                    if (customDataLines[i].StartsWith("Cached:"))
                    {
                        customDataLines[i] = gpsCoordinates;
                        cachedLineFound = true;
                    }
                    else if (customDataLines[i].StartsWith("CachedSpeed:"))
                    {
                        customDataLines[i] = cachedSpeed;
                        cachedSpeedFound = true;
                    }
                }

                if (!cachedLineFound)
                {
                    List<string> customDataList = new List<string>(customDataLines);
                    customDataList.Add(gpsCoordinates);
                    customDataLines = customDataList.ToArray();
                }

                if (!cachedSpeedFound)
                {
                    List<string> customDataList = new List<string>(customDataLines);
                    customDataList.Add(cachedSpeed);
                    customDataLines = customDataList.ToArray();
                }

                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
            }
            public AirtoAir(Program program, Jet jet) : base(program)
            {
                // Fetch missile bays
                missileBays = jet._bays;
                baySelected = new bool[missileBays.Count];
                name = "Air To Air";
                // Fetch sound blocks
                program.GridTerminalSystem.GetBlocksOfType(
                    soundblocks,
                    b => b.CustomName.Contains("Canopy Side Plate Sound Block")
                );

                // **Initialize lastPlayedSounds with empty strings corresponding to each sound block**
                lastPlayedSounds = new List<string>();
                foreach (var block in soundblocks)
                {
                    if (block != null && block.IsFunctional)
                    {
                        lastPlayedSounds.Add(block.SelectedSound);
                    }
                    else
                    {
                        lastPlayedSounds.Add("");
                    }
                }

                // Fetch turret
                turret = jet._radar;
            }

            public static class MathHelper
            {
                public static float Clamp(float value, float min, float max)
                {
                    if (value < min)
                        return min;
                    if (value > max)
                        return max;
                    return value;
                }

                public static double Clamp(double value, double min, double max)
                {
                    if (value < min)
                        return min;
                    if (value > max)
                        return max;
                    return value;
                }

                public static int Clamp(int value, int min, int max)
                {
                    if (value < min)
                        return min;
                    if (value > max)
                        return max;
                    return value;
                }
            }

            private Vector3D CalculateDirectionVector(float azimuth, float elevation)
            {
                // Azimuth rotation around the Y-axis, elevation rotation around the X-axis
                double cosElevation = Math.Cos(elevation);
                double sinElevation = Math.Sin(elevation);
                double cosAzimuth = Math.Cos(azimuth);
                double sinAzimuth = Math.Sin(azimuth);

                // Assuming the forward direction is along the positive Z-axis
                Vector3D direction = new Vector3D(
                    cosElevation * sinAzimuth, // X component
                    sinElevation, // Y component
                    cosElevation * cosAzimuth // Z component
                );

                // Normalize the direction vector
                direction.Normalize();

                return direction;
            }

            public override string[] GetOptions()
            {
                var options = new List<string>
                {
                    "Fire Selected Bays",
                    "Toggle Selected Bays",
                    string.Format("Seeker [{0}]", isAirtoAirenabled ? "ON" : "OFF")
                };
                for (int i = 0; i < missileBays.Count; i++)
                {
                    string baySymbol = baySelected[i] ? "[X]" : "[ ]";
                    string bayStatus = missileBays[i]?.IsConnected == true ? "[ON]" : "[OFF]";
                    var mergeBlock = missileBays[i] as IMyShipMergeBlock;
                    bool isConnected = mergeBlock != null && mergeBlock.IsConnected;
                    char colorChar = ColorToChar(isConnected ? 0 : 255, isConnected ? 255 : 0, 0);
                    options.Add(
                        string.Format(
                            "{0}{1} {2} {3}",
                            colorChar,
                            baySymbol,
                            missileBays[i]?.CustomName ?? "Unknown Bay",
                            bayStatus
                        )
                    );
                }
                return options.ToArray();
            }
            public override void ExecuteOption(int index)
            {
                if (index == 2)
                {
                    ToggleSensor();
                    ToggleAirtoAirMode();
                }
                else if (index == 0)
                {
                    FireSelectedBays();
                    TransferCacheToSlots();
                }
                else if (index == 1)
                {
                    ToggleSelectedBays();
                }
                if (index > 2 && index - 3 < missileBays.Count)
                {
                    ToggleBaySelection(index - 3);
                }
            }
            private void ToggleAirtoAirMode()
            {
                isAirtoAirenabled = !isAirtoAirenabled;
                UpdateTopdownCustomData();
            }
            private void ToggleSensor()
            {
                if (turret.Enabled && isAirtoAirenabled)
                {
                    turret.Enabled = false;
                }
                else
                {
                    turret.Enabled = true;
                    turret.ShootOnce();
                }
            }
            private void UpdateTopdownCustomData()
            {
                var customDataLines = ParentProgram.Me.CustomData.Split(
                    new[] { '\n' },
                    StringSplitOptions.None
                );
                bool found = false;
                for (int i = 0; i < customDataLines.Length; i++)
                {
                    if (customDataLines[i].StartsWith("AntiAir:"))
                    {
                        customDataLines[i] = "AntiAir:" + (isAirtoAirenabled ? "true" : "false");
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var lines = new List<string>(customDataLines);
                    lines.Add("AntiAir:" + (isAirtoAirenabled ? "true" : "false"));
                    customDataLines = lines.ToArray();
                }
                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
            }

            public override void Tick()
            {
                detectedEntity = turret.GetTargetedEntity();
                ticket++;
                // Update hotkey text based on detected entity and mode
                if (isAirtoAirenabled)
                {
                    if (!detectedEntity.IsEmpty())
                    {
                        hotkeytext = $"Entity Name: {detectedEntity.Name}\n";
                    }
                    else
                    {
                        hotkeytext =
                            "5: Fire Next Available Bay\n6: Fire Selected Bays\n7: Toggle Selected Bays\n";
                        if(ticket % 16 == 0)
                        {
                            //turret.Enabled = !turret.Enabled;
                            if(turret.Enabled)
                            {
                                turret.ShootOnce();
                            }
                        }
                        if(ticket % 32 == 0)
                        {
                            // Define how many steps we want along each axis.
                            int xCount = 7;  // steps in azimuth
                            int yCount = 6;  // steps in elevation

                            // Calculate total points in one full pass.
                            int totalPoints = xCount * yCount;

                            // Current position in the overall pattern.
                            long cycle = ticket/128 % totalPoints;

                            // Determine the "row" (yIndex) and "column" (xIndex).
                            int yIndex = (int)(cycle / xCount);
                            int xIndex = (int)(cycle % xCount);

                            // Convert xIndex and yIndex into fractional positions [0..1].
                            // (xCount - 1) because e.g. 7 steps create 6 intervals, etc.
                            float fracX = (float)xIndex / (xCount - 1);
                            float fracY = (float)yIndex / (yCount - 1);

                            // Map fractions to actual turret angles:
                            //  Azimuth: from -0.7 to +0.7  => total range = 1.4
                            //  Elevation: from -0.3 to +0.3 => total range = 0.6
                            float azimuth = -0.7f + 1.4f * fracX;
                            float elevation = -0.3f + 0.6f * fracY;

                            // Now set the turret angles accordingly.
                            // E.g., MyTurret.Azimuth = azimuth;
                            //       MyTurret.Elevation = elevation;
                            turret.Azimuth = azimuth;
                            turret.Elevation = elevation;
                            turret.SyncAzimuth();
                            turret.SyncElevation();
                        }
                        
                    }
                }

                // Manage sound blocks
                for (int i = 0; i < soundblocks.Count; i++)
                {
                    var soundBlock = soundblocks[i];
                    if (soundBlock == null || !soundBlock.IsFunctional)
                    {
                        continue;
                    }

                    soundBlock.Volume = 0.3f;

                    string desiredSound = string.Empty;

                    if (isAirtoAirenabled)
                    {
                        desiredSound = !detectedEntity.IsEmpty() ? "AIM9Lock" : "AIM9Search";
                    }

                    if (lastPlayedSounds.Count <= i)
                    {
                        lastPlayedSounds.Add(string.Empty);
                        lastSoundTickCounters.Add(0);
                    }
                    if (!detectedEntity.IsEmpty())
                    {
                        var customDataLines = ParentProgram.Me.CustomData.Split(
                            new[] { '\n' },
                            StringSplitOptions.RemoveEmptyEntries
                        );
                        string gpsCoordinates =
                            "Cached:GPS:Target2:"
                            + detectedEntity.Position.X
                            + ":"
                            + detectedEntity.Position.Y
                            + ":"
                            + detectedEntity.Position.Z
                            + ":#FF75C9F1:";

                        // Add the speed information to be cached
                        string cachedSpeed =
                            "CachedSpeed:"
                            + detectedEntity.Velocity.X
                            + ":"
                            + detectedEntity.Velocity.Y
                            + ":"
                            + detectedEntity.Velocity.Z
                            + ":#FF75C9F1:";

                        UpdateCustomDataWithCache(gpsCoordinates, cachedSpeed);
                    }
                    ChangeSound(desiredSound, soundBlock, i);
                }

                tickCounter++;

                const int ticksPerLoop = 50; // 5 seconds / 0.1s per tick
                if (tickCounter >= ticksPerLoop)
                {
                    LoopSounds();
                    tickCounter = 0;
                }
            }

            private void RestartSounds()
            {
                for (int i = 0; i < soundblocks.Count; i++)
                {
                    var soundBlock = soundblocks[i];
                    string currentSound = lastPlayedSounds[i];
                    if (!string.IsNullOrEmpty(currentSound))
                    {
                        soundBlock.ApplyAction("StopSound");
                        soundBlock.ApplyAction("PlaySound");
                    }
                }
            }

            private void ChangeSound(string desiredSound, IMySoundBlock block, int index)
            {
                if (lastPlayedSounds[index] == desiredSound)
                {
                    // Same sound is already playing; no action needed
                    return;
                }

                // Stop the current sound
                block.ApplyAction("StopSound");

                if (!string.IsNullOrEmpty(desiredSound))
                {
                    // Set the new sound
                    block.SelectedSound = desiredSound;

                    // Play the new sound once
                    block.ApplyAction("PlaySound");
                }

                // Update last played sound
                lastPlayedSounds[index] = desiredSound;
            }

            private void LoopSounds()
            {
                for (int i = 0; i < soundblocks.Count; i++)
                {
                    var soundBlock = soundblocks[i];
                    string currentSound =
                        lastPlayedSounds.Count > i ? lastPlayedSounds[i] : string.Empty;

                    if (!string.IsNullOrEmpty(currentSound))
                    {
                        // Replay the sound
                        soundBlock.ApplyAction("StopSound");
                        soundBlock.ApplyAction("PlaySound");
                    }
                }
            }

            private void FireSelectedBays()
            {
                var selectedBays = new StringBuilder("Firing bays: ");
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        selectedBays.Append(missileBays[i]?.CustomName + " ");
                        FireMissileFromBayWithGps(i);
                    }
                }
            }
            private void FireNextAvailableBay()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    var bay = missileBays[i];
                    if (IsBayReadyToFire(bay))
                    {
                        try
                        {
                            FireMissileFromBayWithGps(i);
                            ;
                            return;
                        }
                        catch (Exception) { }
                    }
                }
            }
            private bool IsBayReadyToFire(IMyShipMergeBlock bay)
            {
                if (bay == null)
                {
                    return false;
                }
                return bay.IsConnected;
            }
            private void ToggleBaySelection(int bayIndex)
            {
                if (bayIndex >= 0 && bayIndex < baySelected.Length)
                {
                    baySelected[bayIndex] = !baySelected[bayIndex];
                }
            }
            private void ToggleSelectedBays()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        var bay = missileBays[i];
                        if (bay == null)
                        {
                            continue;
                        }
                        bool isOn = bay.Enabled;
                        if (isOn)
                        {
                            bay.Enabled = false;
                        }
                        else
                        {
                            bay.Enabled = true;
                        }
                    }
                }
            }
            private void ExecuteBombardment()
            {
                var cachedData = ParentProgram.Me.CustomData
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(line => line.StartsWith("Cached:GPS:"));
                if (cachedData == null)
                {
                    return;
                }
                var parts = cachedData.Split(':');
                if (parts.Length < 6)
                {
                    return;
                }
                double x,
                    y,
                    z;
                if (
                    !double.TryParse(parts[3], out x)
                    || !double.TryParse(parts[4], out y)
                    || !double.TryParse(parts[5], out z)
                )
                {
                    return;
                }
                var centralTarget = new Vector3D(x, y, z);
                var bombardmentTargets = CalculateTargetPositions(centralTarget);
                int targetIndex = 0;
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i] && targetIndex < bombardmentTargets.Count)
                    {
                        var targetPosition = bombardmentTargets[targetIndex];
                        FireMissileFromBayWithGps(i, targetPosition);
                        targetIndex++;
                    }
                }
            }
            private void FireMissileFromBayWithGps(
                int bayIndex,
                Vector3D targetPosition = default(Vector3D)
            )
            {
                try
                {
                    var bay = missileBays[bayIndex];
                    if (bay == null || !bay.IsConnected)
                    {
                        return;
                    }
                    if (targetPosition.Equals(default(Vector3D)))
                    {
                        var customDataLines = ParentProgram.Me.CustomData.Split(
                            new[] { '\n' },
                            StringSplitOptions.RemoveEmptyEntries
                        );
                        var cachedData = customDataLines.FirstOrDefault(
                            line => line.StartsWith("Cached:GPS:")
                        );
                        if (cachedData == null)
                        {
                            return;
                        }
                        var parts = cachedData.Split(':');
                        if (parts.Length < 6)
                        {
                            return;
                        }
                        double x,
                            y,
                            z;
                        if (
                            !double.TryParse(parts[3], out x)
                            || !double.TryParse(parts[4], out y)
                            || !double.TryParse(parts[5], out z)
                        )
                        {
                            return;
                        }
                        targetPosition = new Vector3D(x, y, z);
                    }
                    string gpsData = string.Format(
                        "GPS:Target:{0}:{1}:{2}:#FF75C9F1:",
                        targetPosition.X,
                        targetPosition.Y,
                        targetPosition.Z
                    );
                    UpdateCustomDataWithGpsData(bayIndex, gpsData);
                    bay.ApplyAction("Fire");
                }
                catch (Exception) { }
            }
            private void UpdateCustomDataWithGpsData(int bayIndex, string gpsData)
            {
                try
                {
                    var customDataLines = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.None)
                        .ToList();
                    string cacheLabel = string.Format("Cache{0}:", bayIndex);
                    int cacheIndex = customDataLines.FindIndex(line => line.StartsWith(cacheLabel));
                    if (cacheIndex != -1)
                    {
                        customDataLines[cacheIndex] = string.Format("{0}{1}", cacheLabel, gpsData);
                    }
                    else
                    {
                        customDataLines.Add(string.Format("{0}{1}", cacheLabel, gpsData));
                    }
                    ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                }
                catch (Exception ex)
                {
                    ParentProgram.Echo(ex.ToString());
                }
            }
            private void TransferCacheToSlots()
            {
                try
                {
                    var customDataLines = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.None)
                        .ToList();
                    for (int i = 0; i < customDataLines.Count; i++)
                    {
                        string cacheLabel = string.Format("Cache{0}:", i);
                        int cacheIndex = customDataLines.FindIndex(
                            line => line.StartsWith(cacheLabel)
                        );
                        if (cacheIndex != -1)
                        {
                            var cacheLine = customDataLines[cacheIndex];
                            var cacheContent = cacheLine.Substring(cacheLabel.Length).Trim();
                            if (!string.IsNullOrEmpty(cacheContent))
                            {
                                string targetLabel = string.Format("{0}:", i);
                                int targetIndex = customDataLines.FindIndex(
                                    line => line.StartsWith(targetLabel)
                                );
                                if (targetIndex != -1)
                                {
                                    customDataLines[targetIndex] = string.Format(
                                        "{0} {1}",
                                        targetLabel,
                                        cacheContent
                                    );
                                }
                                else
                                {
                                    customDataLines.Add(
                                        string.Format("{0} {1}", targetLabel, cacheContent)
                                    );
                                }
                                customDataLines[cacheIndex] = cacheLabel;
                            }
                        }
                    }
                    ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                }
                catch (Exception ex)
                {
                    ParentProgram.Echo(ex.ToString());
                }
            }
            private List<Vector3D> CalculateTargetPositions(Vector3D centralTarget)
            {
                var targets = new List<Vector3D>();
                int selectedBayCount = baySelected.Count(b => b);
                if (selectedBayCount == 0)
                {
                    return targets;
                }
                double radius = 4.0 * selectedBayCount;
                for (int i = 0; i < selectedBayCount; i++)
                {
                    double angle = 2 * Math.PI * i / selectedBayCount;
                    double offsetX = radius * Math.Cos(angle);
                    double offsetZ = radius * Math.Sin(angle);
                    var offset = new Vector3D(offsetX, 0, offsetZ);
                    targets.Add(centralTarget + offset);
                }
                return targets;
            }
            private int ExtractBayNumber(string name)
            {
                var parts = name.Split(' ');
                int number;
                if (parts.Length > 1 && int.TryParse(parts[1], out number))
                {
                    return number;
                }
                return -1;
            }
            private static char ColorToChar(int r, int g, int b)
            {
                const double BIT_SPACING = 255.0 / 7.0;
                return (char)(
                    0xe100
                    + ((int)Math.Round(r / BIT_SPACING) << 6)
                    + ((int)Math.Round(g / BIT_SPACING) << 3)
                    + (int)Math.Round(b / BIT_SPACING)
                );
            }
            public override void HandleSpecialFunction(int key)
            {
                if (key == 5)
                {
                    FireNextAvailableBay();
                    TransferCacheToSlots();
                }
                if (key == 7)
                {
                    TransferCacheToSlots();
                }
            }

            public string hotkeytext =
                "5: Fire Next Available Bay\n6: Fire Selected Bays\n7: Toggle Selected Bays\n";
            public override string GetHotkeys()
            {
                return hotkeytext;
            }
        }

        public static class VectorMath
        {
            public static Vector3D SafeNormalize(Vector3D a)
            {
                if (Vector3D.IsZero(a))
                    return Vector3D.Zero;
                if (Vector3D.IsUnit(ref a))
                    return a;
                return Vector3D.Normalize(a);
            }
            public static Vector3D Reflection(Vector3D a, Vector3D b, double rejectionFactor = 1)
            {
                Vector3D proj = Projection(a, b);
                Vector3D rej = a - proj;
                return proj - rej * rejectionFactor;
            }
            public static Vector3D Rejection(Vector3D a, Vector3D b)
            {
                if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                    return Vector3D.Zero;
                return a - a.Dot(b) / b.LengthSquared() * b;
            }
            public static Vector3D Projection(Vector3D a, Vector3D b)
            {
                if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                    return Vector3D.Zero;
                if (Vector3D.IsUnit(ref b))
                    return a.Dot(b) * b;
                return a.Dot(b) / b.LengthSquared() * b;
            }
            public static double ScalarProjection(Vector3D a, Vector3D b)
            {
                if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                    return 0;
                if (Vector3D.IsUnit(ref b))
                    return a.Dot(b);
                return a.Dot(b) / b.Length();
            }
            public static double AngleBetween(Vector3D a, Vector3D b)
            {
                if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                    return 0;
                else
                    return Math.Acos(
                        MathHelper.Clamp(
                            a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()),
                            -1,
                            1
                        )
                    );
            }
            public static double CosBetween(Vector3D a, Vector3D b)
            {
                if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                    return 0;
                else
                    return MathHelper.Clamp(
                        a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()),
                        -1,
                        1
                    );
            }
            public static bool IsDotProductWithinTolerance(Vector3D a, Vector3D b, double tolerance)
            {
                double dot = Vector3D.Dot(a, b);
                double num =
                    a.LengthSquared() * b.LengthSquared() * tolerance * Math.Abs(tolerance);
                return Math.Abs(dot) * dot > num;
            }
        }

        private class FroggerGameControl : ProgramModule
        {
            private UIController uiController;
            private Player player;
            private List<Obstacle> obstacles;
            private int playerScore;
            private int tickCounter;
            private bool isGameActive = false;
            private bool isPaused = false;
            private Random random = new Random();
            private const float ObstacleHeight = 5f;
            private int highestScore = 0;
            private const float PlayerSize = 5f;
            private const int SafeZoneLines = 2;
            private const int LaneHeight = 20;
            private const float PlayerSpeed = 5f;
            private int playerLives = 3;
            private int currentLevel = 1;

            public FroggerGameControl(Program program, UIController uiController) : base(program)
            {
                name = "Frogger Game";
                this.uiController = uiController;
                InitializeGame();
            }

            private void InitializeGame()
            {
                player = new Player(
                    uiController.MainScreen.SurfaceSize.X / 2,
                    uiController.MainScreen.SurfaceSize.Y - PlayerSize - 10
                );
                obstacles = new List<Obstacle>();
                playerScore = 0;
                highestScore = 0;
                currentLevel = 1;
                playerLives = 3;
                isPaused = false;
                GenerateObstacles();
                tickCounter = 0;
            }

            private void GenerateObstacles()
            {
                obstacles.Clear();
                int numberOfLanes = (int)uiController.MainScreen.SurfaceSize.Y / LaneHeight;
                float minSpeed = 1 + currentLevel * 0.5f;
                float maxSpeed = 2 + currentLevel * 0.5f;

                for (int i = SafeZoneLines; i < numberOfLanes; i++)
                {
                    float speed = random.NextFloat(minSpeed, maxSpeed);
                    speed *= (random.Next(2) == 0 ? 1 : -1);
                    int length = random.Next(10, 30);
                    Color color = GetObstacleColorBySpeed(Math.Abs(speed), playerScore);
                    obstacles.Add(
                        new Obstacle(
                            random.Next((int)uiController.MainScreen.SurfaceSize.X),
                            i * LaneHeight,
                            speed,
                            length,
                            color
                        )
                    );
                }
            }

            private Color GetObstacleColorBySpeed(float speed, int score)
            {
                int level = score / 35;
                float greenChance,
                    yellowChance;
                if (level < 1)
                {
                    greenChance = 0.7f;
                    yellowChance = 0.2f;
                }
                else if (level < 3)
                {
                    greenChance = 0.5f;
                    yellowChance = 0.3f;
                }
                else
                {
                    greenChance = 0.3f;
                    yellowChance = 0.4f;
                }

                float randomValue = (float)random.NextDouble();
                if (randomValue < greenChance)
                    return new Color(0, 255, 0);
                else if (randomValue < greenChance + yellowChance)
                    return new Color(255, 255, 0);
                else
                    return new Color(255, 0, 0);
            }

            public override string[] GetOptions() => new string[] { "Frogger Game", "Back" };
            public override void ExecuteOption(int index)
            {
                if (index == 0)
                {
                    if (isGameActive)
                    {
                        TogglePause();
                    }
                    else
                    {
                        StartGame();
                    }
                }
                else
                {
                    RenderMenu();
                }
            }

            private void StartGame()
            {
                isGameActive = true;
                InitializeGame();
            }

            private void TogglePause()
            {
                isPaused = !isPaused;
            }

            public override void HandleSpecialFunction(int key)
            {
                if (!isGameActive || isPaused)
                    return;

                switch (key)
                {
                    case 5: // Left
                        player.PositionX = Math.Max(0, player.PositionX - PlayerSpeed);
                        break;
                    case 6: // Up
                        player.PositionY = Math.Max(0, player.PositionY - PlayerSpeed);
                        playerScore += 10 * currentLevel;
                        if (playerScore > highestScore)
                            highestScore = playerScore;
                        if (player.PositionY <= 0)
                        {
                            currentLevel++;
                            GenerateObstacles();
                            player.PositionY =
                                uiController.MainScreen.SurfaceSize.Y - PlayerSize - 10;
                        }
                        break;
                    case 7: // Right
                        player.PositionX = Math.Min(
                            uiController.MainScreen.SurfaceSize.X - PlayerSize,
                            player.PositionX + PlayerSpeed
                        );
                        break;
                    case 8: // Down
                        player.PositionY = Math.Min(
                            uiController.MainScreen.SurfaceSize.Y - PlayerSize - 10,
                            player.PositionY + PlayerSpeed
                        );
                        playerScore = Math.Max(0, playerScore - 5 * currentLevel);
                        break;
                }
            }

            public override void Tick()
            {
                if (isGameActive && !isPaused)
                {
                    tickCounter++;
                    UpdateObstacles();
                    CheckCollisions();
                    RenderGame();
                }
            }

            private void UpdateObstacles()
            {
                foreach (var obstacle in obstacles)
                {
                    obstacle.PositionX += obstacle.Speed;
                    if (
                        obstacle.PositionX > uiController.MainScreen.SurfaceSize.X + obstacle.Length
                    )
                    {
                        obstacle.PositionX = -obstacle.Length;
                    }
                    else if (obstacle.PositionX < -obstacle.Length)
                    {
                        obstacle.PositionX =
                            uiController.MainScreen.SurfaceSize.X + obstacle.Length;
                    }
                }
            }

            private void CheckCollisions()
            {
                foreach (var obstacle in obstacles)
                {
                    if (IsColliding(player, obstacle))
                    {
                        playerLives--;
                        if (playerLives <= 0)
                        {
                            isGameActive = false;
                            if (playerScore > highestScore)
                                highestScore = playerScore;
                            playerScore = 0;
                            RenderMenu();
                        }
                        else
                        {
                            // Reset player position
                            player.PositionX = uiController.MainScreen.SurfaceSize.X / 2;
                            player.PositionY =
                                uiController.MainScreen.SurfaceSize.Y - PlayerSize - 10;
                        }
                        break;
                    }
                }
            }

            private bool IsColliding(Player player, Obstacle obstacle)
            {
                float playerLeft = player.PositionX;
                float playerRight = player.PositionX + PlayerSize;
                float playerTop = player.PositionY;
                float playerBottom = player.PositionY + PlayerSize;

                float obstacleLeft = obstacle.PositionX;
                float obstacleRight = obstacle.PositionX + obstacle.Length;
                float obstacleTop = obstacle.PositionY;
                float obstacleBottom = obstacle.PositionY + ObstacleHeight;

                return playerLeft < obstacleRight
                    && playerRight > obstacleLeft
                    && playerTop < obstacleBottom
                    && playerBottom > obstacleTop;
            }

            private void RenderMenu()
            {
                uiController.RenderCustomFrame(
                    (frame, area) =>
                    {
                        var menuSprite = new MySprite
                        {
                            Type = SpriteType.TEXT,
                            Data = "Frogger Game - Start",
                            Position = new Vector2(
                                uiController.MainScreen.SurfaceSize.X / 2,
                                uiController.MainScreen.SurfaceSize.Y / 2 - 20
                            ),
                            RotationOrScale = 0.8f,
                            Color = Color.White,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        };
                        frame.Add(menuSprite);

                        var backSprite = new MySprite
                        {
                            Type = SpriteType.TEXT,
                            Data = "Back",
                            Position = new Vector2(
                                uiController.MainScreen.SurfaceSize.X / 2,
                                uiController.MainScreen.SurfaceSize.Y / 2
                            ),
                            RotationOrScale = 0.8f,
                            Color = Color.White,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        };
                        frame.Add(backSprite);

                        var highScoreSprite = new MySprite
                        {
                            Type = SpriteType.TEXT,
                            Data = $"High Score: {highestScore}",
                            Position = new Vector2(
                                uiController.MainScreen.SurfaceSize.X / 2,
                                uiController.MainScreen.SurfaceSize.Y / 2 + 20
                            ),
                            RotationOrScale = 0.6f,
                            Color = Color.Yellow,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        };
                        frame.Add(highScoreSprite);

                        var latestScoreSprite = new MySprite
                        {
                            Type = SpriteType.TEXT,
                            Data = $"Latest Score: {playerScore}",
                            Position = new Vector2(
                                uiController.MainScreen.SurfaceSize.X / 2,
                                uiController.MainScreen.SurfaceSize.Y / 2 + 40
                            ),
                            RotationOrScale = 0.6f,
                            Color = Color.White,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        };
                        frame.Add(latestScoreSprite);
                    },
                    new RectangleF(Vector2.Zero, uiController.MainScreen.SurfaceSize)
                );
            }

            private void RenderGame()
            {
                uiController.RenderCustomFrame(
                    (frame, area) =>
                    {
                        DrawBackground(frame);
                        DrawVerticalLines(frame);
                        DrawPlayer(frame, player);
                        foreach (var obstacle in obstacles)
                        {
                            DrawObstacle(frame, obstacle);
                        }
                        DrawScore(frame);
                        DrawHighestScore(frame);
                        DrawGameInfo(frame);
                    },
                    new RectangleF(Vector2.Zero, uiController.MainScreen.SurfaceSize)
                );
            }

            private void DrawVerticalLines(MySpriteDrawFrame frame)
            {
                int numberOfLanes = (int)uiController.MainScreen.SurfaceSize.Y / LaneHeight;

                for (int i = SafeZoneLines; i < numberOfLanes; i++)
                {
                    var linePosition = new Vector2(
                        uiController.MainScreen.SurfaceSize.X / 2,
                        i * LaneHeight
                    );
                    var line = new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = linePosition,
                        Size = new Vector2(uiController.MainScreen.SurfaceSize.X, 12),
                        Color = new Color(255, 255, 255, 30),
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(line);
                }
            }

            private void DrawHighestScore(MySpriteDrawFrame frame)
            {
                var highestScoreSprite = new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = $"High Score: {highestScore}",
                    Position = new Vector2(
                        uiController.MainScreen.SurfaceSize.X - 10,
                        uiController.MainScreen.SurfaceSize.Y - 20
                    ),
                    RotationOrScale = 0.5f,
                    Color = Color.Yellow,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White"
                };
                frame.Add(highestScoreSprite);
            }

            private void DrawBackground(MySpriteDrawFrame frame)
            {
                var background = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Gradient",
                    Position = new Vector2(
                        uiController.MainScreen.SurfaceSize.X / 2,
                        uiController.MainScreen.SurfaceSize.Y / 2
                    ),
                    Size = uiController.MainScreen.SurfaceSize,
                    Color = new Color(0, 30, 60),
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(background);
            }

            private void DrawPlayer(MySpriteDrawFrame frame, Player player)
            {
                var playerSprite = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = new Vector2(player.PositionX, player.PositionY),
                    Size = new Vector2(PlayerSize * 2f, PlayerSize * 2f),
                    Color = new Color(0, 255, 0),
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(playerSprite);
            }

            private void DrawObstacle(MySpriteDrawFrame frame, Obstacle obstacle)
            {
                var obstacleSprite = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(obstacle.PositionX, obstacle.PositionY),
                    Size = new Vector2(obstacle.Length, ObstacleHeight),
                    Color = obstacle.Color,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(obstacleSprite);
                // Optional: Draw a trail or animation for the obstacle
            }

            private void DrawScore(MySpriteDrawFrame frame)
            {
                var scoreSprite = new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = $"Score: {playerScore}",
                    Position = new Vector2(10, uiController.MainScreen.SurfaceSize.Y - 20),
                    RotationOrScale = 0.5f,
                    Color = Color.White,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                };
                frame.Add(scoreSprite);
            }

            private void DrawGameInfo(MySpriteDrawFrame frame)
            {
                // Draw Lives
                var livesSprite = new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = $"Lives: {playerLives}",
                    Position = new Vector2(10, 10),
                    RotationOrScale = 0.5f,
                    Color = Color.White,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                };
                frame.Add(livesSprite);

                // Draw Level
                var levelSprite = new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = $"Level: {currentLevel}",
                    Position = new Vector2(uiController.MainScreen.SurfaceSize.X - 10, 10),
                    RotationOrScale = 0.5f,
                    Color = Color.White,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White"
                };
                frame.Add(levelSprite);
            }
        }

        public class Player
        {
            public float PositionX;
            public float PositionY;
            public Player(float x, float y)
            {
                PositionX = x;
                PositionY = y;
            }
        }

        public class Obstacle
        {
            public float PositionX;
            public float PositionY;
            public float Speed;
            public int Length;
            public Color Color;
            public Obstacle(float x, float y, float speed, int length, Color color)
            {
                PositionX = x;
                PositionY = y;
                Speed = speed;
                Length = length;
                Color = color;
            }
        }

        public static class NavigationHelper
        {
            public static double CalculateHeading(IMyCockpit cockpit)
            {
                if (cockpit == null) return 0;

                // 1. Define World Up direction (opposite to gravity or World Y+)
                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D worldUp;
                bool hasGravity = gravity.LengthSquared() > 1e-6; // Add tolerance

                if (hasGravity)
                {
                    worldUp = -Vector3D.Normalize(gravity);
                }
                else
                {
                    // No natural gravity: Use world Y+ as Up reference.
                    // You might adjust this based on desired zero-G compass behavior (e.g., use grid's Up).
                    worldUp = Vector3D.Up; // World Y+
                }

                // 2. Get Cockpit's Forward Direction
                Vector3D forwardVector = cockpit.WorldMatrix.Forward;

                // 3. Project Forward onto the Horizontal Plane (perpendicular to worldUp)
                Vector3D forwardHorizontal = Vector3D.Reject(forwardVector, worldUp);

                // Check if projected vector is too small (pointing nearly straight up/down)
                if (forwardHorizontal.LengthSquared() < 1e-8) // Use a small tolerance
                {
                    // Heading is undefined when looking straight up or down relative to worldUp.
                    // Return 0, or potentially previous heading, or derive from Right vector.
                    return 0;
                }
                forwardHorizontal.Normalize(); // Ensure it's a unit vector

                // 4. Define World North and East on the Horizontal Plane
                // Assumes World Z- is global North direction reference.
                Vector3D worldNorthRef = new Vector3D(0, 0, -1);

                // Project world North onto the horizontal plane
                Vector3D northHorizontal = Vector3D.Reject(worldNorthRef, worldUp);
                Vector3D eastHorizontal;
                // Handle edge case: If worldUp is aligned with worldNorthRef (e.g., at poles)
                if (northHorizontal.LengthSquared() < 1e-8)
                {
                    // North is ambiguous, use world East (X+) as primary reference instead
                    Vector3D worldEastRef = new Vector3D(1, 0, 0);
                    eastHorizontal = Vector3D.Normalize(Vector3D.Reject(worldEastRef, worldUp));
                    // Define horizontal North as 90 degrees left of horizontal East
                    northHorizontal = Vector3D.Cross(worldUp, eastHorizontal);
                    // No need to normalize northHorizontal if worldUp and eastHorizontal are unit/orthogonal
                }
                else
                {
                    northHorizontal.Normalize();
                    eastHorizontal = eastHorizontal = Vector3D.Cross(northHorizontal, worldUp);

                }


                // Horizontal East is perpendicular to horizontal North and world Up

                // eastHorizontal should already be normalized if worldUp and northHorizontal are unit/orthogonal.

                // 5. Calculate Components & Angle with Atan2
                // Get the coordinates of forwardHorizontal relative to the North/East horizontal axes
                double northComponent = Vector3D.Dot(forwardHorizontal, northHorizontal);
                double eastComponent = Vector3D.Dot(forwardHorizontal, eastHorizontal);

                // Atan2(y, x) gives the angle counter-clockwise from the positive X-axis.
                // We want the angle from North (northComponent) towards East (eastComponent).
                // So, Y = East component, X = North component.
                double headingRadians = Math.Atan2(eastComponent, northComponent);

                // 6. Convert to Degrees [0, 360)
                double headingDegrees = MathHelper.ToDegrees(headingRadians);
                if (headingDegrees < 0)
                {
                    headingDegrees += 360.0;
                }

                return headingDegrees;
            }
        }

        struct Vector2I
        {
            public int X;
            public int Y;

            public Vector2I(int x, int y)
            {
                X = x;
                Y = y;
            }
        }
    }
    public static class RandomExtensions
    {
        public static float NextFloat(this Random random, float minValue, float maxValue)
        {
            return (float)(random.NextDouble() * (maxValue - minValue) + minValue);
        }
    }
}
