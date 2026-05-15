# RadarFeed v2 And Datalink Rebuild Design

## Goal

Rebuild the JetOS radar, radar feed, and datalink integration around one simple rule: every trackable contact must have a stable game `EntityId`, and all target identity decisions use that id instead of names, source slots, or proximity guesses.

The new system should provide a reliable multi-target radar picture on plugin servers, preserve one onboard AI Combat + Flight combo for backward compatibility and STT-quality refinement, and keep JetOS usable on non-plugin servers with the normal single-combo fallback.

## Scope

This is a v2 cutover, not an in-place extension of the current multi-AI radar pool. New code should live in v2 files first so the old radar stack can be cleanly disconnected after v2 is verified.

In scope:

- Server-side `JetOSRadarFeed` v2 scan of valid top-grid contacts.
- Hostile contacts in the weapon-capable target list.
- Neutral and unknown contacts in a map-only store.
- Datalink sharing for authored and relayed observations.
- One onboard AI Combat + Flight combo for fallback radar and STT assist.
- Kinematic RWR warning over hostile grid tracks.
- Torch/server plugin registration through the existing `JetOSRadarFeed` programmable-block property.

Out of scope:

- Multiple AI Combat block pool emulation.
- Old multi-block RWR choreography.
- Name-based deduplication.
- Character contacts.
- Missile/projectile contacts.
- Target class, mass, or size metadata in the first pass.
- A fake `JetOS Radar Feed` block.
- Client-side/Pulsar terminal property registration.

## Architecture

RadarFeed v2 is the shared truth source when the Torch/server plugin exists. It scans one construct-level radar bubble using the real AI Combat block definition range, emits top-most grid contacts only, and requires a stable nonzero top-grid `EntityId` for every emitted contact.

JetOS stores contacts in two separate PB-side tables:

- `enemyList`: hostile, weapon-eligible targets only.
- Map-only contacts: neutral and unknown contacts for terrain/map situational awareness only.

One onboard AI Combat + Flight combo remains installed and configured. On plugin servers, it can be steered toward the selected hostile for STT-quality lock. On non-plugin servers, it remains the backward-compatible vanilla radar path and works without server assistance.

The old multi-AI pool and old RWR pool should be disconnected after v2 is wired. Their useful behaviors survive as one onboard combo, unified hostile target storage, map-only contact storage, datalink propagation, and kinematic RWR warning.

## RadarFeed v2 Contract

The terminal property remains named `JetOSRadarFeed`. It is registered only by the server/Torch plugin.

The plugin finds the radar source using this order:

1. Prefer an eligible `[JO]` tagged `AI Combat` block on the same construct.
2. Fall back to the first eligible onboard `AI Combat` block for backward compatibility.

The scan uses the AI Combat block's real definition search radius. It does not use a custom JetOS global range. It emits only top-most grid contacts and collapses physically connected grids into one construct-level contact.

Each feed update runs at roughly `0.2` seconds and emits up to:

- 32 hostile contacts, range-sorted.
- 32 neutral/unknown map-only contacts, range-sorted.

Each contact record carries only the first-pass fields JetOS needs:

- `kind`: `H`, `N`, or `U`.
- `entityId`: stable nonzero top-grid id.
- `position`: world position.
- `velocity`: server physics `LinearVelocity`.
- `name`: display-only label.

Contact kinds:

- `H`: hostile, weapon-eligible.
- `N`: neutral, map-only.
- `U`: unknown, map-only.

Names are never used for deduplication, target selection, lock handoff, or datalink identity.

## PB Contact Stores

Hostile contacts feed `Jet.enemyList` and remain the only contacts that can be selected for weapons, missile cache updates, gun logic, HUD target brackets, and STT assist.

Neutral and unknown contacts feed a new map-only store. They are visible only where map population makes sense, such as terrain/radar-map views. They do not appear in target cycling, missile cache, gun logic, or STT assist.

Both stores use 30-second contact decay. Hostile contacts keep the existing slow "connection lost" behavior through age and track history. Map-only contacts can use simpler freshness state because they are not weapon-critical.

All contact lookup and merge behavior must be based on `EntityId`. If a v2 contact lacks a nonzero entity id, JetOS should ignore it rather than falling back to name or proximity identity.

## Datalink

Datalink shares observations, not ownership claims.

A jet authors an observation only when it physically sees the contact itself through RadarFeed v2 or the onboard fallback combo. Authored observations use the author's numeric JetOS programmable block or craft id as `observerId`.

A relaying jet may forward a remote observation, but it preserves the original `observerId` and sends its own identity as `senderId`. It must not convert relayed data into a fresh authored observation.

Datalink packets carry stable identity and freshness:

- `observerId`: original observing JetOS craft.
- `senderId`: current datalink sender. This equals `observerId` on authored packets and differs on relayed packets.
- `targetEntityId`: stable top-grid id.
- `kind`: hostile or map-only kind.
- `position`.
- `velocity`.
- `ageSeconds`: age of the original physical observation when this packet is sent.
- `hopCount`: relay depth, capped at 3 hops.

Rules:

- A jet ignores packets whose `observerId` is itself.
- A jet forwards only newly received or meaningfully updated remote observations.
- Relay hop count is capped at 3.
- Relays preserve original observation age.
- Relays do not make stale tracks immortal.
- Local physical observations beat remote observations.
- Deduplication uses `targetEntityId`.

Traffic control:

- Changed authored observations may transmit at most once every `0.2` seconds per contact.
- Movement counts as an update, but the `0.2` second cap still applies.
- Keyframes are sent every 5 seconds so late or packet-loss receivers recover.
- Only new or changed data is sent between keyframes.

## STT Assist And Fallback Radar

STT assist is hostile-only.

If the pilot selects a hostile that is remote-only or out of local radar range, JetOS keeps that selection stable. On plugin servers, once RadarFeed v2 sees that selected hostile `EntityId` inside the local radar bubble, the plugin should immediately steer the onboard AI Combat + Flight combo toward that hostile.

The preferred implementation is plugin-assisted because the programmable-block API does not expose a force-target method. Decompiled Space Engineers code shows that `MySearchEnemyComponent.DoSearch(...)` accepts `topPriorityEntityId`; matching that id gives the candidate `double.MinValue` priority. The plugin may use this path to strongly prefer the pilot-selected hostile. If needed, it can fall back to setting the search component's found enemy to a valid block on the selected target grid, which triggers the normal target-locking path.

The canonical JetOS target id remains the top-grid `EntityId` from RadarFeed v2. The onboard AI combo may internally lock a block id; JetOS should use that only as STT-quality evidence for the selected top-grid target, not as the contact identity.

If the onboard combo locks a hostile different from the pilot-selected target:

- Pilot selection does not change.
- The off-target combo observation may update the target store.
- STT status for the selected target remains not locked.
- The plugin may keep trying to reacquire the selected hostile when it is valid and in range.

On non-plugin servers, JetOS cannot force the onboard combo to lock a selected `EntityId` through PB APIs. The fallback behavior is to keep the combo scanning normally at the minimum useful interval and merge by `EntityId` if it naturally acquires the selected target.

## RWR v2

RWR v2 is threat assessment over hostile grid tracks. It is not a separate target acquisition system and does not change target selection.

Inputs:

- Local hostile tracks from RadarFeed v2.
- Remote hostile tracks from datalink.
- Hostile tracks from the onboard fallback combo.

RWR does not use missile or projectile contacts. It warns only when a hostile grid appears to be flying toward the jet fast enough to be threatening.

First-pass warning criteria:

- Hostile grid track.
- Closing speed at least 250 m/s.
- Closing trajectory toward ownship.
- Likely near pass rather than distant non-intersecting motion.
- Approaching aspect consistent with the hostile pointing generally at us.

RWR warning must not cause selected-target changes or STT retargeting. A fast incoming hostile grid may generate a warning even when the pilot's selected target is something else.

## Compatibility And Failure Modes

Plugin present:

- RadarFeed v2 populates hostiles and map-only contacts.
- Selected hostile can be handed to the onboard combo when in range.
- Datalink shares local authored observations and bounded relays.

Plugin absent:

- JetOS keeps one onboard AI Combat + Flight combo as normal radar.
- Datalink remote hostile contacts can still be selected and used as tracks.
- JetOS cannot force the onboard combo to acquire a specific selected `EntityId`; it merges if the combo naturally sees the same id.

Feed stale or missing:

- No plugin contacts are refreshed.
- Existing contacts age out through normal 30-second decay.
- JetOS remains usable through onboard fallback and datalink.

Malformed feed row:

- Ignore it.
- Do not create no-id contacts.
- Do not fall back to name or proximity identity.

## Verification Plan

Build checks:

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT
dotnet test Plugins/JetOSRadarFeed.Tests/JetOSRadarFeed.Tests.csproj
dotnet build Plugins/JetOSRadarFeedTorch/JetOSRadarFeedTorch.csproj --configuration Release
```

Packed script size check:

```powershell
(Get-Content -Path "$env:APPDATA\SpaceEngineers\IngameScripts\local\Mdk.PbScript2\script.cs" -Raw).Length
```

In-game checks:

- Plugin server sees multiple hostile top-grid contacts from one radar source.
- Every hostile contact has a stable nonzero top-grid `EntityId`.
- Duplicate names do not create duplicate or merged identities.
- Neutral and unknown contacts appear only in map-only views.
- Hostiles appear in normal target selection and weapon flows.
- Datalink shares authored observations and relays them for at most 3 hops.
- Relayed packets do not refresh original observation age forever.
- Selected remote hostile remains selected when out of range.
- When selected hostile enters local v2 range on a plugin server, onboard combo begins acquiring that same target.
- On non-plugin server, single combo radar still works normally.
- Fast closing hostile grid triggers RWR warning without changing selected target.
