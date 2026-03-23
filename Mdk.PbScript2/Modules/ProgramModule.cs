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
            public string name = "program";
            public abstract string[] GetOptions();
            public abstract void ExecuteOption(int index);
            // Override to take over the main MFD screen with custom rendering
            public virtual bool HasCustomScreen => false;
            public virtual void RenderCustomScreen(MySpriteDrawFrame frame, RectangleF area) { }
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
