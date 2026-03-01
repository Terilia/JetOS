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
                internal int state = 0;
                internal string pendingSound = "";
                internal string activeSound = "";
                internal int playStartTick = 0;
                internal int activeLoopInterval = 300;

                // Per-tick request (reset each tick)
                internal string requestedSound = "";
                internal int requestedPriority = PRIORITY_NONE;
                internal int requestedLoopInterval = 300;
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
            }

            /// <summary>
            /// Request a sound on the warning channel (altitude, RWR).
            /// Highest priority request each tick wins.
            /// </summary>
            public static void RequestWarning(string sound, int priority, int loopInterval = 300)
            {
                if (warningChannel == null) return;
                if (priority >= warningChannel.requestedPriority)
                {
                    warningChannel.requestedSound = sound;
                    warningChannel.requestedPriority = priority;
                    warningChannel.requestedLoopInterval = loopInterval;
                }
            }

            /// <summary>
            /// Request a sound on the weapon channel (AIM9 lock/search tones).
            /// Highest priority request each tick wins.
            /// </summary>
            public static void RequestWeapon(string sound, int priority, int loopInterval = 300)
            {
                if (weaponChannel == null) return;
                if (priority >= weaponChannel.requestedPriority)
                {
                    weaponChannel.requestedSound = sound;
                    weaponChannel.requestedPriority = priority;
                    weaponChannel.requestedLoopInterval = loopInterval;
                }
            }

            public static void Tick(int currentTick)
            {
                if (warningChannel != null)
                {
                    TickChannel(warningChannel, currentTick);
                    warningChannel.requestedSound = "";
                    warningChannel.requestedPriority = PRIORITY_NONE;
                }
                if (weaponChannel != null)
                {
                    TickChannel(weaponChannel, currentTick);
                    weaponChannel.requestedSound = "";
                    weaponChannel.requestedPriority = PRIORITY_NONE;
                }
            }

            private static void TickChannel(SoundChannel ch, int currentTick)
            {
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
                        ch.activeLoopInterval = ch.requestedLoopInterval;
                        ch.playStartTick = currentTick;
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
                        if (currentTick - ch.playStartTick >= ch.activeLoopInterval)
                        {
                            ch.pendingSound = desired;
                            needsChange = true;
                        }
                    }

                    // Execute Stop() immediately on the same tick to avoid 1-tick delay
                    if (needsChange)
                    {
                        foreach (var b in ch.blocks)
                        {
                            if (b != null && b.IsFunctional)
                                b.Stop();
                        }
                        ch.state = 2; // Next tick goes straight to Select
                    }
                }
            }
        }
    }
}
