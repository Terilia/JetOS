// SOUND DIAGNOSTIC - Paste this into a SEPARATE programmable block in-game.
//
// Tests 4 different methods of playing/stopping sounds to find which gives:
//   - Immediate play (no delay after calling play)
//   - Immediate stop (no lingering audio)
//   - Simultaneous playback on two channels
//   - Simultaneous stop on two channels
//
// COMMANDS (set as toolbar arguments on PB):
//   "test1" — Method 1: Single-tick (Stop+Select+Play in one tick)
//   "test2" — Method 2: Two-tick (Stop+Select, then Play next tick)
//   "test3" — Method 3: Three-tick (Stop, Select, Play over 3 ticks)
//   "test4" — Method 4: Action-based (ApplyAction instead of .Play()/.Stop())
//   "stop"  — Emergency stop all sound blocks
//   "list"  — List all sound blocks and their available sounds
//   "reset" — Re-scan blocks and reset state
//
// Each test runs an automatic sequence:
//   Step 0: STOP all sounds (clean slate)
//   Step 1: Play sound 1 on Channel A only
//   Step 2: STOP all (pause)
//   Step 3: Play sound 1 on CH-A AND sound 2 on CH-B simultaneously
//   Step 4: STOP all simultaneously
//   Step 5: Done — report timing
//
// Watch the PB detail panel for live status.
// Listen carefully to judge if play/stop is truly instant.
//
// This file is excluded from the MDK build (see csproj).

// ================================================================
// CONFIGURATION — match your block names and sound file names
// ================================================================
const string CHANNEL_A_FILTER = "Sound Block Warning";
const string CHANNEL_B_FILTER = "Canopy Side Plate Sound Block";

const string SOUND_1 = "F-18PullUp";
const string SOUND_2 = "CAP_F-16_RWR_Lock_Short";

const float VOLUME_A = 1.0f;
const float VOLUME_B = 0.5f;

// How many ticks between sequence steps (60 ticks = 1 second)
const int STEP_GAP = 120; // 2 seconds between steps so you can hear each clearly

// ================================================================
// STATE
// ================================================================
List<IMySoundBlock> channelA = new List<IMySoundBlock>();
List<IMySoundBlock> channelB = new List<IMySoundBlock>();
List<IMySoundBlock> allBlocks = new List<IMySoundBlock>();

int activeMethod = -1;  // -1 = idle, 0-3 = running method test
int step = -1;          // current sequence step
int tickInStep = 0;     // ticks since step started
int globalTick = 0;

// Sub-tick state for multi-tick methods (methods 2, 3)
int multiTickPhase = 0;

// Timing log
List<string> log = new List<string>();

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.None;
    ScanBlocks();
}

public void Main(string argument, UpdateType updateSource)
{
    string arg = (argument ?? "").ToLower().Trim();

    switch (arg)
    {
        case "test1":
            StartTest(0);
            return;
        case "test2":
            StartTest(1);
            return;
        case "test3":
            StartTest(2);
            return;
        case "test4":
            StartTest(3);
            return;
        case "stop":
            EmergencyStop();
            return;
        case "list":
            ListSounds();
            return;
        case "reset":
            EmergencyStop();
            ScanBlocks();
            Echo(">>> RESET — " + allBlocks.Count + " blocks found");
            PrintBlockSummary();
            return;
    }

    // Tick-driven sequence
    if (activeMethod < 0) return;

    globalTick++;
    tickInStep++;

    RunSequence();

    // Status display every 10 ticks
    if (globalTick % 10 == 0)
        PrintStatus();
}

// ================================================================
// BLOCK SCANNING
// ================================================================
void ScanBlocks()
{
    channelA.Clear();
    channelB.Clear();
    allBlocks.Clear();

    var temp = new List<IMySoundBlock>();
    GridTerminalSystem.GetBlocksOfType(temp);

    foreach (var b in temp)
    {
        allBlocks.Add(b);
        if (b.CustomName.Contains(CHANNEL_A_FILTER))
            channelA.Add(b);
        else if (b.CustomName.Contains(CHANNEL_B_FILTER))
            channelB.Add(b);
    }
}

void ListSounds()
{
    ScanBlocks();
    Echo("=== SOUND BLOCKS ===\n");
    Echo("Total: " + allBlocks.Count);
    Echo("CH-A ('" + CHANNEL_A_FILTER + "'): " + channelA.Count);
    Echo("CH-B ('" + CHANNEL_B_FILTER + "'): " + channelB.Count);

    foreach (var b in allBlocks)
    {
        string ch = channelA.Contains(b) ? "A" : channelB.Contains(b) ? "B" : "-";
        Echo("\n[" + ch + "] " + b.CustomName);
        Echo("  Enabled=" + b.Enabled + " Selected='" + b.SelectedSound + "'");

        var sounds = new List<string>();
        b.GetSounds(sounds);
        if (sounds.Count > 0)
        {
            Echo("  Sounds (" + sounds.Count + "):");
            foreach (var s in sounds)
                Echo("    '" + s + "'");
        }
        else
        {
            Echo("  Sounds: (API returned none)");
        }
    }
}

// ================================================================
// TEST CONTROL
// ================================================================
void StartTest(int method)
{
    EmergencyStop();
    activeMethod = method;
    step = 0;
    tickInStep = 0;
    multiTickPhase = 0;
    globalTick = 0;
    log.Clear();

    string[] names = { "Single-Tick", "Two-Tick", "Three-Tick", "Action-Based" };
    log.Add("=== Method " + (method + 1) + ": " + names[method] + " ===");

    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    Echo(">>> Starting Method " + (method + 1) + ": " + names[method]);
}

void EmergencyStop()
{
    activeMethod = -1;
    step = -1;
    Runtime.UpdateFrequency = UpdateFrequency.None;
    foreach (var b in allBlocks)
    {
        if (b != null && b.IsFunctional)
        {
            b.Stop();
        }
    }
}

// ================================================================
// SEQUENCE RUNNER
// ================================================================
// Steps:
//   0: Stop all (clean slate, wait STEP_GAP)
//   1: Play SOUND_1 on CH-A only (wait STEP_GAP)
//   2: Stop all (pause, wait STEP_GAP)
//   3: Play SOUND_1 on CH-A + SOUND_2 on CH-B simultaneously (wait STEP_GAP)
//   4: Stop all simultaneously (wait STEP_GAP)
//   5: Done
void RunSequence()
{
    switch (step)
    {
        case 0: // Clean slate — stop everything
            if (tickInStep == 1)
            {
                log.Add("Step 0: STOP ALL (clean slate) @ tick " + globalTick);
                StopAll_Method(activeMethod);
            }
            if (tickInStep >= STEP_GAP)
                AdvanceStep();
            break;

        case 1: // Play sound 1 on channel A only
            PlaySingle_Method(activeMethod, tickInStep);
            if (tickInStep >= STEP_GAP)
                AdvanceStep();
            break;

        case 2: // Stop all (pause between tests)
            if (tickInStep == 1)
            {
                log.Add("Step 2: STOP ALL (pause) @ tick " + globalTick);
                StopAll_Method(activeMethod);
            }
            if (tickInStep >= STEP_GAP)
                AdvanceStep();
            break;

        case 3: // Play both channels simultaneously
            PlayDual_Method(activeMethod, tickInStep);
            if (tickInStep >= STEP_GAP)
                AdvanceStep();
            break;

        case 4: // Stop all simultaneously
            if (tickInStep == 1)
            {
                log.Add("Step 4: STOP BOTH CHANNELS @ tick " + globalTick);
                StopAll_Method(activeMethod);
            }
            if (tickInStep >= STEP_GAP)
                AdvanceStep();
            break;

        case 5: // Done
            log.Add("COMPLETE @ tick " + globalTick);
            activeMethod = -1;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            PrintResults();
            break;
    }
}

void AdvanceStep()
{
    step++;
    tickInStep = 0;
    multiTickPhase = 0;
}

// ================================================================
// METHOD IMPLEMENTATIONS — PLAY SINGLE (CH-A only)
// ================================================================
void PlaySingle_Method(int method, int t)
{
    switch (method)
    {
        case 0: // Single-tick: Stop+Select+Play all at once
            if (t == 1)
            {
                log.Add("Step 1: PLAY '" + SOUND_1 + "' on CH-A [single-tick] @ tick " + globalTick);
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                    b.SelectedSound = SOUND_1;
                    b.Volume = VOLUME_A;
                    b.Play();
                }
            }
            break;

        case 1: // Two-tick: Stop+Select, then Play
            if (t == 1)
            {
                log.Add("Step 1: PLAY '" + SOUND_1 + "' on CH-A [two-tick] @ tick " + globalTick);
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                    b.SelectedSound = SOUND_1;
                    b.Volume = VOLUME_A;
                }
            }
            else if (t == 2)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Play();
                }
            }
            break;

        case 2: // Three-tick: Stop, Select, Play
            if (t == 1)
            {
                log.Add("Step 1: PLAY '" + SOUND_1 + "' on CH-A [three-tick] @ tick " + globalTick);
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                }
            }
            else if (t == 2)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Enabled = true;
                    b.SelectedSound = SOUND_1;
                    b.Volume = VOLUME_A;
                }
            }
            else if (t == 3)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Play();
                }
            }
            break;

        case 3: // Action-based: ApplyAction
            if (t == 1)
            {
                log.Add("Step 1: PLAY '" + SOUND_1 + "' on CH-A [action-based] @ tick " + globalTick);
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.ApplyAction("StopSound");
                }
            }
            else if (t == 2)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.SelectedSound = SOUND_1;
                    b.Volume = VOLUME_A;
                }
            }
            else if (t == 3)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.ApplyAction("PlaySound");
                }
            }
            break;
    }
}

// ================================================================
// METHOD IMPLEMENTATIONS — PLAY DUAL (CH-A + CH-B simultaneously)
// ================================================================
void PlayDual_Method(int method, int t)
{
    switch (method)
    {
        case 0: // Single-tick
            if (t == 1)
            {
                log.Add("Step 3: PLAY BOTH [single-tick] @ tick " + globalTick);
                log.Add("  CH-A: '" + SOUND_1 + "', CH-B: '" + SOUND_2 + "'");
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                    b.SelectedSound = SOUND_1;
                    b.Volume = VOLUME_A;
                    b.Play();
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                    b.SelectedSound = SOUND_2;
                    b.Volume = VOLUME_B;
                    b.Play();
                }
            }
            break;

        case 1: // Two-tick
            if (t == 1)
            {
                log.Add("Step 3: PLAY BOTH [two-tick] @ tick " + globalTick);
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                    b.SelectedSound = SOUND_1;
                    b.Volume = VOLUME_A;
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                    b.SelectedSound = SOUND_2;
                    b.Volume = VOLUME_B;
                }
            }
            else if (t == 2)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Play();
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Play();
                }
            }
            break;

        case 2: // Three-tick
            if (t == 1)
            {
                log.Add("Step 3: PLAY BOTH [three-tick] @ tick " + globalTick);
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                }
            }
            else if (t == 2)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Enabled = true;
                    b.SelectedSound = SOUND_1;
                    b.Volume = VOLUME_A;
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Enabled = true;
                    b.SelectedSound = SOUND_2;
                    b.Volume = VOLUME_B;
                }
            }
            else if (t == 3)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Play();
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Play();
                }
            }
            break;

        case 3: // Action-based
            if (t == 1)
            {
                log.Add("Step 3: PLAY BOTH [action-based] @ tick " + globalTick);
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.ApplyAction("StopSound");
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.ApplyAction("StopSound");
                }
            }
            else if (t == 2)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.SelectedSound = SOUND_1;
                    b.Volume = VOLUME_A;
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.SelectedSound = SOUND_2;
                    b.Volume = VOLUME_B;
                }
            }
            else if (t == 3)
            {
                foreach (var b in channelA)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.ApplyAction("PlaySound");
                }
                foreach (var b in channelB)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.ApplyAction("PlaySound");
                }
            }
            break;
    }
}

// ================================================================
// METHOD IMPLEMENTATIONS — STOP ALL
// ================================================================
void StopAll_Method(int method)
{
    switch (method)
    {
        case 0: // Single-tick: just Stop()
        case 1: // Two-tick: same stop
        case 2: // Three-tick: same stop
            foreach (var b in allBlocks)
            {
                if (b != null && b.IsFunctional)
                    b.Stop();
            }
            break;

        case 3: // Action-based
            foreach (var b in allBlocks)
            {
                if (b != null && b.IsFunctional)
                    b.ApplyAction("StopSound");
            }
            break;
    }
}

// ================================================================
// DISPLAY
// ================================================================
void PrintStatus()
{
    string[] methodNames = { "1:Single-Tick", "2:Two-Tick", "3:Three-Tick", "4:Action-Based" };
    string[] stepNames = { "STOP ALL", "PLAY CH-A", "STOP ALL", "PLAY CH-A+CH-B", "STOP BOTH", "DONE" };

    Echo("=== SOUND DIAGNOSTIC ===");
    Echo("Tick: " + globalTick + " | Instr: " + Runtime.CurrentInstructionCount);
    Echo("CH-A: " + channelA.Count + " blocks | CH-B: " + channelB.Count + " blocks");
    Echo("");

    if (activeMethod >= 0 && activeMethod < methodNames.Length)
    {
        Echo("METHOD: " + methodNames[activeMethod]);
        if (step >= 0 && step < stepNames.Length)
            Echo("STEP " + step + ": " + stepNames[step]);
        Echo("Step tick: " + tickInStep + " / " + STEP_GAP);
        Echo("");
    }
    else
    {
        Echo("IDLE — use test1/test2/test3/test4");
        Echo("");
    }

    // Live block state
    Echo("--- Block State ---");
    foreach (var b in channelA)
    {
        if (b == null) continue;
        Echo("A: '" + b.SelectedSound + "' vol=" + b.Volume.ToString("F1") + " en=" + b.Enabled);
    }
    foreach (var b in channelB)
    {
        if (b == null) continue;
        Echo("B: '" + b.SelectedSound + "' vol=" + b.Volume.ToString("F1") + " en=" + b.Enabled);
    }

    // Recent log
    Echo("");
    Echo("--- Log ---");
    int start = log.Count > 8 ? log.Count - 8 : 0;
    for (int i = start; i < log.Count; i++)
        Echo(log[i]);
}

void PrintResults()
{
    string[] methodNames = { "Single-Tick", "Two-Tick", "Three-Tick", "Action-Based" };

    Echo("=== TEST COMPLETE ===");
    Echo("Method: " + methodNames[activeMethod >= 0 ? activeMethod : 0]);
    Echo("Total ticks: " + globalTick);
    Echo("");
    Echo("--- Full Log ---");
    foreach (var entry in log)
        Echo(entry);
    Echo("");
    Echo("LISTEN AND JUDGE:");
    Echo("  1. Did CH-A sound play immediately?");
    Echo("  2. Did it stop cleanly (no tail)?");
    Echo("  3. Did both channels start at the same time?");
    Echo("  4. Did both channels stop at the same time?");
    Echo("");
    Echo("Run test1-test4 to compare methods.");
}

void PrintBlockSummary()
{
    foreach (var b in allBlocks)
    {
        string ch = channelA.Contains(b) ? "A" : channelB.Contains(b) ? "B" : "-";
        Echo("  [" + ch + "] " + b.CustomName);
    }
}
