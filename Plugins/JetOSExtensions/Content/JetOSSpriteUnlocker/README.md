# testmod

Local working name for the JetOS sprite mod. Custom LCD sprites referenced
from the script by their `SubtypeId` (e.g.
`MySprite.CreateSprite("JetOS_FPM", ...)`). Without this mod loaded in the
world, those sprite names won't resolve and SE renders the fallback (usually
a magenta box).

## Layout

```
Mod/testmod/
├── Data/
│   └── TransparentMaterials.sbc   — sprite definitions (one <TransparentMaterial> per sprite)
└── Textures/
    └── Sprites/
        └── JetOS_FPM.dds          — author here, 256x256, grayscale-alpha
```

## Authoring conventions

- **Resolution:** 256x256 per sprite.
- **Color:** white on transparent (alpha mask). The script tints via the `Color`
  parameter on `MySprite.CreateSprite`, so the same sprite serves all theme
  variants (normal, warning, lock, etc.).
- **Format:** DDS, BC4 (single-channel alpha) or BC7 sRGB. PNG works for
  one-off testing but DDS is preferred for compression and load performance.
- **Padding:** center the glyph in the 256x256 canvas with transparent
  margin. Render size in-script picks the bounding box on-screen.

## Local deploy (testing)

SE loads mods from `%APPDATA%\SpaceEngineers\Mods\<ModName>\`. Easiest is a
directory junction so edits in this repo show up live in-game:

```powershell
# from an elevated PowerShell:
New-Item -ItemType Junction `
  -Path "$env:APPDATA\SpaceEngineers\Mods\testmod" `
  -Target "$PWD\Mod\testmod"
```

Then enable **testmod** in the world's Mod list and reload the world.

## Workshop publish

Use the in-game **Mods** menu → publish from the local mod entry. SE generates
`modinfo.sbmi` and uploads the folder. Don't hand-edit `modinfo.sbmi`.

## Current sprites

| SubtypeId   | Replaces                                              | Notes                          |
|-------------|-------------------------------------------------------|--------------------------------|
| `JetOS_FPM` | `HorizonRenderer.DrawFlightPathMarker` (4 primitives) | Counter-rotated by roll in script. |
