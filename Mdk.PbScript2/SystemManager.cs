using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
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

            // Cached MFD pages (built once at init; rebuilt for the main menu each frame
            // since module list/selection drives MenuItems).
            private static GridMfdPage _gridPage;
            private static WeaponMfdPage _weaponPage;
            private static MenuMfdPage _mainMenuPage;

            // Wall-clock timestamp of the last main-MFD page swap. The shader-style
            // transition replay runs for PAGE_FADE_DURATION after this. -1 = no fade pending.
            private static double _mainTransitionStart = -1;
            private static ProgramModule _lastModule;

            // Rolling capture of the main MFD's sprite list (refilled each tick).
            // _outgoingSnapshot is a frozen copy taken at the moment of module switch — it's
            // what gets replayed with shader transforms across the entire transition window.
            private static List<MySprite> _mainCapture = new List<MySprite>(384);
            private static List<MySprite> _outgoingSnapshot = new List<MySprite>(384);
            private static List<ProgramModule> modules = new List<ProgramModule>();
            public static int currentMenuIndex = 0;
            public static ProgramModule currentModule;
            private static string[] mainMenuOptions;
            private static Program parentProgram;
            private static UIController uiController;
            private static string _pendingArgument = null;
            private static Program.HUDModule hudProgram;
            private static Program.ConfigurationModule configModule;
            private static Program.RadarControlModuleV2 radarControlModule;
            private static Program.AirtoAir airtoAirModule;
            private static Program.TerrainModule terrainModule;
            // Altitude warning hysteresis
            private static bool altitudeWarningActive = false;
            private static bool bingoFuelActive = false;
            public static bool AltitudeWarningActive => altitudeWarningActive;
            public static bool RwrActive => radarControlModule != null && radarControlModule.HasRwrThreat;
            public static bool TrackLocked => radarControlModule != null && radarControlModule.IsTrackLocked;

            // Timing foundation — lag-resistant, uses wall-clock delta.
            // DeltaSeconds: seconds between this Main() call and the last.
            // ElapsedSeconds: accumulated wall-clock time since script start.
            // Both are clamped per-tick to avoid huge jumps when the script resumes after pause.
            public static double DeltaSeconds = 1.0 / 60.0;
            public static double ElapsedSeconds = 0.0;

            private static Jet _myJet;

            public static void Initialize(Program program)
            {
                _myJet = new Jet(program.GridTerminalSystem);
                var cockpit =
                    program.GridTerminalSystem.GetBlockWithName("JetOS [HFPS]") as IMyTextSurfaceProvider;
                if (cockpit != null && cockpit.SurfaceCount >= 3)
                {
                    lcdMain = cockpit.GetSurface(0);
                    PrepSurface(lcdMain);
                    lcdExtra = cockpit.GetSurface(1);
                    PrepSurface(lcdExtra);
                    lcdWeapons = cockpit.GetSurface(2);
                    PrepSurface(lcdWeapons);
                    lcdWeapons.BackgroundColor = Color.Black;
                    lcdWeapons.ScriptBackgroundColor = Color.Black;
                    lcdWeapons.ScriptForegroundColor = Color.White;
                    lcdWeapons.FontColor = Cr(25, 217, 140, 255);
                }

                parentProgram = program;
                modules = new List<ProgramModule>();

                // Initialize subsystems
                CustomDataManager.Initialize(program.Me);
                SoundManager.Initialize(program.GridTerminalSystem);
                TerrainData.Probe(program.Me);
                TerrainData.Init(program.Me);

                // Initialize centralized radar control FIRST
                radarControlModule = new RadarControlModuleV2(parentProgram, _myJet);
                modules.Add(radarControlModule);

                airtoAirModule = new AirtoAir(parentProgram, _myJet);
                modules.Add(airtoAirModule);

                hudProgram = new HUDModule(parentProgram, _myJet, radarControlModule);
                modules.Add(hudProgram);
                uiController = new UIController();

                configModule = new ConfigurationModule(parentProgram, _myJet);
                modules.Add(configModule);

                terrainModule = new TerrainModule(parentProgram, _myJet);
                modules.Add(terrainModule);

                modules.Add(new DatalinkModule(parentProgram, _myJet));

                mainMenuOptions = new string[modules.Count];
                for (int i = 0; i < modules.Count; i++)
                {
                    mainMenuOptions[i] = modules[i].name;
                }
                currentModule = null;

                // Build static MFD pages for surface 1 (status grid) and surface 2 (weapons).
                _gridPage = new GridMfdPage(parentProgram, _myJet, hudProgram);
                _weaponPage = new WeaponMfdPage(hudProgram);
                _mainMenuPage = new MenuMfdPage("SYS", mainMenuOptions, showSidebar: true,
                    sidebarRenderer: RenderDefaultSidebar);
            }

            // Default sidebar renderer — used by main menu and every module menu (fuel/battery/engine/terrain).
            public static void RenderDefaultSidebar(RectangleF area)
            {
                StatusPanelRenderer.Render(area, _myJet, hudProgram);
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

            public static void MarkCustomDataDirty()
            {
                CustomDataManager.MarkDirty();
            }

            public static double GetSmoothedAoA()
            {
                return hudProgram != null ? hudProgram.smoothedAoA : 0;
            }

            public static float GetConfigValue(string configName)
            {
                if (configModule != null)
                    return configModule.GetValue(configName);
                return 0f;
            }

            public static void Main(string argument, UpdateType updateSource)
            {
                // When a toolbar button is pressed (or Run is clicked in the terminal), SE calls
                // Main() in the same sim tick as the regular Update1 pass. We must only process once
                // to prevent double-advancing time, double-ticking modules, etc.
                if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal)) != 0)
                {
                    _pendingArgument = argument;
                    return;
                }
                if (!SE(_pendingArgument))
                {
                    argument = _pendingArgument;
                    _pendingArgument = null;
                }

                // Update wall-clock timing. Clamp to avoid huge jumps after pause.
                double dt = parentProgram.Runtime.TimeSinceLastRun.TotalSeconds;
                if (dt <= 0 || dt > 1.0) dt = 1.0 / 60.0;
                DeltaSeconds = dt;
                ElapsedSeconds += dt;
                Jet.GameSeconds = ElapsedSeconds;

                // Cache cockpit, resource, and engine display state once per tick.
                _myJet.UpdateTickCache();
                DatalinkV2.Tick(parentProgram, _myJet);

                double velocity = _myJet.CockpitSpeed;
                double velocityKnots = velocity * 1.94384;
                double altitude = _myJet.SurfaceAltitude;

                // Terrain: download chunks during init, update tangent vectors when ready
                TerrainData.Tick(parentProgram.Me, _myJet.CockpitPosition);

                // Altitude warning with hysteresis
                float altWarn = GetConfigValue(CFG_ALTITUDE_WARNING);
                float spdWarn = GetConfigValue(CFG_SPEED_WARNING);
                if (altitudeWarningActive)
                {
                    if (velocityKnots < spdWarn - 20 || altitude > altWarn + 40)
                    {
                        altitudeWarningActive = false;
                    }
                    else
                    {
                        SoundManager.Event(SoundManager.PULL_UP);
                    }
                }
                else
                {
                    if (velocityKnots > spdWarn && altitude < altWarn)
                    {
                        altitudeWarningActive = true;
                        SoundManager.Event(SoundManager.PULL_UP);
                    }
                }

                float bingo = GetConfigValue(CFG_BINGO_FUEL);
                if (bingoFuelActive)
                {
                    if (_myJet.FuelPct > bingo + 0.05f)
                        bingoFuelActive = false;
                }
                else if (_myJet.tanks.Count > 0 && _myJet.FuelPct < bingo)
                {
                    bingoFuelActive = true;
                    SoundManager.Event(SoundManager.BINGO);
                }

                if (_myJet.LeftEngineBad) SoundManager.Event(SoundManager.ENGINE_LEFT);
                if (_myJet.RightEngineBad) SoundManager.Event(SoundManager.ENGINE_RIGHT);

                if (SW(argument))
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

                TickBackground(hudProgram);
                TickBackground(radarControlModule);
                TickBackground(airtoAirModule);
                configModule.TickSystems();

                HandleSpecialFunctionInputs(argument);

                // Process sound AFTER all modules have made their requests.
                // Previously this ran before module Tick() calls, which meant
                // RadarControlModuleV2 and AirtoAir sound requests were delayed
                // by one tick (they'd be processed next tick instead of this one).
                SoundManager.Tick(ElapsedSeconds);
                Jet.IC = parentProgram.Runtime.CurrentInstructionCount;
                if (Jet.IC > Jet.IP) Jet.IP = Jet.IC;
                Jet.IA = (Jet.IA * 59 + Jet.IC) / 60;
            }

            static void TickBackground(ProgramModule module)
            {
                if (module != null && currentModule != module)
                    module.Tick();
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
                if (currentModule != _lastModule)
                {
                    _mainTransitionStart = ElapsedSeconds;
                    _lastModule = currentModule;
                    // Freeze the just-rendered outgoing page as the transition replay source.
                    _outgoingSnapshot.Clear();
                    _outgoingSnapshot.AddRange(_mainCapture);
                }

                MfdPage mainPage = currentModule == null ? _mainMenuPage : currentModule.GetPage();

                _mainCapture.Clear();
                // Only pass the snapshot while the transition window is open; null after it ends.
                bool inTransition = _mainTransitionStart >= 0
                    && (ElapsedSeconds - _mainTransitionStart) < UIController.PAGE_FADE_DURATION;
                List<MySprite> prevFrame = inTransition ? _outgoingSnapshot : null;
                uiController.Render(mainPage, lcdMain, currentMenuIndex, _mainTransitionStart, _mainCapture, prevFrame);
                uiController.Render(_gridPage, lcdExtra);
                uiController.Render(_weaponPage, lcdWeapons);
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
                        if (sorted[i].Matches(selected.Value))
                        {
                            currentIndex = i;
                            break;
                        }
                    }
                }

                // Advance to next entry (wrapping)
                int nextIndex = (currentIndex + 1) % sorted.Count;
                _myJet.SelectEnemy(sorted[nextIndex]);
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

            public static GunControlModule GetGunControl()
            {
                return configModule != null ? configModule.Gun : null;
            }

        }
    }
}
