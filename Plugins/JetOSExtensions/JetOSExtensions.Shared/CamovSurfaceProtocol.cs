using System;
using System.Collections.Generic;

namespace JetOSExtensions.Shared
{
    public static class CamovSurfaceProtocol
    {
        public const string CameraDisplayScriptId = "TSS_CameraDisplay_2";
        public const string ForcedMarker = "Forced";

        public static bool UsesForcedMode(string customData, int surfaceId, bool commonTssSet, bool cameraSelected)
        {
            return cameraSelected && (commonTssSet || IsForcedSurface(customData, surfaceId));
        }

        public static bool HasCameraSelection(string customData, int surfaceId)
        {
            return !string.IsNullOrWhiteSpace(GetCameraSelectionName(customData, surfaceId));
        }

        public static string GetCameraSelectionName(string customData, int surfaceId)
        {
            if (string.IsNullOrWhiteSpace(customData))
                return string.Empty;

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
                    if (segments.Length == 0)
                        continue;

                    string name = segments[0].Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }

            return string.Empty;
        }

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
                    if (segments.Length == 0 || string.IsNullOrWhiteSpace(segments[0]))
                        continue;

                    for (int i = 1; i < segments.Length; i++)
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
