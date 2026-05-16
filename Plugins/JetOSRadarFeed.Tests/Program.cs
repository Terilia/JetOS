using JetOSRadarFeed;
using VRage.Game;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{name}: expected '{expected}', got '{actual}'");
}

Equal("AI Combat 12", RadarFeedEngine.NormalizeRadarNameForTest("AI Combat 12 [JO]"), "normalize tagged name");
Equal("AI Combat", RadarFeedEngine.NormalizeRadarNameForTest(" AI  Combat   [JO] "), "normalize spaces");
Equal(1, RadarFeedEngine.GetRadarIndexForTest("AI Combat [JO]"), "base radar index");
Equal(12, RadarFeedEngine.GetRadarIndexForTest("AI Combat 12 [JO]"), "numbered radar index");
Equal(int.MaxValue, RadarFeedEngine.GetRadarIndexForTest("AI Combat bad [JO]"), "bad radar index");
Equal("name with pipes  and breaks", RadarFeedEngine.SanitizeForTest("name|with|pipes\r\nand breaks"), "sanitize feed text");
Equal("JetOSRadarFeed", RadarFeedEngine.PropertyNameForTest(), "terminal property name");
Equal(3, RadarFeedEngine.FeedVersionForTest(), "feed protocol version");
Equal('F', RadarFeedEngine.ContactKindForTest(MyRelationsBetweenPlayerAndBlock.Owner), "owner relation kind");
Equal('F', RadarFeedEngine.ContactKindForTest(MyRelationsBetweenPlayerAndBlock.FactionShare), "faction relation kind");
Equal('F', RadarFeedEngine.ContactKindForTest(MyRelationsBetweenPlayerAndBlock.Friends), "friend relation kind");
Equal('E', RadarFeedEngine.ContactKindForTest(MyRelationsBetweenPlayerAndBlock.Enemies), "enemy relation kind");
Equal('U', RadarFeedEngine.ContactKindForTest(MyRelationsBetweenPlayerAndBlock.Neutral), "neutral relation kind");
Equal('U', RadarFeedEngine.ContactKindForTest(MyRelationsBetweenPlayerAndBlock.NoOwnership), "no ownership relation kind");
Equal('H', RadarFeedEngine.ContactKindForRelationForTest(MyRelationsBetweenPlayerAndBlock.Enemies), "v2 enemy relation kind");
Equal('N', RadarFeedEngine.ContactKindForRelationForTest(MyRelationsBetweenPlayerAndBlock.Neutral), "v2 neutral relation kind");
Equal('U', RadarFeedEngine.ContactKindForRelationForTest(MyRelationsBetweenPlayerAndBlock.NoOwnership), "v2 unknown relation kind");
Equal(1, RadarFeedEngine.FirstUnassignedIndexForTest(new long[] { 10, 20, 30 }, new long[] { 10, 30 }), "first unassigned target index");
Equal(-1, RadarFeedEngine.FirstUnassignedIndexForTest(new long[] { 10, 20, 30 }, new long[] { 10, 20, 30 }), "no duplicate fallback target");
Equal("Fighter", RadarFeedEngine.FormatContactNameForTest("Fighter", 0x123456, false), "unique name unchanged");
Equal("123456 Fighter", RadarFeedEngine.FormatContactNameForTest("Fighter", 0x123456, true), "duplicate name gets id prefix");
Equal("123456", RadarFeedEngine.FormatContactNameForTest("", 0x123456, false), "empty name gets id");
Equal(true, RadarFeedEngine.ShouldAppendContactForTest('H', 31, 0), "hostile under cap");
Equal(false, RadarFeedEngine.ShouldAppendContactForTest('H', 32, 0), "hostile cap");
Equal(true, RadarFeedEngine.ShouldAppendContactForTest('N', 0, 31), "neutral under cap");
Equal(false, RadarFeedEngine.ShouldAppendContactForTest('U', 0, 32), "unknown cap");
Equal("R|H|42|1|2|3|4|5|6|Target", RadarFeedEngine.FormatContactLineForTest('H', 42, "Target", 1, 2, 3, 4, 5, 6), "v2 contact line");

Console.WriteLine("JetOSRadarFeed helper tests passed.");
