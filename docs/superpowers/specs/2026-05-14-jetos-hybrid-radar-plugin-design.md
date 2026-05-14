# JetOS Hybrid Radar Plugin Design

## Goal

JetOS should keep working on unpatched Space Engineers servers with the vanilla one-active-combat-radar limitation, while gaining multi-target radar acquisition on patched servers that run a JetOS-compatible server plugin.

The patched path must remain tied to real radar blocks. Extra targets should come from installed, functional AI Combat blocks, not from cameras or an invisible global scanner.

## Existing Constraint

Vanilla Space Engineers stores active AI components per grid by `MyAiBlockType`. Because `Combat` is a single dictionary entry, activating one AI Combat block deactivates the previous active combat block on that grid. JetOS can rotate through many AI Combat blocks, but only one can be the active vanilla combat radar at a time on an unpatched server.

Patching that singleton directly is high risk. Combat AI, movement AI, waypoint updates, terminal state, and attack-pattern logic all assume one active combat component and one active movement component per grid.

## Naming And Tagging

JetOS radar blocks use the existing Space Engineers naming pattern, with an optional JetOS tag:

```text
AI Combat [JO]
AI Flight [JO]

AI Combat 2 [JO]
AI Flight 2 [JO]

AI Combat 3 [JO]
AI Flight 3 [JO]
```

Rules:

- `[JO]` is the JetOS ownership tag.
- JetOS discovery strips `[JO]` before matching names, so tagged blocks still work on unpatched servers.
- Pairing is by normalized name and number: `AI Combat 7 [JO]` pairs with `AI Flight 7 [JO]` or `AI Flight 7`.
- The patched plugin treats a tagged AI Combat block as the authoritative radar slot marker.
- `[JO]` on the AI Flight block is preferred for readability but not required for plugin eligibility.
- Untagged AI Combat blocks remain available for vanilla behavior, but the plugin ignores them by default.

## Architecture

JetOS has two radar providers:

1. Vanilla provider

   The current programmable-block radar path remains available everywhere. It discovers AI Combat and AI Flight pairs, activates one combat radar at a time, and feeds contacts into JetOS through the existing target list.

2. Patched provider

   A server-side Pulsar/plugin/mod component discovers `[JO]` AI Combat blocks and publishes a multi-contact radar feed for JetOS. JetOS detects the feed heartbeat and ingests the plugin contacts. If the heartbeat expires, JetOS falls back to the vanilla provider without pilot action.

## Patched Provider Behavior

For each tagged AI Combat block on the same construct:

- The block must be functional, enabled, powered, complete, and owned in a way that matches the JetOS grid.
- The block's vanilla radar settings remain meaningful where practical: targeting relation, target priority, update interval, and character targeting.
- The plugin scans candidate entities through game/mod APIs, then assigns unique contacts across available radar slots.
- Each radar slot reports at most one primary contact per scan cycle.
- A target already assigned to one radar slot should not be assigned to a second slot in the same cycle unless no unique targets remain.
- Lost or invalid contacts age out rather than staying pinned forever.

The plugin may use one of two internal strategies:

- Direct search strategy: call the real search component on each AI Combat block when possible, using ignored or prioritized entity IDs to avoid duplicates.
- Plugin-side scan strategy: query top-most entities in radar range, apply relation and validity filters, and publish contacts using the AI Combat block as capacity and configuration.

The first implementation should prefer the safer plugin-side scan strategy, with the direct search strategy kept as an optional later refinement if vanilla AI search semantics are needed.

## Feed Contract

The first bridge is a text panel or LCD block named `JetOS Radar Feed [JO]` on the same construct as the JetOS programmable block. The plugin writes machine-readable feed data into that block's CustomData. JetOS reads the same block from programmable-block code.

The plugin publishes compact contact records:

- feed version
- heartbeat timestamp or frame
- radar block entity id
- radar slot index
- target entity id
- target name or type label when available
- world position
- linear velocity
- relationship or target kind
- contact timestamp

JetOS ingests these records into the existing datalink-backed enemy/target list. The active target remains a pilot/JetOS decision, not a plugin decision.

## Fallback And Failure Modes

- No plugin feed: use vanilla provider only.
- Plugin feed stale: discard plugin contacts and return to vanilla provider.
- Tagged combat block missing matching flight block: plugin can still use the combat block for contact feed, but JetOS should mark the slot degraded if flight-derived data is unavailable.
- Block disabled, damaged, unpowered, or incomplete: remove that radar slot from plugin capacity.
- Duplicate contacts: merge by target entity id first, then by normalized name and proximity if entity id is unavailable.
- Server/plugin mismatch: JetOS should show a simple degraded/fallback status instead of failing the radar module.

## Implementation Scope

JetOS changes:

- Normalize radar block names by removing `[JO]` during discovery.
- Add a plugin-feed reader that can merge external radar contacts into the existing target list.
- Add provider status reporting so the pilot can tell whether radar is in vanilla or patched mode.
- Keep current one-radar vanilla behavior intact.

Plugin changes:

- Create a JetOS radar feed plugin using the existing local Pulsar plugin style.
- Discover tagged AI Combat blocks.
- Build one contact feed per eligible radar block.
- Publish feed data through a bridge JetOS can read from programmable-block code.

Out of scope for the first pass:

- Camera-based tracking.
- Patching `MyAiBlockSystem` to allow multiple active combat AI components.
- Autonomous weapon control decisions in the plugin.
- Removing pilot-facing target metadata to save script size.

## Verification

Unpatched server/world:

- Tagged blocks are still discovered by JetOS.
- Radar falls back to the existing one-active-radar behavior.
- Existing target display and datalink behavior remain intact.

Patched server/world:

- With 3 airborne targets and 22 tagged radar slots, JetOS can ingest more than one simultaneous contact.
- Disabling or damaging a tagged AI Combat block reduces available plugin radar slots.
- Removing the plugin or stopping the feed returns JetOS to vanilla mode.

Build check for JetOS changes:

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT
```

Packed size check:

```powershell
(Get-Content -Path "$env:APPDATA\SpaceEngineers\IngameScripts\local\Mdk.PbScript2\script.cs" -Raw).Length
```
