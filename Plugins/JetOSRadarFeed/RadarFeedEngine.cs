using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.Entities.Blocks;
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
        const int FeedVersion = 2;
        const char KindEnemy = 'E';
        const char KindFriendly = 'F';
        const char KindUnknown = 'U';
        const double RadarRangeMeters = 2500.0;
        const int UpdateIntervalFrames = 10;

        readonly Action<string> _log;
        readonly List<MyEntity> _candidates = new List<MyEntity>(128);
        readonly List<ContactCandidate> _ranked = new List<ContactCandidate>(128);
        readonly HashSet<long> _assignedTargets = new HashSet<long>();
        readonly List<MyCubeGrid> _radarGrids = new List<MyCubeGrid>(32);
        readonly List<MyOffensiveCombatBlock> _radars = new List<MyOffensiveCombatBlock>(64);
        readonly List<ConstructFeed> _constructFeeds = new List<ConstructFeed>(16);
        readonly List<ContactCandidate> _feedContacts = new List<ContactCandidate>(64);
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
            property.Setter = (block, value) => { };
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
                {
                    if (!IsEligibleRadar(radar))
                        continue;

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

                _radars.Sort((a, b) => GetRadarIndex(a).CompareTo(GetRadarIndex(b)));
                _constructFeeds.Add(new ConstructFeed(sourceGrid, BuildFeed(sourceGrid, _radars)));
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
            if (!name.Contains(Tag))
                return false;

            string normalized = NormalizeRadarName(name);
            return normalized == CombatBaseName || normalized.StartsWith(CombatBaseName + " ");
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

        StringBuilder BuildFeed(MyCubeGrid sourceGrid, List<MyOffensiveCombatBlock> radars)
        {
            _assignedTargets.Clear();
            _feedContacts.Clear();
            _feed.Clear();
            _feed.Append(Header).Append('|').Append(FeedVersion).Append('|').Append(_sequence).AppendLine();

            for (int i = 0; i < radars.Count; i++)
            {
                ContactCandidate? contact = FindContactForRadar(sourceGrid, radars[i]);
                if (!contact.HasValue)
                    continue;

                ContactCandidate c = contact.Value;
                _assignedTargets.Add(c.EntityId);
                _feedContacts.Add(c);
            }

            for (int i = 0; i < _feedContacts.Count; i++)
            {
                ContactCandidate c = _feedContacts[i];
                AppendContact(c, HasDuplicateFeedName(c, i));
            }

            return new StringBuilder(_feed.ToString());
        }

        bool HasDuplicateFeedName(ContactCandidate contact, int index)
        {
            string name = Sanitize(contact.Name);
            if (string.IsNullOrEmpty(name))
                return true;

            for (int i = 0; i < _feedContacts.Count; i++)
                if (i != index && Sanitize(_feedContacts[i].Name) == name)
                    return true;
            return false;
        }

        ContactCandidate? FindContactForRadar(MyCubeGrid sourceGrid, MyOffensiveCombatBlock radar)
        {
            _candidates.Clear();
            _ranked.Clear();

            long sourceOwner = GetGridOwner(sourceGrid);
            Vector3D radarPos = radar.WorldMatrix.Translation;
            var sphere = new BoundingSphereD(radarPos, RadarRangeMeters);
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, _candidates, MyEntityQueryType.Dynamic);

            foreach (MyEntity entity in _candidates)
            {
                if (entity == null || entity.MarkedForClose || entity.EntityId == sourceGrid.EntityId)
                    continue;

                var targetGrid = entity as MyCubeGrid;
                if (targetGrid == null)
                    continue;
                if (targetGrid.IsSameConstructAs(sourceGrid))
                    continue;

                char kind = GetContactKind(sourceOwner, targetGrid);
                if (kind == KindFriendly)
                    continue;

                double distanceSq = Vector3D.DistanceSquared(radarPos, entity.WorldMatrix.Translation);
                double rank = GetRank(radar.TargetPriority, entity, distanceSq);
                _ranked.Add(new ContactCandidate(targetGrid, rank, kind));
            }

            _ranked.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            int index = FirstUnassignedIndex(_ranked, _assignedTargets);
            if (index >= 0)
                return _ranked[index];

            return null;
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

        void AppendContact(ContactCandidate contact, bool duplicateName)
        {
            Vector3D pos = contact.Position;
            Vector3D vel = contact.Velocity;
            _feed.Append("R|")
                .Append(contact.EntityId).Append('|');
            AppendDouble(pos.X); _feed.Append('|');
            AppendDouble(pos.Y); _feed.Append('|');
            AppendDouble(pos.Z); _feed.Append('|');
            AppendDouble(vel.X); _feed.Append('|');
            AppendDouble(vel.Y); _feed.Append('|');
            AppendDouble(vel.Z); _feed.Append('|');
            _feed.Append(FormatContactName(contact.Name, contact.EntityId, duplicateName)).AppendLine();
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

            public ContactCandidate(MyEntity entity, double rank, char kind)
            {
                EntityId = entity.EntityId;
                Name = entity.DisplayName ?? "";
                Position = entity.WorldMatrix.Translation;
                Velocity = entity.Physics == null ? Vector3D.Zero : entity.Physics.LinearVelocity;
                Rank = rank;
                Kind = kind;
            }
        }
    }
}
