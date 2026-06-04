# DataLink V2 — Technical Detail Report

The shared radio "language" JetOS jets (and the HQ station) speak over Space Engineers'
Inter-Grid Communication (IGC). This document is the authoritative reference for the wire
format, what it can express, and — just as important — what it **cannot**.

Source of truth: `Mdk.PbScript2/Utilities/DatalinkV2.cs` (jet side),
`Mdk.PbScript2/Utilities/Datalink.cs` (`Node` record), `HQ/JetOS_HQ.cs` (station side).

---

## 1. Transport

- **Channel:** a single IGC broadcast tag, `"JETOS_DL"`. Everyone who shares it hears each
  other; it is effectively the squadron "frequency."
- **Reach:** IGC broadcast range == the sending grid's **antenna** range. No antenna → no reach.
- **Delivery:** unreliable/best-effort, not ordered, no acknowledgement. The protocol is built
  to tolerate loss (everything is re-sent on an interval; nothing is a one-shot command).
- **A sender never receives its own broadcast back** — but every handler still drops messages
  where `Sender == Me.EntityId` defensively.

## 2. The envelope (one shape for everything)

Every message is the **same** concrete nested tuple. The receiver does one type-check + one
cast, then dispatches on `Tag`:

```
MyTuple< int Tag, long Sender, Vector3D Pos, Vector3D Vel, long Packed,
         MyTuple< long IdB, double Num, int Misc, string Text > >
```

| Slot | Type | Name | Generic meaning |
|------|------|------|-----------------|
| Item1 | int | Tag | message kind (see §3) |
| Item2 | long | Sender | EntityId of the transmitting PB |
| Item3 | Vector3D | Pos | a world position |
| Item4 | Vector3D | Vel | a world velocity |
| Item5 | long | Packed | tag-specific 64-bit payload |
| Item6.Item1 | long | IdB | tag-specific id |
| Item6.Item2 | double | Num | tag-specific scalar |
| Item6.Item3 | int | Misc | tag-specific small int / bitfield |
| Item6.Item4 | string | Text | tag-specific free string |

There is **no separate string protocol** — the old `J2|...` pipe-format was removed. Typed
tuples are smaller on the wire (binary, not text), need zero parsing, and are smaller in code.

## 3. Tag dispatch

| Tag | Name | Direction | Purpose |
|-----|------|-----------|---------|
| 0 | STATUS | jet → all | per-jet status ping (~5 Hz) |
| 1 | CONTACT | any → all | a tracked enemy/neutral/unknown contact, relayable |
| 2 | STATION | HQ → all | HQ position + orders/news + pre-formatted screen text (~1 Hz) |

Unknown tags are ignored. **Adding a new tag does not break old receivers** (they fall through
the dispatch) — see §8.

### 3.1 STATUS (Tag 0)

| Field | Holds |
|-------|-------|
| Sender | jet EntityId |
| Pos / Vel | jet world position / velocity (speed derives from `|Vel|`) |
| Packed | the **status word** (§4) |
| IdB | jet's currently selected target EntityId (for de-confliction) |
| Text | callsign (the jet's grid name) |
| Num / Misc | unused (reserved) |

### 3.2 CONTACT (Tag 1)

| Field | Holds |
|-------|-------|
| Sender | the relaying PB (who put this packet on the wire now) |
| Pos / Vel | contact world position / velocity |
| Packed | **ObserverId** — the PB that originally saw it |
| IdB | contact EntityId (the target) |
| Num | **age** in seconds since observation (double) |
| Misc | `((int)Kind << 4) | Hop` — see §5 |
| Text | contact name |

### 3.3 STATION (Tag 2) — the HQ contract

| Field | Holds |
|-------|-------|
| Sender | station/HQ EntityId |
| Pos | station world position (jets show range to it) |
| Vel | unused (zero) |
| Packed | station flags (reserved, 0 today) |
| IdB | waypoint/rally EntityId (reserved, 0 today) |
| Num | **TTL** seconds; `0` ⇒ jets use their default `STATION_TIMEOUT` (10 s) |
| Misc | **order type**: `0` news · `1` order · `2` alert · `3` recall |
| Text | the screen the jet renders **verbatim**, newline-separated lines |

> The jet's `DL` page is a **thin client**: it prints `Text` line-by-line under an
> `HQ <range>km` header and does no status decoding of its own. HQ owns all aggregation and
> formatting. With no fresh STATION in range the jet falls back to a local count readout.

## 4. The status word (Packed in STATUS)

A single `long`, little fields packed low→high. Built in `BuildStatusWord` (jet), decoded in
`HQ/JetOS_HQ.cs`.

| Bits | Width | Field | Encoding | Decode |
|------|-------|-------|----------|--------|
| 0–6   | 7  | fuel %      | 0–100             | `w & 127` |
| 7–13  | 7  | battery %   | 0–100             | `(w>>7) & 127` |
| 14–20 | 7  | integrity % | functional/total engines | `(w>>14) & 127` |
| 21–24 | 4  | missiles    | bays ready, cap 15 | `(w>>21) & 15` |
| 25–27 | 3  | gun bucket  | `ammo/20`, cap 7 (0=empty…7=full) | `(w>>25) & 7` |
| 28–31 | 4  | state       | enum (below)      | `(w>>28) & 15` |
| 32–43 | 12 | altitude    | metres ÷ 8 (0…32 760 m) | `((w>>32) & 4095) * 8` |
| 44–55 | 12 | flags       | bitset (below)    | `(w>>44) & 4095` |
| 56–63 | 8  | —           | **reserved / free** | — |

**State enum:** `1` CRUISE · `2` ENGAGING (has target) · `3` DEFENDING (RWR) · `5` BINGO
(fuel < 15 %). `0` = unknown. Values `4,6–15` are free.

**Flags bits:** `0` RWR active · `1` being locked · `2` bingo fuel · `3` altitude warning.
Bits `4–11` are free.

Speed and altitude trend are **not** in the word — speed is `|Vel|` (free), and only a coarse
altitude is carried. Render state/flags as colour or glyphs, never as a stored name table.

## 5. Contact kind & hop packing (CONTACT.Misc)

`Misc = ((int)Kind << 4) | Hop`

- **Kind** is a `char`: `'H'` hostile, `'N'` neutral, `'U'` unknown. Decode: `(char)(Misc >> 4)`.
- **Hop** is 0–3. Decode: `Misc & 15`.

Hostile contacts feed the jet's `enemyList`; neutral/unknown feed `MapContactStoreV2`.

## 6. Relay & lifecycle mechanics

- **Broadcast cadence (jet):** every `0.2 s` a jet sends its STATUS plus any due contacts.
- **Multi-hop relay:** a received contact is re-broadcast by other jets with `Hop+1`, up to
  `MAX_HOPS = 3`, extending coverage past a single antenna's range. The original `ObserverId`
  and `age` are preserved across hops.
- **Send throttling (`ShouldSend`):** a contact is only re-sent if it **moved** (>0.1 m) or a
  `KEYFRAME_SECONDS = 5 s` keyframe is due, and never more often than the broadcast interval.
- **Only fresh local observations are originated:** `LOCAL_OBSERVATION_WINDOW = 3 s`.
- **Decay / pruning (wall-clock, lag-resistant):**
  - friends/jets: `FRIEND_TIMEOUT = 2 s`
  - relayed contacts: `CONTACT_DECAY_SECONDS = 30 s`
  - stations: `STATION_TIMEOUT = 10 s` (or the packet's TTL if non-zero)
- **HQ cadence:** broadcasts STATION every `1 s`; drops a jet from its roster after
  `JET_TIMEOUT = 5 s` of silence.

All ages use `SystemManager.ElapsedSeconds` (wall-clock); pausing SE freezes aging.

## 7. Capabilities — what the language CAN do

- Carry **fixed-schema scalar + vector telemetry** compactly and parse-free (positions,
  velocities, ids, and a dense 64-bit status word).
- Carry **arbitrary free text** (`Text`) — callsigns, contact names, and the HQ's orders/news
  "screen." This is the open-ended escape hatch.
- **Multi-hop relay** with origin attribution and age, for range beyond one antenna.
- **De-confliction data**: each jet advertises its selected target id.
- **HQ-driven presentation**: HQ ships a ready-to-render text screen; jets are dumb terminals.
- **Backward-compatible growth**: new tags, new status bits, and use of the reserved fields are
  all additive and do not break older receivers (§8).
- **No-`eval` extensibility**: because deployed code can't be patched, anything *informational*
  rides in `Text` (free, zero code); only a tiny fixed vocabulary of machine-actionable items
  (order type, and future waypoint/recall) needs code.

## 8. Limitations — what it CANNOT do

- **No runtime type union.** The tuple shape is fixed at compile time; "message kinds" are a
  `Tag` int + overloaded fields, not genuinely different payload types in one slot.
- **No variable-length lists in a single packet.** A tuple can't hold "N contacts." Options:
  send one message per item (what we do), or move the variable part into `Text`, or use an
  `ImmutableArray<…>` (adds a distinct type + code). The HQ roster sidesteps this by flattening
  everything into one `Text` blob.
- **Shape changes are breaking.** Adding/removing/reordering tuple fields changes the concrete
  type, so old jets' type-check fails and silently drops the message → **the whole fleet must
  update together** for a shape change. (Tag/bit/reserved-field changes are *not* breaking.)
- **`Text` is the only home for dynamic/structured data**, and it must be length-bounded
  (bandwidth + the jet renders it). There is no schema enforcement inside `Text`.
- **Deeply nested generics cost packed chars.** `MyTuple`/`Vector3D` type names cannot be
  minified, so each `is`/cast/`Create` site spends real characters — keep nesting to one level
  and reuse the single shape (we currently spell it ~3 times).
- **Only IGC-whitelisted types may be sent**: primitives, `string`, VRageMath structs
  (`Vector3D`, …), nested `MyTuple` (≤6 fields each), and `ImmutableArray<…>`. No custom
  classes — the tuple *is* the marshalling format.

## 9. Versioning & compatibility rules

- ✅ **Safe / non-breaking:** add a new `Tag`; define new bits in the status word; use the
  reserved status bits (56–63), flag bits (4–11), or the unused STATION fields. Old receivers
  ignore what they don't understand.
- ⚠️ **Breaking (requires fleet-wide update):** change the tuple field count/order/types.
- There is generous headroom before any breaking change is forced: 8 reserved status bits,
  ~8 free flag bits, several free state-enum values, and 2 reserved STATION fields.

## 10. Extending it

1. **New informational content** → put it in `Text`. Zero code on the jet beyond rendering.
2. **New per-jet metric** → claim reserved status-word bits, set them in `BuildStatusWord`,
   decode them wherever consumed (HQ today). No shape change, no fleet break.
3. **New message kind** → pick the next `Tag`, reuse the existing tuple shape, add a dispatch
   branch. Old receivers ignore it.
4. **New machine-actionable order** → assign a STATION `Misc` order-type code (or use the
   reserved `IdB` waypoint / `Packed` flags), and teach the jet to act on it. Keep this
   vocabulary small; everything else stays free text.

## 11. Gotchas

- The jet runs at `Update1`; the HQ at `Update10` with a 1 Hz broadcast gate. Cadences differ —
  don't assume synchronization.
- IGC has practical per-message size and volume limits; keep `Text` modest (a screenful), and
  remember the 0.2 s contact storm is the heaviest traffic, not STATUS/STATION.
- A jet only displays an HQ screen if a STATION packet is fresh (within TTL/`STATION_TIMEOUT`);
  otherwise it shows the local fallback.

## 12. Example — the HQ "news screen" end to end

`HQ/JetOS_HQ.cs` seeds this into the station PB's **Custom Data** on first run (operator edits it):

```
! NYINAH CORP TACNET
ORDERS
 VIPER  RTB + rearm
 HORNET hold CAP WP-2
THREATS
 3 bandits 045 ang8
 SAM active @ ridge
NEWS
 Carrier ETA 12m
 Wx clear wind 5E
FREQ RED  //  GLHF
```

The leading `!` sets **order type 2 (ALERT)** and is stripped. Each tick the HQ appends a live
roster (decoded from every jet's status word) and broadcasts the whole thing as the STATION
`Text`. A jet in range renders it **verbatim** on its `DL` page, under a range header:

```
 DL                                  M1
 SYSTEM MENU > DL
 HQ 3km
 NYINAH CORP TACNET
 ORDERS
  VIPER  RTB + rearm
  HORNET hold CAP WP-2
 THREATS
  3 bandits 045 ang8
  SAM active @ ridge
 NEWS
  Carrier ETA 12m
  Wx clear wind 5E
 FREQ RED  //  GLHF
 -- WING --
 VIPER1 F87 H100 4m ENG
 VIPER2 F62 H100 2m DEF
 HORNET F41 H80 0m BNG
```

This one screen exercises the whole language: the **alert** order type, free-form multi-line
**orders / threats / news** (the "website"), the **auto roster** with each jet's decoded fuel
(`F`), integrity (`H`), missiles (`m`) and state (CRU/ENG/DEF/BNG), and the jet-side **range to
HQ** header — all carried in one STATION packet's `Text`, with zero bespoke parsing on the jet.

With **no** HQ in range the same page falls back to the local readout, e.g. `NO HQ LINK` /
`3W  5C` (3 wingmen pooled, 5 contacts relayed).

