# Dark Forces Map Randomizer — Room Specification

**Status:** Draft 0.7 (2026-10-02) · **Applies to:** proof-of-concept (spec version `0`)

This document defines what a *room* is, how it is packaged, and the rules a room must satisfy so the
randomizer can place, rotate and connect it with other rooms. Rules are written so that each one can be
checked by a tool; rule IDs (`G3`, `I5`, …) are what the validator will report.

Building a room? Start with the walkthrough in [`authoring-rooms.md`](authoring-rooms.md); this document is the rulebook.

The key words **MUST**, **MUST NOT**, **SHOULD** and **MAY** are used as in RFC 2119.
Numbers come from the *Dark Forces Unofficial Specs 3.01* (see `reference/df-specs.md`) unless marked
**[verify]**, meaning they must be confirmed in-engine before the spec leaves draft.

---

## 1. Concepts

| Term | Meaning |
|---|---|
| **Room** | A self-contained group of sectors, with its objects and INF logic, authored in local coordinates. |
| **Connector** | A standard doorway where a room can join another room or a generated hallway. |
| **Portal wall** | The single open wall of a connector that gets adjoined to another connector's portal wall. |
| **Room package** | A folder holding a room's LEV/O/INF files and its `room.json` metadata. |
| **Instance** | One placement of a room in a generated level: room + rotation + translation + name prefix. |
| **Layout** | The full set of instances, hallways and progression placements that make one generated level. |
| **Generator-owned** | Things only the randomizer places: player start, level exit, keys, goal items, locked doors. |

## 2. Coordinate system and units

- Axes follow the LEV format: **X** and **Z** are the map plane, **Y points down** (a higher floor has a
  *more negative* altitude).
- Object yaw: 0° = map north (+Z), increasing clockwise.
- Unit: one DF unit (≈ 25 cm by the specs' measured metrics). Textures are believed to map
  8 texels per unit, so a 64×64 floor texture repeats every 8 units **[verify]**.
- **Placement grid: 8 units.** Every instance translation in X and Z is a multiple of 8, and every rotation is a
  multiple of 90°. This keeps floor/ceiling textures aligned without rewriting offsets (floor textures are
  world-anchored) and keeps connector vertices on exact integer coordinates, so vertices that should meet do so
  exactly, with no floating-point drift.
- Vertical translations are multiples of **1 unit** in the PoC. Whether wall textures stay aligned under vertical
  moves depends on how DF anchors wall texture V offsets **[verify]**.

### Player metrics that the rules depend on

| Metric | Value | Used by |
|---|---|---|
| Max step-up (walkable) | 3.50 | traversal (§7), connectors (§4) |
| Max jumpable height | 9.65 | traversal requirements |
| Min walkable width | 4.90 | connectors, G7 |
| Min walkable height (floor→ceiling) | 6.80 | connectors, G7 |
| Min crouch height | 3.00 | traversal requirements |
| Max fall without damage | 36 | traversal (one-way drops) |
| Max fall without death | 79 | traversal (one-way drops) |

## 3. Room package

```
rooms/
  secbase-hangar-01/
    room.json      metadata (§8), including the spec version it targets
    ROOM.LEV       geometry: the room sectors + connector stubs only
    ROOM.O         objects (MAY be absent if the room has none)
    ROOM.INF       logic (MAY be absent)
    preview.png    optional, for the community catalog
```

- The LEV/O/INF files are **ordinary, complete DF files** in the room's local coordinates. A room is authored
  and edited in any DF editor (WDFUSE, The Force Engine's editor) as a tiny level.
- The tool can wrap any room package into a **standalone test GOB** (adds a player start at a chosen connector
  and seals every connector), so authors can play-test a room by itself.
- Local origin: any point; the tool normalizes placements by connector position, not by origin.
- **Stock rooms** (extracted from the original levels) contain LucasArts geometry, so they are never committed or
  shared as files. The repository keeps only the *selection* (source level + sector numbers,
  `rooms/poc-selection.json`), and `tools/room-extractor` regenerates the packages from the user's own `DARK.GOB`
  into `rooms/stock/` (git-ignored). Community rooms are original work and can be shared normally.

## 4. Connectors

A connector is a **stub sector**: a short rectangular sector at the room's edge whose outer wall is the portal.

```
            room interior
      ───────┐        ┌───────
             │  stub  │   depth 4
             │ sector │
             └────────┘  ← portal wall (width 8), faces outward
```

### Rules

| ID | Rule |
|---|---|
| C1 | A stub sector is named `CX_<id>`, where `<id>` is 1–4 characters `[A-Z0-9]`, unique within the room. |
| C2 | The stub MUST be a rectangle: 4 vertices, 4 walls, axis-aligned, **width 8** along the portal, **depth 4**. |
| C3 | All four stub vertices MUST have integer coordinates, and the portal wall's vertices MUST lie on the 8-unit grid. |
| C4 | The portal wall MUST have `ADJOIN: -1` in the room file. It is the only wall of the stub not adjoined into the room. |
| C5 | The wall opposite the portal MUST adjoin a room sector; the two side walls MUST NOT adjoin anything. |
| C6 | Stub floor altitude MUST be an integer. Stub ceiling MUST be exactly **8** above its floor (`ceiling = floor − 8`), matching stock doors (SECBASE and ARC doors are 8 high). |
| C7 | Stub sector MUST have FLAGS `0 0 0`, SECOND ALTITUDE 0, and MUST NOT be an INF item or referenced by INF. (Doors on stubs are added by the generator at join time, never authored; see *Doors* below.) |
| C8 | The portal wall's MID texture is the **seal texture**: what is shown if the generator leaves the connector unused. It MUST be a valid texture index. |
| C9 | A room MUST have at least 1 connector. The PoC limits rooms to 4 connectors. |
| C10 | Connector `facing` (N/E/S/W) is the portal wall's outward normal. `room.json` MUST agree with the geometry, and the validator derives and checks it. |
| C11 | An **adapter** sector named `CA_<id>` MAY sit between the room and stub `CX_<id>`, bridging an original doorway of any width to the standard 8-wide stub. It MUST be a single convex loop, adjoin exactly one stub and at least one room sector, and has the same restrictions as a stub (C7; no objects). |

### How connectors are joined

Two connectors join only if they face opposite directions after rotation and have the same floor altitude after
translation. The generator translates one instance so the two portal walls coincide vertex-for-vertex
(the LEFT of one equals the RIGHT of the other), then sets ADJOIN, MIRROR and WALK on both walls to point at each other.

An unused connector stays sealed (`ADJOIN: -1`) and becomes a shallow alcove showing the seal texture.
Room authors SHOULD choose a seal texture that reads as a closed door or blank wall.

### Doors

A room's original door is kept as **data on its connector** (`connectors[].door` in `room.json`), not as geometry:
its kind (`flag` instant door, or the INF class `door` / `door_mid` / `door_inv`), its **panel** texture (what
the closed door shows), its **frame** texture, its INF settings such as speed and sound, and the original key, for reference.

- **Unjoined:** the portal's seal texture is the door panel, so an unused connector looks like a shut door.
- **Joined:** the generator turns **one** of the two stubs into that door. It uses the first room's door if it has one,
  otherwise the second's. The stub gets the DOOR flag (or the INF door elevator), the adjoining walls' TOP textures
  become the panel, and the stub's side walls become the frame. So every join has exactly one working door.
  A join where neither side had a door stays an open passage.
- Locks (`key:`) are generator-owned and will come from the progression knobs; `door.key` is informational only.
- INF door classes whose stops depend on the original sector's heights (`basic`, `inv`, `basic_auto`) are rebuilt as flag doors.

Anything else is bridged by a **generated hallway** between the two connectors. Hallways are generated *room
packages*: an 8-wide corridor (12 high) with a standard stub at each end, so they pass this spec's validator and are
joined, doored, locked and solved exactly like rooms. Two shapes exist, straight and L-shaped; either end can attach,
at any rotation, so one L covers left and right turns. Floor changes become a 4-unit landing, then stairs of at most 3 units per 4-unit step. The landing keeps the first step away from the 8-high stub: a step right off the stub would leave a 5-high opening and force the player to crouch.
Elevators for large drops are planned. Hallways are built from the same stub dimensions,
so they satisfy these rules by construction.

## 5. Geometry rules

| ID | Rule |
|---|---|
| G1 | Every sector's walls MUST form closed loops: each vertex is the LEFT of exactly as many walls as it is the RIGHT of (walls need not be stored in loop order). Walls MUST wind **clockwise** (+Z north, so negative signed area), as every sector in the stock levels does. The floor MUST NOT be above the ceiling. |
| G2 | Every adjoin MUST be mutual: if wall *w* of sector *s* adjoins *t*/*m*, then wall *m* of *t* adjoins *s*/*w*. |
| G3 | No wall MAY adjoin outside the room except connector portals, which are unadjoined in the room file (C4). |
| G4 | The room's footprint (all vertices) MUST fit inside the `bounds` declared in `room.json`. The PoC reserves the whole axis-aligned bounding box, and instances' boxes MUST NOT overlap. |
| G5 | Connector stubs MUST lie on the boundary of `bounds`: the portal wall is on the box edge it faces. |
| G6 | PoC size limits: bounds ≤ 256 × 256 units, ≤ 64 sectors, ≤ 512 walls. |
| G7 | Walkable sectors SHOULD have floor→ceiling ≥ 6.8, and openings intended for walking SHOULD be ≥ 4.9 wide. An opening between two such sectors whose floors differ by a walkable step (≤ 3.5) SHOULD also be ≥ 6.8 high (lower floor's ceiling vs. higher floor), or the player must crouch to take the step. Doors and INF-moved sectors are not checked. Violations are warnings, since they may be intentional crawlspaces. |
| G8 | Exterior sectors (sector flag 1 = sky, 128 = pit) are allowed. The room MUST declare `"exterior": true` so the generator can apply one consistent PARALLAX value. |
| G9 | Sector LAYER values are room-local. The generator reassigns layers for the in-game map (§9). |
| G10 | Rooms SHOULD keep the number of adjoins visible through each other below 20 from any viewpoint. The engine hard limit is 40 active windows, and hallways add to it. This is a guideline, not validated. |
| G11 | Floor textures SHOULD be 64×64 (the engine misbehaves otherwise). A room whose floor or ceiling textures have a visible direction (arrows, stripes) MUST set `"rotatable": false` if rotating would break them (DF floor textures do not rotate with the room). |

## 6. Object rules (`ROOM.O`)

| ID | Rule |
|---|---|
| O1 | A room MAY contain **one** player object (`LOGIC: PLAYER`), but no more. It marks the room's start point and takes precedence over `startPoints` (M5). The generator and playtest remove it and spawn the single real player there when the room is the start room, else just inside a doorway. |
| O2 | Rooms MUST NOT contain **progression items**: `RED`, `BLUE`, `YELLOW`, `CODE1`–`CODE9`, gating items (`CLEATS`, `MASK`, `GOGGLES`), or key carriers (`I_OFFICERR/B/Y`, `I_OFFICER1`–`9`). Progression items go only in declared `itemSlots`. **Goal items** (`PLANS`, `PHRIK`, `NAVA`, `DATATAPE`, `DT_WEAPON`, `PILE`) MAY stay in the room if `goals` in `room.json` declares each one (M8). |
| O3 | Every object MUST be inside a room sector: inside its polygon, with Y between the sector's ceiling and floor (floor − second altitude for platforms). Connector stubs MUST NOT contain objects. |
| O4 | Ordinary enemies, pickups, scenery, SAFE points and generators are allowed and move with the room. |
| O5 | Objects that use VUE motion files (`LOGIC: KEY`, `VUE:` entries) are **not allowed** in the PoC, because VUE paths are absolute coordinates in separate files. |
| O6 | The room MUST list its resource usage (3DO/WAX/FME/VOC names). The generator enforces the level-wide limit of **64 each** of PODs, FMEs and WAXs across all instances. |

## 7. INF rules (`ROOM.INF`)

| ID | Rule |
|---|---|
| I1 | Every sector an INF item names, and every sector any INF statement references (`client:`, `slave:`, `target:`, message receivers, `adjoin:`, stop values given as sector names), MUST exist in the same room. |
| I2 | Sector names used by INF MUST be ≤ 12 characters `[A-Za-z0-9_]`, leaving room for the generator's instance prefix. The engine's real limit is **[verify]**. |
| I3 | Rooms MUST NOT send `complete`, MUST NOT reference the `complete` elevator, and MUST NOT send `lights` to `SYSTEM`. These are level-wide and generator-owned. |
| I4 | Rooms MUST NOT use `key:`. Locked doors are generator-owned and are placed on connectors or hallways. |
| I5 | `teleporter chute` targets MUST be in the room, and chutes count as a one-way edge in `traversal`. |
| I6 | `text:` MAY reference only stock TEXT.MSG ids. |
| I7 | `item: level` is not allowed (it is level-wide). |

### What the generator rewrites per instance

| Data | Translate (dx, dy, dz) | Rotate (90° steps) |
|---|---|---|
| Vertices | X += dx, Z += dz | rotate about the placement origin |
| Floor, ceiling altitudes | += dy | — |
| SECOND ALTITUDE | unchanged (relative) | — |
| Object X/Y/Z | += dx/dy/dz | rotate position; YAW += rotation |
| INF `center:` | X += dx, Z += dz | rotate |
| INF `angle:` (scroll_floor/ceiling, morph_move, moving sectors) | unchanged | += rotation (0 = north) |
| INF `angle:` on `scroll_wall` | unchanged (0 = down, wall-relative) | unchanged |
| Absolute `stop:` values for altitude elevators: `move_floor`, `move_ceiling`, `move_fc`. INF altitudes point **up**, the opposite of LEV (`stop: -20` is LEV altitude 20; verified on SECBASE's cage doors and guard-post lifts), so they move the other way | −= dy | — |
| `basic`, `inv`, `basic_auto`: stops are automatic when none are given; explicit absolute stops −= dy, as above | −= dy | — |
| `@n` relative stops; stops given as sector names | unchanged | — |
| Stops of `change_light`, `change_wall_light`, `scroll_*`, `morph_*`, `move_wall`, `rotate_wall`, `move_offset` | unchanged (they are not altitudes) | unchanged |
| Door elevators (`door`, `door_mid`, `door_inv`) | automatic stops, nothing to rewrite | — |
| Sector names | prefixed `R<nn>_` | — |
| Texture, POD/SPR/FME/SOUND indices | remapped into merged tables | — |
| Floor/ceiling texture offsets | unchanged (8-unit grid, §2) | unchanged (textures don't rotate, G11) |

## 8. Metadata (`room.json`)

`dftool room metadata <dir> --write` builds this file. It derives `palette`, `exterior`, `bounds`, connector
ids/facings/floors, `resources` and goal positions from the room files, and keeps every other field from the
existing `room.json` (see authoring-rooms.md §6).

```json
{
  "spec": 0,
  "id": "secbase-hangar-01",
  "name": "Secret Base hangar",
  "author": "community-handle",
  "source": { "level": "SECBASE", "sectors": [41, 42, 43, 57] },
  "palette": "SECBASE.PAL",
  "exterior": false,
  "rotatable": true,
  "bounds": { "minX": 0, "minZ": 0, "maxX": 96, "maxZ": 64, "minY": -24, "maxY": 0 },

  "connectors": [
    { "id": "A", "facing": "W", "floor": 0,
      "door": { "kind": "flag", "panel": "IDASMAL3.BM", "frame": "IWADARK3.BM", "inf": [] } },
    { "id": "B", "facing": "E", "floor": 0 },
    { "id": "C", "facing": "N", "floor": -8 }
  ],

  "traversal": [
    { "from": "A", "to": "B", "requires": [] },
    { "from": "B", "to": "A", "requires": [] },
    { "from": "A", "to": "C", "requires": ["CLEATS"] },
    { "from": "C", "to": "A", "requires": [], "oneWay": true, "note": "drop from ledge" }
  ],

  "itemSlots": [
    { "id": "s1", "x": 48, "y": 0, "z": 32, "reachableFrom": ["A", "B"], "requires": [] }
  ],

  "goals": [
    { "item": "PLANS", "x": 40, "y": 0, "z": 16, "reachableFrom": [] }
  ],

  "startPoints": [
    { "x": 16, "y": 0, "z": 32, "yaw": 90 }
  ],

  "resources": { "pods": [], "sprites": ["STORMFIN.WAX"], "frames": ["IENERGY.FME"], "sounds": [] },
  "tags": ["imperial", "combat-light"]
}
```

### Field rules

| ID | Rule |
|---|---|
| M1 | `connectors[*].id` MUST match a `CX_<id>` stub sector, and `facing`/`floor` MUST match the geometry (C10). |
| M2 | `traversal` is a directed graph between connectors and item slots. Each edge lists the items needed to cross it (`CLEATS`, `MASK`, `GOGGLES`, `RED`, …). An edge not listed is assumed **impossible**, so a room with no traversal entries is a dead end from every connector. |
| M3 | Requirements use the item logic names from O2. `jump` and `crouch` are reserved for later movement-tech tiers and are unused in the PoC. |
| M4 | `itemSlots` MUST satisfy O3, and each slot's `reachableFrom` MUST be consistent with `traversal`. |
| M5 | `startPoints` are optional. The first one is the room's start point when `ROOM.O` has no player object (O1). A start point is assumed to reach the room's first connector. A room without one starts the player just inside that connector. |
| M6 | `bounds` MUST contain every vertex (G4), and `minY`/`maxY` MUST cover every floor and ceiling (for later stacking). |
| M7 | `resources` MUST list exactly the names used by `ROOM.O` (O6). The validator checks this. |
| M8 | Each `goals` entry MUST name a goal item and match an object in `ROOM.O` with that logic (within 1 unit in X/Z). An item appears at most once per room (the engine counts each goal item once per level). `reachableFrom` lists the connectors it can be reached from; empty means all. |

## 9. Generator-owned responsibilities (for reference)

These are **not** authored in rooms. They are listed so authors know what they can leave out.

- The player and the SAFE point at the start (at the start room's start point, O1/M5), the level exit trigger, and the `complete` elevator with its stops.
  The exit is an unused connector's stub (lit, with a `trigger single` sending `complete 1`). The `complete`
  elevator lives in a small isolated script sector away from all rooms, as in the stock levels.
- GOL goals: `ITEM:` goals for the goal items the rooms keep, then `TRIG: 1` for the exit. A goal item counts once
  per level, so when a goal room repeats, only its first copy keeps the item. The `complete` elevator gets one
  `hold` stop per goal item plus one for the exit, then its `complete` stop, so the mission ends once all of them
  are done, in any order.
- Placing keys (`IKEYR/B/Y.FME`, `LOGIC: ITEM RED`…). Keys go in `itemSlots`, or in an automatic spot: the room's largest plain sector, never a lift,
  pit, sky, water or damaging floor.
- Rebuilding one original door per join (from `connectors[].door`), and locks (INF `key:`) on connectors or hallways.
- Hallways and adapters between connectors.
- Merging texture and resource tables, the palette, and a single PARALLAX value.
- Assigning sector LAYER values so the in-game map stays readable.
- JEDI.LVL entry and GOB packing.

## 10. Open questions to settle in-engine

1. Wall texture V anchoring under vertical translation: does a room moved by `dy` keep wall texture alignment?
2. Floor texture scale: confirm 8 units per 64-texel tile, which the 8-unit grid assumes.
3. INF sector-name length limit, and total INF item / sector limits in DOS `DARK.EXE` vs The Force Engine.
4. Whether seamless adjoins between rooms with different AMBIENT values look acceptable, or whether connector stubs should blend light.
5. Whether rooms can overlap in XZ when their Y ranges don't intersect (true room-over-room). The PoC forbids it (G4) until this is tested.
6. Whether a stub ceiling height of 12 fits enough stock rooms' doorways. It may become a small set of connector classes (e.g. 8×12 and 16×16).
   **Resolved (Draft 0.2):** stock doors are 8 high (SECBASE, ARC), so stubs are 8 high (C6). Doorway widths vary
   (6–16), which the adapter sector (C11) absorbs. Many SECBASE doorways are angled (43°, 70°, 127°…), which will need
   angled adapters. ARC's are axis-aligned.

## 11. Proof-of-concept scope

The PoC implements the subset needed to stitch 2–3 rooms from a **single source level** (one palette):
C1–C11, G1–G6, O1–O3, O5, I1, I3, I4, M1, M2, M6, plus the rewrite table in §7.
The validator (`dftool room validate`) implements these as errors, and G7, I2, I5, M7 as warnings.
Everything else is validated as a warning until the open questions above are answered.
