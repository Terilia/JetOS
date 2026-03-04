using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class SystemManager
        {
            private static IMyTextSurface lcdMain;
            private static IMyTextSurface lcdExtra;
            private static IMyTextSurface lcdWeapons;
            private static List<ProgramModule> modules = new List<ProgramModule>();
            public static int currentMenuIndex = 0;
            public static ProgramModule currentModule;
            private static string[] mainMenuOptions;
            private static Program parentProgram;
            private static UIController uiController;
            private static int lastHandledSpecialTick = -1;
            public static int currentTick = 0;
            private static Program.RaycastCameraControl raycastProgram;
            private static Program.HUDModule hudProgram;
            private static Program.ConfigurationModule configModule;
            private static Program.RadarControlModule radarControlModule;
            private static Program.AirtoAir airtoAirModule;
            private static Program.GunControlModule gunControlModule;
            private static List<IMyThrust> thrusters = new List<IMyThrust>();

            // Altitude warning hysteresis
            private static bool altitudeWarningActive = false;

            // FPS tracking
            private static Jet _myJet;
            private static long lastTimeTicks = 0;
            private static double accumulatedTime = 0.0;
            private static int tickCount = 0;

            public static void Initialize(Program program)
            {
                _myJet = new Jet(program.GridTerminalSystem);
                var cockpit =
                    program.GridTerminalSystem.GetBlockWithName("JetOS") as IMyTextSurfaceProvider;
                if (cockpit != null)
                {
                    lcdMain = cockpit.GetSurface(0);
                    lcdMain.ContentType = ContentType.SCRIPT;
                    lcdMain.BackgroundColor = Color.Transparent;
                    lcdExtra = cockpit.GetSurface(1);
                    lcdExtra.ContentType = ContentType.SCRIPT;
                    lcdExtra.BackgroundColor = Color.Transparent;
                    lcdWeapons = cockpit.GetSurface(2);
                    lcdWeapons.ContentType = ContentType.SCRIPT;
                    lcdWeapons.Script = "";
                    lcdWeapons.BackgroundColor = Color.Black;
                    lcdWeapons.ScriptBackgroundColor = Color.Black;
                    lcdWeapons.ScriptForegroundColor = Color.White;
                    lcdWeapons.FontColor = new Color(25, 217, 140, 255);
                    lcdWeapons.FontSize = 0.1f;
                    lcdWeapons.TextPadding = 0f;
                    lcdWeapons.Alignment = TextAlignment.CENTER;

                    for (int i = 0; i < 3; i++)
                    {
                        cockpit.GetSurface(i).FontColor = new Color(25, 217, 140, 255);
                    }
                }

                thrusters = _myJet._thrusters;
                parentProgram = program;
                modules = new List<ProgramModule>();

                // Initialize subsystems
                CustomDataManager.Initialize(program.Me);
                SoundManager.Initialize(program.GridTerminalSystem);

                // Initialize centralized radar control FIRST
                radarControlModule = new RadarControlModule(parentProgram, _myJet);
                _myJet.radarControl = radarControlModule;
                modules.Add(radarControlModule);

                modules.Add(new AirToGround(parentProgram, _myJet));
                airtoAirModule = new AirtoAir(parentProgram, _myJet);
                modules.Add(airtoAirModule);

                raycastProgram = new RaycastCameraControl(parentProgram, _myJet);
                hudProgram = new HUDModule(parentProgram, _myJet, lcdWeapons, radarControlModule);
                modules.Add(hudProgram);
                modules.Add(raycastProgram);
                uiController = new UIController(lcdMain, lcdExtra);

                configModule = new ConfigurationModule(parentProgram);
                modules.Add(configModule);

                gunControlModule = new GunControlModule(parentProgram, _myJet);
                modules.Add(gunControlModule);
                mainMenuOptions = new string[modules.Count];
                for (int i = 0; i < modules.Count; i++)
                {
                    mainMenuOptions[i] = modules[i].name;
                }
                currentModule = null;
            }

            // CustomData Cache - delegates to CustomDataManager
            public static string GetCustomDataValue(string key)
            {
                return CustomDataManager.GetValue(key);
            }

            public static void SetCustomDataValue(string key, string value)
            {
                CustomDataManager.SetValue(key, value);
            }

            public static bool TryGetCustomDataValue(string key, out string value)
            {
                return CustomDataManager.TryGetValue(key, out value);
            }

            public static void RemoveCustomDataValue(string key)
            {
                CustomDataManager.RemoveValue(key);
            }

            public static void MarkCustomDataDirty()
            {
                CustomDataManager.MarkDirty();
            }

            public static float GetConfigValue(string configName)
            {
                if (configModule != null)
                    return configModule.GetValue(configName);
                return 0f;
            }

            public static void Main(string argument, UpdateType updateSource)
            {
                currentTick++;
                Jet.GameTicks++;

                Vector3D cockpitPosition = _myJet.GetCockpitPosition();
                MatrixD cockpitMatrix = _myJet.GetCockpitMatrix();

                double velocity = _myJet.GetVelocity();
                double velocityKnots = velocity * 1.94384;
                double altitude;
                _myJet._cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude);

                // Altitude warning with hysteresis
                float altWarn = GetConfigValue("altitude_warning");
                float spdWarn = GetConfigValue("speed_warning");
                if (altitudeWarningActive)
                {
                    if (velocityKnots < spdWarn - 20 || altitude > altWarn + 40)
                    {
                        altitudeWarningActive = false;
                    }
                    else
                    {
                        SoundManager.RequestWarning("Tief", SoundManager.PRIORITY_ALTITUDE);
                    }
                }
                else
                {
                    if (velocityKnots > spdWarn && altitude < altWarn)
                    {
                        altitudeWarningActive = true;
                        SoundManager.RequestWarning("Tief", SoundManager.PRIORITY_ALTITUDE);
                    }
                }

                // Run unified sound system
                SoundManager.Tick(currentTick);

                if (currentTick == lastHandledSpecialTick)
                    return;
                lastHandledSpecialTick = currentTick;

                if (string.IsNullOrWhiteSpace(argument))
                {
                    DisplayMenu();
                }
                else
                {
                    HandleInput(argument);
                }

                if (currentModule != null)
                {
                    currentModule.Tick();
                }

                if (raycastProgram != null)
                {
                    raycastProgram.Tick();
                    hudProgram.Tick();
                }

                if (radarControlModule != null && currentModule != radarControlModule)
                {
                    radarControlModule.Tick();
                }

                if (airtoAirModule != null && currentModule != airtoAirModule)
                {
                    airtoAirModule.Tick();
                }

                if (gunControlModule != null && currentModule != gunControlModule)
                {
                    gunControlModule.Tick();
                }

                HandleSpecialFunctionInputs(argument);

                // FPS tracking
                if (lastTimeTicks == 0)
                    lastTimeTicks = DateTime.UtcNow.Ticks;

                long nowTicks = DateTime.UtcNow.Ticks;
                long diffTicks = nowTicks - lastTimeTicks;
                double deltaSeconds = diffTicks / (double)TimeSpan.TicksPerSecond;

                accumulatedTime += deltaSeconds;
                tickCount++;
                if (accumulatedTime >= 1.0)
                {
                    accumulatedTime = 0.0;
                    tickCount = 0;
                }

                lastTimeTicks = nowTicks;
            }

            private static void HandleSpecialFunctionInputs(string argument)
            {
                int key;
                if (int.TryParse(argument, out key))
                {
                    if (currentModule != null)
                    {
                        currentModule.HandleSpecialFunction(key);
                    }
                }
            }

            private static void DisplayMenu()
            {
                string[] options =
                    currentModule == null ? mainMenuOptions : currentModule.GetOptions();
                uiController.RenderMainScreen(
                    title: "System Menu",
                    options: options,
                    currentMenuIndex: currentMenuIndex,
                    navigationInstructions: "1: ▲  | 2: ▼ | 3: Select | 4: Back | 5-8: Special | 9: Menu"
                );

                var area = new RectangleF(0, 0, 512, 512);
                uiController.RenderCustomExtraFrame(
                    (frame, renderArea) =>
                    {
                        GridVisualization.Render(frame, renderArea, parentProgram, _myJet, radarControlModule);
                    },
                    area
                );
            }

            private static void HandleInput(string argument)
            {
                switch (argument)
                {
                    case "1":
                        NavigateUp();
                        break;
                    case "2":
                        NavigateDown();
                        break;
                    case "3":
                        ExecuteCurrentOption();
                        break;
                    case "4":
                        DeselectOrGoBack();
                        break;
                    case "9":
                        ReturnToMainMenu();
                        break;
                    case "5":
                        break;
                    case "6":
                        _myJet.offset += -1;
                        break;
                    case "7":
                        _myJet.offset += 1;
                        break;
                    case "8":
                        FlipGPS();
                        break;
                    default:
                        break;
                }
            }

            private static void FlipGPS()
            {
                var sorted = _myJet.GetEnemiesSortedByDistance();
                if (sorted.Count == 0)
                {
                    _myJet.ClearSelection();
                    return;
                }

                // Find current selection in sorted list by identity match
                int currentIndex = -1;
                var selected = _myJet.GetSelectedEnemy();
                if (selected.HasValue)
                {
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        // Match by EntityId first, then Name
                        if (selected.Value.EntityId != 0 && sorted[i].EntityId == selected.Value.EntityId)
                        {
                            currentIndex = i;
                            break;
                        }
                        if (sorted[i].Name == selected.Value.Name && sorted[i].SourceIndex == selected.Value.SourceIndex)
                        {
                            currentIndex = i;
                            break;
                        }
                    }
                }

                // Advance to next entry (wrapping)
                int nextIndex = (currentIndex + 1) % sorted.Count;
                var nextContact = sorted[nextIndex];

                // Check if this is the pinned target
                if (_myJet.pinnedRaycastTarget.HasValue &&
                    nextContact.SourceIndex == _myJet.pinnedRaycastTarget.Value.SourceIndex &&
                    nextContact.Name == _myJet.pinnedRaycastTarget.Value.Name &&
                    Vector3D.Distance(nextContact.Position, _myJet.pinnedRaycastTarget.Value.Position) < 1.0)
                {
                    _myJet.SelectPinned();
                }
                else
                {
                    _myJet.SelectEnemy(nextContact);
                }

                UpdateActiveTargetGPS();
            }

            public static void UpdateActiveTargetGPS()
            {
                var selected = _myJet.GetSelectedEnemy();
                if (!selected.HasValue)
                {
                    return;
                }

                Vector3D targetPos = selected.Value.Position;
                Vector3D targetVel = selected.Value.Velocity;

                // Write through cache — no direct Me.CustomData access
                string gpsValue = $"GPS:Target:{targetPos.X}:{targetPos.Y}:{targetPos.Z}:#FF75C9F1:";
                string speedValue = $"{targetVel.X}:{targetVel.Y}:{targetVel.Z}:#FF75C9F1:";

                SetCustomDataValue("Cached", gpsValue);
                SetCustomDataValue("CachedSpeed", speedValue);
            }

            private static void NavigateUp()
            {
                if (currentModule != null && currentModule.HandleNavigation(true))
                {
                    return;
                }

                if (currentMenuIndex > 0)
                {
                    currentMenuIndex--;
                }
            }

            private static void NavigateDown()
            {
                if (currentModule != null && currentModule.HandleNavigation(false))
                {
                    return;
                }

                int totalOptions = (
                    currentModule == null
                        ? mainMenuOptions.Length
                        : currentModule.GetOptions().Length
                );
                if (currentMenuIndex < totalOptions - 1)
                {
                    currentMenuIndex++;
                }
            }

            private static void ExecuteCurrentOption()
            {
                if (currentModule == null)
                {
                    currentModule = modules[currentMenuIndex];
                    currentMenuIndex = 0;
                }
                else
                {
                    currentModule.ExecuteOption(currentMenuIndex);
                }
            }

            private static void DeselectOrGoBack()
            {
                if (currentModule != null)
                {
                    if (currentModule.HandleBack())
                    {
                        return;
                    }

                    currentModule = null;
                    currentMenuIndex = 0;
                }
            }

            public static void ReturnToMainMenu()
            {
                currentModule = null;
                currentMenuIndex = 0;
            }

            public static IMyTextSurface GetMainLCD()
            {
                return lcdMain;
            }

            public static IMyTextSurface GetExtraLCD()
            {
                return lcdExtra;
            }

            public static GunControlModule GetGunControl()
            {
                return gunControlModule;
            }
        }
    }
}
