# Optimization: Pre-compute Horizon Sprite Rotation

## Problem

`DrawArtificialHorizon()` builds a list of sprites, then rotates every sprite by the roll angle:

```csharp
for (int s = 0; s < sprites.Count; s++)
{
    MySprite sprite = sprites[s];
    Vector2 pos = sprite.Position ?? Vector2.Zero;
    Vector2 offset = pos - V2(centerX, centerY);

    Vector2 rotated = V2(
        offset.X * cosRoll - offset.Y * sinRoll,
        offset.X * sinRoll + offset.Y * cosRoll
    );

    sprite.Position = rotated + V2(centerX, centerY);
    // ...
    sprites[s] = sprite;  // struct copy back
    frame.Add(sprite);
}
```

Each sprite requires: 1 Vector2 subtract, 4 multiplies, 2 add/subtract, 1 Vector2 add, plus a struct copy. With ~20 sprites (pitch lines + labels + horizon), that's ~20 * ~10 operations = ~200 floating-point operations.

## Proposed Solution

Compute the rotated positions directly when creating the sprites, instead of creating unrotated sprites and then rotating them in a second pass:

```csharp
// Helper: rotate a point around center
Vector2 Rot(float x, float y, float cx, float cy, float cos, float sin)
{
    float dx = x - cx, dy = y - cy;
    return V2(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
}

// In the pitch line loop:
Vector2 leftPos = Rot(centerX * 0.75f, markerY, centerX, centerY, cosRoll, sinRoll);
frame.Add(SpriteHelpers.FBx(leftPos.X, leftPos.Y, lineWidth, lineThickness, lineColor));
```

This eliminates:
- The intermediate `_horizonSprites` list and its Clear/Add operations
- The second pass over all sprites
- The struct copies (`sprites[s] = sprite`)

## Impact

- **Instructions saved**: Eliminates ~60 list operations (Clear + Add + indexed write for ~20 sprites)
- **Memory**: Eliminates the `_horizonSprites` list allocation
- **Risk**: Low - the rotation math is identical, just applied at creation time
- **Files affected**: HorizonRenderer.cs
