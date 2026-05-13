# Unified Target Datalink Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace standalone friendly telemetry with one compact datalink that broadcasts friendly ownship status and locally observed hostile contacts, and treats received hostile contacts as full JetOS targets.

**Architecture:** Add `Datalink` as a focused static utility under `Utilities`. It owns one IGC listener, ownship broadcasting, local-contact broadcasting, remote target ingestion into `Jet.enemyList`, and a small friendly cache for terrain display. `Jet.enemyList` remains the target fusion table, with `SourceIndex == -1` reserved for remote datalink contacts.

**Tech Stack:** Space Engineers programmable block C# 6, MDK2, IGC broadcast messages, `VRage.MyTuple`, existing `Jet.EnemyContact`, existing terrain/HUD renderers.

---

## File Structure

- Delete: `Mdk.PbScript2/Utilities/FriendlyJetTelemetry.cs`
- Create: `Mdk.PbScript2/Utilities/Datalink.cs`
- Modify: `Mdk.PbScript2/SystemManager.cs`
- Modify: `Mdk.PbScript2/Jet.cs`
- Modify: `Mdk.PbScript2/Modules/TerrainModule.cs`
- Modify: `Mdk.PbScript2/HUD/WeaponScreenRenderer.cs`
- Verify: `dotnet build Mdk.PbScript2.sln --configuration Release`

`Datalink.cs` replaces friendly telemetry and owns all network-specific state. `Jet.cs` remains the only hostile target store and gains the small remote/local authority rule. Renderers only change enough to read friendlies from `Datalink` and label remote target source as `DL`.

---

### Task 1: Replace Friendly Telemetry With Datalink Utility

**Files:**
- Delete: `Mdk.PbScript2/Utilities/FriendlyJetTelemetry.cs`
- Create: `Mdk.PbScript2/Utilities/Datalink.cs`

- [ ] **Step 1: Remove the old friendly telemetry file**

Delete `Mdk.PbScript2/Utilities/FriendlyJetTelemetry.cs`.

- [ ] **Step 2: Create `Datalink.cs`**

Add `Mdk.PbScript2/Utilities/Datalink.cs` with:

```csharp
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class Datalink
        {
            public const string IGC_CHANNEL = "JETOS_DL";
            public const int SOURCE_INDEX = -1;
            const int KIND_FRIEND = 0;
            const int KIND_TARGET = 1;
            const double BROADCAST_INTERVAL = 0.2;
            const double FRIEND_TIMEOUT = 2.0;
            const double MAX_TARGET_AGE = 3.0;

            static IMyBroadcastListener _listener;
            static readonly List<FriendlyStatus> _friends = new List<FriendlyStatus>();
            static double _broadcastAccum = BROADCAST_INTERVAL;

            public struct FriendlyStatus
            {
                public long Id;
                public Vector3D Position;
                public Vector3D Velocity;
                public double SeenAt;
            }

            public static void Tick(Program program, Jet jet)
            {
                if (program == null || jet == null) return;
                Poll(program, jet);
                Broadcast(program, jet);
                PruneFriends();
            }

            static void Broadcast(Program program, Jet jet)
            {
                if (jet._cockpit == null) return;
                _broadcastAccum += SystemManager.DeltaSeconds;
                if (_broadcastAccum < BROADCAST_INTERVAL) return;
                _broadcastAccum = 0;

                program.IGC.SendBroadcastMessage(IGC_CHANNEL,
                    MyTuple.Create(KIND_FRIEND, program.Me.EntityId, jet.CockpitPosition, jet.CockpitVelocity));

                for (int i = 0; i < jet.enemyList.Count; i++)
                {
                    var c = jet.enemyList[i];
                    if (c.SourceIndex < 0 || c.AgeSeconds > MAX_TARGET_AGE) continue;
                    program.IGC.SendBroadcastMessage(IGC_CHANNEL,
                        MyTuple.Create(KIND_TARGET, program.Me.EntityId, c.EntityId, c.Position, c.Velocity, c.AgeSeconds));
                }
            }

            static void Poll(Program program, Jet jet)
            {
                if (_listener == null)
                    _listener = program.IGC.RegisterBroadcastListener(IGC_CHANNEL);

                while (_listener.HasPendingMessage)
                {
                    MyIGCMessage msg = _listener.AcceptMessage();
                    if (msg.Data is MyTuple<int, long, Vector3D, Vector3D>)
                    {
                        var t = (MyTuple<int, long, Vector3D, Vector3D>)msg.Data;
                        if (t.Item1 != KIND_FRIEND || t.Item2 == program.Me.EntityId) continue;
                        UpsertFriend(new FriendlyStatus
                        {
                            Id = t.Item2,
                            Position = t.Item3,
                            Velocity = t.Item4,
                            SeenAt = SystemManager.ElapsedSeconds
                        });
                    }
                    else if (msg.Data is MyTuple<int, long, long, Vector3D, Vector3D, double>)
                    {
                        var t = (MyTuple<int, long, long, Vector3D, Vector3D, double>)msg.Data;
                        if (t.Item1 != KIND_TARGET || t.Item2 == program.Me.EntityId) continue;
                        if (t.Item6 > MAX_TARGET_AGE || t.Item4.LengthSquared() < 1.0) continue;
                        jet.UpdateOrAddEnemy(t.Item4, t.Item5, "", SOURCE_INDEX, t.Item3);
                    }
                }
            }

            static void UpsertFriend(FriendlyStatus status)
            {
                for (int i = 0; i < _friends.Count; i++)
                {
                    if (_friends[i].Id == status.Id)
                    {
                        _friends[i] = status;
                        return;
                    }
                }
                _friends.Add(status);
            }

            static void PruneFriends()
            {
                double now = SystemManager.ElapsedSeconds;
                for (int i = _friends.Count - 1; i >= 0; i--)
                    if (now - _friends[i].SeenAt > FRIEND_TIMEOUT)
                        _friends.RemoveAt(i);
            }

            public static List<FriendlyStatus> GetActiveFriendlies()
            {
                PruneFriends();
                return _friends;
            }
        }
    }
}
```

- [ ] **Step 3: Run a build and expect missing references**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build fails because `SystemManager` and `TerrainModule` still reference `FriendlyJetTelemetry`.

- [ ] **Step 4: Commit is deferred**

Do not commit yet. Task 2 updates the call sites and should be committed with this task.

---

### Task 2: Wire Datalink Into The Tick Loop And Terrain Map

**Files:**
- Modify: `Mdk.PbScript2/SystemManager.cs`
- Modify: `Mdk.PbScript2/Modules/TerrainModule.cs`

- [ ] **Step 1: Update the system tick call**

In `Mdk.PbScript2/SystemManager.cs`, replace:

```csharp
FriendlyJetTelemetry.Tick(parentProgram, _myJet);
```

with:

```csharp
Datalink.Tick(parentProgram, _myJet);
```

- [ ] **Step 2: Update terrain friendly source**

In `Mdk.PbScript2/Modules/TerrainModule.cs`, replace:

```csharp
var friends = FriendlyJetTelemetry.GetActiveFriends();
```

with:

```csharp
var friends = Datalink.GetActiveFriendlies();
```

The rest of `DrawFriendlyJets()` continues to use `friend.Id` and `friend.Position`, which are preserved by `Datalink.FriendlyStatus`.

- [ ] **Step 3: Run build and expect success or source-tag-only errors**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build succeeds unless the compiler exposes a `MyTuple` arity issue. If `MyTuple<int, long, long, Vector3D, Vector3D, double>` is unsupported, replace the hostile packet with nested tuple:

```csharp
MyTuple<int, long, MyTuple<long, Vector3D, Vector3D, double>>
```

and adjust `Broadcast()`/`Poll()` accordingly.

- [ ] **Step 4: Commit datalink replacement**

Run:

```bash
git add Mdk.PbScript2/Utilities/FriendlyJetTelemetry.cs Mdk.PbScript2/Utilities/Datalink.cs Mdk.PbScript2/SystemManager.cs Mdk.PbScript2/Modules/TerrainModule.cs
git commit -m "feat: replace friendly telemetry with datalink"
```

---

### Task 3: Add Remote-Source Target Fusion Rules

**Files:**
- Modify: `Mdk.PbScript2/Jet.cs`

- [ ] **Step 1: Add remote/local authority guard**

In `Jet.UpdateOrAddEnemy()`, after acceleration calculation and before creating the new `EnemyContact`, add:

```csharp
if (existingIndex >= 0 && sourceIndex < 0)
{
    var old = enemyList[existingIndex];
    if (old.SourceIndex >= 0 && old.AgeSeconds <= 3.0)
        return;
}
```

This keeps a fresh local radar contact from being downgraded to datalink source or overwritten by remote position/velocity.

- [ ] **Step 2: Preserve existing local promotion behavior**

Do not change the rest of `UpdateOrAddEnemy()`. When local radar later reports the same entity id, the existing method replaces the remote contact with a local source index and recomputes acceleration.

- [ ] **Step 3: Run build**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build succeeds.

- [ ] **Step 4: Commit fusion rule**

Run:

```bash
git add Mdk.PbScript2/Jet.cs
git commit -m "feat: preserve local authority over datalink tracks"
```

---

### Task 4: Show Datalink Source Tag On Weapon Screen

**Files:**
- Modify: `Mdk.PbScript2/HUD/WeaponScreenRenderer.cs`

- [ ] **Step 1: Update selected target source text**

In `DrawSelectedTargetDetail()`, replace:

```csharp
string sourceText = contact.SourceIndex == 0 ? "RDR" : $"RWR{contact.SourceIndex}";
```

with:

```csharp
string sourceText = contact.SourceIndex < 0 ? "DL" : contact.SourceIndex == 0 ? "RDR" : $"RWR{contact.SourceIndex}";
```

- [ ] **Step 2: Run build**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build succeeds.

- [ ] **Step 3: Commit source tag**

Run:

```bash
git add Mdk.PbScript2/HUD/WeaponScreenRenderer.cs
git commit -m "feat: label datalink target source"
```

---

### Task 5: Final Verification And Documentation Check

**Files:**
- Verify only unless build exposes issues.

- [ ] **Step 1: Search for old telemetry references**

Run: `rg -n "FriendlyJetTelemetry|JETOS_JET_STAT" Mdk.PbScript2 docs`

Expected: only historical docs may mention `JETOS_JET_STAT`; no compiled `Mdk.PbScript2` source references remain.

- [ ] **Step 2: Run release build**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build succeeds.

- [ ] **Step 3: Inspect git status**

Run: `git status --short`

Expected: clean worktree, unless build output or unrelated user changes already existed.

- [ ] **Step 4: Record in-game verification notes for the user**

Report that in-game checks still need to cover:

- two JetOS grids draw each other as friendlies on terrain;
- local target from Jet A appears on Jet B as a selectable `DL` target;
- selected remote target writes `Cached` and `CachedSpeed`;
- local acquisition of the same entity id merges instead of duplicating;
- remote-only target falls out when reports stop.
