using System.Text;
using DarkForces.Core.Gob;
using DarkForces.Core.Gol;
using DarkForces.Core.Knobs;
using DarkForces.Core.Lvl;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;
using DarkForces.Core.Text;

namespace DarkForces.Core.Generation;

public sealed record GeneratedLevel(GobArchive Gob, LevelLayout Layout, ProgressionPlan Progression, List<string> Log);

/// <summary>
/// The randomizer: settings + seed + room library + the game's DARK.GOB -> a user GOB replacing one mission.
/// Deterministic: the same inputs always produce the same bytes.
/// </summary>
public static class Generator
{
    public const string SettingsFile = "RANDO.TXT";

    public static GeneratedLevel Generate(RandomizerSettings settings, IReadOnlyList<RoomPackage> rooms, GobArchive game)
    {
        var errors = KnobCatalog.Validate(settings);
        if (errors.Count > 0)
            throw new ArgumentException("invalid settings:\n  " + string.Join("\n  ", errors));

        var log = new List<string>();
        var pool = new List<RoomPackage>();
        foreach (var room in rooms)
        {
            var roomErrors = RoomValidator.Validate(room).Where(f => f.Severity == Severity.Error).ToList();
            if (roomErrors.Count > 0)
                log.Add($"skipped room {room.Metadata.Id}: {roomErrors.Count} validation error(s), first: {roomErrors[0]}");
            else
                pool.Add(room);
        }
        if (pool.Count == 0)
            throw new InvalidOperationException("no valid rooms to place");
        // A level has one palette. Stock levels share palettes under different names (ARC.PAL is SECBASE.PAL),
        // so rooms are compared by the palette and colormap contents, not by name.
        var palettes = pool.GroupBy(r => PaletteKey(r.Lev.Palette, game)).ToList();
        if (palettes.Count > 1)
            throw new InvalidOperationException("rooms use different palettes (" +
                string.Join("; ", palettes.Select(g => string.Join(", ", g.Select(r => r.Lev.Palette.ToUpperInvariant()).Distinct()))) +
                "); a level has one palette");

        // Rooms are considered in id order so the result never depends on directory enumeration order.
        pool.Sort((a, b) => string.CompareOrdinal(a.Metadata.Id, b.Metadata.Id));

        var slot = settings.Output.LevelSlot.ToUpperInvariant();
        var lvl = JediLvl.Parse(DfText.Decode(game.Get("JEDI.LVL")));
        var entry = lvl.Levels.FindIndex(l => l.Name.Equals(slot, StringComparison.OrdinalIgnoreCase));
        if (entry < 0)
            throw new ArgumentException($"output.levelSlot '{slot}' is not a mission in JEDI.LVL ({string.Join(", ", lvl.Levels.Select(l => l.Name))})");

        var rng = new SeededRandom(settings.Seed);
        var layout = new LayoutGenerator(pool, settings.Layout, rng).Generate();
        foreach (var inst in layout.Instances)
        {
            var at = $"rotated {inst.Placement.Turns * 90} at ({inst.Placement.Dx}, {inst.Placement.Dy}, {inst.Placement.Dz}) as {inst.Prefix}*";
            if (HallwayBuilder.IsHallway(inst.Source))
            {
                var m = inst.Source.Metadata;
                var rise = m.Connectors.Single(c => c.Id == "B").Floor;
                log.Add($"hallway: {m.Name}, {m.Bounds.MaxX - m.Bounds.MinX}x{m.Bounds.MaxZ - m.Bounds.MinZ}, rise {rise}, {at}");
            }
            else
            {
                log.Add($"room: {inst.Source.Metadata.Id} {at}");
            }
        }

        var plan = new ProgressionPlanner(layout, settings.Progression, rng).Plan();
        log.AddRange(plan.Log);

        var merged = LevelMerger.Merge(layout, slot, plan);
        log.AddRange(merged.Log);

        var gob = new GobArchive();
        gob.Put($"{slot}.LEV", DfText.Encode(merged.Lev.Write()));
        gob.Put($"{slot}.O", DfText.Encode(merged.Objects.Write()));
        gob.Put($"{slot}.INF", DfText.Encode(merged.Inf.Write()));
        var gol = new GolFile();
        foreach (var g in plan.Goals)
            gol.Goals.Add(new Goal(gol.Goals.Count, GoalKind.Item, LogicCatalog.GoalNumbers[g.Item]));
        if (plan.Exit != null)
            gol.Goals.Add(new Goal(gol.Goals.Count, GoalKind.Trig, LevelMerger.ExitGoalTrigger));
        gob.Put($"{slot}.GOL", DfText.Encode(gol.Write()));
        var cmp = Path.ChangeExtension(merged.Lev.Palette, ".CMP");
        if (game.Find(cmp) is { } cmpEntry)
            gob.Put($"{slot}.CMP", cmpEntry.Data);
        else
            log.Add($"warning: {cmp} not found in the game GOB; the mission keeps its own colormap");

        // Mission-menu title: JEDI.LVL is comma-separated, so commas can't appear in it.
        var title = settings.Output.LevelTitle.Replace(",", " ").Trim();
        lvl.Levels[entry] = lvl.Levels[entry] with { Description = title.Length > 0 ? title : lvl.Levels[entry].Description };
        gob.Put("JEDI.LVL", DfText.Encode(lvl.Write()));

        if (settings.Output.EmbedSettings)
            gob.Put(SettingsFile, Encoding.UTF8.GetBytes(SettingsText(settings, layout)));

        return new GeneratedLevel(gob, layout, plan, log);
    }

    /// <summary>The palette's identity: its PAL and CMP bytes from the game GOB, or its name when they aren't there.</summary>
    static string PaletteKey(string palette, GobArchive game)
    {
        var pal = game.Find(palette)?.Data;
        if (pal == null) return palette.ToUpperInvariant();
        var cmp = game.Find(Path.ChangeExtension(palette, ".CMP"))?.Data ?? [];
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([.. pal, .. cmp]));
    }

    /// <summary>RANDO.TXT: enough to reproduce the level (seed, every knob, the rooms used).</summary>
    static string SettingsText(RandomizerSettings s, LevelLayout layout)
    {
        var sb = new StringBuilder();
        sb.Append("DfRando generated level").Append(DfText.NewLine);
        sb.Append("settings: ").Append(KnobCatalog.SettingsString(s)).Append(DfText.NewLine);
        sb.Append("rooms: ").Append(string.Join(", ", layout.Instances.Where(i => !HallwayBuilder.IsHallway(i.Source))
            .Select(i => i.Source.Metadata.Id).Distinct().Order()))
          .Append(DfText.NewLine);
        return sb.ToString();
    }

    /// <summary>Loads room packages: each path is a package (contains room.json) or a folder of packages.</summary>
    public static List<RoomPackage> LoadRooms(IEnumerable<string> paths)
    {
        var rooms = new List<RoomPackage>();
        foreach (var p in paths)
        {
            if (File.Exists(Path.Combine(p, RoomPackage.MetadataFile)))
                rooms.Add(RoomPackage.Load(p));
            else
                rooms.AddRange(Directory.GetDirectories(p).Order(StringComparer.Ordinal)
                    .Where(d => File.Exists(Path.Combine(d, RoomPackage.MetadataFile)))
                    .Select(RoomPackage.Load));
        }
        return rooms;
    }
}
