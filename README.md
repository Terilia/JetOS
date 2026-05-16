<p align="center">
  <img src="docs/assets/jetos-logo.svg" alt="JetOS - Tactical Avionics PB System" width="720">
</p>

<p align="center">
  <strong>A Space Engineers fighter cockpit operating system built around one programmable block script and the JetOS Extensions plugin bundle.</strong>
</p>

## Installation

JetOS is meant to be installed as a small set of client/server pieces:

- The JetOS programmable block script.
- The JetOS Extensions client plugin for Pulsar.
- The JetOS Extensions server plugin for Torch when you use a dedicated server.
- The JetOS LCD sprite/content mod.

### 1. Install Pulsar

Every client that uses the JetOS cockpit should have Pulsar installed. JetOS uses Pulsar for the client-side extension bundle.

Copy these files from the repository:

```text
ExternalMods\Built\Pulsar\JetOSExtensions.Client.dll
ExternalMods\Built\Pulsar\0Harmony.dll
```

Place them here:

```text
%APPDATA%\Pulsar\Legacy\Local\
```

After copying, the folder should contain:

```text
%APPDATA%\Pulsar\Legacy\Local\JetOSExtensions.Client.dll
%APPDATA%\Pulsar\Legacy\Local\0Harmony.dll
```

Restart Space Engineers through Pulsar after updating these files.

### 2. Install The Sprite Content Mod

Subscribe to the JetOS sprite/content mod on Steam Workshop:

[JetOSSpriteUnlocker](https://steamcommunity.com/sharedfiles/filedetails/?id=3720997935)

Enable `JetOSSpriteUnlocker` in the world. This supplies the custom `JetOS_*` HUD and MFD sprites used by the cockpit displays.

For offline/local installs, the same content is also included in this repository. Copy this folder:

```text
ExternalMods\Built\Content\JetOSSpriteUnlocker
```

Place it in your local Space Engineers mods folder:

```text
%APPDATA%\SpaceEngineers\Mods\JetOSSpriteUnlocker
```

### 3. Install The Programmable Block Script

Load the JetOS programmable block script into the ship programmable block.

If you are installing from this repository with MDK2 available, building the solution deploys the script locally:

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT
```

The deployed in-game script is written to:

```text
%APPDATA%\SpaceEngineers\IngameScripts\local\Mdk.PbScript2
```

In-game, load `Mdk.PbScript2` into the programmable block and compile it.

### 4. Torch Dedicated Server Install

If you use a Torch dedicated server, install the combined server plugin:

```text
ExternalMods\Built\Torch\JetOSExtensions.Server.zip
```

Place the zip in the Torch plugin folder used by your server instance. The zip already contains:

```text
manifest.xml
JetOSExtensions.Server.dll
JetOSExtensions.Server.pdb
0Harmony.dll
```

If your Torch setup uses an unpacked plugin folder instead of zipped plugins, keep `0Harmony.dll` in the same folder as `JetOSExtensions.Server.dll`.

Add this plugin GUID to the `<Plugins>` section of your Torch config:

```xml
<guid>6f2b0e7d-43e2-4a3a-93fd-4f6bdf6ab4f1</guid>
```

Example:

```xml
<Plugins>
  <guid>6f2b0e7d-43e2-4a3a-93fd-4f6bdf6ab4f1</guid>
</Plugins>
```

Restart Torch after changing the config or replacing the plugin zip.

## JetOS Extensions Source

The full source code for the JetOS Extensions bundle is included in this repository:

```text
Plugins\JetOSExtensions\
```

The bundle contains:

```text
Plugins\JetOSExtensions\JetOSExtensions.Client\      Pulsar / Space Engineers client plugin
Plugins\JetOSExtensions\JetOSExtensions.Server\      Torch server plugin
Plugins\JetOSExtensions\JetOSExtensions.Shared\      Shared protocol code
Plugins\JetOSExtensions\Content\JetOSSpriteUnlocker\ Sprite/content mod source
```

## Upstream And License Notes

The CAMOV camera LCD support in JetOS Extensions is based on [StarCpt's SE-CameraLCD-Remastered](https://github.com/StarCpt/SE-CameraLCD-Remastered), which is licensed under [MPL-2.0](https://www.mozilla.org/en-US/MPL/2.0/). JetOS ships the full JetOS Extensions source above so those parts are available in source form.

I reached out to the author to ask if I could PR the CAMOV behavior upstream, but did not receive a reply. ¯\\_(ツ)_/¯

The built convenience artifacts live under:

```text
ExternalMods\Built\Pulsar\
ExternalMods\Built\Torch\
ExternalMods\Built\Content\
```

## What JetOS Adds

JetOS turns a custom fighter craft into an integrated tactical cockpit. It drives the HUD, MFD pages, radar/RWR, datalink target picture, weapon handoff, gun tracking, terrain awareness, canard damping, warnings, and cockpit UI from one programmable block.

JetOS Extensions provides the supporting plugin features:

- `TerrainAPI` programmable block terminal property for terrain map data.
- `JetOSRadarFeed` programmable block terminal property for server-side target feeds.
- LCD booster patches for high-rate cockpit display updates.
- CAMOV camera LCD support on the client.
- `[Ani]` canard angle sync/fix support.
- JetOS LCD sprite definitions and textures.

JetOS Extensions is still experimental. The plugin bundle is not fully stable yet and may break after Space Engineers, Pulsar, Torch, or mod updates.

## Required Ship Blocks

| Block type | Required name | Purpose |
| --- | --- | --- |
| Cockpit | `Jet Pilot Seat` | Primary ship controller and flight reference |
| Text surface provider | `JetOS [HFPS]` | Main MFD provider with at least three surfaces |
| Text surface | `Fighter HUD [HFPS]` | Forward HUD display surface |

## Optional Ship Blocks

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

Thrusters with `Industrial` in the name are ignored. Hydrogen thrusters are treated as afterburners; atmospheric thrusters provide the normal and MIL thrust stages.

## Cockpit Controls

JetOS is controlled through programmable block toolbar arguments, usually mapped to numpad-style cockpit buttons.

| Argument | Action |
| --- | --- |
| `1` / `2` | Navigate up / down |
| `3` | Select |
| `4` | Back |
| `5` | Module-specific function |
| `6` / `7` | Global AoA trim down / up |
| `8` | Cycle target |
| `9` | Return to main menu |

## Repository Map

```text
Mdk.PbScript2\                  JetOS programmable block script
Plugins\JetOSExtensions\        Full JetOS Extensions source bundle
ExternalMods\Built\Pulsar\      Client plugin files for Pulsar
ExternalMods\Built\Torch\       Server plugin files for Torch
ExternalMods\Built\Content\     JetOS sprite/content mod
docs\                           Additional subsystem notes
```

## Notes

- Use `JetOSExtensions.Server.zip` on Torch instead of the older standalone Torch packages.
- Use `JetOSExtensions.Client.dll` on Pulsar instead of the older standalone client packages.
- Keep `0Harmony.dll` beside the JetOS Extensions DLLs; Torch and Pulsar need it at load time.
- In multiplayer, the radar feed is server-side. Client-side Pulsar plugins cannot register the server-side programmable block radar feed.
- Except for the upstream-derived MPL-2.0 CAMOV camera LCD portions noted above, no public license has been added. Treat the rest of the repository as source-available unless a license file is provided.
