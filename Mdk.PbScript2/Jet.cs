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
        public class Jet
        {
            // Core blocks
            public IMyCockpit _cockpit;
            public List<IMyThrust> _thrustersbackwards;

            // Engine grouping (left/right split by grid position, populated in constructor)
            public List<IMyThrust> leftEngines = new List<IMyThrust>();
            public List<IMyThrust> rightEngines = new List<IMyThrust>();
            public List<IMyThrust> centerEngines = new List<IMyThrust>();
            public List<IMyThrust> leftAB = new List<IMyThrust>();
            public List<IMyThrust> rightAB = new List<IMyThrust>();
            public List<IMyThrust> centerAB = new List<IMyThrust>();
            public List<IMyThrust> leftEnginesAll = new List<IMyThrust>();
            public List<IMyThrust> rightEnginesAll = new List<IMyThrust>();
            public List<IMyThrust> centerEnginesAll = new List<IMyThrust>();
            public List<IMyThrust> leftABAll = new List<IMyThrust>();
            public List<IMyThrust> rightABAll = new List<IMyThrust>();
            public List<IMyThrust> centerABAll = new List<IMyThrust>();

            // Wall-clock elapsed seconds (mirror of SystemManager.ElapsedSeconds — lag-resistant).
            public static double GameSeconds = 0.0;
            public static int IC, IP, IA;

            // Identity-based target selection
            public string selectedEnemyName = "";
            public long selectedEnemyEntityId = 0;

            // Enemy contact tracking with decay
            public struct EnemyContact
            {
                public Vector3D Position;
                public Vector3D Velocity;
                public Vector3D Acceleration;
                public string Name;
                public long EntityId;        // For reliable matching (0 if unknown)
                public double LastSeen;      // Wall-clock seconds (GameSeconds at last update)
                public int SourceIndex;      // Which AI combo detected this (0=primary, 1=RWR, 2=third combo, etc.)

                // 30-second tracking timeline: each bit = 1 second, bit 0 = most recent
                // 1 = radar update received, 0 = no update (stale)
                public uint TrackHistory;
                public double LastHistoryShift; // GameSeconds when history was last shifted

                public EnemyContact(Vector3D pos, Vector3D vel, string name, int source, long entityId = 0, Vector3D accel = default(Vector3D))
                {
                    Position = pos;
                    Velocity = vel;
                    Acceleration = accel;
                    Name = name;
                    EntityId = entityId;
                    LastSeen = GameSeconds;
                    SourceIndex = source;
                    TrackHistory = 0x3FFFFFFF; // all 30 bits set — new contact starts fully green
                    LastHistoryShift = GameSeconds;
                }

                public double AgeSeconds => GameSeconds - LastSeen;
                public bool IsStale => AgeSeconds > CONTACT_DECAY_SECONDS;

                /// <summary>
                /// Returns the 30-bit tracking history adjusted for current staleness.
                /// Bit 0 = most recent second, bit 29 = 30 seconds ago.
                /// </summary>
                public bool Matches(EnemyContact other)
                {
                    if (EntityId != 0 && other.EntityId != 0)
                        return EntityId == other.EntityId;
                    if (!string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(other.Name))
                        return Name == other.Name;
                    return VDi(Position, other.Position) < 50.0;
                }

                public uint GetDisplayHistory()
                {
                    double elapsed = GameSeconds - LastHistoryShift;
                    int elapsedSeconds = (int)elapsed;
                    if (elapsedSeconds <= 0) return TrackHistory;
                    if (elapsedSeconds >= 30) return 0;
                    // Shift left to insert stale gap for seconds since last shift
                    return TrackHistory << elapsedSeconds;
                }
            }

            public List<EnemyContact> enemyList = new List<EnemyContact>();
            Dictionary<long, int> _entityIdIndex = new Dictionary<long, int>();
            public const double CONTACT_DECAY_SECONDS = 30.0;   // wall-clock seconds without update before removal
            public const double SELECTED_DECAY_SECONDS = 60.0;  // longer timeout for the pilot-selected target
            private double decayCheckAccum = 0;
            private const double DECAY_CHECK_SECONDS = 1.0;      // re-check decay once per wall-clock second

            // Cached gravity vector (updated once per tick by SystemManager)
            public Vector3D CachedGravity = VZ;

            public List<IMyShipMergeBlock> _bays;
            public List<IMyTerminalBlock> leftstab = new List<IMyTerminalBlock>();
            public List<IMyTerminalBlock> rightstab = new List<IMyTerminalBlock>();
            public IMyTerminalBlock hudBlock;
            public IMyTextSurface hud;
            public List<IMyGasTank> tanks = new List<IMyGasTank>();
            public List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
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
                    _thrustersbackwards = new List<IMyThrust>();
                    _bays = new List<IMyShipMergeBlock>();
                    return;
                }

                grid.GetBlocksOfType(_gatlings, t => t.IsSameConstructAs(_cockpit));

                // bays
                _bays = new List<IMyShipMergeBlock>();
                grid.GetBlocksOfType(_bays, b => b.CustomName.Contains("Bay") && b.IsSameConstructAs(_cockpit));
                _bays.Sort(
                    (a, b) =>
                        ExtractBayNumber(a.CustomName).CompareTo(ExtractBayNumber(b.CustomName))
                );

                grid.GetBlocksOfType(rightstab, g => g.CustomName.Contains("invertedstab") && g.IsSameConstructAs(_cockpit));
                grid.GetBlocksOfType(leftstab, g => g.CustomName.Contains("normalstab") && g.IsSameConstructAs(_cockpit));

                // Collect all jet-family, non-industrial thrusters on the construct.
                // Direction filter + lateral split is deferred to ClassifyEnginesIfNeeded(),
                // called from HUDModule.Tick. We can't filter by GridThrustDirection here
                // because that property may return stale/Zero values during Program() ctor —
                // the grid's thrust system hasn't necessarily registered every sub-grid thruster
                // yet, especially for non-Large-Atmospheric subtypes (Sci-Fi, Hydrogen).
                // First-tick deferral lets the engine register those properties before we read.
                _thrustersbackwards = new List<IMyThrust>();
                grid.GetBlocksOfType(
                    _thrustersbackwards,
                    g => g.IsSameConstructAs(_cockpit) && IsJetEngineCandidate(g)
                );

                hudBlock = grid.GetBlockWithName("Fighter HUD [HFPS]");
                hud = hudBlock as IMyTextSurface;
                grid.GetBlocksOfType(tanks, g => g.IsSameConstructAs(_cockpit) && g.CustomName.Contains("Jet"));
                grid.GetBlocksOfType(batteries, b => b.IsSameConstructAs(_cockpit));
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
            // ENGINE CLASSIFICATION (deferred from ctor — see Jet ctor comment)
            // ------------------------------

            // Diagnostic output of the most recent classification pass.
            // HUDModule echoes this so the pilot can see what got assigned where and why.
            public string EngineDebug = "";
            static readonly Vector3I FORWARD_PROPULSION_DIRECTION = Vector3I.Backward;
            static readonly Vector3I REVERSE_PROPULSION_DIRECTION = Vector3I.Forward;

            public void ClassifyEnginesIfNeeded()
            {
                if (_cockpit == null) return;

                leftEngines.Clear(); rightEngines.Clear(); centerEngines.Clear();
                leftAB.Clear(); rightAB.Clear(); centerAB.Clear();
                leftEnginesAll.Clear(); rightEnginesAll.Clear(); centerEnginesAll.Clear();
                leftABAll.Clear(); rightABAll.Clear(); centerABAll.Clear();

                Vector3D cockpitRight = _cockpit.WorldMatrix.Right;
                Vector3D cockpitPos = _cockpit.GetPosition();
                const double LATERAL_TOLERANCE = 1.25;

                int rejected = 0, reverseRecognized = 0;
                StringBuilder rejectList = null;
                for (int i = 0; i < _thrustersbackwards.Count; i++)
                {
                    var t = _thrustersbackwards[i];
                    if (t == null) continue;
                    Vector3I dir = t.GridThrustDirection;
                    bool forwardPropulsion = dir == FORWARD_PROPULSION_DIRECTION;
                    bool reversePropulsion = dir == REVERSE_PROPULSION_DIRECTION;
                    if (!forwardPropulsion && !reversePropulsion)
                    {
                        rejected++;
                        if (rejected <= 8)
                        {
                            if (rejectList == null) rejectList = new StringBuilder();
                            rejectList.Append("  rej dir=").Append(dir)
                                .Append(' ').Append(t.CustomName).Append('\n');
                        }
                        continue;
                    }
                    if (reversePropulsion) reverseRecognized++;

                    double rightOffset = Vector3D.Dot(t.GetPosition() - cockpitPos, cockpitRight);
                    bool isLeft = rightOffset < -LATERAL_TOLERANCE;
                    bool isRight = rightOffset > LATERAL_TOLERANCE;
                    bool isCenter = !isLeft && !isRight;
                    bool isHydrogen = t.BlockDefinition.SubtypeId.Contains("Hydrogen");
                    if (isHydrogen)
                    {
                        AddEngineToSide(t, isLeft, isRight, leftABAll, centerABAll, rightABAll);
                        if (forwardPropulsion)
                            AddEngineToSide(t, isLeft, isRight, leftAB, centerAB, rightAB);
                    }
                    else
                    {
                        AddEngineToSide(t, isLeft, isRight, leftEnginesAll, centerEnginesAll, rightEnginesAll);
                        if (forwardPropulsion)
                            AddEngineToSide(t, isLeft, isRight, leftEngines, centerEngines, rightEngines);
                    }
                }

                EngineDebug =
                    $"Engines: cand={_thrustersbackwards.Count} rev={reverseRecognized} rej={rejected}\n" +
                    $"  USE ATMO L/C/R: {leftEngines.Count}/{centerEngines.Count}/{rightEngines.Count}\n" +
                    $"  USE AB   L/C/R: {leftAB.Count}/{centerAB.Count}/{rightAB.Count}\n" +
                    $"  ALL ATMO L/C/R: {leftEnginesAll.Count}/{centerEnginesAll.Count}/{rightEnginesAll.Count}\n" +
                    $"  ALL AB   L/C/R: {leftABAll.Count}/{centerABAll.Count}/{rightABAll.Count}\n" +
                    (rejectList != null ? rejectList.ToString() : "");
            }

            static void AddEngineToSide(IMyThrust t, bool isLeft, bool isRight,
                List<IMyThrust> left, List<IMyThrust> center, List<IMyThrust> right)
            {
                if (isLeft) left.Add(t);
                else if (isRight) right.Add(t);
                else center.Add(t);
            }

            static bool IsJetEngineCandidate(IMyThrust t)
            {
                if (t == null) return false;
                string subtype = t.BlockDefinition.SubtypeId ?? "";
                string name = t.CustomName ?? "";
                if (HasText(subtype, "Industrial") || HasText(name, "Industrial")) return false;
                return HasText(subtype, "Atmospheric") || HasText(name, "Atmospheric")
                    || HasText(subtype, "Hydrogen") || HasText(name, "Hydrogen");
            }

            static bool HasText(string text, string value)
            {
                return text != null && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
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
                const double PROXIMITY_SQ = 50.0 * 50.0; // 50m merge threshold, squared

                int existingIndex = -1;

                // Priority 1: Match by EntityId — O(1) dictionary lookup
                // TryGetValue sets out param to 0 (not -1) on miss — use temp to avoid false match
                if (entityId != 0)
                {
                    int tmp;
                    if (_entityIdIndex.TryGetValue(entityId, out tmp))
                        existingIndex = tmp;
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
                        if ((enemyList[i].Position - pos).LengthSquared() < PROXIMITY_SQ)
                        {
                            existingIndex = i;
                            break;
                        }
                    }
                }

                Vector3D accel = VZ;
                if (existingIndex >= 0)
                {
                    double dt = GameSeconds - enemyList[existingIndex].LastSeen;
                    if (dt > 0 && dt < 5.0) // <5s old
                    {
                        Vector3D rawAccel = (vel - enemyList[existingIndex].Velocity) / dt;
                        accel = enemyList[existingIndex].Acceleration * 0.6 + rawAccel * 0.4; // EMA α=0.4
                    }
                }

                EnemyContact contact = new EnemyContact(pos, vel, name, sourceIndex, entityId, accel);

                // Carry over and advance tracking history
                if (existingIndex >= 0)
                {
                    var old = enemyList[existingIndex];
                    // Update EntityId index: remove old mapping if EntityId changed
                    if (old.EntityId != 0 && old.EntityId != entityId)
                        _entityIdIndex.Remove(old.EntityId);

                    int elapsedSeconds = (int)(GameSeconds - old.LastHistoryShift);
                    if (elapsedSeconds > 0 && elapsedSeconds < 30)
                    {
                        contact.TrackHistory = (old.TrackHistory << elapsedSeconds) | 1;
                        contact.LastHistoryShift = GameSeconds;
                    }
                    else if (elapsedSeconds == 0)
                    {
                        contact.TrackHistory = old.TrackHistory | 1;
                        contact.LastHistoryShift = old.LastHistoryShift; // keep old reference
                    }
                    // else elapsedSeconds >= 30: history is all stale, new contact starts fresh with 1
                    enemyList[existingIndex] = contact;
                    if (entityId != 0) _entityIdIndex[entityId] = existingIndex;
                }
                else
                {
                    if (entityId != 0) _entityIdIndex[entityId] = enemyList.Count;
                    enemyList.Add(contact);
                }
            }

            /// <summary>
            /// Removes contacts older than CONTACT_DECAY_SECONDS (or SELECTED_DECAY_SECONDS if selected).
            /// Throttled to run at most once per DECAY_CHECK_SECONDS of wall-clock time.
            /// </summary>
            public void UpdateEnemyDecay()
            {
                decayCheckAccum += SystemManager.DeltaSeconds;
                if (decayCheckAccum < DECAY_CHECK_SECONDS)
                    return;

                decayCheckAccum = 0;
                int prevCount = enemyList.Count;

                for (int i = enemyList.Count - 1; i >= 0; i--)
                {
                    var c = enemyList[i];
                    bool isSelected = (c.EntityId != 0 && c.EntityId == selectedEnemyEntityId)
                        || (!string.IsNullOrEmpty(c.Name) && c.Name == selectedEnemyName);
                    double timeout = isSelected ? SELECTED_DECAY_SECONDS : CONTACT_DECAY_SECONDS;
                    if (c.AgeSeconds > timeout)
                    {
                        if (isSelected) ClearSelection();
                        enemyList.RemoveAt(i);
                    }
                }

                // Rebuild EntityId index if any contacts were removed (indices shifted)
                if (enemyList.Count != prevCount)
                {
                    _entityIdIndex.Clear();
                    for (int i = 0; i < enemyList.Count; i++)
                    {
                        long eid = enemyList[i].EntityId;
                        if (eid != 0) _entityIdIndex[eid] = i;
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

                var sorted = SortEnemiesByDistance();
                int count = Mn(n, sorted.Count);
                for (int i = 0; i < count; i++)
                {
                    _resultBuffer.Add(sorted[i].Value);
                }

                return _resultBuffer;
            }

            private List<KeyValuePair<double, EnemyContact>> SortEnemiesByDistance()
            {
                _sortBuffer.Clear();
                if (_cockpit == null) return _sortBuffer;
                Vector3D cockpitPos = GetCockpitPosition();
                for (int i = 0; i < enemyList.Count; i++)
                {
                    double distance = VDi(enemyList[i].Position, cockpitPos);
                    _sortBuffer.Add(new KeyValuePair<double, EnemyContact>(distance, enemyList[i]));
                }
                _sortBuffer.Sort((a, b) => a.Key.CompareTo(b.Key));
                return _sortBuffer;
            }

            // ------------------------------
            // IDENTITY-BASED TARGET SELECTION
            // ------------------------------

            public EnemyContact? GetSelectedEnemy()
            {
                if (selectedEnemyEntityId != 0)
                {
                    for (int i = 0; i < enemyList.Count; i++)
                    {
                        if (enemyList[i].EntityId == selectedEnemyEntityId)
                            return enemyList[i];
                    }
                }

                if (!string.IsNullOrEmpty(selectedEnemyName))
                {
                    for (int i = 0; i < enemyList.Count; i++)
                    {
                        if (enemyList[i].Name == selectedEnemyName)
                            return enemyList[i];
                    }
                }

                return null;
            }

            /// <summary>
            /// Convenience null+staleness check for selected enemy.
            /// </summary>
            public bool HasSelectedEnemy()
            {
                return GetSelectedEnemy().HasValue;
            }

            public void SelectEnemy(EnemyContact contact)
            {
                selectedEnemyName = contact.Name;
                selectedEnemyEntityId = contact.EntityId;
            }

            public void ClearSelection()
            {
                selectedEnemyName = "";
                selectedEnemyEntityId = 0;
            }

            // Reusable buffer for sorted-by-distance results
            private List<EnemyContact> _distanceSortedBuffer = new List<EnemyContact>();

            public List<EnemyContact> GetEnemiesSortedByDistance()
            {
                _distanceSortedBuffer.Clear();
                var sorted = SortEnemiesByDistance();
                for (int i = 0; i < sorted.Count; i++)
                {
                    _distanceSortedBuffer.Add(sorted[i].Value);
                }

                return _distanceSortedBuffer;
            }

            /// <summary>
            /// Gets a color for enemy contacts based on age (for HUD decay visualization)
            /// </summary>
            public Color GetEnemyContactColor(EnemyContact contact)
            {
                double ageSeconds = contact.AgeSeconds;

                if (ageSeconds < 3)
                {
                    // Fresh: Bright red
                    return Cr(255, 0, 0);
                }
                else if (ageSeconds < 6)
                {
                    // Aging: Orange
                    return Cr(255, 165, 0);
                }
                else
                {
                    // Stale: Yellow
                    return Cr(255, 255, 0);
                }
            }

            // ------------------------------
            // COCKPIT & SHIP INFO
            // ------------------------------

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
            public Vector3D GetCockpitPosition() => _cockpit?.GetPosition() ?? VZ;

            // ------------------------------
            // THRUSTERS
            // ------------------------------

            /// <summary>
            /// Returns (functional, total) count for an engine group.
            /// </summary>
            public static void GetEngineHealth(List<IMyThrust> engines, out int functional, out int total)
            {
                total = engines.Count;
                functional = 0;
                for (int i = 0; i < engines.Count; i++)
                {
                    if (engines[i] != null && engines[i].IsFunctional)
                        functional++;
                }
            }

            /// <summary>
            /// Returns (currentThrust, maxEffectiveThrust) in kN for an engine group.
            /// </summary>
            public static void GetEngineThrust(List<IMyThrust> engines, out float current, out float max)
            {
                current = 0f; max = 0f;
                for (int i = 0; i < engines.Count; i++)
                {
                    if (engines[i] == null || !engines[i].IsFunctional) continue;
                    current += engines[i].CurrentThrust;
                    max += engines[i].MaxEffectiveThrust;
                }
                current /= 1000f; // Convert N to kN
                max /= 1000f;
            }

            public void GetBatteryStatus(out float currentMWh, out float maxMWh, out float netDrainMW)
            {
                currentMWh = 0f; maxMWh = 0f; netDrainMW = 0f;
                for (int i = 0; i < batteries.Count; i++)
                {
                    if (batteries[i] == null || !batteries[i].IsFunctional) continue;
                    currentMWh += batteries[i].CurrentStoredPower;
                    maxMWh += batteries[i].MaxStoredPower;
                    netDrainMW += batteries[i].CurrentOutput - batteries[i].CurrentInput;
                }
            }

            public void GetFuelStatus(out double fillRatio, out double remainSeconds)
            {
                double cap = 0, filled = 0;
                for (int i = 0; i < tanks.Count; i++)
                {
                    if (tanks[i] == null) continue;
                    if (!tanks[i].BlockDefinition.SubtypeId.Contains("Hydrogen")) continue;
                    cap += tanks[i].Capacity;
                    filled += tanks[i].Capacity * tanks[i].FilledRatio;
                }
                fillRatio = cap > 0 ? filled / cap : 0;
                remainSeconds = fillRatio * 600; // same 10min assumption as GridVisualization
            }

            // ------------------------------
            // GUN SYSTEM
            // ------------------------------

            /// <summary>
            /// Gets ammo count for a single gatling gun.
            /// </summary>
            public static int GetGunAmmo(IMySmallGatlingGun gun)
            {
                if (gun == null || !gun.IsFunctional)
                    return 0;

                var inventory = gun.GetInventory();
                if (inventory == null)
                    return 0;

                int ammo = 0;
                for (int j = 0; j < inventory.ItemCount; j++)
                {
                    var item = inventory.GetItemAt(j);
                    if (item.HasValue)
                    {
                        ammo += (int)item.Value.Amount;
                    }
                }
                return ammo;
            }

            /// <summary>
            /// Gets total ammo count across all gatling guns.
            /// Cached for CachedAmmoMaxAgeSeconds — inventory iteration is relatively expensive
            /// and ammo count is only used for display.
            /// </summary>
            private int _cachedTotalAmmo = 0;
            private double _cachedAmmoAgeSeconds = double.MaxValue;
            private const double CachedAmmoMaxAgeSeconds = 0.5;

            public int GetTotalGunAmmo()
            {
                _cachedAmmoAgeSeconds += SystemManager.DeltaSeconds;
                if (_cachedAmmoAgeSeconds < CachedAmmoMaxAgeSeconds)
                    return _cachedTotalAmmo;

                int total = 0;
                for (int i = 0; i < _gatlings.Count; i++)
                {
                    total += GetGunAmmo(_gatlings[i]);
                }
                _cachedTotalAmmo = total;
                _cachedAmmoAgeSeconds = 0;
                return total;
            }

        }
    }
}
