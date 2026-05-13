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
            private static int _cdCheckTicks;
            private static StringBuilder _rebuildSb = new StringBuilder(256);

            public static void Initialize(IMyProgrammableBlock me)
            {
                programBlock = me;
                ParseCustomData();
            }

            private static void ParseCustomData()
            {
                if (!customDataDirty && ++_cdCheckTicks < 10)
                    return;
                _cdCheckTicks = 0;

                string currentData = programBlock.CustomData;

                if (currentData == lastCustomDataRaw && !customDataDirty)
                    return;

                customDataCache.Clear();
                var lines = currentData.Split('\n');

                foreach (var line in lines)
                {
                    if (SW(line))
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
                string value;
                return customDataCache.TryGetValue(key, out value) ? value : null;
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

            public static void MarkDirty()
            {
                customDataDirty = true;
            }

            private static void RebuildCustomData()
            {
                _rebuildSb.Clear();
                foreach (var kvp in customDataCache)
                {
                    _rebuildSb.Append(kvp.Key).Append(':').Append(kvp.Value).Append('\n');
                }

                programBlock.CustomData = _rebuildSb.ToString();
                lastCustomDataRaw = programBlock.CustomData;
            }
        }
    }
}
