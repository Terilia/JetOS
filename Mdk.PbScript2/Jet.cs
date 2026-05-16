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
            string selectedEnemyName = "";
            long selectedEnemyEntityId;
            int selectedEnemySourceIndex;

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
                    return SameTarget(EntityId, Name, SourceIndex, other.EntityId, other.Name, other.SourceIndex);
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
            const double CONTACT_DECAY_SECONDS = 30;   // wall-clock seconds without update before removal
            private double decayCheckAccum = 0;
            private const double DECAY_CHECK_SECONDS = 1;      // re-check decay once per wall-clock second

            // Cached gravity vector (updated once per tick by SystemManager)
            public Vector3D CachedGravity = VZ;
            public MatrixD CockpitMatrix = MatrixD.Identity;
            public Vector3D CockpitPosition = VZ;
            public Vector3D CockpitVelocity = VZ;
            public double CockpitSpeed = 0;
            public double SurfaceAltitude = 0;
            public double FuelPct = 0, FuelSec = 0;
            public float BatteryCurMWh = 0, BatteryMaxMWh = 0, BatteryNetDrainMW = 0, BatteryPct = 0;
            public int LeftUseFn, LeftUseTot, RightUseFn, RightUseTot;
            public int LeftAllFn, LeftAllTot, RightAllFn, RightAllTot;
            public int LeftAbFn, LeftAbTot, RightAbFn, RightAbTot;
            public int LeftAllDam, RightAllDam, LeftAllMax, RightAllMax;
            public float LeftUseCurKN, LeftUseMaxKN, RightUseCurKN, RightUseMaxKN, LeftAbCurKN, RightAbCurKN;
            double engineClassifyAge = double.MaxValue;
            const double ENGINE_CLASSIFY_SECONDS = 1.0;
            public bool LeftEngineBad { get { return LeftAllMax > 0 && (LeftAllTot < LeftAllMax || LeftAllFn < LeftAllTot || LeftAllDam > 0); } }
            public bool RightEngineBad { get { return RightAllMax > 0 && (RightAllTot < RightAllMax || RightAllFn < RightAllTot || RightAllDam > 0); } }

            public static bool SameTarget(long aId, string aName, int aSource, long bId, string bName, int bSource)
            {
                return aId != 0 && bId != 0 ? aId == bId : aId == bId && aSource == bSource && !SE(aName) && aName == bName;
            }

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

            public void UpdateTickCache()
            {
                if (_cockpit == null) return;
                CockpitMatrix = WM(_cockpit);
                CockpitPosition = GP(_cockpit);
                CockpitVelocity = LV(_cockpit);
                CockpitSpeed = _cockpit.GetShipSpeed();
                _cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out SurfaceAltitude);
                CachedGravity = _cockpit.GetNaturalGravity();
                ClassifyEnginesIfNeeded();
                UpdateResourceCache();
                UpdateEngineMetricCache();
            }

            static readonly Vector3I FORWARD_PROPULSION_DIRECTION = Vector3I.Backward;
            static readonly Vector3I REVERSE_PROPULSION_DIRECTION = Vector3I.Forward;

            public void ClassifyEnginesIfNeeded()
            {
                if (_cockpit == null) return;
                engineClassifyAge += SystemManager.DeltaSeconds;
                if (engineClassifyAge < ENGINE_CLASSIFY_SECONDS) return;
                engineClassifyAge = 0;

                leftEngines.Clear(); rightEngines.Clear(); centerEngines.Clear();
                leftAB.Clear(); rightAB.Clear(); centerAB.Clear();
                leftEnginesAll.Clear(); rightEnginesAll.Clear(); centerEnginesAll.Clear();
                leftABAll.Clear(); rightABAll.Clear(); centerABAll.Clear();

                Vector3D cockpitRight = CockpitMatrix.Right;
                Vector3D cockpitPos = CockpitPosition;
                const double LATERAL_TOLERANCE = 1.25;

                for (int i = 0; i < _thrustersbackwards.Count; i++)
                {
                    var t = _thrustersbackwards[i];
                    if (t == null || !t.IsSameConstructAs(_cockpit)) continue;
                    Vector3I dir = t.GridThrustDirection;
                    bool forwardPropulsion = dir == FORWARD_PROPULSION_DIRECTION;
                    bool reversePropulsion = dir == REVERSE_PROPULSION_DIRECTION;
                    if (!forwardPropulsion && !reversePropulsion)
                        continue;

                    double rightOffset = Vector3D.Dot(t.GetPosition() - cockpitPos, cockpitRight);
                    bool isLeft = rightOffset < -LATERAL_TOLERANCE;
                    bool isRight = rightOffset > LATERAL_TOLERANCE;
                    bool isCenter = !isLeft && !isRight;
                    bool isHydrogen = t.BlockDefinition.SubtypeId.Contains(S_HYDROGEN);
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
            }

            void UpdateResourceCache()
            {
                GetFuelStatus(out FuelPct, out FuelSec);
                GetBatteryStatus(out BatteryCurMWh, out BatteryMaxMWh, out BatteryNetDrainMW);
                BatteryPct = BatteryMaxMWh > 0 ? Cl(BatteryCurMWh / BatteryMaxMWh, 0f, 1f) : 0f;
            }

            void UpdateEngineMetricCache()
            {
                CacheEngineSide(leftEnginesAll, leftABAll, leftEngines, leftAB,
                    out LeftAllFn, out LeftAllTot, out LeftUseFn, out LeftUseTot,
                    out LeftAbFn, out LeftAbTot, out LeftUseCurKN, out LeftUseMaxKN, out LeftAbCurKN, out LeftAllDam);
                CacheEngineSide(rightEnginesAll, rightABAll, rightEngines, rightAB,
                    out RightAllFn, out RightAllTot, out RightUseFn, out RightUseTot,
                    out RightAbFn, out RightAbTot, out RightUseCurKN, out RightUseMaxKN, out RightAbCurKN, out RightAllDam);
                if (LeftAllTot > LeftAllMax) LeftAllMax = LeftAllTot;
                if (RightAllTot > RightAllMax) RightAllMax = RightAllTot;
            }

            static void CacheEngineSide(List<IMyThrust> allEng, List<IMyThrust> allAb,
                List<IMyThrust> useEng, List<IMyThrust> useAb,
                out int allFn, out int allTot, out int useFn, out int useTot,
                out int abFn, out int abTot, out float useCur, out float useMax, out float abCur, out int allDam)
            {
                int af, at, ef, et, ad, abd;
                GetEngineHealth(allEng, out af, out at, out ad);
                GetEngineHealth(allAb, out abFn, out abTot, out abd);
                allFn = af + abFn; allTot = at + abTot;
                allDam = ad + abd;
                GetEngineHealth(useEng, out ef, out et, out ad);
                GetEngineHealth(useAb, out af, out at, out abd);
                useFn = ef + af; useTot = et + at;
                float cur, max, abMax;
                GetEngineThrust(useEng, out cur, out max);
                GetEngineThrust(useAb, out abCur, out abMax);
                useCur = cur + abCur; useMax = max + abMax;
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
                    || HasText(subtype, S_HYDROGEN) || HasText(name, S_HYDROGEN);
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
            public void UpdateOrAddEnemy(Vector3D pos, Vector3D vel, string name, int sourceIndex, long entityId = 0, double observedAgeSeconds = 0)
            {
                int existingIndex = -1;

                // Priority 1: Match by EntityId — O(1) dictionary lookup
                // TryGetValue sets out param to 0 (not -1) on miss — use temp to avoid false match
                if (entityId != 0)
                {
                    int tmp;
                    if (_entityIdIndex.TryGetValue(entityId, out tmp))
                        existingIndex = tmp;
                }

                // Priority 2/3 are only for legacy no-id contacts. Real ids are authoritative.
                if (entityId == 0 && existingIndex < 0 && !SE(name))
                {
                    for (int i = 0; i < enemyList.Count; i++)
                    {
                        if (SameTarget(0, name, sourceIndex, enemyList[i].EntityId, enemyList[i].Name, enemyList[i].SourceIndex))
                        {
                            existingIndex = i;
                            break;
                        }
                    }
                }

                if (existingIndex < 0)
                {
                    for (int i = 0; i < enemyList.Count; i++)
                    {
                        if (entityId != 0 && enemyList[i].EntityId != 0 && enemyList[i].EntityId != entityId)
                            continue;
                        if ((enemyList[i].Position - pos).LengthSquared() < 2500)
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
                    if (dt > 0 && dt < 5) // <5s old
                    {
                        Vector3D rawAccel = (vel - enemyList[existingIndex].Velocity) / dt;
                        accel = enemyList[existingIndex].Acceleration * 0.6 + rawAccel * 0.4; // EMA α=0.4
                    }
                }

                if (existingIndex >= 0)
                {
                    var old = enemyList[existingIndex];
                    if (sourceIndex < 0 && old.SourceIndex >= 0 && old.AgeSeconds <= 3)
                        return;
                    if (sourceIndex >= 0 && sourceIndex < 100 && old.SourceIndex > 99 && old.AgeSeconds <= 3)
                        return;
                }

                long contactId = entityId;
                if (contactId == 0 && existingIndex >= 0)
                    contactId = enemyList[existingIndex].EntityId;
                if (existingIndex >= 0 && SE(name))
                    name = enemyList[existingIndex].Name;

                EnemyContact contact = new EnemyContact(pos, vel, name, sourceIndex, contactId, accel);
                if (observedAgeSeconds > 0)
                {
                    contact.LastSeen = GameSeconds - observedAgeSeconds;
                    contact.LastHistoryShift = contact.LastSeen;
                }

                // Carry over and advance tracking history
                if (existingIndex >= 0)
                {
                    var old = enemyList[existingIndex];
                    // Update EntityId index: remove old mapping if EntityId changed
                    if (old.EntityId != 0 && old.EntityId != contactId)
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
                    if (observedAgeSeconds > 0)
                        contact.LastHistoryShift = contact.LastSeen;
                    // else elapsedSeconds >= 30: history is all stale, new contact starts fresh with 1
                    enemyList[existingIndex] = contact;
                    if (contactId != 0) _entityIdIndex[contactId] = existingIndex;
                }
                else
                {
                    if (contactId != 0) _entityIdIndex[contactId] = enemyList.Count;
                    enemyList.Add(contact);
                    SoundManager.Event(SoundManager.NEW_TARGET);
                }
            }

            /// <summary>
            /// Removes contacts older than CONTACT_DECAY_SECONDS.
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
                    bool isSelected = SameTarget(c.EntityId, c.Name, c.SourceIndex, selectedEnemyEntityId, selectedEnemyName, selectedEnemySourceIndex);
                    if (c.AgeSeconds > CONTACT_DECAY_SECONDS)
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
            private List<KeyValuePair<double, EnemyContact>> SortEnemiesByDistance()
            {
                _sortBuffer.Clear();
                if (_cockpit == null) return _sortBuffer;
                Vector3D cockpitPos = CockpitPosition;
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
                for (int i = 0; i < enemyList.Count; i++)
                    if (SameTarget(selectedEnemyEntityId, selectedEnemyName, selectedEnemySourceIndex, enemyList[i].EntityId, enemyList[i].Name, enemyList[i].SourceIndex))
                        return enemyList[i];
                return null;
            }

            public bool IsSelected(EnemyContact contact)
            {
                return SameTarget(selectedEnemyEntityId, selectedEnemyName, selectedEnemySourceIndex, contact.EntityId, contact.Name, contact.SourceIndex);
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
                selectedEnemySourceIndex = contact.SourceIndex;
            }

            public long GetSelectedEnemyId()
            {
                return selectedEnemyEntityId;
            }

            public bool IsSelectedEntity(long entityId)
            {
                return entityId != 0 && selectedEnemyEntityId == entityId;
            }

            public void ClearSelection()
            {
                selectedEnemyName = "";
                selectedEnemyEntityId = 0;
                selectedEnemySourceIndex = 0;
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

            // ------------------------------
            // THRUSTERS
            // ------------------------------

            /// <summary>
            /// Returns (functional, total) count for an engine group.
            /// </summary>
            public static void GetEngineHealth(List<IMyThrust> engines, out int functional, out int total, out int damaged)
            {
                total = engines.Count;
                functional = 0;
                damaged = 0;
                for (int i = 0; i < engines.Count; i++)
                {
                    var e = engines[i];
                    if (e != null && e.IsFunctional)
                        functional++;
                    if (EngineDamaged(e))
                        damaged++;
                }
            }

            static bool EngineDamaged(IMyThrust e)
            {
                if (e == null || !e.IsFunctional) return true;
                var s = e.CubeGrid.GetCubeBlock(e.Position);
                return s == null || s.CurrentDamage > 0.01f;
            }

            /// <summary>
            /// Returns (currentThrust, maxEffectiveThrust) in kN for an engine group.
            /// </summary>
            public static void GetEngineThrust(List<IMyThrust> engines, out float current, out float max)
            {
                current = 0f; max = 0f;
                for (int i = 0; i < engines.Count; i++)
                {
                    var e = engines[i];
                    if (e == null || !e.IsFunctional) continue;
                    current += e.CurrentThrust;
                    max += e.MaxEffectiveThrust;
                }
                current /= 1000f; // Convert N to kN
                max /= 1000f;
            }

            public void GetBatteryStatus(out float currentMWh, out float maxMWh, out float netDrainMW)
            {
                currentMWh = 0f; maxMWh = 0f; netDrainMW = 0f;
                for (int i = 0; i < batteries.Count; i++)
                {
                    var b = batteries[i];
                    if (b == null || !b.IsFunctional) continue;
                    currentMWh += b.CurrentStoredPower;
                    maxMWh += b.MaxStoredPower;
                    netDrainMW += b.CurrentOutput - b.CurrentInput;
                }
            }

            public void GetFuelStatus(out double fillRatio, out double remainSeconds)
            {
                double cap = 0, filled = 0;
                for (int i = 0; i < tanks.Count; i++)
                {
                    var t = tanks[i];
                    if (t == null) continue;
                    if (!t.BlockDefinition.SubtypeId.Contains(S_HYDROGEN)) continue;
                    cap += t.Capacity;
                    filled += t.Capacity * t.FilledRatio;
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
