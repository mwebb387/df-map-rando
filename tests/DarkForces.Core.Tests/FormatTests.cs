using DarkForces.Core.Gol;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Lvl;
using DarkForces.Core.Objects;

namespace DarkForces.Core.Tests;

public class LevFileTests
{
    [Fact]
    public void Parses_header_textures_and_first_sector()
    {
        var lev = LevFile.Parse(TestData.Text("DEMO1.GOB", "SECBASE.LEV"));
        Assert.Equal("2.1", lev.Version);
        Assert.Equal("SECBASE.PAL", lev.Palette);
        Assert.Equal(11, lev.Textures.Count);
        Assert.Equal("IDFUEL1.BM", lev.Textures[0]);
        Assert.Equal(22, lev.Sectors.Count);

        var s = lev.Sectors[0];
        Assert.Equal("start_point", s.Name);
        Assert.Equal(31, s.Ambient);
        Assert.Equal(-16, s.CeilingAltitude);
        Assert.Equal(12, s.Vertices.Count);
        Assert.Equal(new Vertex(68, 364), s.Vertices[0]);
        Assert.Equal(12, s.Walls.Count);
        Assert.Equal(14, s.Walls[4].Adjoin);
        Assert.Equal(new SurfaceTexture(0, 11.31, 0, 0), s.Walls[1].Mid);
    }

    [Fact]
    public void Ignores_trailing_garbage_from_wdfuse_210()
    {
        var lev = LevFile.Parse(TestData.Text("DEMO1.GOB", "SECBASE.LEV"));
        Assert.Equal("0", lev.Trailing);
        Assert.True(lev.HadNonSequentialSectorLabels);
    }

    [Theory]
    [MemberData(nameof(TestData.DemoGobNames), MemberType = typeof(TestData))]
    public void Adjoins_are_mutual(string gobName)
    {
        // Sanity check that parsing is positionally right: every ADJOIN/MIRROR pair must point back.
        var lev = LevFile.Parse(TestData.Text(gobName, "SECBASE.LEV"));
        for (var si = 0; si < lev.Sectors.Count; si++)
        {
            var walls = lev.Sectors[si].Walls;
            for (var wi = 0; wi < walls.Count; wi++)
            {
                var w = walls[wi];
                if (w.Adjoin < 0) continue;
                var back = lev.Sectors[w.Adjoin].Walls[w.Mirror];
                Assert.True(back.Adjoin == si && back.Mirror == wi,
                    $"{gobName}: sector {si} wall {wi} -> {w.Adjoin}/{w.Mirror} does not point back");
            }
        }
    }
}

public class InfFileTests
{
    [Fact]
    public void Parses_items_and_groups_statements_by_class()
    {
        var inf = InfFile.Parse(TestData.Text("DEMO3.GOB", "SECBASE.INF"));
        Assert.Equal(5, inf.Items.Count);
        Assert.Equal(5, inf.DeclaredItemCount);

        var spin = inf.Items.Single(i => i.Name == "morphspin01");
        var cls = Assert.Single(spin.Classes);
        Assert.Equal("elevator", cls.Kind);
        Assert.Equal("morph_spin2", cls.Subtype);
        Assert.Equal(["72", "440"], cls.Statements.Single(s => s.Key == "center").Args);

        var platform = inf.Items.Single(i => i.Name == "simple_platform");
        Assert.Equal([["0", "hold"], ["16", "10"]],
            platform.Statements.Where(s => s.Key == "stop").Select(s => s.Args));
    }

    [Fact]
    public void Parses_line_items_and_messages()
    {
        var inf = InfFile.Parse(TestData.Text("TUTOR001.GOB", "SECBASE.INF"));
        var bottom = inf.Items.Single(i => i.Name == "bottom");
        Assert.True(bottom.IsLine);
        Assert.Equal(5, bottom.Num);
        var trigger = Assert.Single(bottom.Classes);
        Assert.Equal(("trigger", "toggle"), (trigger.Kind, trigger.Subtype));
        Assert.Equal(["2147483656"], trigger.Statements.Single(s => s.Key == "entity_mask").Args);

        var movefloor = inf.Items.Single(i => i.Name == "movefloor1");
        Assert.Contains(movefloor.Statements, s => s.Key == "message" && s.Args.SequenceEqual(["1", "moveceiling1", "next_stop"]));
    }
}

public class ObjFileTests
{
    [Fact]
    public void Parses_tables_objects_and_sequences()
    {
        var o = ObjFile.Parse(TestData.Text("DEMO1.GOB", "SECBASE.O"));
        Assert.Equal(2, o.Objects.Count);
        var player = o.Objects[0];
        Assert.Equal("SPIRIT", player.Class);
        Assert.Equal((32.0, -4.0, 396.0, 60.0), (player.X, player.Y, player.Z, player.Yaw));
        Assert.Equal(["PLAYER"], player.Logics);
        Assert.Null(o.TableFor("SAFE"));
    }

    [Fact]
    public void Keeps_wdfuse_sector_hint()
    {
        var o = ObjFile.Parse(TestData.Text("TUTOR001.GOB", "SECBASE.O"));
        var extra = Assert.Single(o.Objects[1].ExtraHeader);
        Assert.Equal(("SEC", "1"), (extra.Key, extra.Values.Single()));
        Assert.Contains(" SEC: 1", o.Write());
    }
}

public class GolAndLvlTests
{
    [Fact]
    public void Parses_goals()
    {
        var gol = GolFile.Parse(TestData.Text("DEMO1.GOB", "SECBASE.GOL"));
        Assert.Equal([new Goal(0, GoalKind.Item, 0), new Goal(1, GoalKind.Trig, 1)], gol.Goals);
    }

    [Fact]
    public void Parses_level_list()
    {
        var lvl = JediLvl.Parse(TestData.Text("TUTOR001.GOB", "JEDI.LVL"));
        Assert.Equal(14, lvl.Levels.Count);
        Assert.Equal(new LevelEntry("Secret Base", "SECBASE", lvl.Levels[0].Paths), lvl.Levels[0]);
        Assert.Equal("ARC", lvl.Levels[^1].Name);
    }
}
