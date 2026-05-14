<p align="center">
  <img src="docs/assets/jetos-logo.svg" alt="JetOS - Tactical Avionics PB System" width="720">
</p>

<p align="center">
  <strong>A Space Engineers fighter cockpit operating system built as an MDK2 programmable block script.</strong>
</p>

JetOS turns a custom fighter craft into an integrated tactical cockpit. It drives
the HUD, MFD pages, radar/RWR, datalink target picture, weapon handoff, gun
tracking, terrain awareness, canard damping, warnings, and cockpit UI from one
programmable block.

The project is designed for aircraft that use Space Engineers AI blocks, custom
LCD sprites, missile bays, rotor/hinge gun mounts, and a three-screen cockpit
layout. It is also written under programmable-block size pressure, so the code
leans toward compact shared helpers and careful runtime budgeting.

## Quick Links

[Build](#build-and-deploy) · [Setup](#in-game-setup) · [Controls](#cockpit-controls) · [Documentation](#documentation) · [Sprite Mod](#sprite-mod)

## What JetOS Can Do Now

- **Flight HUD:** artificial horizon, pitch ladder, flight path marker, aircraft
  symbol, heading, speed and altitude tapes, AoA, G-load, throttle state, lead
  pip, target brackets, off-screen arrows, gun funnel, breakaway cues, and radar
  overlay.
- **Tactical picture:** AI Flight/Combat block radar pairs feed one shared target
  table with identity matching, track history, selection, decay, and threat
  coloring.
- **Datalink:** nearby JetOS craft broadcast friendly ownship status and locally
  observed hostile targets over IGC channel `JETOS_DL`. Remote hostile reports
  become selectable targets and are marked as `DL` until local radar authority
  refreshes them.
- **Radar/RWR:** configurable sweep/track radar pool plus RWR radars,
  selected-target lock detection, RWR threat assessment, target cycling, and
  warning/lock/search tones.
- **Weapons:** missile bay selection, per-bay target handoff, missile status
  intake, weapon timeline display, seeker tones, and selected-target GPS/velocity
  sync through CustomData.
- **Gun control:** rotor/hinge gun mounts can auto-track targets with ballistic
  lead, closure/aspect display, configurable gains, and range limits.
- **Aircraft control:** MIL/afterburner throttle gate, hydrogen afterburner
  handling, thrust/fuel/battery status, stabilizer trim, and optional canard AoA
  damping.
- **MFD system:** three coordinated text surfaces with dark green NYINAH CORP
  styling, shared chrome, menu navigation, grid/status page, weapon page, terrain
  page, configuration page, and module-specific screens.
- **Terrain awareness:** optional TerrainAPI integration downloads a planet
  heightmap, renders contour maps, shows AGL-relative danger bands, and displays
  friendly/target markers on the terrain page.
- **Sprite mod:** custom `JetOS_*` LCD sprites provide HUD glyphs, radar contacts,
  MFD corners, warning markers, bay icons, missiles, gauges, and aircraft
  symbology.

## Cockpit Experience

JetOS is meant to feel like an avionics suite rather than a pile of independent
scripts. The HUD and MFDs share the same tactical picture, selected target, sound
state, and configuration values. A target detected by radar, received through
datalink, selected by the pilot, shown on the weapon page, and handed to a missile
all flows through the same target model.

The default visual style is a restrained dark-green cockpit theme: phosphor text,
compact data panels, muted gold labels, and white-on-transparent sprites tinted
at runtime by the script.

## Requirements

- Space Engineers for PC
- [MDK2](https://github.com/malware-dev/MDK2)
- .NET Framework 4.8
- C# 6.0
- Optional: the included JetOS sprite mod in `Mod/testmod`
- Optional: a TerrainAPI world mod that exposes the `TerrainAPI` programmable
  block property used by the terrain page

## Build And Deploy

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT
```

The Release build is packaged by MDK2 and deploys to:

```text
%APPDATA%\SpaceEngineers\IngameScripts\local\Mdk.PbScript2
```

For local verification, build successfully, load the programmable block script
in-game, and check the cockpit displays and block interactions on the grid. This
repository does not currently include automated tests.

To inspect the packed script size:

```powershell
(Get-Content -Path "$env:APPDATA\SpaceEngineers\IngameScripts\local\Mdk.PbScript2\script.cs" -Raw).Length
```

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
| AI Flight + AI Combat | `AI Flight`, `AI Combat`, `AI Flight N`, `AI Combat N` | Radar, RWR, and target acquisition |
| Merge blocks | `Bay 1`, `Bay 2`, ... | Missile bay selection and launch |
| Rotor + hinge | `Gun Rotor Left/Right`, `Gun Hinge Left/Right` | Auto-tracking gun mounts |
| Canards | `Canard L [Ani]`, `Canard R [Ani]` | AoA damping |
| Sound block | `Sound Block Warning` | Altitude, RWR, and system warnings |
| Sound block | `Canopy Side Plate Sound Block` | Weapon search and lock tones |
| Stabilizer groups | names containing `normalstab` or `invertedstab` | Trim and stabilization |
| Hydrogen tanks | names containing `Jet` | Fuel display |

Thrusters with `Industrial` in the name are ignored. Hydrogen thrusters are
treated as afterburners; atmospheric thrusters provide the normal and MIL thrust
stages.

### TerrainAPI Mod

The terrain page depends on a separate TerrainAPI mod. That mod is not included
in this repository; JetOS only contains the programmable-block client that talks
to it.

When the mod is loaded in the world, it exposes a `TerrainAPI` terminal property
on the programmable block. JetOS downloads the planet heightmap in chunks, builds
a tile index, then performs terrain lookups locally for the MFD map. If the
property is missing, JetOS disables terrain features and the rest of the system
continues running.

## Cockpit Controls

JetOS is controlled through programmable block toolbar arguments, usually mapped
to numpad-style cockpit buttons.

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

## Repository Map

```text
Mdk.PbScript2/
  Program.cs                  Entry point and MDK-compatible shell
  SystemManager.cs            Initialization, tick loop, input routing
  Jet.cs                      Grid hardware model and shared target table
  Modules/                    HUD, radar, weapons, guns, canards, terrain, config
  HUD/                        Flight, targeting, radar, and weapon renderers
  UI/                         MFD pages, chrome, transitions, grid/status panels
  Utilities/                  Ballistics, datalink, sound, terrain, sprites, CustomData
  Diagnostics/                Standalone in-game debug scripts, excluded from build

Mod/testmod/                  JetOS LCD sprite mod sources and textures
Tools/                        Sprite and workshop helper tooling
docs/                         Architecture notes, demos, and subsystem references
docs/assets/                  GitHub/README visual assets
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

| Document | Contents |
| --- | --- |
| [Architecture](docs/architecture.md) | Initialization order, tick loop, module system |
| [HUD Rendering](docs/hud-rendering.md) | HUD pipeline, renderers, symbology, themes |
| [Target Tracking](docs/target-tracking.md) | Contact acquisition, decay, target selection |
| [Weapons](docs/weapons.md) | Radar, RWR, missile bays, gun control |
| [Configuration](docs/configuration.md) | Runtime parameters, persistence, HUD toggles |
| [Sound System](docs/sound-system.md) | Dual-channel warning and weapon audio |
| [Terrain System](docs/terrain-system.md) | TerrainAPI heightmap loading and MFD page |
| [Canard System](docs/canard-system.md) | Canard control surfaces and stabilizer spillover |
| [SE API Reference](docs/se-api-reference.md) | Verified Space Engineers API usage |
| [SE Scripting Oddities](docs/se-scripting-oddities.md) | Documented engine and PB quirks |

Browser-based demos and visual references live under `docs/interactive/`.

## Sprite Mod

The script references sprites by subtype id, for example `JetOS_FPM`,
`JetOS_RangeRing`, and `JetOS_MFD_Corner`. The source SVGs and generated PNG/DDS
assets live under `Mod/testmod`.

The sprites are white on transparent and tinted by the script at runtime, so the
same asset can serve normal, warning, lock, dim, and emphasis states.

For local testing, junction the mod folder into the Space Engineers mods
directory and enable it in the world:

```powershell
New-Item -ItemType Junction `
  -Path "$env:APPDATA\SpaceEngineers\Mods\testmod" `
  -Target "$PWD\Mod\testmod"
```

## Development Notes

- Target framework: `.NET Framework 4.8`
- Language version: `C# 6.0`
- Runtime cadence: `UpdateFrequency.Update1`
- MDK2 minification is configured in `Mdk.PbScript2/Mdk.PbScript2.mdk.ini`
- `Diagnostics/` scripts are intentionally excluded from the packaged PB script
- Programmable-block size is a hard constraint; prefer behavior-preserving
  deduplication and shared helpers over feature cuts

## Project Status

JetOS is source-available cockpit software for a specific Space Engineers fighter
workflow. It is actively evolving and currently optimized for local/in-game
verification rather than a public package workflow.

No public license has been added. Treat the repository as source-available unless
a license file is provided.
