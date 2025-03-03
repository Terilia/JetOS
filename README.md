# JetOS - A Modular Space Engineers Flight & Combat System

This script (commonly referred to as **JetOS**) is an **Ingame Script** written for **Space Engineers**. It integrates a variety of features for piloting, combat, and gameplay enhancements—ranging from custom HUDs and thruster overrides to guided missile systems, radar toggling, and even a mini “Frogger” game. Below is an overview of how it is structured and what each major part of the code does.

---

## Table of Contents

- [General Overview](#general-overview)
- [Key Components and Modules](#key-components-and-modules)
  - [Jet Class](#jet-class)
  - [SystemManager](#systemmanager)
  - [Modules Overview](#modules-overview)
    - [Air To Ground (ATG)](#air-to-ground-atg)
    - [Air To Air (ATA)](#air-to-air-ata)
    - [RaycastCameraControl](#raycastcameracontrol)
    - [HUDModule](#hudmodule)
    - [LogoDisplay (Screen Saver)](#logodisplay-screen-saver)
    - [FroggerGameControl](#froggergamecontrol)
- [Setting Up Blocks and Naming Conventions](#setting-up-blocks-and-naming-conventions)
- [Hotkeys / Input Usage](#hotkeys--input-usage)
- [Custom Data Configuration](#custom-data-configuration)
- [Installation Instructions](#installation-instructions)
- [Troubleshooting Tips](#troubleshooting-tips)
- [License](#license)

---

## General Overview

**JetOS** is designed to be placed in a programmable block (`PB`) in Space Engineers. Once compiled and run, it initializes multiple sub-systems (“modules”) responsible for different functionalities such as:

- Managing thrusters and controlling flight characteristics
- Handling radar blocks and toggling them at certain intervals
- Providing a custom cockpit HUD (speed, altitude, AoA, G-forces, etc.)
- Launching air-to-ground or air-to-air guided munitions
- Raycasting for target detection
- A mini “Frogger” game rendered on an LCD panel (just for fun!)
- Displaying a custom screensaver or “logo display”

The script is quite extensive and relies on specific block names to function properly.

---

## Key Components and Modules

### Jet Class

The **Jet** class is responsible for collecting references to important blocks in your grid (e.g., the cockpit, thrusters, sound blocks, radar turrets, merge blocks for missile bays, and hydrogen tanks). 

- It identifies your **“Jet Pilot Seat”** as the main cockpit and fetches:
  - **All thrusters** on the same grid that _aren’t_ named “Sci-Fi”
  - **Sound blocks** that contain “Sound Block Warning” in the name
  - **Radar turrets** (any Gatling turret named with “Radar” or specifically `"JetNoseRad"`)
  - **Ship merge blocks** named with “Bay” (used for missiles)
  - **Stabilizer blocks** named `normalstab` or `invertedstab` (left and right stabs)
  - An LCD or text surface named **“Fighter HUD”** for your custom heads-up display
  - **Gas tanks** that contain `"Jet"` in their name

These references are used throughout the script to handle all flight and combat operations.

### SystemManager

**SystemManager** is a static class that orchestrates which “module” is currently active and handles user input (like pressing keys `1`-`9` on the cockpit’s Custom Data or via the game’s terminal controls). It also:

- Updates each module every tick (via `Tick()` methods)
- Displays a main menu on an LCD (usually titled “JetOS” or “Jet Pilot Seat” sub-surface)
- Renders additional screens (like extra status or a custom UI) on a secondary LCD surface if available
- Manages global states, such as toggling radars or playing warning sounds

### Modules Overview

A **module** is a distinct feature set that you can switch between at runtime:

1. **Air To Ground (ATG)**
   - Manages missile launch for ground targets
   - “Bombardment” functionality for placing multiple GPS offsets around a central target
   - Supports toggling “Topdown” bombing logic via Custom Data

2. **Air To Air (ATA)**
   - Similar to ATG, but geared towards aerial targets
   - Hooks into a turret for target data (search and track)
   - Plays “seeker” or “lock” sounds via `IMySoundBlock`s (like an AIM-9 missile lock tone)
   - Allows firing selected or “next available bay” for missiles

3. **RaycastCameraControl**
   - Performs a raycast from a specified camera block (“Camera Targeting Turret”)
   - If a target is detected, it prints or caches GPS data for that target
   - Animates a “crosshair” on an LCD to indicate lock-on / scanning

4. **HUDModule**
   - Draws an extensive pilot HUD on the **“Fighter HUD”** panel
   - Displays altitude, airspeed (knots, KPH), AoA (angle of attack), G-forces, trim, heading tape, compass, etc.
   - Manages thruster override percentages (like afterburner logic) and automatically toggles hydrogen tanks if you push beyond 80% throttle

5. **LogoDisplay (Screen Saver)**
   - Renders a colorful, animated “screensaver” on the main or extra LCD
   - Cycles between motivational texts and “evil corporate” texts
   - Creates dancing particle effects, star-like or dust-like visuals

6. **FroggerGameControl**
   - A fully rendered mini “Frogger” game on the LCD
   - Move up, down, left, right to avoid obstacles
   - Score points each time you cross lanes
   - Lose lives if colliding with an obstacle

---

## Setting Up Blocks and Naming Conventions

To get the most out of this script, set up your blocks with the following **exact** or **partial** names:

1. **Cockpit**  
   - Name: **“Jet Pilot Seat”**  
     - Or, have a cockpit that contains “Jet Pilot Seat” in its name.

2. **HUD Text Surface**  
   - Name: **“Fighter HUD”**  
   - Must be on the same grid as the cockpit or accessible from your programmable block’s grid.

3. **Camera** (optional)  
   - Name: **“Camera Targeting Turret”**  
   - If you plan on using the Raycast features.

4. **Sound Blocks**  
   - For warnings: name contains **“Sound Block Warning”**  
   - For A2A seeker tones: name contains **“Canopy Side Plate Sound Block”** (or adapt the relevant filter in the code if needed).

5. **Missile Bays**  
   - Merge blocks whose names contain **“Bay”**.  
   - These are used by the AirToGround or AirToAir modules for firing.

6. **Radars**  
   - Gatling turret blocks with **“Radar”** in the name or specifically “JetNoseRad”.

7. **Stabilizer Hinges / Rotors**  
   - Named with “normalstab” or “invertedstab” for correct handling of trim logic.

8. **Gas Tanks**  
   - Must contain **“Jet”** in their custom name (e.g., “Jet Hydrogen Tank”).

If any of these names are missing, the associated feature might fail or be skipped.

---

## Hotkeys / Input Usage

While the script runs, you can pass **arguments** (e.g., `1`, `2`, `3`, etc.) to the programmable block to navigate or trigger certain actions:

- **`1`** – Navigate **Up** in the menu  
- **`2`** – Navigate **Down**  
- **`3`** – Select current menu option  
- **`4`** – Go back to the previous menu (or deselect current module)  
- **`5` / `6` / `7` / `8`** – Often mapped to special module functions (like launching missiles, toggling something, etc.)  
- **`9`** – Return to the main menu  

These are read by `HandleInput(string argument)` in **SystemManager**.


---

## License

This script is shared **as-is** within the Space Engineers community. Feel free to modify or adapt it for your personal or server use. Much love to Whiplash141

Enjoy your enhanced flight control and combat systems with **JetOS**!
