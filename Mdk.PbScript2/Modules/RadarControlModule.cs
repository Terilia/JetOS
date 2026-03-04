using Sandbox.ModAPI.Ingame;
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
                        PositionHistory.Add(Vector3D.Zero);
                    }
                }

                public void ClearHistory()
                {
                    for (int i = 0; i < PositionHistory.Count; i++)
                    {
                        PositionHistory[i] = Vector3D.Zero;
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

            // Track radar correlation: true when radar 1 (track) is following the selected enemy
            public bool IsTrackLocked { get; private set; }

            private string lastConsoleOutput = "";

            // Accumulated absolute time for radar tracking (in ticks)
            private long accumulatedTimeTicks = 0;

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
                        allRadars.Add(new RadarTrackingModule(flightBlock, combatBlock));
                        rwrStates.Add(new RWRTrackingState());
                    }
                }

                // Load RWR config from CustomData (using centralized cache)
                string savedCount = SystemManager.GetCustomDataValue("RWRCount");
                int count;
                if (!string.IsNullOrEmpty(savedCount) && int.TryParse(savedCount, out count))
                {
                    configuredRWRCount = Math.Max(0, Math.Min(count, allRadars.Count));
                }
                else
                {
                    configuredRWRCount = allRadars.Count; // Default: use all
                }

                program.Echo($"RadarControl: {allRadars.Count} radars, RWR: {GetActiveRWRCount()}");
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

                // Summary info
                int tracking = 0;
                for (int i = 0; i < allRadars.Count; i++)
                {
                    if (allRadars[i].IsTracking)
                        tracking++;
                }

                options.Add($"Radars Active: {tracking}/{allRadars.Count}");
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
                        if (configuredRWRCount < allRadars.Count)
                        {
                            configuredRWRCount++;
                            SystemManager.SetCustomDataValue("RWRCount", configuredRWRCount.ToString());
                        }
                        break;

                    case 2: // Decrease RWR count
                        if (configuredRWRCount > 0)
                        {
                            configuredRWRCount--;
                            SystemManager.SetCustomDataValue("RWRCount", configuredRWRCount.ToString());
                        }
                        break;
                }
            }

            public override void Tick()
            {
                // Accumulate absolute time for radar tracking
                accumulatedTimeTicks += ParentProgram.Runtime.TimeSinceLastRun.Ticks;

                IsTrackLocked = false;

                // Determine how many radars serve scan/track roles
                int scanTrackCount = Math.Min(2, allRadars.Count); // 0=scan, 1=track

                // --- Scan & Track radars (indices 0 and 1) ---
                for (int i = 0; i < scanTrackCount; i++)
                {
                    var radar = allRadars[i];
                    if (radar == null) continue;

                    radar.UpdateTracking(accumulatedTimeTicks);

                    if (radar.IsTracking && radar.HasReceivedPosition)
                    {
                        Vector3D targetPos = radar.TargetPosition;
                        Vector3D targetVel = radar.TargetVelocity;
                        string targetName = radar.TrackedObjectName;

                        // Guard: skip zero/origin positions (stale default data)
                        if (targetPos.LengthSquared() < 1.0)
                            continue;

                        // Both scan (0) and track (1) feed enemyList
                        myJet.UpdateOrAddEnemy(targetPos, targetVel, targetName, i);

                        // Radar 1 = track: correlate with selected enemy
                        if (i == 1)
                        {
                            var selected = myJet.GetSelectedEnemy();
                            if (selected.HasValue)
                            {
                                // Match by EntityId or Name
                                if ((selected.Value.EntityId != 0 && selected.Value.EntityId == radar.TrackedEntityId) ||
                                    (!string.IsNullOrEmpty(selected.Value.Name) && selected.Value.Name == targetName))
                                {
                                    IsTrackLocked = true;
                                }
                            }
                        }
                    }
                }

                // --- RWR radars (indices 2+) — update tracking but don't feed enemyList ---
                for (int i = scanTrackCount; i < allRadars.Count; i++)
                {
                    var radar = allRadars[i];
                    if (radar != null)
                    {
                        radar.UpdateTracking(accumulatedTimeTicks);
                    }
                }

                // If only 1 radar, it does implicit track too
                if (allRadars.Count == 1 && allRadars[0] != null && allRadars[0].IsTracking && allRadars[0].HasReceivedPosition)
                {
                    var selected = myJet.GetSelectedEnemy();
                    if (selected.HasValue)
                    {
                        string trackName = allRadars[0].TrackedObjectName;
                        if ((selected.Value.EntityId != 0 && selected.Value.EntityId == allRadars[0].TrackedEntityId) ||
                            (!string.IsNullOrEmpty(selected.Value.Name) && selected.Value.Name == trackName))
                        {
                            IsTrackLocked = true;
                        }
                    }
                }

                // Decay old contacts
                myJet.UpdateEnemyDecay();

                // RWR Threat Detection
                if (rwrEnabled && rwrStates.Count > 0)
                {
                    activeThreats.Clear();
                    anyThreatDetected = false;

                    Vector3D playerPos = myJet._cockpit.GetPosition();
                    Vector3D playerVel = myJet._cockpit.GetShipVelocities().LinearVelocity;
                    Vector3D gravity = myJet._cockpit.GetNaturalGravity();

                    int rwrCount = GetActiveRWRCount();
                    for (int i = 0; i < rwrCount; i++)
                    {
                        ProcessRWR(i, playerPos, playerVel, gravity);
                    }

                    // Manage warning sounds
                    ManageWarningSounds();

                    // Update console output (only when state changes)
                    UpdateConsoleOutput();
                }
            }

            public override void HandleSpecialFunction(int key)
            {
                // No special functions
            }

            public override string GetHotkeys()
            {
                return "Radar Control is a status display";
            }

            // Public API for modules to request radars
            public List<RadarTrackingModule> RequestRadars(int count)
            {
                var result = new List<RadarTrackingModule>();

                int available = Math.Min(count, allRadars.Count);
                for (int i = 0; i < available; i++)
                {
                    result.Add(allRadars[i]);
                }

                return result;
            }

            // Get total count of available radars
            public int GetRadarCount()
            {
                return allRadars.Count;
            }

            // ==== RWR Helper Methods ====
            // Use centralized SystemManager for CustomData access

            private int GetActiveRWRCount()
            {
                // RWR radars are index 2+ (after scan and track)
                int rwrRadarCount = Math.Max(0, allRadars.Count - 2);
                if (rwrRadarCount == 0)
                {
                    // If fewer than 3 radars, piggyback RWR on all radars
                    rwrRadarCount = allRadars.Count;
                }
                if (configuredRWRCount == 0)
                    return rwrRadarCount;
                return Math.Min(configuredRWRCount, rwrRadarCount);
            }

            private void ProcessRWR(int rwrIndex, Vector3D playerPos, Vector3D playerVel, Vector3D gravity)
            {
                // Map RWR index to actual radar index
                // If 3+ radars: RWR uses index 2+ (offset by scan+track)
                // If fewer than 3: RWR piggybacks on all radars (no offset)
                int radarIndex = allRadars.Count >= 3 ? rwrIndex + 2 : rwrIndex;

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

                    // Skip zero/origin positions (stale default data)
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
                    Vector3D enemyDirection = enemyVel;
                    if (enemyDirection.LengthSquared() > 0)
                        enemyDirection = Vector3D.Normalize(enemyDirection);
                    Vector3D toPlayer = Vector3D.Normalize(relativePos);
                    double aspectAngle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(enemyDirection, toPlayer), -1.0, 1.0));
                    double aspectAngleDeg = MathHelper.ToDegrees(aspectAngle);
                    return aspectAngleDeg < 30.0;
                }

                Vector3D losDirection = Vector3D.Normalize(relativePos);
                double closingVelocity = -Vector3D.Dot(relativeVel, losDirection);

                if (closingVelocity <= 0) return false;

                double timeToClosestApproach = range / closingVelocity;
                if (timeToClosestApproach > 300.0) return false;

                Vector3D ourFuturePos = playerPos + playerVel * timeToClosestApproach;
                Vector3D enemyFuturePos = enemyPos + enemyVel * timeToClosestApproach;
                double closestApproachDistance = Vector3D.Distance(ourFuturePos, enemyFuturePos);

                if (closestApproachDistance > 500.0) return false;

                Vector3D enemyDirection2 = enemyVel;
                if (enemyDirection2.LengthSquared() > 0)
                    enemyDirection2 = Vector3D.Normalize(enemyDirection2);
                Vector3D toPlayer2 = Vector3D.Normalize(relativePos);
                double aspectAngle2 = Math.Acos(MathHelper.Clamp(Vector3D.Dot(enemyDirection2, toPlayer2), -1.0, 1.0));
                double aspectAngleDeg2 = MathHelper.ToDegrees(aspectAngle2);

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

                    if (i < allRadars.Count && allRadars[i].IsTracking)
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
