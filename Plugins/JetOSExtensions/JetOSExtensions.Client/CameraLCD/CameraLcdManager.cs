using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JetOSExtensions.Shared;
using Sandbox.Game.Entities.Blocks;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;

namespace CameraLCD
{
    public static class CameraLcdManager
    {
        private static readonly ConcurrentDictionary<DisplayId, CameraTSS> _displays = new ConcurrentDictionary<DisplayId, CameraTSS>();
        private static readonly ConcurrentDictionary<DisplayId, MySprite[]> _externalSprites =
            new ConcurrentDictionary<DisplayId, MySprite[]>();
        private static long _renderCount = 0;

        // Pending "heal swaps": dispose+recreate the LCD's text-surface script (what a manual TSS swap
        // does) to re-bind a scaled surface stranded on the stock screen texture after a far->near
        // round-trip. Driven on the sim thread from Plugin.Update (SelectScriptToDraw must run there).
        // Local/render-side only — no network sync.
        private static readonly ConcurrentDictionary<DisplayId, MyTextPanelComponent> _healQueue =
            new ConcurrentDictionary<DisplayId, MyTextPanelComponent>();

        // Last script a surface had selected while it was actively drawing (i.e. BEFORE it detached on
        // zoom-out). The heal restores this, not the post-zoom value, which SE may have reset to the
        // spawn/blueprint script.
        private static readonly ConcurrentDictionary<DisplayId, string> _recordedScript =
            new ConcurrentDictionary<DisplayId, string>();

        public static void RecordScript(DisplayId id, string script)
        {
            if (!string.IsNullOrEmpty(script))
                _recordedScript[id] = script;
        }

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

        public static void RequestHeal(DisplayId id, MyTextPanelComponent comp)
        {
            if (comp != null)
                _healQueue[id] = comp;
        }

        // Drives queued heal swaps. Call every frame from Plugin.Update (sim/main thread).
        public static void ProcessHealQueue()
        {
            if (_healQueue.IsEmpty) return;
            foreach (var kv in _healQueue)
            {
                MyTextPanelComponent comp = kv.Value;
                _healQueue.TryRemove(kv.Key, out _);
                try
                {
                    // Restore the script the surface had while last actively drawing (recorded before the
                    // zoom-out detach); fall back to its current value, then the camov script.
                    string recorded = _recordedScript.TryGetValue(kv.Key, out var r) ? r : null;
                    string restore = !string.IsNullOrEmpty(recorded) ? recorded
                        : (string.IsNullOrEmpty(comp.Script) ? CameraTSS.SCRIPT_ID : comp.Script);

                    // Force a real dispose+recreate by clearing ONLY m_previousScript — never the synced
                    // Script property. SelectScriptToDraw with a non-empty id then recreates the script
                    // without hitting its `Script = string.Empty` branch (MyTextPanelComponent.cs ~1754),
                    // so the terminal menu keeps the correct script and there is no value-changed cascade
                    // / re-dispose. The fresh script re-applies the resolution scale and re-binds on its
                    // next draw.
                    comp.m_previousScript = string.Empty;
                    comp.SelectScriptToDraw(restore);

                    // Local-init the synced Script value (no network) so the terminal menu reads the real
                    // script instead of "None". Set ONLY the script (not content/font — those writes caused
                    // the earlier re-dispose cascade). The resulting ValueChanged -> SelectScriptToDraw(restore)
                    // early-returns (we just selected it), so no extra dispose. Client-side only.
                    var sd = comp.m_scriptData.Value;
                    if (sd.Script != restore)
                    {
                        sd.Script = restore;
                        comp.m_scriptData.SetLocalValue(sd);
                    }

                    MyLog.Default.WriteLine($"CAMOV CLIENT: heal-recreate script={restore} property={comp.Script ?? "<none>"}");
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"CAMOV CLIENT: heal-recreate failed: {ex.Message}");
                }
            }
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

            bool anyDrawn = false;
            foreach (var display in _displays.Values)
            {
                if (display.Draw())
                    anyDrawn = true;
            }
            return anyDrawn;
        }
    }
}
