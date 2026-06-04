using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Base class for every HQ station program (Tactical, Roster, Orders, Map, Config).
        // Ported from the jet's Modules/ProgramModule.cs — same contract so the menu/render
        // pipeline is identical.
        public abstract class ProgramModule
        {
            protected Program ParentProgram;
            public ProgramModule(Program program)
            {
                ParentProgram = program;
            }
            public string name = "";
            public abstract string[] GetOptions();
            public abstract void ExecuteOption(int index);
            // Override to render a custom MFD page instead of the default menu list.
            public virtual MfdPage GetPage() => new MenuMfdPage(this);
            public virtual void HandleSpecialFunction(int key) { }
            public virtual void Tick() { }
            public virtual string GetHotkeys() { return ""; }
            // Return true if the module handles navigation internally.
            public virtual bool HandleNavigation(bool isUp) { return false; }
            // Return true if the module handles back internally (else default: exit module).
            public virtual bool HandleBack() { return false; }
        }
    }
}
