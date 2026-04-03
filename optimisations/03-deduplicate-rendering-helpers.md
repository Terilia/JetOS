# Optimization: Deduplicate Rect/Txt Sprite Helper Methods

## Problem

Four classes independently define identical sprite helper methods for drawing rectangles and text:

| Class | Rect method | Text method |
|---|---|---|
| `UIController` | `R(ref frame, cx, cy, w, h, c)` | `T(ref frame, d, x, y, s, c, a)` |
| `MFDFrame` | `Rect(frame, cx, cy, w, h, c)` | `Txt(frame, d, x, y, s, c, a)` |
| `StatusPanelRenderer` | `Rect(frame, cx, cy, w, h, c)` | `Txt(frame, d, x, y, s, c, a)` |
| `GridVisualization` | Uses `SpriteHelpers.Bx/Tt` | Uses `SpriteHelpers.Bx/Tt` |
| `SpriteHelpers` | `Bx(frame, x, y, w, h, c)` | `Tt(frame, d, x, y, s, c, a)` |

Every single one does the exact same thing: creates a `new MySprite { ... }` and adds it to the frame.

## Current State

5 independent implementations of the same sprite creation code. Some take `ref MySpriteDrawFrame`, some take it by value. Some use `MFDTheme.FONT`, some are hardcoded.

## Proposed Solution

Standardize on `SpriteHelpers.Bx()` and `SpriteHelpers.Tt()` everywhere. Remove the private `R`/`T`/`Rect`/`Txt` methods from `UIController`, `MFDFrame`, and `StatusPanelRenderer`.

The `ref MySpriteDrawFrame` variants in UIController can be changed to pass by value (MySpriteDrawFrame is a struct but since it wraps a reference-type list, passing by value is fine and is what all other code does).

## Impact

- **Lines saved**: ~30 lines of duplicate method definitions
- **Consistency**: Single point of change for sprite creation behavior
- **Risk**: Very low - pure refactor, identical behavior
- **Files affected**: UIController.cs, MFDFrame.cs, StatusPanelRenderer.cs
