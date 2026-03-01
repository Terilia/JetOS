using Sandbox.ModAPI.Ingame;
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
            public IMyFlightMovementBlock _aiFlightBlock;
            public IMyOffensiveCombatBlock _aiCombatBlock;

            // Game tick counter for consistent timing (updated by SystemManager)
            public static long GameTicks = 0;

            // Multi-target tracking system - unified slot structure
            public struct TargetSlot
            {
                public bool IsOccupied;
                public Vector3D Position;
                public Vector3D Velocity;
                public Vector3D Acceleration;
                public string Name;
                public long TimestampTicks; // Uses GameTicks for consistency across save/load

                public TargetSlot(Vector3D pos, Vector3D vel, string name, Vector3D accel = default(Vector3D))
                {
                    IsOccupied = true;
                    Position = pos;
                    Velocity = vel;
                    Acceleration = accel;
                    Name = name;
                    TimestampTicks = GameTicks;
                }

                public void Clear()
                {
                    IsOccupied = false;
                    Position = Vector3D.Zero;
                    Velocity = Vector3D.Zero;
                    Acceleration = Vector3D.Zero;
                    Name = "";
                    TimestampTicks = 0;
                }

                public long AgeTicks => GameTicks - TimestampTicks;
                public double AgeSeconds => AgeTicks / 60.0; // Assuming 60 ticks per second
            }

            public TargetSlot[] targetSlots = new TargetSlot[5];  // 5 target slots (0-4)
            public int activeSlotIndex = 0;                        // Currently selected slot

            // Enemy contact tracking with decay
            public struct EnemyContact
            {
                public Vector3D Position;
                public Vector3D Velocity;
                public Vector3D Acceleration;
                public string Name;
                public long EntityId;      // For reliable matching (0 if unknown)
                public long LastSeenTicks; // Uses GameTicks
                public int SourceIndex;    // Which AI combo detected this (0=primary, 1=RWR, 2=third combo, etc.)

                public EnemyContact(Vector3D pos, Vector3D vel, string name, int source, long entityId = 0, Vector3D accel = default(Vector3D))
                {
                    Position = pos;
                    Velocity = vel;
                    Acceleration = accel;
                    Name = name;
                    EntityId = entityId;
                    LastSeenTicks = GameTicks;
                    SourceIndex = source;
                }

                public long AgeTicks => GameTicks - LastSeenTicks;
                public double AgeSeconds => AgeTicks / 60.0; // Assuming 60 ticks per second
            }

            public List<EnemyContact> enemyList = new List<EnemyContact>();
            public const long CONTACT_DECAY_TICKS = 180 * 60; // 3 minutes at 60 ticks/second = 10800 ticks
            private int decayCheckCounter = 0;
            private const int DECAY_CHECK_INTERVAL = 60; // Check decay every 60 ticks (1 second)

            // Centralized radar control
            public RadarControlModule radarControl;

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
                // Find the cockpit - CRITICAL: must exist for jet to function
                _cockpit = grid.GetBlockWithName("Jet Pilot Seat") as IMyCockpit;
                if (_cockpit == null)
                {
                    // Cannot initialize without cockpit - leave everything empty
                    _thrusters = new List<IMyThrust>();
                    _thrustersbackwards = new List<IMyThrust>();
                    _bays = new List<IMyShipMergeBlock>();
                    return;
                }

                grid.GetBlocksOfType(
                                    _gatlings,
                                    t => t.CubeGrid == _cockpit.CubeGrid
                                );
                _thrusters = new List<IMyThrust>();
                grid.GetBlocksOfType(
                    _thrusters,
                    t => t.CubeGrid == _cockpit.CubeGrid && !t.CustomName.Contains("Industrial")
                );

                // AI blocks for radar tracking (primary pair, used by AirtoAir)
                _aiFlightBlock = grid.GetBlockWithName("AI Flight") as IMyFlightMovementBlock;
                _aiCombatBlock = grid.GetBlockWithName("AI Combat") as IMyOffensiveCombatBlock;

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
            /// Updates or adds an enemy contact to the enemy list.
            /// Matches by EntityId first, then by name, then by position proximity.
            /// </summary>
            public void UpdateOrAddEnemy(Vector3D pos, Vector3D vel, string name, int sourceIndex, long entityId = 0)
            {
                const double PROXIMITY_THRESHOLD = 50.0; // Merge contacts within 50m

                int existingIndex = -1;

                // Priority 1: Match by EntityId (most reliable)
                if (entityId != 0)
                {
                    for (int i = 0; i < enemyList.Count; i++)
                    {
                        if (enemyList[i].EntityId == entityId)
                        {
                            existingIndex = i;
                            break;
                        }
                    }
                }

                // Priority 2: Match by name
                if (existingIndex < 0 && !string.IsNullOrEmpty(name))
                {
                    for (int i = 0; i < enemyList.Count; i++)
                    {
                        if (enemyList[i].Name == name)
                        {
                            existingIndex = i;
                            break;
                        }
                    }
                }

                // Priority 3: Match by position proximity (for unnamed/unknown targets)
                if (existingIndex < 0)
                {
                    for (int i = 0; i < enemyList.Count; i++)
                    {
                        if (Vector3D.Distance(enemyList[i].Position, pos) < PROXIMITY_THRESHOLD)
                        {
                            existingIndex = i;
                            break;
                        }
                    }
                }

                Vector3D accel = Vector3D.Zero;
                if (existingIndex >= 0)
                {
                    long tickDelta = GameTicks - enemyList[existingIndex].LastSeenTicks;
                    if (tickDelta > 0 && tickDelta < 300) // <5 seconds old
                    {
                        double dt = tickDelta / 60.0;
                        Vector3D rawAccel = (vel - enemyList[existingIndex].Velocity) / dt;
                        accel = enemyList[existingIndex].Acceleration * 0.6 + rawAccel * 0.4; // EMA α=0.4
                    }
                }

                EnemyContact contact = new EnemyContact(pos, vel, name, sourceIndex, entityId, accel);

                if (existingIndex >= 0)
                {
                    enemyList[existingIndex] = contact;
                }
                else
                {
                    enemyList.Add(contact);
                }
            }

            /// <summary>
            /// Removes contacts older than CONTACT_DECAY_TICKS (3 minutes).
            /// Optimized to only check every DECAY_CHECK_INTERVAL ticks.
            /// </summary>
            public void UpdateEnemyDecay()
            {
                decayCheckCounter++;
                if (decayCheckCounter < DECAY_CHECK_INTERVAL)
                    return;

                decayCheckCounter = 0;

                for (int i = enemyList.Count - 1; i >= 0; i--)
                {
                    if (enemyList[i].AgeTicks > CONTACT_DECAY_TICKS)
                    {
                        enemyList.RemoveAt(i);
                    }
                }
            }

            // Reusable lists to reduce GC pressure
            private List<KeyValuePair<double, EnemyContact>> _sortBuffer = new List<KeyValuePair<double, EnemyContact>>();
            private List<EnemyContact> _resultBuffer = new List<EnemyContact>();

            /// <summary>
            /// Gets the N closest enemies sorted by distance from cockpit.
            /// Uses pre-allocated buffers to reduce garbage collection.
            /// </summary>
            public List<EnemyContact> GetClosestNEnemies(int n)
            {
                _resultBuffer.Clear();

                if (_cockpit == null || enemyList.Count == 0)
                    return _resultBuffer;

                Vector3D cockpitPos = GetCockpitPosition();

                // Reuse sort buffer
                _sortBuffer.Clear();
                for (int i = 0; i < enemyList.Count; i++)
                {
                    double distance = Vector3D.Distance(enemyList[i].Position, cockpitPos);
                    _sortBuffer.Add(new KeyValuePair<double, EnemyContact>(distance, enemyList[i]));
                }

                // Sort by distance
                _sortBuffer.Sort((a, b) => a.Key.CompareTo(b.Key));

                // Take top N
                int count = Math.Min(n, _sortBuffer.Count);
                for (int i = 0; i < count; i++)
                {
                    _resultBuffer.Add(_sortBuffer[i].Value);
                }

                return _resultBuffer;
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
                double ageSeconds = contact.AgeSeconds;

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

            // ------------------------------
            // GUN SYSTEM
            // ------------------------------

            /// <summary>
            /// Gets total ammo count across all gatling guns.
            /// Returns the number of ammunition items (NATO 25x184mm or similar).
            /// </summary>
            public int GetTotalGunAmmo()
            {
                int total = 0;
                for (int i = 0; i < _gatlings.Count; i++)
                {
                    var gun = _gatlings[i];
                    if (gun == null || !gun.IsFunctional)
                        continue;

                    var inventory = gun.GetInventory();
                    if (inventory == null)
                        continue;

                    // Sum all items in the gun's inventory (ammo magazines)
                    for (int j = 0; j < inventory.ItemCount; j++)
                    {
                        var item = inventory.GetItemAt(j);
                        if (item.HasValue)
                        {
                            total += (int)item.Value.Amount;
                        }
                    }
                }
                return total;
            }

            /// <summary>
            /// Checks if any gatling gun has ammo and is functional.
            /// </summary>
            public bool HasGunAmmo()
            {
                return GetTotalGunAmmo() > 0;
            }

            /// <summary>
            /// Gets the number of functional gatling guns.
            /// </summary>
            public int GetGunCount()
            {
                int count = 0;
                for (int i = 0; i < _gatlings.Count; i++)
                {
                    if (_gatlings[i] != null && _gatlings[i].IsFunctional)
                        count++;
                }
                return count;
            }
        }
    }
}
