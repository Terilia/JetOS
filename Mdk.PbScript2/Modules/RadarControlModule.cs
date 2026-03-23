using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class RadarControlModule : ProgramModule
        {
            private Jet myJet;
            private List<RadarTrackingModule> allRadars = new List<RadarTrackingModule>();
            private List<AIBlockPair> detectedAIPairs = new List<AIBlockPair>();

            private struct AIBlockPair
            {
                public IMyFlightMovementBlock FlightBlock;
                public IMyOffensiveCombatBlock CombatBlock;
                public int Index;

                public AIBlockPair(IMyFlightMovementBlock flight, IMyOffensiveCombatBlock combat, int idx)
                {
                    FlightBlock = flight;
                    CombatBlock = combat;
                    Index = idx;
                }
            }

            // ==== Sequential Activation State Machine ====
            // Pool radars activate one at a time in a chain:
            //   IDLE → SEARCHING → LOCKED
            // Only 1 radar is SEARCHING at any time. When it finds a new target
            // (not already locked by another), it transitions to LOCKED and the
            // next IDLE radar becomes SEARCHING.
            private enum RadarRole { IDLE, SEARCHING, LOCKED, RWR }

            private class RadarState
            {
                public RadarRole Role;
                public long TrackedEntityId;
                public string TrackedName;
                public int TicksSinceLastSeen;
                // True once we've called ActivateBehavior_On for this radar at runtime
                public bool BehaviorActivated;
                // Cooldown ticks after activation before we start reading data
                public int ActivationCooldown;
            }

            private List<RadarState> radarStates = new List<RadarState>();

            // Target priority rotation for diversity in multi-target detection
            private static readonly string[] TargetPriorityActions = {
                "SetTargetPriority_Closest",
                "SetTargetPriority_Largest",
                "SetTargetPriority_Smallest"
            };
            private int nextPriorityIndex = 0;

            // ==== RWR (Radar Warning Receiver) Integrated Functionality ====
            private class RWRTrackingState
            {
                public string CurrentEnemyName = "";
                public int TicksSinceEnemyChange = 0;
                public List<Vector3D> PositionHistory;
                public int HistoryIndex = 0;
                public int TickCounter = 0;

                public RWRTrackingState()
                {
                    PositionHistory = new List<Vector3D>();
                    for (int i = 0; i < 10; i++)
                    {
                        PositionHistory.Add(VZ);
                    }
                }

                public void ClearHistory()
                {
                    for (int i = 0; i < PositionHistory.Count; i++)
                    {
                        PositionHistory[i] = VZ;
                    }
                    HistoryIndex = 0;
                }
            }

            private List<RWRTrackingState> rwrStates = new List<RWRTrackingState>();
            private bool rwrEnabled = true;
            private int configuredRWRCount = 0;
            private bool anyThreatDetected = false;

            public bool IsRWREnabled { get { return rwrEnabled; } }
            public bool IsThreat { get { return anyThreatDetected; } }
            public List<RWRWarning> activeThreats = new List<RWRWarning>();

            // True when ANY pool radar in LOCKED state matches the selected enemy
            public bool IsTrackLocked { get; private set; }

            private string lastConsoleOutput = "";

            // Accumulated absolute time for radar tracking (in ticks)
            private long accumulatedTimeTicks = 0;

            // Sequential init: activate RWR radars one per tick first, then start the chain
            private int initRWRIndex = 0;
            private bool rwrInitComplete = false;
            // Once RWR init is done, we activate the first pool radar as SEARCHING
            private bool poolChainStarted = false;

            // Activation cooldown: after calling ActivateBehavior_On, wait this many ticks
            // before reading data (SE needs time to process the action)
            private const int ACTIVATION_COOLDOWN = 10;
            // Ticks before a LOCKED radar that lost its target reverts to IDLE
            private const int LOST_TARGET_TIMEOUT = 120;

            public RadarControlModule(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                name = "Radar & RWR Control";

                // Auto-detect all AI Flight/Combat pairs (1-99)
                for (int i = 1; i <= 99; i++)
                {
                    string flightName = i == 1 ? "AI Flight" : $"AI Flight {i}";
                    string combatName = i == 1 ? "AI Combat" : $"AI Combat {i}";

                    var flightBlock = program.GridTerminalSystem.GetBlockWithName(flightName) as IMyFlightMovementBlock;
                    var combatBlock = program.GridTerminalSystem.GetBlockWithName(combatName) as IMyOffensiveCombatBlock;

                    if (flightBlock != null && combatBlock != null)
                    {
                        detectedAIPairs.Add(new AIBlockPair(flightBlock, combatBlock, i));
                        var radar = new RadarTrackingModule(flightBlock, combatBlock);
                        allRadars.Add(radar);
                        radarStates.Add(new RadarState());
                        rwrStates.Add(new RWRTrackingState());
                    }
                }

                // Load RWR config from CustomData
                int maxRWR = Mx(0, allRadars.Count - 1);
                string savedCount = SystemManager.GetCustomDataValue("RWRCount");
                int count;
                if (!string.IsNullOrEmpty(savedCount) && int.TryParse(savedCount, out count))
                {
                    configuredRWRCount = Mx(0, Mn(count, maxRWR));
                }
                else
                {
                    configuredRWRCount = allRadars.Count >= 2 ? 1 : 0;
                }

                // Assign initial roles — NO ApplyAction here (unreliable in constructor)
                ReassignRoles();

                program.Echo($"RadarControl: {allRadars.Count} radars, Pool: {GetSweepTrackPoolSize()}, RWR: {GetActiveRWRCount()}");
            }

            public override string[] GetOptions()
            {
                var options = new List<string>();

                if (allRadars.Count == 0)
                {
                    options.Add("No radars detected");
                    return options.ToArray();
                }

                // RWR Controls
                options.Add(string.Format("RWR [{0}]", rwrEnabled ? "ON" : "OFF"));
                int activeRWR = GetActiveRWRCount();
                int poolSize = GetSweepTrackPoolSize();
                options.Add(string.Format("RWR Units + (Current: {0}/{1})", activeRWR, allRadars.Count));
                options.Add(string.Format("RWR Units - (Current: {0}/{1})", activeRWR, allRadars.Count));

                // RWR Status
                if (rwrEnabled)
                {
                    int threatCount = activeThreats.Count;
                    if (threatCount > 0)
                    {
                        options.Add(string.Format("RWR: {0} THREAT{1}", threatCount, threatCount > 1 ? "S" : ""));
                    }
                    else
                    {
                        options.Add("RWR: Scanning...");
                    }
                }

                // Per-radar state display
                for (int i = 0; i < allRadars.Count; i++)
                {
                    var state = radarStates[i];
                    string roleStr;
                    switch (state.Role)
                    {
                        case RadarRole.SEARCHING:
                            roleStr = $"R{i + 1}: SEARCHING";
                            break;
                        case RadarRole.LOCKED:
                            roleStr = $"R{i + 1}: LOCKED [{state.TrackedName}]";
                            break;
                        case RadarRole.RWR:
                            roleStr = $"R{i + 1}: RWR";
                            break;
                        default:
                            roleStr = $"R{i + 1}: IDLE";
                            break;
                    }
                    options.Add(roleStr);
                }

                options.Add($"Pool: {poolSize} | RWR: {activeRWR}");
                options.Add($"Total Contacts: {myJet.enemyList.Count}");

                return options.ToArray();
            }

            public override void ExecuteOption(int index)
            {
                if (allRadars.Count == 0)
                    return;

                switch (index)
                {
                    case 0: // Toggle RWR ON/OFF
                        rwrEnabled = !rwrEnabled;
                        if (!rwrEnabled)
                        {
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
                        if (configuredRWRCount < allRadars.Count - 1)
                        {
                            configuredRWRCount++;
                            SystemManager.SetCustomDataValue("RWRCount", configuredRWRCount.ToString());
                            ReassignRoles();
                        }
                        break;

                    case 2: // Decrease RWR count
                        if (configuredRWRCount > 0)
                        {
                            configuredRWRCount--;
                            SystemManager.SetCustomDataValue("RWRCount", configuredRWRCount.ToString());
                            ReassignRoles();
                        }
                        break;
                }
            }

            public override void Tick()
            {
                if (allRadars.Count == 0) return;

                // Accumulate absolute time for radar tracking
                accumulatedTimeTicks += ParentProgram.Runtime.TimeSinceLastRun.Ticks;

                int poolSize = GetSweepTrackPoolSize();

                // UpdateTracking on ALL radars EVERY tick — even during init.
                // This keeps timestamps current so velocity calculations don't spike
                // when a radar first starts processing.
                for (int i = 0; i < allRadars.Count; i++)
                {
                    if (allRadars[i] != null)
                        allRadars[i].UpdateTracking(accumulatedTimeTicks);
                }

                // ============================================================
                // PHASE 0: Staggered initialization
                // First init all RWR radars (1 per tick), then start pool chain
                // ============================================================
                if (!rwrInitComplete)
                {
                    int rwrCount = GetActiveRWRCount();
                    if (rwrCount == 0 || initRWRIndex >= rwrCount)
                    {
                        rwrInitComplete = true;
                    }
                    else
                    {
                        int radarIndex = GetRWRRadarIndex(initRWRIndex);
                        if (radarIndex < allRadars.Count)
                        {
                            ActivateRadar(radarIndex, "SetTargetPriority_Closest");
                        }
                        initRWRIndex++;
                        // Don't process pool this tick — let SE digest the RWR activation
                        goto SkipPool;
                    }
                }

                if (!poolChainStarted && rwrInitComplete)
                {
                    // Start the chain: activate the first pool radar as SEARCHING
                    if (poolSize > 0)
                    {
                        StartSearching(0);
                    }
                    poolChainStarted = true;
                }

                // ============================================================
                // PHASE 2: Sequential pool processing
                // ============================================================
                IsTrackLocked = false;
                int searchingIndex = -1;

                for (int i = 0; i < poolSize; i++)
                {
                    var radar = allRadars[i];
                    var state = radarStates[i];
                    if (radar == null) continue;

                    // Decrement activation cooldown
                    if (state.ActivationCooldown > 0)
                    {
                        state.ActivationCooldown--;
                        continue; // Skip processing until cooldown expires
                    }

                    if (state.Role == RadarRole.SEARCHING)
                    {
                        searchingIndex = i;
                        ProcessSearchingRadar(i, poolSize);
                    }
                    else if (state.Role == RadarRole.LOCKED)
                    {
                        ProcessLockedRadar(i, poolSize);
                    }
                    // IDLE radars do nothing — they wait to be activated
                }

                // If no radar is currently SEARCHING and there are IDLE radars, start the next one
                if (searchingIndex == -1 && poolChainStarted)
                {
                    // Check if any radar became SEARCHING during processing (from ProcessLockedRadar demoting)
                    bool hasSearcher = false;
                    for (int i = 0; i < poolSize; i++)
                    {
                        if (radarStates[i].Role == RadarRole.SEARCHING)
                        {
                            hasSearcher = true;
                            break;
                        }
                    }

                    if (!hasSearcher)
                    {
                        // Find first IDLE radar and start it searching
                        for (int i = 0; i < poolSize; i++)
                        {
                            if (radarStates[i].Role == RadarRole.IDLE)
                            {
                                StartSearching(i);
                                break;
                            }
                        }
                    }
                }

                // ============================================================
                // PHASE 3: Compute IsTrackLocked
                // ============================================================
                var selected = myJet.GetSelectedEnemy();
                if (selected.HasValue)
                {
                    for (int i = 0; i < poolSize; i++)
                    {
                        var state = radarStates[i];
                        if (state.Role != RadarRole.LOCKED) continue;

                        if ((selected.Value.EntityId != 0 && selected.Value.EntityId == state.TrackedEntityId) ||
                            (!string.IsNullOrEmpty(selected.Value.Name) && selected.Value.Name == state.TrackedName))
                        {
                            IsTrackLocked = true;
                            break;
                        }
                    }
                }

            SkipPool:
                // ============================================================
                // PHASE 4: Process RWR pool
                // ============================================================
                if (rwrEnabled && rwrStates.Count > 0)
                {
                    activeThreats.Clear();
                    anyThreatDetected = false;

                    Vector3D playerPos = myJet._cockpit.GetPosition();
                    Vector3D playerVel = myJet._cockpit.GetShipVelocities().LinearVelocity;
                    Vector3D gravity = myJet.CachedGravity;

                    int rwrCount = GetActiveRWRCount();
                    for (int i = 0; i < rwrCount; i++)
                    {
                        ProcessRWR(i, playerPos, playerVel, gravity);
                    }

                    ManageWarningSounds();
                    UpdateConsoleOutput();
                }

                // ============================================================
                // PHASE 5: Decay old contacts
                // ============================================================
                myJet.UpdateEnemyDecay();

            }

            // ============================================================
            // Sequential chain: Process a SEARCHING radar
            // ============================================================
            private void ProcessSearchingRadar(int index, int poolSize)
            {
                var radar = allRadars[index];
                var state = radarStates[index];

                if (!radar.IsTracking || !radar.HasReceivedPosition)
                    return; // Still scanning, nothing found yet

                Vector3D targetPos = radar.TargetPosition;
                if (targetPos.LengthSquared() < 1.0)
                    return; // Stale/zero position

                long entityId = radar.TrackedEntityId;
                string targetName = radar.TrackedObjectName;

                // Check if this target is already LOCKED by another radar
                bool alreadyLocked = IsEntityLockedByAnother(entityId, targetName, index, poolSize);

                // Always feed enemy list — even for already-locked targets,
                // the SEARCHING radar is a valid data source
                string feedName = !string.IsNullOrEmpty(targetName) ? targetName : "";
                myJet.UpdateOrAddEnemy(targetPos, radar.TargetVelocity, feedName, index, entityId);

                if (!alreadyLocked)
                {
                    // NEW target found! Lock onto it
                    state.Role = RadarRole.LOCKED;
                    state.TrackedEntityId = entityId;
                    state.TrackedName = feedName;
                    state.TicksSinceLastSeen = 0;

                    // Activate next IDLE radar as SEARCHING
                    ActivateNextSearcher(index, poolSize);
                }
                // If already locked by another, stay SEARCHING — SE will naturally
                // cycle to a different target via UpdateTargetInterval
            }

            // ============================================================
            // Sequential chain: Process a LOCKED radar
            // ============================================================
            private void ProcessLockedRadar(int index, int poolSize)
            {
                var radar = allRadars[index];
                var state = radarStates[index];

                if (radar.IsTracking && radar.HasReceivedPosition)
                {
                    Vector3D targetPos = radar.TargetPosition;
                    if (targetPos.LengthSquared() < 1.0)
                    {
                        // Position is zero/stale
                        state.TicksSinceLastSeen++;
                        if (state.TicksSinceLastSeen > LOST_TARGET_TIMEOUT)
                        {
                            DemoteToIdle(index);
                        }
                        return;
                    }

                    long entityId = radar.TrackedEntityId;
                    string targetName = radar.TrackedObjectName;
                    string feedName = !string.IsNullOrEmpty(targetName) ? targetName : state.TrackedName;

                    if (entityId == state.TrackedEntityId)
                    {
                        // Same target — feed and reset
                        myJet.UpdateOrAddEnemy(targetPos, radar.TargetVelocity, feedName, index, entityId);
                        state.TicksSinceLastSeen = 0;
                        if (!string.IsNullOrEmpty(targetName))
                            state.TrackedName = targetName;
                    }
                    else
                    {
                        // SE switched to a different target
                        string newTargetName = !string.IsNullOrEmpty(targetName) ? targetName : "";

                        if (!IsEntityLockedByAnother(entityId, newTargetName, index, poolSize))
                        {
                            // New target is NOT locked by anyone else — adopt it
                            myJet.UpdateOrAddEnemy(targetPos, radar.TargetVelocity, newTargetName, index, entityId);
                            state.TrackedEntityId = entityId;
                            state.TrackedName = newTargetName;
                            state.TicksSinceLastSeen = 0;
                        }
                        else
                        {
                            // Already locked by another — stay LOCKED, feed data, wait for SE to cycle back
                            myJet.UpdateOrAddEnemy(targetPos, radar.TargetVelocity, newTargetName, index, entityId);
                            state.TicksSinceLastSeen++;
                            if (state.TicksSinceLastSeen > LOST_TARGET_TIMEOUT)
                            {
                                DemoteToIdle(index);
                            }
                        }
                    }
                }
                else
                {
                    // Lost tracking
                    state.TicksSinceLastSeen++;
                    if (state.TicksSinceLastSeen > LOST_TARGET_TIMEOUT)
                    {
                        DemoteToIdle(index);
                    }
                }
            }

            // ============================================================
            // Activate a radar — SE ENGINE REQUIREMENT:
            // The flight+combat block pair MUST be configured in this exact
            // sequence, in a single tick. Splitting properties and behavior
            // activation across ticks causes SE to disable the behavior.
            // This matches the proven pattern from Rdav's Guided Missile Script.
            // DO NOT reorder, split, or "optimize" this sequence.
            //
            // Order: Flight properties → flight ActivateBehavior_On →
            //        Combat properties → combat ActivateBehavior_On →
            //        SetTargetingGroup → SetTargetPriority
            // ============================================================
            private void ActivateRadar(int index, string priorityAction)
            {
                var radar = allRadars[index];
                var state = radarStates[index];

                if (!state.BehaviorActivated)
                {
                    // Flight block: keep disabled, do NOT activate behavior.
                    // The combat block pushes waypoints to the flight block's internal
                    // list via AiBlockSystem events regardless of flight block activation.
                    // NOT activating prevents the autopilot's stuck detection from clearing
                    // waypoints (the ship isn't flying, so stuck detection would fire).
                    radar.L_FlightBlock.Enabled = false;
                    radar.L_FlightBlock.CollisionAvoidance = false;

                    // Combat block: activate behavior — this is the only block that needs it
                    radar.L_CombatBLock.Enabled = true;
                    radar.L_CombatBLock.UpdateTargetInterval = 5; // SE clamps to [5,60]
                    radar.L_CombatBLock.SearchEnemyComponent.TargetingLockOptions = VRage.Game.ModAPI.Ingame.MyGridTargetingRelationFiltering.Enemy;
                    radar.L_CombatBLock.SelectedAttackPattern = 3;
                    radar.L_CombatBLock.SetValue<long>("OffensiveCombatIntercept_GuidanceType", 0);
                    radar.L_CombatBLock.SetValueBool("OffensiveCombatIntercept_OverrideCollisionAvoidance", true);
                    radar.L_CombatBLock.ApplyAction("ActivateBehavior_On");
                    radar.L_CombatBLock.ApplyAction("SetTargetingGroup_Weapons");

                    state.BehaviorActivated = true;
                    state.ActivationCooldown = ACTIVATION_COOLDOWN;
                }

                // Always apply priority — safe anytime, doesn't toggle behavior
                radar.L_CombatBLock.ApplyAction(priorityAction);
            }

            // ============================================================
            // Start a radar searching
            // ============================================================
            private void StartSearching(int index)
            {
                var state = radarStates[index];
                state.Role = RadarRole.SEARCHING;
                state.TrackedEntityId = 0;
                state.TrackedName = "";
                state.TicksSinceLastSeen = 0;

                // Rotate priority so each searcher looks for different targets
                string priority = TargetPriorityActions[nextPriorityIndex % TargetPriorityActions.Length];
                nextPriorityIndex++;

                ActivateRadar(index, priority);
            }

            // ============================================================
            // Demote a LOCKED radar back to IDLE (behavior stays on, it just
            // won't be processed until re-activated as SEARCHING)
            // ============================================================
            private void DemoteToIdle(int index)
            {
                var state = radarStates[index];
                state.Role = RadarRole.IDLE;
                state.TrackedEntityId = 0;
                state.TrackedName = "";
                state.TicksSinceLastSeen = 0;
            }

            // ============================================================
            // Find and activate the next IDLE radar as SEARCHING
            // ============================================================
            private void ActivateNextSearcher(int afterIndex, int poolSize)
            {
                // Search from afterIndex+1 wrapping around, find first IDLE
                for (int offset = 1; offset < poolSize; offset++)
                {
                    int candidate = (afterIndex + offset) % poolSize;
                    if (radarStates[candidate].Role == RadarRole.IDLE)
                    {
                        StartSearching(candidate);
                        return;
                    }
                }
                // No IDLE radars left — all are LOCKED. That's fine.
            }

            // ============================================================
            // Check if an entity is already LOCKED by another pool radar
            // ============================================================
            private bool IsEntityLockedByAnother(long entityId, string name, int excludeIndex, int poolSize)
            {
                for (int i = 0; i < poolSize; i++)
                {
                    if (i == excludeIndex) continue;
                    if (radarStates[i].Role != RadarRole.LOCKED) continue;

                    if (entityId != 0 && radarStates[i].TrackedEntityId == entityId)
                        return true;
                    if (!string.IsNullOrEmpty(name) && radarStates[i].TrackedName == name)
                        return true;
                }
                return false;
            }

            public override void HandleSpecialFunction(int key)
            {
                // No special functions
            }

            public override string GetHotkeys()
            {
                return "Radar Control is a status display";
            }

            // Get total count of available radars
            public int GetRadarCount()
            {
                return allRadars.Count;
            }

            // ==== Pool / RWR Size Helpers ====

            private int GetSweepTrackPoolSize()
            {
                return Mx(0, allRadars.Count - configuredRWRCount);
            }

            private void ReassignRoles()
            {
                int poolSize = GetSweepTrackPoolSize();
                for (int i = 0; i < allRadars.Count; i++)
                {
                    if (i < poolSize)
                    {
                        // Keep LOCKED if already locked, otherwise set to IDLE
                        if (radarStates[i].Role != RadarRole.LOCKED)
                        {
                            radarStates[i].Role = RadarRole.IDLE;
                            radarStates[i].TrackedEntityId = 0;
                            radarStates[i].TrackedName = "";
                            radarStates[i].TicksSinceLastSeen = 0;
                        }
                    }
                    else
                    {
                        // RWR — clear any tracking state
                        radarStates[i].Role = RadarRole.RWR;
                        radarStates[i].TrackedEntityId = 0;
                        radarStates[i].TrackedName = "";
                        radarStates[i].TicksSinceLastSeen = 0;
                    }
                }
                // Reset chain — will re-pick a SEARCHING radar next tick
                poolChainStarted = false;
            }

            // ==== RWR Helper Methods ====

            private int GetActiveRWRCount()
            {
                if (configuredRWRCount == 0 && allRadars.Count <= 2)
                {
                    return allRadars.Count;
                }
                return Mn(configuredRWRCount, allRadars.Count);
            }

            private int GetRWRRadarIndex(int rwrIndex)
            {
                int poolSize = GetSweepTrackPoolSize();
                if (configuredRWRCount == 0 && allRadars.Count <= 2)
                    return rwrIndex;
                return poolSize + rwrIndex;
            }

            private void ProcessRWR(int rwrIndex, Vector3D playerPos, Vector3D playerVel, Vector3D gravity)
            {
                int radarIndex = GetRWRRadarIndex(rwrIndex);

                if (radarIndex >= allRadars.Count || rwrIndex >= rwrStates.Count)
                    return;

                var radar = allRadars[radarIndex];
                var state = rwrStates[rwrIndex];

                state.TickCounter++;

                if (radar.IsTracking && radar.HasReceivedPosition)
                {
                    string enemyName = radar.TrackedObjectName;
                    Vector3D enemyPos = radar.TargetPosition;
                    Vector3D enemyVel = radar.TargetVelocity;

                    if (enemyPos.LengthSquared() < 1.0)
                    {
                        if (state.CurrentEnemyName != "")
                        {
                            state.CurrentEnemyName = "";
                            state.TicksSinceEnemyChange = 0;
                            state.ClearHistory();
                        }
                        return;
                    }

                    if (enemyName != state.CurrentEnemyName)
                    {
                        state.CurrentEnemyName = enemyName;
                        state.TicksSinceEnemyChange = 0;
                        state.ClearHistory();
                    }
                    else
                    {
                        state.TicksSinceEnemyChange++;
                    }

                    if (state.TickCounter % 10 == 0)
                    {
                        state.PositionHistory[state.HistoryIndex] = enemyPos;
                        state.HistoryIndex = (state.HistoryIndex + 1) % state.PositionHistory.Count;
                    }

                    if (state.TicksSinceEnemyChange >= 30)
                    {
                        bool isThreatening = IsThreatening(enemyPos, enemyVel, playerPos, playerVel, gravity, state.PositionHistory);

                        if (isThreatening)
                        {
                            activeThreats.Add(new RWRWarning(enemyPos, enemyVel, enemyName, true, rwrIndex));
                            anyThreatDetected = true;
                        }
                        else
                        {
                            activeThreats.Add(new RWRWarning(enemyPos, enemyVel, enemyName, false, rwrIndex));
                        }
                    }
                }
                else
                {
                    if (state.CurrentEnemyName != "")
                    {
                        state.CurrentEnemyName = "";
                        state.TicksSinceEnemyChange = 0;
                        state.ClearHistory();
                    }
                }
            }

            private bool IsThreatening(Vector3D enemyPos, Vector3D enemyVel, Vector3D playerPos, Vector3D playerVel, Vector3D gravity, List<Vector3D> positionHistory)
            {
                Vector3D relativePos = playerPos - enemyPos;
                Vector3D relativeVel = playerVel - enemyVel;

                double range = relativePos.Length();
                if (range < 1.0)
                    return false;

                double relativeSpeed = relativeVel.Length();
                double enemySpeed = enemyVel.Length();

                if (relativeSpeed < 1.0)
                {
                    if (enemySpeed < 0.5) return false;
                    double aspectAngleDeg = NavigationHelper.GetAspectAngleDeg(enemyVel, relativePos);
                    return aspectAngleDeg < 30.0;
                }

                Vector3D losDirection = VN(relativePos);
                double closingVelocity = -VD(relativeVel, losDirection);

                if (closingVelocity <= 0) return false;

                double timeToClosestApproach = range / closingVelocity;
                if (timeToClosestApproach > 300.0) return false;

                Vector3D ourFuturePos = playerPos + playerVel * timeToClosestApproach;
                Vector3D enemyFuturePos = enemyPos + enemyVel * timeToClosestApproach;
                double closestApproachDistance = VDi(ourFuturePos, enemyFuturePos);

                if (closestApproachDistance > 500.0) return false;

                double aspectAngleDeg2 = NavigationHelper.GetAspectAngleDeg(enemyVel, relativePos);
                if (aspectAngleDeg2 > 90.0) return false;

                return true;
            }

            private void ManageWarningSounds()
            {
                if (anyThreatDetected)
                {
                    SoundManager.RequestWarning("Alert 2", SoundManager.PRIORITY_RWR, 60);
                }
            }

            private void UpdateConsoleOutput()
            {
                var sb = new StringBuilder();
                sb.Append("RWR: ");

                int activeCount = GetActiveRWRCount();
                for (int i = 0; i < activeCount; i++)
                {
                    if (i > 0) sb.Append(" ");

                    sb.Append("R").Append(i + 1).Append(":");

                    int radarIndex = GetRWRRadarIndex(i);
                    if (radarIndex < allRadars.Count && allRadars[radarIndex].IsTracking)
                    {
                        sb.Append("A,T");

                        bool isThreat = false;
                        bool isIncoming = false;
                        foreach (var threat in activeThreats)
                        {
                            if (threat.RWRIndex == i)
                            {
                                isThreat = true;
                                if (threat.IsIncoming)
                                    isIncoming = true;
                                break;
                            }
                        }

                        if (isIncoming)
                            sb.Append(",H+");
                        else if (isThreat)
                            sb.Append(",H");
                        else
                            sb.Append(",-");
                    }
                    else
                    {
                        sb.Append("A,-,-");
                    }
                }

                string newOutput = sb.ToString();
                if (newOutput != lastConsoleOutput)
                {
                    ParentProgram.Echo(newOutput);
                    lastConsoleOutput = newOutput;
                }
            }
        }
    }
}
