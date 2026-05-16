using System;
using System.Collections.Generic;
using JetOSExtensions.Shared;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;

namespace CameraLCD
{
    /// <summary>
    /// For LCDs whose CustomData contains a surface line with the "Forced" flag
    /// (e.g. "0:Eye:Forced"), keeps the real Camera Display text-surface script
    /// selected. That lets vanilla release and recreate generated textures normally
    /// while the UpdateSpriteCollection patch preserves PB/app sprite overlays.
    /// </summary>
    internal static class CamovShadowManager
    {
        public const string ForcedMarker = CamovSurfaceProtocol.ForcedMarker;

        private const int ScanIntervalTicks = 60;
        private static long _tickCounter;
        private static readonly Dictionary<DisplayId, ForcedSurfaceState> ForcedStates = new Dictionary<DisplayId, ForcedSurfaceState>();

        private struct ForcedSurfaceState
        {
            public ContentType ContentType;
            public string Script;
            public bool TextureGenerated;
            public bool Registered;

            public ForcedSurfaceState(ContentType contentType, string script, bool textureGenerated, bool registered)
            {
                ContentType = contentType;
                Script = script ?? "<null>";
                TextureGenerated = textureGenerated;
                Registered = registered;
            }

            public bool SameAs(ForcedSurfaceState other)
            {
                return ContentType == other.ContentType
                    && Script == other.Script
                    && TextureGenerated == other.TextureGenerated
                    && Registered == other.Registered;
            }
        }

        public static void Update()
        {
            if (++_tickCounter % ScanIntervalTicks != 0) return;
            if (!Plugin.Settings.Enabled) return;

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

                    ProcessBlock(block);
                }
            }
        }

        private static void ProcessBlock(MyTerminalBlock block)
        {
            if (block is MyTextPanel tp)
            {
                var comp = tp.PanelComponent;
                if (comp != null) ProcessSurface(block, comp, 0);
                return;
            }
            var multi = block.Components.Get<MyMultiTextPanelComponent>();
            if (multi?.Panels != null)
            {
                for (int i = 0; i < multi.Panels.Count; i++)
                    ProcessSurface(block, multi.Panels[i], i);
            }
        }

        private static void ProcessSurface(MyTerminalBlock block, MyTextPanelComponent surface, int area)
        {
            // Only claim this surface if its specific CustomData line actually has the Forced flag.
            if (!CameraTSS.TryParseForcedForSurface(block.CustomData, GetSurfaceKey(block, area)))
                return;

            try
            {
                ContentType beforeContent = surface.ContentType;
                string beforeScript = surface.Script ?? "<null>";
                bool beforeTextureGenerated = surface.m_textureGenerated;
                bool beforeRegistered = CameraLcdManager.HasDisplay(block.EntityId, area);

                if (surface.ContentType != ContentType.SCRIPT)
                    surface.ContentType = ContentType.SCRIPT;

                if (surface.Script != CameraTSS.SCRIPT_ID)
                    surface.Script = CameraTSS.SCRIPT_ID;

                surface.SelectScriptToDraw(CameraTSS.SCRIPT_ID);

                DisplayId id = new DisplayId(block.EntityId, area);
                var current = new ForcedSurfaceState(
                    surface.ContentType,
                    surface.Script,
                    surface.m_textureGenerated,
                    CameraLcdManager.HasDisplay(block.EntityId, area));

                bool first = !ForcedStates.TryGetValue(id, out var previous);
                bool changed = first || !current.SameAs(previous) ||
                    beforeContent != ContentType.SCRIPT || beforeScript != CameraTSS.SCRIPT_ID;

                if (changed && Plugin.Settings.DebugLogging)
                {
                    string reason = first ? "first-seen" :
                        (beforeContent != ContentType.SCRIPT || beforeScript != CameraTSS.SCRIPT_ID) ? "reattach-script" :
                        beforeTextureGenerated != current.TextureGenerated ? "texture-state" :
                        beforeRegistered != current.Registered ? "display-registration" :
                        "state-change";

                    MyLog.Default.WriteLine(
                        $"CAMOV CLIENT: forced-select reason={reason} lcd={block.EntityId} area={area} " +
                        $"lcdName=\"{block.CustomName}\" beforeContent={beforeContent} beforeScript={beforeScript} " +
                        $"content={current.ContentType} script={current.Script} " +
                        $"texGenerated={current.TextureGenerated} registered={current.Registered}");
                }

                ForcedStates[id] = current;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"CameraLCD-CAMOV: forced script selection failed for {block.CustomName}: {ex.Message}");
            }
        }

        private static int GetSurfaceKey(MyTerminalBlock block, int area)
        {
            // MyTextPanel's one surface is addressed by "0:" regardless of the panel's Area.
            return block is MyTextPanel ? 0 : area;
        }
    }
}
