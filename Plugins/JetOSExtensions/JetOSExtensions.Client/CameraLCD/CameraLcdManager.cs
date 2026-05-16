using System.Collections.Concurrent;
using System.Linq;

namespace CameraLCD
{
    public static class CameraLcdManager
    {
        private static readonly ConcurrentDictionary<DisplayId, CameraTSS> _displays = new ConcurrentDictionary<DisplayId, CameraTSS>();
        private static long _renderCount = 0;
        private static int _displayIndex = 0;

        public static void AddDisplay(DisplayId id, CameraTSS tss)
        {
            _displays.TryAdd(id, tss);
        }

        public static void RemoveDisplay(DisplayId id)
        {
            _displays.TryRemove(id, out _);
        }

        // True iff the (entityId, area) pair is a display we actively render.
        // Used by Patch_RenderSpritesToTexture to suppress vanilla async sprite rasterize,
        // which otherwise wipes the camera image we wrote synchronously. Our CamovComposite
        // is the single writer for sprites on these displays.
        public static bool HasDisplay(long entityId, int area)
        {
            return _displays.ContainsKey(new DisplayId(entityId, area));
        }

        private static bool ShouldDraw()
        {
            return Plugin.Settings.Enabled && (_renderCount % Plugin.Settings.Ratio) == 0;
        }

        public static bool Draw()
        {
            _renderCount++;
            if (!ShouldDraw() || _displays.Count == 0)
                return false;

            if (_displayIndex > _displays.Count)
                _displayIndex = 0;

            int i = _displayIndex;
            if (i < _displays.Count)
            {
                foreach (var display in _displays.Values.Skip(_displayIndex))
                {
                    i++;
                    if (display.Draw())
                    {
                        _displayIndex = i;
                        return true;
                    }
                }
            }

            i = 0;
            foreach (var display in _displays.Values)
            {
                if (i == _displayIndex)
                {
                    _displayIndex++;
                    return false;
                }

                i++;
                if (display.Draw())
                {
                    _displayIndex = i;
                    return true;
                }
            }

            _displayIndex = 0;
            return false;
        }
    }
}
