using System.Text;

namespace JetOSExtensions.Shared
{
    public static class RadarFeedProtocol
    {
        public const string PropertyName = "JetOSRadarFeed";
        public const string Header = "JORAD";
        public const int FeedVersion = 3;

        public static string EmptyFeed(long sequence)
        {
            return Header + "|" + FeedVersion + "|" + sequence + "\n";
        }

        public static string FormatContactLine(char kind, long entityId, string name, double px, double py, double pz, double vx, double vy, double vz)
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

        public static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("|", " ").Replace("\r", " ").Replace("\n", " ");
        }

        public static void AppendDouble(StringBuilder sb, double value)
        {
            sb.Append(value.ToString("R"));
        }
    }
}
