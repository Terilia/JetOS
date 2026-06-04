# JetOS Jet-Side Zones — Implementation Plan

## Goal & scope
HQ lets the operator draw named regions (Enemy / NoFly / SAM / CAP / Rally) and broadcasts them over
DataLink V2 (**Tag 3**). Jets already get the zone **names** for free — HQ injects a `#ZONES` block
into the STATION text and the jet's `DL` page renders it verbatim. This plan adds the optional
**plot**: the jet draws each zone as a **named ring** on its top-down terrain map.

- **Read-only consumer.** The jet never authors, edits, or re-broadcasts zones — that's HQ-only.
- **Circle-only.** Every zone is drawn as a named *bounding circle* (center + radius). No polygon
  outlines, no point-in-polygon tests. This is exactly the "named circle" fallback the wire was
  designed around, and it's what keeps the jet cost minimal.

## What the jet receives — the wire format
Same single envelope as everything else (see `docs/datalink-v2.md`):

```
MyTuple< int Tag, long Sender, Vector3D Pos, Vector3D Vel, long Packed,
         MyTuple< long IdB, double Num, int Misc, string Text > >
```

**Tag 3 = ZONE.** HQ sends **one packet per polygon vertex** and round-robins the whole set
continuously (~5 Hz, bandwidth is free). Every packet of a given zone repeats that zone's summary
fields, so a circle-only jet ignores the per-vertex `Pos`/index entirely and just upserts by id:

| Field | Slot | Jet use |
|-------|------|---------|
| Tag | Item1 | `== 3` → dispatch |
| Sender | Item2 | ignore (HQ EntityId) |
| Pos | Item3 | **ignore** (per-vertex; only needed for a full polygon plot) |
| Vel | Item4 | **center** (world position) |
| Packed | Item5 | **zoneId** (upsert / remove key) |
| IdB | Item6.Item1 | ignore (reserved) |
| Num | Item6.Item2 | **radius** (metres) |
| Misc | Item6.Item3 | **packed bits** (below) |
| Text | Item6.Item4 | **name** |

**`Misc` bit layout** (from HQ `DatalinkHQ.SendZonePacket`):
```
[0..5]   vertexIndex   ignore (circles)
[6..11]  vertexCount   0 = TOMBSTONE → delete this zone
[12..14] shape         0 = Polygon, 1 = Circle
[15..18] kind          0 Enemy · 1 NoFly · 2 SAM · 3 CAP · 4 Rally
[19..23] colorIdx      ignore (jet colors by kind)
[24..31] reserved
```

**Jet decode (the whole contract):**
```csharp
long     id     = t.Item5;            // Packed
int      misc   = inner.Item3;
int      vc     = (misc >> 6) & 63;   // 0 ⇒ delete
int      kind   = (misc >> 15) & 15;  // 0..4
Vector3D center = t.Item4;            // Vel
double   radius = inner.Item2;        // Num
string   name   = inner.Item4 ?? "";  // Text
```

**Lifecycle:** HQ retransmits continuously, so the jet just keeps upserting by id. A packet with
`vc == 0` is a tombstone → remove that id. Any zone not heard for `CONTACT_DECAY_SECONDS` (30 s)
decays out — this covers HQ going offline or out of antenna range. No acks, loss-tolerant.

## Components to add (jet)
1. **Dispatch** — `const int TAG_ZONE = 3;` plus one branch in `DatalinkV2.Poll`
   (`Mdk.PbScript2/Utilities/DatalinkV2.cs`, after the STATUS/STATION block, before TAG_CONTACT):
   decode the fields above, `vc==0 ? ZoneStoreV2.Remove(id) : ZoneStoreV2.Update(...)`, `continue`.
2. **`ZoneStoreV2`** (new `Mdk.PbScript2/Utilities/ZoneStoreV2.cs`) — an id-keyed store mirroring the
   existing `MapContactStoreV2` (list + upsert-by-id + wall-clock decay):
   ```csharp
   struct ZoneV2 { public long Id; public Vector3D Center; public double Radius;
                   public int Kind; public string Name; public double SeenAt; }
   static class ZoneStoreV2 {
       static readonly List<ZoneV2> _z = new List<ZoneV2>();
       public static void Update(long id, Vector3D c, double r, int k, string n) { /* upsert; SeenAt = Jet.GameSeconds */ }
       public static void Remove(long id) { /* drop by id */ }
       public static List<ZoneV2> GetActive() { /* decay 30s */ return _z; }
   }
   ```
3. **Render** — `TerrainModule.DrawZones(cx,cy,ma,ppm,sp,jf,jr)` called inside `DrawMap` (under the
   contact blips), reusing the existing projection (`DrawMapContact` math + `ClipMap`):
   ```csharp
   var zs = ZoneStoreV2.GetActive();
   float h = ma / 2f;
   for (int i = 0; i < zs.Count; i++) {
       var z = zs[i];
       Vector3D to = z.Center - sp;
       float dx = (float)VD(to, jr) * ppm, dy = -(float)VD(to, jf) * ppm;
       Vector2 p = ClipMap(cx, cy, dx, dy, h);
       float d = (float)(z.Radius * ppm) * 2f;
       SpriteHelpers.Sp(TEX_RANGE_RING, p.X, p.Y, d, d, ZoneColor(z.Kind));
       MFDFrame.Txt(Clip(z.Name, 9, "ZONE"), p.X + 5f, p.Y, 0.28f, ZoneColor(z.Kind), MFDTheme.AL);
   }
   ```
   plus `ZoneColor(int kind)` → red / amber / magenta / green / gold, and one call site in `DrawMap`.

All projection, the ring sprite (`TEX_RANGE_RING`), clipping (`ClipMap`), and the store pattern
already exist — the new code is a dispatch branch, a small store, and a draw loop.

## Budget-saver variant (only if reclaim is tight)
Instead of a separate store, **piggyback on `MapContactStoreV2`**: add a zone "kind" char, stash the
`radius` in the otherwise-unused `Velocity.X`, and branch in `DrawMapOnlyContacts` to draw a ring
instead of a blip. Saves the separate store (~450 chars) but entangles zones with the contact
decay/dedup path. Use only if we can't free enough room for the clean version.

## Size budget — the gating constraint
- Circle-only plot ≈ **~1,000–1,100 packed chars** (clean separate store) or **~600–700** (piggyback).
- The jet sits at ~99,7xx / 100,000 — only **tens of chars free**. So the real task is **reclaiming
  ~600–1,100 chars first**; easy reclaim is already spent, so expect a **feature trim or a focused
  minify pass**.
- Polygons (true outlines + point-in-polygon) would roughly **double** the cost — not recommended
  jet-side.
- Verify each step with `dotnet build Mdk.PbScript2.sln --configuration Release /p:OS=Windows_NT`.
  The MDK packer prints the char count **only when over budget** (operator checks final size).

## Phases (pause after each)
- **J0 — Reclaim.** Free ~700–1,100 chars and confirm feasibility BEFORE writing any plot code. Gate.
- **J1 — Receive + store.** `TAG_ZONE` dispatch + `ZoneStoreV2`, no render. Build → confirm under budget.
- **J2 — Render.** `DrawZones` + `ZoneColor` on the terrain map. In-game: rings + names appear, decay
  removes stale, tombstone removes deleted.
- **J3 (optional, likely never).** Point-in-polygon "ENTERING <ZONE>" caution via `SoundManager`;
  full polygon outlines. Budget-permitting only.

## Out of scope (jet)
- Authoring / editing / broadcasting zones (HQ only).
- Polygon outlines and point-in-polygon membership tests (future; ~2× cost).
- Any zone-driven behavior change (no autopilot/weapon coupling).

## Risks
- **Budget** (primary) — may require cutting a jet feature to fit.
- Sprite count on the terrain map — cap zone count + cull off-screen rings.
- Ring scale at extreme zoom — clamp min/max pixel radius so tiny/huge rings stay sane.
- Name overlap — clip to ~9 chars (as contacts do) and offset the label.
