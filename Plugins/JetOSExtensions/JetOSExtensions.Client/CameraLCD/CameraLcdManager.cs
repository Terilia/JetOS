using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using JetOSExtensions.Shared;
using VRage.Game.GUI.TextPanel;

namespace CameraLCD
{
    public static class CameraLcdManager
    {
        private static readonly ConcurrentDictionary<DisplayId, CameraTSS> _displays = new ConcurrentDictionary<DisplayId, CameraTSS>();
        private static readonly ConcurrentDictionary<DisplayId, MySprite[]> _externalSprites =
            new ConcurrentDictionary<DisplayId, MySprite[]>();
        private static long _renderCount = 0;
        private static int _displayIndex = 0;

        public static void AddDisplay(DisplayId id, CameraTSS tss)
        {
            _displays.TryAdd(id, tss);
            if (_externalSprites.TryGetValue(id, out var sprites))
                tss.UpdateExternalSprites(sprites);
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

        public static void CaptureExternalSprites(long entityId, int area, MySerializableSpriteCollection sprites)
        {
            var id = new DisplayId(entityId, area);
            MySprite[] frame = _externalSprites.AddOrUpdate(
                id,
                _ => BuildExternalSpriteFrame(null, sprites),
                (_, previous) => BuildExternalSpriteFrame(previous, sprites));
            if (_displays.TryGetValue(id, out var tss))
                tss.UpdateExternalSprites(frame);
        }

        private static MySprite[] BuildExternalSpriteFrame(MySprite[] previous, MySerializableSpriteCollection sprites)
        {
            var frame = previous == null ? new List<MySprite>() : new List<MySprite>(previous);
            CamovSpriteDeltas.ApplyIndexedDelta(frame, sprites.Length, sprites.Sprites, GetSpriteIndex, ToSprite);
            return frame.ToArray();
        }

        private static int GetSpriteIndex(MySerializableSprite sprite) => sprite.Index;

        private static MySprite ToSprite(MySerializableSprite sprite) => sprite;

        private static bool ShouldDraw()
        {
            return (_renderCount % Plugin.Settings.Ratio) == 0;
        }

        private static void RestoreAllResolutionScales()
        {
            foreach (var display in _displays.Values)
                display.RestoreResolutionScale();
        }

        public static bool Draw()
        {
            _renderCount++;
            if (_displays.Count == 0)
                return false;

            if (!Plugin.Settings.Enabled)
            {
                RestoreAllResolutionScales();
                return false;
            }

            if (!ShouldDraw())
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
