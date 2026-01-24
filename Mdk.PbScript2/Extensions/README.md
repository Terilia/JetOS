# Extensions

This folder contains C# extension methods used throughout JetOS.

## Files

### RandomExtensions.cs
Provides extension methods for the `System.Random` class.

- `NextFloat(float min, float max)` - Returns a random float within the specified range.

## Usage Notes

Unlike other files in the project, extension classes must be defined **outside** the `partial class Program` block because C# requires extension methods to be in static, non-nested classes.

```csharp
namespace IngameScript
{
    public static class RandomExtensions
    {
        public static float NextFloat(this Random random, float min, float max)
        {
            return (float)(random.NextDouble() * (max - min) + min);
        }
    }
}
```

This is the only exception to the standard file template pattern used elsewhere in the codebase.
