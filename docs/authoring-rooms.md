# Authoring rooms

This guide walks through building your own room for the randomizer, from an empty folder to a room that
shows up in generated levels. The rules themselves are in [`room-spec.md`](room-spec.md). This guide explains
them in the order you'll meet them, and every rule ID it mentions (`C2`, `M8`, …) is what the validator reports.

A worked example lives in [`rooms/examples/example-junction/`](../rooms/examples/example-junction). Open it
alongside this guide.

---

## 1. What a room is

A room is a small, self-contained piece of a level: a few sectors, the objects in them, and their INF logic.
It has one to four **connectors**: standard doorways where the generator can attach another room or a hallway.

A room is a folder, a *room package*:

| File | What it holds | Who writes it |
|---|---|---|
| `ROOM.LEV` | Geometry: the room's sectors and its connector stubs | You, in a DF editor (`room init` gives you a starting point) |
| `ROOM.O` | Objects: enemies, pickups, scenery. Optional. | You |
| `ROOM.INF` | Logic: elevators, switches, triggers. Optional. | You |
| `room.json` | Metadata: connectors, doors, how they connect inside the room, … | `room metadata` derives most of it. You add what only you know. |

The LEV, O and INF files are ordinary Dark Forces files. The room is a tiny level in its own local
coordinates, and the generator moves, turns and renames it when it places the room.

## 2. The workflow

All authoring commands are under `dftool room`:

```
dftool room init     <dir> [options]     create a starter room that already validates
dftool room metadata <dir> [--write]     build room.json from the room files, keeping what you wrote
dftool room validate <dir>...            check rooms against the spec
dftool room playtest <dir> --gob <DARK.GOB> --out <FILE.GOB> [--at <connector>]
                                         play the room on its own
```

(`dftool` is `dotnet run --project src/DfTool --`.)

A typical session:

```
dftool room init rooms/mine/guard-room --connectors W,E16,N --size 48x32 --name "Guard room" --author you
#   ...edit ROOM.LEV / ROOM.O / ROOM.INF in your editor...
dftool room metadata rooms/mine/guard-room --write     # re-derive connectors, bounds, resources, goals
#   ...edit room.json: doors, traversal, item slots...
dftool room validate rooms/mine/guard-room
```

Then try it:

```
# The room on its own, the player just inside doorway A (also writes out/GUARD.zip for The Force Engine)
dftool room playtest rooms/mine/guard-room --gob gamedata/DARK.GOB --out out/GUARD.GOB --at A

# The room in generated levels, alongside the stock rooms
dftool generate --gob gamedata/DARK.GOB --rooms rooms/stock --rooms rooms/mine --out out/MINE.GOB --seed 1
```

`room playtest` replaces one mission (`--slot`, default SECBASE) with your room alone: doorways stay sealed,
and the player and a SAFE point start at the room's start point (§7.1). With `--at`, or if the room has no start
point, they start 4 units inside that doorway (default the first), facing in. In The
Force Engine, load the `.zip` as a mod. In DOS, run `dark -uGUARD.GOB`. Then start that mission. It refuses a room
with validation errors unless you pass `--force`, and `--no-zip` skips the zip. Keep `.GOB` names to 8
characters for DOS.

Run `room metadata --write` again whenever you change the geometry or objects. It never touches your
LEV/O/INF files, and it refuses to write a `room.json` that leaves the room invalid.

## 3. Starting a room: `room init`

`room init` writes a rectangular room (floor 0, open on top to `--height`) with a connector stub centered on
each wall you list, a default texture set, and a `room.json` that matches. The result validates cleanly. It's a
starting point to reshape, not a template you must keep.

| Option | Default | Meaning |
|---|---|---|
| `--connectors` | `W,E` | One doorway per listed wall, `N` `E` `S` `W`. A number after the letter is the width of the opening into the room: `E16` is 16 wide, narrowed to the standard 8-wide stub by an adapter (§4). |
| `--size` | `32x32` | Room width (along X, east) × depth (along Z, north), multiples of 8. |
| `--height` | `16` | Floor to ceiling. |
| `--id` | the folder name | Lower-case letters, digits and dashes. Must be unique across the room pool. |
| `--name`, `--author` | | Free text for `room.json`. |
| `--palette` | `SECBASE.PAL` | Palette the textures are meant for. Rooms in one level must share a palette (§8). |
| `--wall`, `--floor`, `--ceiling` | `IWPANEL1.BM`, `IPSEC3.BM`, `IF3.BM` | Textures. |
| `--seal`, `--frame` | `IDDOOR1.BM`, `IWFRAME1.BM` | Unused-doorway texture (the door panel), and the door frame. |
| `--ambient` | `24` | Light level, 0–31. |
| `--no-doors` | doors on | Leave doorways as open archways instead of giving each a door (§5). |
| `--force` | | Overwrite the room files in a folder that isn't empty. |

```
             N (C)
        ┌────[  ]────┐
        │            │
 W (A) [             ] E (B)    room init --connectors W,E,N
        │            │
        └────────────┘
```

## 4. Doorways: connector stubs

This is the part the generator depends on most. Every doorway is a small sector at the edge of the room, the
**stub**, named `CX_<id>`. Its outer wall, the **portal**, is where the generator attaches the next room.

```
                  room
      ────────────┐         ┌────────────
                  │  CX_A   │   4 deep
                  │         │
                  └─────────┘   ← portal wall: 8 wide, on the 8-unit grid,
                                  not adjoined, faces straight N, E, S or W
```

The rules, and how to meet them in an editor:

| Rule | What to do |
|---|---|
| **C1** name | Name the sector `CX_` + 1–4 letters or digits: `CX_A`, `CX_N2`. The id (`A`, `N2`) is how `room.json` refers to it. |
| **C2** shape | Exactly 4 vertices: a rectangle **8 wide** (along the portal) by **4 deep**, with edges running along the axes. |
| **C3** grid | All four vertices on whole numbers, and the two portal vertices on multiples of **8**. |
| **C4** portal | The outer wall is not adjoined to anything. |
| **C5** sides | The inner wall adjoins your room. The two side walls are solid. |
| **C6** height | Floor on a whole number, ceiling exactly **8** above it (in LEV numbers, `ceiling = floor − 8`). |
| **C7** plain | No sector flags, no INF on the stub, nothing referencing it. Doors are not drawn in the stub; they're described in `room.json` (§5) and built by the generator. |
| **C8** seal | The portal's MID texture is what players see if the doorway is left unused. Use a shut door or a plain wall. |
| **C9** count | 1 to 4 connectors per room. |
| **G5** edge | The portal must be on the outside edge of the room: nothing of the room may stick out past it. |
| **O3** empty | No objects inside a stub or adapter. |

### Wider openings: adapters

If your room's doorway is wider than 8, or doesn't line up with the grid, put an **adapter** sector named
`CA_<id>` (same id as its stub) between the room and the stub. The adapter can be any convex shape. It adjoins
the room on one side and the stub's inner wall on the other, and has the stub's restrictions (C11).
`room init --connectors E16` builds one for you. The example room's east doorway is one.

```
        room
   ─────┐                 ┌─────
        │   CA_B (any     │
         \   width)      /
          └──┬───────┬──┘
             │ CX_B  │
             └───────┘   portal (8 wide)
```

### How doorways get joined

Two connectors join when, after the generator has turned and moved the rooms, they face each other at the
same floor height and their portals line up exactly. The generator only turns rooms in 90° steps and moves them
in 8-unit steps. That's why portals must be axis-aligned and on the grid. When two doorways can't meet
directly, the generator builds a hallway between them, with stairs for height differences.

A connector that ends up unused stays sealed: a shallow alcove showing its seal texture. The level exit is
also chosen from unused connectors (lit up, with the trigger that ends the mission), so you never place an
exit yourself.

### Common mistakes

- **Portal off the grid by half a unit.** Snap the portal vertices to multiples of 8, not the stub's middle.
- **Stub drawn counter-clockwise.** DF sectors wind clockwise (G1). Most editors handle this; hand-written files don't.
- **Ceiling 8 *below* the floor.** Y points down in LEV files: a ceiling 8 above floor 0 is altitude `-8`.
- **Stairs right at the stub.** If your room's floor rises right behind the stub, the 8-high stub leaves too little
  headroom to walk up. G7 warns about any opening the player must crouch through.

## 5. Doors

A door at a doorway is described in `room.json`, not drawn. When the connector is joined, the generator turns
**one** of the two stubs into a working door, using this room's door if it has one, otherwise the other room's.
A join where neither side has a door is an open passage. Only joins with a door can be locked by the
randomizer.

```json
{ "id": "A", "facing": "W", "floor": 0,
  "door": { "kind": "flag", "panel": "IDDOOR1.BM", "frame": "IWFRAME1.BM", "inf": [] } }
```

| Field | Meaning |
|---|---|
| `kind` | `flag`: a standard instant door (sector flag 2). `door`, `door_mid`, `door_inv`: INF door elevators. |
| `panel` | Texture of the closed door. Also use it as the portal's seal texture, so an unused doorway looks shut. |
| `frame` | Texture for the doorway's side walls. |
| `inf` | For INF kinds, extra statements for the door elevator, e.g. `"speed: 20"`, `"sound: 1 door.voc"`. Leave out `stop:`, `key:` and `message:`, which the generator owns. |

Leave `door` out for an open archway. Never put `key:` on a door yourself: locks come from the randomizer's knobs.

## 6. `room.json`

`room metadata` writes this file. It splits the fields into two kinds:

**Derived:** rebuilt from the room files every run. Don't edit these by hand; your changes would be overwritten.

| Field | From |
|---|---|
| `palette` | `ROOM.LEV` |
| `exterior` | Any sky or pit sector (sector flags 1, 128) makes it `true` (G8) |
| `bounds` | Every vertex, floor and ceiling (G4, M6) |
| `connectors[].id/facing/floor` | Each `CX_` stub |
| `resources` | The POD/SPR/FME/SOUND tables in `ROOM.O` (O6) |
| `goals[].item/x/y/z` | Goal items in `ROOM.O` (§7) |

**Authored:** yours. Kept on every run.

| Field | Meaning |
|---|---|
| `id`, `name`, `author` | `id` must be unique across the pool. |
| `tags` | Free-form labels (`imperial`, `combat-light`, …), for filtering later. |
| `notes` | Anything worth recording. |
| `rotatable` | `false` if the room must never be turned, e.g. its floor texture has arrows (G11). |
| `connectors[].door` | §5 |
| `traversal` | §6.1 |
| `itemSlots` | Spots where the generator may place keys and other progression items (§7). |
| `goals[].reachableFrom` | §7 |
| `startPoints` | Where the player starts if this is the start room, `{ x, y, z, yaw }`. Only used when `ROOM.O` has no player object (§7.1). |

`room metadata` also reports what changed: connectors added, removed, or moved to a new facing or floor, goals
found or gone, traversal edges dropped or drafted. Read that report, because it's how you catch an
accidental change.

### 6.1 Traversal: how the doorways connect inside the room

`traversal` is a list of one-way edges saying which doorway can reach which, and what it takes:

```json
"traversal": [
  { "from": "A", "to": "B", "requires": [] },
  { "from": "B", "to": "A", "requires": [] },
  { "from": "A", "to": "C", "requires": ["CLEATS"], "note": "icy ramp" },
  { "from": "C", "to": "A", "requires": [], "oneWay": true, "note": "drop from the ledge" }
]
```

- **An edge you don't list is impossible.** A doorway with no edges is a dead end. Edges are one-way, so list
  both directions when the player can go both ways.
- `requires` names items needed to cross (`CLEATS`, `MASK`, `GOGGLES`, `RED`, …). Leave it empty when the way is open.
- `oneWay` and `note` are for you and other authors.
- When a connector has no edges at all, `room metadata` **drafts** "reaches every other doorway" edges with the note
  `auto: unverified`. Play the room, fix what's wrong, and change the note (the example uses
  `"one open floor, nothing in the way"`). The randomizer trusts this list when it decides where keys can go
  and whether a level can be finished, so a wrong edge can make an unwinnable level.

## 7. Objects and goals

Put enemies, pickups, scenery and generators in `ROOM.O` as in any level. They move with the room (O4). A few
things are off limits, because the randomizer places them:

| Not allowed in a room | Why | Instead |
|---|---|---|
| Keys `RED`/`BLUE`/`YELLOW`, `CODE1`–`9`, cleats, mask, goggles, key-carrying officers (O2) | The randomizer decides where progression goes | An `itemSlots` entry where such an item could sit |
| VUE-animated objects (O5) | VUE paths are absolute coordinates | — |

### 7.1 The player object and start points

You may leave **one** player object (`LOGIC: PLAYER`) in `ROOM.O`, so the room can be tested in your editor. It
marks the room's **start point**. Where the player starts, when your room is the level's start room:

1. at your player object, if `ROOM.O` has one;
2. otherwise at the first `startPoints` entry in `room.json`;
3. otherwise 4 units inside the room's first doorway, facing in.

The player object itself never appears in a generated level. The generator removes it from every copy of the
room and spawns the one real player at the start point, which moves and turns with the room. `room playtest`
does the same. Two or more player objects in one room is an error (O1).

The randomizer assumes the start point can reach the room's first connector (A), as the doorway start does.
Put it somewhere the player can walk from to the rest of the room.

**Goal items** (`PLANS`, `PHRIK`, `NAVA`, `DATATAPE`, `DT_WEAPON`, `PILE`) are the exception. Place one in
`ROOM.O` and `room metadata` declares it in `goals` (rule M8). Each goal item may appear once per room. In a
generated level, the mission ends once every goal is taken and the exit is reached. If your room is used
twice, only its first copy keeps the goal. Set `reachableFrom` to the connectors the goal can be reached from;
empty means all of them.

```json
"itemSlots": [ { "id": "s1", "x": 40, "y": 0, "z": 24, "reachableFrom": ["A", "B"], "requires": [] } ],
"goals":     [ { "item": "PLANS", "x": 20, "y": 0, "z": 20, "reachableFrom": [] } ]
```

## 8. INF logic

Elevators, switches and triggers work as in any level, within the room (spec §7):

- Everything an INF item names or sends to must be a sector **in this room** (I1). INF sector names are at most
  **12 characters** (I2), because the generator adds a prefix like `R3_` to every name.
- Don't send `complete`, touch the `complete` elevator, use `key:`, or write `item: level` (I3, I4, I7): those are
  level-wide and belong to the generator.
- No INF on stubs or adapters (C7).
- The generator rewrites positions, angles and absolute elevator stops when it moves the room (spec §7, *What the
  generator rewrites*). Relative stops (`@8`) are the safest choice when you have one.

## 9. Palettes and textures

Every room in one generated level must use the same palette. The generator compares palette *contents*, so
the eight levels that share the Imperial palette (SECBASE, TESTBASE, DTENTION, RAMSHED, IMPCITY, FUELSTAT,
EXECUTOR, ARC) can all be mixed. Use textures that exist in the game's `TEXTURES.GOB`. The generator merges
texture tables, so you can use any you like.

Floor and ceiling textures don't turn with the room (G11), and stay aligned only because rooms move in 8-unit
steps. Avoid floor textures with an obvious direction, or set `"rotatable": false`.

## 10. When the validator complains

| Rule | Usually means | Fix |
|---|---|---|
| C2, C3 | Stub isn't an 8×4 rectangle, or the portal isn't on the 8-grid | Move the stub's vertices; snap the portal to multiples of 8 |
| C4, C5 | The portal is adjoined, or a side wall is | Unadjoin the portal; make the side walls solid |
| C6 | Stub ceiling isn't 8 above its floor | Set ceiling = floor − 8 |
| C10, M1 | `room.json` disagrees with the stubs | Run `room metadata --write` |
| G1 | A sector winds counter-clockwise or isn't closed | Fix in the editor (reverse the sector) |
| G2 | An adjoin doesn't point back | Re-adjoin the two walls in the editor |
| G5 | Something sticks out past a portal | Move the stub out to the room's edge |
| G7 (warning) | A low ceiling, or an opening you must crouch through | Raise the ceiling, or ignore it if the crawlspace is intended |
| O1 | More than one player object | Keep one (it's the start point, §7.1) |
| O2 | A key, gating item or key carrier in `ROOM.O` | Delete it; add an `itemSlots` spot instead |
| M8 | A goal is declared but the object is gone, or the other way round | Run `room metadata --write` |
| I1 | INF talks to a sector outside the room | Keep logic within the room |

## 11. The example room

`rooms/examples/example-junction` is a 40 × 32 room with three doorways:

- **A (west)** and **C (north)**: standard stubs with flag doors.
- **B (east)**: a 16-wide opening narrowed by adapter `CA_B`, with no door, so joins there are open archways.
- A stormtrooper and a medkit in `ROOM.O`.
- Traversal written by hand: one open floor, every doorway reaches every other.

It was made with:

```
dftool room init rooms/examples/example-junction --connectors W,E16,N --size 40x32 --name "Example junction" --author DfRando
#   added ROOM.O; removed connector B's "door"; wrote the traversal notes
dftool room metadata rooms/examples/example-junction --write
```

## 12. Sharing rooms

Rooms you build yourself are your own work, so share the folder. Rooms cut from the stock levels contain
LucasArts geometry. Don't share those files: share the selection (level and sector numbers, as in
`rooms/poc-selection.json`), and `tools/room-extractor` rebuilds them from each user's own `DARK.GOB`.
