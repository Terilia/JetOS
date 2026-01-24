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
                // Log the error for debugging
                Echo($"NullRef Error: {e.Message}");
                Echo($"Stack: {e.StackTrace}");
                // Reinitialize to recover from missing blocks
                SystemManager.Initialize(this);
            }
            catch (Exception e)
            {
                // Log unexpected errors but don't hide them
                Echo($"CRITICAL ERROR: {e.GetType().Name}");
                Echo($"Message: {e.Message}");
                Echo($"Stack: {e.StackTrace}");
                // Don't automatically reinitialize on unexpected errors
                // This helps identify bugs during development
            }
        }

        // Helper function for finding slot for new target
        public static int FindEmptyOrOldestSlot(Jet jet)
        {
            // First pass: find empty slot
            for (int i = 0; i < jet.targetSlots.Length; i++)
            {
                if (!jet.targetSlots[i].IsOccupied)
                {
                    return i;
                }
            }

            // Second pass: all slots occupied, find oldest by timestamp
            int oldestIndex = 0;
            long oldestTimestamp = jet.targetSlots[0].TimestampTicks;

            for (int i = 1; i < jet.targetSlots.Length; i++)
            {
                if (jet.targetSlots[i].TimestampTicks < oldestTimestamp)
                {
                    oldestTimestamp = jet.targetSlots[i].TimestampTicks;
                    oldestIndex = i;
                }
            }

            return oldestIndex;
        }
    }
}
