using System.IO.Compression;
using DarkForces.Core.Gob;

namespace DarkForces.Core.Output;

/// <summary>Writes a finished GOB and, optionally, a zip containing it (The Force Engine loads mods from zip files).</summary>
public static class OutputWriter
{
    /// <summary>Fixed entry timestamp so the zip is byte-identical for identical input.</summary>
    static readonly DateTimeOffset EntryTime = new(1995, 2, 15, 0, 0, 0, TimeSpan.Zero);

    /// <returns>Paths written: the GOB, then the zip if <paramref name="zip"/>.</returns>
    public static List<string> Write(GobArchive gob, string gobPath, bool zip)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(gobPath))!;
        Directory.CreateDirectory(dir);
        var bytes = gob.Write();
        File.WriteAllBytes(gobPath, bytes);
        var written = new List<string> { gobPath };
        if (!zip)
            return written;

        var zipPath = Path.ChangeExtension(gobPath, ".zip");
        using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(Path.GetFileName(gobPath), CompressionLevel.Optimal);
            entry.LastWriteTime = EntryTime;
            using var s = entry.Open();
            s.Write(bytes);
        }
        written.Add(zipPath);
        return written;
    }
}
