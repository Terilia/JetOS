# Optimization: Reduce Enemy List Traversals

## Problem

`Jet.GetSelectedEnemy()` does a linear scan of `enemyList` every call. It's called from:

1. `HUDModule.RenderHUD()` -> passed to targeting renderers
2. `WeaponScreenRenderer.RenderWeaponScreen()` -> checks and renders selected target
3. `AirtoAir.Tick()` -> `HasSelectedEnemy()` which calls `GetSelectedEnemy()`
4. `SystemManager.FlipGPS()` -> calls it to find current selection
5. `RadarControlModule.Tick()` -> checks for IsTrackLocked match
6. `RadarRenderer.DrawRadarMinimap()` -> checks each contact against selection

That's 6+ linear scans of the enemy list per tick. Each scan is O(n) where n = number of contacts.

Additionally, `SortEnemiesByDistance()` is called from both `GetClosestNEnemies()` and `GetEnemiesSortedByDistance()`, re-sorting the entire list each time.

## Current State

```csharp
public EnemyContact? GetSelectedEnemy()
{
    // Scan 1: by EntityId
    for (int i = 0; i < enemyList.Count; i++)
        if (enemyList[i].EntityId == selectedEnemyEntityId)
            return enemyList[i];

    // Scan 2: by Name
    for (int i = 0; i < enemyList.Count; i++)
        if (enemyList[i].Name == selectedEnemyName)
            return enemyList[i];

    return null;
}
```

## Proposed Solution

### 1. Cache selected enemy per tick
Add a `cachedSelectedEnemy` field to `Jet`, computed once at the start of the tick:

```csharp
private EnemyContact? _cachedSelected;
private long _selectedCacheTick = -1;

public EnemyContact? GetSelectedEnemy()
{
    if (_selectedCacheTick == GameTicks) return _cachedSelected;
    _selectedCacheTick = GameTicks;
    _cachedSelected = FindSelectedEnemy(); // the current linear scan
    return _cachedSelected;
}
```

### 2. Use EntityId dictionary for O(1) lookup
The `_entityIdIndex` dictionary already exists but isn't used by `GetSelectedEnemy()`:

```csharp
if (selectedEnemyEntityId != 0)
{
    int idx;
    if (_entityIdIndex.TryGetValue(selectedEnemyEntityId, out idx))
        return enemyList[idx];
}
```

### 3. Cache sorted enemy list per tick
```csharp
private long _sortCacheTick = -1;
private List<KeyValuePair<double, EnemyContact>> SortEnemiesByDistance()
{
    if (_sortCacheTick == GameTicks) return _sortBuffer;
    _sortCacheTick = GameTicks;
    // ... existing sort code ...
}
```

## Impact

- **Instruction savings**: Eliminates 5+ redundant linear scans per tick, plus 1 redundant sort
- **Risk**: Very low - caching within a single tick is always safe
- **Files affected**: Jet.cs
