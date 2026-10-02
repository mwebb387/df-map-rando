# Randomizer Knobs

**Status:** living document. Knobs are added as features land.

A **knob** is a user-facing setting that changes what the randomizer produces. Configurability is a goal of the
whole project, so each generator feature is designed around its knobs from the start. They are not added afterwards.

## Principles

1. **No hidden choices.** Every decision the generator makes either comes from a knob or is derived from knobs and
   the seed. If a feature needs a constant, first ask whether players would want to change it. If they would, make it a knob.
2. **Deterministic.** The same seed, knob values and room library always produce a byte-identical GOB.
3. **Typed and self-describing.** Every knob is declared once in the core (`DarkForces.Core`), with a name, type,
   default, allowed range, description, and the version it was added in. The CLI, any future UI, and the
   documentation all read that one declaration.
4. **Portable.** Knob values save to a JSON *preset*. The seed plus the preset form a shareable *settings string*,
   and the string is embedded in the generated GOB so any generated level can be reproduced.
5. **Validated together.** Invalid combinations are rejected before generation, with a message naming the knobs
   involved. For example, more locked doors than connectors, or gating items enabled when no room in the pool needs them.
6. **Versioned.** Presets record the knob schema version. A preset from an older version loads with defaults for
   knobs it doesn't mention.

## Shape

Knobs are properties on `RandomizerSettings` (`src/DarkForces.Core/Knobs`), each marked with a `[Knob]` attribute
that holds its description and range. `KnobCatalog` reads those declarations to list, set, validate and serialize knobs.
To add a knob, add a property with `[Knob(...)]` to a section class, use it in the generator, and add a row below.

```jsonc
// dftool knobs --json   (the default preset)
{
  "schema": 1,
  "seed": 0,
  "layout": { "roomCount": 3, "allowRotation": true, "allowRepeats": true, "attempts": 200 },
  "output": { "levelSlot": "SECBASE", "levelTitle": "Randomized", "zip": true, "embedSettings": true }
}
```

In the CLI:
- `dftool knobs` lists every knob with its type, default and range.
- `--preset <file.json>` loads a preset.
- `--seed <n|text>` sets the seed. Text is hashed, so `--seed kyle` works.
- `--set layout.roomCount=5` overrides a single knob, and can be repeated.

Planned sections (progression, difficulty) keep the shape shown in the catalog below.

## Catalog

Status: **planned**, **PoC** (needed for the proof of concept), or **done**.

### Layout

| Knob | Type | Default | Effect | Status |
|---|---|---|---|---|
| `layout.roomCount` | int | 3 | Number of room instances placed (1–64). | **done** |
| `layout.sourceLevels` | list | `["SECBASE"]` | Which levels' rooms are in the pool (palette permitting). | PoC |
| `layout.roomTags` | include/exclude lists | — | Filter the room pool by `room.json` tags. | planned |
| `layout.allowRotation` | bool | true | Allow 90° rotations (rooms with `rotatable: false` are never rotated). | **done** |
| `layout.allowRepeats` | bool | true | Allow the same room to be placed more than once. | **done** |
| `layout.attempts` | int | 200 | Layout attempts before giving up; each restarts from an empty level (1–100000). | **done** |
| `layout.hallways` | bool | true | Generate hallways: always when no room can join a doorway directly, and sometimes otherwise (`hallwayChance`). Hallways don't count toward `roomCount`, and never hold keys or the exit. | **done** |
| `layout.hallwayChance` | number | 0.3 | Chance (0–1) of a hallway between two rooms even when they could join directly. | **done** |
| `layout.hallwayMinLength` | int | 16 | Shortest hallway leg in DF units (rounded up to the 8-unit grid; stairs may need more). | **done** |
| `layout.hallwayMaxLength` | int | 48 | Longest hallway leg in DF units (rounded down to the grid). | **done** |
| `layout.hallwayMaxRise` | int | 8 | Largest floor change across a hallway, built as stairs of at most 3 units per step. | **done** |
| `layout.hallwayTurns` | bool | true | Allow L-shaped hallways, which connect doorways facing at right angles. | **done** |
| `layout.deadEnds` | bool | true | Allow rooms with only one used connector. | planned |
| `layout.maxBounds` | size | 1024×1024 | Overall level footprint limit. | planned |

### Progression

| Knob | Type | Default | Effect | Status |
|---|---|---|---|---|
| `progression.exit` | bool | true | Add a level exit: an unused doorway alcove, lit up, that completes the mission when entered. If every doorway is used, a plain room sector is used instead. | **done** |
| `progression.exitPlacement` | `far` / `random` | `far` | The room farthest from the start (by room hops), or any room other than the start's. | **done** |
| `progression.lockedDoors` | bool | true | Lock some joined doors with keys. Only joins that have a door can be locked. | **done** |
| `progression.lockedDoorsMin` | int | 1 | Fewest locked doors (0–32, capped by joins with a door; the cap is logged). | **done** |
| `progression.lockedDoorsMax` | int | 3 | Most locked doors (0–32). | **done** |
| `progression.keyColors` | list | `RED,BLUE,YELLOW` | Colors that may be used. With more locks than colors, colors repeat, and one key opens every door of its color. | **done** |
| `progression.keyPlacement` | `anywhere` / `far` | `anywhere` | Any reachable spot, or the reachable room farthest from the start. | **done** |
| `progression.gatingItems` | set of CLEATS/MASK/GOGGLES | none | Which items the logic may require, using the `traversal` requirements in rooms. | planned |
| `progression.goals` | bool | true | Keep the goal items rooms declare (room.json `goals`, e.g. the Death Star plans) as mission objectives: the mission ends once every goal is taken and the exit reached, in any order. A repeated room keeps its goal only once. Off removes them. | **done** |
| `progression.goalPlacement` | `room` / … | `room` | Moving goal items to other spots (item slots, far rooms). | planned |
| `progression.startRoom` | `random` / room id | `random` | Where the player starts (from rooms with `startPoints`). | planned |

**Guarantee:** every generated level can be finished. Keys are only placed where the player can already get
without them. After planning, a solver simulates play (collect every reachable key, repeat) and generation fails
if the exit or a goal item can't be reached. Reachability uses each room's `traversal` edges, so a requirement a room declares is respected.

### Difficulty

| Knob | Type | Default | Effect | Status |
|---|---|---|---|---|
| `difficulty.enemyDensity` | multiplier | 1.0 | Scale authored enemy counts (remove or duplicate within rooms). | planned |
| `difficulty.enemyShuffle` | `off` / `sameTier` / `any` | `off` | Replace enemy logics, e.g. stormtrooper ↔ commando within a tier. | planned |
| `difficulty.pickupDensity` | multiplier | 1.0 | Scale ammo, health and shield pickups. | planned |
| `difficulty.extraLives` | int | per room | Remove or add `LIFE` pickups. | planned |
| `difficulty.hazards` | bool | true | Allow rooms with damaging sectors or crushers. | planned |
| `difficulty.gameDifficulty` | easy/medium/hard/all | all | Keep objects by their DIFF value, or rewrite DIFF. | planned |

### Output

| Knob | Type | Default | Effect | Status |
|---|---|---|---|---|
| `output.levelSlot` | level name | `SECBASE` | Which mission the generated level replaces in `JEDI.LVL`. | **done** |
| `output.levelTitle` | string | `Randomized` | Mission-menu name (commas are removed; JEDI.LVL is comma-separated). | **done** |
| `output.embedSettings` | bool | true | Store the seed and every knob in the GOB as `RANDO.TXT`. | **done** |
| `output.zip` | bool | true | Also write `<name>.zip` containing the GOB (The Force Engine loads mods from zip files). | **done** |

### Validation

| Knob | Type | Default | Effect | Status |
|---|---|---|---|---|
| `validation.strict` | bool | false | Treat room-spec warnings as errors when loading rooms. | planned |
