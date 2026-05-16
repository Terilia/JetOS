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

            // Scan the grid for LCDs flagged "Forced" in CustomData and create shadow TSS
            // instances for them (so camera renders even when ContentType != SCRIPT).
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
