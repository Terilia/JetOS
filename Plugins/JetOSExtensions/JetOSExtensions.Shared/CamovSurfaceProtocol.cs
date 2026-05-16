using System;
using System.Collections.Generic;

namespace JetOSExtensions.Shared
{
    public static class CamovSurfaceProtocol
    {
        public const string CameraDisplayScriptId = "TSS_CameraDisplay_2";
        public const string ForcedMarker = "Forced";

        public static bool IsForcedSurface(string customData, int surfaceId)
        {
            if (string.IsNullOrWhiteSpace(customData))
                return false;

            string prefix = surfaceId + ":";
            using (var reader = new System.IO.StringReader(customData))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith(prefix, StringComparison.Ordinal) || line.Length <= prefix.Length)
                        continue;

                    string rest = line.Substring(prefix.Length);
                    string[] segments = rest.Split(':');
                    for (int i = 0; i < segments.Length; i++)
                    {
                        if (segments[i].Trim().Equals(ForcedMarker, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            return false;
        }
    }

    public static class CamovSpriteDeltas
    {
        public static void ApplyIndexedDelta<TDelta, TValue>(
            IList<TValue> target,
            int length,
            IEnumerable<TDelta> deltas,
            Func<TDelta, int> getIndex,
            Func<TDelta, TValue> getValue)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (getIndex == null) throw new ArgumentNullException(nameof(getIndex));
            if (getValue == null) throw new ArgumentNullException(nameof(getValue));

            if (length < 0)
                length = 0;

            while (target.Count > length)
                target.RemoveAt(target.Count - 1);

            while (target.Count < length)
                target.Add(default(TValue)!);

            if (deltas == null)
                return;

            foreach (TDelta delta in deltas)
            {
                int index = getIndex(delta);
                if (index < 0 || index >= length)
                    continue;

                target[index] = getValue(delta);
            }
        }
    }
}
