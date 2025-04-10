using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
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
            // Constructor: gather all relevant blocks
            public Jet(IMyGridTerminalSystem grid)
            {
                // Find the cockpit
                _cockpit = grid.GetBlockWithName("Jet Pilot Seat") as IMyCockpit;

                // Thrusters (non-Sci-Fi, on same grid)
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
            // SOUND BLOCKS
            // ------------------------------

            /// <summary>
            /// Plays a specified sound on all Sound Block Warning blocks.
            /// </summary>
            public void PlayWarningSound(string soundName)
            {
                foreach (var soundBlock in _soundBlocks)
                {
                    soundBlock.Stop(); // Stop any currently playing
                    soundBlock.SelectedSound = soundName;
                    soundBlock.Play();
                }
            }

            /// <summary>
            /// Stops any currently playing sound on all Sound Block Warning blocks.
            /// </summary>
            public void StopWarningSounds()
            {
                foreach (var soundBlock in _soundBlocks)
                {
                    soundBlock.Stop();
                    soundBlock.SelectedSound = "";
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
            private static bool soundNeedsToPlay = false; // Track if sound is currently playing
            private static string previousSelectedSound;
            private static int soundStartTick = 0;
            private static int soundSetupStep = 0; // 0: Idle, 1: Stop, 2: Set, 3: Play, 4: Wait
            private static List<IMyLargeGatlingTurret> radars = new List<IMyLargeGatlingTurret>();
            private static Jet _myJet;
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
                        var blocks = new List<IMyTerminalBlock>();
                        parentProgram.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks);

                        // Step 1: Get X/Z bounds
                        int minX = int.MaxValue,
                            maxX = int.MinValue;
                        int minZ = int.MaxValue,
                            maxZ = int.MinValue;

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

                                frame.Add(
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
            const string cockpitName = "Jet Pilot Seat";
            const string hudName = "Fighter HUD";
            IMyGyro gyroStab;
            float rotoracceleration = 0;
            private PIDController aoaPIDController;
            float absTrimDiff;
            float currentTrim;
            List<IMyTerminalBlock> leftstab = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> rightstab = new List<IMyTerminalBlock>();
            Queue<double> velocityHistory = new Queue<double>();
            Queue<double> altitudeHistory = new Queue<double>();
            Queue<double> gForcesHistory = new Queue<double>();
            Queue<double> aoaHistory = new Queue<double>();
            Queue<Vector3D> targetpositions = new Queue<Vector3D>();
            Queue<Vector3D> targetvelocities = new Queue<Vector3D>();
            private List<IMyThrust> thrusters = new List<IMyThrust>();
            private List<IMyGasTank> tanks = new List<IMyGasTank>();
            private List<IMyDoor> airbreaks = new List<IMyDoor>();
            const int smoothingWindowSize = 10;
            double smoothedVelocity = 0;
            double smoothedAltitude = 0;
            float maxMultiplier;
            double smoothedGForces = 0;
            double smoothedAoA = 0;
            double smoothedThrottle = 0;
            double delay = 0;
            float throttlecontrol = 0f;
            bool hydrogenswitch = false;
            Vector3D previousVelocity = Vector3D.Zero;
            string hotkeytext = "Test";
            Jet myjet;
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
            private void DrawLeadingPip(
                MySpriteDrawFrame frame,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D shooterPosition,
                Vector3D shooterVelocity,
                double projectileSpeed,
                Vector3D gravityDirection
            )
            {
                // Use a fixed-size circular buffer for efficiency
                const int historySize = 5;
                CircularBuffer<Vector3D> velocityHistory = new CircularBuffer<Vector3D>(
                    historySize
                );

                velocityHistory.Enqueue(targetVelocity);

                // Calculate average acceleration
                Vector3D averageAcceleration = Vector3D.Zero;
                double deltaT = 1.0 / 60.0; // Time between ticks in seconds
                if (velocityHistory.Count >= 2)
                {
                    Vector3D velocityChange = velocityHistory.Last() - velocityHistory.First();
                    double totalDeltaT = (velocityHistory.Count - 1) * deltaT;
                    averageAcceleration = velocityChange / totalDeltaT;
                }

                // Estimate time to intercept (iterative for gravity)
                double t_pred = 0;
                Vector3D predictedTargetPosition = targetPosition;
                int iterations = 0;
                const int maxIterations = 5; // Limit iterations to prevent infinite loops
                const double tolerance = 0.1; // Acceptable error in intercept time

                do
                {
                    double distanceToTarget = (predictedTargetPosition - shooterPosition).Length();
                    t_pred = distanceToTarget / projectileSpeed;

                    predictedTargetPosition =
                        targetPosition
                        + targetVelocity * t_pred
                        + 0.5 * averageAcceleration * t_pred * t_pred;
                    iterations++;
                } while (
                    Math.Abs(
                        (predictedTargetPosition - shooterPosition).Length()
                            - projectileSpeed * t_pred
                    ) > tolerance
                    && iterations < maxIterations
                );

                Vector3D predictedTargetVelocity = targetVelocity + averageAcceleration * t_pred;

                // Calculate the intercept point using predicted values
                Vector3D interceptPoint;

                if (
                    !CalculateInterceptPoint(
                        shooterPosition,
                        shooterVelocity,
                        projectileSpeed,
                        predictedTargetPosition,
                        predictedTargetVelocity,
                        gravityDirection,
                        out interceptPoint
                    )
                )
                {
                    return;
                }

                // Calculate the direction to the intercept point
                Vector3D directionToIntercept = interceptPoint - shooterPosition;

                // Transform direction to local cockpit coordinates
                MatrixD cockpitMatrix = cockpit.WorldMatrix;
                Vector3D localDirectionToIntercept = Vector3D.TransformNormal(
                    directionToIntercept,
                    MatrixD.Transpose(cockpitMatrix)
                );
                // Define line thickness and reticle size as a fraction of the screen size
                float lineThickness = hud.SurfaceSize.Y * 0.01f; // 1% of screen height
                float reticleSize = hud.SurfaceSize.Y * 0.05f; // 5% of screen height

                // Calculate center position
                float centerX = hud.SurfaceSize.X / 2;
                float centerY = hud.SurfaceSize.Y / 2;
                Vector2 center = new Vector2(centerX, centerY);

                // Calculate the angle between the forward vector and the direction to the intercept point
                Vector3D forwardVector = new Vector3D(0, 0, -1); // Forward direction in local coordinates
                Vector3D normalizedDirection = Vector3D.Normalize(localDirectionToIntercept);
                double angleRadians = Math.Acos(Vector3D.Dot(forwardVector, normalizedDirection));
                double angleDegrees = MathHelper.ToDegrees(angleRadians);

                // Calculate screen coordinates for the intercept point
                float zoomFactor = 4f; // Increase zoom factor to make the leading pip more prominent
                float screenX =
                    (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z)
                        * hud.SurfaceSize.X
                        / 2
                        * zoomFactor
                    + hud.SurfaceSize.X / 2;
                float screenY =
                    (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z)
                        * hud.SurfaceSize.Y
                        / 2
                        * zoomFactor
                    + hud.SurfaceSize.Y / 2;

                // Check if the pip is within the screen bounds
                bool isOnScreen = (
                    screenX >= 0
                    && screenX <= hud.SurfaceSize.X
                    && screenY >= 0
                    && screenY <= hud.SurfaceSize.Y
                );
                // Check if the target is behind
                if (localDirectionToIntercept.Z >= 0)
                {
                    var horizontalLine = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX, centerY),
                        Size = new Vector2(20, 1),
                        Color = Color.White,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(horizontalLine);
                }
                else
                {
                    if (isOnScreen)
                    {
                        // Draw the leading pip as a red "X" using diagonal lines
                        float pipSize = reticleSize; // Size of the pip's diagonal lines

                        // First diagonal line (\) at the intercept point
                        var pipDiagonalLine1 = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenX, screenY),
                            Size = new Vector2(lineThickness, pipSize),
                            RotationOrScale = MathHelper.ToRadians(45),
                            Color = Color.Red,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(pipDiagonalLine1);

                        // Second diagonal line (/) at the intercept point
                        var pipDiagonalLine2 = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenX, screenY),
                            Size = new Vector2(lineThickness, pipSize),
                            RotationOrScale = MathHelper.ToRadians(-45),
                            Color = Color.Red,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(pipDiagonalLine2);

                        // Define the size of the hollow square
                        float squareSize = pipSize * 2; // Adjust as needed
                        float halfSquareSize = squareSize / 2;

                        // Top line of the hollow square
                        var topLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenX, screenY - halfSquareSize),
                            Size = new Vector2(squareSize, lineThickness),
                            Color = Color.Red,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(topLine);

                        // Bottom line of the hollow square
                        var bottomLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenX, screenY + halfSquareSize),
                            Size = new Vector2(squareSize, lineThickness),
                            Color = Color.Red,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(bottomLine);

                        // Left line of the hollow square
                        var leftLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenX - halfSquareSize, screenY),
                            Size = new Vector2(lineThickness, squareSize),
                            Color = Color.Red,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(leftLine);

                        // Right line of the hollow square
                        var rightLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenX + halfSquareSize, screenY),
                            Size = new Vector2(lineThickness, squareSize),
                            Color = Color.Red,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(rightLine);
                        // Draw the central plus sign
                        var verticalLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX, centerY),
                            Size = new Vector2(lineThickness, reticleSize * 2),
                            Color = Color.White,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(verticalLine);

                        var horizontalLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX, centerY),
                            Size = new Vector2(reticleSize * 2, lineThickness),
                            Color = Color.White,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(horizontalLine);
                    }
                    else
                    {
                        float dx = screenX - centerX;
                        float dy = screenY - centerY;
                        Vector2 direction = new Vector2(dx, dy);

                        // Normalize direction
                        if (direction.LengthSquared() > 0)
                        {
                            direction.Normalize();

                            // Adjust the arrow length based on the angle to the target
                            // Define minimum and maximum arrow lengths
                            float minArrowLength = hud.SurfaceSize.Y * 0.1f; // Minimum arrow length when looking directly at the target
                            float maxArrowLength = hud.SurfaceSize.Y * 0.4f; // Maximum arrow length when looking away from the target

                            // Clamp the angle to a reasonable range (0 to 90 degrees)
                            double clampedAngle = MathHelper.Clamp(angleDegrees, 0, 90);

                            // Map the angle to the arrow length (larger angle means longer arrow)
                            float angleFactor = (float)(clampedAngle / 90.0); // Normalized between 0 and 1
                            float arrowLength =
                                angleFactor * (maxArrowLength - minArrowLength) + minArrowLength;

                            // Compute the end point of the arrow within the screen
                            Vector2 lineEndPoint = center + direction * arrowLength;

                            // Clamp the line end point to screen boundaries
                            lineEndPoint.X = MathHelper.Clamp(lineEndPoint.X, 0, hud.SurfaceSize.X);
                            lineEndPoint.Y = MathHelper.Clamp(lineEndPoint.Y, 0, hud.SurfaceSize.Y);

                            // Draw the line from the center to the arrow end point
                            Vector2 toPoint = lineEndPoint - center;
                            float length = toPoint.Length();

                            Vector2 linePosition = center + toPoint / 2; // Midpoint of the line

                            float rotation =
                                (float)Math.Atan2(toPoint.Y, toPoint.X) + (float)Math.PI / 2;

                            // Adjust line thickness as needed
                            float lineThicknessOffScreen = hud.SurfaceSize.Y * 0.005f;

                            // Draw the arrow shaft
                            var lineSprite = new MySprite()
                            {
                                Type = SpriteType.TEXTURE,
                                Data = "SquareSimple",
                                Position = linePosition,
                                Size = new Vector2(lineThicknessOffScreen, length),
                                RotationOrScale = rotation,
                                Color = Color.Red,
                                Alignment = TextAlignment.CENTER
                            };
                            frame.Add(lineSprite);

                            // Draw an arrowhead at the end of the line
                            float arrowSize = reticleSize * 0.5f; // Adjust arrow size as needed

                            var arrowSprite = new MySprite()
                            {
                                Type = SpriteType.TEXTURE,
                                Data = "Triangle",
                                Position = lineEndPoint,
                                Size = new Vector2(arrowSize, arrowSize),
                                RotationOrScale = rotation,
                                Color = Color.Red,
                                Alignment = TextAlignment.CENTER
                            };
                            frame.Add(arrowSprite);
                        }
                    }
                }
            }

            private bool CalculateInterceptPoint(
                Vector3D shooterPosition,
                Vector3D shooterVelocity,
                double projectileSpeed,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D gravityDirection,
                out Vector3D interceptPoint
            )
            {
                // Relative position and velocity
                Vector3D relativePosition = targetPosition - shooterPosition;
                Vector3D relativeVelocity = targetVelocity - shooterVelocity - gravityDirection;

                double relativeSpeedSquared = relativeVelocity.LengthSquared();
                double projectileSpeedSquared = projectileSpeed * projectileSpeed;

                double a = relativeSpeedSquared - projectileSpeedSquared;
                double b = 2 * Vector3D.Dot(relativePosition, relativeVelocity);
                double c = relativePosition.LengthSquared();

                double discriminant = b * b - 4 * a * c;

                double t;
                if (Math.Abs(a) < 1e-6)
                {
                    // When 'a' is near zero, handle as linear equation
                    t = -c / b;
                    if (t <= 0)
                    {
                        interceptPoint = Vector3D.Zero;
                        return false;
                    }
                }
                else if (discriminant >= 0)
                {
                    // Two possible solutions, select the smallest positive time
                    double sqrtDiscriminant = Math.Sqrt(discriminant);
                    double t1 = (-b - sqrtDiscriminant) / (2 * a);
                    double t2 = (-b + sqrtDiscriminant) / (2 * a);

                    t = double.PositiveInfinity;
                    if (t1 > 0 && t1 < t)
                        t = t1;
                    if (t2 > 0 && t2 < t)
                        t = t2;

                    if (double.IsInfinity(t))
                    {
                        interceptPoint = Vector3D.Zero;
                        return false;
                    }
                }
                else
                {
                    // No real solution, cannot intercept
                    interceptPoint = Vector3D.Zero;
                    return false;
                }

                // Calculate intercept point
                interceptPoint = targetPosition + targetVelocity * t;

                return true;
            }
            // Class-level PID variables
            private float integralError = 0f;
            private float previousError = 0f;

            // PID constants
            private  float Kp = 5f;
            private  float Ki = 0.0024f;
            private  float Kd = 1f;

            // PID limits
            private const float MaxPIDOutput = 60f;
            private const float MaxAOA = 36f;

            private void AdjustStabilizers(double aoa, Jet myjet)
            {

                Vector2 pitchyaw = cockpit.RotationIndicator;
                ParentProgram.Echo("Pilot Input: " + pitchyaw.Y); //Todo: Does not work. 

                // Check if pilot input exists:
                if (Math.Abs(pitchyaw.Y) > 0)
                {
                    if(pitchyaw.Y > 0)
                    {
                        AdjustTrim(rightstab, pitchyaw.Y);
                        AdjustTrim(leftstab, -pitchyaw.Y);
                    }
                    if (pitchyaw.Y < 0)
                    {
                        AdjustTrim(rightstab, -pitchyaw.Y);
                        AdjustTrim(leftstab, pitchyaw.Y);
                    }
                    integralError = 0;
                    previousError = 0;
                }
                else
                {

                    float aoaClamped = MathHelper.Clamp((float)aoa, -MaxAOA, MaxAOA);
                    float pidOutput = PIDController(aoaClamped + myjet.offset);

                    AdjustTrim(rightstab, pidOutput);
                    AdjustTrim(leftstab, -pidOutput);

                    
                }

            }

            private float PIDController(float currentError)
            {
                var cachedData = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in cachedData)
                {
                    if (line.StartsWith("KPI:"))
                    {
                        string[] parts = line.Split(':');


                        Kp = float.Parse( parts[1]);
            Ki = float.Parse(parts[2]);
                              Kd = float.Parse(parts[3]);

                    }
                }
                    // Integral term calculation
                    integralError += currentError;
                integralError = MathHelper.Clamp(integralError,-500, 500);
                // Derivative term calculation
                float derivative = currentError - previousError;

                // PID output
                float pidOutput = (Kp * currentError) + (Ki * integralError) + (Kd * derivative);
                ParentProgram.Echo(Kp.ToString());

                // Clamp PID output
                pidOutput = MathHelper.Clamp(pidOutput, -MaxPIDOutput, MaxPIDOutput);

                previousError = currentError;

                return pidOutput;
            }

            private void AdjustTrim(IEnumerable<IMyTerminalBlock> stabilizers, float desiredTrim)
            {

                foreach (var item in stabilizers)
                {
                    currentTrim = item.GetValueFloat("Trim");

                    // Limit the adjustment per tick


                    item.SetValue("Trim", desiredTrim);

                }
            }

            public override void Tick()
            {
                if (cockpit == null || hud == null)
                {
                    return;
                }

                if (!cockpit.IsFunctional || !hudBlock.IsFunctional)
                {
                    return;
                }

                var worldMatrix = cockpit.WorldMatrix;
                var forwardVector = worldMatrix.Forward;
                var upVector = worldMatrix.Up;
                var leftVector = worldMatrix.Left;

                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D gravityDirection = Vector3D.Zero;
                bool inGravity = gravity.LengthSquared() > 0;
                if (inGravity)
                {
                    gravityDirection = Vector3D.Normalize(gravity);
                }

                double pitch = 0;
                double roll = 0;
                if (inGravity)
                {
                    pitch = (
                        Math.Asin(Vector3D.Dot(forwardVector, gravityDirection)) * (180 / Math.PI)
                    );
                    roll = (
                        Math.Atan2(
                            Vector3D.Dot(leftVector, gravityDirection),
                            Vector3D.Dot(upVector, gravityDirection)
                        ) * (180 / Math.PI)
                    );
                    if (roll < 0)
                    {
                        roll += 360;
                    }
                }

                double velocity = cockpit.GetShipSpeed();
                double velocityKnots = velocity * 1.94384;

                const double speedOfSound = 343.0; // Speed of sound in m/s at sea level
                double mach = velocity / speedOfSound; // Calculate Mach number
                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;
                double deltaTime = ParentProgram.Runtime.TimeSinceLastRun.TotalSeconds;
                if (deltaTime <= 0)
                    deltaTime = 0.0167;

                Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                double gForces = acceleration.Length() / 9.81;
                previousVelocity = currentVelocity;
                if (gForces > peakGForce)
                {
                    peakGForce = gForces;
                }
                double heading = CalculateHeading();
                double altitude = GetAltitude();
                double aoa = CalculateAngleOfAttack(
                    cockpit.WorldMatrix.Forward,
                    cockpit.GetShipVelocities().LinearVelocity, upVector
                );

                double throttle = cockpit.MoveIndicator.Z * -1; // Assuming Z-axis throttle control
                double jumpthrottle = cockpit.MoveIndicator.Y; // Assuming Z-axis throttle control
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
                using (var frame = hud.DrawFrame())
                {
                    DrawArtificialHorizon(frame, (float)pitch, (float)roll);
                    //DrawHeadingTape(frame, heading); //No clue what it does
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
                    DrawFlightPathMarker(frame, currentVelocity, worldMatrix, roll);
                    DrawCompass(frame, heading);
                    //DrawRollIndicator(frame, (float)roll);
                    DrawAirspeedIndicator(frame, smoothedVelocity);
                    DrawAltitudeIndicator(frame, smoothedAltitude);
                    DrawAOABracket(frame, aoa);
                    DrawTrim(frame, aoa);
                    DrawBomb(frame, aoa);
                    DrawGForceIndicator(frame, gForces, peakGForce);
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
                    double tarx,
                        tary,
                        tarz;
                    if (
                        !double.TryParse(parts[3], out tarx)
                        || !double.TryParse(parts[4], out tary)
                        || !double.TryParse(parts[5], out tarz)
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
                    Vector3D targetVelocity = new Vector3D(speex, speey, speez); // Replace with actual target speed
                    Vector3D shooterPosition = cockpit.GetPosition(); // Your ship's current position
                    double muzzleVelocity = 910; // Muzzle velocity of your weapon in m/s
                    // Compute the projectile's initial velocity
                    Vector3D shooterForwardDirection = cockpit.WorldMatrix.Forward;
                    Vector3D projectileInitialVelocity =
                        currentVelocity + muzzleVelocity * shooterForwardDirection;

                    // Call the DrawLeadingPip function
                    DrawLeadingPip(
                        frame,
                        targetPosition,
                        targetVelocity,
                        shooterPosition,
                        currentVelocity,
                        muzzleVelocity,
                        gravityDirection
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
                    Color = Color.White,
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
                    Color = Color.White,
                    Alignment = TextAlignment.LEFT,
                    FontId = "White"
                };
                frame.Add(peakGSprite);
            }
            private double smoothAirspeed = 0;

            private void DrawAirspeedIndicator(MySpriteDrawFrame frame, double airspeed)
            {
                float scaleX = 60f;
                float centerY = hud.SurfaceSize.Y / 2;
                float scaleHeight = hud.SurfaceSize.Y * 0.7f;
                float pixelsPerUnit = scaleHeight / 200f;

                // Smooth the airspeed
                smoothAirspeed += (airspeed - smoothAirspeed) * 0.1;
                int airspeedRounded = (int)Math.Round(smoothAirspeed);

                // Current speed indicator box
                var speedBox = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareHollow",
                    Position = new Vector2(scaleX - 20f, centerY - 20f),
                    Size = new Vector2(80f, 30f),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(speedBox);

                // Current airspeed
                var airspeedLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = airspeedRounded.ToString(),
                    Position = new Vector2(scaleX - 20f, centerY - 30f),
                    RotationOrScale = 0.8f,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                };
                frame.Add(airspeedLabel);
            }

            private void DrawAltitudeIndicator(MySpriteDrawFrame frame, double currentAltitude)
            {
                // Calculate total altitude change over the tick window
                double totalAltitudeChange = 0;
                if (altitudeHistory.Count > 1)
                {
                    double oldestAltitude = altitudeHistory.Peek();
                    totalAltitudeChange = currentAltitude - oldestAltitude;
                }

                // Display on HUD
                float scaleX = hud.SurfaceSize.X - 120f;
                float centerY = hud.SurfaceSize.Y / 2;

                // Draw current altitude
                string altitudeText = currentAltitude.ToString("F0");
                var altitudeLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = altitudeText,
                    Position = new Vector2(scaleX, centerY),
                    RotationOrScale = 1f,
                    Color = Color.White,
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

                altitudeHistory.Enqueue(altitude);
                if (altitudeHistory.Count > smoothingWindowSize)
                {
                    altitudeHistory.Dequeue();
                }
                smoothedAltitude = altitudeHistory.Average();

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

                // Now draw each piece of information with a box around it
                DrawTextWithManualBox(
                    frame,
                    surface,
                    $"M {mach:F2}",
                    new Vector2(padding, infoY + textHeight),
                    TextAlignment.LEFT,
                    boxPadding,
                    maxWidth,
                    textScale
                );
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
                        Color = Color.White,
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
                barColor = barColor == default(Color) ? Color.White : barColor;
                boxColor = boxColor == default(Color) ? Color.White : boxColor;

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
                barColor = throttle > 0.8f ? Color.Yellow : Color.White;

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
                for (int i = 0; i <= numberOfTicks; i++)
                {
                    // Calculate the position for each tick
                    float percent = i / (float)numberOfTicks;
                    float xPosition = boxTopLeft.X + (percent * boxSize.X); // Adjust based on your coordinate system
                    Vector2 filledSizetick = new Vector2(
                        maxWidth * 100,
                        barHeight * percent * boxSize.Y * 1.25f
                    );

                    // Depending on the orientation of your throttle bar, adjust the tick position
                    // Here, assuming horizontal throttle bar
                    frame.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "----", // Tick mark
                            Position =
                                boxTopLeft
                                + new Vector2(
                                    0,
                                    (
                                        boxSize.Y
                                        - boxPadding / 33
                                        - lineThickness / 2
                                        - filledSizetick.Y
                                    ) - 3
                                ),
                            FontId = "Debug", // Use an appropriate font
                            RotationOrScale = 0.2f,
                            Color = tickColor
                        }
                    );
                }
            }

            private void DrawFlightPathMarker(
                MySpriteDrawFrame frame,
                Vector3D currentVelocity,
                MatrixD worldMatrix,
                double roll
            )
            {
                // Constants for degree-radian conversion and marker properties
                const double DegToRad = Math.PI / 180.0;
                const double RadToDeg = 180.0 / Math.PI;
                const float MarkerSize = 20f;
                const float WingLength = 15f;
                const float WingThickness = 2f;
                const float WingOffsetX = 10f; // Additional offset along X-axis

                // Normalize current velocity
                Vector3D velocityDirection = Vector3D.Normalize(currentVelocity);

                // Transform velocity into local coordinates
                Vector3D localVelocity = Vector3D.TransformNormal(
                    velocityDirection,
                    MatrixD.Transpose(worldMatrix)
                );

                // Compute yaw and pitch from local velocity (in degrees)
                double velocityYaw = Math.Atan2(localVelocity.X, -localVelocity.Z) * RadToDeg;
                double velocityPitch = Math.Atan2(localVelocity.Y, -localVelocity.Z) * RadToDeg;

                // Convert degrees to pixels based on HUD size
                float pixelsPerDegreeX = hud.SurfaceSize.X / 60f; // Adjust as needed
                float pixelsPerDegreeY = hud.SurfaceSize.Y / 45f; // Adjust as needed

                // Calculate marker offset in pixels
                Vector2 markerOffset = new Vector2(
                    (float)(-velocityYaw * pixelsPerDegreeX),
                    (float)(velocityPitch * pixelsPerDegreeY)
                );

                // Convert roll to radians
                float rollRad = (float)(roll * DegToRad);

                // Rotate the marker offset by negative roll angle
                Vector2 rotatedOffset = RotatePoint(markerOffset, Vector2.Zero, -rollRad);

                // Determine the final marker position
                Vector2 hudCenter = hud.SurfaceSize / 2f;
                Vector2 markerPosition = hudCenter + rotatedOffset;

                // Draw the Flight Path Marker (circle)
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

                // Wing offsets before rotation
                Vector2 leftWingOffset = new Vector2(-WingLength / 2 - WingOffsetX, 0f);
                Vector2 rightWingOffset = new Vector2(WingLength / 2 + WingOffsetX, 0f);

                // Rotate wing offsets
                Vector2 rotatedLeftWingOffset = RotatePoint(leftWingOffset, Vector2.Zero, -rollRad);
                Vector2 rotatedRightWingOffset = RotatePoint(
                    rightWingOffset,
                    Vector2.Zero,
                    -rollRad
                );

                // Create and add left wing sprite
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

                // Create and add right wing sprite
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

            private double CalculateAngleOfAttack(Vector3D forwardVector, Vector3D velocity, Vector3D upVector)
            {





                    if (velocity.LengthSquared() < 0.01) return 0;


                    Vector3D velocityDirection = Vector3D.Normalize(velocity);





                    // Calculate the angle between the velocity and the forward vector in the plane defined by the up vector


                    double angleOfAttack = Math.Atan2(


                        Vector3D.Dot(velocityDirection, upVector),


                        Vector3D.Dot(velocityDirection, forwardVector)


                    ) * (180 / Math.PI);





                    return angleOfAttack;


                
            }

            private void DrawArtificialHorizon(MySpriteDrawFrame frame, float pitch, float roll)
            {
                float centerX = hud.SurfaceSize.X / 2;
                float centerY = hud.SurfaceSize.Y / 2;
                float pixelsPerDegree = hud.SurfaceSize.Y / 40f; // F18-like scaling

                List<MySprite> sprites = new List<MySprite>();

                // Pitch ladder
                for (int i = -90; i <= 90; i += 5)
                {
                    float markerY = centerY - (i - pitch) * pixelsPerDegree;
                    if (markerY < -100 || markerY > hud.SurfaceSize.Y + 100)
                        continue;

                    bool majorLine = i % 10 == 0;
                    float lineWidth = majorLine ? 150f : 75f;
                    float lineThickness = majorLine ? 3f : 1f;
                    Color lineColor = majorLine ? Color.Lime : Color.Gray;

                    // Pitch ladder line
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX, markerY),
                            Size = new Vector2(lineWidth, lineThickness),
                            Color = lineColor,
                            Alignment = TextAlignment.CENTER
                        }
                    );

                    // Labels for major lines
                    if (majorLine && i != 0)
                    {
                        string label = Math.Abs(i).ToString();

                        sprites.Add(
                            new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = label,
                                Position = new Vector2(centerX - lineWidth / 2 - 30, markerY - 12),
                                RotationOrScale = 0.6f,
                                Color = lineColor,
                                Alignment = TextAlignment.RIGHT,
                                FontId = "White"
                            }
                        );

                        sprites.Add(
                            new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = label,
                                Position = new Vector2(centerX + lineWidth / 2 + 30, markerY - 12),
                                RotationOrScale = 0.6f,
                                Color = lineColor,
                                Alignment = TextAlignment.LEFT,
                                FontId = "White"
                            }
                        );
                    }
                }

                // Distinct Horizon line
                float horizonY = centerY + pitch * pixelsPerDegree;
                // Distinct Horizon line
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX, horizonY),
                        Size = new Vector2(hud.SurfaceSize.X, 4f),
                        Color = Color.White,
                        Alignment = TextAlignment.CENTER
                    }
                );

                // F18-style center marker (-^-)
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

                // Apply roll rotation
                float rollRad = MathHelper.ToRadians(-roll);
                float cosRoll = (float)Math.Cos(rollRad);
                float sinRoll = (float)Math.Sin(rollRad);

                for (int i = 0; i < sprites.Count; i++)
                {
                    MySprite sprite = sprites[i];
                    Vector2 offset =
                        (sprite.Position ?? Vector2.Zero) - new Vector2(centerX, centerY);
                    sprite.Position = new Vector2(
                        offset.X * cosRoll - offset.Y * sinRoll + centerX,
                        offset.X * sinRoll + offset.Y * cosRoll + centerY
                    );

                    if (sprite.Type == SpriteType.TEXTURE)
                        sprite.RotationOrScale = rollRad;

                    sprites[i] = sprite;
                    frame.Add(sprite);
                }
            }

            private void DrawCompass(MySpriteDrawFrame frame, double heading)
            {
                float centerX = hud.SurfaceSize.X / 2;
                float compassY = 40f;
                float compassWidth = hud.SurfaceSize.X * 0.9f;
                float compassHeight = 40f;

                float headingScale = compassWidth / 90f; // Pixels per degree over a 90-degree field of view

                // Background for the compass
                var compassBg = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(centerX, compassY),
                    Size = new Vector2(compassWidth, compassHeight),
                    Color = new Color(0, 0, 0, 150), // Semi-transparent black
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(compassBg);

                // Loop over all compass headings in 5-degree increments
                for (int markerHeading = 0; markerHeading < 360; markerHeading += 5)
                {
                    double deltaHeading = ((markerHeading - heading + 540) % 360) - 180;

                    if (deltaHeading >= -45 && deltaHeading <= 45)
                    {
                        float markerX = centerX + (float)deltaHeading * headingScale;
                        float markerHeight =
                            (markerHeading % 10 == 0) ? compassHeight * 0.6f : compassHeight * 0.4f;
                        Color markerColor = (markerHeading % 90 == 0) ? Color.Cyan : Color.White;

                        // Draw the marker line
                        var markerLine = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(markerX, compassY),
                            Size = new Vector2(2, markerHeight),
                            Color = markerColor,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(markerLine);

                        // Draw text for major markers
                        if (markerHeading % 10 == 0)
                        {
                            string label =
                                (markerHeading % 90 == 0)
                                    ? GetCompassDirection(markerHeading)
                                    : markerHeading.ToString();
                            Color textColor = (markerHeading % 90 == 0) ? Color.Cyan : Color.White;

                            var markerText = new MySprite()
                            {
                                Type = SpriteType.TEXT,
                                Data = label,
                                Position = new Vector2(markerX, compassY + compassHeight / 2),
                                RotationOrScale = 0.8f,
                                Color = textColor,
                                Alignment = TextAlignment.CENTER,
                                FontId = "White"
                            };
                            frame.Add(markerText);
                        }
                    }
                }

                // Add the heading indicator (Yellow Triangle)
                var headingIndicator = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Triangle",
                    Position = new Vector2(centerX, compassY - 20),
                    Size = new Vector2(14, 10), // Slightly larger for better visibility
                    Color = Color.Yellow,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = (float)Math.PI
                };
                frame.Add(headingIndicator);
            }

            private string GetCompassDirection(double heading)
            {
                if (heading >= 337.5 || heading < 22.5)
                    return "N";
                else if (heading >= 22.5 && heading < 67.5)
                    return "NE";
                else if (heading >= 67.5 && heading < 112.5)
                    return "E";
                else if (heading >= 112.5 && heading < 157.5)
                    return "SE";
                else if (heading >= 157.5 && heading < 202.5)
                    return "S";
                else if (heading >= 202.5 && heading < 247.5)
                    return "SW";
                else if (heading >= 247.5 && heading < 292.5)
                    return "W";
                else
                    return "NW";
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
                if (cockpit == null || cockpit == null)
                    return 0;

                Vector3D forwardVector = cockpit.WorldMatrix.Forward;
                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D gravityDir =
                    gravity.LengthSquared() == 0
                        ? cockpit.WorldMatrix.Down
                        : Vector3D.Normalize(gravity);

                // Project forwardVector onto the horizontal plane defined by gravityDir
                Vector3D forwardProjected = Vector3D.Reject(forwardVector, gravityDir);

                if (forwardProjected.LengthSquared() == 0)
                    return 0;

                forwardProjected = Vector3D.Normalize(forwardProjected);

                Vector3D northVector = new Vector3D(0, 0, -1); // Assuming Z- is North
                double headingRadians = Math.Acos(
                    MathHelper.Clamp(Vector3D.Dot(northVector, forwardProjected), -1.0, 1.0)
                );

                // Determine the sign of the heading
                if (Vector3D.Dot(Vector3D.Cross(northVector, forwardProjected), gravityDir) < 0)
                    headingRadians = -headingRadians;

                double headingDegrees = MathHelper.ToDegrees(headingRadians);
                if (headingDegrees < 0)
                    headingDegrees += 360;
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
