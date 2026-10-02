using System.IO.Compression;
using DarkForces.Core.Gob;
using DarkForces.Core.Text;

namespace DarkForces.Core.Tests;

/// <summary>
/// Sample data, read straight from the WDFUSE 2.10 distribution zip (w1632.zip) in the repository root:
/// the demo GOBs under WDFDEMOS/ and the DF Specs help file.
/// </summary>
static class TestData
{
    public static readonly string Root = FindRoot();

    static readonly Lazy<Dictionary<string, byte[]>> Zip = new(LoadZip);

    public static IEnumerable<string> DemoGobs =>
        Zip.Value.Keys.Where(k => k.StartsWith("WDFDEMOS/") && k.EndsWith(".GOB"))
            .Select(k => k["WDFDEMOS/".Length..])
            .Order(StringComparer.OrdinalIgnoreCase);

    public static TheoryData<string> DemoGobNames()
    {
        var data = new TheoryData<string>();
        foreach (var g in DemoGobs)
            data.Add(g);
        return data;
    }

    public static byte[] DemoGobBytes(string name) => Zip.Value[$"WDFDEMOS/{name}"];

    public static byte[] ZipFile(string path) => Zip.Value[path];

    public static GobArchive Gob(string name) => GobArchive.Read(DemoGobBytes(name));

    public static string Text(string gob, string entry) => DfText.Decode(Gob(gob).Get(entry));

    static Dictionary<string, byte[]> LoadZip()
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(Root, "w1632.zip"));
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries.Where(e => e.Length > 0))
        {
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            files[entry.FullName] = ms.ToArray();
        }
        return files;
    }

    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DfRando.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("repository root (DfRando.slnx) not found");
    }
}
