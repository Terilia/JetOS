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
            public virtual void HandleSpecialFunction(int key) { }
            public virtual void Tick() { }
            public int currentTick = 0;
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
