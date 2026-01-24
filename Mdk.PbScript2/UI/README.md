# UI

This folder contains the user interface rendering system for JetOS LCD displays.

## Files

### UIController.cs
Main UI rendering controller for menu screens.

Key features:
- Manages two LCD surfaces (main menu + extra info panel)
- Sprite-based rendering using `IMyTextSurface.DrawFrame()`
- Menu navigation with highlight states
- Custom frame rendering for specialized displays (grid visualization, fuel ring)

Key methods:
- `RenderMainScreen()` - Draws the main menu with options
- `RenderExtraScreen()` - Draws the secondary information panel
- `RenderCustomFrame()` - Allows modules to render custom content
- `RenderCustomExtraFrame()` - Custom rendering on the extra screen

### UIElements.cs
Base classes for UI element hierarchy:

- `UIElement` (abstract) - Base class with position, size, color, visibility
- `UILabel` - Text rendering element with font support
- `UIContainer` - Container for grouping child elements
- `UISquare` - Simple colored rectangle element

## Color Scheme

The UI uses a military/avionics-inspired color palette:
- Primary: Light green (`Color.Lime`)
- Secondary: Dark green
- Emphasis: Yellow (warnings, highlights)
- Warning: Red (critical alerts)
- Background: Near-black with transparency

## Performance Considerations

The UI system uses sprite caching where possible to avoid recalculating static elements every tick. The grid visualization in particular caches the outline sprite list and only recalculates when block count changes.
