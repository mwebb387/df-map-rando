using DarkForces.Core.Generation;
using DarkForces.Core.Geometry;
using DarkForces.Core.Gob;
using DarkForces.Core.Inf;
using DarkForces.Core.Knobs;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;
using DarkForces.Core.Text;

namespace DarkForces.Core.Tests;

/// <summary>A room with doors on both sides, so rooms chain and locks can sit between them.</summary>
static class CorridorRoom
{
    //  z=8  +----+------------+----+
    //       |CX_A|    main    |CX_B|   A faces W (portal x=0), B faces E (portal x=24)
    //  z=0  +----+------------+----+
    //      x=0  x=4         x=20  x=24
    public static RoomPackage Build(string id = "corridor")
    {
        var tex = new SurfaceTexture(0, 0, 0, 0);
        Wall W(int l, int r, int adjoin = -1, int mirror = -1) => new()
        {
            Left = l, Right = r, Mid = tex, Top = tex, Bot = tex, Sign = new SurfaceTexture(-1, 0, 0, null),
            Adjoin = adjoin, Mirror = mirror, Walk = adjoin,
        };
        Sector S(string name, double ceiling) =>
            new() { Name = name, FloorAltitude = 0, CeilingAltitude = ceiling, FloorTexture = tex, CeilingTexture = tex };

        var main = S("main", -16);
        main.Vertices.AddRange([new(4, 0), new(4, 8), new(20, 8), new(20, 0)]);
        main.Walls.AddRange([W(0, 1, 1, 0), W(1, 2), W(2, 3, 2, 0), W(3, 0)]);

        var west = S("CX_A", -8);
        west.Vertices.AddRange([new(4, 8), new(4, 0), new(0, 0), new(0, 8)]);
        west.Walls.AddRange([W(0, 1, 0, 0), W(1, 2), W(2, 3), W(3, 0)]);

        var east = S("CX_B", -8);
        east.Vertices.AddRange([new(20, 0), new(20, 8), new(24, 8), new(24, 0)]);
        east.Walls.AddRange([W(0, 1, 0, 2), W(1, 2), W(2, 3), W(3, 0)]);

        var lev = new LevFile { LevelName = "TEST", Palette = "SECBASE.PAL" };
        lev.Textures.Add("WALL.BM");
        lev.Sectors.AddRange([main, west, east]);

        var door = new DoorInfo("flag", "DOOR.BM", "FRAME.BM", []);
        var meta = new RoomMetadata
        {
            Id = id,
            Palette = "SECBASE.PAL",
            Bounds = new RoomBounds { MinX = 0, MinZ = 0, MaxX = 24, MaxZ = 8, MinY = -16, MaxY = 0 },
            Connectors = [new ConnectorInfo("A", Facing.W, 0, door), new ConnectorInfo("B", Facing.E, 0, door)],
            Traversal = [new TraversalEdge("A", "B", []), new TraversalEdge("B", "A", [])],
        };
        return new RoomPackage(meta, lev, new ObjFile(), new InfFile());
    }

    /// <summary>The corridor with the Death Star plans in the middle, declared as a goal.</summary>
    public static RoomPackage WithPlans(string id = "plans-corridor", bool declare = true)
    {
        var room = Build(id);
        room.Objects.Frames.Add("IDPLANS.FME");
        room.Objects.Objects.Add(new DfObject { Class = "FRAME", Data = 0, X = 12, Y = 0, Z = 4, Difficulty = 1, Seq = [new SeqEntry("LOGIC", ["PLANS"])] });
        room.Metadata.Resources.Frames.Add("IDPLANS.FME");
        if (declare)
            room.Metadata.Goals.Add(new GoalInfo("PLANS", 12, 0, 4, []));
        return room;
    }
}

public class ProgressionTests
{
    static GobArchive FakeGame()
    {
        var gob = new GobArchive();
        gob.Put("JEDI.LVL", DfText.Encode("LEVELS 1\r\nSecret Base, SECBASE, x\r\n"));
        return gob;
    }

    static RandomizerSettings Settings(int rooms, ulong seed, Action<ProgressionSettings>? tweak = null)
    {
        var s = new RandomizerSettings { Seed = seed };
        s.Layout.RoomCount = rooms;
        tweak?.Invoke(s.Progression);
        return s;
    }

    static (LevFile Lev, ObjFile Obj, InfFile Inf, string Gol) Files(GeneratedLevel g) => (
        LevFile.Parse(DfText.Decode(g.Gob.Get("SECBASE.LEV"))),
        ObjFile.Parse(DfText.Decode(g.Gob.Get("SECBASE.O"))),
        InfFile.Parse(DfText.Decode(g.Gob.Get("SECBASE.INF"))),
        DfText.Decode(g.Gob.Get("SECBASE.GOL")));

    [Fact]
    public void Corridor_room_is_valid()
    {
        Assert.Empty(RoomValidator.Validate(CorridorRoom.Build()));
    }

    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(17UL)] [InlineData(99UL)] [InlineData(12345UL)]
    public void Every_seed_gives_a_finishable_level_with_keys_before_locks(ulong seed)
    {
        var g = Generator.Generate(Settings(5, seed), [CorridorRoom.Build()], FakeGame());
        var plan = g.Progression;

        Assert.InRange(plan.Locks.Count, 1, 3);
        Assert.Equal(plan.Locks.Select(l => l.Color).Distinct().Order(), plan.Keys.Select(k => k.Color).Order());
        Assert.NotNull(plan.Exit);

        var (lev, obj, inf, gol) = Files(g);
        // One key: line per lock, on the lock's door stub; one key pickup per color.
        Assert.Equal(plan.Locks.Count, inf.Items.Count(i => i.Statements.Any(s => s.Key == "key")));
        Assert.Equal(plan.Keys.Count, obj.Objects.Count(o => o.Logics.Any(l => LogicCatalog.Keys.Contains(LogicCatalog.BaseName(l)))));
        // Exit trigger, the complete elevator and its sector, and the GOL goal.
        Assert.Contains(inf.Items, i => i.Classes.Any(c => c.Kind == "trigger") &&
                                        i.Statements.Any(s => s.Key == "message" && s.Args.SequenceEqual(["complete", "1"])));
        Assert.Contains(inf.Items, i => i.Name == LevelMerger.CompleteElevator && i.Statements.Any(s => s.Args.Contains("complete")));
        Assert.Contains(lev.Sectors, s => s.Name == LevelMerger.CompleteElevator && s.Walls.All(w => w.Adjoin < 0));
        Assert.Contains("TRIG:", gol);
        // Keys sit inside a room sector on its floor.
        foreach (var k in plan.Keys)
            Assert.Contains(lev.Sectors, s => Geo.Contains(s, k.X, k.Z) && Math.Abs(s.FloorAltitude - k.Y) < 1e-6);
    }

    [Fact]
    public void Solver_rejects_a_key_behind_its_own_door()
    {
        var settings = Settings(3, 7, p => { p.LockedDoorsMin = 1; p.LockedDoorsMax = 1; });
        var layout = new LayoutGenerator([CorridorRoom.Build()], settings.Layout, new SeededRandom(7)).Generate();
        var planner = new ProgressionPlanner(layout, settings.Progression, new SeededRandom(7));
        var plan = planner.Plan();
        Assert.True(planner.IsBeatable(plan, out _));

        // Move the key to the far side of its lock (the room of the join's second instance, never the start).
        var join = layout.Joins[plan.Locks[0].JoinIndex];
        var behind = join.InstanceB == layout.StartInstance ? join.InstanceA : join.InstanceB;
        var bad = plan with { Keys = [plan.Keys[0] with { Instance = behind }] };
        Assert.False(planner.IsBeatable(bad, out var why));
        Assert.Contains(plan.Locks[0].Color, why);
    }

    [Fact]
    public void Knobs_turn_locks_and_exit_off()
    {
        var g = Generator.Generate(Settings(4, 3, p => { p.LockedDoors = false; p.Exit = false; }), [CorridorRoom.Build()], FakeGame());
        var (_, obj, inf, gol) = Files(g);
        Assert.Empty(g.Progression.Locks);
        Assert.DoesNotContain(inf.Items, i => i.Statements.Any(s => s.Key == "key"));
        Assert.DoesNotContain(inf.Items, i => i.Name == LevelMerger.CompleteElevator);
        Assert.DoesNotContain(obj.Objects, o => o.Logics.Any(l => LogicCatalog.Keys.Contains(LogicCatalog.BaseName(l))));
        Assert.DoesNotContain("GOAL:", gol);
    }

    [Fact]
    public void Lock_count_is_capped_by_joins_with_doors_and_logged()
    {
        var g = Generator.Generate(Settings(2, 5, p => { p.LockedDoorsMin = 3; p.LockedDoorsMax = 3; }), [CorridorRoom.Build()], FakeGame());
        Assert.Single(g.Progression.Locks);
        Assert.Contains(g.Log, l => l.Contains("wanted 3 locked door(s), only 1"));
    }

    [Fact]
    public void Single_color_is_reused_for_every_lock()
    {
        var g = Generator.Generate(Settings(5, 11, p => { p.KeyColors = "yellow"; p.LockedDoorsMin = 3; p.LockedDoorsMax = 3; }),
                                   [CorridorRoom.Build()], FakeGame());
        Assert.All(g.Progression.Locks, l => Assert.Equal("YELLOW", l.Color));
        Assert.Equal("YELLOW", Assert.Single(g.Progression.Keys).Color);
    }

    [Fact]
    public void Goal_items_must_be_declared()
    {
        Assert.DoesNotContain(RoomValidator.Validate(CorridorRoom.WithPlans()), f => f.Severity == Severity.Error);
        Assert.Contains(RoomValidator.Validate(CorridorRoom.WithPlans(declare: false)), f => f.Rule == "O2");

        var missing = CorridorRoom.Build();
        missing.Metadata.Goals.Add(new GoalInfo("PLANS", 12, 0, 4, []));
        Assert.Contains(RoomValidator.Validate(missing), f => f.Rule == "M8");
    }

    [Fact]
    public void A_goal_room_keeps_its_goal_once_and_the_mission_needs_goal_and_exit()
    {
        var g = Generator.Generate(Settings(4, 9), [CorridorRoom.WithPlans()], FakeGame());
        var (_, obj, inf, gol) = Files(g);

        var goal = Assert.Single(g.Progression.Goals);
        Assert.Equal("PLANS", goal.Item);
        Assert.Single(obj.Objects, o => LogicCatalog.GoalItemOf(o) == "PLANS");   // 4 copies of the room, one plans
        Assert.Contains(g.Log, l => l.Contains("removed (already in the level)"));

        Assert.Matches(@"GOAL:\s+0\s+ITEM:\s+0", gol);                         // PLANS is goal item 0
        Assert.Contains("TRIG:", gol);
        // Plans pickup + exit trigger: two hold stops, then complete.
        var stops = inf.Items.Single(i => i.Name == LevelMerger.CompleteElevator).Statements.Where(s => s.Key == "stop").ToList();
        Assert.Equal([["@0", "hold"], ["@1", "hold"], ["@2", "complete"]], stops.Select(s => s.Args.ToArray()));
    }

    [Fact]
    public void Goals_knob_off_removes_goal_items()
    {
        var g = Generator.Generate(Settings(2, 9, p => p.Goals = false), [CorridorRoom.WithPlans()], FakeGame());
        var (_, obj, inf, gol) = Files(g);
        Assert.Empty(g.Progression.Goals);
        Assert.DoesNotContain(obj.Objects, o => LogicCatalog.GoalItemOf(o) != null);
        Assert.DoesNotContain("ITEM:", gol);
        Assert.Equal(2, inf.Items.Single(i => i.Name == LevelMerger.CompleteElevator).Statements.Count(s => s.Key == "stop"));
    }

    [Theory]
    [InlineData("progression.lockedDoorsMin=4", "lockedDoorsMin")]
    [InlineData("progression.keyColors=RED,GREEN", "GREEN")]
    [InlineData("progression.exitPlacement=nearby", "exitPlacement")]
    [InlineData("progression.keyPlacement=under_the_bed", "keyPlacement")]
    public void Invalid_progression_knobs_are_rejected(string assignment, string mentions)
    {
        var s = new RandomizerSettings();
        KnobCatalog.Set(s, assignment);
        Assert.Contains(KnobCatalog.Validate(s), e => e.Contains(mentions));
    }
}
