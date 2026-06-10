# JetOS System Guide

**What it does, what it's good at, and where a redesign would buy more capability.**

Audience: the project owner, other SE developers, and curious users. This is not a bug
report (see `docs/review-2026-06-10.md` for that) and not a size audit. It judges each
subsystem on **capability**: what it can do, what it only approximates, and where a
different *internal* design — within the PB script, within the 100K char / 50K
instruction budgets — would be genuinely more powerful. External mods/community scripts
are out of scope by deliberate choice.

Scope: `Mdk.PbScript2/` only (the jet script). The HQ station, Terminal viewer, and
plugins are separate projects.

---

## The one-paragraph version

JetOS turns a Space Engineers programmable block into a fighter avionics suite: a HUD
with pitch ladder, lead-computing gunsight and radar minimap; three themed MFD pages
(menu/status, damage synoptic, weapons); AI-block radar with STT lock and RWR; missile
bay management with in-flight IGC telemetry; rotor-turret gun control with a real
P+FF+D control law; canard AoA damping with stab spillover; a full-planet offline
terrain map with contour rendering; and a 3-hop mesh datalink that shares contacts,
wingman status, and HQ zones across a squadron. It runs every tick (60 Hz) inside SE's
~50,000-instruction budget and packs to under 100,000 characters after minification.

The recurring engineering idea that makes that possible: **throttle everything by
wall-clock age** (engine reclassification 1 s, resource metrics 0.25 s, ammo 0.5 s,
contact decay 1 s, thrust-max 0.5 s, datalink keyframes 5 s) and **pay construction
costs once** (cached sprites, pre-allocated buffers, single-site sprite construction,
one intercept solve shared by both turrets).

---

## Hard constraints everything lives under

These are the walls; every judgment below is relative to them.

| Constraint | Value | Consequence |
|---|---|---|
| Packed script size | 100,000 chars (now ~97.6K — ~2.3K headroom) | New features compete for ~2K chars. A new full-frame MFD page costs **~620 chars just for the page mechanism** (measured) — at most 2–3 more, ever. |
| Instruction budget | ~50,000/tick | All three MFDs + HUD render at 60 Hz; richness is bought with throttling and caching. |
| Input surface | 9 toolbar buttons, discrete presses only | No held keys, no chords. 5 of 9 are reserved system-wide. This caps UI depth more than anything else. |
| Sound API | ~1 block action/tick, ops batched | Forces SoundManager's 3-tick state machine (~150 ms latency floor). **Off-limits for refactor.** |
| Sprite API | No clipping, no text measurement, no arcs/polylines | All layout is constant-tuned; curves come from the pre-baked `JetOS_*` sprite mod. |
| AI block radar | `UpdateTargetInterval` clamped ≥5 ticks, ~2.5 km range, closest-target priority | Position samples are quantized; everything downstream (velocity, gun lead) inherits that noise. |

Four designs in this codebase look like bugs but are verified responses to SE engine
behavior. **Do not "clean up":** the double-Main trigger guard, the SoundManager state
machine, deferred engine classification (ctor-time `GridThrustDirection` is stale), and
the sound-tick-runs-last ordering.

---

## Core: tick loop, recovery, timing

**What it does.** `Program.cs` is a 45-line shell; `SystemManager` orchestrates
everything 60×/s: advance clamped wall-clock time, cache cockpit/gravity reads, stream
terrain chunks, evaluate latched warnings with hysteresis (PULL UP unlatches only 20 kts
/ 40 m clear), route the 9-button input into the menu system, tick the active module
plus background modules, run sound last, record instruction telemetry (current/EMA/peak
shown live in every screen header).

**Good at.**
- *Surviving SE.* The double-Main guard makes toolbar input safe; clamped
  `TimeSinceLastRun` makes every duration lag- and pause-proof; tiered exception
  handling (NRE → re-Initialize to heal lost blocks after battle damage; anything else →
  Echo only so real bugs surface) is the right recovery split.
- *Honest warnings.* Dead-bands on both sides of every threshold — no chatter at the
  boundary.
- *Self-observability.* Permanent in-game profiler in the header (`IC/IA/IP`).

**Where a redesign is more powerful.**
- **Render decimation** — the single biggest instruction-budget lever in the project.
  Surfaces 1 and 2 change at ~1 Hz but are fully redrawn at 60 Hz. Alternating them at
  20–30 Hz frees a meaningful budget slice to pay for everything else in this doc, at
  imperceptible visual cost.
- **Stateful recovery.** An NRE rebuilds *all* modules — menu position, radar mode, bay
  selections are lost mid-fight. Re-fetching blocks into existing objects would preserve
  pilot state.
- **A 2–3 deep input ring** instead of the single `_pendingArgument` — two presses in
  one sim tick currently lose one.

*Quality:* deliberate god-object/service-locator; the right call in PB-land. The one
fragility is that tick order in `Main` is load-bearing and only enforced by comments.

---

## Jet: hardware abstraction + track database

**What it does.** One object knows what the aircraft is made of (cockpit, engines
classified left/right/center × atmo/AB — re-classified every second so battle damage
and merges self-heal, bays, tanks, batteries, guns) and owns the tactical picture:
every detection flows into `UpdateOrAddEnemy`, which dedupes in three tiers (EntityId
O(1) → name → 50 m proximity), smooths acceleration with an EMA, keeps a 30-bit
per-second track-history bitfield per contact, and decays contacts at 30 s (60 s if
pilot-selected).

**Good at.**
- *Track hygiene.* The dedup refuses to proximity-merge contacts with different known
  EntityIds; a source-priority guard stops a relayed datalink hop from overwriting a
  fresh local STT track; identity-based selection (id+name+source) means your selected
  target never silently becomes a different aircraft when the list reorders.
- *Cheap history.* 30 s of track quality in 4 bytes, shift-on-read — zero per-tick cost,
  and it back-dates datalink contacts by their relay latency.
- *Tiered caching.* One cockpit read per tick fans out everywhere; each cached metric
  refreshes at the rate it actually changes.

**Where a redesign is more powerful.**
- **Shared dead-reckoning** — the highest-value capability change in the whole script.
  Contacts serve raw last-seen positions; every consumer that wants extrapolation (HUD,
  guns) reimplements it or skips it. A single `PredictedPosition(now)` =
  pos + v·age + ½a·age² (capped at a few seconds) on the contact would make *every*
  display and weapon consumer track-smooth between radar updates, using the
  acceleration EMA that is currently computed and then mostly ignored.
- **Measured fuel flow.** Time-remaining is `fillRatio × 600 s` — a guess. An EMA of
  d(filled)/dt turns the bingo display from decorative into navigational.
- **Track-quality gating.** The history bitfield already encodes quality (popcount);
  exposing it would let weapons refuse a missile launch on a 3-of-30 track.
- Contact-capacity scaling (spatial hashing etc.) is *not* needed at fighter scale.

*Quality:* airframe hardware and track database in one class is unconventional but
right here — both need to be reachable from everywhere. `UpdateOrAddEnemy` is the most
invariant-dense code in the project; keep its comments current.

---

## Module framework, menus, input

**What it does.** Every feature is a `ProgramModule`: `GetOptions()` + `ExecuteOption()`
gets you a fully-themed menu screen for free; optional hooks intercept navigation, back,
hotkeys, or take over the screen with a custom page. The main menu auto-builds from
module names — adding a module is genuinely three steps.

**Good at.** The consume-or-fall-through input contract (return `bool` to claim a key)
gives layered input ownership with zero event-system machinery. The framework is 40
lines and all six modules follow it identically.

**Where a redesign is more powerful.**
- **Clamp `currentMenuIndex` against the live option count at execute time.** Menus
  with dynamic length (bays, contacts) can leave the cursor out of range; one line kills
  the whole bug class.
- **A double-press-confirm helper** for destructive options ("Reset All" currently fires
  on one press).
- Note the implicit trap for new modules: digits are delivered to *both* the nav handler
  and `HandleSpecialFunction` — every new module author rediscovers this.

Otherwise at its practical ceiling: the 9-button reality, not the framework, is the
limit.

---

## Configuration system

**What it does.** Category → parameter → adjust, ~21 tunables (warning thresholds, gun
gains, canard, HUD toggles, theme) with min/max/step/default declared in one line each;
persistence as `Config_*` lines in CustomData, surviving recompiles; modified values
marked `*`. It also owns and ticks the gun and canard subsystems.

**Good at.** The declarative parameter table — clamping, reset, persistence, and UI all
come free per entry. Persistence is CRLF-safe and preserves foreign CustomData lines.
Live-edit means gain tuning takes effect while you fly.

**Where a redesign is more powerful.**
- **Revert-on-back** (~4 lines): snapshot the value entering the editor, restore on key
  4. Today "cancel" doesn't exist — backing out keeps un-saved edits until recompile.
- **A named-option parameter type** (generalize the theme hack) so future enums (units,
  radar modes) join the framework instead of forking it.

Otherwise at ceiling for a 4-button float editor. *Quality:* its moonlighting as the
gun/canard lifecycle owner surprises readers expecting a settings screen.

---

## CustomData cache + persistence

**What it does.** Wraps the PB's one persistent string as a key:value dictionary;
writes rebuild the string only when a value actually changed (critical — `Me.CustomData`
writes are network-synced, and the target cache writes at 60 Hz); external edits are
detected by throttled string compare.

**Good at.** Change-suppressed writes and the documented `MarkDirty()` contract for
bypassers. Essentially at ceiling for its job.

**One lurking trap worth fixing:** the dict rebuild drops any line that isn't
`key:value`, while ConfigurationModule's direct writer preserves them — two persistence
paths with different preservation semantics. ~6 lines to unify; removes a foot-gun
rather than adding a feature.

---

## Sound system

**What it does.** Three logical channels (warnings, RWR tones, events) with named-block
fallback routing, per-tick priority arbitration (ALTITUDE > RWR > LOCK > SEARCH),
per-event cooldowns, and loop-for-duration semantics — driven through a 3-tick
`idle→stop→select→play` state machine because SE silently drops block actions issued
faster than ~1/tick.

**Good at.** The hard-won details: cooldowns latch when a sound *actually plays* (a
request that loses arbitration doesn't burn its cooldown); loop durations are captured
at decision time so a mid-transition new request can't corrupt the playing sound;
continuous warnings re-loop seamlessly.

**Where a redesign is more powerful** (request layer only — the state machine is
off-limits by hard-learned project policy):
- **A one-slot deferred queue per channel.** A one-shot event (NEW CONTACT, LAUNCH)
  arriving while a transition is in flight is currently lost unless its source
  re-requests. Remember the best loser and replay it when the channel idles.
- **Data-drive the event table.** `lastEvent` is sized 9 with a fail-open guard — event
  id 9+ would silently bypass its cooldown.

The ~150 ms latency floor is SE physics; not improvable from inside the PB.

---

## HUD: flight control + glass

`HUDModule` is two systems sharing a file: a fly-by-wire layer and the HUD compositor.

### Flight control (throttle, balancing, auto-gun)

**What it does.** A virtual throttle (W/S, 0.6/s ramp) drives atmospheric thrusters
with left/right balancing normalized to the *weaker* side (asymmetric engine damage
produces no yaw); hydrogen afterburners gate behind an 80% MIL detent (release-re-press
W, or hold ~0.67 s, to punch through; 2% disengage hysteresis; hard-zero AB override
when off). Doors act as airbrakes; gatlings auto-enable only when the nose overlaps the
lead pip — using *the same* spawn-delay-compensated math as the pip, so the SHOOT cue
and the guns cannot disagree.

**Good at.** The AB gate is a genuinely complete state machine; the pip/gun/bracket
temporal consistency (`(V_tgt − V_ship) × dt` applied identically in three places) is
the project's best systemic-correctness story.

**Where a redesign is more powerful.**
- **True load-factor G** (one line): current G is |dV/dt|/9.81 — level flight reads
  ~0 G, free-fall ~1 G. Subtract gravity from the acceleration vector first.
- **Zero-g attitude fallback**: pitch/roll exist only in gravity; snapshot a reference
  frame when gravity drops out and the horizon works in space for one cached matrix.
- **Config-expose the FOV scale** (`COCKPIT_FOV_SCALE_Y = 0.31`): pip alignment is tuned
  to one cockpit; other seats misalign with no calibration path.
- **Name-scope airbrakes** — currently claims *every* door on the construct.
- A speed-hold mode (target KPH, P-controller) fits the architecture cheaply.

*Quality:* actuation living inside a "renderer" module is the project's biggest layering
smell; the partial-file split keeps it readable anyway.

### HUD renderers (horizon, instruments, minimap, targeting)

**What they do.** F-18-style glass: pitch ladder (culled to ~5 visible rungs, one
pre-baked texture each, one shared rotation pass), roll-stabilized flight-path marker
with off-screen tether; speed/alt tapes from one parameterized tape function, compass,
AoA indexer with 4-level stall escalation that scales the stall threshold down with
airspeed, energy-rate carat (a real BFM tool); a heading-up radar minimap whose range
auto-scales with *speed* (constant time-to-edge — a 300 m/s pass and a 50 m/s pass show
the same seconds of forward view) and colors contacts by computed time-to-closest-
approach, not distance; and the gunnery layer — bullet-drop-corrected lead pip,
distance-scaled funnel, range/closure/aspect bracket, edge-clamped off-screen arrow,
datalink friendly brackets, PULL UP / BREAK AWAY cross.

**Good at.** Sprite economy throughout (visible-range culling, one-texture rungs,
run-length-batched track timelines); kinematic honesty (TCA-based threat color, aspect
angle, aimpoint-vs-intercept distinction so guns-on-pip actually hits).

**Where a redesign is more powerful.**
- **The selected target vanishes from the HUD when no gun solution exists.** Pip,
  funnel, *and target bracket* all sit inside `if (hasIntercept)` — a target receding
  faster than muzzle velocity renders nothing. Decoupling the bracket and off-screen
  arrow from the intercept gate is a small change that removes the most significant
  functional gap in the HUD.
- **Minimap altitude dimension**: relative altitude is computed and discarded. A ▲/▼
  chevron per contact is one dot product on data already in the buffer.
- **Friendlies on the minimap**: they exist only as world-view ghosts; the projection
  loop and the datalink data are both already in the same file.
- **Compass target-bearing caret**: the delta-heading projection and the bearing
  function already exist; one sprite connects them — cheapest high-value add in the HUD.
- **Aural stall warning**: the 4-level visual escalation never calls SoundManager,
  though the channel and priority slot exist.
- **True tracer funnel**: sample the actual ballistic arc at 3–5 ranges via
  BallisticsCalculator instead of the `range/2000` trapezoid heuristic.
- Missile employment cues (Rmax/Rmin) are absent everywhere — see Weapons.

---

## MFD/UI stack

**What it does.** A three-layer pipeline: `MfdPage` contract (modules declare title/
breadcrumb/footer/content hooks) → one `UIController.Render` (chrome, breadcrumb, menu
or content, sidebar, border, in deterministic order) → `SpriteBus`, a 40-line chokepoint
every themed sprite passes through. The chokepoint is what makes the page-fade
transition possible: last tick's sprite stream is captured and replayed drifting
outward/desaturating for 0.3 s on module switch. Menu highlight slides between rows
(interrupt-safe — a fast double-press continues from mid-flight). The damage page
builds a top-down silhouette of the jet from terminal-block positions, colored by
per-block integrity, rebuilt via a staggered 3-phase state machine and replayed from
cache every tick; it even subtracts 17 blocks per fired missile so launching ordnance
doesn't read as battle damage. The sidebar stacks fuel/battery cards (eased bars),
a live missile-bay grid with in-flight ETA + seeker state, and a terrain minimap, each
degrading gracefully when hardware or vertical space is missing.

**Good at.** This is unusually well-layered for a PB script — the transition system was
added without touching a single renderer, which is the architectural payoff in one
sentence. Chrome is ~17 fixed sprites/surface; theme re-skin is a single palette block.

**Where a redesign is more powerful** (in leverage order):
1. **A global transform/alpha/tint state inside `SpriteBus.Add`** (~10 lines, one
   chokepoint): enables incoming-page transitions (today only the dying page animates),
   master-warning screen shake, night-mode dimming, and damage flicker — across every
   surface simultaneously, with zero new-page tax and zero renderer changes. The
   highest-leverage UI upgrade available.
2. **Richer menu-item model parsed inside the shared `DrawMenuList`** (sentinel prefix
   chars for right-aligned value / warning color / dimmed-disabled): upgrades *every*
   module's screen at once — Config and Radar gain status-colored value columns for
   free. The size-budget math forbids new custom pages; this is the alternative that
   doesn't pay the +620-char tax. Add menu scrolling here too (today ~9–12 options
   overflow into the footer with no clamp).
3. **Armor in the damage silhouette**: `IMyCubeGrid.Min/Max` + `CubeExists` are
   PB-callable; the staged-scan machinery to amortize the cost already exists. The
   current silhouette is the functional skeleton only — armor hits are invisible.

Direction explicitly *not* worth pursuing: per-module custom pages (page tax × ~2.3K
headroom forbids it) and side-view damage projection (double the cache for modest
payoff).

---

## Radar + RWR

**What it does.** *(Note: CLAUDE.md still describes the V1 multi-radar pool — the live
code is V2.)* One AI Flight + AI Combat pair acts as a pure sensor: the combat block's
targeting AI runs in radar-only mode (the flight block stays disabled so the autopilot
never flies the plane) and target position is read back through the waypoint list the
combat block pushes into the *inactive* flight block — a genuinely clever extraction
trick. Velocity comes from timestamped sample pairs with NaN guards and a 1 s
extrapolation cap. If the JetOSExtensions plugin is present, a multi-contact "JORAD"
feed becomes the primary source and STT can be requested on the *selected* target. RWR
is a real TCA/CPA computation (closing >250 m/s, time-to-approach <60 s, miss distance
≤800 m), not a proximity beep.

**Good at.** Navigating SE's AI-block minefield correctly (activation cooldowns,
radar-only mode, `DetailedInfo` parsed only on target change, dual-path lock detection
with the flicker rationale documented).

**Capability ceiling to be honest about.** Onboard: one track, ~2.5 km, closest-target
priority (not your selected target — `IsTrackLocked` goes false whenever the AI looks
elsewhere), position quantized to ≥5-tick samples. RWR infers from *known* contacts
only — SE exposes no emissions API, so a missile from an undetected shooter produces no
warning. These are mostly SE walls, not design choices.

**Where a redesign is more powerful.**
- **Filter the STT stream** (alpha-beta, or just use Jet's existing acceleration EMA in
  extrapolation — the doc comment already claims it does and it doesn't): directly
  improves gun lead and missile cues, attacking the 5-tick quantization at its source.
- **RWR threat vector instead of a boolean**: the worst-TCA contact and its bearing are
  already computed in the loop; surfacing them gives the HUD a threat azimuth marker.
- **Restore multi-pair support** (the V1 pool idea) for 2+ simultaneous onboard tracks
  when the plugin is absent — V2's single-pair simplification traded that away.

---

## Weapons: missiles + bays

**What it does.** The Weapons menu lists merge-block bays with selection checkboxes;
firing writes the target GPS into CustomData (global + per-bay slots) *before* the
merge releases — no race — then streams live target pos/vel to each bay's IGC channel
every tick and listens for telemetry back (position, ETA, seeker-acquired, active),
which renders as moving missile icons on the terrain map and live ETAs in the bay grid.
Hotkey 5 quick-fires the first connected bay.

**Good at.** The closed telemetry loop — fire-and-watch with per-bay ETA and seeker
state is something almost no SE script does. Typed tuples on cached channel names keep
the per-tick cost flat. Backward-compatible status parsing (5- and 6-field).

**Where a redesign is more powerful.**
- **Launch-acceptability cues (DLZ).** Nothing stops or informs a hopeless launch — no
  range, closure, or aspect check anywhere, and no Rmax/Rmin on the HUD or weapon
  screen. With target range, closure, and configured missile speed, a rough DLZ is pure
  arithmetic on data already in hand. This is the single biggest capability gap in the
  weapons stack.
- **Per-bay target assignment.** All bays chase the one selected enemy; the per-bay
  channels and the contact list both already exist — only selection UI and per-bay GPS
  bookkeeping are missing. This would turn one jet into a multi-target shooter.
- **Unique missile IDs** (needs a missile-side change too): telemetry is keyed by bay
  number, so a re-loaded bay's second missile collides with the first in the status
  table.
- Cone/salvo approach geometry was tried and deliberately abandoned (the geometry asks
  for tighter turns than the missile can pull) — don't re-litigate without new missile
  kinematics.

---

## Guns: turret control + ballistics

**What it does.** Two mirror-mounted rotor+hinge gatling turrets auto-track the selected
target inside a 15° nose cone: one ship-level intercept solve per tick (closed-form
quadratic in the relative frame, gravity drop folded in, earliest-positive root), then
per-gun parallax aiming driven by P (heading error) + ship-rotation feedforward +
target-LOS-rate D-term, with mounting geometry (yaw/pitch signs) derived once at
construction so the same code drives both sides.

**Good at.** This is about as good as a 1-tick-discrete control law gets on SE rotors —
the D-term history reset on target switch (kills derivative kick), the relative
degenerate-case epsilon in the quadratic (an absolute epsilon would never fire at
bullet speeds), conditional RPM writes, and the exact spawn-delay match with the HUD pip
are all signs of a system that has been tuned against reality.

**Where a redesign is more powerful.**
- **Use target acceleration in the intercept** — a single `+0.5·a·t²` term using the
  EMA the contact database already maintains. A 5 g target at 1 s time-of-flight is
  currently missed by ~25 m; this is the biggest accuracy win available, and it's one
  line.
- **Optional auto-fire**: `IsTracking && TTI < threshold` → shoot action; all the state
  exists, only the trigger is manual today.
- One or two fixed-point iterations of the gravity-corrected solve for long-arc shots
  (marginal at gatling ranges).

Beyond that: at the practical ceiling. The floor is SE rotor physics and the radar's
sample quantization, not the math.

---

## Canard AoA damping

**What it does.** Two animated canards deflect opposite AoA (with a sideslip
cross-coupling term deflecting them differentially); when commanded deflection exceeds
the ±45° hardware limit the excess spills onto the main stabilizers, and authority hands
back to pilot trim exactly once when the canards suffice again. The pilot just feels the
jet resist AoA build-up while trim keys keep working underneath.

**Good at.** The spillover/handback ownership scheme — graceful authority extension
without ever fighting the pilot. Smallest, cleanest module in the set.

**Where a redesign is more powerful.**
- **Gain scheduling by airspeed** (divide by v² with a floor, a few lines): one gain
  currently serves the whole envelope — too soft slow, twitchy fast.
- **A pitch-rate D-term** (from cockpit matrix deltas, same trick as the guns): pure
  proportional resists displacement but doesn't damp oscillation.

Beyond that, at ceiling: SE offers no direct aero-force API; this rides the mod-added
`Trim` property.

---

## Terrain: heightmap + moving map

**What it does.** With the companion mod, the jet downloads the *entire planet's*
heightmap once per session (chunked, instruction-budget-aware, chars-as-int16 encoding)
and then does instant offline lookups forever. The Terrain page is a track-up moving map
rendered by real marching-squares contouring (full 16-case table, edge interpolation,
saddle handling) colored by clearance relative to *your own altitude* — the
operationally correct question ("what can I hit"), not "what is tall" — with 9 zoom
levels, labeled peaks, range rings, sub-cell-smooth panning, a forward AGL profile
strip, and overlays for hostiles, missiles in flight, wingmen, neutral contacts, and
HQ zones. Equirectangular distortion is corrected by latitude so the map stays metric
off the equator. Under the 350-sprite budget, danger contours always complete before
cosmetic ones degrade.

**Good at.** This is the most technically ambitious subsystem and it shows: contour
rendering, threshold-major sprite triage, and zero steady-state allocation in the
render path are all things you don't expect to see on a PB.

**Ceiling to be honest about.** 200 m cells (ridgelines and canyons are invisible;
nearest-cell AGL steps laterally); the map is a static snapshot (craters and mining
never appear); silently disabled without the mod.

**Where a redesign is more powerful.**
- **Predictive terrain warning** — the highest-value addition in the whole project. The
  14-sample forward profile is already computed every frame; integrating it against
  current descent rate gives a real "PULL UP" (sound + HUD) instead of the current
  simple altitude gate. All data exists; only the comparison is missing.
- **Bilinear height interpolation** for AGL and the profile (4 lookups + a lerp):
  smooths the 200 m quantization exactly where it matters.
- **Contour-segment caching** (recompute only when moved >½ cell or zoom changed) frees
  instructions for denser sampling near the ship.

---

## Datalink + map contacts

**What it does.** Every jet broadcasts at 5 Hz on one IGC channel: a 64-bit packed
status word (fuel, battery, engines, ordnance, altitude, RWR/lock/bingo flags) plus
contacts it has *personally* seen in the last 3 s. Jets relay each other's contacts up
to 3 hops with age-based freshness (no cross-grid clock sync needed — receivers
reconstruct observation time from carried age), so one wingman's detection appears on
the whole squadron's maps. HQ broadcasts orders text and named zones (circles and
polygons, one vertex per broadcast round under the fixed envelope, tombstone deletion).
Non-hostile contacts live in a separate store so weapons can never select a neutral.
The jet-side DL page is deliberately a plain menu list (a rich page measurably doesn't
fit the size budget; that job belongs to the separate Terminal project).

**Good at.** The protocol engineering: one typed tuple envelope for every message kind
(survives full minification — verified), movement-or-keyframe send dedup, hop-limited
relay with local-authority preservation (only locally-observed contacts originate, so
echoes can't amplify), out-of-order rejection. This is a real mesh network in a toy
scripting environment.

**Where a redesign is more powerful.**
- **Cheap authentication** — the open hole: anyone who learns the channel string can
  inject false contacts or spoofed HQ orders. Salting the channel name or XOR-ing a
  shared secret into the packed field costs a few chars and closes it.
- **Wingman target deconfliction**: friend records already carry their current target
  id; drawing "wingman X is on contact Y" needs zero new wire traffic.
- **Velocity-extrapolated rendering** of datalinked contacts between 0.2 s updates —
  velocity is already in every record; drawn positions are raw.
- **A "request STT" message tag** so a plugin-less jet can cue a wingman's radar — the
  envelope has spare capacity.

*Quality:* the most contract-heavy code in the repo — bit layouts couple to the HQ and
Terminal projects, so wire changes are multi-project edits; the inline field-mapping
tables are what keep that tractable. `Datalink.cs` (v1) survives only as the shared
`Node` struct; the v1 protocol is fully gone.

---

## The short list

If you only act on five things from this document, ranked by capability-per-effort:

1. **Shared dead-reckoned contact position** (`Jet`) + **acceleration term in the gun
   intercept** — one design change, two consumers, directly improves everything that
   aims or displays a contact.
2. **Predictive terrain pull-up** — profile data already computed; turns a display into
   a safety system.
3. **Missile DLZ / launch-acceptability cues** — the weapons stack's only real
   employment gap, and it's pure arithmetic.
4. **SpriteBus global transform/alpha** + **richer shared menu items** — the two UI
   upgrades that dodge the +620-char page tax and improve every screen at once.
5. **Un-gate the selected-target bracket from `hasIntercept`** + **render decimation of
   surfaces 1/2** — one removes the HUD's biggest functional gap, the other pays the
   instruction bill for the rest of this list.

And the standing reminders: don't touch the four constraint-driven "looks like a bug"
designs; don't add custom MFD pages; the binding constraint on everything above is the
~2.3K-char packed headroom, so each item should be costed against the checkpoint before
implementation.

---

*Generated 2026-06-10 from a full read of `Mdk.PbScript2/` (excluding `Diagnostics/`
and build-excluded files). Companion docs: `docs/architecture.md`,
`docs/review-2026-06-10.md`, `docs/se-api-reference.md`, `docs/datalink-v2.md`.*
