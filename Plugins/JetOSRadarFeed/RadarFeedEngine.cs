using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using SpaceEngineers.Game.Entities.Blocks;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.Entity;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace JetOSRadarFeed
{
    public sealed class RadarFeedEngine
    {
        const string Tag = "[JO]";
        const string CombatBaseName = "AI Combat";
        const string FeedBlockName = "JetOS Radar Feed [JO]";
        const string Header = "JORAD";
        const double RadarRangeMeters = 2500.0;
        const int UpdateIntervalFrames = 10;

        readonly Action<string> _log;
        readonly List<MyEntity> _candidates = new List<MyEntity>(128);
        readonly List<ContactCandidate> _ranked = new List<ContactCandidate>(128);
        readonly HashSet<long> _assignedTargets = new HashSet<long>();
        readonly StringBuilder _feed = new StringBuilder(2048);
        int _frame;
        long _sequence;

        public RadarFeedEngine(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        public void Update()
        {
            if (MySession.Static == null)
                return;

            _frame++;
            if (_frame < UpdateIntervalFrames)
                return;
            _frame = 0;

            try
            {
                UpdateFeeds();
            }
            catch (Exception ex)
            {
                _log("JetOSRadarFeed: update failed: " + ex);
            }
        }

        void UpdateFeeds()
        {
            _sequence++;

            foreach (MyEntity entity in MyEntities.GetEntities())
            {
                var grid = entity as MyCubeGrid;
                if (grid == null || grid.MarkedForClose)
                    continue;

                MyTerminalBlock feedBlock = FindFeedBlock(grid);
                if (feedBlock == null)
                    continue;

                List<MyOffensiveCombatBlock> radars = FindRadars(grid);
                WriteFeed(feedBlock, grid, radars);
            }
        }

        MyTerminalBlock FindFeedBlock(MyCubeGrid grid)
        {
            foreach (MyTerminalBlock block in grid.GetFatBlocks<MyTerminalBlock>())
            {
                if (block == null || block.MarkedForClose)
                    continue;
                if (block.CustomName != null && block.CustomName.ToString() == FeedBlockName)
                    return block;
            }
            return null;
        }

        List<MyOffensiveCombatBlock> FindRadars(MyCubeGrid grid)
        {
            var radars = new List<MyOffensiveCombatBlock>();
            foreach (MyOffensiveCombatBlock radar in grid.GetFatBlocks<MyOffensiveCombatBlock>())
            {
                if (!IsEligibleRadar(radar))
                    continue;
                radars.Add(radar);
            }
            radars.Sort((a, b) => GetRadarIndex(a).CompareTo(GetRadarIndex(b)));
            return radars;
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

        void WriteFeed(MyTerminalBlock feedBlock, MyCubeGrid sourceGrid, List<MyOffensiveCombatBlock> radars)
        {
            _assignedTargets.Clear();
            _feed.Clear();
            _feed.Append(Header).Append("|1|").Append(_sequence).AppendLine();

            for (int i = 0; i < radars.Count; i++)
            {
                ContactCandidate? contact = FindContactForRadar(sourceGrid, radars[i]);
                if (!contact.HasValue)
                    continue;

                ContactCandidate c = contact.Value;
                _assignedTargets.Add(c.EntityId);
                AppendContact(c);
            }

            string next = _feed.ToString();
            if (feedBlock.CustomData != next)
                feedBlock.CustomData = next;
        }

        ContactCandidate? FindContactForRadar(MyCubeGrid sourceGrid, MyOffensiveCombatBlock radar)
        {
            _candidates.Clear();
            _ranked.Clear();

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

                double distanceSq = Vector3D.DistanceSquared(radarPos, entity.WorldMatrix.Translation);
                double rank = GetRank(radar.TargetPriority, entity, distanceSq);
                _ranked.Add(new ContactCandidate(entity, rank));
            }

            _ranked.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            for (int i = 0; i < _ranked.Count; i++)
            {
                ContactCandidate candidate = _ranked[i];
                if (!_assignedTargets.Contains(candidate.EntityId))
                    return candidate;
            }

            return _ranked.Count > 0 ? _ranked[0] : (ContactCandidate?)null;
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

        void AppendContact(ContactCandidate contact)
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
            _feed.Append(Sanitize(contact.Name)).AppendLine();
        }

        void AppendDouble(double value)
        {
            _feed.Append(value.ToString("R"));
        }

        static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace("|", " ").Replace("\r", " ").Replace("\n", " ");
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

        struct ContactCandidate
        {
            public readonly long EntityId;
            public readonly string Name;
            public readonly Vector3D Position;
            public readonly Vector3D Velocity;
            public readonly double Rank;

            public ContactCandidate(MyEntity entity, double rank)
            {
                EntityId = entity.EntityId;
                Name = entity.DisplayName ?? "";
                Position = entity.WorldMatrix.Translation;
                Velocity = entity.Physics == null ? Vector3D.Zero : entity.Physics.LinearVelocity;
                Rank = rank;
            }
        }
    }
}
