# Optimization: Radar Block Discovery Loop Iterates to 99

## Problem

`RadarControlModule` constructor searches for AI block pairs by name from index 1 to 99:

```csharp
for (int i = 1; i <= 99; i++)
{
    string flightName = i == 1 ? "AI Flight" : $"AI Flight {i}";
    string combatName = i == 1 ? "AI Combat" : $"AI Combat {i}";

    var flightBlock = program.GridTerminalSystem.GetBlockWithName(flightName) as IMyFlightMovementBlock;
    var combatBlock = program.GridTerminalSystem.GetBlockWithName(combatName) as IMyOffensiveCombatBlock;
    // ...
}
```

That's up to 198 `GetBlockWithName()` calls (99 flight + 99 combat) during initialization. Most jets will have 2-5 AI pairs. After the first miss, all subsequent iterations are wasted.

## Proposed Solution

Break out of the loop when a pair is not found (assuming sequential numbering):

```csharp
for (int i = 1; i <= 99; i++)
{
    string flightName = i == 1 ? "AI Flight" : $"AI Flight {i}";
    string combatName = i == 1 ? "AI Combat" : $"AI Combat {i}";

    var flightBlock = program.GridTerminalSystem.GetBlockWithName(flightName) as IMyFlightMovementBlock;
    var combatBlock = program.GridTerminalSystem.GetBlockWithName(combatName) as IMyOffensiveCombatBlock;

    if (flightBlock == null || combatBlock == null)
        break;  // Stop at first gap - pairs must be sequential

    detectedAIPairs.Add(new AIBlockPair(flightBlock, combatBlock, i));
    // ...
}
```

If non-sequential numbering must be supported (e.g., AI Flight 1, AI Flight 3 with no 2), add a `missCount` that allows a small gap before stopping:

```csharp
int missCount = 0;
for (int i = 1; i <= 99 && missCount < 3; i++)
{
    // ...
    if (flightBlock == null || combatBlock == null) { missCount++; continue; }
    missCount = 0;
    // ...
}
```

## Impact

- **Instruction savings**: ~180 fewer `GetBlockWithName()` calls during init (for a typical 3-pair setup)
- **Risk**: Very low - only affects constructor (runs once). If pairs are non-sequential, the gap-tolerance variant handles it.
- **Files affected**: RadarControlModule.cs
