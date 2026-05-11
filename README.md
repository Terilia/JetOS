# JetOS

JetOS is a cockpit operating system for Space Engineers fighter craft. It runs as an
MDK2 programmable block script and brings the aircraft's flight display, tactical
picture, weapon management, radar control, terrain page, canard damping, and
multi-function displays into one integrated system.

The project is built for aircraft that use Space Engineers AI blocks, custom LCD
sprites, missile bays, rotor/hinge gun mounts, and a three-screen cockpit layout.

## Capabilities

- **Flight HUD** - artificial horizon, pitch ladder, flight path marker, heading,
  speed and altitude tapes, AoA, G-load, throttle state, lead pip, radar overlay.
- **Radar and RWR** - coordinated AI Flight/Combat block pairs, radar search and
  lock states, threat contacts, selected target tracking, and warning tones.
- **Weapons** - air-to-air bay selection, target data transfer, missile launch
  setup, weapon screen timelines, and rotor/hinge gun tracking with ballistic lead.
- **Aircraft control** - MIL/afterburner throttle gate, thrust handling, stabilizer
  trim, and optional canard AoA damping.
- **MFD system** - three coordinated text surfaces using the NYINAH CORP dark
  green phosphor theme, shared chrome, page transitions, grid/status view, weapon
  page, terrain page, and menu navigation.
- **Sprite mod support** - custom `JetOS_*` LCD sprites for HUD glyphs, radar
  contacts, bay icons, MFD corners, warning markers, and aircraft symbology.

## Requirements

- Space Engineers for PC
- [MDK2](https://github.com/malware-dev/MDK2)
- .NET Framework 4.8
- C# 6.0
- Optional: the included JetOS sprite mod in `Mod/testmod`
- Optional: a TerrainAPI world mod that exposes the `TerrainAPI` programmable
  block property used by the terrain page

## Build

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release
dotnet build Mdk.PbScript2.sln --configuration Debug
```

The Release build is packaged by MDK2 and deploys to:

```text
%APPDATA%\SpaceEngineers\IngameScripts\local\Mdk.PbScript2
```

There are no automated tests in this repository. Verification is done by building
successfully, loading the programmable block script in-game, and checking the
cockpit displays and block interactions on the grid.

## In-Game Setup

### Required Blocks

| Block type | Required name | Purpose |
| --- | --- | --- |
| Cockpit | `Jet Pilot Seat` | Primary ship controller and flight reference |
| Text surface provider | `JetOS [HFPS]` | Main MFD provider with at least three surfaces |
| Text surface | `Fighter HUD [HFPS]` | Forward HUD display surface |

### Optional Blocks

| Block type | Naming convention | Used by |
| --- | --- | --- |
| AI Flight + AI Combat | `AI Flight`, `AI Combat`, `AI Flight N`, `AI Combat N` | Radar and RWR |
| Merge blocks | `Bay 1`, `Bay 2`, ... | Missile bay selection and launch |
| Rotor + hinge | `Gun Rotor Left/Right`, `Gun Hinge Left/Right` | Auto-tracking gun mounts |
| Canards | `Canard L [Ani]`, `Canard R [Ani]` | AoA damping |
| Sound block | `Sound Block Warning` | Altitude, threat, and system warnings |
| Sound block | `Canopy Side Plate Sound Block` | Weapon search and lock tones |
| Stabilizer groups | names containing `normalstab` or `invertedstab` | Trim and stabilization |
| Hydrogen tanks | names containing `Jet` | Fuel display |

Thrusters with `Industrial` in the name are ignored. Hydrogen thrusters are treated
as afterburners; atmospheric thrusters provide the normal and MIL thrust stages.

### TerrainAPI Mod

The terrain page depends on a separate TerrainAPI mod. That mod is not included in
this repository and may not be publicly available; JetOS only contains the client
side that talks to it.

When the mod is loaded in the world, it exposes a `TerrainAPI` terminal property on
the programmable block. JetOS uses that property to download the planet heightmap
in chunks, then performs terrain lookups locally for the MFD map. If the property
is missing, JetOS disables terrain features silently and the rest of the system can
continue running.

## Controls

JetOS is controlled through programmable block toolbar arguments, usually mapped to
numpad-style cockpit buttons.

| Argument | Action |
| --- | --- |
| `1` / `2` | Navigate up / down |
| `3` | Select |
| `4` | Back |
| `5` | Module-specific function |
| `6` / `7` | Global AoA trim down / up |
| `8` | Cycle target |
| `9` | Return to main menu |

Modules can override navigation and special-function handling when they own the
current MFD page.

## Repository Layout

```text
Mdk.PbScript2/
  Program.cs                  Entry point and MDK-compatible shell
  SystemManager.cs            Initialization, tick loop, input routing
  Jet.cs                      Grid hardware model and target database
  Modules/                    HUD, radar, weapons, guns, canards, terrain, config
  HUD/                        Flight, targeting, radar, and weapon renderers
  UI/                         MFD pages, chrome, transitions, grid/status panels
  Utilities/                  Ballistics, sound, terrain data, sprites, CustomData
  Diagnostics/                Standalone in-game debug scripts, excluded from build

Mod/testmod/                  JetOS LCD sprite mod sources and textures
Tools/                        Sprite and workshop helper tooling
docs/                         Architecture notes, demos, and subsystem references
```

Every compiled `.cs` file follows the MDK programmable block pattern:

```csharp
namespace IngameScript
{
    partial class Program
    {
        // JetOS code lives here.
    }
}
```

## Documentation

The core references are:

| Document | Contents |
| --- | --- |
| [Architecture](docs/architecture.md) | Initialization order, tick loop, module system |
| [HUD Rendering](docs/hud-rendering.md) | HUD pipeline, renderers, symbology |
| [Target Tracking](docs/target-tracking.md) | Contact acquisition, decay, target selection |
| [Weapons](docs/weapons.md) | Radar, RWR, missile bays, gun control |
| [Sound System](docs/sound-system.md) | Dual-channel warning and weapon audio |
| [Terrain System](docs/terrain-system.md) | TerrainAPI heightmap loading and MFD page |
| [SE API Reference](docs/se-api-reference.md) | Verified Space Engineers API usage |
| [SE Scripting Oddities](docs/se-scripting-oddities.md) | Documented engine and PB quirks |

Browser-based demos and visual references live under `docs/interactive/`.

## Sprite Mod

The script references sprites by subtype id, for example `JetOS_FPM`,
`JetOS_RangeRing`, and `JetOS_MFD_Corner`. The source SVGs and generated PNG/DDS
assets live under `Mod/testmod`.

The sprites are white on transparent and tinted by the script at runtime, so the
same asset can be used for normal, warning, lock, dim, and emphasis states.

For local testing, junction the mod folder into the Space Engineers mods directory
and enable it in the world:

```powershell
New-Item -ItemType Junction `
  -Path "$env:APPDATA\SpaceEngineers\Mods\testmod" `
  -Target "$PWD\Mod\testmod"
```

## Development Notes

- Target framework: `.NET Framework 4.8`
- Language version: `C# 6.0`
- Runtime cadence: `UpdateFrequency.Update1`
- Instruction budget: Space Engineers programmable blocks are tight, so rendering,
  radar updates, terrain loading, and CustomData access are written with allocation
  and instruction count in mind.
- Minification is configured in `Mdk.PbScript2/Mdk.PbScript2.mdk.ini`.
- `Diagnostics/` scripts are intentionally excluded from the packaged PB script.

## Visual Direction

JetOS uses a restrained tactical MFD style: dark green background, phosphor text,
gold corporate accents, compact data panels, and sprite-based HUD symbols. A good
wordmark direction would be **Rajdhani SemiBold** or **IBM Plex Sans Condensed**.
Both fit the cockpit-instrument tone without turning the project into a generic
sci-fi logo.

## License

No public license has been added. Treat the repository as source-available unless
a license file is provided.
