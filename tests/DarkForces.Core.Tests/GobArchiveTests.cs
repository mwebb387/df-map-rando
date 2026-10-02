using DarkForces.Core.Gob;

namespace DarkForces.Core.Tests;

public class GobArchiveTests
{
    [Theory]
    [MemberData(nameof(TestData.DemoGobNames), MemberType = typeof(TestData))]
    public void Rewrite_is_byte_identical(string gobName)
    {
        var raw = TestData.DemoGobBytes(gobName);
        Assert.Equal(raw, GobArchive.Read(raw).Write());
    }

    [Fact]
    public void Reads_index_names_and_sizes()
    {
        var gob = TestData.Gob("TUTOR001.GOB");
        Assert.Equal(
            ["JEDI.LVL", "SECBASE.CMP", "SECBASE.GOL", "SECBASE.INF", "SECBASE.LEV", "SECBASE.O", "SECBASE.PAL", "TEXT.MSG"],
            gob.Entries.Select(e => e.Name));
        Assert.Equal(768, gob.Get("secbase.pal").Length);
    }

    [Fact]
    public void Renamed_entry_writes_a_clean_name_field()
    {
        var gob = TestData.Gob("DEMO1.GOB"); // WDFUSE 2.10 leaves garbage after the NUL
        gob.Entries[0].Name = "NEW.CMP";
        var reread = GobArchive.Read(gob.Write());
        Assert.Equal("NEW.CMP", reread.Entries[0].Name);
        Assert.Equal(gob.Entries[0].Data, reread.Entries[0].Data);
    }

    [Fact]
    public void Put_replaces_case_insensitively_or_appends()
    {
        var gob = new GobArchive();
        gob.Put("A.LEV", [1]);
        gob.Put("a.lev", [2]);
        gob.Put("B.O", [3]);
        var reread = GobArchive.Read(gob.Write());
        Assert.Equal(2, reread.Entries.Count);
        Assert.Equal([2], reread.Get("A.LEV"));
    }

    [Fact]
    public void Rejects_names_longer_than_8_3()
    {
        var gob = new GobArchive();
        gob.Put("TOOLONGNAME.LEV", []);
        Assert.Throws<InvalidOperationException>(() => gob.Write());
    }

    [Fact]
    public void Extract_then_pack_preserves_contents()
    {
        var dir = Directory.CreateTempSubdirectory("dfgob").FullName;
        try
        {
            var original = TestData.Gob("TUTOR001.GOB");
            original.ExtractTo(dir);
            var packed = GobArchive.Read(GobArchive.FromDirectory(dir).Write());
            foreach (var e in original.Entries)
                Assert.Equal(e.Data, packed.Get(e.Name));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Rejects_non_gob_data()
    {
        Assert.Throws<InvalidDataException>(() => GobArchive.Read("LEV 2.1"u8));
    }
}
