using System;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Default page used when a module doesn't override GetPage(). Renders the module's
        // menu options as a vertical list with breadcrumb + footer hints. The main menu uses
        // this with module=null. Ported from the jet's UI/MenuMfdPage.cs.
        class MenuMfdPage : MfdPage
        {
            private readonly ProgramModule _module;
            private readonly string _title;
            private readonly string[] _items;
            private readonly bool _showSidebar;
            private readonly Action<RectangleF> _sidebarRenderer;

            public MenuMfdPage(ProgramModule module)
            {
                _module = module;
                _title = module.name.ToUpper();
                _items = null;
                _showSidebar = false;
            }

            public MenuMfdPage(string title, string[] items, bool showSidebar,
                Action<RectangleF> sidebarRenderer = null)
            {
                _module = null;
                _title = title;
                _items = items;
                _showSidebar = showSidebar;
                _sidebarRenderer = sidebarRenderer;
            }

            public override void RenderSidebar(RectangleF area)
            {
                if (_sidebarRenderer != null) _sidebarRenderer(area);
                else SystemManager.RenderDefaultSidebar(area);
            }

            public override string Title => _module != null ? "" : _title;
            public override bool ShowFooterNav => true;
            public override bool ShowBreadcrumb => _module != null;
            public override string BreadcrumbPath => _module != null ? _module.name : "";
            public override bool HasMenu => true;
            public override string[] MenuItems => _items != null ? _items : _module.GetOptions();
            public override bool HasSidebar => _showSidebar || _module != null;
            public override bool CompactRows => _module != null;
            public override string HeaderRight => "C2";
            public override string FooterRight => _module != null ? _module.GetHotkeys() : "";
        }
    }
}
