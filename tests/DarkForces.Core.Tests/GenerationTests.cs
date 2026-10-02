using System.IO.Compression;
using DarkForces.Core.Generation;
using DarkForces.Core.Geometry;
using DarkForces.Core.Gob;
using DarkForces.Core.Knobs;
using DarkForces.Core.Lev;
using DarkForces.Core.Output;
using DarkForces.Core.Rooms;
using DarkForces.Core.Text;

namespace DarkForces.Core.Tests;

public class PlacementTests
{
    [Theory]
    [InlineData(0, 1, 2, 1, 2)]
    [InlineData(1, 1, 2, 2, -1)]   // clockwise: north -> east
    [InlineData(2, 1, 2, -1, -2)]
    [InlineData(3, 1, 2, -2, 1)]
    [InlineData(-1, 1, 2, -2, 1)]
    public void Rotates_points_clockwise(int turns, double x, double z, double ex, double ez)
    {
        Assert.Equal(new Vertex(ex, ez), new Placement(turns, 0, 0, 0).Apply(new Vertex(x, z)));
    }

    [Fact]
    public void Rotates_facings_and_angles_with_points()
    {
        var p = new Placement(1, 0, 0, 0);
        Assert.Equal([Facing.E, Facing.S, Facing.W, Facing.N], new[] { Facing.N, Facing.E, Facing.S, Facing.W }.Select(p.Apply));
        Assert.Equal(0, p.ApplyAngle(270));
        Assert.Equal(315, new Placement(3, 0, 0, 0).ApplyAngle(45 + 360));
    }
}

public class RoomTransformerTests
{
    [Fact]
    public void Transformed_room_stays_valid_and_moves_everything()
    {
        var room = SampleRoom.Build();
        room.Objects.Objects.Add(SampleRoom.Object(4, 4, "ITEM", "SHIELD"));
        var p = new Placement(1, 16, -8, 40);
        var inst = RoomTransformer.Apply(room, p, "R1_");

        Assert.Equal("R1_main", inst.Lev.Sectors[0].Name);
        Assert.Equal(p.Apply(new Vertex(0, 8)), inst.Lev.Sectors[0].Vertices[1]);
        Assert.Equal(-8, inst.Lev.Sectors[0].FloorAltitude);
        Assert.Equal(-24, inst.Lev.Sectors[0].CeilingAltitude);
        Assert.True(Geo.SignedArea(inst.Lev.Sectors[0]) < 0, "rotation must keep clockwise winding");

        var o = inst.Objects.Objects[0];
        Assert.Equal((p.Apply(new Vertex(4, 4)).X, -8.0, p.Apply(new Vertex(4, 4)).Z, 90.0), (o.X, o.Y, o.Z, o.Yaw));
        Assert.Equal(0, room.Lev.Sectors[0].FloorAltitude); // source untouched
    }

    [Fact]
    public void Rewrites_inf_per_the_rule_table()
    {
        var room = SampleRoom.Build();
        room.Inf.Items.Add(SampleRoom.Item("main",
            ("class", ["elevator", "move_floor"]),
            ("stop", ["-20", "hold"]),        // absolute altitude (INF points up: LEV altitude 20): shifts by -dy
            ("stop", ["@8", "3"]),            // relative: untouched
            ("slave", ["main"]),
            ("message", ["1", "main(2)", "done"])));
        room.Inf.Items.Add(SampleRoom.Item("main",
            ("class", ["elevator", "change_light"]),
            ("stop", ["0", "hold"])));         // light level: untouched
        room.Inf.Items.Add(SampleRoom.Item("main",
            ("class", ["elevator", "scroll_floor"]),
            ("angle", ["90"]),                // world direction: rotates
            ("center", ["4", "4"])));

        var p = new Placement(1, 8, 5, 0);
        var inf = RoomTransformer.Apply(room, p, "R2_").Inf;
        var lift = inf.Items[0].Statements;
        Assert.Equal("R2_main", inf.Items[0].Name);
        Assert.Equal(["-25", "hold"], lift[1].Args); // the room moved 5 down (LEV 20 -> 25), so the stop does too
        Assert.Equal(["@8", "3"], lift[2].Args);
        Assert.Equal(["R2_main"], lift[3].Args);
        Assert.Equal(["1", "R2_main(2)", "done"], lift[4].Args);
        Assert.Equal(["0", "hold"], inf.Items[1].Statements[1].Args);
        Assert.Equal(["180"], inf.Items[2].Statements[1].Args);
        var c = p.Apply(new Vertex(4, 4));
        Assert.Equal([c.X.ToString(), c.Z.ToString()], inf.Items[2].Statements[2].Args);
    }

    [Fact]
    public void Rejects_off_grid_translation_and_rotating_fixed_rooms()
    {
        var room = SampleRoom.Build();
        Assert.Throws<ArgumentException>(() => RoomTransformer.Apply(room, new Placement(0, 4, 0, 0), "R1_"));
        room.Metadata.Rotatable = false;
        Assert.Throws<ArgumentException>(() => RoomTransformer.Apply(room, new Placement(1, 0, 0, 0), "R1_"));
    }
}

public class KnobTests
{
    [Fact]
    public void Catalog_lists_declared_knobs_with_defaults()
    {
        var k = KnobCatalog.All.Single(x => x.Path == "layout.roomCount");
        Assert.Equal(3, k.Default);
        Assert.Equal("1..64", k.Range);
        Assert.Contains(KnobCatalog.All, x => x.Path == "output.zip" && Equals(x.Default, true));
    }

    [Fact]
    public void Set_parses_types_and_rejects_unknown_knobs()
    {
        var s = new RandomizerSettings();
        KnobCatalog.Set(s, "layout.roomCount=7");
        KnobCatalog.Set(s, "Output.Zip = false");
        KnobCatalog.Set(s, "seed=42");
        Assert.Equal((7, false, 42UL), (s.Layout.RoomCount, s.Output.Zip, s.Seed));
        Assert.Throws<ArgumentException>(() => KnobCatalog.Set(s, "layout.nope=1"));
        Assert.Throws<ArgumentException>(() => KnobCatalog.Set(s, "layout.roomCount=many"));
    }

    [Fact]
    public void Validate_enforces_declared_ranges()
    {
        var s = new RandomizerSettings();
        s.Layout.RoomCount = 0;
        Assert.Contains(KnobCatalog.Validate(s), e => e.StartsWith("layout.roomCount"));
    }

    [Fact]
    public void Presets_round_trip_and_text_seeds_are_stable()
    {
        var s = new RandomizerSettings { Seed = 9 };
        s.Layout.AllowRotation = false;
        var back = KnobCatalog.LoadPreset(KnobCatalog.ToJson(s));
        Assert.Equal((9UL, false), (back.Seed, back.Layout.AllowRotation));
        Assert.Equal(KnobCatalog.ParseSeed("ice cleats"), KnobCatalog.ParseSeed("ice cleats"));
        Assert.NotEqual(KnobCatalog.ParseSeed("ice cleats"), KnobCatalog.ParseSeed("gas mask"));
    }
}

public class SeededRandomTests
{
    [Fact]
    public void Sequence_is_fixed_for_a_seed()
    {
        // Golden values: if these change, every previously shared seed produces a different level.
        var r = new SeededRandom(1);
        Assert.Equal([0xB3F2AF6D0FC710C5UL, 0x853B559647364CEAUL], new[] { r.NextUInt64(), r.NextUInt64() });
    }

    [Fact]
    public void Next_stays_in_range()
    {
        var r = new SeededRandom(7);
        Assert.All(Enumerable.Range(0, 1000).Select(_ => r.Next(5)), v => Assert.InRange(v, 0, 4));
    }
}

public class GeneratorTests
{
    static GobArchive FakeGame()
    {
        var gob = new GobArchive();
        gob.Put("JEDI.LVL", DfText.Encode("LEVELS 2\r\nSecret Base, SECBASE, x\r\nTalay, TALAY, x\r\n"));
        gob.Put("SECBASE.CMP", [1, 2, 3]);
        return gob;
    }

    static RoomPackage DoorRoom()
    {
        var room = SampleRoom.Build();
        room.Metadata.Id = "door-room";
        room.Metadata.Connectors[0] = room.Metadata.Connectors[0] with { Door = new DoorInfo("flag", "DOOR.BM", "FRAME.BM", []) };
        return room;
    }

    static RandomizerSettings Settings(int rooms, ulong seed = 5)
    {
        var s = new RandomizerSettings { Seed = seed };
        s.Layout.RoomCount = rooms;
        return s;
    }

    [Fact]
    public void Joins_two_rooms_portal_to_portal_with_one_door()
    {
        var settings = Settings(2);
        settings.Progression.LockedDoors = false; // a locked door is an INF door; this test covers the plain flag door
        var result = Generator.Generate(settings, [DoorRoom()], FakeGame());
        var join = Assert.Single(result.Layout.Joins);
        var lev = LevFile.Parse(DfText.Decode(result.Gob.Get("SECBASE.LEV")));

        var stubs = lev.Sectors.Select((s, i) => (s, i)).Where(p => p.s.Name.Contains("CX_")).ToList();
        Assert.Equal(2, stubs.Count);
        foreach (var (s, i) in stubs)
        {
            var portal = s.Walls[2];
            Assert.True(portal.Adjoin >= 0, $"{s.Name} portal not adjoined");
            Assert.Equal((i, 2), (lev.Sectors[portal.Adjoin].Walls[portal.Mirror].Adjoin, portal.Mirror));
        }
        Assert.Single(lev.Sectors, s => (s.Flags1 & 2) != 0);
        Assert.Contains("DOOR.BM", lev.Textures);
        Assert.Contains(result.Log, l => l.Contains("flag door"));
    }

    [Fact]
    public void Same_seed_and_settings_give_identical_bytes()
    {
        byte[] Run(ulong seed) => Generator.Generate(Settings(2, seed), [DoorRoom()], FakeGame()).Gob.Write();
        Assert.Equal(Run(11), Run(11));
    }

    [Fact]
    public void Replaces_the_slot_title_and_embeds_settings()
    {
        var s = Settings(2);
        s.Output.LevelTitle = "Rando, test";
        var gob = Generator.Generate(s, [DoorRoom()], FakeGame()).Gob;
        Assert.Contains("Rando  test,", DfText.Decode(gob.Get("JEDI.LVL")));
        Assert.Contains("\"seed\":5", DfText.Decode(gob.Get(Generator.SettingsFile)));
        Assert.Equal([1, 2, 3], gob.Get("SECBASE.CMP"));
    }

    [Fact]
    public void Impossible_layouts_fail_with_a_knob_hint()
    {
        var s = Settings(3);
        s.Layout.AllowRepeats = false;
        var ex = Assert.Throws<InvalidOperationException>(() => Generator.Generate(s, [DoorRoom()], FakeGame()));
        Assert.Contains("layout.allowRepeats", ex.Message);
    }

    [Fact]
    public void Rejects_unknown_level_slots()
    {
        var s = Settings(1);
        s.Output.LevelSlot = "NOWHERE";
        Assert.Throws<ArgumentException>(() => Generator.Generate(s, [DoorRoom()], FakeGame()));
    }

    [Fact]
    public void Rooms_mix_when_their_palettes_have_the_same_contents()
    {
        var other = DoorRoom();
        other.Metadata.Id = "other-room";
        other.Lev.Palette = "ARC.PAL";
        var game = FakeGame();
        game.Put("SECBASE.PAL", [9, 9, 9]);
        game.Put("ARC.PAL", [9, 9, 9]);       // same palette and colormap under another name, as in the stock game
        game.Put("ARC.CMP", [1, 2, 3]);
        Assert.NotNull(Generator.Generate(Settings(2), [DoorRoom(), other], game));

        game.Put("ARC.PAL", [7, 7, 7]);
        var ex = Assert.Throws<InvalidOperationException>(() => Generator.Generate(Settings(2), [DoorRoom(), other], game));
        Assert.Contains("ARC.PAL", ex.Message);
    }
}

public class OutputWriterTests
{
    [Fact]
    public void Writes_gob_and_a_reproducible_zip_containing_it()
    {
        var dir = Directory.CreateTempSubdirectory("dfout").FullName;
        try
        {
            var gob = new GobArchive();
            gob.Put("A.LEV", [1, 2, 3]);
            var path = Path.Combine(dir, "TEST.GOB");
            var written = OutputWriter.Write(gob, path, zip: true);
            Assert.Equal([path, Path.Combine(dir, "TEST.zip")], written);
            var first = File.ReadAllBytes(written[1]);
            using (var z = ZipFile.OpenRead(written[1]))
                Assert.Equal("TEST.GOB", Assert.Single(z.Entries).FullName);
            OutputWriter.Write(gob, path, zip: true);
            Assert.Equal(first, File.ReadAllBytes(written[1]));
            Assert.Single(OutputWriter.Write(gob, path, zip: false));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
