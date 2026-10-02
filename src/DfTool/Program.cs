using DarkForces.Core.Authoring;
using DarkForces.Core.Generation;
using DarkForces.Core.Gob;
using DarkForces.Core.Knobs;
using DarkForces.Core.Output;
using DarkForces.Core.Rooms;
using DarkForces.Core.Verification;

try
{
    return args switch
    {
        ["gob", "list", var gob] => GobList(gob),
        ["gob", "extract", var gob, var dir] => GobExtract(gob, dir),
        ["gob", "pack", var dir, var gob] => GobPack(dir, gob),
        ["roundtrip", .. var paths] when paths.Length > 0 => RoundTripCheck(paths),
        ["room", "init", var dir, .. var rest] => RoomInit(dir, rest),
        ["room", "validate", .. var dirs] when dirs.Length > 0 => RoomValidate(dirs),
        ["room", "metadata", var dir, .. var rest] => RoomMetadataCommand(dir, rest.Contains("--write")),
        ["room", ..] => RoomUsage(),
        ["knobs", .. var rest] => Knobs(rest.Contains("--json")),
        ["generate", .. var rest] => Generate(rest),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or IOException or NotSupportedException)
{
    // Expected failures (bad knobs, impossible layouts, unreadable files) are reported, not dumped.
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          dftool gob list <file.gob>
          dftool gob extract <file.gob> <dir>
          dftool gob pack <dir> <file.gob>        packs every file in <dir> (sorted by name)
          dftool roundtrip <path>...              verify GOB/LEV/O/INF/GOL/JEDI.LVL read+write fidelity
                                                  (files or directories, searched recursively)
          dftool room ...                         room authoring: init, validate, metadata (dftool room for details)
          dftool knobs [--json]                   list every knob (or print the default preset as JSON)
          dftool generate --gob <DARK.GOB> --rooms <dir> [--rooms <dir>...] --out <NAME.GOB>
                          [--preset <file.json>] [--seed <n|text>] [--set knob=value]...
                                                  build a randomized level; <dir> is a room package or a folder of them
        """);
    return 1;
}

static int GobList(string path)
{
    var gob = GobArchive.Load(path);
    foreach (var e in gob.Entries)
        Console.WriteLine($"{e.Name,-13}{e.Data.Length,10:N0}");
    Console.WriteLine($"{gob.Entries.Count} files");
    return 0;
}

static int GobExtract(string path, string dir)
{
    var gob = GobArchive.Load(path);
    gob.ExtractTo(dir);
    Console.WriteLine($"{gob.Entries.Count} files -> {dir}");
    return 0;
}

static int GobPack(string dir, string path)
{
    var gob = GobArchive.FromDirectory(dir);
    gob.Save(path);
    Console.WriteLine($"{gob.Entries.Count} files -> {path}");
    return 0;
}

static int RoundTripCheck(string[] paths)
{
    int total = 0, failures = 0;
    foreach (var r in RoundTrip.CheckPaths(paths))
    {
        total++;
        if (r.Status != RoundTripStatus.Ok) failures++;
        Console.WriteLine(r);
    }
    Console.WriteLine($"{total - failures}/{total} ok");
    return failures == 0 ? 0 : 2;
}

static int RoomUsage()
{
    Console.Error.WriteLine("""
        room authoring (docs/authoring-rooms.md):
          dftool room init <dir> [--id <id>] [--name <text>] [--author <text>]
                               [--size <width>x<depth>] [--height <h>] [--connectors W,E16,...]
                               [--palette <PAL>] [--wall <BM>] [--floor <BM>] [--ceiling <BM>]
                               [--seal <BM>] [--frame <BM>] [--ambient <0-31>] [--no-doors] [--force]
                    create a starter room package that already validates: one rectangular room
                    with a doorway stub centered on each listed wall (default W,E; a number after
                    the side, e.g. E16, makes a wider opening with an adapter). Default size 32x32x16.
          dftool room validate <dir>...
                    check room packages against docs/room-spec.md
          dftool room metadata <dir> [--write]
                    build room.json from ROOM.LEV/.O/.INF: connectors, bounds, resources and goals are
                    derived; id, name, doors, traversal, item slots, start points and tags are kept from
                    the existing room.json. Prints the JSON, or with --write saves it (only if the room
                    is then valid).
        """);
    return 1;
}

static int RoomInit(string dir, string[] a)
{
    string? Opt(string name) => Array.IndexOf(a, name) is var i and >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    int Int(string name, int fallback) => Opt(name) is { } v
        ? int.TryParse(v, out var n) ? n : throw new ArgumentException($"{name} '{v}' is not a whole number")
        : fallback;

    if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() && !a.Contains("--force"))
        throw new IOException($"{dir} is not empty (use --force to overwrite its room files)");

    var defaults = new ScaffoldOptions();
    var (width, depth) = (defaults.Width, defaults.Depth);
    if (Opt("--size") is { } size)
    {
        var parts = size.ToLowerInvariant().Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out depth))
            throw new ArgumentException($"--size '{size}' must look like 32x24 (width along X, depth along Z)");
    }
    var options = defaults with
    {
        Id = Opt("--id") ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir))).ToLowerInvariant(),
        Name = Opt("--name") ?? "",
        Author = Opt("--author") ?? "",
        Width = width,
        Depth = depth,
        Height = Int("--height", defaults.Height),
        Connectors = Opt("--connectors") is { } list ? [.. list.Split(',').Select(ConnectorSpec.Parse)] : defaults.Connectors,
        Palette = Opt("--palette") ?? defaults.Palette,
        Wall = Opt("--wall") ?? defaults.Wall,
        Floor = Opt("--floor") ?? defaults.Floor,
        Ceiling = Opt("--ceiling") ?? defaults.Ceiling,
        Seal = Opt("--seal") ?? defaults.Seal,
        Frame = Opt("--frame") ?? defaults.Frame,
        Ambient = Int("--ambient", defaults.Ambient),
        Doors = !a.Contains("--no-doors"),
    };

    var room = RoomScaffold.Build(options);
    room.Save(dir);
    var findings = RoomValidator.Validate(RoomPackage.Load(dir));
    Console.WriteLine($"created {dir}: room '{room.Metadata.Id}', {room.Lev.Sectors.Count} sectors, connectors " +
                      string.Join(", ", room.Metadata.Connectors.Select(c => $"{c.Id} ({c.Facing})")));
    foreach (var f in findings)
        Console.WriteLine($"    {f}");
    Console.WriteLine("next: edit ROOM.LEV in a DF editor, then run 'dftool room metadata <dir> --write' and 'dftool room validate <dir>'");
    return findings.Any(f => f.Severity == Severity.Error) ? 2 : 0;
}

static int RoomMetadataCommand(string dir, bool write)
{
    var result = RoomMetadataBuilder.Build(dir);
    var log = write ? Console.Out : Console.Error; // without --write, stdout carries only the JSON
    foreach (var c in result.Changes)
        log.WriteLine($"  {c}");
    foreach (var f in result.Findings)
        log.WriteLine($"    {f}");

    if (!write)
    {
        Console.WriteLine(result.Metadata.ToJson());
        return result.IsValid ? 0 : 2;
    }
    if (!result.IsValid)
    {
        log.WriteLine($"not written: fix the errors above, then run again");
        return 2;
    }
    File.WriteAllText(Path.Combine(dir, RoomPackage.MetadataFile), result.Metadata.ToJson());
    log.WriteLine($"-> {Path.Combine(dir, RoomPackage.MetadataFile)}" + (result.Changes.Count == 0 ? " (no changes)" : ""));
    return 0;
}

static int RoomValidate(string[] dirs)
{
    var errors = 0;
    foreach (var dir in dirs)
    {
        var findings = RoomValidator.Validate(RoomPackage.Load(dir));
        var e = findings.Count(f => f.Severity == Severity.Error);
        errors += e;
        Console.WriteLine($"{(e == 0 ? "valid  " : "INVALID")} {dir}  ({e} errors, {findings.Count - e} warnings)");
        foreach (var f in findings)
            Console.WriteLine($"    {f}");
    }
    return errors == 0 ? 0 : 2;
}

static int Knobs(bool json)
{
    if (json)
    {
        Console.WriteLine(KnobCatalog.ToJson(new RandomizerSettings()));
        return 0;
    }
    Console.WriteLine($"{"knob",-26} {"type",-7} {"default",-12} {"range",-10} description");
    foreach (var k in KnobCatalog.All)
        Console.WriteLine($"{k.Path,-26} {k.TypeName,-7} {(k.Default is bool b ? (b ? "true" : "false") : Convert.ToString(k.Default, System.Globalization.CultureInfo.InvariantCulture)),-12} {k.Range,-10} {k.Declaration.Description}");
    Console.WriteLine($"{"seed",-26} {"uint64",-7} {"0",-12} {"",-10} Random seed; any text is hashed to a number.");
    return 0;
}

static int Generate(string[] a)
{
    string? Opt(string name) => Array.IndexOf(a, name) is var i and >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    IEnumerable<string> All(string name) => a.Select((x, i) => (x, i)).Where(p => p.x == name && p.i + 1 < a.Length).Select(p => a[p.i + 1]);

    var gobPath = Opt("--gob");
    var outPath = Opt("--out");
    var roomDirs = All("--rooms").ToList();
    if (gobPath == null || outPath == null || roomDirs.Count == 0)
        return Usage();

    var settings = Opt("--preset") is { } preset ? KnobCatalog.LoadPreset(File.ReadAllText(preset)) : new RandomizerSettings();
    if (Opt("--seed") is { } seed) settings.Seed = KnobCatalog.ParseSeed(seed);
    foreach (var assignment in All("--set"))
        KnobCatalog.Set(settings, assignment);

    if (Path.GetFileNameWithoutExtension(outPath).Length > 8)
        Console.Error.WriteLine($"warning: {Path.GetFileName(outPath)} is longer than 8.3; DOS Dark Forces needs short names");

    var result = Generator.Generate(settings, Generator.LoadRooms(roomDirs), GobArchive.Load(gobPath));
    foreach (var line in result.Log)
        Console.WriteLine(line);
    foreach (var path in OutputWriter.Write(result.Gob, outPath, settings.Output.Zip))
        Console.WriteLine($"-> {path}");
    Console.WriteLine($"seed {settings.Seed}; replaces mission {settings.Output.LevelSlot.ToUpperInvariant()}");
    return 0;
}
