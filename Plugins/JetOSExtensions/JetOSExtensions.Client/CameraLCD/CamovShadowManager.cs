using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;

namespace CameraLCD
{
    /// <summary>
    /// For LCDs whose CustomData contains a surface line with the "Forced" flag
    /// (e.g. "0:Eye:Forced"), creates and maintains a shadow CameraTSS instance so
    /// the camera-overlay pipeline runs even when the user hasn't selected the
    /// "Camera Display" TSS in the content dropdown.
    ///
    /// If the player picks our TSS via the UI, SE creates its own TSS and our
    /// shadow is disposed on the next scan.
    /// </summary>
    internal static class CamovShadowManager
    {
        public const string ForcedMarker = "Forced";

        private const int ScanIntervalTicks = 60;
        private static long _tickCounter;

        // key = (entityId << 8) | surfaceId
        private static readonly Dictionary<long, CameraTSS> _shadows = new Dictionary<long, CameraTSS>();

        public static void Update()
        {
            if (++_tickCounter % ScanIntervalTicks != 0) return;
            if (!Plugin.Settings.Enabled) return;

            var seen = new HashSet<long>();

            var entities = MyEntities.GetEntities();
            if (entities == null) return;
            foreach (var ent in entities)
            {
                if (!(ent is MyCubeGrid grid) || grid.MarkedForClose) continue;
                foreach (var block in grid.GetFatBlocks<MyTerminalBlock>())
                {
                    if (block == null || block.MarkedForClose) continue;
                    string data = block.CustomData;
                    if (string.IsNullOrEmpty(data)) continue;
                    // Cheap string-contains filter first; per-surface parsing happens
                    // inside CameraTSS.TryParseForcedForSurface.
                    if (data.IndexOf(ForcedMarker, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    ProcessBlock(block, seen);
                }
            }

            // Dispose shadows for keys not seen this scan (marker removed / block destroyed)
            List<long> toRemove = null;
            foreach (var kvp in _shadows)
            {
                if (!seen.Contains(kvp.Key))
                {
                    try { kvp.Value?.Dispose(); } catch { }
                    (toRemove ?? (toRemove = new List<long>())).Add(kvp.Key);
                }
            }
            if (toRemove != null)
                foreach (var k in toRemove) _shadows.Remove(k);
        }

        private static void ProcessBlock(MyTerminalBlock block, HashSet<long> seen)
        {
            if (block is MyTextPanel tp)
            {
                var comp = tp.PanelComponent;
                if (comp != null) ProcessSurface(block, comp, 0, seen);
                return;
            }
            var multi = block.Components.Get<MyMultiTextPanelComponent>();
            if (multi?.Panels != null)
            {
                for (int i = 0; i < multi.Panels.Count; i++)
                    ProcessSurface(block, multi.Panels[i], i, seen);
            }
        }

        private static void ProcessSurface(MyTerminalBlock block, MyTextPanelComponent surface, int area, HashSet<long> seen)
        {
            // Only claim this surface if its specific CustomData line actually has the Forced flag.
            if (!CameraTSS.TryParseForcedForSurface(block.CustomData, GetSurfaceKey(block, area)))
                return;

            long key = (block.EntityId << 8) | (uint)(area & 0xFF);
            seen.Add(key);

            // If SE already runs the Camera Display TSS for this surface, defer to it.
            bool seHasTss = surface.ContentType == ContentType.SCRIPT && surface.Script == CameraTSS.SCRIPT_ID;
            if (seHasTss)
            {
                if (_shadows.TryGetValue(key, out var existing))
                {
                    try { existing?.Dispose(); } catch { }
                    _shadows.Remove(key);
                }
                return;
            }

            if (_shadows.TryGetValue(key, out var current))
            {
                // Surface still has the Forced marker — nudge the shadow to rebind its
                // camera if it drifted (away-then-return, local CustomData edit, etc.).
                try { current.RefreshIfBroken(); }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"CameraLCD-CAMOV: RefreshIfBroken failed for {block.CustomName}: {ex.Message}");
                }
                return;
            }

            try
            {
                var tss = new CameraTSS(surface, block, surface.SurfaceSize);
                _shadows[key] = tss;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"CameraLCD-CAMOV: shadow TSS creation failed for {block.CustomName}: {ex.Message}");
            }
        }

        private static int GetSurfaceKey(MyTerminalBlock block, int area)
        {
            // MyTextPanel's one surface is addressed by "0:" regardless of the panel's Area.
            return block is MyTextPanel ? 0 : area;
        }
    }
}
