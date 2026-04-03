# Optimization: DisplayMenu Calls GetOptions Redundantly

## Problem

When navigating the menu, `GetOptions()` is called multiple times per tick:

1. **`NavigateDown()`** calls `currentModule.GetOptions().Length` just to bounds-check the menu index
2. **`DisplayMenu()`** calls `currentModule.GetOptions()` again to render the menu

```csharp
private static void NavigateDown()
{
    // ...
    int totalOptions = (
        currentModule == null
            ? mainMenuOptions.Length
            : currentModule.GetOptions().Length  // <-- allocates full array just for .Length
    );
    if (currentMenuIndex < totalOptions - 1)
        currentMenuIndex++;
}
```

Then immediately after in `Main()`:
```csharp
DisplayMenu();  // calls GetOptions() again to render
```

## Proposed Solution

Cache the options array in `SystemManager` and reuse it:

```csharp
private static string[] _currentOptions;

// Compute once at the start of DisplayMenu/input handling:
_currentOptions = currentModule == null ? mainMenuOptions : currentModule.GetOptions();

// NavigateDown uses cached length:
private static void NavigateDown()
{
    if (currentModule != null && currentModule.HandleNavigation(false))
        return;
    if (currentMenuIndex < _currentOptions.Length - 1)
        currentMenuIndex++;
}
```

Or more simply: pass `GetOptions()` result to the input handler instead of re-calling it.

## Impact

- **Allocation reduction**: 1 fewer array allocation per tick when navigating
- **Risk**: Very low
- **Files affected**: SystemManager.cs
