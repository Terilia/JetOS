# Friendly Jet Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Broadcast each JetOS jet's own id, position, and velocity at 5 Hz, then draw other JetOS jets as blue squares on the Terrain map without adding them to hostile targets.

**Architecture:** Add a focused `FriendlyJetTelemetry` utility that owns the IGC protocol, incoming cache, and pruning. Tick it from `SystemManager.Main()` after the jet cache is refreshed. Have `TerrainModule` read the helper's friendly-only list and draw map markers using the existing Terrain projection helpers.

**Tech Stack:** Space Engineers programmable block C# 6, MDK2, IGC broadcast messages, `MyTuple<long, Vector3D, Vector3D>`, existing `MFDFrame`/`SpriteHelpers` render helpers.

---

### Task 1: Add Friendly Telemetry Utility

**Files:**
- Create: `Mdk.PbScript2/Utilities/FriendlyJetTelemetry.cs`
- Verify: `dotnet build Mdk.PbScript2.sln --configuration Release`

- [ ] **Step 1: Create the telemetry helper**

Create `Mdk.PbScript2/Utilities/FriendlyJetTelemetry.cs` with:

```csharp
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class FriendlyJetTelemetry
        {
            public const string IGC_CHANNEL = "JETOS_JET_STAT";
            const double BROADCAST_INTERVAL = 0.2;
            const double STATUS_TIMEOUT = 2.0;

            static IMyBroadcastListener _listener;
            static readonly List<FriendlyJetStatus> _friends = new List<FriendlyJetStatus>();
            static double _broadcastAccum = BROADCAST_INTERVAL;

            public struct FriendlyJetStatus
            {
                public long Id;
                public Vector3D Position;
                public Vector3D Velocity;
                public double SeenAt;
            }

            public static void Tick(Program program, Jet jet)
            {
                if (program == null || jet == null) return;
                Poll(program);
                Broadcast(program, jet);
                Prune();
            }

            static void Broadcast(Program program, Jet jet)
            {
                if (jet._cockpit == null) return;
                _broadcastAccum += SystemManager.DeltaSeconds;
                if (_broadcastAccum < BROADCAST_INTERVAL) return;
                _broadcastAccum = 0;

                var payload = MyTuple.Create(program.Me.EntityId, jet.CockpitPosition, jet.CockpitVelocity);
                program.IGC.SendBroadcastMessage(IGC_CHANNEL, payload);
            }

            static void Poll(Program program)
            {
                if (_listener == null)
                    _listener = program.IGC.RegisterBroadcastListener(IGC_CHANNEL);

                while (_listener.HasPendingMessage)
                {
                    MyIGCMessage msg = _listener.AcceptMessage();
                    if (!(msg.Data is MyTuple<long, Vector3D, Vector3D>)) continue;
                    var t = (MyTuple<long, Vector3D, Vector3D>)msg.Data;
                    if (t.Item1 == program.Me.EntityId) continue;
                    Upsert(new FriendlyJetStatus
                    {
                        Id = t.Item1,
                        Position = t.Item2,
                        Velocity = t.Item3,
                        SeenAt = SystemManager.ElapsedSeconds
                    });
                }
            }

            static void Upsert(FriendlyJetStatus status)
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

            static void Prune()
            {
                double now = SystemManager.ElapsedSeconds;
                for (int i = _friends.Count - 1; i >= 0; i--)
                    if (now - _friends[i].SeenAt > STATUS_TIMEOUT)
                        _friends.RemoveAt(i);
            }

            public static List<FriendlyJetStatus> GetActiveFriends()
            {
                Prune();
                return _friends;
            }
        }
    }
}
```

- [ ] **Step 2: Run build verification**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build succeeds or only fails on unrelated pre-existing issues. If it fails because `FriendlyJetTelemetry.cs` is malformed, fix the new helper before moving on.

### Task 2: Tick Friendly Telemetry From SystemManager

**Files:**
- Modify: `Mdk.PbScript2/SystemManager.cs`
- Verify: `dotnet build Mdk.PbScript2.sln --configuration Release`

- [ ] **Step 1: Add the telemetry tick after jet cache refresh**

In `SystemManager.Main()`, after:

```csharp
_myJet.UpdateTickCache();
```

add:

```csharp
FriendlyJetTelemetry.Tick(parentProgram, _myJet);
```

- [ ] **Step 2: Run build verification**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build succeeds and `FriendlyJetTelemetry` resolves.

### Task 3: Draw Friendly Jets On Terrain Map

**Files:**
- Modify: `Mdk.PbScript2/Modules/TerrainModule.cs`
- Verify: `dotnet build Mdk.PbScript2.sln --configuration Release`

- [ ] **Step 1: Draw friends after hostile contacts and missiles**

In `DrawMap()`, after:

```csharp
DrawMissiles(frame, cx, ccy, ma, ppm, sp, jF, jR);
```

add:

```csharp
DrawFriendlyJets(frame, cx, ccy, ma, ppm, sp, jF, jR);
```

- [ ] **Step 2: Add blue-square renderer**

Add this method near `DrawMissiles()`:

```csharp
static void DrawFriendlyJets(MySpriteDrawFrame f, float cx, float cy, float ma, float ppm, Vector3D sp, Vector3D jf, Vector3D jr)
{
    var friends = FriendlyJetTelemetry.GetActiveFriends();
    float h = ma / 2f;
    Color blue = Cr(70, 150, 255);
    for (int i = 0; i < friends.Count; i++)
    {
        var friend = friends[i];
        Vector3D to = friend.Position - sp;
        float dx = (float)VD(to, jr) * ppm, dy = -(float)VD(to, jf) * ppm;
        Vector2 p = ClipMap(cx, cy, dx, dy, h - 4f);
        float vx = (float)VD(friend.Velocity, jr) * ppm;
        float vy = -(float)VD(friend.Velocity, jf) * ppm;
        float vl = (float)Math.Sqrt(vx * vx + vy * vy);
        if (vl > 0.1f)
        {
            float tl = Cl(vl * 3f, 5f, 15f);
            Vector2 q = V2(p.X - vx / vl * tl, p.Y - vy / vl * tl);
            AF(f, q, p, 1f, Cr(blue, 0.55f));
        }
        Sq(p.X + 1f, p.Y + 1f, 7f, 7f, Cr(0, 0, 0, 180));
        Sq(p.X, p.Y, 7f, 7f, Cr(blue, 0.85f));
        Sq(p.X, p.Y, 4f, 4f, blue);
    }
}
```

- [ ] **Step 3: Run build verification**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build succeeds and Terrain map renderer compiles with the new helper.

### Task 4: Final Verification

**Files:**
- Review: `Mdk.PbScript2/Utilities/FriendlyJetTelemetry.cs`
- Review: `Mdk.PbScript2/SystemManager.cs`
- Review: `Mdk.PbScript2/Modules/TerrainModule.cs`

- [ ] **Step 1: Verify requirements against the design**

Check:

```text
JETOS_JET_STAT channel exists.
Payload is MyTuple<long, Vector3D, Vector3D>.
Broadcast interval is 0.2 seconds.
Own Me.EntityId messages are ignored.
Friend cache prunes at 2 seconds.
Terrain map draws friends from FriendlyJetTelemetry only.
No code inserts friends into Jet.enemyList.
```

- [ ] **Step 2: Run final build**

Run: `dotnet build Mdk.PbScript2.sln --configuration Release`

Expected: build succeeds.
