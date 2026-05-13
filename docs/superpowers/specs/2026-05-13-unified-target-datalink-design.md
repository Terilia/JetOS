# Unified Target Datalink Design

## Goal

Replace the standalone friendly jet telemetry path with one compact JetOS datalink that carries both friendly ownship status and hostile target observations.

Network-shared hostile contacts are full targets. They should appear in the normal target list, HUD/radar displays, terrain map, weapon screen, missile target cache, missile target update stream, and gun logic. The pilot can choose to use a remote-only track based on range and displayed freshness.

This design supersedes `2026-05-13-friendly-jet-telemetry-design.md`.

## Current Target Source

JetOS currently gets hostile targets only from local AI Flight and AI Combat block pairs managed by `RadarControlModule`.

The combat block finds the enemy and exposes `SearchEnemyComponent.FoundEnemyId`. The paired flight block receives the target position as an autopilot waypoint. `RadarTrackingModule` reads the first waypoint, derives target velocity from the last two samples, and extrapolates position for up to one second.

Current usable target fields are:

- Entity id from `FoundEnemyId`.
- Name parsed from combat block `DetailedInfo`.
- World position from the AI flight waypoint.
- World velocity derived from waypoint deltas.
- Acceleration computed in `Jet.UpdateOrAddEnemy()` from velocity changes.
- Source index for the local radar pair.
- Last-seen time and a 30-second track history bitfield.

`Jet.enemyList` is the central target table. `Jet.UpdateOrAddEnemy()` deduplicates by entity id, then name, then 50m proximity. Normal contacts decay after 30 seconds without updates. The selected target decays after 60 seconds.

## Architecture

Add a focused static `Datalink` utility and remove `FriendlyJetTelemetry`.

`Datalink` owns:

- One IGC channel, `JETOS_DL`.
- One broadcast listener.
- One broadcast accumulator.
- A small friendly ownship cache for terrain-map friendly markers.
- Receive logic that inserts target packets into `Jet.enemyList`.

`SystemManager.Main()` ticks `Datalink` immediately after `Jet.UpdateTickCache()`, where friendly telemetry is ticked today. This keeps the datalink independent of the active MFD module.

Local radar remains the primary sensor path. `RadarControlModule` continues to feed local observations through `Jet.UpdateOrAddEnemy()`. Datalink target packets also feed that same function so every target consumer keeps using the existing target table.

## Protocol

Use one IGC channel with two numeric packet kinds.

Friendly ownship packet:

```csharp
MyTuple<int, long, Vector3D, Vector3D>
```

- `Item1`: packet kind, `0`.
- `Item2`: sender programmable block entity id.
- `Item3`: sender cockpit position.
- `Item4`: sender cockpit velocity.

Hostile target packet:

```csharp
MyTuple<int, long, long, Vector3D, Vector3D, double>
```

- `Item1`: packet kind, `1`.
- `Item2`: sender programmable block entity id.
- `Item3`: target entity id from SE, or `0` if unknown.
- `Item4`: target world position.
- `Item5`: target world velocity.
- `Item6`: observation age in seconds when sent.

The target packet intentionally omits target name at first. Entity id is the important SE lock-handoff key. Position proximity is the fallback dedup path. Avoiding strings saves payload size, parsing, and minified script size.

## Broadcast Behavior

Every 0.2 seconds, each JetOS instance broadcasts:

- Its own friendly status packet.
- Recently updated local hostile contacts from `enemyList`.

To avoid loops and packet spam, JetOS does not rebroadcast remote-only target tracks. The simplest implementation rule is:

- local radar/RWR source indices are nonnegative and may be broadcast;
- datalink-inserted source indices are negative and are not broadcast.

Target packets are sent only for contacts whose local observation age is 3 seconds or less. This prevents a stale local track from being refreshed forever across the network. Receivers reject target packets whose observation age is greater than 3 seconds.

## Receive Behavior

Incoming packets from the local programmable block id are ignored.

Friendly ownship packets upsert the friendly cache by sender id. Friendly cache entries decay quickly, around 2 seconds, so missing jets disappear from the terrain map without affecting weapon logic.

Hostile target packets are validated, age-checked, and inserted into `Jet.enemyList` through an extended target update path. Remote contacts use reserved source index `-1` so UI and rebroadcast logic can identify them cheaply.

Malformed packets are ignored silently.

## Lock Handoff

Remote targets count as full targets immediately, but JetOS cannot directly command a Space Engineers combat block to lock a specific remote entity. The handoff mechanism is therefore identity and kinematics based.

The datalink broadcasts the same target entity id that SE exposed through `FoundEnemyId`. If the local combat block later sees that target, `Jet.UpdateOrAddEnemy()` merges the local radar report into the existing remote contact by entity id. The track becomes locally refreshed without duplicate entries.

If entity id is missing, the existing 50m proximity fallback can still merge local acquisition into the remote track. This is less reliable, but adequate for unknown-id contacts.

As a small assist, when the selected contact is remote-only, JetOS attempts to force a local search using the combat block force-search path documented in the local SE API notes. If that call is not available in the programmable block API at compile time, implementation omits the assist and relies on normal SE acquisition plus entity-id merge. This is a best-effort nudge for SE to rescan, not a guaranteed target assignment.

## Target Fusion

The existing `enemyList` remains the fusion table.

When a local report updates a remote contact:

- position and velocity are replaced with local values;
- acceleration is recomputed by the existing EMA path;
- source index becomes the local radar source;
- track history is advanced normally.

When a remote report updates an existing local contact:

- it may refresh position and velocity if the local contact is stale enough;
- it should not downgrade a fresh local source to datalink source;
- local observations should win over remote observations when both are fresh.

This keeps local radar authority while still allowing remote tracks to be weapon usable.

## Terrain Map

Remove `FriendlyJetTelemetry.GetActiveFriends()` and the standalone friendly telemetry path.

The terrain map draws:

- hostile contacts from `Jet.enemyList`, which now includes local and remote targets;
- friendly jets from `Datalink.GetActiveFriendlies()`.

Friendly jets remain display-only. They do not enter `enemyList`, do not affect target cycling, and do not feed weapons.

## UI And Source Tags

Remote hostile contacts should be visually normal targets, with compact source tag `DL` where source text is shown today. Existing target freshness coloring and track-history display should continue to communicate stale data.

If script size is tight, the first implementation can skip new icons and reuse existing target/friendly sprites. The important behavior is data flow, not new symbology.

## Size Constraints

The implementation should favor small, direct code:

- reuse `enemyList` instead of adding a second target facade;
- reuse `MyTuple` packets and existing vector fields;
- avoid string target names in network packets;
- avoid rebroadcasting remote tracks;
- remove `FriendlyJetTelemetry` rather than keep two network helpers;
- keep friendlies separate from hostile targets with one small cache.

## Testing

Verification should include:

- `dotnet build Mdk.PbScript2.sln --configuration Release`
- In-game two-jet check: both jets draw each other as friendlies on the terrain map.
- Local radar target on Jet A appears as a full target on Jet B.
- Remote target on Jet B can be selected and writes `Cached`/`CachedSpeed`.
- Missile target update packets use the selected remote target.
- Gun logic can consider the remote target if it is in cone/range.
- When Jet B locally acquires the same SE entity id, the remote and local tracks merge rather than duplicate.
- Remote-only targets fall out after the configured timeout when no reports continue.
- Friendlies never appear in target cycling or weapon logic.
