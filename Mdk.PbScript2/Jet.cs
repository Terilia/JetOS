using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
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

            // Scalable radar tracking modules (DEPRECATED - use radarControl instead)
            public List<RadarTrackingModule> radarModules = new List<RadarTrackingModule>();

            // Centralized radar control
            public RadarControlModule radarControl;

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
            /// Request N radars from centralized radar control (convenience method)
            /// </summary>
            public List<RadarTrackingModule> RequestRadars(int count)
            {
                if (radarControl == null)
                    return new List<RadarTrackingModule>();

                return radarControl.RequestRadars(count);
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
    }
}
