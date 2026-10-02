using DarkForces.Core.Geometry;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;

namespace DarkForces.Core.Tests;

/// <summary>A minimal valid room built by hand: a 20x8 room with one east-facing connector stub.</summary>
static class SampleRoom
{
    //  z=8  +------------------+----+
    //       |      main        |CX_A| portal at x=24 (on the 8 grid), facing E
    //  z=0  +------------------+----+
    //      x=0               x=20  x=24
    public static RoomPackage Build()
    {
        var tex = new SurfaceTexture(0, 0, 0, 0);
        Wall W(int l, int r, int adjoin = -1, int mirror = -1) => new()
        {
            Left = l, Right = r, Mid = tex, Top = tex, Bot = tex, Sign = new SurfaceTexture(-1, 0, 0, null),
            Adjoin = adjoin, Mirror = mirror, Walk = adjoin,
        };

        var main = new Sector { Name = "main", FloorAltitude = 0, CeilingAltitude = -16, FloorTexture = tex, CeilingTexture = tex };
        main.Vertices.AddRange([new(0, 0), new(0, 8), new(20, 8), new(20, 0)]); // clockwise
        main.Walls.AddRange([W(0, 1), W(1, 2), W(2, 3, adjoin: 1, mirror: 0), W(3, 0)]);

        var stub = new Sector { Name = "CX_A", FloorAltitude = 0, CeilingAltitude = -8, FloorTexture = tex, CeilingTexture = tex };
        stub.Vertices.AddRange([new(20, 0), new(20, 8), new(24, 8), new(24, 0)]);
        stub.Walls.AddRange([W(0, 1, adjoin: 0, mirror: 2), W(1, 2), W(2, 3), W(3, 0)]);

        var lev = new LevFile { LevelName = "TEST", Palette = "SECBASE.PAL" };
        lev.Textures.Add("WALL.BM");
        lev.Sectors.AddRange([main, stub]);

        var meta = new RoomMetadata
        {
            Id = "test-room",
            Palette = "SECBASE.PAL",
            Bounds = new RoomBounds { MinX = 0, MinZ = 0, MaxX = 24, MaxZ = 8, MinY = -16, MaxY = 0 },
            Connectors = [new ConnectorInfo("A", Facing.E, 0)],
        };
        return new RoomPackage(meta, lev, new ObjFile(), new InfFile());
    }

    public static DfObject Object(double x, double z, params string[] logic) => new()
    {
        Class = "SPIRIT", X = x, Y = 0, Z = z, Seq = [new SeqEntry("LOGIC", [.. logic])],
    };

    public static InfItem Item(string name, params (string Key, string[] Args)[] statements)
    {
        var item = new InfItem { Type = "sector", Name = name };
        item.Statements.AddRange(statements.Select(s => new InfStatement(s.Key, [.. s.Args])));
        return item;
    }
}

public class RoomValidatorTests
{
    static List<string> Errors(RoomPackage room) =>
        RoomValidator.Validate(room).Where(f => f.Severity == Severity.Error).Select(f => f.Rule).ToList();

    [Fact]
    public void Sample_room_is_valid()
    {
        Assert.Empty(RoomValidator.Validate(SampleRoom.Build()));
    }

    [Fact]
    public void Finds_the_connector_and_its_portal()
    {
        var c = Assert.Single(RoomValidator.FindConnectors(SampleRoom.Build().Lev));
        Assert.Equal(("A", 1, 2, (Facing?)Facing.E), (c.Id, c.StubSector, c.PortalWall, c.Facing));
    }

    [Fact]
    public void G2_adjoin_must_point_back()
    {
        var room = SampleRoom.Build();
        room.Lev.Sectors[1].Walls[0].Mirror = 1;
        Assert.Contains("G2", Errors(room));
    }

    [Fact]
    public void G1_sectors_must_wind_clockwise()
    {
        var room = SampleRoom.Build();
        foreach (var w in room.Lev.Sectors[0].Walls)
            (w.Left, w.Right) = (w.Right, w.Left);
        Assert.Contains("G1", Errors(room));
    }

    [Fact]
    public void C3_portal_must_be_on_the_grid()
    {
        var room = SampleRoom.Build();
        foreach (var s in room.Lev.Sectors)
            for (var i = 0; i < s.Vertices.Count; i++)
                s.Vertices[i] = s.Vertices[i] with { X = s.Vertices[i].X + 1 };
        room.Metadata.Bounds.MinX = 1;
        room.Metadata.Bounds.MaxX = 25;
        Assert.Contains("C3", Errors(room));
    }

    [Fact]
    public void C6_stub_opening_must_be_8_high()
    {
        var room = SampleRoom.Build();
        room.Lev.Sectors[1].CeilingAltitude = -12;
        Assert.Contains("C6", Errors(room));
    }

    [Fact]
    public void C10_metadata_facing_must_match_geometry()
    {
        var room = SampleRoom.Build();
        room.Metadata.Connectors[0] = room.Metadata.Connectors[0] with { Facing = Facing.W };
        Assert.Contains("C10", Errors(room));
    }

    [Fact]
    public void G4_vertices_must_fit_declared_bounds()
    {
        var room = SampleRoom.Build();
        room.Metadata.Bounds.MaxX = 16;
        Assert.Contains("G4", Errors(room));
    }

    [Fact]
    public void Object_rules()
    {
        var room = SampleRoom.Build();
        room.Objects.Objects.Add(SampleRoom.Object(4, 4, "PLAYER"));   // one is the room's start point...
        room.Objects.Objects.Add(SampleRoom.Object(5, 4, "PLAYER"));   // ...two is an error
        room.Objects.Objects.Add(SampleRoom.Object(6, 4, "ITEM", "RED"));
        room.Objects.Objects.Add(SampleRoom.Object(22, 4, "ITEM", "SHIELD")); // inside the stub
        room.Objects.Objects.Add(SampleRoom.Object(50, 50, "ITEM", "SHIELD")); // outside the room
        var errors = Errors(room);
        Assert.Single(errors, e => e == "O1");
        Assert.Contains("O2", errors);
        Assert.Equal(2, errors.Count(e => e == "O3"));
    }

    [Fact]
    public void Inf_rules()
    {
        var room = SampleRoom.Build();
        room.Inf.Items.Add(SampleRoom.Item("main",
            ("class", ["elevator", "move_floor"]),
            ("slave", ["elsewhere"]),
            ("message", ["0", "complete", "next_stop"]),
            ("key", ["red"])));
        var errors = Errors(room);
        Assert.Contains("I1", errors);
        Assert.Contains("I3", errors);
        Assert.Contains("I4", errors);
    }

    [Fact]
    public void Connector_parts_must_not_have_inf()
    {
        var room = SampleRoom.Build();
        room.Inf.Items.Add(SampleRoom.Item("CX_A", ("class", ["elevator", "change_light"])));
        Assert.Contains("C7", Errors(room));
    }

    [Fact]
    public void Metadata_must_list_every_stub_and_valid_traversal()
    {
        var room = SampleRoom.Build();
        room.Metadata.Connectors.Clear();
        room.Metadata.Traversal.Add(new TraversalEdge("A", "Z", ["CLEATS"]));
        var errors = Errors(room);
        Assert.Contains("M1", errors);
        Assert.Contains("M2", errors);
    }

    [Fact]
    public void Package_saves_and_loads_unchanged()
    {
        var dir = Directory.CreateTempSubdirectory("dfroom").FullName;
        try
        {
            var room = SampleRoom.Build();
            room.Inf.Items.Add(SampleRoom.Item("main", ("class", ["elevator", "change_light"])));
            room.Save(dir);
            var loaded = RoomPackage.Load(dir);
            Assert.Equal(room.Lev.Write(), loaded.Lev.Write());
            Assert.Equal(room.Inf.Write(), loaded.Inf.Write());
            Assert.Equal(room.Metadata.ToJson(), loaded.Metadata.ToJson());
            Assert.Empty(RoomValidator.Validate(loaded));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class GeoTests
{
    [Fact]
    public void Clockwise_sector_has_negative_area_and_outward_facings()
    {
        var s = SampleRoom.Build().Lev.Sectors[0];
        Assert.Equal(-160, Geo.SignedArea(s));
        Assert.Equal([Facing.W, Facing.N, Facing.E, Facing.S], s.Walls.Select(w => Geo.OutwardFacing(s, w)!.Value));
    }

    [Fact]
    public void Contains_is_inclusive_of_interior_only()
    {
        var s = SampleRoom.Build().Lev.Sectors[0];
        Assert.True(Geo.Contains(s, 10, 4));
        Assert.False(Geo.Contains(s, 21, 4));
        Assert.False(Geo.Contains(s, 10, -1));
    }

    [Fact]
    public void Convexity()
    {
        var room = SampleRoom.Build();
        Assert.True(Geo.IsConvex(room.Lev.Sectors[1]));
        var notch = new Sector();
        notch.Vertices.AddRange([new(0, 0), new(0, 8), new(4, 4), new(8, 8), new(8, 0)]);
        for (var i = 0; i < 5; i++) notch.Walls.Add(new Wall { Left = i, Right = (i + 1) % 5 });
        Assert.False(Geo.IsConvex(notch));
    }
}

public class InfReferenceTests
{
    static List<SectorRef> Refs(string key, params string[] args) => [.. InfReferences.Of(new InfStatement(key, [.. args]))];

    [Fact]
    public void Recognizes_every_reference_form()
    {
        Assert.Equal([new SectorRef("door1", null)], Refs("client", "door1"));
        Assert.Equal([new SectorRef("tunnel", 7)], Refs("message", "0", "tunnel(7)", "done"));
        Assert.Empty(Refs("message", "master_on"));                       // trigger message: targets come from client:
        Assert.Equal([new SectorRef("lift", null)], Refs("message", "lift", "next_stop"));
        Assert.Equal([new SectorRef("a", 1), new SectorRef("b", 2)], Refs("adjoin", "1", "a", "1", "b", "2"));
        Assert.Equal([new SectorRef("other", null)], Refs("stop", "other", "hold"));
        Assert.Empty(Refs("stop", "@32", "3"));
        Assert.Empty(Refs("stop", "-16.5", "hold"));
    }
}
