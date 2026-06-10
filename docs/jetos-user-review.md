# JetOS — A Pilot's Review

*Third-party review, written from the seat, not the source code. The reviewer is a
Space Engineers player who flies fighters, has used the usual community scripts, and
does not read C#.*

---

## Verdict up front

**Impressive is the wrong word — it's the wrong *category*.** Most SE "fighter HUDs"
are a speed readout, an artificial horizon, and maybe a lead dot. JetOS is a cohesive
avionics suite: the HUD, the three cockpit screens, the sounds, the radar, the map,
and your wingmen's jets all behave like parts of one product. It has a *brand*
(everything is themed as NYINAH CORP equipment, down to the gold accent line and the
footer hints), and after an hour you stop thinking "this is a script" and start
thinking "this is the plane."

It is also clearly a real-world project, not a tech demo: it has the polish of
something flown daily and the rough edges of something built to a hard size limit.
Both show.

**Score: 9/10 as an experience. 7/10 as a product you hand to a stranger** — setup
expects you to name blocks exactly right, install companion mods, and learn a
9-button control scheme with no in-game tutorial.

---

## First contact: setup

Honest warning for new users: JetOS is not paste-and-fly.

- Blocks must be named *exactly* — "Jet Pilot Seat", "JetOS [HFPS]", "Fighter HUD
  [HFPS]", "Bay 1", "Gun Rotor Left"… Get a name wrong and that feature silently
  doesn't exist. There's no setup screen that says "I found 7 of 9 expected systems."
- Full experience needs companion mods (the sprite pack, the terrain provider) and
  ideally the extension plugin for the good radar. Without them things degrade
  *gracefully* — the terrain page politely tells you the mod is missing rather than
  erroring — but you're flying maybe 60% of the product.
- Controls are nine toolbar buttons doing everything: navigate, select, back, trim,
  cycle targets. It's a real MFD-button feel once learned, and genuinely awkward for
  the first session.

Once it's set up, though, it *stays* set up. Settings survive recompiles and world
reloads, and the script recovers itself when blocks get shot off and welded back.

---

## In the cockpit: what makes it feel expensive

**The throttle has a detent.** Hold W and the engines spool to a green "MIL" bar and
*stop*. To get afterburner you either release and tap W again, or hold it deliberately
for most of a second — and then the bar goes yellow and the hydrogen lights. It's a
tiny thing and it completely changes how the jet feels: you stop accidentally dumping
hydrogen, and pushing through the gate becomes a decision. This is the best
"game-feel" moment in the whole script.

**The jet doesn't yaw when an engine dies.** Lose half your engines on one side and
the script quietly throttles the good side down to match. You notice the thrust loss;
you don't notice asymmetry. Most players will never realize this is even happening,
which is exactly the point.

**Warnings behave like avionics, not like alarms.** "PULL UP" latches when you're low
and fast, and doesn't flicker off the instant you cross back over the line — you have
to properly clear the condition. Master caution and master warning blink at different
rates. Sounds never stutter or talk over each other; the most important one always
wins the speaker.

**The screens animate.** Switch pages and the old page visibly dissolves outward for
a third of a second. The menu highlight slides between rows instead of teleporting.
Fuel bars ease to their new value. None of this is necessary, all of it is why the
thing feels like hardware.

**The map is the headline feature.** The terrain page downloads the *whole planet*
once, then draws live contour lines colored by what matters: red terrain is above
you, green is below you. It pans smoothly, labels the peaks, shows your wingmen,
enemy contacts, missiles in flight, and HQ-drawn zones, and a strip along the bottom
shows the ground profile ahead of your nose. I have not seen anything else like it
running on a programmable block.

**Missiles report back.** Fire a missile and its bay tile on the side panel shows a
live countdown to impact and tells you when the seeker has its own lock. You watch
your shot fly on the map. Every other missile script I've used is fire-and-pray.

---

## A few really smart solutions

Things where you can tell someone *thought*, even without reading code:

1. **The radar minimap scales by your speed, not by distance.** Fly fast and it zooms
   out; fly slow and it zooms in — so the edge of the map is always roughly the same
   number of *seconds* away. Contacts are colored by whether they're actually going
   to be a problem (closing fast, on an intercept) rather than just by range. The map
   answers "who matters" instead of "who's closest."

2. **The terrain map colors by clearance, not by height.** A 2,000 m mountain you're
   above is painted calm green; an 800 m ridge you're below is red. The map answers
   the only question a low-flying pilot has: *what can I hit?*

3. **The gunsight and the guns cannot disagree.** The auto-aiming turrets and the HUD
   lead pip are computed from the same math, including the same one-tick timing
   correction. When the SHOOT cue lights, the bullets actually go where the pip is.
   You learn to trust it fast, and trust is the whole game for a gunsight.

4. **Firing a missile doesn't show up as battle damage.** The damage screen knows the
   17 blocks that just left the airframe were ordnance, not a hit. A small thing that
   says a lot about attention to detail.

5. **The contact list shows track *history*, not just contacts.** Each enemy has a
   little 30-second timeline showing exactly when your sensors have and haven't seen
   it — so "stale" isn't a guess, it's visible. Wingman detections flow into the same
   list over the datalink, hop through up to three jets, and the system still refuses
   to let secondhand data overwrite what your own radar just saw.

---

## What doesn't work (or works less than it looks like it should)

In rough order of how much each one actually hurt:

- **Your locked target can vanish from the HUD.** If the target runs away faster than
  your guns could ever catch it, the pip, the funnel, *and the target box* all
  disappear together. The one moment you most need to know where the bandit went —
  he's extending, you're deciding whether to chase — the glass goes blank. This is
  the gap I'd fix first.

- **Missiles will happily take hopeless shots.** There is no "in range" cue for
  missiles anywhere — no max range, no minimum, nothing. The HUD tells you when
  *guns* can make the shot, but the missile button always works and never warns you.
  New pilots will waste a lot of ordnance learning ranges by feel.

- **The horizon dies in space.** Leave gravity and the pitch ladder simply stops
  meaning anything. For a game where every flight eventually goes orbital, the HUD
  is atmosphere-only.

- **The G meter isn't a G meter.** Sit in level flight: it reads ~0. It actually
  measures how hard your velocity is changing, which is close enough in a turn and
  nonsense in cruise or free-fall. The peak-G readout is fun; just don't believe it
  the way you'd believe the speed tape.

- **The stall warning is silent.** There's a beautiful four-stage visual escalation
  (AOA → HIGH AOA → STALL, escalating colors and blink rates) — and not one beep.
  The jet has a warning speaker; the stall never uses it. You will be looking outside
  exactly when it matters.

- **Long menus fall off the screen.** Give a page more than about nine options and
  the extras render over the footer or off the panel. No scrolling. Similarly, the
  contact list stops at 10 and just says "+N" — in a big fight, the fight is
  literally not all on the screen.

- **There's no "cancel" in settings.** Adjusting a value applies it live (great for
  tuning), but backing out doesn't undo — your half-experiment stays until you save
  something else or recompile. Also, "Reset All" fires on a single press. I found
  that out the way you'd expect.

- **Open a hangar door, get an airbrake.** The airbrake function grabs *every* door
  on the ship. Ramps, canopies, the works. Name-filtering exists everywhere else in
  this script; doors didn't get the memo.

- **The damage screen can't see armor.** It shows functional blocks — engines,
  tanks, guns — going yellow and red, which is genuinely useful. But a jet stitched
  with bullet holes through plain armor looks pristine on the synoptic.

- **The map is a photograph, not a feed.** Terrain is downloaded once per session;
  the crater your wingman just made is not on it. Fine for navigation, worth knowing
  before you trust it at 50 m.

- **Fast fingers lose inputs.** Two button presses in the same instant and one is
  gone. You learn a deliberate, one-press-at-a-time rhythm. (Reportedly a game
  limitation as much as a script one — but you feel it either way.)

- **After a crash-recovery, the jet forgets where you were.** The script heals itself
  when blocks are destroyed — genuinely great — but it reboots to the main menu with
  default selections. Mid-dogfight, that's a screen of button presses at the worst
  time.

- **The squadron channel trusts everyone.** Anyone who knows the channel name can
  inject fake contacts or fake HQ orders into your map. In PvP against people who
  read workshop pages, that's not theoretical.

---

## Could it be worse?

Easily, and it's worth saying how. Nothing here ever stuttered, froze, or ate a tick
budget mid-fight; the instruction counter is *displayed on every screen* like the
developers dare you to check. Sounds never overlapped. The UI never flickered. After
losing a wing and most of one side's engines, the script rebuilt its own block list
and kept flying. The failure modes are missing features and blank corners — never
crashes, never garbage on screen. For a 100,000-character program running sixty times
a second inside a survival game, the *reliability* is the most impressive feature it
has, and the easiest one to overlook.

## Could it be better?

Yes — and the striking thing is that most of the list above is *cues, not systems*.
The data for a missile range cue exists. The terrain profile for a predictive pull-up
warning is already drawn on screen. The stall logic already knows it's escalating.
The target the HUD drops is still tracked perfectly on the weapons page. JetOS's
remaining problems are mostly the last inch between "the system knows" and "the pilot
is told" — which, for a project this deep into a hard size limit, is the most
flattering kind of problem to have.

---

*Reviewed in atmosphere and (briefly, regrettably) out of it. Score: 9/10 in the
seat, 7/10 out of the box.*
