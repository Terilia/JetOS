using JetOSRadarFeed;

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

Console.WriteLine("JetOSRadarFeed helper tests passed.");
