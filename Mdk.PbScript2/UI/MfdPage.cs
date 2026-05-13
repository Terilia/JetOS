using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // A renderable MFD page. One subclass per logical screen (menu, terrain map,
        // weapon panel, grid status). UIController renders the chrome around it and
        // dispatches content/sidebar/menu rendering through the virtuals here.
        public abstract class MfdPage
        {
            // Header text shown on the right side of the chrome (e.g. "MFD-1", "TERRAIN").
            public virtual string HeaderRight => "";

            // Smaller chrome for dense non-interactive status pages.
            public virtual bool CompactChrome => false;

            // Section title drawn between the breadcrumb and the content.
            // Empty string skips the section title entirely.
            public virtual string Title => "";

            // Show the "1 UP 2 DN..." nav strip in the footer.
            // Off for non-interactive surfaces (grid, weapons) — on for menu pages.
            public virtual bool ShowFooterNav => false;

            // Show the SYSTEM MENU > MODULE breadcrumb under the header.
            public virtual bool ShowBreadcrumb => false;

            // Breadcrumb path (only consulted if ShowBreadcrumb). Last segment is bright.
            public virtual string BreadcrumbPath => "";

            // Render a menu list in the content area. When false, RenderContent is called instead.
            public virtual bool HasMenu => false;

            // Used when HasMenu is true.
            public virtual string[] MenuItems => null;

            // Module menus pack rows tighter than the main menu so longer option lists fit.
            public virtual bool CompactRows => false;

            // Optional module-owned drawing that sits inside the normal menu column after
            // the shared menu renderer has drawn the selectable rows.
            public virtual void RenderMenuSupplement(RectangleF menuArea,
                Vector2 surfaceSize, int selectedIndex) { }

            // Reserve a sidebar column on the right (currently only the main menu).
            public virtual bool HasSidebar => false;

            // Optional footer right-text (replaces the corporate watermark).
            // Use this to surface module hotkey hints. Empty leaves the watermark in place.
            public virtual string FooterRight => "";

            // Free-form content rendering (called when HasMenu is false, or alongside the menu).
            // contentArea is the inner rect after chrome/breadcrumb/title; surfaceSize is the
            // full surface dimensions (renderers using absolute coords need this for centering).
            public virtual void RenderContent(RectangleF contentArea, Vector2 surfaceSize) { }

            // Sidebar rendering (called when HasSidebar is true). Receives the sidebar rect.
            public virtual void RenderSidebar(RectangleF area) { }
        }
    }
}
