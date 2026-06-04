using Sandbox.ModAPI.Ingame;
using System;

namespace IngameScript
{
    // ============================================================================
    //  JetOS HQ  —  Station Operating System
    // ----------------------------------------------------------------------------
    //  A standalone MDK2 programmable-block project built on the same module/menu
    //  framework as the jet (Mdk.PbScript2), adapted into its own compilation unit
    //  with its own ~100,000-char budget. Build (output=auto) deploys to
    //  %APPDATA%/SpaceEngineers/IngameScripts/local/JetOS_HQ.
    //
    //  It is the fleet's sensor-fusion + command center: a background DataLink V2
    //  service consumes every jet's status + relayed contacts, fuses them, and
    //  broadcasts a STATION feed; the operator drives station "programs" (Tactical,
    //  Wing, Orders, Map, Config) on a single navigable MFD.
    //
    //  Required blocks (see docs/datalink-v2.md):
    //    * "HQ MFD"          — a text-surface provider for the main MFD
    //    * an IMyRadioAntenna — IGC reach to the fleet (== antenna range)
    //  Optional:
    //    * "HQ Command Seat" — toolbar nav (numpad 1-9) + mouse/WASD map panning
    //
    //  Protocol reference: docs/datalink-v2.md
    // ============================================================================
    partial class Program : MyGridProgram
    {
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
                // Recover from a missing/renamed block by re-gathering hardware.
                Echo("NRE " + e.Message);
                SystemManager.Initialize(this);
            }
            catch (Exception e)
            {
                Echo("ERR " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
