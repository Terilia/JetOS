using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
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
            private bool pluginFeedAvailable;
            private string pluginFeedRaw = "";

            // ==== Sequential Activation State Machine ====
            // Pool radars activate one at a time in a chain:
            //   IDLE → SEARCHING → LOCKED
            // Only 1 radar is SEARCHING at any time. When it finds a new target
            // (not already locked by another), it transitions to LOCKED and the
            // next IDLE radar becomes SEARCHING. Duplicate sightings yield so
            // one sticky SE target cannot block the chain.
            private enum RadarRole { IDLE, SEARCHING, LOCKED, RWR }

            private class RadarState
            {
                public RadarRole Role;
                public long TrackedEntityId;
                public string TrackedName;
                public double SecondsSinceLastSeen;
                // True once we've called ActivateBehavior_On for this radar at runtime
                public bool BehaviorActivated;
                // Wall-clock cooldown after activation before we start reading data
                public double ActivationCooldown;
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
                public long CurrentEnemyEntityId = 0;
                public double SecondsSinceEnemyChange = 0;
            }

            private List<RWRTrackingState> rwrStates = new List<RWRTrackingState>();
            private bool rwrEnabled = true;
            private int configuredRWRCount = 0;
            private int activeRwrTrackCount = 0;
            private int activeRwrThreatCount = 0;

            // True when ANY pool radar in LOCKED state matches the selected enemy
            public bool IsTrackLocked { get; private set; }

            // Accumulated absolute time for radar tracking (in ticks)
            private long accumulatedTimeTicks = 0;

            // Sequential init: activate RWR radars one per tick first, then start the chain
            private int initRWRIndex = 0;
            private bool rwrInitComplete = false;
            // Once RWR init is done, we activate the first pool radar as SEARCHING
            private bool poolChainStarted = false;

            // Activation cooldown: after calling ActivateBehavior_On, wait this long
            // (wall-clock) before reading data (SE needs time to process the action)
            private const double ACTIVATION_COOLDOWN_SECONDS = 0.167;
            // Wall-clock seconds before a LOCKED radar that lost its target reverts to IDLE
            private const double LOST_TARGET_TIMEOUT_SECONDS = 2.0;
            // RWR stabilization delay before threat classification fires
            private const double RWR_STABILIZATION_SECONDS = 0.5;

            public RadarControlModule(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                name = "Radar";

                pluginFeedAvailable = ParentProgram.Me.GetProperty("JetOSRadarFeed") != null;

                // Auto-detect all AI Flight/Combat pairs (1-99), allowing names tagged with [JO].
                for (int i = 1; i <= 99; i++)
                {
                    string flightName = "AI Flight" + (i == 1 ? "" : " " + i);
                    string combatName = "AI Combat" + (i == 1 ? "" : " " + i);

                    var flightBlock = GetRadarBlock<IMyFlightMovementBlock>(flightName);
                    var combatBlock = GetRadarBlock<IMyOffensiveCombatBlock>(combatName);

                    if (flightBlock != null && combatBlock != null)
                    {
                        var radar = new RadarTrackingModule(flightBlock, combatBlock);
                        allRadars.Add(radar);
                        radarStates.Add(new RadarState());
                        rwrStates.Add(new RWRTrackingState());
                    }
                }

                // Load RWR config from CustomData
                int maxRWR = Mx(0, allRadars.Count - 1);
                string savedCount = SystemManager.GetCustomDataValue(CD_RWR_COUNT);
                int count;
                if (!SE(savedCount) && int.TryParse(savedCount, out count))
                {
                    configuredRWRCount = Mx(0, Mn(count, maxRWR));
                }
                else
                {
                    configuredRWRCount = allRadars.Count >= 2 ? 1 : 0;
                }

                // Assign initial roles — NO ApplyAction here (unreliable in constructor)
                ReassignRoles();

            }

            public override string[] GetOptions()
            {
                var options = new List<string>();

                if (allRadars.Count == 0)
                {
                    options.Add("NO RAD");
                    return options.ToArray();
                }

                // RWR Controls
                options.Add($"RWR [{(rwrEnabled ? "ON" : "OFF")}]");
                int activeRWR = GetActiveRWRCount();
                int poolSize = GetSweepTrackPoolSize();
                options.Add($"RWR+ {activeRWR}/{allRadars.Count}");
                options.Add($"RWR- {activeRWR}/{allRadars.Count}");

                // RWR Status
                if (rwrEnabled)
                {
                    int threatCount = activeRwrThreatCount;
                    if (threatCount > 0)
                    {
                        options.Add($"{threatCount} THR");
                    }
                    else if (activeRwrTrackCount > 0)
                    {
                        options.Add($"{activeRwrTrackCount} RWR");
                    }
                    else
                    {
                        options.Add("RWR SCAN");
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
                            roleStr = $"R{i + 1}: SRCH";
                            break;
                        case RadarRole.LOCKED:
                            roleStr = $"R{i + 1}: LOCK [{state.TrackedName}]";
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

                options.Add($"P {poolSize} R {activeRWR}");
                options.Add($"TGT {myJet.enemyList.Count}");

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
                                state.CurrentEnemyName = "";
                                state.CurrentEnemyEntityId = 0;
                                state.SecondsSinceEnemyChange = 0;
                            }
                            activeRwrTrackCount = 0;
                            activeRwrThreatCount = 0;
                        }
                        break;

                    case 1: // Increase RWR count
                        if (configuredRWRCount < allRadars.Count - 1)
                        {
                            configuredRWRCount++;
                            SystemManager.SetCustomDataValue(CD_RWR_COUNT, configuredRWRCount.ToString());
                            ReassignRoles();
                        }
                        break;

                    case 2: // Decrease RWR count
                        if (configuredRWRCount > 0)
                        {
                            configuredRWRCount--;
                            SystemManager.SetCustomDataValue(CD_RWR_COUNT, configuredRWRCount.ToString());
                            ReassignRoles();
                        }
                        break;
                }
            }

            public override void Tick()
            {
                if (allRadars.Count == 0)
                {
                    ProcessPluginFeed();
                    myJet.UpdateEnemyDecay();
                    return;
                }

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
                            ActivateRadar(radarIndex, TargetPriorityActions[0]);
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

                    // Decrement activation cooldown (wall-clock)
                    if (state.ActivationCooldown > 0)
                    {
                        state.ActivationCooldown -= SystemManager.DeltaSeconds;
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
                            (!SE(selected.Value.Name) && selected.Value.Name == state.TrackedName))
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
                    activeRwrTrackCount = 0;
                    activeRwrThreatCount = 0;

                    Vector3D playerPos = GP(myJet._cockpit);
                    Vector3D playerVel = LV(myJet._cockpit);

                    int rwrCount = GetActiveRWRCount();
                    for (int i = 0; i < rwrCount; i++)
                    {
                        ProcessRWR(i, playerPos, playerVel);
                    }

                    ManageWarningSounds();
                }

                // ============================================================
                // PHASE 5: Decay old contacts
                // ============================================================
                ProcessPluginFeed();
                myJet.UpdateEnemyDecay();

            }

            private T GetRadarBlock<T>(string targetName) where T : class, IMyTerminalBlock
            {
                var b = ParentProgram.GridTerminalSystem.GetBlockWithName(targetName + " [JO]") as T;
                if (b == null || (myJet._cockpit != null && !b.IsSameConstructAs(myJet._cockpit)))
                    b = ParentProgram.GridTerminalSystem.GetBlockWithName(targetName) as T;
                return b != null && (myJet._cockpit == null || b.IsSameConstructAs(myJet._cockpit)) ? b : null;
            }

            private void ProcessPluginFeed()
            {
                if (!pluginFeedAvailable) return;

                StringBuilder sb = ParentProgram.Me.GetValue<StringBuilder>("JetOSRadarFeed");
                if (sb == null) return;
                string raw = sb.ToString();
                if (SE(raw) || raw == pluginFeedRaw || !raw.StartsWith("JORAD|2|")) return;

                pluginFeedRaw = raw;

                string[] lines = raw.Split('\n');
                int feedContactCount = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] p = lines[i].Trim().Split('|');
                    if (p.Length < 9 || p[0] != "R") continue;

                    long targetId;
                    double px, py, pz, vx, vy, vz;
                    long.TryParse(p[1], out targetId);
                    if (!TryParseFeedDouble(p[2], out px) || !TryParseFeedDouble(p[3], out py) || !TryParseFeedDouble(p[4], out pz)) continue;
                    if (!TryParseFeedDouble(p[5], out vx) || !TryParseFeedDouble(p[6], out vy) || !TryParseFeedDouble(p[7], out vz)) continue;

                    Vector3D pos = new Vector3D(px, py, pz);
                    if (pos.LengthSquared() < 1.0) continue;

                    myJet.UpdateOrAddEnemy(pos, new Vector3D(vx, vy, vz), p[8], 100 + feedContactCount++, targetId);
                }
            }

            private static bool TryParseFeedDouble(string s, out double value)
            {
                return double.TryParse(s, out value);
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

                // Always feed enemy list — even for already-locked targets,
                // the SEARCHING radar is a valid data source
                targetName = !SE(targetName) ? targetName : "";
                myJet.UpdateOrAddEnemy(targetPos, radar.TargetVelocity, targetName, index, entityId);

                if (IsEntityLockedByAnother(entityId, targetName, index, poolSize))
                {
                    DemoteToIdle(index);
                }
                else
                {
                    // NEW target found! Lock onto it
                    state.Role = RadarRole.LOCKED;
                    state.TrackedEntityId = entityId;
                    state.TrackedName = targetName;
                    state.SecondsSinceLastSeen = 0;
                }

                // Activate next IDLE radar as SEARCHING
                ActivateNextSearcher(index, poolSize);
            }

            // ============================================================
            // Sequential chain: Process a LOCKED radar
            // ============================================================
            private void ProcessLockedRadar(int index, int poolSize)
            {
                var radar = allRadars[index];
                var state = radarStates[index];

                double dt = SystemManager.DeltaSeconds;
                if (radar.IsTracking && radar.HasReceivedPosition)
                {
                    Vector3D targetPos = radar.TargetPosition;
                    if (targetPos.LengthSquared() < 1.0)
                    {
                        // Position is zero/stale
                        state.SecondsSinceLastSeen += dt;
                        if (state.SecondsSinceLastSeen > LOST_TARGET_TIMEOUT_SECONDS)
                        {
                            DemoteToIdle(index);
                        }
                        return;
                    }

                    long entityId = radar.TrackedEntityId;
                    string targetName = radar.TrackedObjectName;
                    string feedName = !SE(targetName) ? targetName : state.TrackedName;

                    if (entityId == state.TrackedEntityId)
                    {
                        // Same target — feed and reset
                        myJet.UpdateOrAddEnemy(targetPos, radar.TargetVelocity, feedName, index, entityId);
                        state.SecondsSinceLastSeen = 0;
                        if (!SE(targetName))
                            state.TrackedName = targetName;
                    }
                    else
                    {
                        // SE switched to a different target
                        string newTargetName = !SE(targetName) ? targetName : "";

                        if (!IsEntityLockedByAnother(entityId, newTargetName, index, poolSize))
                        {
                            // New target is NOT locked by anyone else — adopt it
                            myJet.UpdateOrAddEnemy(targetPos, radar.TargetVelocity, newTargetName, index, entityId);
                            state.TrackedEntityId = entityId;
                            state.TrackedName = newTargetName;
                            state.SecondsSinceLastSeen = 0;
                        }
                        else
                        {
                            // Already locked by another — stay LOCKED, feed data, wait for SE to cycle back
                            myJet.UpdateOrAddEnemy(targetPos, radar.TargetVelocity, newTargetName, index, entityId);
                            state.SecondsSinceLastSeen += dt;
                            if (state.SecondsSinceLastSeen > LOST_TARGET_TIMEOUT_SECONDS)
                            {
                                DemoteToIdle(index);
                            }
                        }
                    }
                }
                else
                {
                    // Lost tracking
                    state.SecondsSinceLastSeen += dt;
                    if (state.SecondsSinceLastSeen > LOST_TARGET_TIMEOUT_SECONDS)
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
                    state.ActivationCooldown = ACTIVATION_COOLDOWN_SECONDS;
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
                state.SecondsSinceLastSeen = 0;

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
                state.SecondsSinceLastSeen = 0;
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
                    if (!SE(name) && radarStates[i].TrackedName == name)
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
                return "";
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
                            radarStates[i].SecondsSinceLastSeen = 0;
                        }
                    }
                    else
                    {
                        // RWR — clear any tracking state
                        radarStates[i].Role = RadarRole.RWR;
                        radarStates[i].TrackedEntityId = 0;
                        radarStates[i].TrackedName = "";
                        radarStates[i].SecondsSinceLastSeen = 0;
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

            private void ProcessRWR(int rwrIndex, Vector3D playerPos, Vector3D playerVel)
            {
                int radarIndex = GetRWRRadarIndex(rwrIndex);

                if (radarIndex >= allRadars.Count || rwrIndex >= rwrStates.Count)
                    return;

                var radar = allRadars[radarIndex];
                var state = rwrStates[rwrIndex];

                double dt = SystemManager.DeltaSeconds;

                if (radar.IsTracking && radar.HasReceivedPosition)
                {
                    string enemyName = radar.TrackedObjectName;
                    long enemyId = radar.TrackedEntityId;
                    Vector3D enemyPos = radar.TargetPosition;
                    Vector3D enemyVel = radar.TargetVelocity;

                    if (enemyPos.LengthSquared() < 1.0)
                    {
                        if (state.CurrentEnemyName != "" || state.CurrentEnemyEntityId != 0)
                        {
                            state.CurrentEnemyName = "";
                            state.CurrentEnemyEntityId = 0;
                            state.SecondsSinceEnemyChange = 0;
                        }
                        return;
                    }

                    string feedName = !SE(enemyName) ? enemyName : "";
                    myJet.UpdateOrAddEnemy(enemyPos, enemyVel, feedName, radarIndex, enemyId);
                    activeRwrTrackCount++;

                    bool enemyChanged = enemyId != 0
                        ? enemyId != state.CurrentEnemyEntityId
                        : enemyName != state.CurrentEnemyName;

                    if (enemyChanged)
                    {
                        state.CurrentEnemyName = enemyName;
                        state.CurrentEnemyEntityId = enemyId;
                        state.SecondsSinceEnemyChange = 0;
                    }
                    else
                    {
                        state.SecondsSinceEnemyChange += dt;
                    }

                    if (state.SecondsSinceEnemyChange >= RWR_STABILIZATION_SECONDS)
                    {
                        bool isThreatening = IsThreatening(enemyPos, enemyVel, playerPos, playerVel);

                        if (isThreatening)
                        {
                            activeRwrThreatCount++;
                        }
                    }
                }
                else
                {
                    if (state.CurrentEnemyName != "" || state.CurrentEnemyEntityId != 0)
                    {
                        state.CurrentEnemyName = "";
                        state.CurrentEnemyEntityId = 0;
                        state.SecondsSinceEnemyChange = 0;
                    }
                }
            }

            private bool IsThreatening(Vector3D enemyPos, Vector3D enemyVel, Vector3D playerPos, Vector3D playerVel)
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
                if (activeRwrThreatCount > 0)
                {
                    SoundManager.Event(SoundManager.RWR_LOCK);
                }
            }

        }
    }
}
