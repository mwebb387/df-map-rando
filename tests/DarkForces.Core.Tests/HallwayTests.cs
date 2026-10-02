using DarkForces.Core.Generation;
using DarkForces.Core.Geometry;
using DarkForces.Core.Gob;
using DarkForces.Core.Knobs;
using DarkForces.Core.Lev;
using DarkForces.Core.Rooms;
using DarkForces.Core.Text;

namespace DarkForces.Core.Tests;

public class HallwayTests
{
    static readonly HallwayTextures Tex = new("WALL.BM", "FLOOR.BM", "CEIL.BM", "SECBASE.PAL", 20);

    [Theory]
    [InlineData(HallwayShape.Straight, 16, 0, 0)]
    [InlineData(HallwayShape.Straight, 32, 0, 8)]
    [InlineData(HallwayShape.Straight, 24, 0, -7)]
    [InlineData(HallwayShape.Turn, 16, 8, 0)]
    [InlineData(HallwayShape.Turn, 24, 24, 6)]
    public void Built_hallways_are_valid_rooms(HallwayShape shape, int length, int length2, int rise)
    {
        var hall = HallwayBuilder.Build("h", shape, length, length2, rise, Tex);
        Assert.DoesNotContain(RoomValidator.Validate(hall), f => f.Severity == Severity.Error);

        var connectors = RoomValidator.FindConnectors(hall.Lev).ToDictionary(c => c.Id);
        Assert.Equal(Facing.W, connectors["A"].Facing);
        Assert.Equal(shape == HallwayShape.Straight ? Facing.E : Facing.N, connectors["B"].Facing);
        Assert.Equal(0, connectors["A"].Floor);
        Assert.Equal(rise, connectors["B"].Floor);
        Assert.True(HallwayBuilder.IsHallway(hall));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(-13)]
    public void Stairs_never_rise_more_than_a_walkable_step(int rise)
    {
        var hall = HallwayBuilder.Build("h", HallwayShape.Straight, HallwayBuilder.MinLength(rise), 0, rise, Tex);
        var sectors = hall.Lev.Sectors;
        for (var si = 0; si < sectors.Count; si++)
            foreach (var w in sectors[si].Walls.Where(w => w.Adjoin >= 0))
                Assert.InRange(Math.Abs(sectors[si].FloorAltitude - sectors[w.Adjoin].FloorAltitude), 0, HallwayBuilder.MaxStepRise + 1e-9);
    }

    [Theory]
    [InlineData(HallwayShape.Straight, -3)] [InlineData(HallwayShape.Straight, -8)] [InlineData(HallwayShape.Straight, -16)]
    [InlineData(HallwayShape.Straight, 3)] [InlineData(HallwayShape.Straight, 8)] [InlineData(HallwayShape.Turn, -8)]
    [InlineData(HallwayShape.Turn, 6)]
    public void Every_opening_can_be_walked_through_standing(HallwayShape shape, int rise)
    {
        // Regression: a climbing step right off the 8-high stub left a 5-high opening, so the player had to crouch.
        var hall = HallwayBuilder.Build("h", shape, HallwayBuilder.MinLength(rise), 8, rise, Tex);
        var sectors = hall.Lev.Sectors;
        for (var si = 0; si < sectors.Count; si++)
            foreach (var w in sectors[si].Walls.Where(w => w.Adjoin >= 0))
            {
                var (p, q) = (sectors[si], sectors[w.Adjoin]);
                var opening = Math.Min(p.FloorAltitude, q.FloorAltitude) - Math.Max(p.CeilingAltitude, q.CeilingAltitude);
                Assert.True(opening >= RoomRules.MinHeight, $"S{si} -> S{w.Adjoin}: opening {opening}");
            }
        Assert.DoesNotContain(RoomValidator.Validate(hall), f => f.Rule == "G7");
    }

    [Fact]
    public void Validator_warns_about_an_opening_too_low_to_walk_through()
    {
        var hall = HallwayBuilder.Build("h", HallwayShape.Straight, 24, 0, -3, Tex);
        // Recreate the old layout: the first step's floor raised right next to stub A, landing removed.
        var landing = hall.Lev.Sectors[1];
        landing.FloorAltitude = -3;
        landing.CeilingAltitude = -15;
        Assert.Contains(RoomValidator.Validate(hall), f => f.Rule == "G7" && f.Message.Contains("crouch"));
    }

    [Fact]
    public void Rejects_lengths_that_cannot_fit_the_stairs()
    {
        Assert.Throws<ArgumentException>(() => HallwayBuilder.Build("h", HallwayShape.Straight, 12, 0, 0, Tex));          // off grid
        Assert.Throws<ArgumentException>(() => HallwayBuilder.Build("h", HallwayShape.Straight, 16, 0, 30, Tex));         // 10 steps don't fit
    }

    static GobArchive FakeGame()
    {
        var gob = new GobArchive();
        gob.Put("JEDI.LVL", DfText.Encode("LEVELS 1\r\nSecret Base, SECBASE, x\r\n"));
        return gob;
    }

    [Theory]
    [InlineData(1UL)] [InlineData(4UL)] [InlineData(21UL)] [InlineData(777UL)]
    public void Hallway_chance_one_puts_a_hallway_on_every_join(ulong seed)
    {
        var s = new RandomizerSettings { Seed = seed };
        s.Layout.RoomCount = 4;
        s.Layout.HallwayChance = 1;
        var g = Generator.Generate(s, [CorridorRoom.Build()], FakeGame());
        var l = g.Layout;

        var rooms = l.Instances.Count(i => !HallwayBuilder.IsHallway(i.Source));
        Assert.Equal(4, rooms);
        Assert.Equal(3, l.Instances.Count - rooms);
        // Every join has a hallway on exactly one side (room - hallway - room).
        Assert.All(l.Joins, j => Assert.True(
            HallwayBuilder.IsHallway(l.Instances[j.InstanceA].Source) ^ HallwayBuilder.IsHallway(l.Instances[j.InstanceB].Source)));

        // The merged level is sound: every adjoin points back.
        var lev = LevFile.Parse(DfText.Decode(g.Gob.Get("SECBASE.LEV")));
        for (var si = 0; si < lev.Sectors.Count; si++)
            for (var wi = 0; wi < lev.Sectors[si].Walls.Count; wi++)
            {
                var w = lev.Sectors[si].Walls[wi];
                if (w.Adjoin < 0) continue;
                var back = lev.Sectors[w.Adjoin].Walls[w.Mirror];
                Assert.Equal((si, wi), (back.Adjoin, back.Mirror));
            }
        // Keys and the exit stay out of hallways.
        Assert.All(g.Progression.Keys, k => Assert.False(HallwayBuilder.IsHallway(l.Instances[k.Instance].Source)));
        Assert.False(HallwayBuilder.IsHallway(l.Instances[g.Progression.Exit!.Instance].Source));
    }

    [Fact]
    public void Hallways_off_means_rooms_join_directly()
    {
        var s = new RandomizerSettings { Seed = 3 };
        s.Layout.RoomCount = 4;
        s.Layout.Hallways = false;
        var g = Generator.Generate(s, [CorridorRoom.Build()], FakeGame());
        Assert.DoesNotContain(g.Layout.Instances, i => HallwayBuilder.IsHallway(i.Source));
    }

    [Fact]
    public void Hallway_length_knobs_are_checked_together()
    {
        var s = new RandomizerSettings();
        KnobCatalog.Set(s, "layout.hallwayMinLength=64");
        KnobCatalog.Set(s, "layout.hallwayMaxLength=32");
        Assert.Contains(KnobCatalog.Validate(s), e => e.Contains("hallwayMinLength"));
        KnobCatalog.Set(s, "layout.hallwayChance=1.5");
        Assert.Contains(KnobCatalog.Validate(s), e => e.StartsWith("layout.hallwayChance"));
    }
}
