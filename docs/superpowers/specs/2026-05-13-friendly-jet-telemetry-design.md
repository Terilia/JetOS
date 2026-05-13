# Friendly Jet Telemetry Design

## Goal

JetOS jets should broadcast a tiny friendly telemetry packet over IGC so nearby JetOS instances can draw each other on the terrain map. The data must stay separate from hostile radar contacts, weapon selection, gun logic, and missile target logic.

## Protocol

- Channel: `JETOS_JET_STAT`
- Rate: 5 Hz
- Payload: `MyTuple<long, Vector3D, Vector3D>`
- `Item1`: `Me.EntityId` from the sending programmable block
- `Item2`: cockpit world position from `Jet.CockpitPosition`
- `Item3`: cockpit linear velocity from `Jet.CockpitVelocity`

`Me.EntityId` is a numeric programmable block identity. It is stable across rename/player scan naming changes, but changes if the programmable block is deleted and rebuilt.

## Architecture

Add a small utility helper, separate from `MissileBayHelper`, for friendly jet telemetry. It owns:

- One broadcast channel constant.
- A 0.2 second broadcast accumulator for 5 Hz output.
- One IGC listener for incoming friendly telemetry.
- A compact list of recently seen friendly jets.

The helper should be ticked from `SystemManager.Main()` after `_myJet.UpdateTickCache()` has refreshed cockpit position and velocity. This keeps the telemetry path independent of modules and ensures it still runs while any screen/module is active.

## Receive Behavior

Incoming packets are accepted only if they match the expected tuple type. Packets whose id equals this programmable block's `Me.EntityId` are ignored so the terrain map does not draw the local jet twice.

Friendly entries are upserted by id and pruned after a short timeout, recommended at 2 seconds. This gives enough tolerance for missed packets at 5 Hz without keeping stale aircraft on the map for long.

## Terrain Map Display

`TerrainModule.DrawMap()` should draw friendly jets after contacts and missiles, using the same cockpit-relative map projection:

- Relative vector: `friendly.Position - ownPosition`
- X screen axis: dot with jet right vector
- Y screen axis: negative dot with jet forward vector
- Clip at the map edge using existing `ClipMap()`
- Draw as a blue square, with a subtle velocity tick if useful and still cheap

Friendly jets must not be inserted into `Jet.enemyList`. They must not appear in the target list, selection cycle, radar UI, gun logic, missile guidance, or hostile contact color logic.

## Error Handling

If IGC data is malformed or from another protocol version, ignore it silently. If the cockpit is missing or the jet cache has not produced a useful position yet, skip broadcast for that tick.

## Testing

There are no automated tests in this MDK project. Verification should include:

- `dotnet build Mdk.PbScript2.sln --configuration Release`
- Confirm the script still packages under MDK minification.
- In-game check with two JetOS programmable blocks:
  - Each jet broadcasts at about 5 Hz.
  - Each terrain map shows the other jet as a blue square.
  - The local jet does not show as a duplicate friendly.
  - Friendly jets do not appear in the target list or affect weapons.
