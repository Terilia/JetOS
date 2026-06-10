using CameraLCD.Gui;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Utils;

namespace CameraLCD
{
    public sealed class Plugin
    {
        public static CameraLCDSettings Settings { get; private set; }
        public static Boxed<(uint CharacterActorId, string[] MaterialsDisabledInFirst)>? FirstPersonCharacter = null;

        public Plugin()
        {
            Settings = CameraLCDSettings.Load();
        }

        private uint _counter = 0;
        public void Update()
        {
            // Drive heal swaps every frame (sim thread) so far->near recovery is snappy, not gated
            // by the 10-frame throttle below.
            if (Settings.Enabled)
                CameraLcdManager.ProcessHealQueue();

            if (++_counter % 10 != 0 || !Settings.Enabled)
                return;

            if (MySession.Static?.CameraController?.Entity is MyCharacter character && (character.IsInFirstPersonView || character.ForceFirstPersonCamera))
            {
                FirstPersonCharacter = new((character.Render.GetRenderObjectID(), character.Definition.MaterialsDisabledIn1st));
            }
            else
            {
                FirstPersonCharacter = null;
            }

            // Scan for LCDs flagged "Forced" in CustomData and keep the real Camera
            // Display text-surface script selected for those surfaces.
            CamovShadowManager.Update();
        }

        public void OpenConfigDialog()
        {
            MyGuiSandbox.AddScreen(new MyGuiScreenPluginConfig());
        }

        public void Dispose()
        {
        }
    }
}
