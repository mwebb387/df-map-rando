using System.Globalization;
using DarkForces.Core.Geometry;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;

namespace DarkForces.Core.Authoring;

/// <summary>A doorway for <see cref="RoomScaffold"/>: the wall it is on, and the width of its opening into the room.</summary>
/// <param name="Opening">8 = the stub joins the room directly; wider adds a <c>CA_</c> adapter narrowing to the stub.</param>
public sealed record ConnectorSpec(Facing Side, int Opening = (int)RoomRules.StubWidth)
{
    /// <summary>Parses <c>W</c>, <c>E16</c>, … (side letter, then an optional opening width).</summary>
    public static ConnectorSpec Parse(string text)
    {
        var t = text.Trim().ToUpperInvariant();
        if (t.Length == 0 || !Enum.TryParse<Facing>(t[..1], out var side))
            throw new ArgumentException($"connector '{text}': start with the wall it is on, N, E, S or W (e.g. W, E16)");
        var opening = t.Length > 1
            ? int.TryParse(t[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var w) ? w
              : throw new ArgumentException($"connector '{text}': the opening width after the side must be a number")
            : (int)RoomRules.StubWidth;
        return new ConnectorSpec(side, opening);
    }
}

/// <summary>What <see cref="RoomScaffold.Build"/> makes. Sizes are DF units; width runs along X (east), depth along Z (north).</summary>
public sealed record ScaffoldOptions
{
    public string Id { get; init; } = "my-room";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public int Width { get; init; } = 32;
    public int Depth { get; init; } = 32;
    public int Height { get; init; } = 16;
    public List<ConnectorSpec> Connectors { get; init; } = [new(Facing.W), new(Facing.E)];
    public string Palette { get; init; } = "SECBASE.PAL";
    public string Wall { get; init; } = "IWPANEL1.BM";
    public string Floor { get; init; } = "IPSEC3.BM";
    public string Ceiling { get; init; } = "IF3.BM";
    /// <summary>Shown on an unused doorway, and the door panel when <see cref="Doors"/> is on.</summary>
    public string Seal { get; init; } = "IDDOOR1.BM";
    public string Frame { get; init; } = "IWFRAME1.BM";
    public int Ambient { get; init; } = 24;
    /// <summary>Give every connector a door (an instant <c>flag</c> door with the seal as its panel).</summary>
    public bool Doors { get; init; } = true;
}

/// <summary>
/// Makes a starter room package that already passes the validator: one rectangular room sector with a
/// connector stub (and, for wide openings, an adapter) centered on each requested wall. Authors open
/// <c>ROOM.LEV</c> in a DF editor and reshape it from there.
/// </summary>
public static class RoomScaffold
{
    /// <summary>Adapter depth for wide openings: 8, so the stub's portal still lands on the 8-unit grid.</summary>
    const double AdapterDepth = 8;

    /// <summary>The room sector sits 4 units in from the origin, so stubs directly on its walls end on the grid.</summary>
    const double Inset = RoomRules.StubDepth;

    public static RoomPackage Build(ScaffoldOptions o)
    {
        Check(o);
        var lev = new LevFile { LevelName = "ROOM", Palette = o.Palette };
        lev.Textures.AddRange([o.Wall, o.Floor, o.Ceiling, o.Seal, o.Frame]);
        SurfaceTexture T(int i) => new(i, 0, 0, 0);
        var (wall, floor, ceil, seal) = (T(0), T(1), T(2), T(3));

        double x0 = Inset, z0 = Inset, x1 = Inset + o.Width, z1 = Inset + o.Depth;
        var connectors = o.Connectors.Select((c, i) => (Spec: c, Id: ((char)('A' + i)).ToString(), Portal: PortalStart(c.Side, x0, z0, x1, z1))).ToList();

        // ---- the room: a clockwise rectangle (W edge northward, N eastward, E southward, S westward),
        // with each doorway's opening cut into its edge as a wall of its own.
        var corners = new[] { new Vertex(x0, z0), new Vertex(x0, z1), new Vertex(x1, z1), new Vertex(x1, z0) };
        var edgeSide = new[] { Facing.W, Facing.N, Facing.E, Facing.S };
        var verts = new List<Vertex>();
        var openingWall = new Dictionary<string, int>();
        for (var e = 0; e < 4; e++)
        {
            verts.Add(corners[e]);
            foreach (var c in connectors.Where(c => c.Spec.Side == edgeSide[e]))
            {
                var mid = c.Portal + RoomRules.StubWidth / 2;
                var (lo, hi) = (mid - c.Spec.Opening / 2.0, mid + c.Spec.Opening / 2.0);
                // W and N edges run toward +t, E and S edges toward -t.
                var (first, second) = edgeSide[e] is Facing.W or Facing.N ? (lo, hi) : (hi, lo);
                verts.Add(OnEdge(edgeSide[e], first, x0, z0, x1, z1));
                openingWall[c.Id] = verts.Count - 1;
                verts.Add(OnEdge(edgeSide[e], second, x0, z0, x1, z1));
            }
        }
        var room = NewSector("", verts, 0, -o.Height, o.Ambient, floor, ceil, wall);
        lev.Sectors.Add(room);

        // ---- per doorway: [adapter] + stub, built as the extractor does (docs/room-spec.md §4)
        foreach (var c in connectors)
        {
            var wi = openingWall[c.Id];
            var (a, b) = Geo.Segment(room, room.Walls[wi]);
            var (dx, dz) = Geo.Direction(c.Spec.Side);
            double Q(Vertex v) => dx != 0 ? v.X : v.Z;
            Vertex At(double q, double t) => dx != 0 ? new Vertex(q, t) : new Vertex(t, q);
            double Tc(Vertex v) => dx != 0 ? v.Z : v.X;
            var sign = dx + dz;
            var wide = c.Spec.Opening > RoomRules.StubWidth;
            var innerQ = Q(a) + sign * (wide ? AdapterDepth : 0);
            var portalQ = innerQ + sign * RoomRules.StubDepth;
            var (ta, tb) = Tc(a) < Tc(b) ? (c.Portal, c.Portal + RoomRules.StubWidth) : (c.Portal + RoomRules.StubWidth, c.Portal);
            Vertex a1 = At(innerQ, ta), b1 = At(innerQ, tb), a2 = At(portalQ, ta), b2 = At(portalQ, tb);

            // Each sector's wall 0 is the edge it shares with the sector inside it; the adapter's wall 2 meets the stub.
            var (innerSector, innerWall) = (0, wi);
            if (wide)
            {
                lev.Sectors.Add(NewSector(RoomRules.AdapterNameFor(c.Id), [b, a, a1, b1], 0, -RoomRules.StubHeight, o.Ambient, floor, ceil, wall));
                Link(lev, innerSector, innerWall, lev.Sectors.Count - 1, 0);
                (innerSector, innerWall) = (lev.Sectors.Count - 1, 2);
            }
            var stub = NewSector(RoomRules.StubNameFor(c.Id), wide ? [b1, a1, a2, b2] : [b, a, a2, b2], 0, -RoomRules.StubHeight, o.Ambient, floor, ceil, wall);
            stub.Walls[2].Mid = seal; // the portal (C8)
            lev.Sectors.Add(stub);
            Link(lev, innerSector, innerWall, lev.Sectors.Count - 1, 0);
        }

        var obj = new ObjFile { LevelName = "ROOM" };
        var seed = new RoomMetadata
        {
            Id = o.Id, Name = o.Name.Length > 0 ? o.Name : o.Id, Author = o.Author, Tags = ["community"],
            Connectors = [.. connectors.Select(c => new ConnectorInfo(c.Id, c.Spec.Side, 0,
                o.Doors ? new DoorInfo("flag", o.Seal, o.Frame, []) : null))],
        };
        var result = RoomMetadataBuilder.Build(lev, obj, new InfFile(), seed, o.Id);
        return new RoomPackage(result.Metadata, lev, obj, new InfFile());
    }

    static void Check(ScaffoldOptions o)
    {
        foreach (var (name, v) in new[] { ("width", o.Width), ("depth", o.Depth) })
            if (v < 2 * RoomRules.Grid || v % RoomRules.Grid != 0 || v > RoomRules.MaxBounds - 2 * (Inset + AdapterDepth))
                throw new ArgumentException($"room {name} {v} must be a multiple of {RoomRules.Grid}, from {2 * RoomRules.Grid} to {RoomRules.MaxBounds - 2 * (Inset + AdapterDepth)}");
        if (o.Height < RoomRules.StubHeight || o.Height > 256)
            throw new ArgumentException($"room height {o.Height} must be from {RoomRules.StubHeight} (a doorway's height) to 256");
        if (o.Connectors.Count is < 1 or > RoomRules.MaxConnectors)
            throw new ArgumentException($"a room needs 1 to {RoomRules.MaxConnectors} connectors (C9)");
        foreach (var dup in o.Connectors.GroupBy(c => c.Side).Where(g => g.Count() > 1))
            throw new ArgumentException($"only one connector per wall; {dup.Key} is listed {dup.Count()} times");
        foreach (var c in o.Connectors)
        {
            var side = c.Side is Facing.N or Facing.S ? o.Width : o.Depth;
            if (c.Opening < RoomRules.StubWidth || c.Opening % 2 != 0 || c.Opening > side)
                throw new ArgumentException($"connector {c.Side}: opening {c.Opening} must be even, at least {RoomRules.StubWidth}, and fit the {side}-long wall");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(o.Id, "^[a-z0-9][a-z0-9-]*$"))
            throw new ArgumentException($"room id '{o.Id}' must be lower-case letters, digits and dashes (e.g. my-junction)");
    }

    /// <summary>The portal's start along its wall: on the grid, as close to the wall's middle as possible.</summary>
    static double PortalStart(Facing side, double x0, double z0, double x1, double z1)
    {
        var (lo, hi) = side is Facing.N or Facing.S ? (x0, x1) : (z0, z1);
        var start = Math.Round(((lo + hi) / 2 - RoomRules.StubWidth / 2) / RoomRules.Grid) * RoomRules.Grid;
        return Math.Clamp(start, Math.Ceiling(lo / RoomRules.Grid) * RoomRules.Grid, hi - RoomRules.StubWidth);
    }

    static Vertex OnEdge(Facing side, double t, double x0, double z0, double x1, double z1) => side switch
    {
        Facing.W => new Vertex(x0, t),
        Facing.E => new Vertex(x1, t),
        Facing.N => new Vertex(t, z1),
        _ => new Vertex(t, z0),
    };

    static Sector NewSector(string name, List<Vertex> verts, double floor, double ceiling, int ambient,
                            SurfaceTexture floorT, SurfaceTexture ceilT, SurfaceTexture wall)
    {
        var s = new Sector
        {
            Name = name, Ambient = ambient, FloorAltitude = floor, CeilingAltitude = ceiling,
            FloorTexture = floorT, CeilingTexture = ceilT,
        };
        s.Vertices.AddRange(verts);
        for (var i = 0; i < verts.Count; i++)
            s.Walls.Add(new Wall
            {
                Left = i, Right = (i + 1) % verts.Count, Mid = wall, Top = wall, Bot = wall,
                Sign = new SurfaceTexture(-1, 0, 0, null), Adjoin = -1, Mirror = -1, Walk = -1,
            });
        return s;
    }

    static void Link(LevFile lev, int sa, int wa, int sb, int wb)
    {
        var x = lev.Sectors[sa].Walls[wa];
        var y = lev.Sectors[sb].Walls[wb];
        x.Adjoin = x.Walk = sb; x.Mirror = wb;
        y.Adjoin = y.Walk = sa; y.Mirror = wa;
    }
}
