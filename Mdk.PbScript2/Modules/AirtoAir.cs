using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class AirtoAir : ProgramModule
        {
            private List<IMyShipMergeBlock> missileBays = new List<IMyShipMergeBlock>();
            private bool[] baySelected;
            private List<IMySoundBlock> soundblocks = new List<IMySoundBlock>();
            private bool isAirtoAirenabled = false;
            private List<string> lastPlayedSounds = new List<string>();

            private List<int> lastSoundTickCounters = new List<int>();
            private int tickCounter = 0;
            // Sound state machine (Space Engineers requires 1 action per tick)
            private List<int> soundStates = new List<int>(); // 0=idle, 1=stopping, 2=selecting, 3=playing
            private List<string> pendingSounds = new List<string>();
            private RadarTrackingModule radarTracker;
            private int ticket = 0;
            private Jet myJet; // Reference to the jet for updating target tracking data
            private List<RadarTrackingModule> myRadars = new List<RadarTrackingModule>(); // Radars from centralized control

            private void UpdateCustomDataWithCache(string gpsCoordinates, string cachedSpeed)
            {
                string[] customDataLines = ParentProgram.Me.CustomData.Split('\n');
                bool cachedLineFound = false;
                bool cachedSpeedFound = false;

                for (int i = 0; i < customDataLines.Length; i++)
                {
                    if (customDataLines[i].StartsWith("Cached:"))
                    {
                        customDataLines[i] = gpsCoordinates;
                        cachedLineFound = true;
                    }
                    else if (customDataLines[i].StartsWith("CachedSpeed:"))
                    {
                        customDataLines[i] = cachedSpeed;
                        cachedSpeedFound = true;
                    }
                }

                if (!cachedLineFound)
                {
                    List<string> customDataList = new List<string>(customDataLines);
                    customDataList.Add(gpsCoordinates);
                    customDataLines = customDataList.ToArray();
                }

                if (!cachedSpeedFound)
                {
                    List<string> customDataList = new List<string>(customDataLines);
                    customDataList.Add(cachedSpeed);
                    customDataLines = customDataList.ToArray();
                }

                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                SystemManager.MarkCustomDataDirty();
            }
            public AirtoAir(Program program, Jet jet) : base(program)
            {
                // Store jet reference
                myJet = jet;

                // Fetch missile bays
                missileBays = jet._bays;
                baySelected = new bool[missileBays.Count];
                name = "Air To Air";
                // Fetch sound blocks
                program.GridTerminalSystem.GetBlocksOfType(
                    soundblocks,
                    b => b.CustomName.Contains("Canopy Side Plate Sound Block")
                );

                // **Initialize lastPlayedSounds with empty strings corresponding to each sound block**
                lastPlayedSounds = new List<string>();
                soundStates = new List<int>();
                pendingSounds = new List<string>();
                foreach (var block in soundblocks)
                {
                    if (block != null && block.IsFunctional)
                    {
                        lastPlayedSounds.Add(block.SelectedSound);
                    }
                    else
                    {
                        lastPlayedSounds.Add("");
                    }
                    soundStates.Add(0); // Initialize to idle state
                    pendingSounds.Add("");
                }

                // Initialize radar tracker with AI blocks (backward compatibility - primary only)
                if (jet._aiFlightBlock != null && jet._aiCombatBlock != null)
                {
                    radarTracker = new RadarTrackingModule(jet._aiFlightBlock, jet._aiCombatBlock);
                }

                // Request radars from centralized control (10 radars for air-to-air)
                myRadars = myJet.RequestRadars(10);
                program.Echo($"AirtoAir: Requested 10 radars, got {myRadars.Count}");
            }

            public override string[] GetOptions()
            {
                var options = new List<string>
                {
                    "Fire Selected Bays",
                    "Toggle Selected Bays",
                    string.Format("Seeker [{0}]", isAirtoAirenabled ? "ON" : "OFF")
                };

                for (int i = 0; i < missileBays.Count; i++)
                {
                    string baySymbol = baySelected[i] ? "[X]" : "[ ]";
                    string bayStatus = missileBays[i]?.IsConnected == true ? "[ON]" : "[OFF]";
                    var mergeBlock = missileBays[i] as IMyShipMergeBlock;
                    bool isConnected = mergeBlock != null && mergeBlock.IsConnected;
                    char colorChar = ColorToChar(isConnected ? 0 : 255, isConnected ? 255 : 0, 0);
                    options.Add(
                        string.Format(
                            "{0}{1} {2} {3}",
                            colorChar,
                            baySymbol,
                            missileBays[i]?.CustomName ?? "Unknown Bay",
                            bayStatus
                        )
                    );
                }
                return options.ToArray();
            }
            public override void ExecuteOption(int index)
            {
                if (index == 0)
                {
                    FireSelectedBays();
                    TransferCacheToSlots();
                }
                else if (index == 1)
                {
                    ToggleSelectedBays();
                }
                else if (index == 2)
                {
                    ToggleSensor();
                    ToggleAirtoAirMode();
                }
                else
                {
                    // Calculate bay index
                    int bayOffset = 3; // Base menu items
                    if (index >= bayOffset && index - bayOffset < missileBays.Count)
                    {
                        ToggleBaySelection(index - bayOffset);
                    }
                }
            }
            private void ToggleAirtoAirMode()
            {
                isAirtoAirenabled = !isAirtoAirenabled;
                UpdateTopdownCustomData();
            }
            private void ToggleSensor()
            {
                // Control AI blocks based on current state (ToggleAirtoAirMode will flip the state)
                // Note: This is called BEFORE ToggleAirtoAirMode, so we check the CURRENT state
                if (isAirtoAirenabled)
                {
                    // Currently ON, about to turn OFF - disable AI blocks
                    if (radarTracker != null)
                    {
                        if (radarTracker.L_CombatBLock != null)
                        {
                            radarTracker.L_CombatBLock.Enabled = false;
                            radarTracker.L_CombatBLock.ApplyAction("ActivateBehavior_Off");
                        }
                        if (radarTracker.L_FlightBlock != null)
                        {
                            radarTracker.L_FlightBlock.Enabled = false;
                            radarTracker.L_FlightBlock.ApplyAction("ActivateBehavior_Off"); // Disable both to fully stop tracking
                        }
                    }
                }
                else
                {
                    // Currently OFF, about to turn ON - enable and configure AI blocks
                    if (radarTracker != null)
                    {
                        if (radarTracker.L_CombatBLock != null)
                        {
                            radarTracker.L_CombatBLock.Enabled = true;
                            radarTracker.L_CombatBLock.UpdateTargetInterval = 4;
                            radarTracker.L_CombatBLock.SearchEnemyComponent.TargetingLockOptions = VRage.Game.ModAPI.Ingame.MyGridTargetingRelationFiltering.Enemy;
                            radarTracker.L_CombatBLock.SelectedAttackPattern = 3; // Intercept mode
                            radarTracker.L_CombatBLock.SetValue<long>("OffensiveCombatIntercept_GuidanceType", 0);
                            radarTracker.L_CombatBLock.SetValueBool("OffensiveCombatIntercept_OverrideCollisionAvoidance", true);
                            radarTracker.L_CombatBLock.ApplyAction("ActivateBehavior_On");
                            radarTracker.L_CombatBLock.ApplyAction("SetTargetingGroup_Weapons");
                            radarTracker.L_CombatBLock.ApplyAction("SetTargetPriority_Closest");
                        }
                        if (radarTracker.L_FlightBlock != null)
                        {
                            radarTracker.L_FlightBlock.Enabled = false; // Must be DISABLED to prevent autopilot control
                            radarTracker.L_FlightBlock.MinimalAltitude = 10;
                            radarTracker.L_FlightBlock.PrecisionMode = false;
                            radarTracker.L_FlightBlock.SpeedLimit = 400;
                            radarTracker.L_FlightBlock.AlignToPGravity = false;
                            radarTracker.L_FlightBlock.CollisionAvoidance = false;
                            radarTracker.L_FlightBlock.ApplyAction("ActivateBehavior_On"); // Behavior ON allows receiving waypoints from Combat Block
                        }
                    }
                }
            }
            private void UpdateTopdownCustomData()
            {
                var customDataLines = ParentProgram.Me.CustomData.Split(
                    new[] { '\n' },
                    StringSplitOptions.None
                );
                bool found = false;
                for (int i = 0; i < customDataLines.Length; i++)
                {
                    if (customDataLines[i].StartsWith("AntiAir:"))
                    {
                        customDataLines[i] = "AntiAir:" + (isAirtoAirenabled ? "true" : "false");
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var lines = new List<string>(customDataLines);
                    lines.Add("AntiAir:" + (isAirtoAirenabled ? "true" : "false"));
                    customDataLines = lines.ToArray();
                }
                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
            }

            public override void Tick()
            {
                ticket++;

                // ===== PASSIVE MODE: Scan and build enemy list without active lock =====
                if (!isAirtoAirenabled)
                {
                    // RadarControl handles all radar updates and enemy list management
                    // AirtoAir just reads the tracking status for target slots
                    for (int i = 0; i < myRadars.Count; i++)
                    {
                        var radarModule = myRadars[i];
                        if (radarModule != null && radarModule.IsTracking)
                        {
                            Vector3D targetPos = radarModule.TargetPosition;
                            Vector3D targetVel = radarModule.TargetVelocity;
                            string targetName = radarModule.TrackedObjectName;

                            // Store in slot for HUD display but DON'T activate
                            int slotIndex = Program.FindEmptyOrOldestSlot(myJet);
                            myJet.targetSlots[slotIndex] = new Jet.TargetSlot(targetPos, targetVel, targetName);
                        }
                    }

                    // Note: Enemy list and radar updates handled by RadarControlModule
                }
                // ===== ACTIVE MODE: Lock closest N enemies and update missile GPS =====
                else
                {
                    // RadarControl handles all radar updates and enemy list management
                    // AirtoAir reads tracking status and activates target GPS for missiles
                    for (int i = 0; i < myRadars.Count; i++)
                    {
                        var radarModule = myRadars[i];
                        if (radarModule != null && radarModule.IsTracking)
                        {
                            Vector3D targetPos = radarModule.TargetPosition;
                            Vector3D targetVel = radarModule.TargetVelocity;
                            string targetName = radarModule.TrackedObjectName;

                            // Find slot or create new one
                            int slotIndex = Program.FindEmptyOrOldestSlot(myJet);
                            myJet.targetSlots[slotIndex] = new Jet.TargetSlot(targetPos, targetVel, targetName);

                            // Note: Enemy list update is handled by RadarControlModule

                            // First radar module (closest target) auto-activates for missiles
                            if (i == 0)
                            {
                                myJet.activeSlotIndex = slotIndex;
                                SystemManager.UpdateActiveTargetGPS(); // Update CustomData for missiles
                            }
                        }
                    }

                    // Backward compatibility: Update old radarTracker if it exists
                    if (radarTracker != null)
                    {
                        radarTracker.UpdateTracking(ticket);
                    }
                }

                // Manage sound blocks
                for (int i = 0; i < soundblocks.Count; i++)
                {
                    var soundBlock = soundblocks[i];
                    if (soundBlock == null || !soundBlock.IsFunctional)
                    {
                        continue;
                    }

                    soundBlock.Volume = 0.3f;

                    string desiredSound = string.Empty;

                    if (isAirtoAirenabled && radarTracker != null)
                    {
                        desiredSound = radarTracker.IsTracking ? "AIM9Lock" : "AIM9Search";
                    }

                    if (lastPlayedSounds.Count <= i)
                    {
                        lastPlayedSounds.Add(string.Empty);
                        lastSoundTickCounters.Add(0);
                    }

                    ChangeSound(desiredSound, soundBlock, i);
                }

                tickCounter++;

                const int ticksPerLoop = 50; // 5 seconds / 0.1s per tick
                if (tickCounter >= ticksPerLoop)
                {
                    LoopSounds();
                    tickCounter = 0;
                }
            }

            private void RestartSounds()
            {
                // Restart all currently playing sounds using the state machine
                for (int i = 0; i < soundblocks.Count; i++)
                {
                    var soundBlock = soundblocks[i];
                    if (soundBlock == null || !soundBlock.IsFunctional)
                        continue;

                    string currentSound = lastPlayedSounds[i];
                    if (!string.IsNullOrEmpty(currentSound) && soundStates[i] == 0)
                    {
                        // Trigger restart via state machine
                        pendingSounds[i] = currentSound;
                        soundStates[i] = 1;
                    }
                }
            }

            private void ChangeSound(string desiredSound, IMySoundBlock block, int index)
            {
                if (block == null || !block.IsFunctional)
                    return;

                // Ensure lists are properly sized
                while (soundStates.Count <= index)
                {
                    soundStates.Add(0);
                    pendingSounds.Add("");
                    lastPlayedSounds.Add("");
                }

                // Multi-tick state machine (Space Engineers requires 1 action per tick)
                // State 0: Idle - check if new sound needed
                // State 1: Stopping - call Stop()
                // State 2: Selecting - set SelectedSound property
                // State 3: Playing - call Play()

                int currentState = soundStates[index];

                // Check if we need to start a new sound (only when idle)
                if (currentState == 0 && desiredSound != lastPlayedSounds[index])
                {
                    pendingSounds[index] = desiredSound;
                    soundStates[index] = 1; // Start sequence
                    return;
                }

                // Execute current state
                switch (currentState)
                {
                    case 1: // Stopping
                        block.Stop();
                        soundStates[index] = 2; // Next tick: select
                        break;

                    case 2: // Selecting
                        // Ensure block is enabled
                        if (!block.Enabled)
                            block.Enabled = true;

                        block.SelectedSound = pendingSounds[index];

                        if (!string.IsNullOrEmpty(pendingSounds[index]))
                        {
                            soundStates[index] = 3; // Next tick: play
                        }
                        else
                        {
                            // Just stopping, go back to idle
                            soundStates[index] = 0;
                            lastPlayedSounds[index] = "";
                        }
                        break;

                    case 3: // Playing
                        block.Play();
                        lastPlayedSounds[index] = pendingSounds[index];
                        soundStates[index] = 0; // Back to idle
                        break;
                }
            }

            private void LoopSounds()
            {
                // Restart all currently playing sounds using the state machine
                for (int i = 0; i < soundblocks.Count; i++)
                {
                    var soundBlock = soundblocks[i];
                    if (soundBlock == null || !soundBlock.IsFunctional)
                        continue;

                    string currentSound =
                        lastPlayedSounds.Count > i ? lastPlayedSounds[i] : string.Empty;

                    if (!string.IsNullOrEmpty(currentSound) && soundStates[i] == 0)
                    {
                        // Trigger restart by setting pending sound (state machine will handle it over 3 ticks)
                        pendingSounds[i] = currentSound;
                        soundStates[i] = 1; // Start the stop-select-play sequence
                    }
                }
            }

            private void FireSelectedBays()
            {
                var selectedBays = new StringBuilder("Firing bays: ");
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        selectedBays.Append(missileBays[i]?.CustomName + " ");
                        FireMissileFromBayWithGps(i);
                    }
                }
            }
            private void FireNextAvailableBay()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    var bay = missileBays[i];
                    if (IsBayReadyToFire(bay))
                    {
                        try
                        {
                            FireMissileFromBayWithGps(i);
                            return;
                        }
                        catch (Exception e)
                        {
                            ParentProgram.Echo($"Bay {i} fire failed: {e.Message}");
                            // Continue to next bay instead of crashing
                        }
                    }
                }
            }
            private bool IsBayReadyToFire(IMyShipMergeBlock bay)
            {
                if (bay == null)
                {
                    return false;
                }
                return bay.IsConnected;
            }
            private void ToggleBaySelection(int bayIndex)
            {
                if (bayIndex >= 0 && bayIndex < baySelected.Length)
                {
                    baySelected[bayIndex] = !baySelected[bayIndex];
                }
            }
            private void ToggleSelectedBays()
            {
                for (int i = 0; i < missileBays.Count; i++)
                {
                    if (baySelected[i])
                    {
                        var bay = missileBays[i];
                        if (bay != null)
                        {
                            bay.Enabled = !bay.Enabled;
                        }
                    }
                }
            }
            private void FireMissileFromBayWithGps(
                int bayIndex,
                Vector3D targetPosition = default(Vector3D)
            )
            {
                try
                {
                    var bay = missileBays[bayIndex];
                    if (bay == null || !bay.IsConnected)
                    {
                        return;
                    }
                    if (targetPosition.Equals(default(Vector3D)))
                    {
                        var customDataLines = ParentProgram.Me.CustomData.Split(
                            new[] { '\n' },
                            StringSplitOptions.RemoveEmptyEntries
                        );
                        var cachedData = customDataLines.FirstOrDefault(
                            line => line.StartsWith("Cached:GPS:")
                        );
                        if (cachedData == null)
                        {
                            return;
                        }
                        var parts = cachedData.Split(':');
                        if (parts.Length < 6)
                        {
                            return;
                        }
                        double x,
                            y,
                            z;
                        if (
                            !double.TryParse(parts[3], out x)
                            || !double.TryParse(parts[4], out y)
                            || !double.TryParse(parts[5], out z)
                        )
                        {
                            return;
                        }
                        targetPosition = new Vector3D(x, y, z);
                    }
                    string gpsData = string.Format(
                        "GPS:Target:{0}:{1}:{2}:#FF75C9F1:",
                        targetPosition.X,
                        targetPosition.Y,
                        targetPosition.Z
                    );
                    UpdateCustomDataWithGpsData(bayIndex, gpsData);
                    bay.ApplyAction("Fire");
                }
                catch (Exception e)
                {
                    ParentProgram.Echo($"FireMissile error: {e.Message}");
                }
            }
            private void UpdateCustomDataWithGpsData(int bayIndex, string gpsData)
            {
                try
                {
                    var customDataLines = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.None)
                        .ToList();
                    string cacheLabel = string.Format("Cache{0}:", bayIndex);
                    int cacheIndex = customDataLines.FindIndex(line => line.StartsWith(cacheLabel));
                    if (cacheIndex != -1)
                    {
                        customDataLines[cacheIndex] = string.Format("{0}{1}", cacheLabel, gpsData);
                    }
                    else
                    {
                        customDataLines.Add(string.Format("{0}{1}", cacheLabel, gpsData));
                    }
                    ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                }
                catch (Exception ex)
                {
                    ParentProgram.Echo(ex.ToString());
                }
            }
            private void TransferCacheToSlots()
            {
                try
                {
                    var customDataLines = ParentProgram.Me.CustomData
                        .Split(new[] { '\n' }, StringSplitOptions.None)
                        .ToList();
                    for (int i = 0; i < customDataLines.Count; i++)
                    {
                        string cacheLabel = string.Format("Cache{0}:", i);
                        int cacheIndex = customDataLines.FindIndex(
                            line => line.StartsWith(cacheLabel)
                        );
                        if (cacheIndex != -1)
                        {
                            var cacheLine = customDataLines[cacheIndex];
                            var cacheContent = cacheLine.Substring(cacheLabel.Length).Trim();
                            if (!string.IsNullOrEmpty(cacheContent))
                            {
                                string targetLabel = string.Format("{0}:", i);
                                int targetIndex = customDataLines.FindIndex(
                                    line => line.StartsWith(targetLabel)
                                );
                                if (targetIndex != -1)
                                {
                                    customDataLines[targetIndex] = string.Format(
                                        "{0} {1}",
                                        targetLabel,
                                        cacheContent
                                    );
                                }
                                else
                                {
                                    customDataLines.Add(
                                        string.Format("{0} {1}", targetLabel, cacheContent)
                                    );
                                }
                                customDataLines[cacheIndex] = cacheLabel;
                            }
                        }
                    }
                    ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                }
                catch (Exception ex)
                {
                    ParentProgram.Echo(ex.ToString());
                }
            }
            private static char ColorToChar(int r, int g, int b)
            {
                const double BIT_SPACING = 255.0 / 7.0;
                return (char)(
                    0xe100
                    + ((int)Math.Round(r / BIT_SPACING) << 6)
                    + ((int)Math.Round(g / BIT_SPACING) << 3)
                    + (int)Math.Round(b / BIT_SPACING)
                );
            }
            public override void HandleSpecialFunction(int key)
            {
                if (key == 5)
                {
                    FireNextAvailableBay();
                    TransferCacheToSlots();
                }
                if (key == 7)
                {
                    TransferCacheToSlots();
                }
            }

            public string hotkeytext =
                "5: Fire Next Available Bay\n6: Fire Selected Bays\n7: Toggle Selected Bays\n";
            public override string GetHotkeys()
            {
                return hotkeytext;
            }
        }
    }
}
