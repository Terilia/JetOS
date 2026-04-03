# Optimization: Reuse GridVisualization Intermediate Arrays

## Problem

`GridVisualization.RunRebuildPhase()` phase 2 allocates new 2D arrays every rebuild:

```csharp
case 2:
    gridOcc = new bool[gridW, gridH];
    gridIntegrity = new float[gridW, gridH];
    gridFunctional = new bool[gridW, gridH];
```

With a typical grid size of 30x10 = 300 cells, that's 3 array allocations of 300 elements each, every 60 ticks (when block count changes) or every 300 ticks (damage refresh).

The arrays are set to `null` after phase 3 to "free" them, but the GC still has to collect them.

## Proposed Solution

Keep the arrays allocated and only reallocate when grid dimensions change:

```csharp
static int allocW = 0, allocH = 0;

static void EnsureArraySize(int w, int h)
{
    if (w == allocW && h == allocH && gridOcc != null) return;
    gridOcc = new bool[w, h];
    gridIntegrity = new float[w, h];
    gridFunctional = new bool[w, h];
    allocW = w;
    allocH = h;
}

// In phase 2, replace allocation with:
EnsureArraySize(gridW, gridH);
// Clear arrays instead of allocating new ones:
Array.Clear(gridOcc, 0, gridOcc.Length);
// ... or use nested loop to reset
```

Don't null out the arrays in phase 3. They stay allocated between rebuilds.

## Impact

- **GC reduction**: Eliminates 3 array allocations every rebuild cycle
- **Risk**: Very low - arrays are sized to grid dimensions which rarely change
- **Files affected**: GridVisualization.cs
