using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.EntityComponents.Blocks;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace JetOSRadarFeed
{
    public sealed class RadarFeedEngine
    {
        const string Tag = "[JO]";
        const string CombatBaseName = "AI Combat";
        const string PropertyName = "JetOSRadarFeed";
        const string Header = "JORAD";
        const int FeedVersion = 3;
        const char KindEnemy = 'E';
        const char KindFriendly = 'F';
        const char KindUnknown = 'U';
        const char KindHostileV2 = 'H';
        const char KindNeutralV2 = 'N';
        const double FallbackRadarRangeMeters = 2500.0;
        const int UpdateIntervalFrames = 10;
        const int MaxHostileContacts = 32;
        const int MaxMapContacts = 32;

        readonly Action<string> _log;
        readonly List<MyEntity> _candidates = new List<MyEntity>(128);
        readonly List<ContactCandidate> _ranked = new List<ContactCandidate>(128);
        readonly List<MyCubeGrid> _radarGrids = new List<MyCubeGrid>(32);
        readonly List<MyOffensiveCombatBlock> _radars = new List<MyOffensiveCombatBlock>(64);
        readonly List<ConstructFeed> _constructFeeds = new List<ConstructFeed>(16);
        readonly List<ContactCandidate> _feedContacts = new List<ContactCandidate>(64);
        readonly HashSet<long> _seenTopGrids = new HashSet<long>();
        readonly StringBuilder _feed = new StringBuilder(2048);
        int _frame;
        long _sequence;
        bool _propertyRegistered;

        public RadarFeedEngine(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        public void Update()
        {
            if (MySession.Static == null)
                return;

            EnsureTerminalProperty();

            _frame++;
            if (_frame < UpdateIntervalFrames)
                return;
            _frame = 0;

            _sequence++;
            try
            {
                RebuildFeeds();
            }
            catch (Exception ex)
            {
                _constructFeeds.Clear();
                _log("JetOSRadarFeed: feed update failed: " + ex);
            }
        }

        void EnsureTerminalProperty()
        {
            if (_propertyRegistered || MyAPIGateway.TerminalControls == null)
                return;

            IMyTerminalControlProperty<StringBuilder> property =
                MyAPIGateway.TerminalControls.CreateProperty<StringBuilder, Sandbox.ModAPI.IMyTerminalBlock>(PropertyName);
            if (property == null)
                return;

            property.Enabled = block => true;
            property.Getter = GetFeed;
            property.Setter = SetFeedRequest;
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyProgrammableBlock>(property);
            _propertyRegistered = true;
            _log("JetOSRadarFeed: terminal property registered.");
        }

        StringBuilder GetFeed(Sandbox.ModAPI.IMyTerminalBlock block)
        {
            try
            {
                var grid = block == null ? null : block.CubeGrid as MyCubeGrid;
                if (grid == null || grid.MarkedForClose)
                    return EmptyFeed();

                for (int i = 0; i < _constructFeeds.Count; i++)
                {
                    ConstructFeed feed = _constructFeeds[i];
                    if (feed.SourceGrid != null && !feed.SourceGrid.MarkedForClose && feed.SourceGrid.IsSameConstructAs(grid))
                        return new StringBuilder(feed.Payload.ToString());
                }

                return EmptyFeed();
            }
            catch (Exception ex)
            {
                _log("JetOSRadarFeed: property getter failed: " + ex);
                return EmptyFeed();
            }
        }

        StringBuilder EmptyFeed()
        {
            return new StringBuilder(Header + "|" + FeedVersion + "|" + _sequence + "\n");
        }

        void SetFeedRequest(Sandbox.ModAPI.IMyTerminalBlock block, StringBuilder value)
        {
            try
            {
                if (block == null || value == null)
                    return;
                string raw = value.ToString();
                if (string.IsNullOrEmpty(raw) || !raw.StartsWith("STT|"))
                    return;

                long targetId;
                if (!long.TryParse(raw.Substring(4).Trim(), out targetId) || targetId == 0)
                    return;

                ApplySttRequest(block, targetId);
            }
            catch (Exception ex)
            {
                _log("JetOSRadarFeed: STT request failed: " + ex);
            }
        }

        void ApplySttRequest(Sandbox.ModAPI.IMyTerminalBlock block, long targetId)
        {
            var sourceGrid = block.CubeGrid as MyCubeGrid;
            if (sourceGrid == null || sourceGrid.MarkedForClose)
                return;

            MyOffensiveCombatBlock radar = FindRadarSourceForConstruct(sourceGrid);
            if (radar == null)
                return;

            MyEntity targetEntity;
            if (!MyEntities.TryGetEntityById(targetId, out targetEntity))
                return;

            var targetGrid = targetEntity as MyCubeGrid;
            if (targetGrid == null || targetGrid.MarkedForClose || targetGrid.IsSameConstructAs(sourceGrid))
                return;

            if (GetContactKindV2(GetGridOwner(sourceGrid), targetGrid) != KindHostileV2)
                return;

            double range = GetRadarRange(radar);
            if (Vector3D.DistanceSquared(radar.WorldMatrix.Translation, targetGrid.WorldMatrix.Translation) > range * range)
                return;

            var search = radar.SearchEnemyComponent as MySearchEnemyComponent;
            var lockBlock = FindLockBlock(targetGrid);
            if (search != null && lockBlock != null)
                search.SetFoundEnemy(lockBlock);
        }

        MyOffensiveCombatBlock FindRadarSourceForConstruct(MyCubeGrid sourceGrid)
        {
            var radars = new List<MyOffensiveCombatBlock>(8);
            foreach (MyEntity entity in MyEntities.GetEntities())
            {
                var grid = entity as MyCubeGrid;
                if (grid == null || grid.MarkedForClose || !grid.IsSameConstructAs(sourceGrid))
                    continue;
                foreach (MyOffensiveCombatBlock radar in grid.GetFatBlocks<MyOffensiveCombatBlock>())
                    if (IsEligibleRadar(radar))
                        radars.Add(radar);
            }
            return radars.Count == 0 ? null : SelectRadarSource(radars);
        }

        static MyCubeBlock FindLockBlock(MyCubeGrid targetGrid)
        {
            foreach (MyCubeBlock block in targetGrid.GetFatBlocks<MyCubeBlock>())
                if (block != null && !block.MarkedForClose && block.IsFunctional)
                    return block;
            return null;
        }

        void RebuildFeeds()
        {
            _constructFeeds.Clear();
            _radarGrids.Clear();

            foreach (MyEntity entity in MyEntities.GetEntities())
            {
                var grid = entity as MyCubeGrid;
                if (grid == null || grid.MarkedForClose)
                    continue;

                foreach (MyOffensiveCombatBlock radar in grid.GetFatBlocks<MyOffensiveCombatBlock>())
                    if (IsEligibleRadar(radar))
                    {
                        _radarGrids.Add(grid);
                        break;
                    }
            }

            for (int i = 0; i < _radarGrids.Count; i++)
            {
                MyCubeGrid sourceGrid = _radarGrids[i];
                if (sourceGrid.MarkedForClose || HasConstructFeed(sourceGrid))
                    continue;

                _radars.Clear();
                for (int j = 0; j < _radarGrids.Count; j++)
                {
                    MyCubeGrid radarGrid = _radarGrids[j];
                    if (radarGrid.MarkedForClose || !radarGrid.IsSameConstructAs(sourceGrid))
                        continue;

                    foreach (MyOffensiveCombatBlock radar in radarGrid.GetFatBlocks<MyOffensiveCombatBlock>())
                    {
                        if (IsEligibleRadar(radar))
                            _radars.Add(radar);
                    }
                }

                if (_radars.Count == 0)
                    continue;

                MyOffensiveCombatBlock sourceRadar = SelectRadarSource(_radars);
                if (sourceRadar != null)
                    _constructFeeds.Add(new ConstructFeed(sourceGrid, BuildFeed(sourceGrid, sourceRadar)));
            }
        }

        bool HasConstructFeed(MyCubeGrid grid)
        {
            for (int i = 0; i < _constructFeeds.Count; i++)
            {
                MyCubeGrid sourceGrid = _constructFeeds[i].SourceGrid;
                if (sourceGrid != null && !sourceGrid.MarkedForClose && sourceGrid.IsSameConstructAs(grid))
                    return true;
            }
            return false;
        }

        bool IsEligibleRadar(MyOffensiveCombatBlock radar)
        {
            if (radar == null || radar.MarkedForClose)
                return false;
            if (!radar.Enabled || !radar.IsFunctional || !radar.IsWorking)
                return false;

            string name = radar.CustomName == null ? "" : radar.CustomName.ToString();
            string normalized = NormalizeRadarName(name);
            return normalized == CombatBaseName || normalized.StartsWith(CombatBaseName + " ");
        }

        static bool IsTaggedRadar(MyOffensiveCombatBlock radar)
        {
            string name = radar.CustomName == null ? "" : radar.CustomName.ToString();
            return name.Contains(Tag);
        }

        MyOffensiveCombatBlock SelectRadarSource(List<MyOffensiveCombatBlock> radars)
        {
            radars.Sort((a, b) => GetRadarIndex(a).CompareTo(GetRadarIndex(b)));
            for (int i = 0; i < radars.Count; i++)
                if (IsTaggedRadar(radars[i]))
                    return radars[i];
            return radars.Count > 0 ? radars[0] : null;
        }

        int GetRadarIndex(MyOffensiveCombatBlock radar)
        {
            return GetRadarIndex(radar.CustomName == null ? "" : radar.CustomName.ToString());
        }

        static int GetRadarIndex(string name)
        {
            string normalized = NormalizeRadarName(name);
            if (normalized == CombatBaseName)
                return 1;

            int value;
            string suffix = normalized.Substring(CombatBaseName.Length).Trim();
            return int.TryParse(suffix, out value) ? value : int.MaxValue;
        }

        static string NormalizeRadarName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            string n = name.Replace(Tag, "").Trim();
            while (n.Contains("  "))
                n = n.Replace("  ", " ");
            return n;
        }

        StringBuilder BuildFeed(MyCubeGrid sourceGrid, MyOffensiveCombatBlock radar)
        {
            _feedContacts.Clear();
            _seenTopGrids.Clear();
            _feed.Clear();
            _feed.Append(Header).Append('|').Append(FeedVersion).Append('|').Append(_sequence).AppendLine();

            ScanContacts(sourceGrid, radar);
            _feedContacts.Sort((a, b) => a.Rank.CompareTo(b.Rank));

            int hostileCount = 0;
            int mapCount = 0;
            for (int i = 0; i < _feedContacts.Count; i++)
            {
                ContactCandidate c = _feedContacts[i];
                if (!ShouldAppendContact(c.Kind, hostileCount, mapCount))
                    continue;
                AppendContact(c);
                if (c.Kind == KindHostileV2)
                    hostileCount++;
                else
                    mapCount++;
            }

            return new StringBuilder(_feed.ToString());
        }

        void ScanContacts(MyCubeGrid sourceGrid, MyOffensiveCombatBlock radar)
        {
            _candidates.Clear();

            long sourceOwner = GetGridOwner(sourceGrid);
            Vector3D radarPos = radar.WorldMatrix.Translation;
            double range = GetRadarRange(radar);
            var sphere = new BoundingSphereD(radarPos, range);
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, _candidates, MyEntityQueryType.Dynamic);

            for (int i = 0; i < _candidates.Count; i++)
            {
                MyEntity entity = _candidates[i];
                if (entity == null || entity.MarkedForClose || entity.EntityId == sourceGrid.EntityId)
                    continue;

                var targetGrid = entity as MyCubeGrid;
                if (targetGrid == null)
                    continue;
                if (targetGrid.IsSameConstructAs(sourceGrid))
                    continue;

                long topId = targetGrid.EntityId;
                if (topId == 0 || _seenTopGrids.Contains(topId))
                    continue;

                char kind = GetContactKindV2(sourceOwner, targetGrid);
                if (!ShouldEmitKind(kind))
                    continue;

                Vector3D contactPosition = GetContactPosition(targetGrid);
                double distanceSq = Vector3D.DistanceSquared(radarPos, contactPosition);
                _seenTopGrids.Add(topId);
                _feedContacts.Add(new ContactCandidate(targetGrid, contactPosition, distanceSq, kind));
            }
        }

        static Vector3D GetContactPosition(MyEntity entity)
        {
            var grid = entity as MyCubeGrid;
            if (grid != null)
                return MyGridPhysicalGroupData.GetGroupSharedProperties(grid).CoMWorld;
            return entity.PositionComp == null ? entity.WorldMatrix.Translation : entity.PositionComp.WorldAABB.Center;
        }

        static int FirstUnassignedIndex(List<ContactCandidate> ranked, HashSet<long> assigned)
        {
            for (int i = 0; i < ranked.Count; i++)
                if (!assigned.Contains(ranked[i].EntityId))
                    return i;
            return -1;
        }

        double GetRank(OffensiveCombatTargetPriority priority, MyEntity entity, double distanceSq)
        {
            double size = entity.PositionComp.WorldAABB.Size.LengthSquared();
            switch (priority)
            {
                case OffensiveCombatTargetPriority.Largest:
                    return -size;
                case OffensiveCombatTargetPriority.Smallest:
                    return size;
                default:
                    return distanceSq;
            }
        }

        static bool ShouldEmitKind(char kind)
        {
            return kind == KindHostileV2 || kind == KindNeutralV2 || kind == KindUnknown;
        }

        static bool ShouldAppendContact(char kind, int hostileCount, int mapCount)
        {
            return kind == KindHostileV2 ? hostileCount < MaxHostileContacts : mapCount < MaxMapContacts;
        }

        static double GetRadarRange(MyOffensiveCombatBlock radar)
        {
            var search = radar.SearchEnemyComponent as MySearchEnemyComponent;
            if (search == null)
                return FallbackRadarRangeMeters;
            double range = search.GetSearchRadius();
            return range > 1 ? range : FallbackRadarRangeMeters;
        }

        void AppendContact(ContactCandidate contact)
        {
            _feed.Append(FormatContactLine(contact.Kind, contact.EntityId, contact.Name,
                contact.Position.X, contact.Position.Y, contact.Position.Z,
                contact.Velocity.X, contact.Velocity.Y, contact.Velocity.Z)).AppendLine();
        }

        void AppendDouble(double value)
        {
            _feed.Append(value.ToString("R"));
        }

        static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace("|", " ").Replace("\r", " ").Replace("\n", " ");
        }

        static string FormatContactName(string name, long entityId, bool duplicateName)
        {
            name = Sanitize(name);
            if (!duplicateName && !string.IsNullOrEmpty(name))
                return name;

            string id = ShortId(entityId);
            return string.IsNullOrEmpty(name) ? id : id + " " + name;
        }

        static string FormatContactLine(char kind, long entityId, string name, double px, double py, double pz, double vx, double vy, double vz)
        {
            var sb = new StringBuilder(128);
            sb.Append("R|").Append(kind).Append('|').Append(entityId).Append('|');
            AppendDouble(sb, px); sb.Append('|');
            AppendDouble(sb, py); sb.Append('|');
            AppendDouble(sb, pz); sb.Append('|');
            AppendDouble(sb, vx); sb.Append('|');
            AppendDouble(sb, vy); sb.Append('|');
            AppendDouble(sb, vz); sb.Append('|');
            sb.Append(Sanitize(name));
            return sb.ToString();
        }

        static void AppendDouble(StringBuilder sb, double value)
        {
            sb.Append(value.ToString("R"));
        }

        static string ShortId(long entityId)
        {
            string hex = ((ulong)entityId).ToString("X");
            return hex.Length <= 6 ? hex : hex.Substring(hex.Length - 6);
        }

        static long GetGridOwner(MyCubeGrid grid)
        {
            if (grid == null)
                return 0;
            if (grid.BigOwners != null && grid.BigOwners.Count > 0)
                return grid.BigOwners[0];
            if (grid.SmallOwners != null && grid.SmallOwners.Count > 0)
                return grid.SmallOwners[0];
            return 0;
        }

        static char GetContactKind(long sourceOwner, MyCubeGrid targetGrid)
        {
            long targetOwner = GetGridOwner(targetGrid);
            if (sourceOwner == 0 || targetOwner == 0)
                return KindUnknown;
            return ContactKindForRelation(MyIDModule.GetRelationPlayerBlock(sourceOwner, targetOwner, MyOwnershipShareModeEnum.Faction));
        }

        static char GetContactKindV2(long sourceOwner, MyCubeGrid targetGrid)
        {
            long targetOwner = GetGridOwner(targetGrid);
            if (sourceOwner == 0 || targetOwner == 0)
                return KindUnknown;
            return ContactKindForRelationV2(MyIDModule.GetRelationPlayerBlock(sourceOwner, targetOwner, MyOwnershipShareModeEnum.Faction));
        }

        static char ContactKindForRelation(MyRelationsBetweenPlayerAndBlock relation)
        {
            switch (relation)
            {
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                case MyRelationsBetweenPlayerAndBlock.Friends:
                    return KindFriendly;
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return KindEnemy;
                default:
                    return KindUnknown;
            }
        }

        static char ContactKindForRelationV2(MyRelationsBetweenPlayerAndBlock relation)
        {
            switch (relation)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return KindHostileV2;
                case MyRelationsBetweenPlayerAndBlock.Neutral:
                    return KindNeutralV2;
                case MyRelationsBetweenPlayerAndBlock.NoOwnership:
                    return KindUnknown;
                default:
                    return KindFriendly;
            }
        }

        public static string NormalizeRadarNameForTest(string name)
        {
            return NormalizeRadarName(name);
        }

        public static int GetRadarIndexForTest(string name)
        {
            return GetRadarIndex(name);
        }

        public static string SanitizeForTest(string value)
        {
            return Sanitize(value);
        }

        public static string PropertyNameForTest()
        {
            return PropertyName;
        }

        public static int FeedVersionForTest()
        {
            return FeedVersion;
        }

        public static char ContactKindForTest(MyRelationsBetweenPlayerAndBlock relation)
        {
            return ContactKindForRelation(relation);
        }

        public static char ContactKindForRelationForTest(MyRelationsBetweenPlayerAndBlock relation)
        {
            return ContactKindForRelationV2(relation);
        }

        public static int FirstUnassignedIndexForTest(long[] rankedIds, long[] assignedIds)
        {
            var assigned = new HashSet<long>();
            if (assignedIds != null)
                for (int i = 0; i < assignedIds.Length; i++)
                    assigned.Add(assignedIds[i]);

            if (rankedIds == null)
                return -1;
            for (int i = 0; i < rankedIds.Length; i++)
                if (!assigned.Contains(rankedIds[i]))
                    return i;
            return -1;
        }

        public static string FormatContactNameForTest(string name, long entityId, bool duplicateName)
        {
            return FormatContactName(name, entityId, duplicateName);
        }

        public static bool ShouldAppendContactForTest(char kind, int hostileCount, int mapCount)
        {
            return ShouldAppendContact(kind, hostileCount, mapCount);
        }

        public static string FormatContactLineForTest(char kind, long entityId, string name, double px, double py, double pz, double vx, double vy, double vz)
        {
            return FormatContactLine(kind, entityId, name, px, py, pz, vx, vy, vz);
        }

        struct ConstructFeed
        {
            public readonly MyCubeGrid SourceGrid;
            public readonly StringBuilder Payload;

            public ConstructFeed(MyCubeGrid sourceGrid, StringBuilder payload)
            {
                SourceGrid = sourceGrid;
                Payload = payload;
            }
        }

        struct ContactCandidate
        {
            public readonly long EntityId;
            public readonly string Name;
            public readonly Vector3D Position;
            public readonly Vector3D Velocity;
            public readonly double Rank;
            public readonly char Kind;

            public ContactCandidate(MyEntity entity, Vector3D position, double rank, char kind)
            {
                EntityId = entity.EntityId;
                Name = entity.DisplayName ?? "";
                Position = position;
                Velocity = entity.Physics == null ? Vector3D.Zero : entity.Physics.LinearVelocity;
                Rank = rank;
                Kind = kind;
            }
        }
    }
}
