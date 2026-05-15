# RadarFeed v2 Datalink Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fragile multi-AI radar pool with RadarFeed v2, stable entity-id target fusion, map-only neutral/unknown contacts, bounded datalink relay, one fallback/STT combo, and kinematic RWR warnings.

**Architecture:** The Torch/server plugin owns full multi-target scanning and publishes compact `JetOSRadarFeed` v2 records. The programmable block splits hostile contacts into `enemyList` and neutral/unknown contacts into a new map-only store, then shares authored/relayed observations through a bounded datalink. One onboard AI Combat + Flight pair remains for non-plugin fallback and plugin-assisted selected-target STT.

**Tech Stack:** Space Engineers programmable block C# 6 via MDK2, Torch plugin C#/.NET Framework 4.8, shared `JetOSRadarFeed` plugin helper library, console helper tests in `Plugins/JetOSRadarFeed.Tests`.

---

## File Structure

- Create `Mdk.PbScript2/Utilities/RadarContactV2.cs`: shared PB-side lightweight structs and constants for contact kind/source/age.
- Create `Mdk.PbScript2/Utilities/MapContactStoreV2.cs`: map-only neutral/unknown contact storage with 30-second decay.
- Create `Mdk.PbScript2/Utilities/DatalinkV2.cs`: v2 datalink packet send/receive/relay logic.
- Create `Mdk.PbScript2/Modules/RadarControlModuleV2.cs`: one onboard combo, plugin feed parsing, selected hostile STT request publishing, and RWR v2.
- Modify `Mdk.PbScript2/Jet.cs`: make target identity entity-id only for v2 contacts and expose selected target id/source helpers.
- Modify `Mdk.PbScript2/SystemManager.cs`: tick `DatalinkV2`, instantiate `RadarControlModuleV2`, and route background ticking to v2.
- Modify terrain/radar renderers only where they need map-only contacts.
- Modify `Plugins/JetOSRadarFeed/RadarFeedEngine.cs`: replace slot-assignment scanner with v2 full construct scan and selected-STT request support.
- Modify `Plugins/JetOSRadarFeed.Tests/Program.cs`: add helper tests for v2 protocol, entity-id identity, relation routing, caps, and datalink relay helpers.

---

### Task 1: Plugin v2 Protocol Tests And Pure Helpers

**Files:**
- Modify: `Plugins/JetOSRadarFeed.Tests/Program.cs`
- Modify: `Plugins/JetOSRadarFeed/RadarFeedEngine.cs`

- [ ] **Step 1: Add failing tests for protocol constants**

Add assertions:

```csharp
Equal(3, RadarFeedEngine.FeedVersionForTest(), "feed protocol version");
Equal("JetOSRadarFeed", RadarFeedEngine.PropertyNameForTest(), "terminal property name");
Equal('H', RadarFeedEngine.ContactKindForRelationForTest(MyRelationsBetweenPlayerAndBlock.Enemies), "enemy relation kind");
Equal('N', RadarFeedEngine.ContactKindForRelationForTest(MyRelationsBetweenPlayerAndBlock.Neutral), "neutral relation kind");
Equal('U', RadarFeedEngine.ContactKindForRelationForTest(MyRelationsBetweenPlayerAndBlock.NoOwnership), "unknown relation kind");
```

- [ ] **Step 2: Run test and verify red**

Run:

```powershell
dotnet test Plugins/JetOSRadarFeed.Tests/JetOSRadarFeed.Tests.csproj
```

Expected: fail because v2 helper names/version do not exist yet.

- [ ] **Step 3: Implement v2 helper surface**

In `RadarFeedEngine.cs`, add constants `FeedVersion = 3`, `KindHostile = 'H'`, `KindNeutral = 'N'`, `KindUnknown = 'U'`, and public test helpers.

- [ ] **Step 4: Run test and verify green**

Run:

```powershell
dotnet test Plugins/JetOSRadarFeed.Tests/JetOSRadarFeed.Tests.csproj
```

Expected: pass.

---

### Task 2: Plugin Full-Scan Feed

**Files:**
- Modify: `Plugins/JetOSRadarFeed/RadarFeedEngine.cs`
- Modify: `Plugins/JetOSRadarFeed.Tests/Program.cs`

- [ ] **Step 1: Add failing pure tests for cap and format helpers**

Add helper tests for:

```csharp
Equal(true, RadarFeedEngine.ShouldAppendContactForTest('H', 31, 0), "hostile under cap");
Equal(false, RadarFeedEngine.ShouldAppendContactForTest('H', 32, 0), "hostile cap");
Equal(true, RadarFeedEngine.ShouldAppendContactForTest('N', 0, 31), "map under cap");
Equal(false, RadarFeedEngine.ShouldAppendContactForTest('U', 0, 32), "map cap");
Equal("R|H|42|1|2|3|4|5|6|Target", RadarFeedEngine.FormatContactLineForTest('H', 42, "Target", 1, 2, 3, 4, 5, 6), "v2 contact line");
```

- [ ] **Step 2: Run test and verify red**

Run `dotnet test Plugins/JetOSRadarFeed.Tests/JetOSRadarFeed.Tests.csproj`.

- [ ] **Step 3: Implement helper and scanner behavior**

Replace per-radar assignment with construct scan:

- Discover one eligible radar source per construct: tagged `[JO]` first, first eligible combat block fallback.
- Use the AI Combat block definition search radius.
- Query top-most dynamic entities.
- Keep only `MyCubeGrid` contacts that are not the source construct and have nonzero top-grid `EntityId`.
- Collapse physical constructs to one top entity.
- Route relation: `H` hostiles, `N` neutrals, `U` unknown/no owner.
- Skip friendlies entirely.
- Sort by distance from radar.
- Emit max 32 hostile and 32 map-only records.
- Format header `JORAD|3|<sequence>` and rows `R|<kind>|<entityId>|<px>|<py>|<pz>|<vx>|<vy>|<vz>|<name>`.

- [ ] **Step 4: Run tests**

Run `dotnet test Plugins/JetOSRadarFeed.Tests/JetOSRadarFeed.Tests.csproj`.

- [ ] **Step 5: Build Torch plugin**

Run:

```powershell
dotnet build Plugins/JetOSRadarFeedTorch/JetOSRadarFeedTorch.csproj --configuration Release
```

Expected: build succeeds.

---

### Task 3: PB v2 Contact Stores

**Files:**
- Create: `Mdk.PbScript2/Utilities/RadarContactV2.cs`
- Create: `Mdk.PbScript2/Utilities/MapContactStoreV2.cs`
- Modify: `Mdk.PbScript2/Jet.cs`

- [ ] **Step 1: Add v2 structs and constants**

Create `RadarContactV2.cs` with:

```csharp
const int SRC_DATALINK = -1;
const int SRC_RADARFEED_V2 = 100;
const int SRC_ONBOARD_STT = 0;
const char KIND_HOSTILE = 'H';
const char KIND_NEUTRAL = 'N';
const char KIND_UNKNOWN = 'U';
```

Add a compact `MapContact` struct with `Id`, `Kind`, `Position`, `Velocity`, `Name`, `LastSeen`, `ObserverId`, `HopCount`.

- [ ] **Step 2: Add map-only store**

Create `MapContactStoreV2` with `Update(...)`, `Decay()`, and `GetActive()` methods. Merge only by nonzero `Id`; ignore zero ids.

- [ ] **Step 3: Update hostile identity**

In `Jet.UpdateOrAddEnemy`, preserve legacy fallback for existing non-v2 calls, but ensure v2 callers pass nonzero `entityId` and never need name/proximity fallback. Add `GetSelectedEnemyId()` and `IsSelectedEntity(long id)`.

- [ ] **Step 4: Build PB**

Run:

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT
```

Expected: build succeeds.

---

### Task 4: PB RadarControlModuleV2

**Files:**
- Create: `Mdk.PbScript2/Modules/RadarControlModuleV2.cs`
- Modify: `Mdk.PbScript2/SystemManager.cs`

- [ ] **Step 1: Create one-combo radar module**

`RadarControlModuleV2` discovers one `AI Flight [JO]`/`AI Combat [JO]` pair, falling back to untagged names. It activates only the combat block behavior using the existing proven sequence. It updates one `RadarTrackingModule`.

- [ ] **Step 2: Add v2 feed parser**

Parse only `JORAD|3|` rows:

```text
R|kind|entityId|px|py|pz|vx|vy|vz|name
```

Reject malformed rows and zero ids. Route `H` to `myJet.UpdateOrAddEnemy(...)`; route `N/U` to `MapContactStoreV2.Update(...)`.

- [ ] **Step 3: Compute STT lock quality**

`IsTrackLocked` is true only when the onboard combo's top-grid target id matches `myJet.GetSelectedEnemyId()`. Do not change pilot selection when the combo sees a different hostile.

- [ ] **Step 4: Wire SystemManager to v2**

Instantiate and tick `RadarControlModuleV2` instead of the old `RadarControlModule`. Keep old file compiled but unused for this cutover.

- [ ] **Step 5: Build PB**

Run `dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT`.

---

### Task 5: Datalink v2

**Files:**
- Create: `Mdk.PbScript2/Utilities/DatalinkV2.cs`
- Modify: `Mdk.PbScript2/SystemManager.cs`
- Modify: `Mdk.PbScript2/Utilities/Datalink.cs` to stop old hostile sharing once `DatalinkV2` is wired; preserve or move friendly ownship cache behavior.

- [ ] **Step 1: Implement packet shape**

Use compact tuple payloads carrying `kind`, `observerId`, `senderId`, `targetEntityId`, `position`, `velocity`, `ageSeconds`, `hopCount`, and `name`.

- [ ] **Step 2: Author local observations**

Broadcast local physical hostile observations and map-only observations only when changed, rate-limited to 0.2 seconds per contact, with 5-second keyframes.

- [ ] **Step 3: Relay remote observations**

Relay new/changed remote observations only when `hopCount < 3`, preserving `observerId` and `ageSeconds`.

- [ ] **Step 4: Receive and route**

Reject own-origin packets, zero target ids, expired observations, and malformed payloads. Route hostiles to `enemyList`, map-only contacts to `MapContactStoreV2`.

- [ ] **Step 5: Build PB**

Run `dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT`.

---

### Task 6: RWR v2 And Render Integration

**Files:**
- Modify: `Mdk.PbScript2/Modules/RadarControlModuleV2.cs`
- Modify: `Mdk.PbScript2/HUD/RadarRenderer.cs`
- Modify: `Mdk.PbScript2/Modules/TerrainModule.cs`

- [ ] **Step 1: Implement kinematic warning**

Evaluate hostile tracks from local/remote target list. Trigger RWR warning when closing speed is at least 250 m/s, trajectory has near pass, and aspect points generally toward ownship.

- [ ] **Step 2: Keep warning non-selecting**

Do not change selected target or STT request state from RWR.

- [ ] **Step 3: Render map-only contacts**

Add neutral/unknown contacts to terrain/radar map views only. Do not add them to weapon list, target cycling, guns, or missile cache.

- [ ] **Step 4: Build PB**

Run `dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT`.

---

### Task 7: Verification, Size, And Package

**Files:**
- Verify only unless package output paths change.

- [ ] **Step 1: Run plugin tests**

Run:

```powershell
dotnet test Plugins/JetOSRadarFeed.Tests/JetOSRadarFeed.Tests.csproj
```

- [ ] **Step 2: Run PB build**

Run:

```powershell
dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT
```

- [ ] **Step 3: Check packed script size**

Run:

```powershell
(Get-Content -Path "$env:APPDATA\SpaceEngineers\IngameScripts\local\Mdk.PbScript2\script.cs" -Raw).Length
```

- [ ] **Step 4: Build Torch plugin**

Run:

```powershell
dotnet build Plugins/JetOSRadarFeedTorch/JetOSRadarFeedTorch.csproj --configuration Release
```

- [ ] **Step 5: Commit**

Stage only files touched for this feature and commit:

```powershell
git add Plugins/JetOSRadarFeed Plugins/JetOSRadarFeed.Tests Mdk.PbScript2 docs/superpowers/plans/2026-05-15-radarfeed-v2-datalink-rebuild.md
git commit -m "feat: rebuild radarfeed v2 datalink"
```
