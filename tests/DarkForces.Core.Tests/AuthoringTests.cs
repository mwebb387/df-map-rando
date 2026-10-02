using DarkForces.Core.Authoring;
using DarkForces.Core.Geometry;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;

namespace DarkForces.Core.Tests;

public class RoomScaffoldTests
{
    [Theory]
    [InlineData("W,E", 32, 32)]
    [InlineData("N", 16, 16)]
    [InlineData("W,E16,N,S24", 40, 32)]
    [InlineData("S32,N8", 32, 48)]
    [InlineData("E", 24, 56)]
    public void Starter_rooms_validate_cleanly(string connectors, int width, int depth)
    {
        var room = RoomScaffold.Build(new ScaffoldOptions
        {
            Id = "t", Width = width, Depth = depth, Connectors = [.. connectors.Split(',').Select(ConnectorSpec.Parse)],
        });
        Assert.Empty(RoomValidator.Validate(room));

        var specs = connectors.Split(',').Select(ConnectorSpec.Parse).ToList();
        Assert.Equal(specs.Select(s => s.Side), room.Metadata.Connectors.Select(c => c.Facing));
        // Wide openings get an adapter, standard ones join the room directly.
        Assert.Equal(specs.Count(s => s.Opening > RoomRules.StubWidth), room.Lev.Sectors.Count(s => s.Name.StartsWith(RoomRules.AdapterPrefix)));
        Assert.All(room.Metadata.Connectors, c => Assert.NotNull(c.Door));
    }

    [Fact]
    public void No_doors_option_leaves_connectors_doorless()
    {
        var room = RoomScaffold.Build(new ScaffoldOptions { Doors = false });
        Assert.All(room.Metadata.Connectors, c => Assert.Null(c.Door));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("E1x")]
    [InlineData("")]
    public void Bad_connector_specs_are_rejected(string text) =>
        Assert.Throws<ArgumentException>(() => ConnectorSpec.Parse(text));

    [Theory]
    [InlineData(12, 32, "W")]     // off the grid
    [InlineData(32, 32, "W,W")]   // two doorways on one wall
    [InlineData(32, 32, "E40")]   // opening wider than the wall
    [InlineData(32, 32, "E9")]    // odd opening
    [InlineData(8, 32, "N")]      // too small
    public void Bad_scaffold_options_are_rejected(int width, int depth, string connectors) =>
        Assert.Throws<ArgumentException>(() => RoomScaffold.Build(new ScaffoldOptions
        {
            Width = width, Depth = depth, Connectors = [.. connectors.Split(',').Select(ConnectorSpec.Parse)],
        }));

    [Fact]
    public void Bad_ids_are_rejected() =>
        Assert.Throws<ArgumentException>(() => RoomScaffold.Build(new ScaffoldOptions { Id = "My Room" }));
}

public class RoomMetadataBuilderTests
{
    static RoomPackage Starter() => RoomScaffold.Build(new ScaffoldOptions { Id = "junction", Connectors = [new(Facing.W), new(Facing.E, 16), new(Facing.N)] });

    [Fact]
    public void Rebuilding_valid_metadata_changes_nothing()
    {
        var room = Starter();
        var r = RoomMetadataBuilder.Build(room.Lev, room.Objects, room.Inf, room.Metadata, "ignored");
        Assert.Empty(r.Changes);
        Assert.True(r.IsValid);
        Assert.Equal(room.Metadata.ToJson(), r.Metadata.ToJson());
    }

    [Fact]
    public void Without_room_json_it_derives_connectors_and_drafts_traversal()
    {
        var room = Starter();
        var r = RoomMetadataBuilder.Build(room.Lev, room.Objects, room.Inf, null, "from-folder");
        Assert.True(r.IsValid);
        Assert.Equal("from-folder", r.Metadata.Id);
        Assert.Equal([Facing.W, Facing.E, Facing.N], r.Metadata.Connectors.Select(c => c.Facing));
        Assert.All(r.Metadata.Connectors, c => Assert.Null(c.Door)); // doors are author data
        Assert.Equal(6, r.Metadata.Traversal.Count);
        Assert.All(r.Metadata.Traversal, e => Assert.Equal(RoomMetadataBuilder.UnverifiedNote, e.Note));
        Assert.Contains(r.Changes, c => c.Contains("drafted 6 edge(s)"));
    }

    [Fact]
    public void Author_fields_are_kept_and_geometry_fields_rederived()
    {
        var room = Starter();
        var authored = RoomMetadata.Parse(room.Metadata.ToJson());
        authored.Name = "The Junction";
        authored.Tags = ["imperial"];
        authored.Traversal = [new TraversalEdge("A", "B", [], Note: "checked in game"), new TraversalEdge("B", "A", [])];
        authored.Bounds = new RoomBounds();      // stale
        authored.Connectors[2] = authored.Connectors[2] with { Floor = 99 };

        var r = RoomMetadataBuilder.Build(room.Lev, room.Objects, room.Inf, authored, "ignored");
        Assert.Equal("The Junction", r.Metadata.Name);
        Assert.Equal(["imperial"], r.Metadata.Tags);
        Assert.NotNull(r.Metadata.Connectors[0].Door);
        Assert.Equal(0, r.Metadata.Connectors[2].Floor);
        Assert.Equal(room.Metadata.Bounds.MaxX, r.Metadata.Bounds.MaxX);
        // A and B keep the author's edges; C had none, so its edges are drafted.
        Assert.Contains(r.Metadata.Traversal, e => e is { From: "A", To: "B", Note: "checked in game" });
        Assert.Equal(4, r.Metadata.Traversal.Count(e => e.Note == RoomMetadataBuilder.UnverifiedNote));
        Assert.Contains(r.Changes, c => c.Contains("bounds"));
        Assert.Contains(r.Changes, c => c.StartsWith("connector C: was"));
    }

    [Fact]
    public void Removed_stubs_drop_their_connector_and_edges()
    {
        var room = Starter();
        room.Lev.Sectors[room.SectorIndex("CX_C")].Name = "ALCOVE";
        var r = RoomMetadataBuilder.Build(room.Lev, room.Objects, room.Inf, room.Metadata, "ignored");
        Assert.DoesNotContain(r.Metadata.Connectors, c => c.Id == "C");
        Assert.DoesNotContain(r.Metadata.Traversal, e => e.From == "C" || e.To == "C");
        Assert.Contains(r.Changes, c => c.StartsWith("connector C: removed"));
        Assert.Contains(r.Changes, c => c.Contains("dropped 4 edge(s)"));
    }

    [Fact]
    public void Goal_items_in_the_objects_become_goals()
    {
        var room = Starter();
        room.Objects.Frames.Add("IDPLANS.FME");
        room.Objects.Objects.Add(new DfObject { Class = "FRAME", Data = 0, X = 20, Y = 0, Z = 20, Difficulty = 1, Seq = [new SeqEntry("LOGIC", ["PLANS"])] });
        var r = RoomMetadataBuilder.Build(room.Lev, room.Objects, new InfFile(), room.Metadata, "ignored");
        Assert.True(r.IsValid, string.Join("; ", r.Findings));
        var goal = Assert.Single(r.Metadata.Goals);
        Assert.Equal(("PLANS", 20.0, 20.0), (goal.Item, goal.X, goal.Z));
        Assert.Equal(["IDPLANS.FME"], r.Metadata.Resources.Frames);
    }

    [Fact]
    public void Invalid_geometry_is_reported()
    {
        var room = Starter();
        room.Lev.Sectors[room.SectorIndex("CX_A")].CeilingAltitude = -10; // stubs are 8 high
        var r = RoomMetadataBuilder.Build(room.Lev, room.Objects, room.Inf, room.Metadata, "ignored");
        Assert.False(r.IsValid);
        Assert.Contains(r.Findings, f => f.Rule == "C6");
    }
}

public class RoomPlaytestTests
{
    [Theory]
    [InlineData(8)]   // stub joins the room directly
    [InlineData(16)]  // through an adapter
    public void Wraps_the_room_with_the_player_just_inside_the_chosen_doorway(int opening)
    {
        var room = RoomScaffold.Build(new ScaffoldOptions { Connectors = [new(Facing.W), new(Facing.E, opening)] });
        var game = new DarkForces.Core.Gob.GobArchive();
        game.Put("SECBASE.CMP", [1, 2, 3]);
        var gob = RoomPlaytest.Build(room, game, "SECBASE", "b");

        Assert.Equal(["SECBASE.LEV", "SECBASE.O", "SECBASE.INF", "SECBASE.GOL", "SECBASE.CMP"], gob.Entries.Select(e => e.Name));
        var obj = ObjFile.Parse(DarkForces.Core.Text.DfText.Decode(gob.Get("SECBASE.O")));
        var player = Assert.Single(obj.Objects, LogicCatalog.IsPlayer);
        // Just inside the room, 4 units past doorway B (the east wall), facing west into the room.
        Assert.True(Geo.Contains(room.Lev.Sectors[0], player.X, player.Z));
        Assert.Equal(4 + 32 - 4, player.X);
        Assert.Equal(270, player.Yaw);
        Assert.Contains(obj.Objects, o => o.Class == "SAFE");
    }

    [Fact]
    public void Unknown_connector_is_an_error()
    {
        var room = RoomScaffold.Build(new ScaffoldOptions());
        var ex = Assert.Throws<ArgumentException>(() => RoomPlaytest.Build(room, new DarkForces.Core.Gob.GobArchive(), "SECBASE", "Q"));
        Assert.Contains("A, B", ex.Message);
    }
}

public class RoomStartTests
{
    static DfObject Player(double x, double z, double yaw) => new()
    {
        Class = "SPIRIT", X = x, Y = 0, Z = z, Yaw = yaw, Difficulty = 1,
        Seq = [new SeqEntry("LOGIC", ["PLAYER"]), new SeqEntry("EYE", ["TRUE"])],
    };

    static RoomPackage Room(bool player, bool metadata)
    {
        var room = RoomScaffold.Build(new ScaffoldOptions { Id = "start-room" });
        if (player) room.Objects.Objects.Add(Player(20, 12, 90));
        if (metadata) room.Metadata.StartPoints.Add(new StartPoint(10, 0, 30, 180));
        return room;
    }

    static DarkForces.Core.Gob.GobArchive FakeGame()
    {
        var gob = new DarkForces.Core.Gob.GobArchive();
        gob.Put("JEDI.LVL", DarkForces.Core.Text.DfText.Encode("LEVELS 1\r\nSecret Base, SECBASE, x\r\n"));
        return gob;
    }

    [Fact]
    public void One_player_object_is_allowed_silently_but_not_two()
    {
        var room = Room(player: true, metadata: false);
        Assert.Empty(RoomValidator.Validate(room));
        room.Objects.Objects.Add(Player(30, 30, 0));
        Assert.Contains(RoomValidator.Validate(room), f => f.Rule == "O1" && f.Severity == Severity.Error);
    }

    [Theory]
    [InlineData(true, true, 20, 12, StartSource.PlayerObject)]   // the player object wins
    [InlineData(true, false, 20, 12, StartSource.PlayerObject)]
    [InlineData(false, true, 10, 30, StartSource.Metadata)]      // no player object: room.json
    public void Start_point_comes_from_the_player_object_else_metadata(bool player, bool metadata, double x, double z, StartSource source)
    {
        var start = RoomStarts.Of(Room(player, metadata));
        Assert.NotNull(start);
        Assert.Equal((x, z, source), (start.Point.X, start.Point.Z, start.Source));
    }

    [Fact]
    public void No_start_point_means_none()
    {
        Assert.Null(RoomStarts.Of(Room(player: false, metadata: false)));
    }

    [Fact]
    public void Metadata_start_points_move_with_the_instance()
    {
        var p = new Placement(1, 64, 5, -8);
        var start = RoomStarts.Of(RoomTransformer.Apply(Room(player: false, metadata: true), p, "R1_"));
        var v = p.Apply(new Vertex(10, 30));
        Assert.Equal((v.X, 5.0, v.Z, 270.0), (start!.Point.X, start.Point.Y, start.Point.Z, start.Point.Yaw));
    }

    [Fact]
    public void Generated_levels_have_one_player_at_the_start_rooms_player_object()
    {
        var s = new DarkForces.Core.Knobs.RandomizerSettings { Seed = 4 };
        s.Layout.RoomCount = 3;
        var g = DarkForces.Core.Generation.Generator.Generate(s, [Room(player: true, metadata: false)], FakeGame());
        var obj = ObjFile.Parse(DarkForces.Core.Text.DfText.Decode(g.Gob.Get("SECBASE.O")));

        var player = Assert.Single(obj.Objects, LogicCatalog.IsPlayer); // three rooms with a player object each, one player
        var inst = g.Layout.Instances[g.Layout.StartInstance];
        var expected = inst.Placement.Apply(new Vertex(20, 12));
        Assert.Equal((expected.X, expected.Z, inst.Placement.ApplyAngle(90)), (player.X, player.Z, player.Yaw));
        Assert.Contains(g.Log, l => l.Contains("at the room's player object"));
    }

    [Fact]
    public void Playtest_starts_at_the_player_object_unless_a_connector_is_given()
    {
        var room = Room(player: true, metadata: false);
        var gob = RoomPlaytest.Build(room, new DarkForces.Core.Gob.GobArchive(), "SECBASE", null);
        var player = Assert.Single(ObjFile.Parse(DarkForces.Core.Text.DfText.Decode(gob.Get("SECBASE.O"))).Objects, LogicCatalog.IsPlayer);
        Assert.Equal((20.0, 12.0), (player.X, player.Z));

        var atA = RoomPlaytest.Spawn(room, "A");
        Assert.NotEqual((20.0, 12.0), (atA.X, atA.Z));
    }
}
