# UI

This folder contains the MFD rendering pipeline for the three cockpit LCD surfaces (main menu, status grid, weapon screen). The HUD glass itself is rendered from `HUD/` — see those renderers for the in-flight overlay.

## Files

### MfdPage.cs
Abstract base class for everything that occupies the main MFD surface.

A page exposes:
- chrome metadata (`HeaderRight`, `FooterRight`, `ShowBreadcrumb`, `BreadcrumbPath`, `Title`, `HasSidebar`, `HasMenu`, `CompactRows`)
- `MenuItems` (when `HasMenu` is true)
- `RenderContent(frame, area, surfaceSize)` for custom pages (terrain, weapons, grid)
- `RenderSidebar(frame, area)` for the right-column status panel

Implementations: `MenuMfdPage` (default for any module), `GridMfdPage` (status grid on surface 1), `WeaponMfdPage` (weapons on surface 2). Modules return their own `MfdPage` from `ProgramModule.GetPage()` to take over the main surface — `TerrainModule` does this for the terrain map.

### UIController.cs
Single-entry-point renderer for the main MFD surface. Draws chrome → breadcrumb → section title → (menu list | custom content) → sidebar → screen border, then runs the shader-style transition replay if the page just changed.

Holds the `MFDTheme` palette + sprite/alignment constants used by every renderer.

### MFDFrame.cs
Static helpers for the shared NYINAH CORP chrome (header, footer, corner brackets, screen border). All three MFD surfaces call `MFDFrame.DrawChrome(...)` so they look like one cohesive panel suite.

### SpriteBus.cs
Central chokepoint for sprite emission. Every `MFDFrame.Rect` / `SpriteHelpers.*` / `Sq` / `SqT` / `Tx` call routes through `SpriteBus.Add` rather than `frame.Add` directly. The bus optionally tees each sprite into a capture list — this is what powers the page-transition replay (`UIController.ReplayWithTransform`). Direct `frame.Add` calls bypass the bus and are not captured (used by `HorizonRenderer` and `GridVisualization`'s cached outline list, which never participate in transitions).

Lifecycle: `SpriteBus.Begin(frame, capture)` → emit sprites → `SpriteBus.End()` → `frame.Dispose()`.

### GridMfdPage.cs / GridVisualization.cs
Surface 1. Ship outline (rebuilt across 3 ticks when block count changes), fuel bar, G-meter, flight readouts. The sprite list for the outline is cached and re-added each frame to avoid re-walking the grid.

### WeaponMfdPage.cs
Surface 2. Delegates to `HUDModule.RenderWeaponContent` which lives in `HUD/WeaponScreenRenderer.cs`.

### MenuMfdPage.cs
Default menu page — wraps a module's `name`, `GetOptions()`, `GetHotkeys()` into the chrome contract.

### StatusPanelRenderer.cs
The right-column sidebar shared across the menu page and most module pages: H2 fuel, battery, dual-engine thrust card, terrain minimap inset.

## Theme — NYINAH CORP MFD

Static `MFDTheme` (in `UIController.cs`) holds the dark-green-phosphor corporate palette:
- Background `(5,8,5)`, panel `(8,14,8)`, header `(10,18,10)`
- Normal text `(90,154,90)`, accent `(64,160,64)`, corp gold `(138,122,80)`
- Borders, dividers, status colors all named so renderers don't hardcode RGB

Sprite type / alignment shortcuts (`TX`, `TT`, `AC`, `AL`, `AR`) live there too — every renderer uses them.

## Sprite Mod (JetOS-Sprites)

Most icons, glyphs, and complex shapes are pre-baked sprites shipped by the project's mod (`Mod/testmod/Data/LCDTextures.sbc`, sprite names `JetOS_*`, declared as `const string TEX_*` in `Utilities/Shortcuts.cs`). One textured sprite replaces what used to be N filled rects + arcs (e.g., `JetOS_BankArc` was 36 line segments, `JetOS_Boresight` was 4 lines, `JetOS_TargetBracket` was 8). When adding new chrome, look first for an existing `TEX_*` constant before composing from primitives.

## Performance

- Outline helpers (`SpriteHelpers.DrawCircleOutline`, `DrawRectangleOutline`) collapse to a single hollow sprite (`CircleHollow` / `SquareHollow`) when the geometry is square-ish; stretched rects fall back to four 1px filled rects so the border stays uniform.
- Animated values use `AnimatedValue` (in `Anim.cs`) so target updates ease to a new value over wall-clock seconds rather than snapping.
- Blink/flash cadence uses `Anim.Blink(period)` (wall-clock) instead of tick counters, so cadence stays honest under sim hitches.
- The grid outline rebuild is staggered across 3 ticks and re-uses a cached sprite list until block count or damage changes.
- Page transitions cap at 85% progress — the last few ticks of replay would be invisible (alpha ≈ 0.003) and waste hundreds of sprite ops.
