# JetOS - Advanced Jet Control System for Space Engineers

This script provides a comprehensive operating system (JetOS) for fighter jets in Space Engineers, offering advanced flight control, targeting, HUD management, and weapon system integration.

## Features

* **System Manager:** Core handler for different modules and user interface.
* **Modular Design:** Supports various modules like:
    * Air-to-Ground (ATG)
    * Air-to-Air (ATA)
    * Targeting Pod Control (RaycastCameraControl)
    * Heads-Up Display (HUDModule)
    * Screensaver/Logo Display
    * Mini-game (Frogger)
* **Advanced HUD (`HUDModule`):**
    * Displays critical flight information: Speed (kph/knots/Mach), Altitude, G-Force (current & peak), Heading, Angle of Attack (AoA), Throttle percentage.
    * Artificial Horizon with pitch and roll indication.
    * Flight Path Marker.
    * Compass Tape.
    * Altitude Tape (F-18 Style) with Vertical Velocity Indicator (VVI).
    * G-Force Indicator.
    * Throttle Bar.
    * AoA Bracket and Trim information.
    * Radar indicator box.
    * Leading Pip calculation for gun targeting based on target position, velocity, and projectile speed.
    * Automatic stabilizer adjustment (PID-based) for maintaining Angle of Attack (AoA) based on user-defined offset.
    * Manual Gatling fire override.
* **Targeting Pod (`RaycastCameraControl`):**
    * Uses a designated camera to perform raycasts and identify targets.
    * Saves target coordinates to the Programmable Block's Custom Data (`Cached:GPS:Target2:...`).
    * Features GPS locking and target tracking using rotors/hinges.
    * Displays targeting information on a designated LCD.
* **Weapon Systems:**
    * **Air-to-Ground (`AirToGround`):** Manages missile bays (Merge Blocks named "Bay X"), allowing selection, firing, bombardment patterns, and top-down attack mode. Transfers GPS coordinates from `Cached:GPS:...` to bay-specific `CacheX:` entries before firing.
    * **Air-to-Air (`AirtoAir`):** Manages missile bays for air combat, integrates with a turret ("JetNoseRad") for target seeking/locking, and provides audio cues (AIM9Lock/AIM9Search). Caches target position and velocity (`Cached:GPS:Target2:...`, `CachedSpeed:...").
* **UI Controller:** Renders menus and information on multiple LCD panels of a cockpit. Supports main menu navigation and module-specific options/hotkeys.
* **Jet Class:** Encapsulates core jet components like Cockpit, Thrusters, Sound Blocks, Radars, Merge Blocks (Bays), Stabilizers, and HUD elements for easy access.
* **Sound System:** Provides audible warnings (e.g., low altitude/high speed) and target lock sounds.
* **Utilities:** Includes PID controllers, vector math functions, and navigation helpers.

## Setup

1.  **Load the Script:** Place the `JetOS file.cs` script into a Programmable Block on your jet.
2.  **Block Naming:** Ensure the following blocks are present and named correctly on the **same grid** as the Programmable Block:
    * **Cockpit:** "Jet Pilot Seat" (Must have `IMyTextSurfaceProvider`)
    * **Main OS Screen:** The cockpit named "JetOS" is used for the main UI (Surface 0) and secondary info (Surface 1).
    * **HUD Block:** A block named "Fighter HUD" (must implement `IMyTextSurface` or `IMyTextSurfaceProvider`).
    * **Thrusters:** Standard thrusters (non-"Sci-Fi"). Thrusters for reverse/braking need "Backward" in their `GridThrustDirection`. Hydrogen tanks need "Jet" in their name.
    * **Gatling Guns:** Standard `IMySmallGatlingGun` blocks.
    * **Sound Blocks:** At least one named "Sound Block Warning" for altitude/speed warnings. Others named "Canopy Side Plate Sound Block" for Air-to-Air sounds.
    * **Radar Turret(s):** At least one `IMyLargeGatlingTurret` named "JetNoseRad" for primary radar/targeting. Additional turrets named "Radar" can be used.
    * **Missile Bays:** `IMyShipMergeBlock` blocks named "Bay X" (e.g., "Bay 1", "Bay 2") sorted numerically.
    * **Stabilizers:** Blocks named "invertedstab" and "normalstab" for AoA control.
    * **Targeting Pod (Optional):**
        * Camera: "Camera Targeting Turret"
        * LCD: "LCD Targeting Pod"
        * Remote Control: "Remote Control"
        * Rotor: "Targeting Rotor"
        * Hinge: "Targeting Hinge"
    * **Airbrakes (Optional):** `IMyDoor` blocks.
3.  **Custom Data:** Add the following lines to the Programmable Block's Custom Data:
    * `Cached:GPS:` (Leave empty initially)
    * `CachedSpeed:` (Leave empty initially)
    * `Topdown:false` (Or `true` if you want ATG topdown by default)
    * `AntiAir:false` (Or `true` if you want ATA mode by default)
    * `CacheGPS0:` (Leave empty)
    * `CacheGPS1:` (Leave empty)
    * ... up to `CacheGPS3:`
    * `DataSlot0:` (Leave empty)
    * ... up to the highest missile bay number.
4.  **Compile & Run:** Check the code and run it.

## Usage

* Interact with the script via the cockpit's control panel (Toolbar actions assigned to the Programmable Block with arguments 1-9).
* **1:** Navigate Up
* **2:** Navigate Down
* **3:** Select/Execute
* **4:** Back/Deselect Module
* **5-8:** Module-Specific Hotkeys (See Module Hotkeys section in UI) or Special Functions (e.g., Raycast, Adjust Trim Offset)
* **9:** Return to Main Menu
* Use the UI displayed on the "JetOS" cockpit screens to select modules and execute actions.
* The HUD on the "Fighter HUD" block will display flight information automatically.
* Use the Targeting Pod module to acquire and lock GPS coordinates.
* Use the ATG/ATA modules to manage and fire weapons from the bays.

## Detailed Explanations

Here's a breakdown of how some key functionalities work:

* **HUD - Artificial Horizon (`DrawArtificialHorizon`):**
    * Calculates the vertical position of pitch lines based on the difference between the line's degree value (e.g., -80, -70 ... +70, +80) and the aircraft's current pitch, scaled by `pixelsPerDegree`.
    * Draws horizontal segments and angled "tips" for each pitch line, styling positive and negative pitch lines differently (like F-16/F-18).
    * Adds numeric labels next to the lines.
    * Draws a distinct, wider horizon line at 0 degrees pitch.
    * Adds a central aiming reticle (`-^-`).
    * Finally, rotates all drawn sprites (lines, labels, reticle) around the HUD center based on the aircraft's current roll angle to simulate the horizon tilting.
* **HUD - Flight Path Marker (`DrawFlightPathMarker`):**
    * Takes the aircraft's current velocity vector and transforms it into the cockpit's local coordinate system.
    * Calculates the yaw and pitch angles *of the velocity vector* relative to the cockpit's forward direction.
    * Determines the screen offset (X, Y) for the marker based on these velocity angles, scaled by `pixelsPerDegree`.
    * Rotates this screen offset based on the aircraft's roll angle.
    * Draws the central marker (circle) at the final calculated screen position (HUD Center + Rotated Offset).
    * Draws small horizontal "wings" attached to the marker, also rotated by the aircraft's roll.
* **HUD - Stabilizer PID Control (`AdjustStabilizers`, `PIDController`):**
    * Reads pilot pitch input (`cockpit.RotationIndicator.X`).
    * If the pilot is actively pitching (input > deadzone), the PID control is paused, and a delay counter (`pidResumeDelayCounter`) increases. Stabilizer trim might be set to 0 during this time.
    * If the pilot is *not* pitching:
        * If the pilot *just stopped* pitching, the PID's integral error is reset to prevent windup from the manual input phase.
        * If the delay counter is active, it counts down. Trim might still be held at 0.
        * Once the delay is over, the PID engages.
        * The **error** for the PID is calculated as the current Angle of Attack (AoA) plus a user-defined offset (`myjet.offset`). The goal is implicitly to drive this error towards zero (maintain the desired AoA).
        * The `PIDController` function calculates the output based on Proportional (Kp * error), Integral (Ki * accumulated error), and Derivative (Kd * change in error) terms.
        * The calculated PID output (clamped between limits) is applied as the `Trim` value to the left and right stabilizer groups (with opposite signs).
* **HUD - Leading Pip (`DrawLeadingPip`, `CalculateInterceptPointIterative`):**
    * **Intercept Calculation:** Uses an iterative method (`CalculateInterceptPointIterative`) to predict where a target (moving at a constant velocity) will be when the projectile reaches it, considering projectile speed, shooter velocity, target velocity, and gravity. It refines the time-to-intercept estimate over several iterations.
    * **Projection:** Calculates the direction vector from the shooter to the predicted intercept point. Transforms this world-space vector into the cockpit's local coordinate system.
    * **Screen Coordinates:** Uses perspective projection (dividing local X/Y by negative local Z and scaling) to determine where the intercept point appears on the 2D HUD surface.
    * **Drawing:**
        * Draws a central aiming reticle.
        * If the intercept point projects onto the screen bounds, it draws the pip (e.g., a circle) at that location.
        * If the intercept point is off-screen, it calculates the direction from the center to the off-screen point and draws an arrow indicator at the edge of the screen pointing towards the pip's direction.
        * If the target is behind the shooter (positive local Z), it draws a simple "behind" indicator instead.
    * **Gatling Control:** Compares the distance between the HUD center (reticle) and the calculated pip position. If the distance is within the pip's radius (meaning the player is aiming correctly), it enables the gatling guns (unless manual fire is active). Otherwise, it disables them (if manual fire is off).
* **Targeting Pod & Data Flow (`RaycastCameraControl`, `AirToGround`, `AirtoAir`):**
    * **Acquisition (Raycast):** The `RaycastCameraControl` module uses the "Camera Targeting Turret" to perform a raycast. If it hits something, the hit position's GPS coordinates are formatted and stored in the *JetOS* Programmable Block's Custom Data under the `Cached:GPS:Target2:...` key.
    * **Air-to-Air Update:** The `AirtoAir` module, when active, uses the "JetNoseRad" turret. If this turret locks onto a target (`GetTargetedEntity`), it continuously updates the `Cached:GPS:Target2:...` and `CachedSpeed:...` entries in the JetOS Custom Data with the locked target's current position and velocity.
    * **Missile Prep (ATG/ATA):** When preparing to fire, both `AirToGround` and `AirtoAir` modules read the *latest* coordinates from `Cached:GPS:Target2:...` (or `Cached:GPS:...` if `Target2` isn't the intended source for ATG).
    * **Bay Caching:** Before firing a specific bay (e.g., Bay 1), the script copies the retrieved target GPS string from `Cached:GPS:...` into a bay-specific cache line, like `Cache1:GPS:Target:...`. This is done for *each selected bay* before the firing command.
    * **Launch & Transfer:** The `FireSelectedBays` (or similar) function triggers the merge block disconnect for the selected bays (`ApplyAction("Fire")` seems to be used, likely triggering a timer or sensor on the missile itself to disconnect). Immediately after initiating the launch sequence for all selected bays, the `TransferCacheToSlots` function is called. This function reads the data from each `CacheX:GPS:...` line and writes it to the *final* destination line that the missile script will read, formatted as `<BayNumber>:GPS:Target:...` (e.g., `1:GPS:Target:...`). It then clears the `CacheX:` line. This ensures each missile gets its intended target data written just before it's expected to read it upon launch.
