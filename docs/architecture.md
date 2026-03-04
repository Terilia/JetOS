# System Architecture

## Tick Loop

Every game tick (~16ms at 60 Hz), `Program.Main()` delegates to `SystemManager.Main()` which orchestrates all modules:

```mermaid
flowchart TD
    A["Program.Main()"] --> B["SystemManager.Main()"]
    B --> C["Increment GameTicks"]
    C --> D["Altitude/Speed Warning Check"]
    D --> E["SoundManager.Tick()"]
    E --> F{Has toolbar argument?}
    F -- Yes --> G["HandleInput(arg)"]
    F -- No --> H["DisplayMenu()"]
    G --> I["Active Module .Tick()"]
    H --> I
    I --> J["Background Ticks"]

    J --> J1["RaycastCameraControl.Tick()"]
    J --> J2["HUDModule.Tick()"]
    J --> J3["RadarControlModule.Tick()"]
    J --> J4["AirtoAir.Tick()"]
    J --> J5["GunControlModule.Tick()"]

    style J1 fill:#2d5a2d
    style J2 fill:#2d5a2d
    style J3 fill:#2d4a5a
    style J4 fill:#2d4a5a
    style J5 fill:#2d4a5a
```

> Green = always ticks. Blue = ticks only when not the active module (avoids double-tick).

**Source:** `SystemManager.cs` — `Main()` method

---

## Initialization Order

Module initialization order matters because some modules depend on others:

```mermaid
flowchart TD
    P["Program()"] --> SM["SystemManager.Initialize(this)"]
    SM --> JET["new Jet(GridTerminalSystem)"]
    JET --> CDM["CustomDataManager.Initialize()"]
    CDM --> SND["SoundManager.Initialize()"]
    SND --> RAD["new RadarControlModule()"]
    RAD --> |"inject into Jet"| JETREF["Jet.radarControl = radarControlModule"]
    JETREF --> ATG["new AirToGround()"]
    ATG --> ATA["new AirtoAir()"]
    ATA --> RCC["new RaycastCameraControl()"]
    RCC --> HUD["new HUDModule(radarControlModule)"]
    HUD --> CFG["new ConfigurationModule()"]
    CFG --> GUN["new GunControlModule()"]
    GUN --> UI["new UIController(lcdMain, lcdExtra)"]

    style RAD fill:#8b4513,color:#fff
    style HUD fill:#2d5a2d
```

> RadarControlModule initializes first because HUDModule and AirtoAir reference it.

**Source:** `SystemManager.cs` — `Initialize()` method

---

## Input Routing

Toolbar arguments (numpad 1-9) are dispatched through `HandleInput()`:

```mermaid
flowchart LR
    INPUT["Toolbar Arg"] --> SW{Switch}
    SW -- "1" --> NAV_UP["NavigateUp()"]
    SW -- "2" --> NAV_DN["NavigateDown()"]
    SW -- "3" --> EXEC["ExecuteCurrentOption()"]
    SW -- "4" --> BACK["DeselectOrGoBack()"]
    SW -- "6" --> TRIM_DN["AoA offset -= 1"]
    SW -- "7" --> TRIM_UP["AoA offset += 1"]
    SW -- "8" --> FLIP["FlipGPS() — cycle targets"]
    SW -- "9" --> MENU["ReturnToMainMenu()"]

    NAV_UP --> MOD_NAV{"module.HandleNavigation()?"}
    NAV_DN --> MOD_NAV
    MOD_NAV -- "true" --> CUSTOM["Module handles it"]
    MOD_NAV -- "false" --> DEFAULT["Default menu nav"]

    EXEC --> MOD_SEL{Module selected?}
    MOD_SEL -- "No" --> ENTER["Enter module from menu"]
    MOD_SEL -- "Yes" --> OPT["module.ExecuteOption(index)"]

    BACK --> MOD_BACK{"module.HandleBack()?"}
    MOD_BACK -- "true" --> STAY["Stay in module"]
    MOD_BACK -- "false" --> EXIT["Exit to main menu"]
```

**Source:** `SystemManager.cs` — `HandleInput()`, `NavigateUp()`, `NavigateDown()`, `ExecuteCurrentOption()`, `DeselectOrGoBack()`

---

## Module System

All modules inherit from `ProgramModule`:

```mermaid
classDiagram
    class ProgramModule {
        <<abstract>>
        +string name
        +GetOptions()* string[]
        +ExecuteOption(int)*
        +Tick()
        +HandleSpecialFunction(int)
        +HandleNavigation(bool) bool
        +HandleBack() bool
        +GetHotkeys() string
    }

    ProgramModule <|-- RadarControlModule
    ProgramModule <|-- AirToGround
    ProgramModule <|-- AirtoAir
    ProgramModule <|-- HUDModule
    ProgramModule <|-- RaycastCameraControl
    ProgramModule <|-- ConfigurationModule
    ProgramModule <|-- GunControlModule
```

### Module Behavior Summary

| Module | Menu Name | Background Tick | Depends On |
|--------|-----------|----------------|------------|
| RadarControlModule | Radar Control | Yes (if not active) | Jet |
| AirToGround | Air To Ground | No | Jet |
| AirtoAir | Air To Air | Yes (if not active) | Jet, RadarTrackingModule |
| HUDModule | HUD Control | Yes (always) | Jet, RadarControlModule |
| RaycastCameraControl | TargetingPod Control | Yes (always) | Jet |
| ConfigurationModule | Configuration | No | — |
| GunControlModule | Gun Control | Yes (if not active) | Jet, BallisticsCalculator |

**Source:** `Modules/ProgramModule.cs` (base class), `SystemManager.cs` lines 78-100 (instantiation), lines 194-218 (tick routing)

---

## Core Data Holders

```mermaid
flowchart TD
    subgraph Jet ["Jet (Hardware Abstraction)"]
        BLOCKS["Block References\ncockpit, thrusters, bays,\nguns, tanks, stabilizers"]
        ENEMIES["enemyList: List&lt;EnemyContact&gt;\nAll detected targets with decay"]
        SELECT["Selection State\nselectedEnemyEntityId\nselectedEnemyName\npinnedRaycastTarget"]
        FLIGHT["Flight APIs\nGetVelocity(), GetAltitude(),\nGetCockpitMatrix()"]
    end

    subgraph CDM ["CustomDataManager"]
        CACHE["Dictionary&lt;string,string&gt; cache\nLazy parse, dirty flag"]
    end

    subgraph SM ["SoundManager"]
        WARN["Warning Channel\n(altitude, RWR)"]
        WEAP["Weapon Channel\n(AIM9 lock/search)"]
    end

    ENEMIES --> |"GetSelectedEnemy()"| SELECT
    SELECT --> |"UpdateActiveTargetGPS()"| CDM
    CDM --> |"Cached, CachedSpeed"| MISSILES["Missile Scripts"]
```

**Source:** `Jet.cs` (enemy list, selection, flight APIs), `Utilities/CustomDataManager.cs` (cache), `Utilities/SoundManager.cs` (channels)

---

## Exception Handling

```mermaid
flowchart TD
    MAIN["Program.Main()"] --> TRY["try: SystemManager.Main()"]
    TRY --> |NullReferenceException| REINIT["Log + SystemManager.Initialize()\n(auto-recover from missing blocks)"]
    TRY --> |Other Exception| LOG["Log error type\n(no auto-recover — reveals bugs)"]
    TRY --> |Success| DONE["Continue next tick"]
```

**Source:** `Program.cs` — `Main()` method
