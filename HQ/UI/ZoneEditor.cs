using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // The zone authoring state machine. Runs every ZONE-mode tick (after MouseCursor.Tick,
        // before Render). It turns cursor clicks into geometry: clicks on the MAP place polygon
        // vertices / a circle center+radius / select a saved zone; clicks on the MFD palette invoke
        // tool buttons (registered by ZonesPage each render). Projection is borrowed from MapView's
        // promoted view frame (ScreenToWorld / WorldToScreen). Drawing of committed zones lives in
        // MapView.DrawZones; this class only renders the in-progress draft + selection overlay.
        static class ZoneEditor
        {
            // Palette action codes (shared with ZonesPage).
            public const int A_NEWPOLY = 0, A_NEWCIRCLE = 1, A_KIND = 2,
                             A_CLOSE = 3, A_UNDO = 4, A_DELETE = 5, A_SAVE = 6;
            // Zone-list row select uses action = 100 + index.

            enum EState { Idle, PlacingPoly, PlacingCircle, Selected }

            static EState _state = EState.Idle;
            static Zone _draft;
            static Zone _selected;
            static ZoneKind _kind = ZoneKind.Enemy;
            static Vector3D _circleCenter;
            static bool _circleHasCenter;

            // Immediate-mode palette buttons: ZonesPage lays them out + registers rects each render;
            // we hit-test them on the next tick's left-click (1-frame lag — harmless).
            struct PButton { public RectangleF Rect; public int Action; }
            static readonly List<PButton> _buttons = new List<PButton>();

            public static ZoneKind Kind => _kind;
            public static bool Placing => _state == EState.PlacingPoly || _state == EState.PlacingCircle;
            public static bool IsSelected(Zone z) => _selected == z;

            public static void ClearButtons() => _buttons.Clear();
            public static void AddButton(RectangleF r, int action) =>
                _buttons.Add(new PButton { Rect = r, Action = action });

            // Cancel any in-progress draft (e.g. on ZONE-mode exit).
            public static void Reset()
            {
                _draft = null;
                _selected = null;
                _state = EState.Idle;
                _circleHasCenter = false;
            }

            public static void Tick()
            {
                if (!MouseCursor.Visible) return;

                // (zoom / elevation / edge-pan handled globally by MapView.UpdateInput)
                // Require a published view frame before any cursor->world projection (avoids a
                // garbage coord if a click lands before the map's first render this session).
                bool onMap = Canvas.OnRight(MouseCursor.X) && MapView.ViewReady;
                float lx = MouseCursor.X - Canvas.LW;
                float ly = Cl(MouseCursor.Y, 0f, Canvas.RH);

                // Live circle radius preview tracks the cursor between center + finalize clicks.
                if (_state == EState.PlacingCircle && _circleHasCenter && onMap)
                    _draft.Radius = VDi(_circleCenter, MapView.ScreenToWorld(lx, ly));

                if (MouseCursor.PrimaryClick)
                {
                    if (onMap) MapClick(MapView.ScreenToWorld(lx, ly), V2(lx, ly));
                    else { int a = HitButton(MouseCursor.X, Cl(MouseCursor.Y, 0f, Canvas.LH)); if (a >= 0) DoAction(a); }
                }
                if (MouseCursor.SecondaryClick) Undo();
            }

            // ── Palette actions ──
            public static void DoAction(int a)
            {
                switch (a)
                {
                    case A_NEWPOLY: NewPoly(); break;
                    case A_NEWCIRCLE: NewCircle(); break;
                    case A_KIND: CycleKind(); break;
                    case A_CLOSE: Close(); break;
                    case A_UNDO: Undo(); break;
                    case A_DELETE: DeleteSelected(); break;
                    case A_SAVE: ZoneStore.Persist(); break;
                    default: if (a >= 100) SelectByIndex(a - 100); break;
                }
            }

            static void NewPoly()
            {
                if (ZoneStore.Full) return;
                _draft = new Zone { Shape = ZoneShape.Polygon, Kind = _kind };
                _selected = null;
                _state = EState.PlacingPoly;
            }

            static void NewCircle()
            {
                if (ZoneStore.Full) return;
                _draft = new Zone { Shape = ZoneShape.Circle, Kind = _kind };
                _selected = null;
                _circleHasCenter = false;
                _state = EState.PlacingCircle;
            }

            static void CycleKind()
            {
                _kind = (ZoneKind)(((int)_kind + 1) % 5);
                if (_draft != null) _draft.Kind = _kind;
                if (_selected != null) { _selected.Kind = _kind; ZoneStore.Persist(); }
            }

            static void Close()
            {
                if (_state == EState.PlacingPoly && _draft != null && _draft.Verts.Count >= 3)
                    ZoneStore.Add(_draft);
                _draft = null; _state = EState.Idle; _circleHasCenter = false;
            }

            static void FinalizeCircle()
            {
                if (_draft != null && _draft.Radius > 1) ZoneStore.Add(_draft);
                _draft = null; _state = EState.Idle; _circleHasCenter = false;
            }

            public static void Undo()
            {
                if (_state == EState.PlacingPoly && _draft != null && _draft.Verts.Count > 0)
                    _draft.Verts.RemoveAt(_draft.Verts.Count - 1);
                else if (_state != EState.Idle)
                { _draft = null; _state = EState.Idle; _circleHasCenter = false; }
            }

            static void DeleteSelected()
            {
                if (_selected != null) { ZoneStore.Remove(_selected); _selected = null; _state = EState.Idle; }
            }

            static void SelectByIndex(int i)
            {
                if (i >= 0 && i < ZoneStore.Zones.Count)
                { _selected = ZoneStore.Zones[i]; _kind = _selected.Kind; _state = EState.Selected; }
            }

            // ── Map clicks ──
            static void MapClick(Vector3D world, Vector2 screen)
            {
                if (_state == EState.PlacingPoly)
                {
                    if (_draft.Verts.Count >= 3 && (MapView.WorldToScreen(_draft.Verts[0]) - screen).Length() < 10f)
                    { Close(); return; }
                    if (_draft.Verts.Count < ZoneStore.MAX_VERTS) _draft.Verts.Add(world);
                }
                else if (_state == EState.PlacingCircle)
                {
                    if (!_circleHasCenter)
                    {
                        _circleCenter = world; _circleHasCenter = true;
                        _draft.Verts.Clear(); _draft.Verts.Add(world); _draft.Radius = 0;
                    }
                    else FinalizeCircle();
                }
                else
                {
                    _selected = HitScreen(screen);
                    _state = _selected != null ? EState.Selected : EState.Idle;
                }
            }

            static int HitButton(float vx, float vy)
            {
                for (int i = 0; i < _buttons.Count; i++)
                {
                    RectangleF r = _buttons[i].Rect;
                    if (vx >= r.Position.X && vx <= r.Position.X + r.Width
                        && vy >= r.Position.Y && vy <= r.Position.Y + r.Height)
                        return _buttons[i].Action;
                }
                return -1;
            }

            // Screen-space hit test against saved zones (topmost first).
            static Zone HitScreen(Vector2 p)
            {
                for (int i = ZoneStore.Zones.Count - 1; i >= 0; i--)
                {
                    Zone z = ZoneStore.Zones[i];
                    if (z.Shape == ZoneShape.Circle)
                    {
                        if ((MapView.WorldToScreen(z.Center) - p).Length() <= (float)(z.Radius / MapView.MetersPerPixel))
                            return z;
                    }
                    else if (PointInPoly(z, p)) return z;
                }
                return null;
            }

            static bool PointInPoly(Zone z, Vector2 p)
            {
                int n = z.Verts.Count;
                if (n < 3) return false;
                bool inside = false;
                Vector2 pj = MapView.WorldToScreen(z.Verts[n - 1]);
                for (int i = 0; i < n; i++)
                {
                    Vector2 pi = MapView.WorldToScreen(z.Verts[i]);
                    if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                        (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                        inside = !inside;
                    pj = pi;
                }
                return inside;
            }

            // ── Status text for the palette ──
            public static string StatusLine()
            {
                switch (_state)
                {
                    case EState.PlacingPoly: return "PLACE POLY  " + (_draft != null ? _draft.Verts.Count : 0) + "pt";
                    case EState.PlacingCircle: return _circleHasCenter ? "SET RADIUS" : "PLACE CENTER";
                    case EState.Selected: return "SEL " + (_selected != null ? _selected.Name : "");
                    default: return "IDLE";
                }
            }

            public static string KindName(ZoneKind k)
            {
                switch (k)
                {
                    case ZoneKind.Enemy: return "ENEMY";
                    case ZoneKind.NoFly: return "NO-FLY";
                    case ZoneKind.SAM: return "SAM";
                    case ZoneKind.CAP: return "CAP";
                    case ZoneKind.Rally: return "RALLY";
                    default: return "?";
                }
            }

            // ── Overlay: in-progress draft + selection emphasis (drawn on the MAP, on top) ──
            public static void RenderOverlay(float k)
            {
                if (_selected != null) DrawOutline(_selected, MFDTheme.BRIGHT_TEXT, 2.4f * k, k);

                if (_draft == null) return;
                Color col = MapView.ZoneColor(_draft.Kind);
                Vector2 cur = V2(MouseCursor.X - Canvas.LW, Cl(MouseCursor.Y, 0f, Canvas.RH));

                if (_draft.Shape == ZoneShape.Circle)
                {
                    if (_circleHasCenter)
                    {
                        Vector2 cs = MapView.WorldToScreen(_circleCenter);
                        MapView.DrawRing(cs, (float)(_draft.Radius / MapView.MetersPerPixel), col);
                        SpriteHelpers.AddLineSprite(cs, cur, 1.4f * k, Cr(col, 0.6f));
                        SpriteHelpers.Sp(TEX_LOCK_DIAMOND, cs.X, cs.Y, 6f * k, 6f * k, col);
                    }
                }
                else
                {
                    int n = _draft.Verts.Count;
                    Vector2 prev = VZ2; bool has = false;
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 pp = MapView.WorldToScreen(_draft.Verts[i]);
                        SpriteHelpers.Sp(TEX_LOCK_DIAMOND, pp.X, pp.Y, 6f * k, 6f * k, col);
                        if (has) SpriteHelpers.AddLineSprite(prev, pp, 1.6f * k, col);
                        prev = pp; has = true;
                    }
                    if (has) SpriteHelpers.AddLineSprite(prev, cur, 1.4f * k, Cr(col, 0.6f));
                }
            }

            static void DrawOutline(Zone z, Color col, float thick, float k)
            {
                if (z.Shape == ZoneShape.Circle)
                {
                    MapView.DrawRing(MapView.WorldToScreen(z.Center), (float)(z.Radius / MapView.MetersPerPixel), col);
                    return;
                }
                int n = z.Verts.Count;
                if (n < 2) return;
                Vector2 prev = MapView.WorldToScreen(z.Verts[n - 1]);
                for (int i = 0; i < n; i++)
                {
                    Vector2 cur = MapView.WorldToScreen(z.Verts[i]);
                    SpriteHelpers.AddLineSprite(prev, cur, thick, col);
                    SpriteHelpers.Sp(TEX_LOCK_DIAMOND, cur.X, cur.Y, 5f * k, 5f * k, col);
                    prev = cur;
                }
            }

            static readonly Vector2 VZ2 = new Vector2(0f, 0f);
        }
    }
}
