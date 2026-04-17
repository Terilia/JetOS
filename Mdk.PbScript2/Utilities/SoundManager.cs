using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        static class SoundManager
        {
            // Priority constants - higher value = higher priority
            public const int PRIORITY_NONE = 0;
            public const int PRIORITY_SEARCH = 1;     // AIM9Search
            public const int PRIORITY_LOCK = 2;        // AIM9Lock
            public const int PRIORITY_RWR = 3;         // RWR Alert
            public const int PRIORITY_ALTITUDE = 4;    // Altitude warning (pilot safety)

            class SoundChannel
            {
                internal List<IMySoundBlock> blocks = new List<IMySoundBlock>();
                internal float volume = 1.0f;

                // State machine: 0=idle, 1=stopping, 2=selecting, 3=playing
                // Each state waits FRAME_DELAY calls before advancing to survive double Main() calls.
                // (FRAME_DELAY stays as a PB-call count — it's about ordering SE block-API
                // operations across sim ticks, not wall-clock time.)
                internal int state = 0;
                internal int delay = 0;
                internal string pendingSound = "";
                internal string activeSound = "";
                internal double playStartSeconds = 0.0;
                internal double activeLoopSeconds = 5.0;

                // Per-tick request (reset each tick)
                internal string requestedSound = "";
                internal int requestedPriority = PRIORITY_NONE;
                internal double requestedLoopSeconds = 5.0;
            }

            private static SoundChannel warningChannel;
            private static SoundChannel weaponChannel;

            public static void Initialize(IMyGridTerminalSystem grid)
            {
                warningChannel = new SoundChannel();
                weaponChannel = new SoundChannel();

                grid.GetBlocksOfType(
                    warningChannel.blocks,
                    b => b.CustomName.Contains("Sound Block Warning")
                );

                grid.GetBlocksOfType(
                    weaponChannel.blocks,
                    b => b.CustomName.Contains("Canopy Side Plate Sound Block")
                );
                weaponChannel.volume = 0.3f;

                // Prep all blocks to a known clean state so the first
                // Play() call works reliably. Without this, blocks may
                // be disabled or mid-play from a previous script run.
                PrepChannel(warningChannel);
                PrepChannel(weaponChannel);
            }

            private static void PrepChannel(SoundChannel ch)
            {
                foreach (var b in ch.blocks)
                {
                    if (b == null || !b.IsFunctional) continue;
                    b.Stop();
                    b.Enabled = true;
                    b.SelectedSound = "";
                    b.Volume = ch.volume;
                }
            }

            /// <summary>
            /// Request a sound on the warning channel (altitude, RWR).
            /// Highest priority request each tick wins.
            /// </summary>
            public static void RequestWarning(string sound, int priority, double loopSeconds = 5.0)
            {
                if (warningChannel == null) return;
                if (priority >= warningChannel.requestedPriority)
                {
                    warningChannel.requestedSound = sound;
                    warningChannel.requestedPriority = priority;
                    warningChannel.requestedLoopSeconds = loopSeconds;
                }
            }

            /// <summary>
            /// Request a sound on the weapon channel (AIM9 lock/search tones).
            /// Highest priority request each tick wins.
            /// </summary>
            public static void RequestWeapon(string sound, int priority, double loopSeconds = 5.0)
            {
                if (weaponChannel == null) return;
                if (priority >= weaponChannel.requestedPriority)
                {
                    weaponChannel.requestedSound = sound;
                    weaponChannel.requestedPriority = priority;
                    weaponChannel.requestedLoopSeconds = loopSeconds;
                }
            }

            public static void Tick(double currentSeconds)
            {
                if (warningChannel != null)
                {
                    TickChannel(warningChannel, currentSeconds);
                    warningChannel.requestedSound = "";
                    warningChannel.requestedPriority = PRIORITY_NONE;
                }
                if (weaponChannel != null)
                {
                    TickChannel(weaponChannel, currentSeconds);
                    weaponChannel.requestedSound = "";
                    weaponChannel.requestedPriority = PRIORITY_NONE;
                }
            }

            const int FRAME_DELAY = 3;

            private static void TickChannel(SoundChannel ch, double currentSeconds)
            {
                // 3-frame delay between each state ensures double Main() calls
                // (Trigger + Update1 on same sim tick) never put two block
                // operations in the same sim tick.
                if (ch.delay > 0)
                {
                    ch.delay--;
                    return;
                }

                // Execute current state machine step
                switch (ch.state)
                {
                    case 1: // Stopping
                        foreach (var b in ch.blocks)
                        {
                            if (b != null && b.IsFunctional)
                                b.Stop();
                        }
                        ch.state = 2;
                        ch.delay = FRAME_DELAY;
                        break;

                    case 2: // Selecting
                        foreach (var b in ch.blocks)
                        {
                            if (b == null || !b.IsFunctional)
                                continue;
                            if (!b.Enabled)
                                b.Enabled = true;
                            b.SelectedSound = ch.pendingSound;
                            b.Volume = ch.volume;
                        }
                        if (!string.IsNullOrEmpty(ch.pendingSound))
                        {
                            ch.state = 3;
                            ch.delay = FRAME_DELAY;
                        }
                        else
                        {
                            ch.state = 0;
                            ch.activeSound = "";
                        }
                        break;

                    case 3: // Playing
                        foreach (var b in ch.blocks)
                        {
                            if (b != null && b.IsFunctional)
                                b.Play();
                        }
                        ch.activeSound = ch.pendingSound;
                        ch.activeLoopSeconds = ch.requestedLoopSeconds;
                        ch.playStartSeconds = currentSeconds;
                        ch.state = 0;
                        break;
                }

                // Check if sound should change (only when idle)
                if (ch.state == 0)
                {
                    string desired = ch.requestedSound ?? "";
                    bool needsChange = false;

                    if (desired != ch.activeSound)
                    {
                        if (!string.IsNullOrEmpty(desired))
                        {
                            ch.pendingSound = desired;
                            needsChange = true;
                        }
                        else if (!string.IsNullOrEmpty(ch.activeSound))
                        {
                            ch.pendingSound = "";
                            needsChange = true;
                        }
                    }
                    else if (!string.IsNullOrEmpty(desired) && !string.IsNullOrEmpty(ch.activeSound))
                    {
                        if (currentSeconds - ch.playStartSeconds >= ch.activeLoopSeconds)
                        {
                            ch.pendingSound = desired;
                            needsChange = true;
                        }
                    }

                    if (needsChange)
                    {
                        ch.state = 1;
                        ch.delay = FRAME_DELAY;
                    }
                }
            }
        }
    }
}
