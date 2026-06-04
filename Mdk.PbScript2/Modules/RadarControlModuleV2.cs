using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class RadarControlModuleV2 : ProgramModule, IRadarLockStatus
        {
            Jet myJet;
            RadarTrackingModule onboardRadar;
            string pluginFeedRaw = "";
            long accumulatedTimeTicks;
            bool behaviorActivated;
            double activationCooldown;
            double sttRequestAccum;
            long lastSttRequestId;
            const double ACTIVATION_COOLDOWN_SECONDS = .167;
            const double STT_REQUEST_INTERVAL_SECONDS = .2;
            const double RWR_FAST_CLOSING_MPS = 250;
            const double RWR_MAX_TCA_SECONDS = 60;
            const double RWR_MAX_CPA_METERS = 800;

            public bool IsTrackLocked { get; private set; }
            public bool HasRwrThreat { get; private set; }

            public RadarControlModuleV2(Program program, Jet jet) : base(program)
            {
                myJet = jet;
                name = "Radar";

                var flightBlock = GetRadarBlock<IMyFlightMovementBlock>("AI Flight");
                var combatBlock = GetRadarBlock<IMyOffensiveCombatBlock>("AI Combat");
                if (flightBlock != null && combatBlock != null)
                    onboardRadar = new RadarTrackingModule(flightBlock, combatBlock);
            }

            public override string[] GetOptions()
            {
                var options = new List<string>();
                options.Add(onboardRadar == null ? "NO STT" : IsTrackLocked ? "STT LOCK" : "STT SRCH");
                options.Add(HasRwrThreat ? "RWR WARN" : "RWR OK");
                options.Add("TGT " + myJet.enemyList.Count);
                options.Add("MAP " + MapContactStoreV2.GetActive().Count);
                return options.ToArray();
            }

            public override void ExecuteOption(int index)
            {
            }

            public override void Tick()
            {
                accumulatedTimeTicks += ParentProgram.Runtime.TimeSinceLastRun.Ticks;
                if (onboardRadar != null)
                {
                    ActivateOnboardRadar();
                    onboardRadar.UpdateTracking(accumulatedTimeTicks);
                    if (activationCooldown > 0)
                        activationCooldown -= SystemManager.DeltaSeconds;
                    else
                        ProcessOnboardRadar();
                }

                ProcessPluginFeed();
                PublishSttRequest();
                UpdateRwrThreats();
                MapContactStoreV2.Decay();
                myJet.UpdateEnemyDecay();
            }

            T GetRadarBlock<T>(string targetName) where T : class, IMyTerminalBlock
            {
                var b = ParentProgram.GridTerminalSystem.GetBlockWithName(targetName + " [JO]") as T;
                if (b == null || (myJet._cockpit != null && !b.IsSameConstructAs(myJet._cockpit)))
                    b = ParentProgram.GridTerminalSystem.GetBlockWithName(targetName) as T;
                return b != null && (myJet._cockpit == null || b.IsSameConstructAs(myJet._cockpit)) ? b : null;
            }

            void ActivateOnboardRadar()
            {
                if (behaviorActivated || onboardRadar == null)
                    return;

                onboardRadar.L_FlightBlock.Enabled = false;
                onboardRadar.L_FlightBlock.CollisionAvoidance = false;

                onboardRadar.L_CombatBLock.Enabled = true;
                onboardRadar.L_CombatBLock.UpdateTargetInterval = 5;
                onboardRadar.L_CombatBLock.SearchEnemyComponent.TargetingLockOptions = VRage.Game.ModAPI.Ingame.MyGridTargetingRelationFiltering.Enemy;
                onboardRadar.L_CombatBLock.SelectedAttackPattern = 3;
                onboardRadar.L_CombatBLock.SetValue<long>("OffensiveCombatIntercept_GuidanceType", 0);
                onboardRadar.L_CombatBLock.SetValueBool("OffensiveCombatIntercept_OverrideCollisionAvoidance", true);
                onboardRadar.L_CombatBLock.ApplyAction("ActivateBehavior_On");
                onboardRadar.L_CombatBLock.ApplyAction("SetTargetingGroup_Weapons");
                onboardRadar.L_CombatBLock.ApplyAction("SetTargetPriority_Closest");

                behaviorActivated = true;
                activationCooldown = ACTIVATION_COOLDOWN_SECONDS;
            }

            void ProcessOnboardRadar()
            {
                IsTrackLocked = false;
                if (!onboardRadar.IsTracking || !onboardRadar.HasReceivedPosition)
                    return;

                Vector3D targetPos = onboardRadar.TargetPosition;
                if (targetPos.LengthSquared() < 1)
                    return;

                long entityId = onboardRadar.TrackedEntityId;
                string name = onboardRadar.TrackedObjectName;
                var selected = myJet.GetSelectedEnemy();
                if (selected.HasValue && (selected.Value.Position - targetPos).LengthSquared() < 2500)
                    IsTrackLocked = true;
                if (ParentProgram.Me.GetProperty("JetOSRadarFeed") != null)
                    return;

                myJet.UpdateOrAddEnemy(targetPos, onboardRadar.TargetVelocity, !SE(name) ? name : "", RadarContactV2.SRC_ONBOARD_STT, entityId);
                IsTrackLocked = IsTrackLocked || myJet.IsSelectedEntity(entityId);
            }

            void ProcessPluginFeed()
            {
                if (ParentProgram.Me.GetProperty("JetOSRadarFeed") == null)
                    return;

                StringBuilder sb = ParentProgram.Me.GetValue<StringBuilder>("JetOSRadarFeed");
                if (sb == null)
                    return;

                string raw = sb.ToString();
                if (SE(raw) || raw == pluginFeedRaw || !raw.StartsWith("JORAD|3|"))
                    return;

                pluginFeedRaw = raw;

                string[] lines = raw.Split('\n');
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] p = lines[i].Trim().Split('|');
                    if (p.Length < 10 || p[0] != "R" || SE(p[1]))
                        continue;

                    char kind = p[1][0];
                    long targetId;
                    double px, py, pz, vx, vy, vz;
                    if (!long.TryParse(p[2], out targetId) || targetId == 0)
                        continue;
                    if (!TryParseFeedDouble(p[3], out px) || !TryParseFeedDouble(p[4], out py) || !TryParseFeedDouble(p[5], out pz))
                        continue;
                    if (!TryParseFeedDouble(p[6], out vx) || !TryParseFeedDouble(p[7], out vy) || !TryParseFeedDouble(p[8], out vz))
                        continue;

                    Vector3D pos = new Vector3D(px, py, pz);
                    Vector3D vel = new Vector3D(vx, vy, vz);
                    if (pos.LengthSquared() < 1)
                        continue;

                    string name = p[9];
                    if (kind == RadarContactV2.KIND_HOSTILE)
                        myJet.UpdateOrAddEnemy(pos, vel, name, RadarContactV2.SRC_RADARFEED_V2, targetId);
                    else if (RadarContactV2.IsMapKind(kind))
                        MapContactStoreV2.Update(kind, targetId, pos, vel, name, ParentProgram.Me.EntityId, 0, 0);
                }
            }

            void PublishSttRequest()
            {
                if (ParentProgram.Me.GetProperty("JetOSRadarFeed") == null)
                    return;

                long selectedId = myJet.GetSelectedEnemyId();
                sttRequestAccum += SystemManager.DeltaSeconds;
                if (selectedId == lastSttRequestId && sttRequestAccum < STT_REQUEST_INTERVAL_SECONDS)
                    return;

                sttRequestAccum = 0;
                lastSttRequestId = selectedId;
                if (selectedId != 0)
                    ParentProgram.Me.SetValue<StringBuilder>("JetOSRadarFeed", new StringBuilder("STT|" + selectedId));
            }

            void UpdateRwrThreats()
            {
                HasRwrThreat = false;
                Vector3D ownPos = myJet.CockpitPosition;
                Vector3D ownVel = myJet.CockpitVelocity;

                for (int i = 0; i < myJet.enemyList.Count; i++)
                {
                    var c = myJet.enemyList[i];
                    if (c.EntityId == 0 || c.IsStale) continue;
                    if (!IsFastClosingGrid(c.Position, c.Velocity, ownPos, ownVel)) continue;
                    HasRwrThreat = true;
                    SoundManager.Event(SoundManager.RWR_LAUNCH);
                    return;
                }
            }

            static bool IsFastClosingGrid(Vector3D targetPos, Vector3D targetVel, Vector3D ownPos, Vector3D ownVel)
            {
                Vector3D toUs = ownPos - targetPos;
                double range = toUs.Length();
                if (range < 1) return false;

                Vector3D los = toUs / range;
                Vector3D relativeVel = ownVel - targetVel;
                double closing = -VD(relativeVel, los);
                if (closing < RWR_FAST_CLOSING_MPS) return false;

                double tca = range / closing;
                if (tca > RWR_MAX_TCA_SECONDS) return false;

                Vector3D ownFuture = ownPos + ownVel * tca;
                Vector3D targetFuture = targetPos + targetVel * tca;
                return VDi(ownFuture, targetFuture) <= RWR_MAX_CPA_METERS;
            }

            static bool TryParseFeedDouble(string s, out double value)
            {
                return double.TryParse(s, out value);
            }

            public override void HandleSpecialFunction(int key)
            {
            }

            public override string GetHotkeys()
            {
                return "";
            }
        }
    }
}
