using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    partial class Program
    {
        static class CustomDataManager
        {
            private static Dictionary<string, string> customDataCache = new Dictionary<string, string>();
            private static bool customDataDirty = true;
            private static string lastCustomDataRaw = "";
            private static IMyProgrammableBlock programBlock;

            public static void Initialize(IMyProgrammableBlock me)
            {
                programBlock = me;
                ParseCustomData();
            }

            private static void ParseCustomData()
            {
                string currentData = programBlock.CustomData;

                if (currentData == lastCustomDataRaw && !customDataDirty)
                    return;

                customDataCache.Clear();
                var lines = currentData.Split('\n');

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    int colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string key = line.Substring(0, colonIndex);
                        string value = line.Substring(colonIndex + 1);
                        customDataCache[key] = value;
                    }
                }

                lastCustomDataRaw = currentData;
                customDataDirty = false;
            }

            public static string GetValue(string key)
            {
                ParseCustomData();
                return customDataCache.ContainsKey(key) ? customDataCache[key] : null;
            }

            public static void SetValue(string key, string value)
            {
                ParseCustomData();
                customDataCache[key] = value;
                RebuildCustomData();
            }

            public static bool TryGetValue(string key, out string value)
            {
                ParseCustomData();
                return customDataCache.TryGetValue(key, out value);
            }

            public static void RemoveValue(string key)
            {
                ParseCustomData();
                if (customDataCache.ContainsKey(key))
                {
                    customDataCache.Remove(key);
                    RebuildCustomData();
                }
            }

            public static void MarkDirty()
            {
                customDataDirty = true;
            }

            private static void RebuildCustomData()
            {
                var sb = new StringBuilder();
                foreach (var kvp in customDataCache)
                {
                    sb.Append(kvp.Key).Append(':').Append(kvp.Value).Append('\n');
                }

                programBlock.CustomData = sb.ToString();
                lastCustomDataRaw = programBlock.CustomData;
            }
        }
    }
}
