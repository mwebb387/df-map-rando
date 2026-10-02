using System.Globalization;
using DarkForces.Core.Geometry;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;

namespace DarkForces.Core.Rooms;

/// <summary>
/// Where one room instance goes: rotate clockwise by <see cref="QuarterTurns"/> x 90 degrees about the room's
/// local origin, then translate. X/Z translations must be multiples of the placement grid (docs/room-spec.md §2).
/// </summary>
public readonly record struct Placement(int QuarterTurns, double Dx, double Dy, double Dz)
{
    public int Turns => ((QuarterTurns % 4) + 4) % 4;

    public Vertex Apply(Vertex v)
    {
        var (x, z) = Turns switch
        {
            0 => (v.X, v.Z),
            1 => (v.Z, -v.X),   // north -> east
            2 => (-v.X, -v.Z),
            _ => (-v.Z, v.X),
        };
        return new Vertex(x + Dx, z + Dz);
    }

    public Facing Apply(Facing f) => (Facing)(((int)f + Turns) % 4);

    /// <summary>Yaw/angle in degrees, clockwise from north.</summary>
    public double ApplyAngle(double degrees) => ((degrees + 90 * Turns) % 360 + 360) % 360;
}

/// <summary>A room after <see cref="RoomTransformer.Apply"/>: world-space files with prefixed sector names.</summary>
public sealed record RoomInstance(RoomPackage Source, Placement Placement, string Prefix, LevFile Lev, ObjFile Objects, InfFile Inf)
{
    public string Rename(string sectorName) => sectorName.Length == 0 ? "" : Prefix + sectorName;
}

/// <summary>Applies a <see cref="Placement"/> and a name prefix to a room (the rewrite table in docs/room-spec.md §7).</summary>
public static class RoomTransformer
{
    /// <summary>Elevator classes whose stops are floor/ceiling altitudes (sign-flipped from LEV); absolute stops shift with the room.</summary>
    static readonly HashSet<string> AltitudeElevators = new(StringComparer.OrdinalIgnoreCase)
        { "move_floor", "move_ceiling", "move_fc", "basic", "inv", "basic_auto" };

    /// <summary>Elevator classes whose <c>angle:</c> is a world direction (0 = north), so it rotates with the room.</summary>
    public static readonly IReadOnlySet<string> WorldAngleElevators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "scroll_floor", "scroll_ceiling", "morph_move1", "morph_move2", "move_wall", "move_offset" };

    public static RoomInstance Apply(RoomPackage room, Placement p, string prefix)
    {
        if (!Geo.OnGrid(p.Dx, RoomRules.Grid) || !Geo.OnGrid(p.Dz, RoomRules.Grid))
            throw new ArgumentException($"X/Z translation ({p.Dx},{p.Dz}) must be a multiple of {RoomRules.Grid}");
        if (p.Turns != 0 && !room.Metadata.Rotatable)
            throw new ArgumentException($"room {room.Metadata.Id} is not rotatable");

        string Rename(string n) => n.Length == 0 ? "" : prefix + n;

        // ---- geometry
        var lev = new LevFile
        {
            Version = room.Lev.Version, LevelName = room.Lev.LevelName, Palette = room.Lev.Palette, Music = room.Lev.Music,
            ParallaxX = room.Lev.ParallaxX, ParallaxY = room.Lev.ParallaxY,
        };
        lev.Textures.AddRange(room.Lev.Textures);
        foreach (var src in room.Lev.Sectors)
        {
            var s = src.Clone();
            s.Name = Rename(s.Name);
            for (var i = 0; i < s.Vertices.Count; i++)
                s.Vertices[i] = p.Apply(s.Vertices[i]);
            s.FloorAltitude += p.Dy;
            s.CeilingAltitude += p.Dy;
            lev.Sectors.Add(s);
        }

        // ---- objects
        var obj = new ObjFile { Version = room.Objects.Version, LevelName = room.Objects.LevelName };
        obj.Pods.AddRange(room.Objects.Pods);
        obj.Sprites.AddRange(room.Objects.Sprites);
        obj.Frames.AddRange(room.Objects.Frames);
        obj.Sounds.AddRange(room.Objects.Sounds);
        foreach (var src in room.Objects.Objects)
        {
            var o = src.Clone();
            var v = p.Apply(new Vertex(o.X, o.Z));
            (o.X, o.Z) = (v.X, v.Z);
            o.Y += p.Dy;
            o.Yaw = p.ApplyAngle(o.Yaw);
            obj.Objects.Add(o);
        }

        // ---- INF
        var inf = new InfFile { Version = room.Inf.Version, LevelName = room.Inf.LevelName };
        foreach (var src in room.Inf.Items)
        {
            var item = src.Clone();
            if (item.Name != null) item.Name = Rename(item.Name);
            string? cls = null;
            foreach (var s in item.Statements)
            {
                if (s.Key == "class")
                {
                    cls = s.Args.Count > 1 ? s.Args[1] : null;
                    continue;
                }
                RenameRefs(s, Rename);
                switch (s.Key)
                {
                    case "center" when s.Args.Count >= 2:
                        var c = p.Apply(new Vertex(Num(s.Args[0]), Num(s.Args[1])));
                        s.Args[0] = Fmt(c.X);
                        s.Args[1] = Fmt(c.Z);
                        break;
                    case "angle" when s.Args.Count >= 1 && cls != null && WorldAngleElevators.Contains(cls):
                        s.Args[0] = Fmt(p.ApplyAngle(Num(s.Args[0])));
                        break;
                    case "stop" when s.Args.Count >= 1 && cls != null && AltitudeElevators.Contains(cls) && IsNumber(s.Args[0]):
                        // INF altitudes point up while LEV altitudes point down (stop -20 is LEV altitude 20),
                        // so a room moved down by Dy has its stops moved by -Dy. '@n' relative stops and
                        // sector-name stops are untouched.
                        s.Args[0] = Fmt(Num(s.Args[0]) - p.Dy);
                        break;
                }
            }
            inf.Items.Add(item);
        }

        return new RoomInstance(room, p, prefix, lev, obj, inf);
    }

    /// <summary>Renames the sector part of every reference in a statement (keeps <c>(wall)</c> suffixes).</summary>
    static void RenameRefs(InfStatement s, Func<string, string> rename)
    {
        var refs = InfReferences.Of(s).Select(r => r.Sector).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (refs.Count == 0) return;
        for (var i = 0; i < s.Args.Count; i++)
        {
            var a = s.Args[i];
            var open = a.IndexOf('(');
            var name = open > 0 ? a[..open] : a;
            // stop: <number> is not a reference; only rename tokens that were reported as sector names.
            if (refs.Contains(name) && !(s.Key == "message" && i == 0 && int.TryParse(a, out _)))
                s.Args[i] = rename(name) + (open > 0 ? a[open..] : "");
        }
    }

    static bool IsNumber(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    static string Fmt(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
