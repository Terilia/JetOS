using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Static orchestrator for JetOS HQ — the station analogue of the jet's SystemManager.
        // Owns timing, the double-Main guard, module dispatch, toolbar input, single-MFD render
        // (with page transitions), and the operator Echo panel.
        static class SystemManager
        {
            // Lag-resistant wall-clock timing (mirrors the jet).
            public static double DeltaSeconds = 1.0 / 60.0;
            public static double ElapsedSeconds = 0.0;

            // Instruction-count health (shown in chrome + Echo).
            public static int IC, IP, IA;

            public static int currentMenuIndex = 0;
            public static ProgramModule currentModule;

            private static Program parentProgram;
            private static Station _station;
            private static UIController _ui;
            private static List<ProgramModule> _modules = new List<ProgramModule>();
            private static string[] _mainMenu;
            private static string _pendingArgument = null;

            // Page-transition state (single MFD).
            private static double _transitionStart = -1;
            private static ProgramModule _lastModule;
            private static readonly List<MySprite> _capture = new List<MySprite>(384);
            private static readonly List<MySprite> _snapshot = new List<MySprite>(384);

            private static readonly StringBuilder _echo = new StringBuilder();

            public static Station Station => _station;
            public static Program Pb => parentProgram;

            // ZONE mode: the ZonesModule owns the stitched mouse cursor + the map-as-canvas.
            public static bool ZoneActive => currentModule is ZonesModule;

            public static void Initialize(Program program)
            {
                parentProgram = program;
                _station = new Station(program);
                _ui = new UIController();

                _modules = new List<ProgramModule>
                {
                    new TacticalModule(program),
                    new RosterModule(program),
                    new OrdersModule(program),
                    new MapModule(program),
                    new ZonesModule(program),
                    new ConfigModule(program),
                };

                _mainMenu = new string[_modules.Count];
                for (int i = 0; i < _modules.Count; i++) _mainMenu[i] = _modules[i].name;
                currentModule = null;
                currentMenuIndex = 0;

                StorageDoc.Init(program);              // must precede any consumer that Loads
                DatalinkHQ.Initialize(program, _station); // calls HQConfig.Load (reads StorageDoc)
                ZoneStore.Load(program);
                TerrainData.Probe(program.Me);
                TerrainData.Init(program.Me);
            }

            public static void Main(string argument, UpdateType updateSource)
            {
                // Double-Main guard: a toolbar press fires Main twice in one sim tick (Trigger
                // then Update1). Stash the arg on the Trigger pass, consume it on the next.
                if ((updateSource & UpdateType.Trigger) != 0)
                {
                    _pendingArgument = argument;
                    return;
                }
                if (!SE(_pendingArgument))
                {
                    argument = _pendingArgument;
                    _pendingArgument = null;
                }

                double dt = parentProgram.Runtime.TimeSinceLastRun.TotalSeconds;
                if (dt <= 0 || dt > 1.0) dt = 1.0 / 60.0;
                DeltaSeconds = dt;
                ElapsedSeconds += dt;

                _station.UpdateTickCache();
                Canvas.Sync(_station);   // keep stitched-canvas dims current even on non-cursor ticks
                TerrainData.Tick(parentProgram.Me, _station.Position);
                DatalinkHQ.Tick();
                MapView.SyncTracks();

                HandleInput(argument);
                if (currentModule != null) currentModule.Tick();
                HandleSpecialFunctionInputs(argument);

                // SEEKER (toggle globally with key 8) reverts the map to mouse-look pan under the
                // center brackets; the global cursor is suspended so the mouse drives the pan. Never
                // in ZONE mode (the cursor is needed there for drawing).
                bool seekerMode = MapView.SeekerOn && !ZoneActive;

                // Global mouse cursor — live whenever the operator is seated (except seeker mode).
                if (_station.SeatControlled && !seekerMode) MouseCursor.Tick(_station, DeltaSeconds);
                else MouseCursor.Deactivate();

                // ZONE mode draws with the cursor; otherwise the cursor clicks MFD menu rows.
                if (ZoneActive) ZoneEditor.Tick();
                else { ZoneEditor.Reset(); HandleMenuClick(); }

                // Map input: seeker → mouse-look pan; else edge-scroll while the cursor hovers the map.
                bool overMap = MouseCursor.Visible && Canvas.OnRight(MouseCursor.X);
                bool mapInput = _station.SeatControlled && (seekerMode || overMap);
                MapView.UpdateInput(_station, mapInput, seekerMode,
                    MouseCursor.X - Canvas.LW, Cl(MouseCursor.Y, 0f, Canvas.RH), DeltaSeconds);

                Render();
                EchoStatus();

                IC = parentProgram.Runtime.CurrentInstructionCount;
                if (IC > IP) IP = IC;
                IA = (IA * 59 + IC) / 60;
            }

            public static void RenderDefaultSidebar(RectangleF area) => StationPanel.Render(area);

            private static void Render()
            {
                // Dedicated map screen renders every tick, independent of the MFD.
                if (_station.Map != null) MapView.RenderFull(_station.Map);

                IMyTextSurface surface = _station.Mfd;
                if (surface == null) return;

                // Fallback: with no dedicated HQ MAP screen, selecting MAP takes over the MFD.
                if (_station.Map == null && currentModule is MapModule)
                {
                    MapView.RenderFull(surface);
                    _lastModule = currentModule;
                    _capture.Clear();
                    return;
                }

                if (currentModule != _lastModule)
                {
                    _transitionStart = ElapsedSeconds;
                    _lastModule = currentModule;
                    _snapshot.Clear();
                    for (int i = 0; i < _capture.Count; i++) _snapshot.Add(_capture[i]);
                }

                MfdPage page = currentModule == null
                    ? new MenuMfdPage("HQ", _mainMenu, true, StationPanel.Render)
                    : currentModule.GetPage();

                _capture.Clear();
                bool inTransition = _transitionStart >= 0
                    && (ElapsedSeconds - _transitionStart) < UIController.PAGE_FADE_DURATION;
                List<MySprite> prev = inTransition ? _snapshot : null;
                _ui.Render(page, surface, currentMenuIndex, _transitionStart, _capture, prev);
            }

            private static void EchoStatus()
            {
                _echo.Clear();
                _echo.Append("JetOS HQ  ").Append(DatalinkHQ.Tacsit).Append('\n');
                _echo.Append("CH ").Append(IGC_CHANNEL).Append('\n');
                _station.AppendDiagnostics(_echo);
                _echo.Append("JETS ").Append(DatalinkHQ.JetCount)
                     .Append("  CTC ").Append(DatalinkHQ.ContactCount).Append('\n');
                _echo.Append("IC ").Append(IC).Append(" avg ").Append(IA).Append(" pk ").Append(IP);
                parentProgram.Echo(_echo.ToString());
            }

            // ── Input ──
            private static void HandleInput(string argument)
            {
                switch (argument)
                {
                    case "1": NavigateUp(); break;
                    case "2": NavigateDown(); break;
                    case "3": ExecuteCurrentOption(); break;
                    case "4": DeselectOrGoBack(); break;
                    // 8 globally swaps cursor mode <-> seeker brackets (except in ZONE mode, where
                    // 8 closes the polygon being drawn).
                    case "8": if (!ZoneActive) MapView.ToggleSeeker(); break;
                    case "9": ReturnToMainMenu(); break;
                    default: break;
                }
            }

            private static void HandleSpecialFunctionInputs(string argument)
            {
                int key;
                if (int.TryParse(argument, out key) && currentModule != null)
                    currentModule.HandleSpecialFunction(key);
            }

            private static void NavigateUp()
            {
                if (currentModule != null && currentModule.HandleNavigation(true)) return;
                if (currentMenuIndex > 0) currentMenuIndex--;
            }

            private static void NavigateDown()
            {
                if (currentModule != null && currentModule.HandleNavigation(false)) return;
                int total = currentModule == null ? _mainMenu.Length : currentModule.GetOptions().Length;
                if (currentMenuIndex < total - 1) currentMenuIndex++;
            }

            private static void ExecuteCurrentOption()
            {
                if (currentModule == null)
                {
                    if (currentMenuIndex < 0 || currentMenuIndex >= _modules.Count) return;
                    currentModule = _modules[currentMenuIndex];
                    currentMenuIndex = 0;
                }
                else currentModule.ExecuteOption(currentMenuIndex);
            }

            // Global mouse click on an MFD menu row → select it + activate (enter module / run option).
            // Hit regions are from last render; bound the index against the CURRENT page so a click
            // coinciding with a back/menu change can't run a stale (out-of-range) option.
            private static void HandleMenuClick()
            {
                if (!MouseCursor.Visible || Canvas.OnRight(MouseCursor.X) || !MouseCursor.PrimaryClick) return;
                int i = UiHit.Hit(MouseCursor.X, Cl(MouseCursor.Y, 0f, Canvas.LH));
                if (i < 0) return;
                int max = currentModule == null ? _mainMenu.Length : currentModule.GetOptions().Length;
                if (i >= max) return;
                currentMenuIndex = i;
                ExecuteCurrentOption();
            }

            private static void DeselectOrGoBack()
            {
                if (currentModule != null)
                {
                    if (currentModule.HandleBack()) return;
                    currentModule = null;
                    currentMenuIndex = 0;
                }
            }

            public static void ReturnToMainMenu()
            {
                currentModule = null;
                currentMenuIndex = 0;
            }
        }
    }
}
