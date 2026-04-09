# JetOS — Documentation

> A fighter-jet operating system written as a Space Engineers programmable block script.
> HUD, weapons, radar, terrain awareness, gun turret auto-track, and a corporate-themed MFD UI.

```
       ┌─────────────────────────────────────────────┐
       │  NYINAH CORP    TACTICAL SYSTEM    MFD-1   │
       │ ─────────────────────────────────────────── │
       │                                             │
       │   ✈  JetOS  —  documentation index          │
       │                                             │
       └─────────────────────────────────────────────┘
```

## Interactive Demos

These run in any browser. Open them locally or serve `docs/` to play with them.

| Demo | What it shows |
|------|---------------|
| **[index.html](index.html)** | Landing page — embeds every demo below in one scrollable showcase |
| **[interactive/horizon-demo.html](interactive/horizon-demo.html)** | Drag pitch/roll sliders, watch the artificial horizon respond in real time |
| **[interactive/throttle-demo.html](interactive/throttle-demo.html)** | Throttle slider with the MIL/AB gate, equalization, and damage scenarios |
| **[interactive/radar-demo.html](interactive/radar-demo.html)** | Animated radar minimap with multiple closing contacts and target cycling |
| **[interactive/sound-priority-demo.html](interactive/sound-priority-demo.html)** | Click priority buttons and watch the dual-channel state machine resolve |
| **[interactive/theme-demo.html](interactive/theme-demo.html)** | Cycle through the four HUD color themes |
| **[propulsion-animation.html](propulsion-animation.html)** | Live JS recreation of the in-game `StatusPanelRenderer` engines |

## System Documentation

| Doc | Subject |
|-----|---------|
| [architecture.md](architecture.md) | Tick loop, module system, exception handling |
| [propulsion.md](propulsion.md) | Engines, throttle stages, MIL/AB gate, hydrogen tanks |
| [engine-equalization.md](engine-equalization.md) | Per-side thrust balancing math and damage scenarios |
| [hud-rendering.md](hud-rendering.md) | HUD pipeline, every renderer, themes, smoothing |
| [target-tracking.md](target-tracking.md) | Enemy contact list, deduplication, selection |
| [weapons.md](weapons.md) | Radar state machine, RWR, missile bays, gun turrets |
| [terrain-system.md](terrain-system.md) | TerrainAPI, heightmap download, terrain map rendering |
| [canard-system.md](canard-system.md) | Canard control surfaces with stabilizer spillover |
| [configuration.md](configuration.md) | 3-level config menu, all parameters, persistence |
| [sound-system.md](sound-system.md) | Dual-channel audio with priority + frame-delay state machine |
| [sound-pipeline-debug.md](sound-pipeline-debug.md) | Tick-by-tick trace of the sound pipeline |
| [lcd-booster.md](lcd-booster.md) | Server + client plugins to break SE's 6 fps LCD throttle |
| [se-api-reference.md](se-api-reference.md) | Verified SE API surface area used by JetOS |
| [se-scripting-oddities.md](se-scripting-oddities.md) | 42 documented SE scripting gotchas |

## Quick Tour

JetOS is a static-class-orchestrated PB script. The flow is:

1. `Program()` → `SystemManager.Initialize()` builds modules in dependency order.
2. Every tick, `Program.Main()` → `SystemManager.Main()`:
   - Increments `Jet.GameTicks`
   - Caches gravity once
   - Processes the altitude/speed warning hysteresis
   - Either renders the menu or routes a toolbar argument to `HandleInput()`
   - Ticks the active module
   - Background-ticks HUD, Radar, AirtoAir, GunControl, Canards (when not active)
   - Drains all queued sound requests through `SoundManager.Tick()`
3. Modules render their MFD pages through `UIController` and the shared `MFDFrame` chrome.

See [architecture.md](architecture.md) for the full tick loop diagram.

## Build & Deploy

```bash
# Release build auto-deploys to %APPDATA%/SpaceEngineers/IngameScripts/local/Mdk.PbScript2
dotnet build Mdk.PbScript2.sln --configuration Release
```

There are no automated tests — verification is done in-game.

## Required Block Names

| Block | Type | Purpose |
|-------|------|---------|
| `Jet Pilot Seat` | `IMyCockpit` | Main control seat (script aborts without it) |
| `JetOS [HFPS]` | `IMyTextSurfaceProvider` | MFD provider with surfaces 0/1/2 |
| `Fighter HUD [HFPS]` | `IMyTextSurface` | Head-up display surface |

See the per-system docs for optional block names (radar, turrets, canards, sound blocks…).
