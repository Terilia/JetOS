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
            public const int PRIORITY_RWR = 3;         // RWR Alert
            public const int PRIORITY_ALTITUDE = 4;    // Altitude warning (pilot safety)
            public const int NEW_TARGET = 1, RWR_LOCK = 2, RWR_LAUNCH = 3, PULL_UP = 4, BINGO = 5, ENGINE_LEFT = 6, ENGINE_RIGHT = 7;

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
                // Latched alongside pendingSound at decision time, consumed at play time —
                // requestedLoopSeconds can be overwritten mid-transition by a newer request,
                // and event cooldowns must only arm when the sound actually plays.
                internal double pendingLoopSeconds = 5.0;
                internal int pendingEventId = 0;

                // Per-tick request (reset each tick)
                internal string requestedSound = "";
                internal int requestedPriority = PRIORITY_NONE;
                internal double requestedLoopSeconds = 5.0;
                internal int requestedEventId = 0;
            }

            private static SoundChannel warningChannel, rwrChannel, eventChannel;
            static readonly double[] lastEvent = new double[9];

            public static void Initialize(IMyGridTerminalSystem grid)
            {
                warningChannel = MakeChannel(grid, "Sound Block Warning", 1.0f);
                rwrChannel = MakeChannel(grid, "Sound Block RWR", 0.8f);
                eventChannel = MakeChannel(grid, "Canopy Side Plate Sound Block", 0.55f);
                if (eventChannel.blocks.Count == 0)
                    eventChannel = MakeChannel(grid, "Sound Block Event", 0.55f);
                if (rwrChannel.blocks.Count == 0)
                    rwrChannel = eventChannel;
            }

            static SoundChannel MakeChannel(IMyGridTerminalSystem grid, string filter, float volume)
            {
                var ch = new SoundChannel { volume = volume };
                grid.GetBlocksOfType(ch.blocks, b => b.CustomName.Contains(filter));
                PrepChannel(ch);
                return ch;
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

            public static void Event(int id)
            {
                SoundChannel ch;
                string snd;
                int pri;
                double cool;
                Map(id, out ch, out snd, out pri, out cool);
                if (ch == null || SE(snd)) return;
                double now = SystemManager.ElapsedSeconds;
                if (id > 0 && id < lastEvent.Length && lastEvent[id] > 0.0 && now - lastEvent[id] < cool) return;
                // Cooldown is latched at play time (TickChannel case 3), not here — a request
                // that wins the race but gets superseded or lands mid-transition never plays,
                // and must not arm its cooldown.
                Request(ch, snd, pri, cool, id);
            }

            static void Map(int id, out SoundChannel ch, out string snd, out int pri, out double cool)
            {
                ch = eventChannel; snd = ""; pri = 1; cool = 3.0;
                switch (id)
                {
                    case NEW_TARGET: ch = eventChannel; snd = "CAP_F-16_NewContact_Air"; pri = 2; cool = 5.0; break;
                    case RWR_LOCK: ch = rwrChannel; snd = "CAP_F-16_RWR_Lock_Short"; pri = 3; cool = 4.0; break;
                    case RWR_LAUNCH: ch = rwrChannel; snd = "CAP_F-16_RWR_Launch_Short"; pri = 4; cool = 2.0; break;
                    case PULL_UP: ch = warningChannel; snd = "F-18PullUp"; pri = 6; cool = 4.0; break;
                    case BINGO: ch = warningChannel; snd = "F-18Bingo"; pri = 2; cool = 30.0; break;
                    case ENGINE_LEFT: ch = warningChannel; snd = "F-18EngineFireLeft"; pri = 5; cool = 12.0; break;
                    case ENGINE_RIGHT: ch = warningChannel; snd = "F-18EngineFireRight"; pri = 5; cool = 12.0; break;
                }
            }

            /// <summary>
            /// Request a sound on the warning channel (altitude, RWR).
            /// Highest priority request each tick wins.
            /// </summary>
            public static void RequestWarning(string sound, int priority, double loopSeconds = 5.0)
            {
                Request(warningChannel, sound, priority, loopSeconds);
            }

            static bool Request(SoundChannel ch, string sound, int priority, double loopSeconds, int eventId = 0)
            {
                if (ch == null) return false;
                if (priority >= ch.requestedPriority)
                {
                    ch.requestedSound = sound;
                    ch.requestedPriority = priority;
                    ch.requestedLoopSeconds = loopSeconds;
                    ch.requestedEventId = eventId;
                    return true;
                }
                return false;
            }

            public static void Tick(double currentSeconds)
            {
                TickAndReset(warningChannel, currentSeconds);
                TickAndReset(rwrChannel, currentSeconds);
                if (eventChannel != rwrChannel)
                    TickAndReset(eventChannel, currentSeconds);
            }

            static void TickAndReset(SoundChannel ch, double currentSeconds)
            {
                if (ch == null) return;
                TickChannel(ch, currentSeconds);
                ch.requestedSound = "";
                ch.requestedPriority = PRIORITY_NONE;
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
                        if (!SE(ch.pendingSound))
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
                        ch.activeLoopSeconds = ch.pendingLoopSeconds;
                        ch.playStartSeconds = currentSeconds;
                        if (ch.pendingEventId > 0) lastEvent[ch.pendingEventId] = currentSeconds;
                        ch.pendingEventId = 0;
                        ch.state = 0;
                        break;
                }

                // Check if sound should change (only when idle)
                if (ch.state == 0)
                {
                    string desired = ch.requestedSound ?? "";
                    if (SE(desired) && !SE(ch.activeSound) && currentSeconds - ch.playStartSeconds < ch.activeLoopSeconds)
                        desired = ch.activeSound;
                    bool needsChange = false;

                    // desired came from this tick's request unless it's the carried-over active sound
                    bool fromRequest = !SE(ch.requestedSound);

                    if (desired != ch.activeSound)
                    {
                        if (!SE(desired))
                        {
                            ch.pendingSound = desired;
                            ch.pendingLoopSeconds = ch.requestedLoopSeconds;
                            ch.pendingEventId = ch.requestedEventId;
                            needsChange = true;
                        }
                        else if (!SE(ch.activeSound))
                        {
                            ch.pendingSound = "";
                            ch.pendingEventId = 0;
                            needsChange = true;
                        }
                    }
                    else if (!SE(desired) && !SE(ch.activeSound))
                    {
                        if (currentSeconds - ch.playStartSeconds >= ch.activeLoopSeconds)
                        {
                            ch.pendingSound = desired;
                            ch.pendingLoopSeconds = fromRequest ? ch.requestedLoopSeconds : ch.activeLoopSeconds;
                            ch.pendingEventId = fromRequest ? ch.requestedEventId : 0;
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
