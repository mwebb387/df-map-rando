# DfRando — Dark Forces map randomizer (proof of concept)

## Layout

| Path | What |
|---|---|
| `src/DarkForces.Core` | Reusable core library: every format, codec and check. All logic lives here. |
| `src/DfTool` | Thin CLI over the core (argument parsing and output only). |
| `tests/DarkForces.Core.Tests` | xUnit tests, run against the WDFUSE demo GOBs and `Df_specs.hlp`, read directly from `w1632.zip`. |
| `docs/room-spec.md` | Room specification draft: rules a room must meet to be randomized. |
| `docs/authoring-rooms.md` | Guide for building your own rooms with `dftool room init / metadata / validate`. |
| `rooms/examples/` | Original example rooms (committed; no LucasArts data). |
| `docs/knobs.md` | Randomizer knobs (settings): principles and catalog. A project-wide goal. |
| `tools/room-candidates/` | Python helper for choosing rooms (map and candidate report). Not part of the core. |
| `tools/room-extractor/` | Cuts selected rooms out of stock levels into room packages, and builds playtest GOBs. Uses the core. |
| `rooms/poc-selection.json`, `rooms/secbase-selection.json` | Chosen rooms per source level (sector numbers, doorways, optional rotation). |
| `rooms/stock/` | Room packages extracted from your `DARK.GOB` (git-ignored: LucasArts geometry). |
| `out/` | Generated levels (git-ignored). |
| `docs/reference/` | Decompiled `Df_specs.hlp` (DF Unofficial Specs 3.01) and the WDFUSE tutorial. |
| `tools/hlp-decompiler/` | Standalone WinHelp `.HLP` to markdown utility (produced `docs/reference/`). Not part of the core. |

### Core namespaces

| Namespace | Contents |
|---|---|
| `DarkForces.Core.Gob` | GOB archive read/write (byte-identical), extract, pack. |
| `DarkForces.Core.Lev` | LEV geometry: header, textures, sectors, vertices, walls. |
| `DarkForces.Core.Objects` | O files: resource tables, objects, SEQ logic. |
| `DarkForces.Core.Inf` | INF items and statements, grouped by class. |
| `DarkForces.Core.Gol` / `.Lvl` | GOL goals, JEDI.LVL mission list. |
| `DarkForces.Core.Geometry` | Sector geometry: winding, containment, bounds, facings, convexity. |
| `DarkForces.Core.Rooms` | Room packages (`room.json` + LEV/O/INF), room-spec rules, the validator, and the room transform (rotate, move, rename). |
| `DarkForces.Core.Authoring` | Room authoring: starter-room scaffold (`room init`) and `room.json` builder (`room metadata`). |
| `DarkForces.Core.Knobs` | Randomizer settings: `[Knob]`-declared properties, presets, `--set` overrides, validation. |
| `DarkForces.Core.Generation` | Seeded RNG, layout generator (direct joins and generated hallways), hallway builder, progression planner and solver, level merger (joins, doors, locks, keys, exit), and the `Generator` entry point. |
| `DarkForces.Core.Output` | Writes the finished GOB and its zip. |
| `DarkForces.Core.Text` | Shared text handling (comments, DOS EOF, invariant number formatting). |
| `DarkForces.Core.Verification` | Round-trip fidelity checks. |

## CLI

```
dotnet run --project src/DfTool -- gob list <file.gob>
dotnet run --project src/DfTool -- gob extract <file.gob> <dir>
dotnet run --project src/DfTool -- gob pack <dir> <file.gob>
dotnet run --project src/DfTool -- roundtrip <path>...      # GOBs / LEV / O / INF / GOL / JEDI.LVL, dirs recursive
dotnet run --project src/DfTool -- room init <dir> [--connectors W,E16] [--size 32x32] ...  # starter room that validates
dotnet run --project src/DfTool -- room metadata <dir> [--write]  # build room.json from the room files, keeping authored fields
dotnet run --project src/DfTool -- room validate <room dir>...   # check room packages against docs/room-spec.md
dotnet run --project src/DfTool -- knobs [--json]                # list knobs / print the default preset
dotnet run --project src/DfTool -- generate --gob gamedata/DARK.GOB --rooms rooms/stock --out out/RANDO.GOB \
    [--preset p.json] [--seed <n|text>] [--set layout.roomCount=5]...  # writes out/RANDO.GOB and out/RANDO.zip
```

## Round-trip guarantee

`roundtrip` passes a file only if:
- GOBs rewrite **byte-identically**.
- Text files parse, rewrite **stably** (writing twice gives the same output), and keep the source's
  comment-free **token stream** (numbers compared by value, case-insensitive).

Known normalizations the writer applies, which the check allows for:
- Comments are dropped.
- `SECTOR n` labels are renumbered by position. The engine ignores them, and ADJOIN uses position.
- The INF `items n` count is rewritten to the real number of items. Four stock levels ship with a stale count:
  SECBASE 60→58, IMPCITY 146→142, TALAY 65→62 (two items commented out), GROMAS 106→105.
- Junk after the last element is ignored and reported, e.g. the stray byte WDFUSE 2.10 appends to every GOB entry.

To validate against the original game, run on the machine that has Dark Forces installed:

```
dotnet run --project src/DfTool -- roundtrip "<DF install dir>"
```
