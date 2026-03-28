using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
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
            // Store last two (position, timestamp) entries
            TrackingPoint p1;
            TrackingPoint p0;

            // True once we have received at least one valid waypoint position
            public bool HasReceivedPosition { get; private set; }

            //Counting Positions
            public long CurrentTime;
            public int CurrentTick;
            const int ForcedRefreshRate = 40; //this is used to force a position relog on static grids

            // Reusable buffer for GetWaypoints to avoid allocation each tick
            List<IMyAutopilotWaypoint> _waypointBuffer = new List<IMyAutopilotWaypoint>();

            /// <summary>
            /// Constructor, takes flight and combat AI blocks
            /// </summary>
            /// <param name="LBlock_F">The flight block to use</param>
            /// <param name="LBlockC">The combat block to use</param>
            public RadarTrackingModule(IMyFlightMovementBlock LBlock_F, IMyOffensiveCombatBlock LBlockC)
            {
                L_FlightBlock = LBlock_F;
                L_CombatBLock = LBlockC;
                // Property config deferred to RadarControlModule.ActivateRadar()
                if (LBlock_F != null) { LBlock_F.Enabled = false; LBlock_F.CollisionAvoidance = false; }
            }

            /// <summary>
            /// Call This Before Using Any Of The Properties, Updates Position.
            /// Reads from GetWaypoints() list instead of CurrentWaypoint — the flight
            /// block's behavior is not activated, so CurrentWaypoint is never set, but
            /// the combat block still pushes waypoints to the internal list.
            /// </summary>
            public void UpdateTracking(long CurrentPBTime_Ticks)
            {
                //Updates Time
                CurrentTime = CurrentPBTime_Ticks;

                // Read waypoint list — combat block pushes target position here
                // even though flight block behavior is not activated
                _waypointBuffer.Clear();
                L_FlightBlock.GetWaypoints(_waypointBuffer);

                if (_waypointBuffer.Count > 0)
                {
                    Vector3D TargetPosition = _waypointBuffer[0].Matrix.Translation;

                    //Need To Use This As Otherwise Gives False Data
                    if (TargetPosition != p0.Position || CurrentTick > ForcedRefreshRate)
                    {
                        // Shift historical data
                        p1 = p0;
                        p0 = new TrackingPoint(TargetPosition, CurrentTime);
                        HasReceivedPosition = true;

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
            /// Gets the most recent velocity vector in m/s.
            /// </summary>
            public Vector3D TargetVelocity
            {
                get
                {
                    // Extract position and time from the stored tracking points
                    double time1 = p1.Timestamp;

                    // p1 not yet initialized (still at default zero) — no valid velocity yet.
                    // Without two real position samples, velocity would be
                    // (realPos - ZeroVector) / time = wildly wrong.
                    if (time1 <= 0) return VZ;

                    Vector3D pos1 = p1.Position;
                    Vector3D pos0 = p0.Position;
                    double time0 = p0.Timestamp;

                    //Calculates protecting against zero time errors (would give NaN)
                    double dt = time0 - time1;
                    if (dt <= 0) return VZ;

                    // Timestamps are in TimeSpan ticks (10,000,000 per second), convert to seconds
                    double dtSeconds = dt / 10000000.0;

                    //Returns velocity in m/s
                    return (pos0 - pos1) / dtSeconds;
                }
            }

            /// <summary>
            /// Predicts the target's position using current velocity and acceleration.
            /// </summary>
            public Vector3D TargetPosition
            {
                get
                {

                    // Extracts Current Position
                    Vector3D lastPosition = p0.Position;
                    double lastTime = p0.Timestamp;

                    //Gets V in m/s
                    Vector3D velocity = TargetVelocity;

                    //Timestep — convert TimeSpan ticks to seconds
                    double dtSeconds = (double)(CurrentTime - lastTime) / 10000000.0;

                    // Cap extrapolation to 1 second — beyond that data is stale,
                    // extrapolating further makes static targets appear to fly away
                    if (dtSeconds > 1.0) return lastPosition;

                    //S1 = S0 + UT (simple suvat equation)
                    return lastPosition + velocity * dtSeconds;
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
            /// Gets the EntityId of the tracked target (0 if not tracking).
            /// </summary>
            public long TrackedEntityId
            {
                get
                {
                    var foundId = L_CombatBLock.SearchEnemyComponent.FoundEnemyId;
                    return foundId ?? 0;
                }
            }

            /// <summary>
            /// Tells You Tracked Object Name
            /// </summary>
            // Prefix used by SE's OffensiveCombatBlock DetailedInfo:
            //   "Status: Attacking {GridDisplayName}"
            // Also check hit-and-run variant which uses the same format.
            const string ATK_PREFIX = "Status: Attacking ";

            public string TrackedObjectName
            {
                get
                {
                    string detailedInfo = L_CombatBLock.DetailedInfo;
                    if (string.IsNullOrEmpty(detailedInfo))
                        return "";

                    // Scan each line for the attacking prefix
                    int start = 0;
                    while (start < detailedInfo.Length)
                    {
                        int nl = detailedInfo.IndexOf('\n', start);
                        int end = nl >= 0 ? nl : detailedInfo.Length;
                        int len = end - start;

                        if (len > ATK_PREFIX.Length &&
                            detailedInfo.IndexOf(ATK_PREFIX, start, len) == start)
                        {
                            return detailedInfo.Substring(start + ATK_PREFIX.Length, len - ATK_PREFIX.Length).Trim();
                        }
                        start = end + 1;
                    }

                    return "";
                }
            }

        }
    }
}
