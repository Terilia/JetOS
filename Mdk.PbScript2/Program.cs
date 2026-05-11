using Sandbox.ModAPI.Ingame;
using System;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // JetOS - Fighter Jet Operating System for Space Engineers
        // This is the main entry point. All other code is in partial class files.
        //
        // File Structure:
        // - Program.cs (this file) - Entry point
        // - SystemManager.cs - Static orchestrator
        // - Jet.cs - Hardware abstraction layer
        // - UI/UIController.cs - LCD rendering
        // - UI/UIElements.cs - UI primitives
        // - Modules/*.cs - Feature modules (HUD, Radar, Weapons, etc.)
        // - Utilities/*.cs - Helper classes (PID, Navigation, etc.)
        // - Extensions/RandomExtensions.cs - Extension methods

        public Program()
        {
            SystemManager.Initialize(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                SystemManager.Main(argument, updateSource);
            }
            catch (NullReferenceException e)
            {
                Echo($"NRE {e.Message}");
                SystemManager.Initialize(this);
            }
            catch (Exception e)
            {
                Echo($"ERR {e.GetType().Name}: {e.Message}");
            }
        }

    }
}
