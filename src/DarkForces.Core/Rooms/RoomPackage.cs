using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Text;

namespace DarkForces.Core.Rooms;

/// <summary>
/// A room package folder: <c>room.json</c>, <c>ROOM.LEV</c>, and optionally <c>ROOM.O</c> / <c>ROOM.INF</c>
/// (docs/room-spec.md §3). Missing O/INF files load as empty.
/// </summary>
public sealed class RoomPackage(RoomMetadata metadata, LevFile lev, ObjFile objects, InfFile inf)
{
    public const string MetadataFile = "room.json";
    public const string LevFileName = "ROOM.LEV";
    public const string ObjFileName = "ROOM.O";
    public const string InfFileName = "ROOM.INF";

    public RoomMetadata Metadata { get; } = metadata;
    public LevFile Lev { get; } = lev;
    public ObjFile Objects { get; } = objects;
    public InfFile Inf { get; } = inf;

    public static RoomPackage Load(string directory)
    {
        var (meta, lev, objects, inf) = LoadParts(directory);
        if (meta == null)
            throw new FileNotFoundException($"{Path.Combine(directory, MetadataFile)} not found (dftool room metadata <dir> --write creates it)");
        return new RoomPackage(meta, lev, objects, inf);
    }

    /// <summary>Loads the package files; <c>room.json</c> may be missing (an author's room before its metadata exists).</summary>
    public static (RoomMetadata? Metadata, LevFile Lev, ObjFile Objects, InfFile Inf) LoadParts(string directory)
    {
        string P(string f) => Path.Combine(directory, f);
        string Text(string f) => DfText.Decode(File.ReadAllBytes(P(f)));

        if (!File.Exists(P(LevFileName)))
            throw new FileNotFoundException($"{P(LevFileName)} not found (dftool room init <dir> creates a starter room)");
        var meta = File.Exists(P(MetadataFile)) ? RoomMetadata.Parse(File.ReadAllText(P(MetadataFile))) : null;
        var lev = LevFile.Parse(Text(LevFileName));
        var objects = File.Exists(P(ObjFileName)) ? ObjFile.Parse(Text(ObjFileName)) : new ObjFile();
        var inf = File.Exists(P(InfFileName)) ? InfFile.Parse(Text(InfFileName)) : new InfFile();
        return (meta, lev, objects, inf);
    }

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        string P(string f) => Path.Combine(directory, f);
        File.WriteAllText(P(MetadataFile), Metadata.ToJson());
        File.WriteAllBytes(P(LevFileName), DfText.Encode(Lev.Write()));
        File.WriteAllBytes(P(ObjFileName), DfText.Encode(Objects.Write()));
        if (Inf.Items.Count > 0)
            File.WriteAllBytes(P(InfFileName), DfText.Encode(Inf.Write()));
        else if (File.Exists(P(InfFileName)))
            File.Delete(P(InfFileName));
    }

    public int SectorIndex(string name) =>
        Lev.Sectors.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
