# Optimization: Deduplicate MFD Chrome Rendering

## Problem

MFD chrome (header, footer, corners, border) is rendered by two independent implementations:

1. **`MFDFrame.DrawChrome()`** - used by Surface 1 (GridVisualization) and Surface 2 (WeaponScreen)
2. **`UIController` methods** - `DrawHeader()`, `DrawFooter()`, `DrawCornerBrackets()`, `DrawScreenBorder()` - used by Surface 0 (main menu)

Both draw the exact same visual elements: NYINAH CORP header, gold accent line, corner brackets, screen border, footer with nav hints and corp watermark. But they're maintained as separate code.

## Current Duplication

```
UIController.DrawHeader()         ≈ MFDFrame.DrawChrome() header section
UIController.DrawCornerBrackets() ≈ MFDFrame.DrawChrome() corner section
UIController.DrawScreenBorder()   ≈ MFDFrame.DrawChrome() border section
UIController.DrawFooter()         ≈ MFDFrame.DrawChrome() footer section
```

Both compute the same layout constants independently:
```csharp
// UIController
HEADER_H = SH * 0.069f;
FOOTER_H = SH * 0.054f;
CORNER_LEN = Mn(SW, SH) * 0.03f;

// MFDFrame
float headerH = sh * 0.069f;
float footerH = sh * 0.054f;
float cornerLen = Mn(sw, sh) * 0.03f;
```

## Proposed Solution

Have `UIController.RenderMainScreen()` use `MFDFrame.DrawChrome()` for the shared chrome, then only draw the content-specific parts (menu rows, sidebar, breadcrumb). This removes ~80 lines from UIController.

The main difference is UIController passes `ref MySpriteDrawFrame` while MFDFrame passes by value. Since `MySpriteDrawFrame` is a disposable wrapper around a list, passing by value works fine (both reference the same underlying sprite list).

## Impact

- **Lines saved**: ~80 lines from UIController
- **Consistency**: Single source of truth for MFD chrome styling
- **Risk**: Low - visual output is identical
- **Files affected**: UIController.cs
