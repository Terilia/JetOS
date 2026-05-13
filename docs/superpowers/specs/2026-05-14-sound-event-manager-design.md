# JetOS Sound Event Manager Design

## Goal

Turn `SoundManager` from a small warning-tone helper into a compact event-driven cockpit audio layer. Modules should request semantic events such as "new target", "RWR lock", or "pull up"; `SoundManager` owns the mapping from events to Space Engineers sound IDs, priorities, channels, and cooldowns.

The system must stay code-size conscious. It should deduplicate sound-block handling and avoid putting raw mod sound names throughout radar, weapons, HUD, and system logic.

## Sound Sources

Current confirmed sound mods:

- Workshop `3365122622`: current Ruediger/Tief sound.
  - `Tief`
- Workshop `2758773086`: aircraft/GPWS/RWR sound pack.
  - F/A-18 style: `F-18Altitude`, `F-18Bingo`, `F-18PullUp`, `F-18EngineFireLeft`, `F-18EngineFireRight`
  - Generic RWR: `RWR-NewContact`, `RWR-SpecialContact`, `RWR-TrackingAndTargeting`, `RWR-TrackingAndTargetingLong`, `RWR-MissileLaunchWarning`, `RWR-MissileLaunchWarningLong`
  - F-16 style: `CAP_F-16_NewContact_Air`, `CAP_F-16_NewContact_Ground`, `CAP_F-16_RWR_Lock_Short`, `CAP_F-16_RWR_Lock_Long`, `CAP_F-16_RWR_Launch_Short`, `CAP_F-16_RWR_Launch_Long`, `CAP_F-16_Caution`, `CAP_F-16_Warning`, `CAP_F-16_MasterCaution`, `CAP_F-16_Bingo`

Explicitly excluded:

- `AIM9Search`
- `AIM9Lock`
- Any continuous or looping seeker tone

Missile search and missile lock sounds are intentionally omitted because they are annoying in normal flight. Missile sounds may only return later as short one-shot launch/fire confirmations if the user asks for that.

## Trigger Rules

The sound layer should trigger on state transitions, not every frame of a continuous condition.

| If this happens | Then play | Notes |
| --- | --- | --- |
| A new hostile/unknown target appears in the shared target list | `CAP_F-16_NewContact_Air` or `RWR-NewContact` | One-shot. Cool down so a cluster of new datalink/radar contacts does not spam audio. |
| The selected target changes | Short target-confirm cue, likely `RWR-NewContact` if no better cue exists | Optional and low priority. No AIM-9 lock/search tone. |
| RWR sees a contact become a threatening track | `CAP_F-16_RWR_Lock_Short` or `RWR-TrackingAndTargeting` | One-shot on escalation. Repeat slowly if threat persists. |
| RWR threat escalates to missile-launch style danger | `CAP_F-16_RWR_Launch_Short` or `RWR-MissileLaunchWarning` | Higher priority than normal RWR lock. May repeat on a slower cooldown while active. |
| Terrain/altitude warning trips | `F-18PullUp`, `F-18Altitude`, or fallback `Tief` | Highest priority safety alert. Repeats only while latched/active at a controlled interval. |
| Fuel crosses bingo/low-fuel threshold for the first time | `F-18Bingo` or `CAP_F-16_Bingo` | One-shot per threshold crossing until recovered/reset. |
| Engine-side failure/fire condition is detected | `F-18EngineFireLeft` or `F-18EngineFireRight` | Only if JetOS can infer side reliably from existing engine/propulsion data. |
| Generic serious aircraft warning appears without a specific voice cue | `CAP_F-16_Warning`, `CAP_F-16_MasterCaution`, or `Tief` | Used as fallback for high-priority safety states. |

The rule of thumb is:

```text
state transition happened -> play a short cue once
continuous state exists -> usually stay silent
severe state persists -> repeat only at a slow cooldown
```

## Architecture

`SoundManager` keeps the proven delayed sound-block state machine because Space Engineers sound blocks are sensitive to `Stop`, `SelectedSound`, and `Play` happening too close together. The current frame-delay behavior should remain.

Public callers should use compact semantic requests:

```csharp
SoundManager.Event(SoundManager.NEW_TARGET);
SoundManager.Event(SoundManager.RWR_LOCK);
SoundManager.Event(SoundManager.RWR_LAUNCH);
SoundManager.Event(SoundManager.PULL_UP);
SoundManager.Event(SoundManager.BINGO);
SoundManager.Event(SoundManager.ENGINE_FIRE_L);
SoundManager.Event(SoundManager.ENGINE_FIRE_R);
```

The implementation can use integer constants rather than an enum if that packs smaller.

Internally, each event maps to:

- channel
- sound ID
- priority
- cooldown seconds
- optional repeat behavior

The default profile is a mixed F/A-18 + F-16 profile:

- F/A-18 voices for pull-up, altitude, bingo, and engine fire.
- F-16/RWR cues for new contacts, RWR lock, and RWR launch.
- `Tief` as a fallback emergency cue.

## Channels

Preferred design is three logical channels:

| Channel | Block name filter | Purpose |
| --- | --- | --- |
| Warning | `Sound Block Warning` | Safety and aircraft voice alerts: pull-up, altitude, bingo, engine fire, master warning. |
| RWR | `Sound Block RWR` | Radar/RWR events: new contact, lock/track, missile launch. |
| Event | `Sound Block Event` | Low-priority one-shots such as selected target confirmation or future fire confirmation. |

If only one or two sound-block groups exist on the grid, the system should still function. Missing channels simply drop their events, or an event can fall back to the warning channel if it is safety-critical.

This avoids the old problem where RWR, GPWS, and weapon tones all compete on one sound block. It also avoids bringing back a weapon seeker channel for AIM-9 search/lock.

## Priority Model

Higher priority wins per channel for the current tick.

Suggested priorities:

| Priority | Events |
| --- | --- |
| 5 | Pull-up, terrain/altitude emergency, engine fire |
| 4 | RWR missile launch |
| 3 | RWR lock/track, master warning/caution |
| 2 | Bingo fuel, new target |
| 1 | Optional target confirm / low-value event |

Within equal priority, later request wins as the current manager already does. This lets a more specific event replace a generic one in the same tick.

## Event State And Deduplication

The caller or `SoundManager` must suppress repeated one-shots. To keep modules simple and code deduplicated, prefer `SoundManager` owning a tiny per-event cooldown table:

- `lastPlayed[eventId]`
- `cooldown[eventId]`

For transition-only events, callers should still only request when they detect a transition. The cooldown is a second layer of protection against datalink/radar churn.

Examples:

- New targets: compare current target identities against the previous tick's known set, then request `NEW_TARGET` only for the first new target in a cooldown window.
- RWR lock: request when `activeRwrThreatCount` changes from `0` to `>0`, or when a threat escalates from non-threatening to threatening.
- RWR launch: request when a future missile-launch heuristic is added, or when the current threat classifier enters its highest danger state.
- Pull-up: current altitude warning latch can request repeatedly, but `SoundManager` should limit replay to the event cooldown.

## Integration Points

Initial implementation should touch only the places that already know the relevant state:

- `SystemManager`: replace direct `RequestWarning("Tief", PRIORITY_ALTITUDE)` with a pull-up/altitude event.
- `RadarControlModule`: replace `RequestWarning("Alert 2", PRIORITY_RWR, 1.0)` with `RWR_LOCK` or `RWR_LAUNCH` once escalation is available.
- `Jet` or target-list owner: detect new target additions and request `NEW_TARGET`.
- Propulsion/status code: optionally add bingo and engine-fire events only if the existing data can support them without new expensive scans.

No module should hardcode aircraft sound IDs.

## Error Handling

- If no sound blocks match a channel filter, do nothing.
- If a selected sound ID is not present in the block's sound list, Space Engineers will simply not play the sound; JetOS should not spend runtime listing sounds every tick.
- The diagnostic script can remain the tool for manually listing and testing available sounds.
- Initialization should continue to stop/enable/clear sound blocks for known channels so first playback is reliable after recompile.

## Testing

Verification should include:

1. Build:

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT
```

2. Packed script length:

```powershell
(Get-Content -Path "$env:APPDATA\SpaceEngineers\IngameScripts\local\Mdk.PbScript2\script.cs" -Raw).Length
```

3. Manual sound-block test in-game:

- Add/rename sound blocks for `Sound Block Warning`, `Sound Block RWR`, and optionally `Sound Block Event`.
- Trigger low-altitude warning and confirm a pull-up/altitude cue.
- Present a new target and confirm one new-contact cue, not repeated spam.
- Trigger RWR threat and confirm RWR lock/track cue.
- Confirm `AIM9Search` and `AIM9Lock` never play.

## Non-Goals

- No AIM-9 search or lock tones.
- No continuous cockpit ambience.
- No runtime sound-mod discovery in the programmable block.
- No broad sound configuration UI unless needed later.
- No feature cuts to targeting, RWR, HUD, or datalink behavior.
