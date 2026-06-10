using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
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
            // Default returns a MenuMfdPage that wraps this module's GetOptions/name/GetHotkeys.
            // Cached — GetPage is called every render tick.
            MfdPage _defaultPage;
            public virtual MfdPage GetPage() => _defaultPage ?? (_defaultPage = new MenuMfdPage(this));
            public virtual void HandleSpecialFunction(int key) { }
            public virtual void Tick() { }
            public virtual string GetHotkeys()
            {
                return "";
            }
            // Return true if module handles navigation internally, false to use default
            public virtual bool HandleNavigation(bool isUp)
            {
                return false; // Default: don't override navigation
            }
            // Return true if module handles back button internally, false to use default (exit module)
            public virtual bool HandleBack()
            {
                return false; // Default: exit module
            }
        }
    }
}
