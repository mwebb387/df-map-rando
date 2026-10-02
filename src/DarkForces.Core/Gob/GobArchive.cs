using System.Buffers.Binary;
using System.Text;

namespace DarkForces.Core.Gob;

/// <summary>
/// GOB archive: "GOB\x0A", int32 index offset, file data, then
/// int32 count and count x { int32 offset; int32 length; char[13] name }.
/// </summary>
/// <remarks>
/// Raw name fields and bytes trailing the index are kept so tool-written GOBs
/// (e.g. WDFUSE 2.10, which leaves garbage after the NUL) rewrite byte-identically.
/// </remarks>
public sealed class GobArchive
{
    static ReadOnlySpan<byte> Magic => "GOB\n"u8;
    const int HeaderSize = 8;
    const int NameSize = 13;
    const int EntrySize = 8 + NameSize;

    public List<GobEntry> Entries { get; } = [];

    /// <summary>Bytes after the index in the source file; written back unchanged.</summary>
    public byte[] Trailer { get; set; } = [];

    public GobEntry? Find(string name) =>
        Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    public byte[] Get(string name) => Find(name)?.Data ?? throw new KeyNotFoundException(name);

    public void Put(string name, byte[] data)
    {
        var existing = Find(name);
        if (existing != null)
            existing.Data = data;
        else
            Entries.Add(new GobEntry(name, data));
    }

    public static GobArchive Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..4].SequenceEqual(Magic))
            throw new InvalidDataException("not a GOB file");
        var indexOffset = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(data[indexOffset..]);

        var gob = new GobArchive();
        var pos = indexOffset + 4;
        for (var i = 0; i < count; i++, pos += EntrySize)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(data[(pos + 4)..]);
            var rawName = data.Slice(pos + 8, NameSize).ToArray();
            var nul = Array.IndexOf(rawName, (byte)0);
            var name = Encoding.ASCII.GetString(rawName, 0, nul < 0 ? NameSize : nul);
            gob.Entries.Add(new GobEntry(name, data.Slice(offset, length).ToArray()) { RawName = rawName });
        }
        gob.Trailer = data[pos..].ToArray();
        return gob;
    }

    public static GobArchive Load(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Writes data in entry order after the header, index last (the layout DF's own GOBs use).</summary>
    public byte[] Write()
    {
        var dataSize = Entries.Sum(e => e.Data.Length);
        var buf = new byte[HeaderSize + dataSize + 4 + Entries.Count * EntrySize + Trailer.Length];
        var span = buf.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderSize + dataSize);

        var pos = HeaderSize;
        var idx = HeaderSize + dataSize;
        BinaryPrimitives.WriteInt32LittleEndian(span[idx..], Entries.Count);
        idx += 4;
        foreach (var e in Entries)
        {
            e.Data.CopyTo(span[pos..]);
            BinaryPrimitives.WriteInt32LittleEndian(span[idx..], pos);
            BinaryPrimitives.WriteInt32LittleEndian(span[(idx + 4)..], e.Data.Length);
            e.EncodeName().CopyTo(span[(idx + 8)..]);
            pos += e.Data.Length;
            idx += EntrySize;
        }
        Trailer.CopyTo(span[idx..]);
        return buf;
    }

    public void Save(string path) => File.WriteAllBytes(path, Write());

    /// <summary>Writes every entry to <paramref name="directory"/> (created if needed).</summary>
    public void ExtractTo(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var e in Entries)
            File.WriteAllBytes(Path.Combine(directory, e.Name), e.Data);
    }

    /// <summary>Builds a GOB from the files directly inside <paramref name="directory"/>, ordered by name, names upper-cased.</summary>
    public static GobArchive FromDirectory(string directory)
    {
        var gob = new GobArchive();
        foreach (var file in Directory.GetFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
            gob.Entries.Add(new GobEntry(Path.GetFileName(file).ToUpperInvariant(), File.ReadAllBytes(file)));
        return gob;
    }

    public sealed class GobEntry(string name, byte[] data)
    {
        public string Name { get; set; } = name;
        public byte[] Data { get; set; } = data;

        /// <summary>Original 13-byte name field; reused only while it still decodes to <see cref="Name"/>.</summary>
        public byte[]? RawName { get; init; }

        internal byte[] EncodeName()
        {
            if (RawName != null)
            {
                var nul = Array.IndexOf(RawName, (byte)0);
                if (nul >= 0 && Encoding.ASCII.GetString(RawName, 0, nul) == Name)
                    return RawName;
            }
            var bytes = Encoding.ASCII.GetBytes(Name);
            if (bytes.Length > NameSize - 1)
                throw new InvalidOperationException($"GOB entry name longer than 12 characters: {Name}");
            var field = new byte[NameSize];
            bytes.CopyTo(field, 0);
            return field;
        }
    }
}
