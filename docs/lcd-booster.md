# LCD Booster — Server & Client Plugins for High-Refresh LCD Rendering

JetOS renders a complex HUD with ~100+ sprites per frame at Update1 (60 ticks/sec). Vanilla Space Engineers throttles LCD updates to ~6 renders/sec on the client and batches network sends every 10 ticks on the server. The LcdBooster plugins remove these bottlenecks so the HUD runs at the full PB update rate.

## Problem

Vanilla SE has three throttles that limit LCD refresh rate:

1. **Server send throttle**: `MyTextPanelComponent.SendSpriteQueue()` is only called from `UpdateAfterSimulation10` — sprites are batched and sent to clients every 10 ticks (6 sends/sec max).
2. **Client render throttle**: `UpdateSpritesTexture()` also runs in `UpdateAfterSimulation10` — even if sprite data arrives faster, the client only renders 6 times/sec.
3. **State sync priority**: LCD property sync groups use default priority, meaning state sync updates compete with all other entity state for limited per-tick packet slots (7 packets/client/tick).
4. **Streaming bottleneck**: Entity streaming (grids, voxels) uses a serial ACK-gated pipeline — only one streaming packet in-flight per entity at a time, causing the "Streaming" HUD indicator.

The result: PB sprites sent at 60/sec are bottlenecked to ~6 visible updates/sec on the client.

## Solution

Two companion plugins work together:

| Plugin | Runtime | Framework | Loader | Purpose |
|--------|---------|-----------|--------|---------|
| **LcdBooster** (server) | Torch dedicated server | .NET Framework 4.8 | Torch plugin | Remove server-side send throttles |
| **LcdBoosterClient** (client) | SE game client | .NET 9.0+ | Pulsar (Legacy) | Remove client-side render throttle |

## Architecture

### Server Plugin — LcdBooster

**Location**: `G:\SteamLibrary\steamapps\common\SpaceEngineers\LcdBooster\`

Harmony patches applied on the Torch dedicated server:

#### 1. ImmediateSpriteSendPatch
**Target**: `MyTextPanelComponent.DispatchSprites` (Postfix)

Vanilla: sprites are queued in `m_spriteQueue` and only sent when `UpdateAfterSimulation10` calls `SendSpriteQueue()` (6/sec).

Patch: calls `SendSpriteQueue()` immediately after `DispatchSprites()`, so sprites are sent the same tick the PB draws them. Also implements **keyframe support** — every 600 ticks (10 seconds), clears `m_lastSpriteQueue` to force a full (non-delta) sprite send, ensuring late-joining clients or missed packets recover. Includes periodic cleanup of dead panel references (every 3600 ticks / 60s) to prevent memory leaks.

```
PB DrawFrame.Dispose()
  -> DispatchSprites()         [vanilla: queues sprites]
  -> ImmediateSpriteSendPatch  [patch: sends immediately]
       -> clears m_lastSpriteQueue every 10s (keyframe)
       -> SendSpriteQueue()
            -> GetDelta() -> RaiseEvent -> clients
```

#### 2. IsHighPriorityPatch
**Target**: `MyPropertySyncStateGroup.IsHighPriority` getter (Postfix)

Makes LCD-related state groups (`MyMultiTextPanelComponent`, `MyLcdSurfaceComponent`) return `IsHighPriority = true`. This causes `ScheduleStateGroupSync` to use `SendInterval / 16` instead of the full `SendInterval`, dramatically increasing state sync frequency for LCD properties (content type, font, script selection, etc.).

#### 3. TripleStateSyncPacketsPatch
**Target**: `MyReplicationServer.FilterStateSync` (Transpiler)

Replaces the hardcoded `int num2 = 7` (max state sync packets per client per tick) with `21`. This triples the available bandwidth for state sync, giving LCD updates more slots to flow through.

#### 4. StreamingPipelinePatch
**Target**: `MyReplicationServer.SendStreamingEntry` (Prefix + Postfix)

Vanilla: `MyStreamingEntityStateGroup.ProcessWrite` checks `LastSent.HasValue` — if the previous streaming packet hasn't been ACKed yet, it sends a no-op. This makes entity streaming serial: only one part in-flight per round-trip, causing the "Streaming" HUD indicator to persist.

Patch:
- **Prefix**: Clears `LastSent` (sets to `null`) before each `SendStreamingEntry`, so `ProcessWrite` always sends the next data part without waiting for ACK.
- **Postfix**: When `RemainingBits == 0` and `!Incomplete`, marks the group as `Dirty = false` and `ForceSend = false` to prevent stale re-sends.

Uses lazy cached reflection with `_reflectionFailed` fail-fast flag — if any reflection target cannot be resolved, the patch silently disables itself rather than throwing on every tick. Resolves `StreamClientData` via `m_clientStreamData` dictionary keyed by `Client.State.EndpointId`.

#### 5. CallSite Distance Radius Patch
**Target**: `OnUpdateSpriteCollection` CallSite (runtime reflection)

Increases the `[DistanceRadius(32f)]` attribute on `OnUpdateSpriteCollection` from 32m to 64m. This doubles the range at which clients receive sprite update events.

Applied via reflection in `LcdBoosterPlugin.Update()` once the replication layer is available:
```
MyTypeTable -> MySynchronizedTypeInfo -> MyEventTable -> CallSite
  -> DistanceRadiusSquared: 1024 (32m) -> 4096 (64m)
```

### Client Plugin — LcdBoosterClient

**Location**: `G:\SteamLibrary\steamapps\common\SpaceEngineers\LcdBoosterClient\`

Single Harmony patch applied via Pulsar plugin loader:

#### ImmediateClientRenderPatch
**Target**: `MyTextPanelComponent.UpdateSpriteCollection` (Postfix)

Vanilla: when the client receives sprite data, it stores it in `m_externalSprites` and updates `m_renderLayers`, but the actual texture render (`UpdateSpritesTexture`) only happens in `UpdateAfterSimulation10` (6/sec).

Patch: immediately calls `EnsureGeneratedTexture()` + `UpdateSpritesTexture()` when sprite data arrives, for panels with `ContentType.SCRIPT`. Uses **distance-based LOD** for performance:

| Distance | Refresh Rate | Behavior |
|----------|-------------|----------|
| 0–5m | 60fps | Immediate render every update |
| 5–12m | 30fps | Render every 2nd update |
| 12m+ | ~6fps | Vanilla behavior (no immediate render) |

Distance is calculated using squared distances (`Vector3D.DistanceSquared`) from `MySector.MainCamera.Position` to `MyTextPanelComponent.WorldPosition` — no sqrt overhead.

Includes periodic cleanup (every 3600 ticks / 60s) of `ConcurrentDictionary` entries for destroyed panels via `m_block.MarkedForClose` check.

```
Server RaiseEvent
  -> Client OnUpdateSpriteCollection
       -> UpdateSpriteCollection  [stores sprites in m_renderLayers]
       -> ImmediateClientRenderPatch  [renders texture based on distance]
            -> distSq <= 25: EnsureGeneratedTexture() + UpdateSpritesTexture() every tick
            -> distSq <= 144: same, but skip every other tick
            -> distSq > 144: no-op (vanilla 6fps)
```

## SE LCD Rendering Pipeline (Decompiled)

### Server Side (Sandbox.Game.dll)

```
PB Script: surface.DrawFrame() -> frame.Add(sprite) x N -> frame.Dispose()
  |
  v
MySpriteDrawFrame.Dispose()
  -> DispatchSprites(drawFrame)
       -> m_renderLayers = drawFrame sprites
       -> m_spriteQueue = drawFrame.ToCollection()
       -> m_areSpritesDirty = true
  |
  v [vanilla: waits for UpdateAfterSimulation10]
  v [patched: immediate]
SendSpriteQueue()
  -> GetDelta(m_lastSpriteQueue, m_spriteQueue)
       -> compares current vs last frame
       -> only sends changed sprites (MySerializableSprite with Index)
       -> returns MySerializableSpriteCollection { Sprites[], Length }
  -> m_spriteCollectionUpdate.Invoke(panel, delta)
       -> MyMultiTextPanelComponent.SpriteCollectionUpdate
            -> SendUpdateSpriteCollection(panelIndex, sprites)
                 -> MyMultiplayer.RaiseEvent(OnUpdateSpriteCollection)
                      [DistanceRadius(32f) -> patched to 64f]
                      [Reliable, Broadcast]
```

### Client Side (Sandbox.Game.dll)

```
Network Event arrives
  -> MyMultiTextPanelComponent.OnUpdateSpriteCollection(panelIndex, sprites)
       -> m_panels[panelIndex].UpdateSpriteCollection(sprites)
            -> m_externalSprites = sprites
            -> m_externalSprites_ValueChanged()
                 -> resize m_renderLayers to sprites.Length
                 -> place each sprite at m_renderLayers[sprite.Index]
  |
  v [vanilla: waits for UpdateAfterSimulation10]
  v [patched: immediate with distance LOD]
UpdateSpritesTexture()
  -> compares m_renderLayers with m_lastRenderLayers
  -> Render.RenderSpritesToTexture(area, m_renderLayers, ...)
       -> iterates all sprites, calls MyRenderProxy draw commands
```

### Delta Mechanism

The server uses delta compression — `GetDelta()` only sends sprites that changed since the last frame. The client reconstructs the full frame by keeping `m_renderLayers` persistent and updating individual indices.

**Keyframe recovery**: Every 10 seconds, the server clears `m_lastSpriteQueue` so `GetDelta` treats the next frame as fully new, sending all sprites. This ensures:
- Late-joining clients get a complete frame within 10 seconds
- Any missed or corrupted deltas self-heal

### Sprite Serialization

Sprites are serialized via VRage's BitStream serialization (not protobuf for network events):

- Array length: varint (no count limit)
- Each `MySerializableSprite`: Type, Position?, Size?, Color?, Data (string), FontId (string), Alignment, RotationOrScale, Index
- Transport: Steam P2P reliable messages (fragmented, up to ~1MB per message)
- `MAX_SPRITE_COLLECTION_BYTE_SIZE = 9504` exists in `MyTextPanelComponent` but is **dead code** — never referenced by any method

## Key Types

| Type | Assembly | Role |
|------|----------|------|
| `MyTextPanelComponent` | Sandbox.Game | Core LCD component — sprite queue, delta, send, render |
| `MyMultiTextPanelComponent` | Sandbox.Game | Multi-surface wrapper — routes events to panel components |
| `MySpriteDrawFrame` | VRage.Game | PB-facing frame builder — `Add()`, `Dispose()` triggers send |
| `MySpriteCollection` | VRage.Game | In-memory sprite array (MySprite[]) |
| `MySerializableSpriteCollection` | VRage.Game | Network-serializable delta (MySerializableSprite[] + Length) |
| `MySerializableSprite` | VRage.Game | Serializable sprite with Index field for delta placement |
| `MyPropertySyncStateGroup` | Sandbox.Game | Sync group for Sync<T> properties (font, content type, etc.) |
| `MyReplicationServer` | VRage.Network | Server-side replication — FilterStateSync, event dispatch |
| `MyStreamingEntityStateGroup` | Sandbox.Game | Streaming replication for grids/voxels — ProcessWrite, OnAck |
| `MySessionComponentPanels` | Sandbox.Game | IsInRange check — distance/quality/memory budget for rendering |

## File Locations

### Server Plugin (Torch)
```
G:\SteamLibrary\steamapps\common\SpaceEngineers\LcdBooster\
  LcdBooster.csproj          net48, references Torch + game DLLs
  LcdBoosterPlugin.cs        TorchPluginBase — Init, Update (CallSite patch), Dispose
  Patches.cs                 Harmony patches: IsHighPriority, ImmediateSpriteSend,
                             TripleStateSync, StreamingPipeline
  TorchBinaries/             Torch.dll, Torch.API.dll, NLog.dll (local copies)
  bin/Release/net48/         Build output -> deploy to Torch Plugins/ folder
```

### Client Plugin (Pulsar)
```
G:\SteamLibrary\steamapps\common\SpaceEngineers\LcdBoosterClient\
  LcdBoosterClient.csproj    net9.0 (required for Pulsar NETCoreApp runtime check)
  LcdBoosterClientPlugin.cs  IPlugin — Init (Harmony.PatchAll), Update, Dispose
  ClientPatches.cs            Harmony patch: ImmediateClientRender with distance LOD
  bin/Release/net9.0/         Build output -> deploy to %APPDATA%/Pulsar/Legacy/Local/
```

### Deployment
- **Server**: Copy `LcdBooster.dll` to Torch's `Plugins/` directory
- **Client**: Copy `LcdBoosterClient.dll` to `%APPDATA%/Pulsar/Legacy/Local/`
- **Client profile**: Enable in `%APPDATA%/Pulsar/Legacy/Profiles/Current.xml` under `<Local><string>LcdBoosterClient.dll</string></Local>`

## Building

```bash
# Server plugin (Torch, .NET Framework 4.8)
cd G:/SteamLibrary/steamapps/common/SpaceEngineers/LcdBooster
dotnet build -c Release

# Client plugin (Pulsar, .NET 9.0)
cd G:/SteamLibrary/steamapps/common/SpaceEngineers/LcdBoosterClient
dotnet build -c Release
```

## Relevance to JetOS

JetOS renders to 3 surfaces every tick at Update1:
- **Fighter HUD** (`hud`): artificial horizon, pitch ladder, speed/altitude tapes, compass, G-force, AoA indexer, flight path marker, radar minimap, lead pip, target boxes, lock indicators (~80-150 sprites)
- **JetOS surface 0** (`mainScreen`): OS menu or custom module rendering
- **JetOS surface 2** (`weaponScreen`): weapon status, enemy list, missile TOF, gun turret status

Without LcdBooster, the HUD only updates 6 times/sec on remote clients despite the PB computing new frames 60 times/sec. With LcdBooster:
- Server sends sprite data every tick (60/sec) instead of every 10 ticks (6/sec)
- Client renders received sprite data immediately with distance-based LOD (60fps/30fps/6fps)
- LCD state sync gets 3x more packet slots (21 vs 7)
- Sprite events reach clients up to 64m away (vs 32m vanilla)
- Full sprite keyframe every 10 seconds recovers missed data
- Entity streaming pipeline is unblocked (parallel streaming vs serial ACK-gated)

This makes the HUD, weapon screen, and OS display feel responsive on multiplayer servers.

## Gotchas

- **Client plugin MUST target net9.0+**: SE runs on .NET 10. Pulsar's `IsSupportedRuntime()` checks for `NETCoreApp` in the assembly runtime string. A `net48` DLL has `NETFramework` and is silently rejected.
- **Pulsar loads DLLs from `%APPDATA%/Pulsar/Legacy/Local/`**: Not from the game directory.
- **Harmony `Private="False"`**: All game DLL references must use `Private="False"` to avoid copying them into the build output.
- **`EnableDynamicLoading`**: Required in the client csproj so Pulsar can load it as a plugin.
- **Delta vs keyframe**: The vanilla delta mechanism only sends changed sprites. If a client misses the initial full frame, it shows an incomplete display until the next keyframe (10s with our patch, never without it).
- **`m_spritesToSend` is static**: Shared across all `MyTextPanelComponent` instances. Single-threaded game loop prevents corruption, but be aware if ever patching threading.
- **Harmony cannot patch closed generic types on .NET 4.8**: Attempting to patch methods on types constructed via `MakeGenericType` (e.g. `MyStreamingEntityStateGroup<MyCubeGridReplicable>`) causes "The given generic instantiation was invalid". Work around by patching the caller (`SendStreamingEntry`) instead.
- **Reflection fail-fast pattern**: All reflection-heavy patches use a `_reflectionFailed` flag. If any target cannot be resolved, the patch silently disables itself rather than throwing on every tick.
