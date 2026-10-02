using DarkForces.Core.Geometry;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;

namespace DarkForces.Core.Generation;

public enum HallwayShape { Straight, Turn }

/// <summary>Textures a hallway borrows from the room it grows out of, so it blends in.</summary>
public sealed record HallwayTextures(string Wall, string Floor, string Ceiling, string Palette, int Ambient);

/// <summary>
/// Builds hallways as ordinary room packages: an 8-wide corridor with a standard connector stub at each end
/// (docs/room-spec.md §4), so layout, merging, doors and progression treat them like any other room.
/// </summary>
/// <remarks>
/// Local frame: connector A's portal is at x = 0 facing W, and the hallway runs east. A straight hallway ends in
/// connector B facing E; a turn hallway turns left and ends in B facing N. Attaching either end to a room, at any
/// rotation, covers left and right turns. A floor change between the ends becomes a landing and stairs in the first leg.
/// </remarks>
public static class HallwayBuilder
{
    public const string Tag = "hallway";
    const double Width = RoomRules.StubWidth;      // 8: the corridor is exactly as wide as a stub
    const double StepDepth = 4;

    /// <summary>Highest single step; below the 3.5 the player can walk up (docs/room-spec.md §2).</summary>
    public const double MaxStepRise = 3;

    /// <summary>Corridor clearance above the floor (stubs stay at the standard 8).</summary>
    const double BodyHeight = 12;

    public static bool IsHallway(RoomPackage room) => room.Metadata.Tags.Contains(Tag);

    /// <summary>Steps needed for a floor change.</summary>
    public static int Steps(double rise) => (int)Math.Ceiling(Math.Abs(rise) / MaxStepRise);

    /// <summary>Flat run between stub A and the stairs; none when there are no stairs.</summary>
    static double LandingDepth(double rise) => Steps(rise) > 0 ? StepDepth : 0;

    /// <summary>Shortest first leg (portal to turn, or whole length) that fits the landing and stairs, on the grid.</summary>
    public static int MinLength(double rise) =>
        (int)(Math.Ceiling((2 * RoomRules.StubDepth + LandingDepth(rise) + StepDepth * Steps(rise)) / RoomRules.Grid) * RoomRules.Grid);

    /// <param name="length">First leg, portal A to the far end (straight) or to the turn (turn); a multiple of 8.</param>
    /// <param name="length2">Turn only: second leg, from the turn to portal B; a multiple of 8, at least 8.</param>
    /// <param name="rise">Floor at B minus floor at A (Y points down, so negative climbs); an integer.</param>
    public static RoomPackage Build(string id, HallwayShape shape, int length, int length2, int rise, HallwayTextures tex)
    {
        if (length % RoomRules.Grid != 0 || length < MinLength(rise))
            throw new ArgumentException($"hallway length {length} must be a multiple of {RoomRules.Grid} and at least {MinLength(rise)}");
        if (shape == HallwayShape.Turn && (length2 % RoomRules.Grid != 0 || length2 < RoomRules.Grid))
            throw new ArgumentException($"turn leg {length2} must be a positive multiple of {RoomRules.Grid}");

        var lev = new LevFile { LevelName = "HALL", Palette = tex.Palette };
        lev.Textures.AddRange([tex.Wall, tex.Floor, tex.Ceiling]);
        var wall = new SurfaceTexture(0, 0, 0, 0);
        var floorT = new SurfaceTexture(1, 0, 0, 0);
        var ceilT = new SurfaceTexture(2, 0, 0, 0);

        // ---- leg 1 along +X: stub A, stairs, flat run (to the end or to the turn)
        var steps = Steps(rise);
        var legEnd = shape == HallwayShape.Straight ? length - RoomRules.StubDepth : length;
        var cuts = new List<(double X0, double X1, double Floor, string Name)> { (0, RoomRules.StubDepth, 0, RoomRules.StubNameFor("A")) };
        var x = RoomRules.StubDepth;
        // A step straight off the 8-high stub would leave 8 - rise of headroom (5 for a 3-unit step): the player
        // would have to crouch to climb it. A landing with corridor headroom first keeps every opening 9+ high.
        if (LandingDepth(rise) > 0)
        {
            cuts.Add((x, x + LandingDepth(rise), 0, ""));
            x += LandingDepth(rise);
        }
        for (var k = 1; k <= steps; k++, x += StepDepth)
            cuts.Add((x, x + StepDepth, Math.Round(rise * (double)k / steps, 4), ""));
        if (x < legEnd)
            cuts.Add((x, legEnd, rise, ""));
        if (shape == HallwayShape.Straight)
            cuts.Add((legEnd, length, rise, RoomRules.StubNameFor("B")));

        var sectors = cuts.Select(c => Box(c.X0, 0, c.X1, Width, c.Floor, c.Name, tex.Ambient, floorT, ceilT, wall)).ToList();
        for (var i = 0; i + 1 < sectors.Count; i++)
            Link(sectors, i, 2, i + 1, 0); // E edge of one to W edge of the next

        if (shape == HallwayShape.Turn)
        {
            // Corner square, then leg 2 along +Z ending in stub B (portal on the N edge).
            var corner = sectors.Count;
            sectors.Add(Box(length, 0, length + Width, Width, rise, "", tex.Ambient, floorT, ceilT, wall));
            Link(sectors, corner - 1, 2, corner, 0);
            var z = Width;
            var zEnd = Width + length2;
            var prev = corner;
            var prevWall = 1; // the corner's N edge
            if (zEnd - RoomRules.StubDepth > z)
            {
                var run = sectors.Count;
                sectors.Add(Box(length, z, length + Width, zEnd - RoomRules.StubDepth, rise, "", tex.Ambient, floorT, ceilT, wall));
                Link(sectors, prev, prevWall, run, 3);
                (prev, prevWall) = (run, 1);
            }
            var stubB = sectors.Count;
            sectors.Add(Box(length, zEnd - RoomRules.StubDepth, length + Width, zEnd, rise, RoomRules.StubNameFor("B"), tex.Ambient, floorT, ceilT, wall));
            Link(sectors, prev, prevWall, stubB, 3);
        }

        // Stubs are 8 high (C6); corridor sectors get more headroom.
        foreach (var s in sectors)
            s.CeilingAltitude = s.FloorAltitude - (RoomRules.StubName().IsMatch(s.Name) ? RoomRules.StubHeight : BodyHeight);
        lev.Sectors.AddRange(sectors);

        var bounds = Geo.BoundsOf(sectors);
        var meta = new RoomMetadata
        {
            Id = id,
            Name = $"generated {shape.ToString().ToLowerInvariant()} hallway",
            Author = "generator",
            Palette = tex.Palette,
            Bounds = new RoomBounds
            {
                MinX = bounds.MinX, MinZ = bounds.MinZ, MaxX = bounds.MaxX, MaxZ = bounds.MaxZ,
                MinY = sectors.Min(s => s.CeilingAltitude), MaxY = sectors.Max(s => s.FloorAltitude),
            },
            Connectors =
            [
                new ConnectorInfo("A", Facing.W, 0),
                new ConnectorInfo("B", shape == HallwayShape.Straight ? Facing.E : Facing.N, rise),
            ],
            Traversal = [new TraversalEdge("A", "B", []), new TraversalEdge("B", "A", [])],
            Tags = [Tag],
        };
        return new RoomPackage(meta, lev, new ObjFile(), new InfFile());
    }

    /// <summary>Axis-aligned clockwise box: walls 0 = W edge, 1 = N edge, 2 = E edge, 3 = S edge.</summary>
    static Sector Box(double x0, double z0, double x1, double z1, double floor, string name, int ambient,
                      SurfaceTexture floorT, SurfaceTexture ceilT, SurfaceTexture wall)
    {
        var s = new Sector { Name = name, Ambient = ambient, FloorAltitude = floor, FloorTexture = floorT, CeilingTexture = ceilT };
        s.Vertices.AddRange([new(x0, z0), new(x0, z1), new(x1, z1), new(x1, z0)]);
        for (var i = 0; i < 4; i++)
            s.Walls.Add(new Wall
            {
                Left = i, Right = (i + 1) % 4, Mid = wall, Top = wall, Bot = wall,
                Sign = new SurfaceTexture(-1, 0, 0, null),
            });
        return s;
    }

    static void Link(List<Sector> sectors, int a, int wa, int b, int wb)
    {
        var x = sectors[a].Walls[wa];
        var y = sectors[b].Walls[wb];
        x.Adjoin = x.Walk = b; x.Mirror = wb;
        y.Adjoin = y.Walk = a; y.Mirror = wa;
    }
}
