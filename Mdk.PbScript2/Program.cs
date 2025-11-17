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
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            try
            {
                SystemManager.Main(argument, updateSource);
            }
            catch (NullReferenceException e)
            {
                // Log the error for debugging
                Echo($"NullRef Error: {e.Message}");
                Echo($"Stack: {e.StackTrace}");
                // Reinitialize to recover from missing blocks
                SystemManager.Initialize(this);
            }
            catch (Exception e)
            {
                // Log unexpected errors but don't hide them
                Echo($"CRITICAL ERROR: {e.GetType().Name}");
                Echo($"Message: {e.Message}");
                Echo($"Stack: {e.StackTrace}");
                // Don't automatically reinitialize on unexpected errors
                // This helps identify bugs during development
            }
        }

        // Helper function for finding slot for new target
        public static int FindEmptyOrOldestSlot(Jet jet)
        {
            // First pass: find empty slot
            for (int i = 0; i < jet.targetSlots.Length; i++)
            {
                if (!jet.targetSlots[i].IsOccupied)
                {
                    return i;
                }
            }

            // Second pass: all slots occupied, find oldest by timestamp
            int oldestIndex = 0;
            long oldestTimestamp = jet.targetSlots[0].TimestampTicks;

            for (int i = 1; i < jet.targetSlots.Length; i++)
            {
                if (jet.targetSlots[i].TimestampTicks < oldestTimestamp)
                {
                    oldestTimestamp = jet.targetSlots[i].TimestampTicks;
                    oldestIndex = i;
                }
            }

            return oldestIndex;
        }

        public class Jet
        {
            // Core blocks
            public IMyCockpit _cockpit;
            public List<IMyThrust> _thrusters;
            public List<IMyThrust> _thrustersbackwards;
            public List<IMySoundBlock> _soundBlocks;
            public IMyFlightMovementBlock _aiFlightBlock;
            public IMyOffensiveCombatBlock _aiCombatBlock;
            public IMyFlightMovementBlock _aiFlightBlock2;  // RWR radar
            public IMyOffensiveCombatBlock _aiCombatBlock2; // RWR radar

            // Multi-target tracking system - unified slot structure
            public struct TargetSlot
            {
                public bool IsOccupied;
                public Vector3D Position;
                public Vector3D Velocity;
                public string Name;
                public long TimestampTicks;

                public TargetSlot(Vector3D pos, Vector3D vel, string name)
                {
                    IsOccupied = true;
                    Position = pos;
                    Velocity = vel;
                    Name = name;
                    TimestampTicks = DateTime.Now.Ticks;
                }

                public void Clear()
                {
                    IsOccupied = false;
                    Position = Vector3D.Zero;
                    Velocity = Vector3D.Zero;
                    Name = "";
                    TimestampTicks = 0;
                }
            }

            public TargetSlot[] targetSlots = new TargetSlot[5];  // 5 target slots (0-4)
            public int activeSlotIndex = 0;                        // Currently selected slot

            // Enemy contact tracking with decay
            public struct EnemyContact
            {
                public Vector3D Position;
                public Vector3D Velocity;
                public string Name;
                public long LastSeenTicks;
                public int SourceIndex;  // Which AI combo detected this (0=primary, 1=RWR, 2=third combo, etc.)

                public EnemyContact(Vector3D pos, Vector3D vel, string name, int source)
                {
                    Position = pos;
                    Velocity = vel;
                    Name = name;
                    LastSeenTicks = DateTime.Now.Ticks;
                    SourceIndex = source;
                }

                public long AgeTicks()
                {
                    return DateTime.Now.Ticks - LastSeenTicks;
                }

                public double AgeSeconds()
                {
                    return (DateTime.Now.Ticks - LastSeenTicks) / (double)TimeSpan.TicksPerSecond;
                }
            }

            public List<EnemyContact> enemyList = new List<EnemyContact>();
            public const long CONTACT_DECAY_TICKS = 180 * TimeSpan.TicksPerSecond; // 3 minute decay

            // Scalable radar tracking modules
            public List<RadarTrackingModule> radarModules = new List<RadarTrackingModule>();

            // RWR AI block pairs (stored in reverse order for priority assignment)
            public struct AIBlockPair
            {
                public IMyFlightMovementBlock FlightBlock;
                public IMyOffensiveCombatBlock CombatBlock;
                public int Index; // Original index (1-99)

                public AIBlockPair(IMyFlightMovementBlock flight, IMyOffensiveCombatBlock combat, int idx)
                {
                    FlightBlock = flight;
                    CombatBlock = combat;
                    Index = idx;
                }
            }
            public List<AIBlockPair> rwrAIBlocks = new List<AIBlockPair>();

            public List<IMyShipMergeBlock> _bays;
            public List<IMyTerminalBlock> leftstab = new List<IMyTerminalBlock>();
            public List<IMyTerminalBlock> rightstab = new List<IMyTerminalBlock>();
            public IMyTerminalBlock hudBlock;
            public IMyTextSurface hud;
            public List<IMyGasTank> tanks = new List<IMyGasTank>();
            public int offset = 0;
            public bool manualfire = true; // Set to true if you want to fire the guns manually, false if you want to use the radar system
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
                    t => t.CubeGrid == _cockpit.CubeGrid && !t.CustomName.Contains("Industrial")
                );

                // Sound blocks with "Sound Block Warning" in name
                _soundBlocks = new List<IMySoundBlock>();
                grid.GetBlocksOfType(
                    _soundBlocks,
                    s => s.CustomName.Contains("Sound Block Warning")
                );

                // AI blocks for radar tracking (targeting) - now scalable
                // Try to find: "AI Flight", "AI Flight 2", "AI Flight 3", etc.
                // and matching: "AI Combat", "AI Combat 2", "AI Combat 3", etc.
                _aiFlightBlock = grid.GetBlockWithName("AI Flight") as IMyFlightMovementBlock;
                _aiCombatBlock = grid.GetBlockWithName("AI Combat") as IMyOffensiveCombatBlock;

                // Second AI blocks for RWR (Radar Warning Receiver) - backward compatibility
                _aiFlightBlock2 = grid.GetBlockWithName("AI Flight 2") as IMyFlightMovementBlock;
                _aiCombatBlock2 = grid.GetBlockWithName("AI Combat 2") as IMyOffensiveCombatBlock;

                // Auto-detect all AI Flight/Combat pairs for RWR system
                // Scan from 1 to 99 and store in reverse order (highest numbers first for RWR priority)
                List<AIBlockPair> detectedPairs = new List<AIBlockPair>();

                for (int i = 1; i <= 99; i++) // Check up to 99 AI combos
                {
                    string flightName = i == 1 ? "AI Flight" : $"AI Flight {i}";
                    string combatName = i == 1 ? "AI Combat" : $"AI Combat {i}";

                    var flightBlock = grid.GetBlockWithName(flightName) as IMyFlightMovementBlock;
                    var combatBlock = grid.GetBlockWithName(combatName) as IMyOffensiveCombatBlock;

                    if (flightBlock != null && combatBlock != null)
                    {
                        detectedPairs.Add(new AIBlockPair(flightBlock, combatBlock, i));
                    }
                }

                // Reverse the list so highest-numbered blocks come first (RWR priority)
                detectedPairs.Reverse();
                rwrAIBlocks = detectedPairs;

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
                        && !g.CustomName.Contains("Industrial")
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
            // ENEMY CONTACT MANAGEMENT
            // ------------------------------

            /// <summary>
            /// Updates or adds an enemy contact to the enemy list
            /// </summary>
            public void UpdateOrAddEnemy(Vector3D pos, Vector3D vel, string name, int sourceIndex)
            {
                // Try to find existing contact by name
                int existingIndex = -1;
                for (int i = 0; i < enemyList.Count; i++)
                {
                    if (enemyList[i].Name == name)
                    {
                        existingIndex = i;
                        break;
                    }
                }

                EnemyContact contact = new EnemyContact(pos, vel, name, sourceIndex);

                if (existingIndex >= 0)
                {
                    // Update existing contact
                    enemyList[existingIndex] = contact;
                }
                else
                {
                    // Add new contact
                    enemyList.Add(contact);
                }
            }

            /// <summary>
            /// Removes contacts older than CONTACT_DECAY_TICKS (3 minutes)
            /// </summary>
            public void UpdateEnemyDecay()
            {
                long currentTicks = DateTime.Now.Ticks;
                for (int i = enemyList.Count - 1; i >= 0; i--)
                {
                    if ((currentTicks - enemyList[i].LastSeenTicks) > CONTACT_DECAY_TICKS)
                    {
                        enemyList.RemoveAt(i);
                    }
                }
            }

            /// <summary>
            /// Gets the N closest enemies sorted by distance from cockpit
            /// </summary>
            public List<EnemyContact> GetClosestNEnemies(int n)
            {
                if (_cockpit == null || enemyList.Count == 0)
                    return new List<EnemyContact>();

                Vector3D cockpitPos = GetCockpitPosition();

                // Create list with distances
                var enemiesWithDistance = new List<KeyValuePair<double, EnemyContact>>();
                foreach (var enemy in enemyList)
                {
                    double distance = (enemy.Position - cockpitPos).Length();
                    enemiesWithDistance.Add(new KeyValuePair<double, EnemyContact>(distance, enemy));
                }

                // Sort by distance
                enemiesWithDistance.Sort((a, b) => a.Key.CompareTo(b.Key));

                // Take top N
                var result = new List<EnemyContact>();
                int count = Math.Min(n, enemiesWithDistance.Count);
                for (int i = 0; i < count; i++)
                {
                    result.Add(enemiesWithDistance[i].Value);
                }

                return result;
            }

            /// <summary>
            /// Gets a color for enemy contacts based on age (for HUD decay visualization)
            /// </summary>
            public Color GetEnemyContactColor(EnemyContact contact)
            {
                double ageSeconds = contact.AgeSeconds();

                if (ageSeconds < 30)
                {
                    // Fresh: Bright red
                    return new Color(255, 0, 0);
                }
                else if (ageSeconds < 60)
                {
                    // Recent: Orange
                    return new Color(255, 165, 0);
                }
                else
                {
                    // Old: Yellow
                    return new Color(255, 255, 0);
                }
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
            // AI RADAR BLOCKS
            // ------------------------------

            /// <summary>
            /// Enables or disables the AI Combat Block for radar tracking.
            /// </summary>
            public void SetAIRadarEnabled(bool enabled)
            {
                if (_aiCombatBlock != null)
                    _aiCombatBlock.Enabled = enabled;
            }
        }

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
            private static Program.RWRModule rwrModule;
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
                else { }
                program.GridTerminalSystem.GetBlocksOfType(
                    soundblocks,
                    b => b.CustomName.Contains("Sound Block Warning")
                );

                thrusters = _myJet._thrusters;
                parentProgram = program;
                modules = new List<ProgramModule>();
                modules.Add(new AirToGround(parentProgram, _myJet));
                airtoAirModule = new AirtoAir(parentProgram, _myJet); // Store reference for continuous scanning
                modules.Add(airtoAirModule);
                rwrModule = new RWRModule(parentProgram, _myJet);  // Radar Warning Receiver
                modules.Add(rwrModule);

                raycastProgram = new RaycastCameraControl(parentProgram, _myJet);
                hudProgram = new HUDModule(parentProgram, _myJet, lcdWeapons, rwrModule);
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

                // Always run passive radar scanning (even in main menu)
                // This builds the enemy contact list for situational awareness
                // Only call if NOT already the active module (to avoid double-tick)
                if (airtoAirModule != null && currentModule != airtoAirModule)
                {
                    // Call tick to run passive scanning logic
                    // The AirtoAir.Tick() method handles passive vs active mode internally
                    airtoAirModule.Tick();
                }

                // Always run RWR threat detection (even in main menu)
                // This provides continuous background threat warnings
                // Only call if NOT already the active module (to avoid double-tick)
                if (rwrModule != null && currentModule != rwrModule)
                {
                    // Call tick to run threat detection
                    // The RWR.Tick() method handles enabled/disabled state internally
                    rwrModule.Tick();
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

                        List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                        parentProgram.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks);
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
                            if (rwrModule != null && rwrModule.IsEnabled)
                            {
                                if (rwrModule.IsThreat)
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
                string gpsCoordinates =
                    "Cached:GPS:Target:"
                    + targetPos.X + ":"
                    + targetPos.Y + ":"
                    + targetPos.Z + ":#FF75C9F1:";

                // Format cached speed
                string cachedSpeed =
                    "CachedSpeed:"
                    + targetVel.X + ":"
                    + targetVel.Y + ":"
                    + targetVel.Z + ":#FF75C9F1:";

                // Update CustomData
                var customDataLines = parentProgram.Me.CustomData.Split('\n');
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

                parentProgram.Me.CustomData = string.Join("\n", customDataLines);
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
                string navigationInstructions,
                int scrollOffset = 0
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

                // Calculate available height for content area
                float availableContentHeight = mainViewport.Height - CONTENT_PADDING_TOP - titlePaddingTop - NAVIGATION_INSTRUCTIONS_HEIGHT - 60;

                // Calculate individual option heights
                float[] optionHeights = new float[options.Length];
                for (int i = 0; i < options.Length; i++)
                {
                    int lineCount = options[i].Split(new[] { '\n' }, StringSplitOptions.None).Length;
                    optionHeights[i] = lineCount * OPTION_HEIGHT * OPTION_SCALE;
                }

                // Position content with padding
                var contentPosition = new Vector2(0, CONTENT_PADDING_TOP + titlePaddingTop);
                var contentSize = new Vector2(mainViewport.Width, availableContentHeight);
                var container = new UIContainer(contentPosition, contentSize)
                {
                    BorderColor = BORDER_COLOR,
                    BorderThickness = BORDER_THICKNESS,
                    Padding = new Vector2(PADDING_TOP, 5)
                };

                // Add options to the container with scrolling support
                float currentY = -scrollOffset * OPTION_HEIGHT * OPTION_SCALE;
                for (int i = 0; i < options.Length; i++)
                {
                    string option = options[i];
                    int lineCount = option.Split(new[] { '\n' }, StringSplitOptions.None).Length;
                    float optionHeight = lineCount * OPTION_HEIGHT;

                    // Only render options that are visible in the viewport
                    if (currentY + optionHeights[i] >= -10 && currentY <= availableContentHeight + 10)
                    {
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
                    }
                    currentY += optionHeights[i];
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
            // Return true if module handles navigation internally, false to use default
            public virtual bool HandleNavigation(bool isUp)
            {
                return false; // Default: don't override navigation
            }
            // Return true if module handles back button internally, false to use default (exit module)
            public virtual bool HandleBack()
            {
                return false; // Default: exit module
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
            private Jet myJet;
            public RaycastCameraControl(Program program, Jet jet) : base(program)
            {
                name = "TargetingPod Control";
                myJet = jet;
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
                        Vector3D targetVelocity = hitInfo.Velocity;

                        // Find first empty slot or overwrite oldest
                        int slotIndex = Program.FindEmptyOrOldestSlot(myJet);

                        // Store target in slot (don't auto-activate, user cycles manually)
                        myJet.targetSlots[slotIndex] = new Jet.TargetSlot(target, targetVelocity, "Raycast");

                        // Add to enemy contact list (source index -1 indicates raycast)
                        myJet.UpdateOrAddEnemy(target, targetVelocity, "Raycast", -1);

                        string gpsCoordinates =
                            "Cached:GPS:Target:"
                            + target.X
                            + ":"
                            + target.Y
                            + ":"
                            + target.Z
                            + ":#FF75C9F1:";

                        // Capture target velocity for motion compensation
                        string cachedSpeed =
                            "CachedSpeed:"
                            + targetVelocity.X
                            + ":"
                            + targetVelocity.Y
                            + ":"
                            + targetVelocity.Z
                            + ":#FF75C9F1:";

                        UpdateCustomDataWithCache(gpsCoordinates, cachedSpeed);
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
                SystemManager.MarkCustomDataDirty();
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
                    // Tracking uses active slot - no separate local variable needed
                    trackingActive = myJet.targetSlots[myJet.activeSlotIndex].IsOccupied;
                    if (trackingActive)
                    {
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

                // Get active target position from Jet slot
                if (!myJet.targetSlots[myJet.activeSlotIndex].IsOccupied)
                {
                    return;  // No target to track
                }

                Vector3D targetPosition = myJet.targetSlots[myJet.activeSlotIndex].Position;

                Vector3D cameraPosition = camera.GetPosition();
                Vector3D directionToTarget = VectorMath.SafeNormalize(
                    targetPosition - cameraPosition
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
                    targetPosition - remotePosition,
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
            // --- HUD Color Palette ---
            private static readonly Color HUD_PRIMARY = Color.Lime;
            private static readonly Color HUD_SECONDARY = Color.Green;
            private static readonly Color HUD_HORIZON = Color.LimeGreen;
            private static readonly Color HUD_EMPHASIS = Color.Yellow;
            private static readonly Color HUD_WARNING = Color.Red;
            private static readonly Color HUD_INFO = Color.White;
            private static readonly Color HUD_RADAR_FRIENDLY = Color.DarkGreen;

            // --- Layout Constants ---
            private const float SPEED_TAPE_CENTER_Y_FACTOR = 2.25f;
            private const float INFO_BOX_Y_OFFSET_FACTOR = 1.85f;
            private const float ALTITUDE_TAPE_CENTER_Y_FACTOR = 2.0f;
            private const float THROTTLE_HYDROGEN_THRESHOLD = 0.8f;

            // --- Physics Constants ---
            private const double SEA_LEVEL_SPEED_OF_SOUND = 343.0; // m/s
            private const double GRAVITY_ACCELERATION = 9.81; // m/s²

            // --- Smoothing Configuration ---
            private const int SMOOTHING_WINDOW_SIZE = 10;

            // --- Component References ---
            IMyCockpit cockpit;
            IMyTextSurface hud;
            IMyTextSurface weaponScreen; // Third screen for weapon/combat info
            IMyTerminalBlock hudBlock;
            List<IMyTerminalBlock> leftstab = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> rightstab = new List<IMyTerminalBlock>();
            private List<IMyThrust> thrusters = new List<IMyThrust>();
            private List<IMyGasTank> tanks = new List<IMyGasTank>();
            private List<IMyDoor> airbreaks = new List<IMyDoor>();
            Jet myjet;
            RWRModule rwrModule; // Reference to RWR module for threat cone display

            // --- Flight Data ---
            double peakGForce = 0;
            float currentTrim;
            double pitch = 0;
            double roll = 0;
            double velocity;
            double deltaTime;
            double mach;
            Vector3D previousVelocity = Vector3D.Zero;

            // --- Smoothed Values with Running Averages ---
            CircularBuffer<double> velocityHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);
            CircularBuffer<AltitudeTimePoint> altitudeHistory = new CircularBuffer<AltitudeTimePoint>(SMOOTHING_WINDOW_SIZE);
            CircularBuffer<double> gForcesHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);
            CircularBuffer<double> aoaHistory = new CircularBuffer<double>(SMOOTHING_WINDOW_SIZE);

            double smoothedVelocity = 0;
            double smoothedAltitude = 0;
            double smoothedGForces = 0;
            double smoothedAoA = 0;
            double smoothedThrottle = 0;

            // Running sums for efficient smoothing
            private double velocitySum = 0;
            private double gForcesSum = 0;
            private double aoaSum = 0;

            // --- Throttle Control ---
            float throttlecontrol = 0f;
            bool hydrogenswitch = false;

            // --- UI State ---
            string hotkeytext = "Test";
            private TimeSpan totalElapsedTime = TimeSpan.Zero;

            // --- Cached HUD Values (recalculate when surface size changes) ---
            private Vector2 hudCenter;
            private float viewportMinDim;

            // --- NEW: Enhanced HUD Features ---
            // Velocity trail (tadpole)

            // Missile tracking
            private struct MissileTrackingData
            {
                public int BayIndex;
                public TimeSpan LaunchTime;
                public double EstimatedTOF;
                public Vector3D TargetPosition;
            }
            private List<MissileTrackingData> activeMissiles = new List<MissileTrackingData>();

            // HUD Mode System
            private enum HUDMode { AirToAir, AirToGround, Navigation }
            private HUDMode currentHUDMode = HUDMode.AirToAir;

            // Radar sweep animation
            private int radarSweepTick = 0;

            // Fuel state tracking
            private const double BINGO_FUEL_PERCENT = 0.20; // 20% fuel = Bingo state
            private const double LOW_FUEL_PERCENT = 0.35;   // 35% fuel = Low fuel warning

            public HUDModule(Program program, Jet jet, IMyTextSurface weaponSurface, RWRModule rwr) : base(program)
            {
                cockpit = jet._cockpit;
                hudBlock = jet.hudBlock;
                hud = jet.hud;
                weaponScreen = weaponSurface; // Store weapon screen reference
                rwrModule = rwr; // Store RWR module reference

                rightstab = jet.rightstab;
                leftstab = jet.leftstab;

                thrusters = jet._thrustersbackwards;
                tanks = jet.tanks;
                for (int i = 0; i < tanks.Count; i++)
                {
                    tanks[i].Enabled = false;
                }
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
                string modeText = currentHUDMode == HUDMode.AirToAir ? "A/A" :
                                 currentHUDMode == HUDMode.AirToGround ? "A/G" : "NAV";
                return $"5: HUD Mode [{modeText}]";
            }

            public override void HandleSpecialFunction(int functionNumber)
            {
                if (functionNumber == 5)
                {
                    // Cycle through HUD modes
                    currentHUDMode = (HUDMode)(((int)currentHUDMode + 1) % 3);
                }
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



            private bool CalculateInterceptPointIterative(
                Vector3D shooterPosition,
                Vector3D shooterVelocity,
                double projectileSpeed,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D gravity,
                int maxIterations,
                out Vector3D interceptPoint,
                out double timeToIntercept)
            {
                interceptPoint = Vector3D.Zero;
                timeToIntercept = -1;

                Vector3D relativePosition = targetPosition - shooterPosition;
                Vector3D relativeVelocity = targetVelocity - shooterVelocity;
                double a = relativeVelocity.LengthSquared() - projectileSpeed * projectileSpeed;
                double b = 2 * Vector3D.Dot(relativePosition, relativeVelocity);
                double c = relativePosition.LengthSquared();
                double t_guess = -1;

                if (Math.Abs(a) < 1e-6)
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
                        else t_guess = Math.Max(t1, t2);
                    }
                }

                if (t_guess <= 0) return false;

                timeToIntercept = t_guess;
                double previousTimeToIntercept = timeToIntercept;
                const double CONVERGENCE_THRESHOLD = 0.001; // Converged if change < 1ms

                for (int i = 0; i < maxIterations; ++i)
                {
                    if (timeToIntercept <= 0) break;

                    Vector3D predictedTargetPos = targetPosition + targetVelocity * timeToIntercept;
                    Vector3D projectileDisplacement = predictedTargetPos - shooterPosition;
                    Vector3D requiredLaunchVel = (projectileDisplacement - 0.5 * gravity * timeToIntercept * timeToIntercept) / timeToIntercept;

                    Vector3D launchDirection = Vector3D.Normalize(requiredLaunchVel);
                    Vector3D actualLaunchVel = launchDirection * projectileSpeed + shooterVelocity;
                    Vector3D newRelativeVelocity = targetVelocity - actualLaunchVel;

                    a = newRelativeVelocity.LengthSquared() - projectileSpeed * projectileSpeed;
                    b = 2 * Vector3D.Dot(relativePosition, newRelativeVelocity);
                    c = relativePosition.LengthSquared();

                    t_guess = -1;
                    if (Math.Abs(a) < 1e-6)
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
                            else t_guess = Math.Max(t1, t2);
                        }
                    }

                    if (t_guess <= 0)
                    {
                        return false;
                    }

                    // FIX: Check convergence - if change is small enough, we're done
                    double delta = Math.Abs(t_guess - previousTimeToIntercept);
                    previousTimeToIntercept = timeToIntercept;
                    timeToIntercept = t_guess;

                    if (delta < CONVERGENCE_THRESHOLD)
                    {
                        break; // Converged successfully
                    }
                }

                interceptPoint = targetPosition + targetVelocity * timeToIntercept;
                return timeToIntercept > 0;
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
                const float MIN_PIP_SIZE_FACTOR = 0.01f;     // Pip size factor at max distance (relative to viewportMinDim)

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
                    return;
                }


                Vector3D directionToIntercept = interceptPoint - shooterPosition;
                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector3D localDirectionToIntercept = Vector3D.TransformNormal(directionToIntercept, worldToCockpitMatrix);

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;
                float viewportMinDim = Math.Min(surfaceSize.X, surfaceSize.Y);
                float targetMarkerSize = viewportMinDim * 0.02f;
                float lineThickness = Math.Max(1f, viewportMinDim * 0.004f);
                float reticleArmLength = viewportMinDim * 0.025f;
                float arrowSize = viewportMinDim * 0.04f;
                float arrowHeadSize = viewportMinDim * 0.025f;
                double distanceToIntercept = Vector3D.Distance(shooterPosition, interceptPoint);
                float distanceScaleFactor = (float)MathHelper.Clamp((MAX_DISTANCE_FOR_SCALING - distanceToIntercept) / (MAX_DISTANCE_FOR_SCALING - MIN_DISTANCE_FOR_SCALING), 0.0, 1.0);
                float currentPipSizeFactor = MathHelper.Lerp(MIN_PIP_SIZE_FACTOR, MAX_PIP_SIZE_FACTOR, distanceScaleFactor);
                float dynamicPipSize = viewportMinDim * currentPipSizeFactor;


                if (localDirectionToIntercept.Z > MIN_Z_FOR_PROJECTION)
                {
                    AddLineSprite(frame, center - new Vector2(reticleArmLength, 0), center + new Vector2(reticleArmLength, 0), lineThickness, behindColor);
                    AddLineSprite(frame, center - new Vector2(0, reticleArmLength), center + new Vector2(0, reticleArmLength), lineThickness, behindColor);
                    return;
                }

                AddLineSprite(frame, center - new Vector2(reticleArmLength, 0), center + new Vector2(reticleArmLength, 0), lineThickness, reticleColor);
                AddLineSprite(frame, center - new Vector2(0, reticleArmLength), center + new Vector2(0, reticleArmLength), lineThickness, reticleColor);


                if (Math.Abs(localDirectionToIntercept.Z) < MIN_Z_FOR_PROJECTION)
                {
                    localDirectionToIntercept.Z = -MIN_Z_FOR_PROJECTION;
                }


                // FOV projection constants - empirically determined from cockpit perspective
                // These values convert 3D directions to 2D screen coordinates
                const float COCKPIT_FOV_SCALE_X = 0.3434f; // Horizontal FOV scale factor
                const float COCKPIT_FOV_SCALE_Y = 0.31f;   // Vertical FOV scale (adjusted for aspect ratio)
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;
                float screenX = center.X + (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z) * scaleX;
                float screenY = center.Y + (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z) * scaleY;
                Vector2 pipScreenPos = new Vector2(screenX, screenY);

                bool isOnScreen = pipScreenPos.X >= 0 && pipScreenPos.X <= surfaceSize.X &&
                                  pipScreenPos.Y >= 0 && pipScreenPos.Y <= surfaceSize.Y;
                float distanceToPip = Vector2.Distance(center, pipScreenPos);
                float pipRadius = dynamicPipSize / 2f;
                if (distanceToPip <= pipRadius)
                {
                    isAimingAtPip = true;
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
                    var pipSprite = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_CIRCLE,
                        Position = pipScreenPos,
                        Size = new Vector2(dynamicPipSize, dynamicPipSize),
                        Color = pipColor,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(pipSprite);
                    Vector2 targetScreenPos = Vector2.Zero; // Initialize
                    const float velocityIndicatorScale = 20f; // Example: Represents 0.3 seconds of travel

                    Vector3D targetVelocityEndPointWorld = interceptPoint + targetVelocity * velocityIndicatorScale;

                    // FIX: Removed duplicate worldToLocalMatrix (already have worldToCockpitMatrix)
                    // FIX: Removed dead code - localTargetVelocityEndPoint was never used
                    Vector2 targetVelEndPointScreenPos = Vector2.Zero; // Initialize

                    // 2. Get the direction vector FROM THE SHOOTER to that point
                    Vector3D directionToVelEndPoint = Vector3D.Normalize(targetVelocityEndPointWorld - shooterPosition);

                    // 3. Transform that DIRECTION into the cockpit's local reference frame
                    Vector3D localDirectionToVelEndPoint = Vector3D.TransformNormal(directionToVelEndPoint, worldToCockpitMatrix);

                    // 4. Now, project this correct local direction onto the screen
                    if (localDirectionToVelEndPoint.Z < 0) // Check if it's in front
                    {
                        float screenX_vel = center.X + (float)(localDirectionToVelEndPoint.X / -localDirectionToVelEndPoint.Z) * scaleX;
                        float screenY_vel = center.Y + (float)(-localDirectionToVelEndPoint.Y / -localDirectionToVelEndPoint.Z) * scaleY;
                        targetVelEndPointScreenPos = new Vector2(screenX_vel, screenY_vel);

                        // Draw your line from pipScreenPos to targetVelEndPointScreenPos
                    }

                    // FIX: Use worldToCockpitMatrix instead of duplicate worldToLocalMatrix
                    Vector3D directionToTarget = targetPosition - shooterPosition;
                    Vector3D localDirectionToTarget = Vector3D.TransformNormal(directionToTarget, worldToCockpitMatrix);

                    Vector2 currentTargetScreenPos = Vector2.Zero; // Initialize

                    if (localDirectionToTarget.Z < -MIN_Z_FOR_PROJECTION)
                    {

                        float screenX_tgt = center.X + (float)(localDirectionToTarget.X / -localDirectionToTarget.Z) * scaleX;
                        float screenY_tgt = center.Y + (float)(-localDirectionToTarget.Y / -localDirectionToTarget.Z) * scaleY; // Y inverted
                        currentTargetScreenPos = new Vector2(screenX_tgt, screenY_tgt);
                    }

                    // FIX: Removed redundant isOnScreen check (already inside isOnScreen block)
                    float halfMark = targetMarkerSize / 2f;
                    AddLineSprite(frame, currentTargetScreenPos - new Vector2(halfMark, halfMark), currentTargetScreenPos + new Vector2(halfMark, halfMark), lineThickness, Color.Yellow);
                    AddLineSprite(frame, currentTargetScreenPos - new Vector2(halfMark, -halfMark), currentTargetScreenPos + new Vector2(halfMark, -halfMark), lineThickness, Color.Yellow);
                    AddLineSprite(frame, pipScreenPos, currentTargetScreenPos, lineThickness, Color.Yellow);
                }
                else
                {
                    Vector2 direction = pipScreenPos - center;
                    direction.Normalize();
                    float maxDistX = surfaceSize.X / 2f - arrowSize / 2f;
                    float maxDistY = surfaceSize.Y / 2f - arrowSize / 2f;
                    float angle = (float)Math.Atan2(direction.Y, direction.X);

                    float edgeX = (float)Math.Cos(angle) * maxDistX;
                    float edgeY = (float)Math.Sin(angle) * maxDistY;

                    // FIX: Prevent division by zero when edgeX or edgeY is near zero
                    Vector2 edgePoint;
                    float absEdgeX = Math.Abs(edgeX);
                    float absEdgeY = Math.Abs(edgeY);

                    // Add epsilon to prevent division by zero
                    if (absEdgeX < 1e-6f) absEdgeX = 1e-6f;
                    if (absEdgeY < 1e-6f) absEdgeY = 1e-6f;

                    if (Math.Abs(edgeX / maxDistX) > Math.Abs(edgeY / maxDistY))
                    {
                        edgePoint = new Vector2(center.X + Math.Sign(edgeX) * maxDistX, center.Y + edgeY * (maxDistX / absEdgeX));
                    }
                    else
                    {
                        edgePoint = new Vector2(center.X + edgeX * (maxDistY / absEdgeY), center.Y + Math.Sign(edgeY) * maxDistY);
                    }


                    edgePoint.X = MathHelper.Clamp(edgePoint.X, arrowSize / 2f, surfaceSize.X - arrowSize / 2f);
                    edgePoint.Y = MathHelper.Clamp(edgePoint.Y, arrowSize / 2f, surfaceSize.Y - arrowSize / 2f);


                    float arrowRotation = (float)Math.Atan2(direction.Y, direction.X);
                    var arrowSprite = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = TEXTURE_TRIANGLE,
                        Position = edgePoint,
                        Size = new Vector2(arrowHeadSize, arrowHeadSize),
                        Color = offScreenColor,
                        RotationOrScale = arrowRotation + (float)Math.PI / 2f,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(arrowSprite);
                }
            }




            const float RADAR_RANGE_METERS = 15000f;
            const float RADAR_BOX_SIZE_PX = 100f;
            const float RADAR_BORDER_MARGIN = 10f;

            // Optimized version using array of target positions
            private void DrawTopDownRadarOptimized(
                MySpriteDrawFrame frame,
                IMyCockpit cockpit,
                IMyTextSurface hud,
                Vector3D[] targetPositions,
                Color radarBgColor,
                Color radarBorderColor,
                Color playerColor,
                Color primaryTargetColor
            )
            {
                if (cockpit == null || hud == null) return;

                Vector2 surfaceSize = hud.SurfaceSize;

                // FIX: Simplified radar origin calculation (X - X*0.2 = X*0.8)
                Vector2 radarOrigin = new Vector2(
                    hud.SurfaceSize.X * 0.8f - RADAR_BORDER_MARGIN,  // 80% from left edge
                    surfaceSize.Y - RADAR_BOX_SIZE_PX - RADAR_BORDER_MARGIN
                );
                Vector2 radarSize = new Vector2(RADAR_BOX_SIZE_PX, RADAR_BOX_SIZE_PX);
                Vector2 radarCenter = radarOrigin + radarSize / 2f;
                float radarRadius = RADAR_BOX_SIZE_PX / 2f;

                DrawRectangleOutline(frame, radarOrigin.X - 5f, radarOrigin.Y - 5f, radarSize.X + 10f, radarSize.Y + 10f, 1f, radarBorderColor);


                var playerArrow = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = TEXTURE_TRIANGLE,
                    Position = radarCenter,
                    Size = new Vector2(radarRadius * 0.15f, radarRadius * 0.15f),
                    Color = playerColor,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = 0
                };
                frame.Add(playerArrow);

                Vector3D shooterPosition = cockpit.GetPosition();

                // Calculate world up vector from gravity
                Vector3D gravity = cockpit.GetNaturalGravity();
                Vector3D worldUp;
                if (gravity.LengthSquared() < 0.01)
                {
                    worldUp = cockpit.WorldMatrix.Up;
                }
                else
                {
                    worldUp = Vector3D.Normalize(-gravity);
                }

                // Create yaw-aligned coordinate system
                Vector3D shipForward = cockpit.WorldMatrix.Forward;
                Vector3D yawForward = Vector3D.Normalize(Vector3D.Reject(shipForward, worldUp));

                if (!yawForward.IsValid() || yawForward.LengthSquared() < 0.1)
                {
                    Vector3D shipRightProjected = Vector3D.Normalize(Vector3D.Reject(cockpit.WorldMatrix.Right, worldUp));
                    if (!shipRightProjected.IsValid() || shipRightProjected.LengthSquared() < 0.1)
                    {
                        yawForward = shipForward;
                    }
                    else
                    {
                        yawForward = Vector3D.Cross(shipRightProjected, worldUp);
                    }
                }

                Vector3D yawRight = Vector3D.Cross(yawForward, worldUp);
                MatrixD yawMatrix = MatrixD.Identity;
                yawMatrix.Forward = yawForward;
                yawMatrix.Right = yawRight;
                yawMatrix.Up = worldUp;

                MatrixD worldToYawPlaneMatrix = MatrixD.Transpose(yawMatrix);
                float pixelsPerMeter = radarRadius / RADAR_RANGE_METERS;

                // Draw all targets using a loop (OPTIMIZED!)
                for (int i = 0; i < targetPositions.Length; i++)
                {
                    Vector3D targetVectorWorld = targetPositions[i] - shooterPosition;
                    Vector3D targetVectorYawLocal = Vector3D.TransformNormal(targetVectorWorld, worldToYawPlaneMatrix);

                    Vector2 targetOffset = new Vector2(
                        (float)targetVectorYawLocal.X * pixelsPerMeter,
                        (float)targetVectorYawLocal.Z * pixelsPerMeter
                    );

                    // Clamp to radar radius
                    float distFromCenter = targetOffset.Length();
                    if (distFromCenter > radarRadius)
                    {
                        if (distFromCenter > 1e-6f)
                            targetOffset /= distFromCenter;
                        targetOffset *= radarRadius;
                    }

                    Vector2 targetRadarPos = radarCenter + targetOffset;

                    // Determine color: primary target (0) gets special color, others are friendly
                    Color targetColor = (i == 0) ? primaryTargetColor : HUD_RADAR_FRIENDLY;

                    if (targetRadarPos.IsValid())
                    {
                        var targetIcon = new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = TEXTURE_SQUARE,
                            Position = targetRadarPos,
                            Size = new Vector2(radarRadius * 0.1f, radarRadius * 0.1f),
                            Color = targetColor,
                            Alignment = TextAlignment.CENTER
                        };
                        frame.Add(targetIcon);
                    }
                }
            }


            private float integralError = 0f;
            private float previousError = 0f;
            private float Kp = 1.2f;
            private float Ki = 0.0024f;
            private float Kd = 0.5f;

            private const float MaxPIDOutput = 60f;
            private const float MaxAOA = 36f;



            private void AdjustStabilizers(double aoa, Jet myjet)
            {
                // Ensure cockpit is valid before accessing properties
                if (cockpit == null)
                {
                    // Handle the error appropriately, maybe disable stabilization
                    return;
                }

                Vector2 pitchyaw = cockpit.RotationIndicator;


                AdjustTrim(rightstab, myjet.offset);
                AdjustTrim(leftstab, -myjet.offset);

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

            // --- Tick() Helper Methods (Refactored for clarity and performance) ---

            private bool ValidateHUDState()
            {
                if (cockpit == null || hud == null)
                    return false;

                if (!cockpit.IsFunctional || !hudBlock.IsFunctional)
                    return false;

                return true;
            }

            private void UpdateFlightData(out MatrixD worldMatrix, out Vector3D forwardVector,
                out Vector3D upVector, out Vector3D gravity, out bool inGravity,
                out Vector3D gravityDirection)
            {
                totalElapsedTime += ParentProgram.Runtime.TimeSinceLastRun;

                // World matrix and direction vectors
                worldMatrix = cockpit.WorldMatrix;
                forwardVector = worldMatrix.Forward;
                upVector = worldMatrix.Up;
                Vector3D leftVector = worldMatrix.Left;

                // Gravity checks
                gravity = cockpit.GetNaturalGravity();
                inGravity = gravity.LengthSquared() > 0;
                gravityDirection = inGravity ? Vector3D.Normalize(gravity) : Vector3D.Zero;

                // Pitch and roll calculations (only if in gravity)
                if (inGravity)
                {
                    pitch = Math.Asin(Vector3D.Dot(forwardVector, gravityDirection)) * (180 / Math.PI);
                    roll = Math.Atan2(
                        Vector3D.Dot(leftVector, gravityDirection),
                        Vector3D.Dot(upVector, gravityDirection)
                    ) * (180 / Math.PI);

                    // Normalize roll to [0, 360)
                    if (roll < 0)
                        roll += 360;
                }

                // Speed and Mach calculation
                velocity = cockpit.GetShipSpeed();
                mach = velocity / SEA_LEVEL_SPEED_OF_SOUND;

                // Acceleration and G-force calculations
                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;
                deltaTime = ParentProgram.Runtime.TimeSinceLastRun.TotalSeconds;

                // Fallback to ~1/60th of a second if deltaTime is not valid
                if (deltaTime <= 0)
                    deltaTime = 0.0167;

                Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                double gForces = acceleration.Length() / GRAVITY_ACCELERATION;
                previousVelocity = currentVelocity;

                // Track peak G-forces
                if (gForces > peakGForce)
                    peakGForce = gForces;

                // Calculate heading, altitude, angle of attack
                double heading = CalculateHeading();
                double altitude = GetAltitude();
                double aoa = CalculateAngleOfAttack(
                    cockpit.WorldMatrix.Forward,
                    cockpit.GetShipVelocities().LinearVelocity,
                    upVector
                );

                double velocityKPH = velocity * 3.6; // Convert m/s to KPH

                // Update smoothed values
                UpdateSmoothedValues(velocityKPH, altitude, gForces, aoa, throttlecontrol);
                AdjustStabilizers(aoa, myjet);


            }

            private void UpdateThrottleControl(double throttle, double jumpthrottle)
            {
                if (throttle == 1f)
                {
                    throttlecontrol += 0.01f;
                    if (throttlecontrol > 1f)
                        throttlecontrol = 1f;
                    if (throttlecontrol >= THROTTLE_HYDROGEN_THRESHOLD)
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
                    if (throttlecontrol < THROTTLE_HYDROGEN_THRESHOLD)
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

                // Airbrake control
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
                if (jumpthrottle < -0.01f)
                {
                    myjet.manualfire = !myjet.manualfire;
                }

                // Manual fire mode
                if (myjet.manualfire)
                {
                    for (int i = 0; i < myjet._gatlings.Count; i++)
                    {
                        myjet._gatlings[i].Enabled = true;
                    }
                }

                // Apply throttle to thrusters
                for (int i = 0; i < thrusters.Count; i++)
                {
                    float scaledThrottle;

                    if (throttlecontrol <= THROTTLE_HYDROGEN_THRESHOLD)
                    {
                        // Scale throttlecontrol to fit the range 0.0 to 1.0
                        scaledThrottle = throttlecontrol / THROTTLE_HYDROGEN_THRESHOLD;
                    }
                    else
                    {
                        // Cap it at 1.0 when throttlecontrol is over threshold
                        scaledThrottle = 1.0f;
                    }

                    thrusters[i].ThrustOverridePercentage = scaledThrottle;
                }
            }

            // Render weapon and combat information to the third screen
            private void RenderWeaponScreen(double heading, double altitude, Vector3D currentVelocity, Vector3D shooterPosition)
            {
                if (weaponScreen == null) return;

                using (var frame = weaponScreen.DrawFrame())
                {
                    float screenWidth = weaponScreen.SurfaceSize.X;
                    float screenHeight = weaponScreen.SurfaceSize.Y;
                    float margin = 10f;
                    float panelY = 25f; // Increased from 10f
                    Color titleColor = new Color(200, 180, 50); // Softer yellow
                    Color headerColor = new Color(50, 180, 200); // Cyan/teal
                    Color borderColor = new Color(60, 120, 60); // Darker green
                    Color panelBgColor = new Color(20, 20, 20, 180); // Dark gray panel background

                    // Draw main background
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, screenHeight / 2f),
                        Size = new Vector2(screenWidth, screenHeight),
                        Color = Color.Black,
                        Alignment = TextAlignment.CENTER
                    });

                    // Title section with background panel
                    float titleHeight = 35f;
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY + 5f),
                        Size = new Vector2(screenWidth - margin * 2, titleHeight),
                        Color = new Color(30, 30, 30, 200),
                        Alignment = TextAlignment.CENTER
                    });

                    // Title text
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = "WEAPON SYSTEMS",
                        Position = new Vector2(screenWidth / 2f, panelY),
                        RotationOrScale = 0.75f,
                        Color = titleColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    panelY += 45f; // Increased spacing

                    // Weapon Status Panel with background
                    float weaponPanelHeight = 100f;
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY + weaponPanelHeight / 2f),
                        Size = new Vector2(screenWidth - margin * 2, weaponPanelHeight),
                        Color = panelBgColor,
                        Alignment = TextAlignment.CENTER
                    });

                    DrawWeaponStatusPanelToScreen(frame, myjet, margin, panelY, screenWidth - margin * 2);

                    panelY += weaponPanelHeight + 15f; // Increased spacing between sections

                    // Separator line
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(screenWidth / 2f, panelY),
                        Size = new Vector2(screenWidth - margin * 4, 2f),
                        Color = borderColor,
                        Alignment = TextAlignment.CENTER
                    });

                    panelY += 15f;

                    // Missile Time-of-Flight Countdown
                    if (activeMissiles.Count > 0)
                    {
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "MISSILES IN FLIGHT",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            RotationOrScale = 0.65f,
                            Color = headerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                        panelY += 30f;

                        DrawMissileTOFToScreen(frame, screenWidth / 2f, panelY);
                        panelY += activeMissiles.Count * 20f + 25f;
                    }

                    // Multi-Target Threat Display - check if we have multiple occupied slots
                    int occupiedSlotCount = 0;
                    for (int i = 0; i < myjet.targetSlots.Length; i++)
                    {
                        if (myjet.targetSlots[i].IsOccupied) occupiedSlotCount++;
                    }
                    if (occupiedSlotCount > 1)
                    {
                        // Separator line
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            Size = new Vector2(screenWidth - margin * 4, 2f),
                            Color = borderColor,
                            Alignment = TextAlignment.CENTER
                        });

                        panelY += 15f;

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "TARGET TRACKING",
                            Position = new Vector2(screenWidth / 2f, panelY),
                            RotationOrScale = 0.65f,
                            Color = headerColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                        panelY += 30f;

                        // Extract positions from occupied slots
                        Vector3D[] occupiedPositions = new Vector3D[occupiedSlotCount];
                        int posIndex = 0;
                        for (int i = 0; i < myjet.targetSlots.Length; i++)
                        {
                            if (myjet.targetSlots[i].IsOccupied)
                            {
                                occupiedPositions[posIndex++] = myjet.targetSlots[i].Position;
                            }
                        }
                        DrawMultiTargetPanelToScreen(frame, occupiedPositions, shooterPosition, margin, panelY, screenWidth - margin * 2);
                    }
                }
            }

            private void RenderHUD(double heading, Vector3D gravityDirection, Vector3D currentVelocity, MatrixD worldMatrix)
            {
                // Cache frequently used values
                hudCenter = hud.SurfaceSize / 2f;
                viewportMinDim = Math.Min(hud.SurfaceSize.X, hud.SurfaceSize.Y);

                float centerX = hudCenter.X;
                float centerY = hudCenter.Y;
                float pixelsPerDegree = hud.SurfaceSize.Y / 16f; // F18-like scaling

                // Get position and altitude for both HUD and weapon screen
                Vector3D shooterPosition = cockpit.GetPosition();
                double altitude = GetAltitude();

                using (var frame = hud.DrawFrame())
                {
                    // === PHASE 1: CORE HUD ELEMENTS ===
                    DrawArtificialHorizon(
                        frame,
                        (float)pitch,
                        (float)roll,
                        centerX,
                        centerY,
                        pixelsPerDegree
                    );

                    // NEW: Bank angle markers on horizon
                    DrawBankAngleMarkers(frame, centerX, centerY, (float)roll, pixelsPerDegree);

                    DrawFlightPathMarker(
                        frame,
                        currentVelocity,
                        worldMatrix,
                        roll,
                        centerX,
                        centerY,
                        pixelsPerDegree
                    );

                    // NEW: Velocity trail (tadpole)

                    DrawLeftInfoBox(
                        frame,
                        smoothedVelocity,
                        centerX + 30f,
                        centerY + centerY * INFO_BOX_Y_OFFSET_FACTOR,
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
                    DrawSpeedIndicatorF18StyleKph(frame, smoothedVelocity);
                    //DrawRadar(frame, myjet, centerX - centerX * 0.70f,
                    //    centerY + centerY * 0.75f, 70, 30,
                    //    pixelsPerDegree);
                    DrawCompass(frame, heading);
                    DrawAltitudeIndicatorF18Style(frame, smoothedAltitude, totalElapsedTime);
                    DrawGForceIndicator(frame, smoothedGForces, peakGForce);

                    // NEW: AOA Indexer - always visible when flying
                    if (velocity > 1.0) // Only show when moving
                    {
                        Vector3D acceleration = (currentVelocity - previousVelocity) / deltaTime;
                        DrawAOAIndexer(frame, smoothedAoA, acceleration, velocity);
                    }

                    // === PHASE 2: MINIMAP (ALWAYS VISIBLE) ===
                    // Draw top-down radar with all targets and sweep animation
                    Vector2 surfaceSize = hud.SurfaceSize;
                    const float RADAR_BOX_SIZE_PX = 100f;
                    const float RADAR_BORDER_MARGIN = 10f;
                    Vector2 radarOrigin = new Vector2(
                        hud.SurfaceSize.X * 0.8f - RADAR_BORDER_MARGIN,
                        surfaceSize.Y - RADAR_BOX_SIZE_PX - RADAR_BORDER_MARGIN
                    );
                    Vector2 radarCenter = radarOrigin + new Vector2(RADAR_BOX_SIZE_PX / 2f, RADAR_BOX_SIZE_PX / 2f);

                    // Extract occupied slot positions for radar display
                    // IMPORTANT: Put active target FIRST so it gets highlighted (index 0 = primary color)
                    Vector3D[] radarTargetPositions = new Vector3D[myjet.targetSlots.Length];
                    int radarTargetCount = 0;

                    // Add active target first (if occupied)
                    if (myjet.targetSlots[myjet.activeSlotIndex].IsOccupied)
                    {
                        radarTargetPositions[radarTargetCount++] = myjet.targetSlots[myjet.activeSlotIndex].Position;
                    }

                    // Add other occupied slots
                    for (int i = 0; i < myjet.targetSlots.Length; i++)
                    {
                        if (i != myjet.activeSlotIndex && myjet.targetSlots[i].IsOccupied)
                        {
                            radarTargetPositions[radarTargetCount++] = myjet.targetSlots[i].Position;
                        }
                    }

                    // Resize array to actual count
                    Array.Resize(ref radarTargetPositions, radarTargetCount);

                    // Always draw minimap (even with zero targets)
                    DrawTopDownRadarOptimized(frame, cockpit, hud,
                        radarTargetPositions,
                        Color.White, HUD_PRIMARY, HUD_EMPHASIS, HUD_WARNING);

                    // Radar sweep animation
                    DrawRadarSweepLine(frame, radarCenter, RADAR_BOX_SIZE_PX / 2f);

                    // RWR threat cones (if RWR is enabled and has threats)
                    DrawRWRThreatCones(frame, cockpit, radarCenter, RADAR_BOX_SIZE_PX / 2f);

                    // === PHASE 3: TARGETING ELEMENTS (ONLY WHEN TARGET LOCKED) ===
                    // Check if active slot has a target
                    if (myjet.targetSlots[myjet.activeSlotIndex].IsOccupied)
                    {
                        Vector3D activeTargetPos = myjet.targetSlots[myjet.activeSlotIndex].Position;
                        Vector3D activeTargetVel = myjet.targetSlots[myjet.activeSlotIndex].Velocity;

                        double muzzleVelocity = 910; // Muzzle velocity in m/s
                        double range = Vector3D.Distance(shooterPosition, activeTargetPos);

                        // Calculate intercept for leading pip and gun funnel
                        Vector3D interceptPoint;
                        double timeToIntercept;
                        bool hasIntercept = CalculateInterceptPointIterative(
                            shooterPosition,
                            currentVelocity,
                            muzzleVelocity,
                            activeTargetPos,
                            activeTargetVel,
                            gravityDirection,
                            INTERCEPT_ITERATIONS,
                            out interceptPoint,
                            out timeToIntercept
                        );

                        if (hasIntercept)
                        {
                            // Check if aiming at pip
                            MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                            Vector3D directionToIntercept = interceptPoint - shooterPosition;
                            Vector3D localDirectionToIntercept = Vector3D.TransformNormal(directionToIntercept, worldToCockpitMatrix);

                            bool isAimingAtPip = false;
                            if (localDirectionToIntercept.Z < 0)
                            {
                                Vector2 center = surfaceSize / 2f;
                                const float COCKPIT_FOV_SCALE_X = 0.3434f;
                                const float COCKPIT_FOV_SCALE_Y = 0.31f;
                                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;
                                float screenX = center.X + (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z) * scaleX;
                                float screenY = center.Y + (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z) * scaleY;
                                Vector2 pipScreenPos = new Vector2(screenX, screenY);

                                float pipRadius = viewportMinDim * 0.05f; // Approximate pip size
                                float distanceToPip = Vector2.Distance(center, pipScreenPos);
                                isAimingAtPip = distanceToPip <= pipRadius;
                            }

                            // NEW: Gun funnel visualization
                            DrawGunFunnel(frame, cockpit, hud, interceptPoint, shooterPosition, range, isAimingAtPip);

                            // Draw leading pip for main target
                            DrawLeadingPip(
                                frame, cockpit, hud,
                                activeTargetPos,
                                activeTargetVel,
                                shooterPosition,
                                currentVelocity,
                                muzzleVelocity,
                                gravityDirection,
                                HUD_WARNING, HUD_EMPHASIS, Color.HotPink, HUD_INFO
                            );

                            // NEW: Target acquisition brackets (keep on main HUD for situational awareness)
                            DrawTargetBrackets(frame, cockpit, hud,
                                activeTargetPos,
                                activeTargetVel,
                                shooterPosition,
                                currentVelocity);
                        }

                        // NEW: Breakaway warning (keep on main HUD - critical safety feature)
                        DrawBreakawayWarning(frame, altitude, currentVelocity, activeTargetPos, shooterPosition);
                    }

                    // NEW: Formation ghosts (keep on main HUD for formation flying)
                    DrawFormationGhosts(frame, cockpit, hud);
                }

                // Render weapon screen (separate display)
                RenderWeaponScreen(heading, altitude, currentVelocity, shooterPosition);
            }

            public override void Tick()
            {
                // Validation
                if (!ValidateHUDState())
                    return;

                // Get throttle inputs
                double throttle = cockpit.MoveIndicator.Z * -1;
                double jumpthrottle = cockpit.MoveIndicator.Y;

                // Update all flight data
                MatrixD worldMatrix;
                Vector3D forwardVector, upVector, gravity, gravityDirection;
                bool inGravity;
                UpdateFlightData(out worldMatrix, out forwardVector, out upVector,
                    out gravity, out inGravity, out gravityDirection);

                // Update throttle and control systems
                UpdateThrottleControl(throttle, jumpthrottle);

                // Render the HUD
                double heading = CalculateHeading();
                Vector3D currentVelocity = cockpit.GetShipVelocities().LinearVelocity;
                RenderHUD(heading, gravityDirection, currentVelocity, worldMatrix);
            }
            private void DrawGForceIndicator(
                MySpriteDrawFrame frame,
                double gForces,
                double peakGForce
            )
            {
                const float PADDING = 10f;
                const float TEXT_SCALE = 0.8f;
                const float LINE_HEIGHT = 20f;

                string gForceText = $"G: {gForces:F1}";
                var gForceSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = gForceText,
                    Position = new Vector2(PADDING, hud.SurfaceSize.Y - PADDING - LINE_HEIGHT),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
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
                    Position = new Vector2(PADDING, hud.SurfaceSize.Y - PADDING - LINE_HEIGHT * 2),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
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
                const float Y_OFFSET_PER_VALUE = 30f;
                const float X_OFFSET_FACTOR = 0.75f;
                const float Y_OFFSET_FACTOR = 0.5f;
                const float LABEL_COLUMN_OFFSET = 40f;
                const float NUMBER_COLUMN_OFFSET = 40f;
                const float TEXT_SCALE = 0.75f;

                float xoffset = centerX - centerX * X_OFFSET_FACTOR;
                float yoffset = centerY - centerY * Y_OFFSET_FACTOR;
                float labelColumnX = xoffset - LABEL_COLUMN_OFFSET;
                float numberColumnX = xoffset + NUMBER_COLUMN_OFFSET;

                for (int i = 0; i < extraValues.Length; i++)
                {
                    string labelText = extraValues[i].Label;
                    double numericValue = extraValues[i].Value;

                    // Draw the label
                    var labelSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = labelText,
                        Position = new Vector2(labelColumnX, yoffset + i * Y_OFFSET_PER_VALUE),
                        RotationOrScale = TEXT_SCALE,
                        Color = HUD_PRIMARY,
                        Alignment = TextAlignment.LEFT,
                        FontId = "White"
                    };
                    frame.Add(labelSprite);

                    // Draw the numeric value
                    var valueSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = numericValue.ToString("F1"),
                        Position = new Vector2(numberColumnX, yoffset + i * Y_OFFSET_PER_VALUE),
                        RotationOrScale = TEXT_SCALE,
                        Color = HUD_PRIMARY,
                        Alignment = TextAlignment.RIGHT,
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
            private const string FONT = "Monospace"; // Use a monospaced font for alignment

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
                    Color = HUD_PRIMARY,
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
                        if (altMark >= 0)
                        {
                            // Draw Tick Mark (line pointing left from the tape line)
                            var tickMark = new MySprite()
                            {
                                Type = SpriteType.TEXTURE,
                                Data = "SquareSimple",
                                Position = new Vector2(tapeLineX - currentTickLength / 2f, yPos),
                                Size = new Vector2(currentTickLength, tapeWidth), // Thin horizontal line
                                Color = HUD_PRIMARY,
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
                                Color = HUD_PRIMARY,
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
                DrawRectangleOutline(frame, digitalAltBoxX - 20, centerY - digitalAltBoxHeight - 225 / 2f, digitalAltBoxWidth, digitalAltBoxHeight, 1f, HUD_PRIMARY);


                // Altitude Text
                string currentAltitudeText = currentAltitude.ToString("F0");
                var altitudeLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = currentAltitudeText,
                    Position = new Vector2(digitalAltBoxX - 20 + digitalAltBoxWidth / 2f, centerY - 140), // Centered in the box area
                    RotationOrScale = 0.8f, // Main font size
                    Color = HUD_PRIMARY,
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
                    Color = HUD_PRIMARY,
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
                    Color = HUD_PRIMARY,
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
                            Color = HUD_PRIMARY,
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
                                Color = HUD_PRIMARY,
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
                DrawRectangleOutline(frame, digitalSpeedBoxX, centerY - digitalSpeedBoxHeight / 2f - 130, digitalSpeedBoxWidth, digitalSpeedBoxHeight, 1f, HUD_PRIMARY); // Adjust Y offset as needed

                // Speed Text
                string currentSpeedText = currentSpeedKph.ToString("F0"); // Integer KPH
                var speedLabel = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = currentSpeedText,
                    // Centered within the conceptual box area
                    Position = new Vector2(digitalSpeedBoxX + digitalSpeedBoxWidth / 2f, centerY - 130 - digitalSpeedBoxHeight / 2f), // Adjust Y pos to be centered in the drawn box
                    RotationOrScale = 0.8f, // Main font size
                    Color = HUD_PRIMARY,
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
                    Color = HUD_PRIMARY,
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
                double velocityKPH,
                double altitude,
                double gForces,
                double aoa,
                double throttle
            )
            {
                // Optimized smoothing using running sums instead of .Average() calls

                // Velocity smoothing
                if (velocityHistory.Count >= SMOOTHING_WINDOW_SIZE)
                {
                    velocitySum -= velocityHistory.Dequeue();
                }
                velocityHistory.Enqueue(velocityKPH);
                velocitySum += velocityKPH;
                smoothedVelocity = velocitySum / velocityHistory.Count;

                // Altitude smoothing (uses struct, so we calculate differently)
                altitudeHistory.Enqueue(new AltitudeTimePoint(totalElapsedTime, altitude));
                if (altitudeHistory.Count > 0)
                {
                    double altSum = 0;
                    foreach (var point in altitudeHistory)
                    {
                        altSum += point.Altitude;
                    }
                    smoothedAltitude = altSum / altitudeHistory.Count;
                }

                // G-Force smoothing (FIXED: dequeue before enqueue to prevent accumulation)
                if (gForcesHistory.Count >= SMOOTHING_WINDOW_SIZE)
                {
                    gForcesSum -= gForcesHistory.Dequeue();
                }
                gForcesHistory.Enqueue(gForces);
                gForcesSum += gForces;
                smoothedGForces = gForcesSum / gForcesHistory.Count;

                // AoA smoothing
                if (aoaHistory.Count >= SMOOTHING_WINDOW_SIZE)
                {
                    aoaSum -= aoaHistory.Dequeue();
                }
                aoaHistory.Enqueue(aoa);
                aoaSum += aoa;
                smoothedAoA = aoaSum / aoaHistory.Count;

                // Throttle percentage
                smoothedThrottle = throttle * 100;
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
                // Change color to yellow when entering hydrogen boost range
                barColor = throttle > THROTTLE_HYDROGEN_THRESHOLD ? HUD_EMPHASIS : HUD_PRIMARY;

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

            // ===================================================================
            // NEW ENHANCED HUD FEATURES
            // ===================================================================

            // --- 1. TARGET ACQUISITION RETICLE (TAR BOX) ---
            private void DrawTargetBrackets(
                MySpriteDrawFrame frame,
                IMyCockpit cockpit,
                IMyTextSurface hud,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D shooterPosition,
                Vector3D shooterVelocity
            )
            {
                if (cockpit == null || hud == null) return;

                // Calculate range
                double range = Vector3D.Distance(shooterPosition, targetPosition);

                // Calculate closure rate (negative = closing, positive = separating)
                Vector3D relativeVelocity = targetVelocity - shooterVelocity;
                Vector3D directionToTarget = Vector3D.Normalize(targetPosition - shooterPosition);
                double closureRate = Vector3D.Dot(relativeVelocity, directionToTarget);

                // Calculate aspect angle (target's heading relative to us)
                Vector3D targetForward = Vector3D.Normalize(targetVelocity);
                Vector3D toShooter = Vector3D.Normalize(shooterPosition - targetPosition);
                double aspectAngle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(targetForward, toShooter), -1, 1)) * (180.0 / Math.PI);

                // Project target to screen
                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector3D directionToTargetLocal = Vector3D.TransformNormal(targetPosition - shooterPosition, worldToCockpitMatrix);

                if (Math.Abs(directionToTargetLocal.Z) < MIN_Z_FOR_PROJECTION)
                    directionToTargetLocal.Z = -MIN_Z_FOR_PROJECTION;

                if (directionToTargetLocal.Z >= 0) return; // Target behind us

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                const float COCKPIT_FOV_SCALE_X = 0.3434f;
                const float COCKPIT_FOV_SCALE_Y = 0.31f;
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;
                float screenX = center.X + (float)(directionToTargetLocal.X / -directionToTargetLocal.Z) * scaleX;
                float screenY = center.Y + (float)(-directionToTargetLocal.Y / -directionToTargetLocal.Z) * scaleY;
                Vector2 targetScreenPos = new Vector2(screenX, screenY);

                // Check if on screen
                bool isOnScreen = targetScreenPos.X >= 0 && targetScreenPos.X <= surfaceSize.X &&
                                  targetScreenPos.Y >= 0 && targetScreenPos.Y <= surfaceSize.Y;

                if (!isOnScreen) return;

                // Draw dynamic brackets
                float bracketSize = MathHelper.Clamp((float)(3000.0 / range), 20f, 80f);
                float bracketThickness = 2f;
                float cornerLength = bracketSize * 0.3f;

                // Determine color based on closure rate
                Color bracketColor = closureRate < -10 ? HUD_WARNING :
                                   closureRate > 10 ? HUD_EMPHASIS : HUD_PRIMARY;

                // Draw four corner brackets
                // Top-left
                AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2 + cornerLength, -bracketSize/2),
                                    bracketThickness, bracketColor);
                AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2, -bracketSize/2 + cornerLength),
                                    bracketThickness, bracketColor);

                // Top-right
                AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2 - cornerLength, -bracketSize/2),
                                    bracketThickness, bracketColor);
                AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2, -bracketSize/2 + cornerLength),
                                    bracketThickness, bracketColor);

                // Bottom-left
                AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2 + cornerLength, bracketSize/2),
                                    bracketThickness, bracketColor);
                AddLineSprite(frame, targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(-bracketSize/2, bracketSize/2 - cornerLength),
                                    bracketThickness, bracketColor);

                // Bottom-right
                AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2 - cornerLength, bracketSize/2),
                                    bracketThickness, bracketColor);
                AddLineSprite(frame, targetScreenPos + new Vector2(bracketSize/2, bracketSize/2),
                                    targetScreenPos + new Vector2(bracketSize/2, bracketSize/2 - cornerLength),
                                    bracketThickness, bracketColor);

                // Draw data readout (below bracket)
                float textY = targetScreenPos.Y + bracketSize/2 + 5f;
                float textScale = 0.5f;

                // Range
                string rangeText = range >= 1000 ? $"{range/1000:F1}km" : $"{range:F0}m";
                var rangeSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = rangeText,
                    Position = new Vector2(targetScreenPos.X, textY),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                };
                frame.Add(rangeSprite);

                // Closure rate
                string closureText = $"Vc:{closureRate:+0;-0}";
                var closureSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = closureText,
                    Position = new Vector2(targetScreenPos.X, textY + 12f),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                };
                frame.Add(closureSprite);

                // Aspect angle
                string aspectText = $"AA:{aspectAngle:F0}°";
                var aspectSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = aspectText,
                    Position = new Vector2(targetScreenPos.X, textY + 24f),
                    RotationOrScale = textScale,
                    Color = bracketColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                };
                frame.Add(aspectSprite);
            }

            // --- 2. WEAPON STATUS PANEL (for weapon screen) ---
            private void DrawWeaponStatusPanelToScreen(MySpriteDrawFrame frame, Jet myjet, float panelX, float panelY, float panelWidth)
            {
                const float PANEL_HEIGHT = 90f;
                const float TEXT_SCALE = 0.7f;
                const float LINE_HEIGHT = 18f;

                // Draw panel outline
                DrawRectangleOutline(frame, panelX, panelY, panelWidth, PANEL_HEIGHT, 2f, HUD_PRIMARY);

                float textX = panelX + 10f;
                float textY = panelY + 10f;

                // Weapon type
                string weaponText = "WPN: GAU-8";
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = weaponText,
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                // Ammo count (infinite for gatling)
                textY += LINE_HEIGHT;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "RND: \u221E",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                // Missile bay status
                textY += LINE_HEIGHT;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "MSL:",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });

                // Draw missile bay grid (squares for each bay)
                float baySquareSize = 10f;
                float bayStartX = textX + 35f;
                float bayY = textY + 2f;

                int maxBays = Math.Min(8, myjet._bays.Count);
                for (int i = 0; i < maxBays; i++)
                {
                    bool bayLoaded = myjet._bays[i].IsConnected;
                    Color bayColor = bayLoaded ? HUD_PRIMARY : HUD_WARNING;

                    float bayX = bayStartX + (i % 4) * (baySquareSize + 3f);
                    float currentBayY = bayY + (i / 4) * (baySquareSize + 3f);

                    if (bayLoaded)
                    {
                        // Filled square for loaded
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(bayX + baySquareSize/2, currentBayY + baySquareSize/2),
                            Size = new Vector2(baySquareSize, baySquareSize),
                            Color = bayColor,
                            Alignment = TextAlignment.CENTER
                        });
                    }
                    else
                    {
                        // Empty outline for expended
                        DrawRectangleOutline(frame, bayX, currentBayY, baySquareSize, baySquareSize, 1f, bayColor);
                    }
                }

                // Targeting pod status (check if active slot has target)
                textY += LINE_HEIGHT * 2;
                bool radarTracking = myjet.targetSlots[myjet.activeSlotIndex].IsOccupied;
                string podStatus = radarTracking ? "LOCK \u2713" : "----";
                Color podColor = radarTracking ? HUD_PRIMARY : HUD_WARNING;
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"POD: {podStatus}",
                    Position = new Vector2(textX, textY),
                    RotationOrScale = TEXT_SCALE,
                    Color = podColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = "Monospace"
                });
            }

            // --- 3. GUN FUNNEL VISUALIZATION ---
            private void DrawGunFunnel(
                MySpriteDrawFrame frame,
                IMyCockpit cockpit,
                IMyTextSurface hud,
                Vector3D interceptPoint,
                Vector3D shooterPosition,
                double range,
                bool isAimingAtPip
            )
            {
                if (cockpit == null || hud == null) return;

                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                // Calculate funnel width based on range (wider at longer range)
                float funnelWidthFactor = (float)MathHelper.Clamp(range / 2000.0, 0.05, 0.3);
                float funnelBaseWidth = surfaceSize.X * funnelWidthFactor;

                // Project intercept point
                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector3D directionToIntercept = interceptPoint - shooterPosition;
                Vector3D localDirectionToIntercept = Vector3D.TransformNormal(directionToIntercept, worldToCockpitMatrix);

                if (localDirectionToIntercept.Z >= 0) return;

                if (Math.Abs(localDirectionToIntercept.Z) < MIN_Z_FOR_PROJECTION)
                    localDirectionToIntercept.Z = -MIN_Z_FOR_PROJECTION;

                const float COCKPIT_FOV_SCALE_X = 0.3434f;
                const float COCKPIT_FOV_SCALE_Y = 0.31f;
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;
                float screenX = center.X + (float)(localDirectionToIntercept.X / -localDirectionToIntercept.Z) * scaleX;
                float screenY = center.Y + (float)(-localDirectionToIntercept.Y / -localDirectionToIntercept.Z) * scaleY;
                Vector2 pipScreenPos = new Vector2(screenX, screenY);

                // Draw funnel lines from screen corners to pip
                Color funnelColor = new Color(HUD_PRIMARY, 0.3f); // Semi-transparent
                float lineThickness = 1f;

                // Four funnel lines converging to pip
                Vector2[] edgePoints = new Vector2[]
                {
                    new Vector2(center.X - funnelBaseWidth/2, 0),
                    new Vector2(center.X + funnelBaseWidth/2, 0),
                    new Vector2(center.X + funnelBaseWidth/2, surfaceSize.Y),
                    new Vector2(center.X - funnelBaseWidth/2, surfaceSize.Y)
                };

                foreach (var edgePoint in edgePoints)
                {
                    AddLineSprite(frame, edgePoint, pipScreenPos, lineThickness, funnelColor);
                }

                // Draw firing cue if aiming at pip
                if (isAimingAtPip && range < 2500)
                {
                    string cueText = range < 1500 ? "SHOOT" : "IN RANGE";
                    Color cueColor = range < 1500 ? HUD_WARNING : HUD_EMPHASIS;

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = cueText,
                        Position = new Vector2(center.X, center.Y - 60f),
                        RotationOrScale = 1.0f,
                        Color = cueColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });
                }
            }

            // --- 4. FUEL RING INDICATOR ---
            private void DrawFuelRing(MySpriteDrawFrame frame, List<IMyGasTank> tanks)
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

                // Arc parameters
                Vector2 center = new Vector2(hud.SurfaceSize.X / 2f, 50f);
                float radius = hud.SurfaceSize.X * 0.35f;
                float arcThickness = 8f;
                float arcSpan = 120f; // degrees

                // Color based on fuel level
                Color fuelColor;
                if (fuelPercent < BINGO_FUEL_PERCENT)
                    fuelColor = HUD_WARNING; // Red
                else if (fuelPercent < LOW_FUEL_PERCENT)
                    fuelColor = HUD_EMPHASIS; // Yellow
                else
                    fuelColor = HUD_PRIMARY; // Green

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

                    // Only draw if within filled portion
                    Color segmentColor = angle2 <= filledAngle ? fuelColor : new Color(fuelColor, 0.2f);

                    AddLineSprite(frame, p1, p2, arcThickness, segmentColor);
                }

                // Draw fuel percentage text
                string fuelText = $"FUEL {fuelPercent*100:F0}%";
                if (fuelPercent < BINGO_FUEL_PERCENT)
                    fuelText = "BINGO FUEL";

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = fuelText,
                    Position = new Vector2(center.X, center.Y + 15f),
                    RotationOrScale = 0.6f,
                    Color = fuelColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });

                // Estimated flight time
                if (fuelPercent > 0.01)
                {
                    double burnRate = 0.1; // Rough estimate, would need actual calculation
                    double timeRemaining = (totalFilled / totalCapacity) * 600; // seconds estimate
                    int minutes = (int)(timeRemaining / 60);
                    int seconds = (int)(timeRemaining % 60);

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = $"{minutes:D2}:{seconds:D2}",
                        Position = new Vector2(center.X, center.Y + 28f),
                        RotationOrScale = 0.5f,
                        Color = fuelColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "Monospace"
                    });
                }
            }

            // --- 5. MULTI-TARGET THREAT DISPLAY (for weapon screen) ---
            private void DrawMultiTargetPanelToScreen(MySpriteDrawFrame frame, Vector3D[] targetPositions, Vector3D shooterPosition, float panelX, float startY, float panelWidth)
            {
                if (targetPositions == null || targetPositions.Length < 1) return;

                const float LINE_HEIGHT = 22f;
                const float TEXT_SCALE = 0.7f;

                float textX = panelX + 10f;
                float textY = startY;

                // Draw all targets (including primary at index 0)
                for (int i = 0; i < Math.Min(5, targetPositions.Length); i++)
                {
                    double range = Vector3D.Distance(shooterPosition, targetPositions[i]);
                    bool isPrimary = (i == 0);

                    // Filled/empty circle indicator
                    Color targetColor = isPrimary ? HUD_WARNING : HUD_RADAR_FRIENDLY;
                    string circleSymbol = isPrimary ? "\u25C9" : "\u25CB"; // Filled or empty circle
                    string targetLabel = isPrimary ? "PRI" : $"T{i}";

                    string targetText = $"{circleSymbol} {targetLabel}";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = targetText,
                        Position = new Vector2(textX, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "Monospace"
                    });

                    // Range bar
                    float barMaxWidth = 80f;
                    float barHeight = 8f;
                    float barX = textX + 50f;
                    float barWidth = MathHelper.Clamp((float)(range / 15000.0 * barMaxWidth), 2f, barMaxWidth);

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(barX, textY + 5f),
                        Size = new Vector2(barWidth, barHeight),
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT
                    });

                    // Range text
                    string rangeText = range >= 1000 ? $"{range/1000:F1}km" : $"{range:F0}m";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = rangeText,
                        Position = new Vector2(barX + barMaxWidth + 10f, textY),
                        RotationOrScale = TEXT_SCALE,
                        Color = targetColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "Monospace"
                    });

                    textY += LINE_HEIGHT;
                }
            }

            // --- 6. AOA INDEXER WITH ENERGY STATE ---
            private void DrawAOAIndexer(MySpriteDrawFrame frame, double aoa, Vector3D acceleration, double velocity)
            {
                const float INDEXER_X = 100f;
                float indexerY = hud.SurfaceSize.Y / 2f;
                const float SYMBOL_SIZE = 18f;

                // Optimal AOA range for turning (example values)
                const double OPTIMAL_AOA_MIN = 8.0;
                const double OPTIMAL_AOA_MAX = 15.0;

                Color indexerColor;
                string spriteType;

                if (aoa < OPTIMAL_AOA_MIN)
                {
                    // Low AOA - show UP chevron (pitch up to increase AOA)
                    indexerColor = HUD_PRIMARY; // Green
                    spriteType = "Triangle"; // Will rotate to point up

                    // Draw upward pointing triangle
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = spriteType,
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE, SYMBOL_SIZE),
                        RotationOrScale = 0f, // Point up
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
                else if (aoa > OPTIMAL_AOA_MAX)
                {
                    // High AOA - show DOWN chevron (pitch down to decrease AOA)
                    indexerColor = HUD_WARNING; // Red
                    spriteType = "Triangle";

                    // Draw downward pointing triangle
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = spriteType,
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE, SYMBOL_SIZE),
                        RotationOrScale = MathHelper.Pi, // Rotate 180° to point down
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
                else
                {
                    // Optimal AOA - show CIRCLE
                    indexerColor = HUD_EMPHASIS; // Yellow

                    // Draw circle for optimal AOA
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = new Vector2(INDEXER_X, indexerY),
                        Size = new Vector2(SYMBOL_SIZE * 0.8f, SYMBOL_SIZE * 0.8f),
                        Color = indexerColor,
                        Alignment = TextAlignment.CENTER
                    });
                }

                // Energy state indicator (E-bracket) - shows if gaining/losing energy
                double energyRate = acceleration.Length(); // Simplified energy calculation
                string energySymbol = energyRate > 5 ? "+" : energyRate < -5 ? "-" : "=";
                Color energyColor = energyRate > 5 ? HUD_PRIMARY : energyRate < -5 ? HUD_WARNING : HUD_EMPHASIS;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"E{energySymbol}",
                    Position = new Vector2(INDEXER_X, indexerY + 25f),
                    RotationOrScale = 0.5f,
                    Color = energyColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace"
                });
            }

            // --- 8. HORIZON BANK ANGLE MARKERS ---
            private void DrawBankAngleMarkers(MySpriteDrawFrame frame, float centerX, float centerY, float roll, float pixelsPerDegree)
            {
                // Bank angle tick marks at 15°, 30°, 45°, 60°
                int[] bankAngles = new int[] { 15, 30, 45, 60, -15, -30, -45, -60 };
                float horizonRadius = pixelsPerDegree * 20f; // Distance from center

                float rollRad = MathHelper.ToRadians(-roll);
                float cosRoll = (float)Math.Cos(rollRad);
                float sinRoll = (float)Math.Sin(rollRad);

                foreach (int angle in bankAngles)
                {
                    float angleRad = MathHelper.ToRadians(angle);
                    Vector2 tickPos = new Vector2((float)Math.Sin(angleRad) * horizonRadius, -(float)Math.Cos(angleRad) * horizonRadius);

                    // Rotate by current roll
                    Vector2 rotatedTick = new Vector2(
                        tickPos.X * cosRoll - tickPos.Y * sinRoll,
                        tickPos.X * sinRoll + tickPos.Y * cosRoll
                    );

                    Vector2 finalPos = new Vector2(centerX, centerY) + rotatedTick;

                    // Draw small tick mark
                    bool isMajor = (Math.Abs(angle) % 30 == 0);
                    float tickLength = isMajor ? 8f : 5f;
                    Color tickColor = isMajor ? HUD_EMPHASIS : HUD_SECONDARY;

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = finalPos,
                        Size = new Vector2(2f, tickLength),
                        Color = tickColor,
                        Alignment = TextAlignment.CENTER,
                        RotationOrScale = angleRad + rollRad
                    });
                }
            }

            // --- 9. MISSILE TIME-OF-FLIGHT COUNTDOWN (for weapon screen) ---
            private void DrawMissileTOFToScreen(MySpriteDrawFrame frame, float centerX, float startY)
            {
                if (activeMissiles.Count == 0) return;

                const float TEXT_SCALE = 0.7f;
                const float LINE_HEIGHT = 20f;

                // Clean up expired missiles
                activeMissiles.RemoveAll(m => (totalElapsedTime - m.LaunchTime).TotalSeconds > m.EstimatedTOF + 5);

                for (int i = 0; i < Math.Min(5, activeMissiles.Count); i++)
                {
                    var missile = activeMissiles[i];
                    double timeRemaining = missile.EstimatedTOF - (totalElapsedTime - missile.LaunchTime).TotalSeconds;

                    if (timeRemaining > 0)
                    {
                        string tofText = $"MSL {missile.BayIndex + 1}: {timeRemaining:F1}s \u2192 TGT";
                        Color tofColor = timeRemaining < 3 ? HUD_WARNING : HUD_EMPHASIS;

                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = tofText,
                            Position = new Vector2(centerX, startY + i * LINE_HEIGHT),
                            RotationOrScale = TEXT_SCALE,
                            Color = tofColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = "Monospace"
                        });
                    }
                }
            }

            // --- 10. RADAR SWEEP ANIMATION ---
            private void DrawRadarSweepLine(MySpriteDrawFrame frame, Vector2 radarCenter, float radarRadius)
            {
                radarSweepTick = (radarSweepTick + 1) % 360;
                float sweepAngle = radarSweepTick * 2f; // 2 degrees per tick

                float sweepRad = MathHelper.ToRadians(sweepAngle);
                Vector2 sweepEnd = radarCenter + new Vector2(
                    (float)Math.Cos(sweepRad) * radarRadius,
                    (float)Math.Sin(sweepRad) * radarRadius
                );

                // Draw sweep line with fade
                Color sweepColor = new Color(HUD_EMPHASIS, 0.6f);
                AddLineSprite(frame, radarCenter, sweepEnd, 2f, sweepColor);

                //// Draw fading trail
                //for (int i = 1; i < 10; i++)
                //{
                //    float trailAngle = sweepAngle - i * 3f;
                //    float trailRad = MathHelper.ToRadians(trailAngle);
                //    Vector2 trailEnd = radarCenter + new Vector2(
                //        (float)Math.Cos(trailRad) * radarRadius,
                //        (float)Math.Sin(trailRad) * radarRadius
                //    );

                //    float alpha = (10 - i) / 10f * 0.4f;
                //    Color trailColor = new Color(HUD_EMPHASIS, alpha);
                //    AddLineSprite(frame, radarCenter, trailEnd, 1f, trailColor);
                //}
            }

            // --- RWR THREAT CONE VISUALIZATION ---
            private void DrawRWRThreatCones(MySpriteDrawFrame frame, IMyCockpit cockpit, Vector2 radarCenter, float radarRadius)
            {
                if (rwrModule == null || !rwrModule.IsEnabled)
                    return;

                // Get active threats from RWR
                var threats = rwrModule.activeThreats;
                if (threats.Count == 0)
                    return;

                Vector3D cockpitPos = cockpit.GetPosition();
                Vector3D worldUp = -Vector3D.Normalize(cockpit.GetNaturalGravity());

                // Build yaw-plane matrix (same as radar targets use)
                Vector3D shipForward = cockpit.WorldMatrix.Forward;
                Vector3D shipRight = cockpit.WorldMatrix.Right;

                Vector3D shipForwardProjected = shipForward - Vector3D.Dot(shipForward, worldUp) * worldUp;
                Vector3D shipRightProjected = shipRight - Vector3D.Dot(shipRight, worldUp) * worldUp;

                Vector3D yawForward;
                if (shipForwardProjected.LengthSquared() > 0.01)
                {
                    yawForward = Vector3D.Normalize(shipForwardProjected);
                }
                else
                {
                    yawForward = Vector3D.Cross(shipRightProjected, worldUp);
                }

                Vector3D yawRight = Vector3D.Cross(yawForward, worldUp);
                MatrixD yawMatrix = MatrixD.Identity;
                yawMatrix.Forward = yawForward;
                yawMatrix.Right = yawRight;
                yawMatrix.Up = worldUp;

                MatrixD worldToYawPlaneMatrix = MatrixD.Transpose(yawMatrix);

                foreach (var threat in threats)
                {
                    // Calculate direction to threat in yaw-plane coordinates (same as radar)
                    Vector3D directionToThreat = threat.Position - cockpitPos;
                    Vector3D localDirection = Vector3D.TransformNormal(directionToThreat, worldToYawPlaneMatrix);

                    // Project onto horizontal plane - X is right, Z is forward
                    Vector2 horizontalDirection = new Vector2((float)localDirection.X, (float)localDirection.Z);

                    if (horizontalDirection.LengthSquared() < 0.001f)
                        continue; // Threat directly above/below

                    horizontalDirection.Normalize();

                    // Calculate angle for cone (atan2 needs Y, X for screen coordinates where Y is forward/Z)
                    float angleRad = (float)Math.Atan2(horizontalDirection.Y, horizontalDirection.X);
                    float CONE_WIDTH_RAD = MathHelper.ToRadians(30f); // 30-degree cone

                    // Draw cone edges
                    float leftAngle = angleRad - CONE_WIDTH_RAD / 2f;
                    float rightAngle = angleRad + CONE_WIDTH_RAD / 2f;

                    Vector2 leftEdge = radarCenter + new Vector2(
                        (float)Math.Cos(leftAngle) * radarRadius,
                        (float)Math.Sin(leftAngle) * radarRadius
                    );

                    Vector2 rightEdge = radarCenter + new Vector2(
                        (float)Math.Cos(rightAngle) * radarRadius,
                        (float)Math.Sin(rightAngle) * radarRadius
                    );

                    // Choose color based on threat type
                    Color coneColor = threat.IsIncoming ? HUD_WARNING : HUD_EMPHASIS; // Red for incoming, Yellow for tracking
                    Color coneColorFaded = new Color(coneColor, 0.3f);

                    // Draw cone edges
                    AddLineSprite(frame, radarCenter, leftEdge, 2f, coneColor);
                    AddLineSprite(frame, radarCenter, rightEdge, 2f, coneColor);

                    // Draw arc connecting cone edges (approximate with line segments)
                    const int ARC_SEGMENTS = 8;
                    for (int i = 0; i < ARC_SEGMENTS; i++)
                    {
                        float t1 = i / (float)ARC_SEGMENTS;
                        float t2 = (i + 1) / (float)ARC_SEGMENTS;

                        float angle1 = leftAngle + t1 * CONE_WIDTH_RAD;
                        float angle2 = leftAngle + t2 * CONE_WIDTH_RAD;

                        Vector2 arc1 = radarCenter + new Vector2(
                            (float)Math.Cos(angle1) * radarRadius,
                            (float)Math.Sin(angle1) * radarRadius
                        );

                        Vector2 arc2 = radarCenter + new Vector2(
                            (float)Math.Cos(angle2) * radarRadius,
                            (float)Math.Sin(angle2) * radarRadius
                        );

                        AddLineSprite(frame, arc1, arc2, 1f, coneColorFaded);
                    }

                    // Draw threat indicator at cone center
                    Vector2 threatIndicator = radarCenter + new Vector2(
                        (float)Math.Cos(angleRad) * (radarRadius * 0.9f),
                        (float)Math.Sin(angleRad) * (radarRadius * 0.9f)
                    );

                    // Draw threat symbol (triangle or exclamation)
                    if (threat.IsIncoming)
                    {
                        // Draw filled triangle for incoming threat
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "!",
                            Position = threatIndicator,
                            RotationOrScale = 0.6f,
                            Color = HUD_WARNING,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                    }
                    else
                    {
                        // Draw hollow triangle for tracking only
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXT,
                            Data = "^",
                            Position = threatIndicator,
                            RotationOrScale = 0.5f,
                            Color = HUD_EMPHASIS,
                            Alignment = TextAlignment.CENTER,
                            FontId = "White"
                        });
                    }
                }
            }

            // --- 11. BREAKAWAY CUE SYSTEM ---
            private void DrawBreakawayWarning(MySpriteDrawFrame frame, double altitude, Vector3D velocity, Vector3D targetPosition, Vector3D shooterPosition)
            {
                bool lowAltitudeWarning = altitude < 100 && velocity.Y < -5;
                bool collisionWarning = false;

                // Check collision course with target
                if (targetPosition != Vector3D.Zero)
                {
                    double range = Vector3D.Distance(shooterPosition, targetPosition);
                    Vector3D relativeVelocity = velocity;
                    Vector3D toTarget = Vector3D.Normalize(targetPosition - shooterPosition);
                    double closureRate = -Vector3D.Dot(relativeVelocity, toTarget);

                    if (range < 500 && closureRate > 100)
                        collisionWarning = true;
                }

                if (!lowAltitudeWarning && !collisionWarning) return;

                // Draw large X warning
                Vector2 center = hud.SurfaceSize / 2f;
                float xSize = hud.SurfaceSize.X * 0.4f;
                Color warningColor = HUD_WARNING;
                float lineThickness = 4f;

                // Flash the warning
                if ((radarSweepTick / 10) % 2 == 0)
                {
                    AddLineSprite(frame, center - new Vector2(xSize/2, xSize/2), center + new Vector2(xSize/2, xSize/2), lineThickness, warningColor);
                    AddLineSprite(frame, center - new Vector2(xSize/2, -xSize/2), center + new Vector2(xSize/2, -xSize/2), lineThickness, warningColor);

                    string warningText = lowAltitudeWarning ? "PULL UP" : "BREAK AWAY";
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = warningText,
                        Position = new Vector2(center.X, center.Y + xSize/2 + 20f),
                        RotationOrScale = 1.2f,
                        Color = warningColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });
                }
            }

            // --- 12. GHOST AIRCRAFT FORMATION ASSIST ---
            private void DrawFormationGhosts(MySpriteDrawFrame frame, IMyCockpit cockpit, IMyTextSurface hud)
            {
                // Parse wingman GPS positions from CustomData (format: Wingman1:GPS:Name:X:Y:Z:)
                var customDataLines = ParentProgram.Me.CustomData.Split('\n');
                List<Vector3D> wingmanPositions = new List<Vector3D>();

                foreach (var line in customDataLines)
                {
                    if (line.StartsWith("Wingman"))
                    {
                        // Simple GPS parsing for wingman positions
                        var parts = line.Split(':');
                        if (parts.Length >= 6)
                        {
                            Vector3D wingmanPos;
                            if (double.TryParse(parts[3], out wingmanPos.X) &&
                                double.TryParse(parts[4], out wingmanPos.Y) &&
                                double.TryParse(parts[5], out wingmanPos.Z))
                            {
                                wingmanPositions.Add(wingmanPos);
                            }
                        }
                    }
                }

                if (wingmanPositions.Count == 0) return;

                Vector3D shooterPosition = cockpit.GetPosition();
                MatrixD worldToCockpitMatrix = MatrixD.Invert(cockpit.WorldMatrix);
                Vector2 surfaceSize = hud.SurfaceSize;
                Vector2 center = surfaceSize / 2f;

                const float COCKPIT_FOV_SCALE_X = 0.3434f;
                const float COCKPIT_FOV_SCALE_Y = 0.31f;
                float scaleX = surfaceSize.X / COCKPIT_FOV_SCALE_X;
                float scaleY = surfaceSize.Y / COCKPIT_FOV_SCALE_Y;

                foreach (var wingmanPos in wingmanPositions)
                {
                    Vector3D directionToWingman = wingmanPos - shooterPosition;
                    Vector3D localDirection = Vector3D.TransformNormal(directionToWingman, worldToCockpitMatrix);

                    if (localDirection.Z >= 0) continue; // Behind us

                    if (Math.Abs(localDirection.Z) < MIN_Z_FOR_PROJECTION)
                        localDirection.Z = -MIN_Z_FOR_PROJECTION;

                    float screenX = center.X + (float)(localDirection.X / -localDirection.Z) * scaleX;
                    float screenY = center.Y + (float)(-localDirection.Y / -localDirection.Z) * scaleY;

                    // Draw simple aircraft symbol
                    Vector2 ghostPos = new Vector2(screenX, screenY);
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Triangle",
                        Position = ghostPos,
                        Size = new Vector2(15f, 15f),
                        Color = new Color(HUD_RADAR_FRIENDLY, 0.7f),
                        Alignment = TextAlignment.CENTER
                    });
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

                // Calculate Azimuth and Elevation from target tracking data
                double radarX = 0.0;  // -1.0 ... +1.0
                double radarY = 0.0;  // -1.0 ... +1.0

                if (myjet.targetSlots[myjet.activeSlotIndex].IsOccupied)
                {
                    Vector3D targetPos = myjet.targetSlots[myjet.activeSlotIndex].Position;
                    Vector3D cockpitPos = myjet._cockpit.GetPosition();
                    Vector3D toTarget = targetPos - cockpitPos;

                    // Get cockpit forward and up vectors
                    MatrixD cockpitMatrix = myjet._cockpit.WorldMatrix;
                    Vector3D forward = cockpitMatrix.Forward;
                    Vector3D up = cockpitMatrix.Up;
                    Vector3D right = cockpitMatrix.Right;

                    // Project target onto forward plane to get azimuth
                    double forwardDist = Vector3D.Dot(toTarget, forward);
                    double rightDist = Vector3D.Dot(toTarget, right);
                    double upDist = Vector3D.Dot(toTarget, up);

                    // Calculate angles (normalized to -1...+1 range, assuming ~90 degree FOV)
                    if (forwardDist > 0)
                    {
                        radarX = Math.Max(-1.0, Math.Min(1.0, rightDist / forwardDist));
                        radarY = Math.Max(-1.0, Math.Min(1.0, -upDist / forwardDist));
                    }
                }

                // --- Draw boundary lines ---------------------------------

                // Top horizontal line
                MySprite topLineSprite = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(boxCenterX, boxCenterY - halfHeight),
                    Size = new Vector2(boxWidth, 2),
                    Color = HUD_PRIMARY,
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
                    Color = HUD_PRIMARY,
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
                    Color = HUD_PRIMARY,
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
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(rightLineSprite);
                string targetName = "null";
                if (myjet.targetSlots[myjet.activeSlotIndex].IsOccupied && !string.IsNullOrEmpty(myjet.targetSlots[myjet.activeSlotIndex].Name))
                {
                    targetName = myjet.targetSlots[myjet.activeSlotIndex].Name;
                }
                MySprite targettext = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "Tar:" + targetName,
                    Position = new Vector2(boxCenterX + 30f, boxCenterY - 40f),
                    RotationOrScale = 0.75f,
                    Color = HUD_PRIMARY,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White"
                };
                frame.Add(targettext);

                // --- Draw active target radar dot (main target) ----------
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
                    Color = HUD_WARNING,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(radarDot);

                // --- Draw all enemy contacts from enemy list with decay colors ---
                var closestEnemies = myjet.GetClosestNEnemies(10); // Get 10 closest
                foreach (var enemy in closestEnemies)
                {
                    Vector3D enemyPos = enemy.Position;
                    Vector3D cockpitPos = myjet._cockpit.GetPosition();
                    Vector3D toEnemy = enemyPos - cockpitPos;

                    // Get cockpit orientation
                    MatrixD cockpitMatrix = myjet._cockpit.WorldMatrix;
                    Vector3D forward = cockpitMatrix.Forward;
                    Vector3D right = cockpitMatrix.Right;
                    Vector3D up = cockpitMatrix.Up;

                    // Project enemy onto forward plane
                    double forwardDist = Vector3D.Dot(toEnemy, forward);
                    double rightDist = Vector3D.Dot(toEnemy, right);
                    double upDist = Vector3D.Dot(toEnemy, up);

                    // Calculate radar position
                    double enemyRadarX = 0.0;
                    double enemyRadarY = 0.0;

                    if (forwardDist > 0)
                    {
                        enemyRadarX = Math.Max(-1.0, Math.Min(1.0, rightDist / forwardDist));
                        enemyRadarY = Math.Max(-1.0, Math.Min(1.0, -upDist / forwardDist));
                    }

                    // Get decay color based on age
                    Color enemyColor = myjet.GetEnemyContactColor(enemy);

                    // Draw enemy blip
                    MySprite enemyBlip = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = new Vector2(
                            boxCenterX + (float)enemyRadarX * halfWidth * -1,
                            boxCenterY + (float)enemyRadarY * halfHeight * -1
                        ),
                        Size = new Vector2(6, 6),
                        Color = enemyColor,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(enemyBlip);

                    // Draw small source indicator (AI1, AI2, RAY, etc.)
                    string sourceLabel = enemy.SourceIndex == -1 ? "RAY" : $"A{enemy.SourceIndex + 1}";
                    MySprite sourceText = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = sourceLabel,
                        Position = new Vector2(
                            boxCenterX + (float)enemyRadarX * halfWidth * -1 + 8,
                            boxCenterY + (float)enemyRadarY * halfHeight * -1 - 3
                        ),
                        RotationOrScale = 0.3f,
                        Color = enemyColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = "White"
                    };
                    frame.Add(sourceText);
                }
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
                    Color lineColor = HUD_PRIMARY;

                    // 1) MAIN HORIZONTAL SEGMENT - Split into two parts (F-16/F-18 style)
                    // Creates a gap in the center for the flight path marker
                    // Left segment at 75% of centerX, right segment at 125% of centerX
                    float halfWidth = lineWidth * 1.225f; //So it clips a tiny bit
                    sprites.Add(
                        new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(centerX * 0.75f, markerY), // Left segment
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
                            Position = new Vector2(centerX * 1.25f, markerY), // Right segment
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
                // Also split into two segments with center gap (same style as pitch lines)
                float horizonY = centerY + pitch * pixelsPerDegree;
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX * 1.25f, horizonY), // Right segment
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = HUD_HORIZON,
                        Alignment = TextAlignment.CENTER
                    }
                );
                sprites.Add(
                    new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(centerX * 0.75f, horizonY), // Left segment
                        Size = new Vector2(hud.SurfaceSize.X * 0.125f, 4f),
                        Color = HUD_HORIZON,
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
                        Color = HUD_EMPHASIS,
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

                // FIX: Removed debug Echo that was spamming output every frame
                // ParentProgram.Echo($"DrawCompass Heading: {heading:F2}");

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
                        Color markerColor = isMajorTick ? HUD_SECONDARY : HUD_PRIMARY;

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
                    Color = HUD_EMPHASIS,
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

                // FIX: Convert roll from [0, 360] to [-180, 180] for intuitive display
                // After normalization, upright = 180°, so subtract 180 to make upright = 0°
                // This gives: 0° upright, +90° right wing down, -90° left wing down, ±180° inverted
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



            private List<string> motivationalTexts = new List<string>
            {
                "Innovate for a better\ntomorrow",
                "Success is a journey,\nnot a destination",
                "Every challenge is\nan opportunity",
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

            // OPTIMIZED: Replaced expensive Mandelbrot rendering with simple animated text
            private void RenderParticles(MySpriteDrawFrame frame, RectangleF area)
            {
                float time = animationcounter / 60.0f;
                Vector2 resolution = new Vector2(area.Width, area.Height);
                Vector2 center = resolution / 2.0f;

                // Draw animated "JetOS" logo
                string logoText = "JetOS";
                float logoScale = 3.0f + (float)Math.Sin(time * 2) * 0.3f; // Pulsing effect
                Color logoColor = new Color(
                    (int)(128 + 127 * Math.Sin(time)),
                    (int)(128 + 127 * Math.Sin(time + 2)),
                    (int)(128 + 127 * Math.Sin(time + 4))
                );

                var logoSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = logoText,
                    Position = center,
                    RotationOrScale = logoScale,
                    Color = logoColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                };
                frame.Add(logoSprite);

                // Draw some simple animated stars (much cheaper than Mandelbrot!)
                int starCount = 20;
                for (int i = 0; i < starCount; i++)
                {
                    float angle = (time + i) * 0.1f;
                    float radius = 100 + i * 15;
                    Vector2 starPos = center + new Vector2(
                        (float)Math.Cos(angle) * radius,
                        (float)Math.Sin(angle) * radius
                    );

                    var starSprite = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = starPos,
                        Size = new Vector2(2, 2),
                        Color = Color.White,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(starSprite);
                }

                // Show motivational text below logo
                int textIndex = (animationcounter / 240) % motivationalTexts.Count;
                string motivText = motivationalTexts[textIndex];

                var textSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = motivText,
                    Position = center + new Vector2(0, 100),
                    RotationOrScale = 0.8f,
                    Color = Color.LightGray,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                };
                frame.Add(textSprite);
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

            // SAFETY: Ensure baySelected array matches missileBays count
            private void EnsureBayArraySynced()
            {
                if (baySelected == null || baySelected.Length != missileBays.Count)
                {
                    var oldArray = baySelected;
                    baySelected = new bool[missileBays.Count];

                    // Preserve old selections if possible
                    if (oldArray != null)
                    {
                        int copyLength = Math.Min(oldArray.Length, baySelected.Length);
                        for (int i = 0; i < copyLength; i++)
                        {
                            baySelected[i] = oldArray[i];
                        }
                    }
                }
            }

            public override string[] GetOptions()
            {
                EnsureBayArraySynced(); // Safety check

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
                    string baySymbol = (i < baySelected.Length) ? (baySelected[i] ? "[X]" : "[ ]") : "[ ]";
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
                            return;
                        }
                        catch (Exception e)
                        {
                            // Continue to next bay instead of crashing
                        }
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
                catch (Exception e)
                {
                }
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
                catch (Exception e)
                {
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

        // Radar Tracking Module using AI Blocks
        public class RadarTrackingModule
        {
            //===============================================================================================
            //This Is A Pretty Generic Targeting Class, I Have Kept It Relatively CLean And Understandable
            //At Runtime It Is Fairly Lightweight, But Don't Spam It a call to 'position' does invoke some logic
            //- needs you to update the tracking info every frame
            //- will throw nullreference if the blocks are destroyed
            //- Use the boost mode to use monkaspeed tracking

            //Used Instead Of A Tuple (keen ree)
            struct TrackingPoint
            {
                public readonly Vector3D Position;
                public readonly double Timestamp;
                public TrackingPoint(Vector3D position, double timestamp)
                {
                    this.Position = position;
                    this.Timestamp = timestamp;
                }
            }

            //Keeps Record Of The Flight Module
            public IMyFlightMovementBlock L_FlightBlock;
            public IMyOffensiveCombatBlock L_CombatBLock;
            public bool BoostMode = false;

            // Store last two (position, timestamp) entries
            TrackingPoint p1;
            TrackingPoint p0;

            //Counting Positions
            public long CurrentTime;
            public int CurrentTick;
            const int ForcedRefreshRate = 40; //this is used to force a position relog on static grids

            /// <summary>
            /// Constructor, takes flight and combat AI blocks
            /// </summary>
            /// <param name="LBlock_F">The flight block to use</param>
            /// <param name="LBlockC">The combat block to use</param>
            public RadarTrackingModule(IMyFlightMovementBlock LBlock_F, IMyOffensiveCombatBlock LBlockC)
            {
                //Sets
                L_FlightBlock = LBlock_F;
                L_CombatBLock = LBlockC;

                //AI Move Block Settings Used For Continual Tracking
                L_FlightBlock.Enabled = false; // Must be DISABLED to prevent autopilot control
                L_FlightBlock.MinimalAltitude = 10; //possibly could be larger
                L_FlightBlock.PrecisionMode = false;
                L_FlightBlock.SpeedLimit = 400;
                L_FlightBlock.AlignToPGravity = false;
                L_FlightBlock.CollisionAvoidance = false;
                L_FlightBlock.ApplyAction("ActivateBehavior_On"); // Behavior ON allows receiving waypoints from Combat Block

                //AI combat block settings
                L_CombatBLock.Enabled = true;
                L_CombatBLock.UpdateTargetInterval = 4;
                L_CombatBLock.SearchEnemyComponent.TargetingLockOptions = VRage.Game.ModAPI.Ingame.MyGridTargetingRelationFiltering.Enemy;
                L_CombatBLock.SelectedAttackPattern = 3; //Sets To Intercept Mode
                L_CombatBLock.SetValue<long>("OffensiveCombatIntercept_GuidanceType", 0); // 1 target prediction, 0 basic
                L_CombatBLock.SetValueBool("OffensiveCombatIntercept_OverrideCollisionAvoidance", true); //Sets To Ignore All Collision Detection
                L_CombatBLock.ApplyAction("ActivateBehavior_On");
                L_CombatBLock.ApplyAction("SetTargetingGroup_Weapons");
                L_CombatBLock.ApplyAction("SetTargetPriority_Closest");
            }

            /// <summary>
            /// Call This Before Using Any Of The Properties, Updates Position
            /// </summary>
            public void UpdateTracking(long CurrentPBTime_Ticks)
            {
                //Updates Time
                CurrentTime = CurrentPBTime_Ticks;

                // Retrieves the flight block's waypoint
                IMyAutopilotWaypoint currentWaypoint = L_FlightBlock.CurrentWaypoint;

                // Null check, or check if block is currently tracking
                if (currentWaypoint != null)
                {
                    //NB this can be up to 2 ticks out of date due to the asynch nature of this
                    Vector3D TargetPosition = currentWaypoint.Matrix.Translation;

                    //Need To Use This As Otherwise Gives False Data
                    if (TargetPosition != p0.Position || CurrentTick > ForcedRefreshRate)
                    {
                        // Shift historical data
                        p1 = p0;
                        p0 = new TrackingPoint(TargetPosition, CurrentTime);

                        //Resets Counter
                        CurrentTick = 0;
                    }
                    else
                    {
                        //Increments
                        CurrentTick++;
                    }
                }
            }

            /// <summary>
            /// Gets the most recent velocity vector.
            /// </summary>
            public Vector3D TargetVelocity
            {
                get
                {
                    // Extract position and time from the stored tracking points
                    Vector3D pos1 = p1.Position;
                    double time1 = p1.Timestamp;

                    Vector3D pos0 = p0.Position;
                    double time0 = p0.Timestamp;

                    //Calculates protecting against zero time errors (would give NaN)
                    double dt = time0 - time1;
                    if (dt <= 0) return Vector3D.Zero;

                    //Returns
                    return (pos0 - pos1) / (double)dt;
                }
            }

            /// <summary>
            /// Predicts the target's position using current velocity and acceleration.
            /// </summary>
            public Vector3D TargetPosition
            {
                get
                {

                    //This Is Emergency Ultra Burn, Use Only In Emergencies As Very Performance Intensive
                    if (BoostMode)
                    {
                        L_CombatBLock.Enabled = false;
                        L_CombatBLock.Enabled = true;
                        var CurrentWaypoint = L_FlightBlock.CurrentWaypoint;
                        var positionwaypoint = CurrentWaypoint.Matrix.GetRow(3);
                        return new Vector3D(positionwaypoint.X, positionwaypoint.Y, positionwaypoint.Z);
                    }

                    // Extracts Current Position
                    Vector3D lastPosition = p0.Position;
                    double lastTime = p0.Timestamp;

                    //Gets V and A
                    Vector3D velocity = TargetVelocity;

                    //Timestep
                    double dt = (double)(CurrentTime - lastTime);

                    //S1 = S0 + UT + 0.5AT^2 (simple suvat equation)
                    return lastPosition + velocity * dt; //found is more stable withoput acceleration term, as its 1.6s of error
                }
            }

            /// <summary>
            /// Tells You If Is Tracking Or Not, If This Is True It Is Actively Seeking
            /// </summary>
            public bool IsTracking
            {
                get
                {
                    return L_CombatBLock.SearchEnemyComponent.FoundEnemyId == null ? false : true;
                }
            }

            /// <summary>
            /// Tells You Tracked Object Name
            /// </summary>
            public string TrackedObjectName
            {
                get
                {
                    //this is
                    string detailedInfo = L_CombatBLock.DetailedInfo;

                    // Split by new lines
                    var lines = detailedInfo.Split('\n');

                    return lines[0];
                }
            }

            /// <summary>
            /// Checks State Of Blocks Internal
            /// </summary>
            public bool CheckWorking(out string errormsg)
            {
                if (L_FlightBlock == null || L_FlightBlock.CubeGrid.GetCubeBlock(L_FlightBlock.Position) == null || !L_FlightBlock.IsWorking)
                { errormsg = " ~ AI Flight Block Not Found,\nInstall Block And Press Recompile"; return false; }
                if (L_CombatBLock == null || L_CombatBLock.CubeGrid.GetCubeBlock(L_CombatBLock.Position) == null || !L_CombatBLock.IsWorking)
                { errormsg = " ~ AI Combat Block Not Found,\nInstall Block And Press Recompile"; return false; }
                errormsg = null;
                return true;
            }

            /// <summary>
            /// Tells You What Is Setup For Line 1 (largest, smallest, closest)
            /// </summary>
            public string GetLine1Info()
            {
                return L_CombatBLock.TargetPriority + "";
            }

            /// <summary>
            /// Tells You What Is Setup For Line 2 (weapons, thrusters etc)
            /// </summary>
            public string GetLine2Info()
            {
                return L_CombatBLock.SearchEnemyComponent.SubsystemsToDestroy + "";
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
            // Sound state machine (Space Engineers requires 1 action per tick)
            private List<int> soundStates = new List<int>(); // 0=idle, 1=stopping, 2=selecting, 3=playing
            private List<string> pendingSounds = new List<string>();
            private RadarTrackingModule radarTracker;
            private int ticket = 0;
            private Jet myJet; // Reference to the jet for updating target tracking data

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
                SystemManager.MarkCustomDataDirty();
            }
            public AirtoAir(Program program, Jet jet) : base(program)
            {
                // Store jet reference
                myJet = jet;

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
                soundStates = new List<int>();
                pendingSounds = new List<string>();
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
                    soundStates.Add(0); // Initialize to idle state
                    pendingSounds.Add("");
                }

                // Initialize radar tracker with AI blocks (backward compatibility - primary only)
                if (jet._aiFlightBlock != null && jet._aiCombatBlock != null)
                {
                    radarTracker = new RadarTrackingModule(jet._aiFlightBlock, jet._aiCombatBlock);
                }

                // Initialize scalable radar modules - auto-detect all AI Flight/Combat pairs
                for (int i = 1; i <= 10; i++) // Check up to 10 AI combos
                {
                    string flightName = i == 1 ? "AI Flight" : $"AI Flight {i}";
                    string combatName = i == 1 ? "AI Combat" : $"AI Combat {i}";

                    var flightBlock = program.GridTerminalSystem.GetBlockWithName(flightName) as IMyFlightMovementBlock;
                    var combatBlock = program.GridTerminalSystem.GetBlockWithName(combatName) as IMyOffensiveCombatBlock;

                    if (flightBlock != null && combatBlock != null)
                    {
                        // Create and add radar module for this combo
                        var radarModule = new RadarTrackingModule(flightBlock, combatBlock);
                        jet.radarModules.Add(radarModule);
                    }
                    else
                    {
                        // No more combos found
                        break;
                    }
                }

                program.Echo($"Total AI combos detected: {jet.radarModules.Count}");
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
                if (index == 0)
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
                    ToggleSensor();
                    ToggleAirtoAirMode();
                }
                else
                {
                    // Calculate bay index
                    int bayOffset = 3; // Base menu items
                    if (index >= bayOffset && index - bayOffset < missileBays.Count)
                    {
                        ToggleBaySelection(index - bayOffset);
                    }
                }
            }
            private void ToggleAirtoAirMode()
            {
                isAirtoAirenabled = !isAirtoAirenabled;
                UpdateTopdownCustomData();
            }
            private void ToggleSensor()
            {
                // Control AI blocks based on current state (ToggleAirtoAirMode will flip the state)
                // Note: This is called BEFORE ToggleAirtoAirMode, so we check the CURRENT state
                if (isAirtoAirenabled)
                {
                    // Currently ON, about to turn OFF - disable AI blocks
                    if (radarTracker != null)
                    {
                        if (radarTracker.L_CombatBLock != null)
                        {
                            radarTracker.L_CombatBLock.Enabled = false;
                            radarTracker.L_CombatBLock.ApplyAction("ActivateBehavior_Off");
                        }
                        if (radarTracker.L_FlightBlock != null)
                        {
                            radarTracker.L_FlightBlock.Enabled = false;
                            radarTracker.L_FlightBlock.ApplyAction("ActivateBehavior_Off"); // Disable both to fully stop tracking
                        }
                    }
                }
                else
                {
                    // Currently OFF, about to turn ON - enable and configure AI blocks
                    if (radarTracker != null)
                    {
                        if (radarTracker.L_CombatBLock != null)
                        {
                            radarTracker.L_CombatBLock.Enabled = true;
                            radarTracker.L_CombatBLock.UpdateTargetInterval = 4;
                            radarTracker.L_CombatBLock.SearchEnemyComponent.TargetingLockOptions = VRage.Game.ModAPI.Ingame.MyGridTargetingRelationFiltering.Enemy;
                            radarTracker.L_CombatBLock.SelectedAttackPattern = 3; // Intercept mode
                            radarTracker.L_CombatBLock.SetValue<long>("OffensiveCombatIntercept_GuidanceType", 0);
                            radarTracker.L_CombatBLock.SetValueBool("OffensiveCombatIntercept_OverrideCollisionAvoidance", true);
                            radarTracker.L_CombatBLock.ApplyAction("ActivateBehavior_On");
                            radarTracker.L_CombatBLock.ApplyAction("SetTargetingGroup_Weapons");
                            radarTracker.L_CombatBLock.ApplyAction("SetTargetPriority_Closest");
                        }
                        if (radarTracker.L_FlightBlock != null)
                        {
                            radarTracker.L_FlightBlock.Enabled = false; // Must be DISABLED to prevent autopilot control
                            radarTracker.L_FlightBlock.MinimalAltitude = 10;
                            radarTracker.L_FlightBlock.PrecisionMode = false;
                            radarTracker.L_FlightBlock.SpeedLimit = 400;
                            radarTracker.L_FlightBlock.AlignToPGravity = false;
                            radarTracker.L_FlightBlock.CollisionAvoidance = false;
                            radarTracker.L_FlightBlock.ApplyAction("ActivateBehavior_On"); // Behavior ON allows receiving waypoints from Combat Block
                        }
                    }
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
                ticket++;

                // ===== PASSIVE MODE: Scan and build enemy list without active lock =====
                if (!isAirtoAirenabled)
                {
                    // Update all radar modules
                    for (int i = 0; i < myJet.radarModules.Count; i++)
                    {
                        var radarModule = myJet.radarModules[i];
                        if (radarModule != null)
                        {
                            radarModule.UpdateTracking(ticket);

                            // If tracking, add to enemy list (no slot activation, no GPS update)
                            if (radarModule.IsTracking)
                            {
                                Vector3D targetPos = radarModule.TargetPosition;
                                Vector3D targetVel = radarModule.TargetVelocity;
                                string targetName = radarModule.TrackedObjectName;

                                // Add/update in enemy list
                                myJet.UpdateOrAddEnemy(targetPos, targetVel, targetName, i);

                                // Store in slot for HUD display but DON'T activate
                                int slotIndex = Program.FindEmptyOrOldestSlot(myJet);
                                myJet.targetSlots[slotIndex] = new Jet.TargetSlot(targetPos, targetVel, targetName);
                            }
                        }
                    }

                    // Decay old contacts every 60 ticks (~1 second)
                    if (ticket % 60 == 0)
                    {
                        myJet.UpdateEnemyDecay();
                    }
                }
                // ===== ACTIVE MODE: Lock closest N enemies and update missile GPS =====
                else
                {
                    // Get closest N enemies (N = number of radar modules)
                    var closestEnemies = myJet.GetClosestNEnemies(myJet.radarModules.Count);

                    // Update all radar modules to track their assigned targets
                    for (int i = 0; i < myJet.radarModules.Count; i++)
                    {
                        var radarModule = myJet.radarModules[i];
                        if (radarModule != null)
                        {
                            radarModule.UpdateTracking(ticket);

                            // If tracking, store target
                            if (radarModule.IsTracking)
                            {
                                Vector3D targetPos = radarModule.TargetPosition;
                                Vector3D targetVel = radarModule.TargetVelocity;
                                string targetName = radarModule.TrackedObjectName;

                                // Find slot or create new one
                                int slotIndex = Program.FindEmptyOrOldestSlot(myJet);
                                myJet.targetSlots[slotIndex] = new Jet.TargetSlot(targetPos, targetVel, targetName);

                                // Add to enemy list
                                myJet.UpdateOrAddEnemy(targetPos, targetVel, targetName, i);

                                // First radar module (closest target) auto-activates for missiles
                                if (i == 0)
                                {
                                    myJet.activeSlotIndex = slotIndex;
                                    SystemManager.UpdateActiveTargetGPS(); // Update CustomData for missiles
                                }
                                else
                                {
                                }
                            }
                        }
                    }

                    // Backward compatibility: Update old radarTracker if it exists
                    if (radarTracker != null)
                    {
                        radarTracker.UpdateTracking(ticket);
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

                    if (isAirtoAirenabled && radarTracker != null)
                    {
                        desiredSound = radarTracker.IsTracking ? "AIM9Lock" : "AIM9Search";
                    }

                    if (lastPlayedSounds.Count <= i)
                    {
                        lastPlayedSounds.Add(string.Empty);
                        lastSoundTickCounters.Add(0);
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
                // Restart all currently playing sounds using the state machine
                for (int i = 0; i < soundblocks.Count; i++)
                {
                    var soundBlock = soundblocks[i];
                    if (soundBlock == null || !soundBlock.IsFunctional)
                        continue;

                    string currentSound = lastPlayedSounds[i];
                    if (!string.IsNullOrEmpty(currentSound) && soundStates[i] == 0)
                    {
                        // Trigger restart via state machine
                        pendingSounds[i] = currentSound;
                        soundStates[i] = 1;
                    }
                }
            }

            private void ChangeSound(string desiredSound, IMySoundBlock block, int index)
            {
                if (block == null || !block.IsFunctional)
                    return;

                // Ensure lists are properly sized
                while (soundStates.Count <= index)
                {
                    soundStates.Add(0);
                    pendingSounds.Add("");
                    lastPlayedSounds.Add("");
                }

                // Multi-tick state machine (Space Engineers requires 1 action per tick)
                // State 0: Idle - check if new sound needed
                // State 1: Stopping - call Stop()
                // State 2: Selecting - set SelectedSound property
                // State 3: Playing - call Play()

                int currentState = soundStates[index];

                // Check if we need to start a new sound (only when idle)
                if (currentState == 0 && desiredSound != lastPlayedSounds[index])
                {
                    pendingSounds[index] = desiredSound;
                    soundStates[index] = 1; // Start sequence
                    return;
                }

                // Execute current state
                switch (currentState)
                {
                    case 1: // Stopping
                        block.Stop();
                        soundStates[index] = 2; // Next tick: select
                        break;

                    case 2: // Selecting
                        // Ensure block is enabled
                        if (!block.Enabled)
                            block.Enabled = true;

                        block.SelectedSound = pendingSounds[index];

                        if (!string.IsNullOrEmpty(pendingSounds[index]))
                        {
                            soundStates[index] = 3; // Next tick: play
                        }
                        else
                        {
                            // Just stopping, go back to idle
                            soundStates[index] = 0;
                            lastPlayedSounds[index] = "";
                        }
                        break;

                    case 3: // Playing
                        block.Play();
                        lastPlayedSounds[index] = pendingSounds[index];
                        soundStates[index] = 0; // Back to idle
                        break;
                }
            }

            private void LoopSounds()
            {
                // Restart all currently playing sounds using the state machine
                for (int i = 0; i < soundblocks.Count; i++)
                {
                    var soundBlock = soundblocks[i];
                    if (soundBlock == null || !soundBlock.IsFunctional)
                        continue;

                    string currentSound =
                        lastPlayedSounds.Count > i ? lastPlayedSounds[i] : string.Empty;

                    if (!string.IsNullOrEmpty(currentSound) && soundStates[i] == 0)
                    {
                        // Trigger restart by setting pending sound (state machine will handle it over 3 ticks)
                        pendingSounds[i] = currentSound;
                        soundStates[i] = 1; // Start the stop-select-play sequence
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
                            return;
                        }
                        catch (Exception e)
                        {
                            // Continue to next bay instead of crashing
                        }
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
                catch (Exception e)
                {
                }
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

        // RWR (Radar Warning Receiver) Module
        // Detects when enemies are on intercept course toward us
        // RWR Warning data structure - accessible by HUD and other modules
        public class RWRWarning
        {
            public Vector3D Position;
            public Vector3D Velocity;
            public string Name;
            public bool IsIncoming; // True if enemy is on intercept course and pursuing
            public int RWRIndex; // Which RWR unit detected this (0-based)
            public long LastSeenTicks;

            public RWRWarning(Vector3D pos, Vector3D vel, string name, bool incoming, int rwrIdx)
            {
                Position = pos;
                Velocity = vel;
                Name = name;
                IsIncoming = incoming;
                RWRIndex = rwrIdx;
                LastSeenTicks = DateTime.Now.Ticks;
            }

            public double AgeSeconds()
            {
                return (DateTime.Now.Ticks - LastSeenTicks) / (double)TimeSpan.TicksPerSecond;
            }
        }

        class RWRModule : ProgramModule
        {
            // Multi-RWR tracking
            private List<RadarTrackingModule> rwrRadars = new List<RadarTrackingModule>();
            private int configuredRWRCount = 0; // How many RWRs user wants to use (0 = all available)
            private int availableRWRCount = 0; // How many AI pairs are actually available

            // Per-RWR tracking state
            private class RWRTrackingState
            {
                public string CurrentEnemyName = "";
                public int TicksSinceEnemyChange = 0;
                public List<Vector3D> PositionHistory = new List<Vector3D>();
                public int HistoryIndex = 0;
                public int TickCounter = 0;

                public RWRTrackingState()
                {
                    for (int i = 0; i < POSITION_HISTORY_SIZE; i++)
                    {
                        PositionHistory.Add(Vector3D.Zero);
                    }
                }

                public void ClearHistory()
                {
                    for (int i = 0; i < POSITION_HISTORY_SIZE; i++)
                    {
                        PositionHistory[i] = Vector3D.Zero;
                    }
                    HistoryIndex = 0;
                }
            }

            private List<RWRTrackingState> rwrStates = new List<RWRTrackingState>();

            private Jet myJet;
            private List<IMySoundBlock> warningSoundBlocks;
            private bool rwrEnabled = false;
            private bool anyThreatDetected = false;  // Track if ANY RWR has a threat

            // Public accessors for status display
            public bool IsEnabled { get { return rwrEnabled; } }
            public bool IsThreat { get { return anyThreatDetected; } }

            // Active warnings list (accessible by HUD and other modules)
            public List<RWRWarning> activeThreats = new List<RWRWarning>();

            // Enemy position tracking for pursuit detection (per-RWR history)
            private const int POSITION_HISTORY_SIZE = 10;
            private const int POSITION_SAMPLE_INTERVAL = 10; // Sample position every 10 ticks
            private const int MIN_TRACKING_TICKS = 30; // Need 30 ticks (0.5 sec) of tracking same enemy

            // Sound state machine (same pattern as AirtoAir)
            private int soundState = 0; // 0=idle, 1=stopping, 2=selecting, 3=playing, 4=waiting
            private string lastPlayedSound = "";
            private string pendingSound = "";
            private int soundTickCounter = 0;
            private const int SOUND_PLAY_INTERVAL = 60; // Play sound every 60 ticks (1 second)

            // Threat detection parameters
            private const double INTERCEPT_TOLERANCE_ANGLE = 15.0; // degrees of "play" in intercept calculation
            private const double MIN_PURSUIT_SAMPLES = 3; // Need 3 consistent samples to confirm pursuit
            private const double ASSUMED_ENEMY_WEAPON_SPEED = 400.0; // m/s - assume enemy has similar weapons
            private const double MIN_ENEMY_MOVEMENT = 50.0; // Minimum movement in meters to be considered moving

            // Console output tracking to only print when state changes
            private string lastConsoleOutput = "";

            public RWRModule(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                name = "RWR System";

                // Fetch warning sound blocks (reuse altitude warning blocks)
                warningSoundBlocks = jet._soundBlocks;

                // Count available RWR AI blocks (from Jet's reversed list)
                availableRWRCount = jet.rwrAIBlocks.Count;

                // Initialize RWR radars from all detected AI block pairs
                for (int i = 0; i < availableRWRCount; i++)
                {
                    var aiPair = jet.rwrAIBlocks[i];
                    rwrRadars.Add(new RadarTrackingModule(aiPair.FlightBlock, aiPair.CombatBlock));
                    rwrStates.Add(new RWRTrackingState());
                }

                // Load configured count from CustomData (0 = use all)
                string savedCount = GetCustomDataValue("RWRCount");
                int count;
                if (!string.IsNullOrEmpty(savedCount) && int.TryParse(savedCount, out count))
                {
                    configuredRWRCount = Math.Max(0, Math.Min(count, availableRWRCount));
                }
                else
                {
                    configuredRWRCount = availableRWRCount; // Default: use all
                }
            }

            private string GetCustomDataValue(string key)
            {
                var lines = ParentProgram.Me.CustomData.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith(key + ":"))
                    {
                        return line.Substring(key.Length + 1);
                    }
                }
                return null;
            }

            private void SetCustomDataValue(string key, string value)
            {
                var lines = ParentProgram.Me.CustomData.Split('\n').ToList();
                bool found = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(key + ":"))
                    {
                        lines[i] = key + ":" + value;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    lines.Add(key + ":" + value);
                }

                ParentProgram.Me.CustomData = string.Join("\n", lines);
            }

            public override string[] GetOptions()
            {
                var options = new List<string>();

                if (availableRWRCount > 0)
                {
                    options.Add(string.Format("RWR [{0}]", rwrEnabled ? "ON" : "OFF"));

                    // Two separate menu items for increasing/decreasing RWR count
                    int activeCount = GetActiveRWRCount();
                    options.Add(string.Format("RWR Units + (Current: {0}/{1})", activeCount, availableRWRCount));
                    options.Add(string.Format("RWR Units - (Current: {0}/{1})", activeCount, availableRWRCount));

                    if (rwrEnabled)
                    {
                        int threatCount = activeThreats.Count;
                        if (threatCount > 0)
                        {
                            options.Add(string.Format("Status: {0} THREAT{1}", threatCount, threatCount > 1 ? "S" : ""));
                        }
                        else
                        {
                            options.Add("Status: Scanning...");
                        }
                    }
                }
                else
                {
                    options.Add("RWR: NO AI BLOCKS");
                }

                return options.ToArray();
            }

            public override void ExecuteOption(int index)
            {
                if (availableRWRCount == 0)
                    return;

                switch (index)
                {
                    case 0: // Toggle RWR ON/OFF
                        rwrEnabled = !rwrEnabled;

                        if (!rwrEnabled)
                        {
                            // Stop warning sound when disabling
                            StopWarningSound();

                            // Clear all tracking states
                            foreach (var state in rwrStates)
                            {
                                state.ClearHistory();
                                state.CurrentEnemyName = "";
                                state.TicksSinceEnemyChange = 0;
                            }

                            activeThreats.Clear();
                        }
                        break;

                    case 1: // Increase RWR count
                        if (configuredRWRCount < availableRWRCount)
                        {
                            configuredRWRCount++;
                            SetCustomDataValue("RWRCount", configuredRWRCount.ToString());
                        }
                        break;

                    case 2: // Decrease RWR count
                        if (configuredRWRCount > 1)
                        {
                            configuredRWRCount--;
                            SetCustomDataValue("RWRCount", configuredRWRCount.ToString());
                        }
                        break;
                }
            }

            private int GetActiveRWRCount()
            {
                return configuredRWRCount == 0 ? availableRWRCount : Math.Min(configuredRWRCount, availableRWRCount);
            }

            public override void Tick()
            {
                if (!rwrEnabled || availableRWRCount == 0)
                {
                    return;
                }

                // Update sound state machine
                UpdateSoundStateMachine();

                // Get player position/velocity once for all RWRs
                Vector3D playerPos = myJet._cockpit.GetPosition();
                Vector3D playerVel = myJet._cockpit.GetShipVelocities().LinearVelocity;
                Vector3D gravity = myJet._cockpit.GetNaturalGravity();

                // Clear previous threats
                activeThreats.Clear();
                anyThreatDetected = false;

                // Process each active RWR
                int activeCount = GetActiveRWRCount();
                for (int i = 0; i < activeCount; i++)
                {
                    ProcessRWR(i, playerPos, playerVel, gravity);
                }

                // Update console output (only when state changes)
                UpdateConsoleOutput();

                // Play sound if any threat detected
                if (anyThreatDetected)
                {
                    PlayWarningSound();
                }
                else
                {
                    StopWarningSound();
                }
            }

            private void ProcessRWR(int rwrIndex, Vector3D playerPos, Vector3D playerVel, Vector3D gravity)
            {
                var radar = rwrRadars[rwrIndex];
                var state = rwrStates[rwrIndex];

                // Update radar tracking
                radar.UpdateTracking(ParentProgram.Runtime.TimeSinceLastRun.Ticks);

                // Increment tick counter
                state.TickCounter++;

                if (radar.IsTracking)
                {
                    string enemyName = radar.TrackedObjectName;
                    Vector3D enemyPos = radar.TargetPosition;
                    Vector3D enemyVel = radar.TargetVelocity;

                    // Check if this is the SAME enemy we were tracking
                    if (enemyName != state.CurrentEnemyName)
                    {
                        // NEW enemy detected - reset tracking
                        state.CurrentEnemyName = enemyName;
                        state.TicksSinceEnemyChange = 0;
                        state.ClearHistory();
                    }
                    else
                    {
                        // Same enemy - increment tracking time
                        state.TicksSinceEnemyChange++;
                    }

                    // Sample position at intervals (not every tick)
                    if (state.TickCounter % POSITION_SAMPLE_INTERVAL == 0)
                    {
                        state.PositionHistory[state.HistoryIndex] = enemyPos;
                        state.HistoryIndex = (state.HistoryIndex + 1) % POSITION_HISTORY_SIZE;
                    }

                    // Only evaluate threat if we've been tracking same enemy for enough ticks
                    if (state.TicksSinceEnemyChange >= MIN_TRACKING_TICKS)
                    {
                        // Check if enemy is a threat
                        bool isThreatening = IsThreatening(enemyPos, enemyVel, playerPos, playerVel, gravity, state.PositionHistory);

                        if (isThreatening)
                        {
                            // Add to active threats
                            activeThreats.Add(new RWRWarning(enemyPos, enemyVel, enemyName, true, rwrIndex));
                            anyThreatDetected = true;
                        }
                        else
                        {
                            // Tracking but not incoming - still add to threats list
                            activeThreats.Add(new RWRWarning(enemyPos, enemyVel, enemyName, false, rwrIndex));
                        }
                    }
                }
                else
                {
                    // No enemy detected - reset state
                    if (state.CurrentEnemyName != "")
                    {
                        state.CurrentEnemyName = "";
                        state.TicksSinceEnemyChange = 0;
                        state.ClearHistory();
                    }
                }
            }

            // Check if enemy is threatening using proportional navigation intercept detection
            private bool IsThreatening(Vector3D enemyPos, Vector3D enemyVel, Vector3D playerPos, Vector3D playerVel, Vector3D gravity, List<Vector3D> positionHistory)
            {
                // PROPORTIONAL NAVIGATION THREAT DETECTION
                // A real RWR detects when an enemy's trajectory is pointing toward our future position
                // This works even when both entities are stationary or moving

                // Step 1: Calculate relative position and velocity
                Vector3D relativePos = playerPos - enemyPos;
                Vector3D relativeVel = playerVel - enemyVel;

                double range = relativePos.Length();
                if (range < 1.0)
                    return false; // Too close, avoid division by zero

                // Step 2: Check if there's any relative motion at all
                double relativeSpeed = relativeVel.Length();
                const double MIN_RELATIVE_SPEED = 1.0; // m/s - minimum relative motion to consider

                // If both are stationary or moving together, check if enemy is ACTIVELY pointing at us
                if (relativeSpeed < MIN_RELATIVE_SPEED)
                {
                    // No relative motion - only threat if enemy velocity is pointing AT us
                    double enemySpeed = enemyVel.Length();
                    if (enemySpeed < 0.5) // Enemy is stationary (< 0.5 m/s)
                        return false; // Stationary enemy is NOT a threat

                    // Enemy is moving - check if they're flying toward us
                    Vector3D enemyToUs = relativePos; // Vector from enemy to us
                    enemyToUs.Normalize();
                    Vector3D enemyVelNorm = Vector3D.Normalize(enemyVel);

                    double dotProduct = Vector3D.Dot(enemyVelNorm, enemyToUs);
                    double aspectAngleDeg = Math.Acos(MathHelper.Clamp(dotProduct, -1.0, 1.0)) * (180.0 / Math.PI);

                    // Only threat if enemy is pointing almost directly at us (within 30 degrees)
                    return aspectAngleDeg < 30.0;
                }

                // Step 3: Calculate Line of Sight (LOS) direction
                Vector3D losDirection = relativePos / range; // Normalized

                // Step 4: Calculate closing velocity (range rate)
                double closingVelocity = -Vector3D.Dot(relativeVel, losDirection);

                // If opening (moving apart), NOT a threat
                if (closingVelocity <= 0)
                    return false; // Moving apart or parallel

                // Step 5: Calculate time to closest approach
                double timeToClosestApproach = range / closingVelocity;

                // Sanity check - ignore very long times (> 5 minutes)
                if (timeToClosestApproach > 300.0 || timeToClosestApproach < 0)
                    return false;

                // Step 6: PROPORTIONAL NAVIGATION CHECK
                // Calculate where we'll be at closest approach
                Vector3D ourFuturePos = playerPos + playerVel * timeToClosestApproach;
                Vector3D enemyFuturePos = enemyPos + enemyVel * timeToClosestApproach;

                // Calculate closest approach distance
                double closestApproachDistance = Vector3D.Distance(ourFuturePos, enemyFuturePos);

                // If closest approach is too far, not an intercept
                const double THREAT_INTERCEPT_DISTANCE = 500.0; // 500m closest approach = threat
                if (closestApproachDistance > THREAT_INTERCEPT_DISTANCE)
                    return false;

                // Step 7: Verify trajectory consistency using position history
                // Check if enemy has been consistently pointing toward us
                if (!IsTrajectoryConsistent(enemyPos, enemyVel, playerPos, playerVel, positionHistory))
                    return false;

                // Step 8: Additional verification - aspect angle check
                // Make sure enemy velocity is generally pointing toward us
                double enemySpd = enemyVel.Length();
                if (enemySpd > 1.0) // Enemy is moving
                {
                    Vector3D enemyToUs = relativePos;
                    enemyToUs.Normalize();
                    Vector3D enemyVelNorm = enemyVel / enemySpd;

                    double dotProduct = Vector3D.Dot(enemyVelNorm, enemyToUs);
                    double aspectAngleDeg = Math.Acos(MathHelper.Clamp(dotProduct, -1.0, 1.0)) * (180.0 / Math.PI);

                    // Enemy must be pointing toward us (within 90 degrees)
                    if (aspectAngleDeg > 90.0)
                        return false; // Enemy is pointing away from us
                }

                // All checks passed - this is a threat!
                return true;
            }

            // Check if enemy trajectory has been consistently pointing toward us over time
            private bool IsTrajectoryConsistent(Vector3D currentEnemyPos, Vector3D currentEnemyVel, Vector3D playerPos, Vector3D playerVel, List<Vector3D> positionHistory)
            {
                // Need at least 3 samples to determine trajectory consistency
                int validSamples = 0;
                for (int i = 0; i < POSITION_HISTORY_SIZE; i++)
                {
                    if (positionHistory[i] != Vector3D.Zero)
                        validSamples++;
                }

                if (validSamples < 3)
                    return true; // Not enough data, give benefit of doubt

                // Calculate historical velocities from position deltas
                int interceptingSamples = 0;
                int totalComparisons = 0;

                for (int i = 1; i < POSITION_HISTORY_SIZE; i++)
                {
                    Vector3D prevPos = positionHistory[i - 1];
                    Vector3D currPos = positionHistory[i];

                    if (prevPos == Vector3D.Zero || currPos == Vector3D.Zero)
                        continue;

                    // Calculate historical velocity from position change
                    // Each sample is POSITION_SAMPLE_INTERVAL ticks apart (10 ticks = 1/6 second)
                    double deltaTime = POSITION_SAMPLE_INTERVAL / 60.0; // Convert to seconds (60 ticks/sec)
                    Vector3D historicalVel = (currPos - prevPos) / deltaTime;

                    // Check if this historical velocity was pointing toward us
                    Vector3D relativePos = playerPos - currPos;
                    if (relativePos.LengthSquared() < 1.0)
                        continue;

                    if (historicalVel.LengthSquared() > 1.0) // Was moving
                    {
                        Vector3D relativeDir = Vector3D.Normalize(relativePos);
                        Vector3D velDir = Vector3D.Normalize(historicalVel);

                        double dotProduct = Vector3D.Dot(velDir, relativeDir);
                        double angleDeg = Math.Acos(MathHelper.Clamp(dotProduct, -1.0, 1.0)) * (180.0 / Math.PI);

                        totalComparisons++;

                        // If pointing within 90 degrees toward us, count as intercepting
                        if (angleDeg < 90.0)
                            interceptingSamples++;
                    }
                }

                if (totalComparisons < 2)
                    return true; // Not enough data

                // Require at least 50% of samples showing intercept trajectory
                double interceptRatio = (double)interceptingSamples / totalComparisons;
                return interceptRatio >= 0.5;
            }

            // Update console output with compact format (only when state changes)
            private void UpdateConsoleOutput()
            {
                if (activeThreats.Count == 0 && lastConsoleOutput == "")
                    return; // No change, no output needed

                var sb = new StringBuilder();
                sb.Append("RWR: ");

                int activeCount = GetActiveRWRCount();
                for (int i = 0; i < activeCount; i++)
                {
                    if (i > 0) sb.Append(" ");

                    sb.Append("R").Append(i + 1).Append(":");

                    var radar = rwrRadars[i];
                    var state = rwrStates[i];

                    // A = Active (radar functional)
                    if (radar != null)
                    {
                        sb.Append("A");
                    }
                    else
                    {
                        sb.Append("-");
                    }

                    sb.Append(",");

                    // T = Tracking
                    if (radar != null && radar.IsTracking)
                    {
                        sb.Append("T");
                    }
                    else
                    {
                        sb.Append("-");
                    }

                    sb.Append(",");

                    // H+ = Hostile Incoming, H = Hostile (not incoming)
                    bool foundThreat = false;
                    foreach (var threat in activeThreats)
                    {
                        if (threat.RWRIndex == i)
                        {
                            if (threat.IsIncoming)
                            {
                                sb.Append("H+");
                            }
                            else
                            {
                                sb.Append("H");
                            }
                            foundThreat = true;
                            break;
                        }
                    }

                    if (!foundThreat)
                    {
                        sb.Append("-");
                    }
                }

                string newOutput = sb.ToString();

                // Only echo if output changed
                if (newOutput != lastConsoleOutput)
                {
                    ParentProgram.Echo(newOutput);
                    lastConsoleOutput = newOutput;
                }
            }

            // Play RWR warning sound
            private void PlayWarningSound()
            {
                if (soundState == 3 && lastPlayedSound == "SoundBlockAlert")
                {
                    return; // Already playing warning sound
                }

                // Queue warning sound
                pendingSound = "SoundBlockAlert";
            }

            // Stop RWR warning sound
            private void StopWarningSound()
            {
                if (soundState == 0)
                {
                    return; // Already stopped
                }

                pendingSound = ""; // Clear pending sound, will trigger stop
            }

            // Sound state machine (7-tick cycle like AirtoAir)
            private void UpdateSoundStateMachine()
            {
                soundTickCounter++;

                if (warningSoundBlocks.Count == 0)
                    return;

                var soundBlock = warningSoundBlocks[0]; // Use first warning sound block

                if (soundBlock == null || !soundBlock.IsFunctional)
                    return;

                switch (soundState)
                {
                    case 0: // Idle - check if we need to play a sound
                        if (!string.IsNullOrEmpty(pendingSound))
                        {
                            soundState = 1; // Move to stopping state
                            soundTickCounter = 0;
                        }
                        break;

                    case 1: // Stopping previous sound
                        if (soundTickCounter >= 1)
                        {
                            soundBlock.Stop();
                            soundState = 2;
                            soundTickCounter = 0;
                        }
                        break;

                    case 2: // Selecting new sound
                        if (soundTickCounter >= 1)
                        {
                            if (!string.IsNullOrEmpty(pendingSound))
                            {
                                soundBlock.SelectedSound = pendingSound;
                                soundState = 3;
                            }
                            else
                            {
                                soundState = 0; // No sound to play, return to idle
                            }
                            soundTickCounter = 0;
                        }
                        break;

                    case 3: // Playing sound
                        if (soundTickCounter >= 1)
                        {
                            soundBlock.Volume = 1.0f; // Full volume for warning
                            soundBlock.Play();
                            lastPlayedSound = pendingSound;
                            soundTickCounter = 0;

                            // Check if we should stop or continue
                            if (string.IsNullOrEmpty(pendingSound))
                            {
                                soundState = 1; // Stop playing
                            }
                            else
                            {
                                soundState = 4; // Move to waiting state
                            }
                        }
                        break;

                    case 4: // Waiting before next play (beeping interval)
                        if (soundTickCounter >= SOUND_PLAY_INTERVAL)
                        {
                            soundTickCounter = 0;
                            if (!string.IsNullOrEmpty(pendingSound))
                            {
                                soundState = 3; // Play again
                            }
                            else
                            {
                                soundState = 1; // Stop
                            }
                        }
                        break;
                }
            }

            public override void HandleSpecialFunction(int key)
            {
                // No special functions for RWR - count adjustment is via menu items
            }

            public override string GetHotkeys()
            {
                return "RWR has no hotkeys - use menu to adjust count";
            }

            // Use HUDModule's intercept calculation (need to access it)
            // This is a simplified copy - ideally would share the method
            private bool CalculateInterceptPointIterative(
                Vector3D shooterPosition,
                Vector3D shooterVelocity,
                double projectileSpeed,
                Vector3D targetPosition,
                Vector3D targetVelocity,
                Vector3D gravity,
                int maxIterations,
                out Vector3D interceptPoint,
                out double timeToIntercept)
            {
                interceptPoint = Vector3D.Zero;
                timeToIntercept = 0;

                Vector3D relativePosition = targetPosition - shooterPosition;
                Vector3D relativeVelocity = targetVelocity - shooterVelocity;

                // Quadratic formula to solve for intercept time
                double a = relativeVelocity.LengthSquared() - projectileSpeed * projectileSpeed;
                double b = 2 * Vector3D.Dot(relativePosition, relativeVelocity);
                double c = relativePosition.LengthSquared();

                double discriminant = b * b - 4 * a * c;

                if (discriminant < 0)
                {
                    return false; // No solution - can't intercept
                }

                double sqrtDiscriminant = Math.Sqrt(discriminant);
                double t1 = (-b + sqrtDiscriminant) / (2 * a);
                double t2 = (-b - sqrtDiscriminant) / (2 * a);

                // Use the smallest positive time
                timeToIntercept = (t2 > 0) ? t2 : t1;

                if (timeToIntercept < 0)
                {
                    return false; // Can't intercept in the future
                }

                // Calculate intercept point with gravity compensation
                Vector3D gravityAccel = gravity;
                for (int i = 0; i < maxIterations; i++)
                {
                    interceptPoint = targetPosition + targetVelocity * timeToIntercept + 0.5 * gravityAccel * timeToIntercept * timeToIntercept;

                    Vector3D newRelPos = interceptPoint - shooterPosition;
                    double distance = newRelPos.Length();
                    double newTime = distance / projectileSpeed;

                    if (Math.Abs(newTime - timeToIntercept) < 0.01)
                    {
                        break; // Converged
                    }

                    timeToIntercept = newTime;
                }

                return true;
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

        class ConfigurationModule : ProgramModule
        {
            private enum MenuLevel { Category, ParameterList, ValueAdjust }
            private MenuLevel currentLevel = MenuLevel.Category;
            private int categoryIndex = 0;
            private int parameterIndex = 0;
            private int scrollOffset = 0;

            private string[] categories = new string[]
            {
                "Flight Control",
                "Weapons",
                "HUD & Display",
                "Radar & Sensors",
                "Warnings & Alerts",
                "Physics & Environment",
                "Advanced Settings",
                "Import/Export/Reset"
            };

            // Configuration storage
            private Dictionary<string, ConfigParam> allConfigs = new Dictionary<string, ConfigParam>();

            public ConfigurationModule(Program program) : base(program)
            {
                name = "Configuration";
                InitializeConfigs();
                LoadFromCustomData();
            }

            private class ConfigParam
            {
                public string Category;
                public string Name;
                public string DisplayName;
                public float Value;
                public float DefaultValue;
                public float MinValue;
                public float MaxValue;
                public float StepSize;
                public string Unit;
                public bool IsModified => Math.Abs(Value - DefaultValue) > 0.0001f;

                public ConfigParam(string category, string name, string displayName, float defaultValue,
                                 float minValue, float maxValue, float stepSize, string unit = "")
                {
                    Category = category;
                    Name = name;
                    DisplayName = displayName;
                    Value = defaultValue;
                    DefaultValue = defaultValue;
                    MinValue = minValue;
                    MaxValue = maxValue;
                    StepSize = stepSize;
                    Unit = unit;
                }

                public void Adjust(int direction)
                {
                    Value = Math.Max(MinValue, Math.Min(MaxValue, Value + direction * StepSize));
                }

                public void Reset()
                {
                    Value = DefaultValue;
                }
            }

            private void InitializeConfigs()
            {
                // FLIGHT CONTROL CATEGORY (Critical - top priority)
                AddConfig("Flight Control", "stabilizer_kp", "Stabilizer Kp", 1.2f, 0.1f, 5.0f, 0.1f);
                AddConfig("Flight Control", "stabilizer_ki", "Stabilizer Ki", 0.0024f, 0.0f, 0.1f, 0.0001f);
                AddConfig("Flight Control", "stabilizer_kd", "Stabilizer Kd", 0.5f, 0.0f, 2.0f, 0.1f);
                AddConfig("Flight Control", "max_pid_output", "Max PID Output", 60f, 10f, 90f, 5f, "deg");
                AddConfig("Flight Control", "max_aoa", "Max AoA Limit", 36f, 10f, 60f, 1f, "deg");
                AddConfig("Flight Control", "integral_clamp", "Integral Clamp", 200f, 50f, 500f, 10f);
                AddConfig("Flight Control", "optimal_aoa_min", "Optimal AoA Min", 8f, 0f, 20f, 1f, "deg");
                AddConfig("Flight Control", "optimal_aoa_max", "Optimal AoA Max", 15f, 10f, 30f, 1f, "deg");

                // WEAPONS CATEGORY
                AddConfig("Weapons", "bombardment_spacing", "Bombardment Spacing", 4.0f, 1.0f, 20.0f, 0.5f, "m");
                AddConfig("Weapons", "circular_pattern_radius", "Circular Pattern Radius", 4.0f, 2.0f, 20.0f, 1.0f, "m");
                AddConfig("Weapons", "shoot_range_inner", "Shoot Cue Range", 1500f, 500f, 5000f, 100f, "m");
                AddConfig("Weapons", "shoot_range_outer", "In Range Cue", 2500f, 1000f, 8000f, 100f, "m");
                AddConfig("Weapons", "proximity_warning", "Proximity Warning", 500f, 100f, 1000f, 50f, "m");
                AddConfig("Weapons", "min_closure_rate", "Min Closure Rate", 100f, 10f, 500f, 10f, "m/s");

                // HUD & DISPLAY CATEGORY
                AddConfig("HUD & Display", "fov_scale_x", "FOV Scale X", 0.3434f, 0.1f, 1.0f, 0.01f);
                AddConfig("HUD & Display", "fov_scale_y", "FOV Scale Y", 0.31f, 0.1f, 1.0f, 0.01f);
                AddConfig("HUD & Display", "velocity_indicator_scale", "Velocity Indicator Scale", 20f, 5f, 50f, 1f);
                AddConfig("HUD & Display", "min_pip_distance", "Min Pip Distance", 50f, 10f, 200f, 10f, "m");
                AddConfig("HUD & Display", "max_pip_distance", "Max Pip Distance", 3000f, 1000f, 10000f, 100f, "m");
                AddConfig("HUD & Display", "max_pip_size", "Max Pip Size Factor", 0.1f, 0.01f, 0.5f, 0.01f);
                AddConfig("HUD & Display", "min_pip_size", "Min Pip Size Factor", 0.01f, 0.001f, 0.1f, 0.001f);
                AddConfig("HUD & Display", "intercept_iterations", "Intercept Iterations", 10f, 1f, 20f, 1f);

                // RADAR & SENSORS CATEGORY
                AddConfig("Radar & Sensors", "radar_range", "Radar Range", 15000f, 1000f, 30000f, 1000f, "m");
                AddConfig("Radar & Sensors", "radar_box_size", "Radar Box Size", 100f, 50f, 200f, 10f, "px");
                AddConfig("Radar & Sensors", "targeting_kp_rotor", "Targeting Pod Kp (Rotor)", 0.05f, 0.01f, 0.5f, 0.01f);
                AddConfig("Radar & Sensors", "targeting_kp_hinge", "Targeting Pod Kp (Hinge)", 0.05f, 0.01f, 0.5f, 0.01f);
                AddConfig("Radar & Sensors", "targeting_max_velocity", "Targeting Max Velocity", 5.0f, 1.0f, 10.0f, 0.5f, "RPM");

                // WARNINGS & ALERTS CATEGORY
                AddConfig("Warnings & Alerts", "bingo_fuel_percent", "Bingo Fuel %", 0.20f, 0.05f, 0.50f, 0.05f, "%");
                AddConfig("Warnings & Alerts", "low_fuel_percent", "Low Fuel %", 0.35f, 0.10f, 0.60f, 0.05f, "%");
                AddConfig("Warnings & Alerts", "altitude_warning_threshold", "Altitude Warning", 380f, 100f, 1000f, 10f, "m");
                AddConfig("Warnings & Alerts", "speed_warning_threshold", "Speed Warning", 360f, 100f, 600f, 10f, "kph");
                AddConfig("Warnings & Alerts", "low_altitude_threshold", "Low Altitude Limit", 100f, 50f, 300f, 10f, "m");
                AddConfig("Warnings & Alerts", "descent_rate_warning", "Descent Rate Warning", -5f, -20f, -1f, 1f, "m/s");

                // PHYSICS & ENVIRONMENT CATEGORY
                AddConfig("Physics & Environment", "speed_of_sound", "Speed of Sound", 343.0f, 300f, 400f, 1f, "m/s");
                AddConfig("Physics & Environment", "gravity", "Gravity", 9.81f, 0.5f, 20f, 0.1f, "m/s²");
                AddConfig("Physics & Environment", "smoothing_window", "Smoothing Window Size", 10f, 1f, 30f, 1f);

                // ADVANCED SETTINGS CATEGORY
                AddConfig("Advanced", "throttle_h2_threshold", "Throttle H2 Threshold", 0.8f, 0.5f, 1.0f, 0.05f);
                AddConfig("Advanced", "gps_cache_slots", "GPS Cache Slots", 4f, 1f, 10f, 1f);
                AddConfig("Advanced", "targeting_angle_threshold", "Targeting Angle Threshold", 2.0f, 0.1f, 10.0f, 0.1f, "deg");
            }

            private void AddConfig(string category, string name, string displayName, float defaultValue,
                                  float minValue, float maxValue, float stepSize, string unit = "")
            {
                allConfigs[name] = new ConfigParam(category, name, displayName, defaultValue,
                                                  minValue, maxValue, stepSize, unit);
            }

            private void LoadFromCustomData()
            {
                string customData = ParentProgram.Me.CustomData;
                if (string.IsNullOrEmpty(customData)) return;

                string[] lines = customData.Split('\n');
                foreach (string line in lines)
                {
                    if (line.StartsWith("Config:"))
                    {
                        string[] parts = line.Substring(7).Split(':');
                        if (parts.Length == 2)
                        {
                            string configName = parts[0];
                            float value;
                            if (allConfigs.ContainsKey(configName) && float.TryParse(parts[1], out value))
                            {
                                allConfigs[configName].Value = value;
                            }
                        }
                    }
                }
            }

            private void SaveToCustomData()
            {
                StringBuilder sb = new StringBuilder();

                // Preserve non-config lines
                string currentData = ParentProgram.Me.CustomData;
                if (!string.IsNullOrEmpty(currentData))
                {
                    string[] lines = currentData.Split('\n');
                    foreach (string line in lines)
                    {
                        if (!line.StartsWith("Config:"))
                        {
                            sb.AppendLine(line);
                        }
                    }
                }

                // Add all config values
                foreach (var kvp in allConfigs)
                {
                    sb.AppendLine($"Config:{kvp.Key}:{kvp.Value.Value}");
                }

                ParentProgram.Me.CustomData = sb.ToString();
                SystemManager.MarkCustomDataDirty();
            }

            public float GetValue(string configName)
            {
                if (allConfigs.ContainsKey(configName))
                    return allConfigs[configName].Value;
                return 0f;
            }

            public override string[] GetOptions()
            {
                switch (currentLevel)
                {
                    case MenuLevel.Category:
                        return categories;

                    case MenuLevel.ParameterList:
                        string selectedCategory = categories[categoryIndex];
                        List<string> options = new List<string>();
                        foreach (var kvp in allConfigs)
                        {
                            if (kvp.Value.Category == selectedCategory)
                            {
                                string modified = kvp.Value.IsModified ? " *" : "";
                                string valueStr = kvp.Value.Value.ToString("F4").TrimEnd('0').TrimEnd('.');
                                options.Add($"{kvp.Value.DisplayName}: {valueStr}{kvp.Value.Unit}{modified}");
                            }
                        }
                        if (selectedCategory == "Import/Export/Reset")
                        {
                            options.Clear();
                            options.Add("Export Config to Antenna");
                            options.Add("Import Config from Argument");
                            options.Add("Reset All to Defaults");
                            options.Add("Back to Categories");
                        }
                        else
                        {
                            options.Add("Reset Category to Defaults");
                            options.Add("Back to Categories");
                        }
                        return options.ToArray();

                    case MenuLevel.ValueAdjust:
                        var currentParams = GetCurrentCategoryParams();
                        if (parameterIndex < currentParams.Count)
                        {
                            var param = currentParams[parameterIndex];
                            return new string[]
                            {
                                $"Adjusting: {param.DisplayName}",
                                "",
                                "^ Increase (Navigate Up)",
                                $"  Current: {param.Value:F4}{param.Unit}",
                                "V Decrease (Navigate Down)",
                                "",
                                $"Default: {param.DefaultValue:F4}{param.Unit}",
                                $"Range: {param.MinValue:F2} - {param.MaxValue:F2}{param.Unit}",
                                "",
                                "SELECT to save changes",
                                "BACK to cancel (no save)"
                            };
                        }
                        break;
                }
                return new string[] { "Error" };
            }

            private List<ConfigParam> GetCurrentCategoryParams()
            {
                string selectedCategory = categories[categoryIndex];
                List<ConfigParam> params_list = new List<ConfigParam>();
                foreach (var kvp in allConfigs)
                {
                    if (kvp.Value.Category == selectedCategory)
                    {
                        params_list.Add(kvp.Value);
                    }
                }
                return params_list;
            }

            public override void ExecuteOption(int index)
            {
                switch (currentLevel)
                {
                    case MenuLevel.Category:
                        categoryIndex = index;
                        currentLevel = MenuLevel.ParameterList;
                        parameterIndex = 0;
                        scrollOffset = 0;
                        SystemManager.currentMenuIndex = 0; // Reset selector to first option
                        break;

                    case MenuLevel.ParameterList:
                        string selectedCategory = categories[categoryIndex];
                        if (selectedCategory == "Import/Export/Reset")
                        {
                            HandleImportExportReset(index);
                        }
                        else
                        {
                            var params_list = GetCurrentCategoryParams();
                            if (index < params_list.Count)
                            {
                                parameterIndex = index;
                                currentLevel = MenuLevel.ValueAdjust;
                                SystemManager.currentMenuIndex = 0; // Reset selector to first line
                            }
                            else if (index == params_list.Count)
                            {
                                // Reset category
                                foreach (var param in params_list)
                                {
                                    param.Reset();
                                }
                                SaveToCustomData();
                            }
                            else
                            {
                                // Back to categories
                                currentLevel = MenuLevel.Category;
                                SystemManager.currentMenuIndex = 0; // Reset selector to first category
                            }
                        }
                        break;

                    case MenuLevel.ValueAdjust:
                        // Save and go back to parameter list
                        SaveToCustomData();
                        currentLevel = MenuLevel.ParameterList;
                        SystemManager.currentMenuIndex = parameterIndex; // Return to the parameter we were editing
                        break;
                }
            }

            private void HandleImportExportReset(int index)
            {
                switch (index)
                {
                    case 0: // Export
                        ExportConfig();
                        break;
                    case 1: // Import
                        break;
                    case 2: // Reset All
                        foreach (var kvp in allConfigs)
                        {
                            kvp.Value.Reset();
                        }
                        SaveToCustomData();
                        break;
                    case 3: // Back
                        currentLevel = MenuLevel.Category;
                        break;
                }
            }

            private void ExportConfig()
            {
                StringBuilder export = new StringBuilder("JetOSConfig:");
                foreach (var kvp in allConfigs)
                {
                    if (kvp.Value.IsModified)
                    {
                        export.Append($"{kvp.Key}={kvp.Value.Value},");
                    }
                }
                string exportString = export.ToString().TrimEnd(',');

                // Try to broadcast via antenna
                List<IMyRadioAntenna> antennas = new List<IMyRadioAntenna>();
                ParentProgram.GridTerminalSystem.GetBlocksOfType(antennas);
                if (antennas.Count > 0)
                {
                    foreach (var antenna in antennas)
                    {
                        antenna.HudText = exportString;
                    }
                }
                ParentProgram.Echo(exportString);
            }

            public void ImportConfig(string configString)
            {
                if (!configString.StartsWith("JetOSConfig:")) return;

                string data = configString.Substring(12);
                string[] pairs = data.Split(',');
                foreach (string pair in pairs)
                {
                    string[] parts = pair.Split('=');
                    if (parts.Length == 2)
                    {
                        string key = parts[0];
                        float value;
                        if (allConfigs.ContainsKey(key) && float.TryParse(parts[1], out value))
                        {
                            allConfigs[key].Value = value;
                        }
                    }
                }
                SaveToCustomData();
                ParentProgram.Echo("Configuration imported successfully");
            }

            public override void HandleSpecialFunction(int key)
            {
                // No special function keys needed - using navigation instead
            }

            public override string GetHotkeys()
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    return "";
                }
                else if (currentLevel == MenuLevel.ParameterList)
                {
                    return "";
                }
                return "";
            }

            public override bool HandleNavigation(bool isUp)
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    // In value adjust mode, up/down changes the value
                    var params_list = GetCurrentCategoryParams();
                    if (parameterIndex < params_list.Count)
                    {
                        var param = params_list[parameterIndex];
                        if (isUp)
                        {
                            param.Adjust(1); // Increase value
                        }
                        else
                        {
                            param.Adjust(-1); // Decrease value
                        }
                        return true; // We handled navigation
                    }
                }
                return false; // Use default navigation
            }

            public override bool HandleBack()
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    // Cancel editing and go back without saving
                    currentLevel = MenuLevel.ParameterList;
                    SystemManager.currentMenuIndex = parameterIndex; // Return to the parameter we were editing
                    return true; // We handled the back button
                }
                else if (currentLevel == MenuLevel.ParameterList)
                {
                    // Go back to category selection
                    currentLevel = MenuLevel.Category;
                    SystemManager.currentMenuIndex = categoryIndex; // Return to the category we were in
                    return true; // We handled the back button
                }
                // At category level, let default behavior exit the module
                return false;
            }

            public override void Tick()
            {
                // Configuration module doesn't need per-frame updates
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
