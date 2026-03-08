# Sound Pipeline Debug Report

Complete trace of every call from seeker activation to sound playback.
Use this to find why the first sound cycle doesn't play.

---

## 1. SE Runtime Model

Space Engineers calls `Program.Main()` potentially **twice per simulation tick** when a toolbar button is pressed:

| Call | `UpdateType` | `argument` | When |
|------|-------------|------------|------|
| 1 | `Trigger` | `"3"` (the button) | User pressed toolbar |
| 2 | `Update1` | `""` (empty) | Every sim tick (UpdateFrequency.Update1) |

Both calls happen in the **same simulation tick**. SE applies all block property changes (Stop, SelectedSound, Play, etc.) at the **end of the simulation tick**.

On normal ticks (no button press), only the `Update1` call happens — one call per sim tick.

---

## 2. Program.Main (Program.cs:27)

```
Program.Main(argument, updateSource)
  └─ try { SystemManager.Main(argument, updateSource); }
     catch NullReferenceException → SystemManager.Initialize(this)  ← SILENT REINIT
     catch Exception → log only
```

**Note:** NullReferenceException triggers a full reinit. If any sound code throws NullRef, the entire system reinitializes silently, resetting all state including SoundManager channels.

---

## 3. SystemManager.Main (SystemManager.cs:142)

This is the full execution order, annotated with line numbers:

```
SystemManager.Main(argument, updateSource):
│
├── L144: currentTick++                          ← increments EVERY call, not every sim tick
├── L145: Jet.GameTicks++
│
├── L147-153: Read cockpit position, velocity, altitude
│
├── L155-176: ALTITUDE WARNING CHECK
│   └── If warning active: SoundManager.RequestWarning("Tief", PRIORITY_ALTITUDE)
│       ← This runs BEFORE the early-return guard, so it runs on EVERY Main() call
│
├── L178-180: EARLY RETURN GUARD
│   │   if (currentTick == lastHandledSpecialTick) return;
│   │   lastHandledSpecialTick = currentTick;
│   │
│   └── BUG: currentTick was just incremented on L144, so this NEVER returns early.
│       Both Trigger and Update1 calls always pass this check.
│       This guard is effectively dead code.
│
├── L182-189: INPUT HANDLING
│   ├── Empty argument → DisplayMenu()
│   └── Non-empty → HandleInput(argument)
│       └── "3" → ExecuteCurrentOption()
│           └── If in AirtoAir module → AirtoAir.ExecuteOption(currentMenuIndex)
│
├── L191-194: CURRENT MODULE TICK
│   └── currentModule.Tick()          ← AirtoAir.Tick() if it's the active module
│
├── L196-200: ALWAYS TICK (raycast + HUD)
│   └── raycastProgram.Tick()
│   └── hudProgram.Tick()
│
├── L202-205: BACKGROUND TICK RADAR (if not current module)
│   └── radarControlModule.Tick()
│
├── L207-210: BACKGROUND TICK AIR-TO-AIR (if not current module)
│   └── airtoAirModule.Tick()         ← AirtoAir.Tick() runs here if NOT the active module
│
├── L212-215: BACKGROUND TICK GUN CONTROL (if not current module)
│   └── gunControlModule.Tick()
│
├── L217: HandleSpecialFunctionInputs(argument)
│
├── L223: SoundManager.Tick(currentTick)          ← PROCESSES all sound requests
│
└── L225-241: FPS tracking
```

**Key observations:**
- `currentTick` counts Main() calls, NOT simulation ticks. On a button press, it increments twice.
- Altitude warning `RequestWarning` runs before the early return guard — runs on every call.
- Module Tick() calls run after the guard — also run on every call (guard is broken).
- `SoundManager.Tick(currentTick)` runs ONCE per Main() call, processing whatever was requested.

---

## 4. AirtoAir Activation (AirtoAir.cs:80-104)

When user presses "3" with `currentMenuIndex=2`:

```
ExecuteOption(2):
  ├── ToggleSensor()          ← L93: runs FIRST, isAirtoAirenabled is still OLD value
  └── ToggleAirtoAirMode()    ← L94: flips isAirtoAirenabled
```

### ToggleSensor (AirtoAir.cs:112-158)

Checks `isAirtoAirenabled` BEFORE it's toggled:

- **Turning ON** (isAirtoAirenabled was `false` → enters `else` branch L130):
  - Enables combat block, configures it, `ActivateBehavior_On`
  - Configures flight block, `ActivateBehavior_On`
  - **These are the SAME AI blocks used by RadarControlModule** (both use "AI Flight" / "AI Combat")

- **Turning OFF** (isAirtoAirenabled was `true` → enters `if` branch L114):
  - Disables combat block, `ActivateBehavior_Off`
  - Disables flight block, `ActivateBehavior_Off`
  - **This kills RadarControlModule's primary radar pair too!**

### ToggleAirtoAirMode (AirtoAir.cs:106-110)

```csharp
isAirtoAirenabled = !isAirtoAirenabled;
UpdateTopdownCustomData();
```

After this, `isAirtoAirenabled` is the NEW value.

---

## 5. AirtoAir.Tick (AirtoAir.cs:165-207)

```
AirtoAir.Tick():
│
├── L168-175: Auto-select closest enemy if none selected
├── L177-180: GPS sync if enemy selected
│
├── L183-186: if (!isAirtoAirenabled) return;     ← EXITS if seeker is OFF
│
├── L189-192: if (radarTracker != null)
│   └── radarTracker.UpdateTracking(SystemManager.currentTick)
│       NOTE: currentTick is Main()-call count, NOT real time ticks.
│       RadarControlModule passes accumulatedTimeTicks (real .NET time) to the same class.
│       This affects velocity calculation but NOT IsTracking.
│
├── L196-197: bool hasLock = (radarTracker != null && radarTracker.IsTracking) ||
│                            (myJet.radarControl != null && myJet.radarControl.IsTrackLocked)
│
└── L199-206: Sound request:
    ├── hasLock=true  → SoundManager.RequestWeapon("AIM9Lock",  PRIORITY_LOCK=2,   loopInterval=300)
    └── hasLock=false → SoundManager.RequestWeapon("AIM9Search", PRIORITY_SEARCH=1, loopInterval=300)
```

**`radarTracker` is null when:** `Jet._aiFlightBlock` or `Jet._aiCombatBlock` is null (no blocks named exactly `"AI Flight"` and `"AI Combat"` on the grid). If null, only `radarControl.IsTrackLocked` is used for lock detection. Sound request still happens regardless.

---

## 6. SoundManager.RequestWeapon (SoundManager.cs:93-102)

```csharp
public static void RequestWeapon(string sound, int priority, int loopInterval = 300)
{
    if (weaponChannel == null) return;
    if (priority >= weaponChannel.requestedPriority)    // >= means same priority overwrites
    {
        weaponChannel.requestedSound = sound;           // e.g. "AIM9Search"
        weaponChannel.requestedPriority = priority;     // e.g. 1
        weaponChannel.requestedLoopInterval = loopInterval; // e.g. 300
    }
}
```

This just STORES the request. No block operations. The request is processed later in `SoundManager.Tick()`.

---

## 7. SoundManager.Tick (SoundManager.cs:104-118)

```
SoundManager.Tick(currentTick):
│
├── TickChannel(warningChannel, currentTick)
│   ├── (runs state machine)
│   └── warningChannel.requestedSound = ""          ← RESET after processing
│       warningChannel.requestedPriority = 0
│
└── TickChannel(weaponChannel, currentTick)
    ├── (runs state machine)
    └── weaponChannel.requestedSound = ""           ← RESET after processing
        weaponChannel.requestedPriority = 0
```

**Critical:** `requestedSound` and `requestedPriority` are RESET to empty/0 after each Tick. `requestedLoopInterval` is NOT reset (keeps last value, default 300).

---

## 8. TickChannel State Machine (SoundManager.cs:120-209)

### Current states: 0=idle, 1=stopping, 2=selecting, 4=waiting, 3=playing

```
TickChannel(ch, currentTick):
│
├── PART A: Execute current state (switch on ch.state)
│   │
│   ├── case 0 (idle):     nothing (no case in switch)
│   ├── case 1 (stopping): block.Stop() on all → state=2
│   ├── case 2 (selecting): block.SelectedSound=pendingSound, block.Volume=vol → state=4 (or 0 if empty)
│   ├── case 4 (waiting):  nothing → state=3
│   └── case 3 (playing):  block.Play() on all → set activeSound, playStartTick, state=0
│
└── PART B: Check if sound should change (only runs when state==0)
    │
    ├── desired = ch.requestedSound (what was requested this tick)
    │
    ├── IF desired != activeSound:
    │   ├── desired not empty → pendingSound=desired, needsChange=true
    │   └── desired empty, active not empty → pendingSound="", needsChange=true
    │
    ├── ELSE IF desired == activeSound (both non-empty):
    │   └── IF currentTick - playStartTick >= activeLoopInterval → needsChange=true (re-trigger)
    │
    └── IF needsChange:
        └── ch.state = 1                  ← just marks state, NO block operations
```

### State transition diagram

```
                  needsChange
        ┌──────── detected ────────┐
        │                          │
        v                          │
   ┌─────────┐    Stop()     ┌─────────┐
   │ 1:STOP  │──────────────>│ 2:SELECT │
   └─────────┘               └─────────┘
                                   │
                                   │ SelectedSound=X
                                   v
                              ┌─────────┐
                              │ 4:WAIT  │  (1 tick gap)
                              └─────────┘
                                   │
                                   v
                              ┌─────────┐
                              │ 3:PLAY  │  Play()
                              └─────────┘
                                   │
                                   │ activeSound=X, playStartTick=now
                                   v
                              ┌─────────┐
                              │ 0:IDLE  │  ← waits here until change or loopInterval
                              └─────────┘
```

Each arrow = one Main() call (NOT one sim tick — could be 2 calls per sim tick).

---

## 9. Full Tick-by-Tick Trace: Seeker Activation

### Initial state (after PrepChannel at init):
```
weaponChannel:
  state = 0 (idle)
  pendingSound = ""
  activeSound = ""
  playStartTick = 0
  activeLoopInterval = 300
  requestedSound = ""
  requestedPriority = 0
  requestedLoopInterval = 300

blocks: Stop()'d, Enabled=true, SelectedSound="", Volume=0.3
```

### User presses "3" to toggle seeker ON (currentMenuIndex=2)

---

#### SIM TICK T — Button press (TWO Main() calls)

**Call 1 (Trigger, argument="3"):**
```
SystemManager.Main("3"):
  currentTick++ → N
  Altitude warning check → maybe RequestWarning("Tief")
  Early return guard: N != (N-1) → PASS
  HandleInput("3") → ExecuteCurrentOption():
    AirtoAir.ExecuteOption(2):
      ToggleSensor()        ← isAirtoAirenabled is false → else branch → enables AI blocks
      ToggleAirtoAirMode()  ← isAirtoAirenabled = true
  currentModule.Tick() → AirtoAir.Tick():
    isAirtoAirenabled = true → doesn't return early
    radarTracker.UpdateTracking(N)
    hasLock = false (probably, just enabled)
    SoundManager.RequestWeapon("AIM9Search", 1, 300)
      → weaponChannel.requestedSound = "AIM9Search"
      → weaponChannel.requestedPriority = 1
      → weaponChannel.requestedLoopInterval = 300
  [other module ticks — radar, gun, etc.]
  SoundManager.Tick(N):
    TickChannel(weaponChannel, N):
      PART A: switch(state=0) → nothing
      PART B: state==0, desired="AIM9Search", activeSound=""
        desired != activeSound, desired not empty
        → pendingSound = "AIM9Search", needsChange = true
        → state = 1                    ← NO block operations, just state change
    Reset: requestedSound="", requestedPriority=0
```

**Call 2 (Update1, argument=""):**
```
SystemManager.Main(""):
  currentTick++ → N+1
  Altitude warning check
  Early return guard: (N+1) != N → PASS       ← guard NEVER blocks, dead code
  DisplayMenu()                                ← empty argument
  currentModule.Tick() → AirtoAir.Tick():
    isAirtoAirenabled = true
    SoundManager.RequestWeapon("AIM9Search", 1, 300)
      → weaponChannel.requestedSound = "AIM9Search"
  [other module ticks]
  SoundManager.Tick(N+1):
    TickChannel(weaponChannel, N+1):
      PART A: switch(state=1) → STOPPING
        block.Stop() on all weapon blocks     ← STOP HAPPENS HERE
        state = 2
      PART B: state==2, not 0 → SKIP
    Reset: requestedSound="", requestedPriority=0
```

**End of SIM TICK T — SE applies block changes:**
```
Block operations this sim tick:
  - block.Stop()           (from Call 2, state 1→2)

That's it — only Stop(). Clean.
```

---

#### SIM TICK T+1 — Normal (ONE Main() call)

```
SystemManager.Main(""):
  currentTick++ → N+2
  AirtoAir.Tick() → SoundManager.RequestWeapon("AIM9Search", 1, 300)
  SoundManager.Tick(N+2):
    TickChannel:
      PART A: switch(state=2) → SELECTING
        block.SelectedSound = "AIM9Search"    ← SELECT HAPPENS HERE
        block.Volume = 0.3
        pendingSound not empty → state = 4
      PART B: state==4, not 0 → SKIP
    Reset
```

**End of SIM TICK T+1 — SE applies:**
```
  - block.SelectedSound = "AIM9Search"
  - block.Volume = 0.3
```

---

#### SIM TICK T+2 — Normal

```
  SoundManager.Tick(N+3):
    TickChannel:
      PART A: switch(state=4) → WAITING
        state = 3                             ← just waits, no block ops
      PART B: state==3, not 0 → SKIP
```

**End of SIM TICK T+2 — SE applies:** nothing

---

#### SIM TICK T+3 — Normal

```
  AirtoAir.Tick() → SoundManager.RequestWeapon("AIM9Search", 1, 300)
  SoundManager.Tick(N+4):
    TickChannel:
      PART A: switch(state=3) → PLAYING
        block.Play() on all                   ← PLAY HAPPENS HERE
        activeSound = "AIM9Search"
        activeLoopInterval = 300              ← from requestedLoopInterval
        playStartTick = N+4
        state = 0
      PART B: state==0
        desired = "AIM9Search", activeSound = "AIM9Search"
        desired == activeSound, both non-empty
        (N+4) - (N+4) = 0 < 300 → no change
```

**End of SIM TICK T+3 — SE applies:**
```
  - block.Play()
```

**Sound should now be audible.** 4 sim ticks after button press.

---

#### SIM TICKS T+4 through T+303 — Idle looping

```
Each tick:
  AirtoAir.Tick() → RequestWeapon("AIM9Search", 1, 300)
  TickChannel:
    PART A: state=0, nothing
    PART B: desired == activeSound, currentTick - playStartTick < 300 → no change

  The sound block is in "playing" state. Whether audio continues depends on
  whether the SE sound file loops internally or plays once and stops.
```

**If the sound file is NOT looping:** audio plays once (maybe 1-2 seconds), then silence until tick T+303.

---

#### SIM TICK T+303 — Loop re-trigger

```
  TickChannel:
    PART B: currentTick - playStartTick = 300 >= 300 → needsChange
      pendingSound = "AIM9Search"
      state = 1

  Then: state 1→2→4→3 over next 4 ticks → Play() again
```

---

## 10. Channel Configuration

### Warning channel (altitude, RWR)
```
Block name filter: "Sound Block Warning"
Volume: 1.0
Sounds used:
  - "Tief" (altitude warning, PRIORITY_ALTITUDE=4)
  - "Alert 2" (RWR threat, PRIORITY_RWR=3, loopInterval=60)
```

### Weapon channel (seeker tones)
```
Block name filter: "Canopy Side Plate Sound Block"
Volume: 0.3
Sounds used:
  - "AIM9Search" (seeker scanning, PRIORITY_SEARCH=1, loopInterval=300)
  - "AIM9Lock" (target locked, PRIORITY_LOCK=2, loopInterval=300)
```

---

## 11. What The Diagnostic Test Does Differently

The diagnostic (`SoundDiagnostic.cs`) uses `UpdateFrequency.None` at init and switches to `Update1` only during tests. It is triggered by a toolbar command. Key differences:

| Aspect | SoundDiagnostic | SoundManager |
|--------|----------------|--------------|
| Runtime | `Update1` only during test | `Update1` always |
| Trigger | Toolbar starts test, then tick-driven | Toolbar triggers input AND tick |
| Stop+Select | Different ticks (t==1 Stop, t==2 Select) | Currently: state 1 Stop, state 2 Select (separate calls but maybe same sim tick?) |
| Block ops per sim tick | At most 1 type of operation | Could be 2 if double Main() advances state |
| Sound selection | Direct, known sound name | Driven by radarTracker.IsTracking state |

### Diagnostic three-tick method (the one that works):
```
tickInStep==1: block.Stop()
tickInStep==2: block.SelectedSound = X, block.Volume = V
tickInStep==3: block.Play()
```
Each `tickInStep` value happens on exactly ONE Main() call. No double-call issue because the diagnostic only uses `Update1` (no toolbar trigger during the sequence — the toolbar starts it, then it's pure Update1).

---

## 12. Potential Issues

### Issue A: Double Main() call on button-press ticks
`currentTick++` runs on every Main() call. The early return guard (`currentTick == lastHandledSpecialTick`) compares after increment, so it **never fires**. Both Trigger and Update1 calls process fully, each advancing the state machine one step.

On the activation tick, the state machine goes 0→1 (Call 1) then 1→2 (Call 2). Stop() happens in Call 2. That means Stop() and the sound request reset both happen on the same sim tick. The SELECT happens on the next sim tick (T+1), which should be clean.

**Question:** Does this actually cause a problem? Stop() alone in one sim tick should be fine. But verify: does the second Main() call's `requestedSound` reset (line 115-116) happen AFTER TickChannel processes state=1? Yes — TickChannel runs first, then reset. So the request is available during TickChannel.

BUT: In Call 2, `requestedSound` was set by AirtoAir.Tick() earlier in that same call. Then TickChannel processes state=1 (Stop). During PART B, state is 2 (not 0), so PART B is skipped. Then reset clears requestedSound. So the request from Call 2 is consumed... no wait, it's NOT consumed. PART B didn't run. The request is just reset to "".

**On the next sim tick (T+1):** AirtoAir.Tick() requests again. TickChannel processes state=2 (Select). PART B is skipped (state=4). Request is reset. Select used `pendingSound` which was set back on Call 1 of sim tick T. This should be fine.

### Issue B: requestedLoopInterval on Play state
In state 3 (Play), `activeLoopInterval = ch.requestedLoopInterval` (line 166). This reads `requestedLoopInterval` which was set by RequestWeapon earlier in the same Main() call. Since AirtoAir.Tick() always runs before SoundManager.Tick() in the same call, this should have the correct value (300).

**But:** `requestedLoopInterval` is NEVER reset (unlike requestedSound and requestedPriority). It keeps the value from the last RequestWeapon call. If no request is made on the tick when Play executes, it retains whatever it was before. This is probably fine since AirtoAir always requests when seeker is on.

### Issue C: ToggleSensor shares AI blocks with RadarControlModule
AirtoAir's `radarTracker` wraps the same "AI Flight" / "AI Combat" blocks that RadarControlModule's `allRadars[0]` wraps. When seeker is toggled OFF, ToggleSensor disables these blocks, breaking RadarControlModule's primary scan/track pair until seeker is toggled back ON.

### Issue D: NullRef auto-reinit
If any code in the pipeline throws NullReferenceException, `Program.Main` catches it and calls `SystemManager.Initialize(this)`. This reinitializes EVERYTHING: new SoundManager channels, new AirtoAir (isAirtoAirenabled=false), new RadarTrackingModules. If this happens silently, it would look like the seeker "stopped working" until toggled again.

### Issue E: Loop interval of 300
After Play(), the state machine waits 300 Main() calls before re-triggering. On a button-press tick, that's ~299 sim ticks (~5 seconds at 60fps). If the SE sound file doesn't loop internally, there's 5 seconds of silence between each play.

---

## 13. Changes Made (current session)

### SoundManager.cs
1. **Added PrepChannel()** — called in Initialize(), stops all blocks and sets them to known state
2. **Added state 4 (waiting)** — 1-tick gap between Select (state 2) and Play (state 3)
3. **Removed inline Stop()** — change detection now sets `state=1` instead of calling `Stop()` + setting `state=2` directly. This was to prevent Stop+Select in the same sim tick.

### SystemManager.cs
4. **Moved SoundManager.Tick()** — from before module ticks to after all module ticks (line 223), so module requests are processed on the same tick they're made.

### AirtoAir.cs
5. **Decoupled sound from radarTracker** — sound requests now always happen when seeker is on, even if `radarTracker` is null. Lock detection uses both `radarTracker.IsTracking` and `radarControl.IsTrackLocked`.

---

## 14. File Reference

| File | Lines | What it does |
|------|-------|-------------|
| `Program.cs:27-50` | Main entry, NullRef catch + reinit |
| `SystemManager.cs:142-242` | Tick loop, call ordering |
| `SystemManager.cs:144` | `currentTick++` (every Main() call) |
| `SystemManager.cs:178-180` | Early return guard (broken/dead code) |
| `SystemManager.cs:223` | `SoundManager.Tick(currentTick)` |
| `AirtoAir.cs:80-104` | ExecuteOption dispatch |
| `AirtoAir.cs:93-94` | ToggleSensor + ToggleAirtoAirMode order |
| `AirtoAir.cs:112-158` | ToggleSensor — enables/disables shared AI blocks |
| `AirtoAir.cs:165-207` | Tick — sound request logic |
| `AirtoAir.cs:196-197` | hasLock detection (radarTracker + radarControl) |
| `SoundManager.cs:39-60` | Initialize + PrepChannel |
| `SoundManager.cs:93-102` | RequestWeapon — stores request |
| `SoundManager.cs:104-118` | Tick — runs TickChannel, resets requests |
| `SoundManager.cs:120-209` | TickChannel — state machine |
| `SoundManager.cs:123-170` | State switch (PART A) |
| `SoundManager.cs:172-209` | Change detection (PART B, only when state==0) |
| `RadarTrackingModule.cs:174-179` | IsTracking — checks FoundEnemyId |
| `RadarControlModule.cs:70` | IsTrackLocked property |
| `RadarControlModule.cs:77-112` | Constructor — creates RadarTrackingModules for same blocks |
| `Diagnostics/SoundDiagnostic.cs` | Standalone test — three-tick method works |
