using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // A renderable MFD page. One subclass per logical screen (menu, tactical map, …).
        // UIController renders the chrome around it and dispatches content/sidebar/menu
        // rendering through the virtuals here. Ported from the jet's UI/MfdPage.cs.
        public abstract class MfdPage
        {
            public virtual string HeaderRight => "";
            public virtual bool CompactChrome => false;
            public virtual string Title => "";
            public virtual bool ShowFooterNav => false;
            public virtual bool ShowBreadcrumb => false;
            public virtual string BreadcrumbPath => "";
            public virtual bool HasMenu => false;
            public virtual string[] MenuItems => null;
            public virtual bool CompactRows => false;
            public virtual void RenderMenuSupplement(RectangleF menuArea,
                Vector2 surfaceSize, int selectedIndex) { }
            public virtual bool HasSidebar => false;
            public virtual string FooterRight => "";
            // Free-form content rendering (called when HasMenu is false, or alongside the menu).
            public virtual void RenderContent(RectangleF contentArea, Vector2 surfaceSize) { }
            public virtual void RenderSidebar(RectangleF area) { }
        }
    }
}
