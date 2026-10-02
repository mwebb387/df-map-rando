using System.Text;
using DarkForces.Core.Text;
using static DarkForces.Core.Text.DfText;

namespace DarkForces.Core.Lev;

/// <summary>A LEV file: level header, texture table and sector geometry.</summary>
/// <remarks>Y axis points down: a higher altitude has a lower (more negative) value.</remarks>
public sealed class LevFile
{
    public string Version { get; set; } = "2.1";
    public string LevelName { get; set; } = "";
    public string Palette { get; set; } = "";
    public string Music { get; set; } = "";
    public double ParallaxX { get; set; } = 1024;
    public double ParallaxY { get; set; } = 1024;
    public List<string> Textures { get; } = [];
    public List<Sector> Sectors { get; } = [];

    /// <summary>Text after the last sector that did not parse (e.g. a stray byte from WDFUSE 2.10 GOBs).</summary>
    public string Trailing { get; set; } = "";

    /// <summary>
    /// True if the source's <c>SECTOR n</c> labels were not 0,1,2,... (LEVMAP output has gaps).
    /// The labels are ignored: ADJOIN refers to a sector's position in the file, and the writer numbers by position.
    /// </summary>
    public bool HadNonSequentialSectorLabels { get; private set; }

    public static LevFile Parse(string text)
    {
        var c = new LineCursor(CleanLines(text), "LEV");
        var lev = new LevFile { Version = c.Expect("LEV") is [var v, ..] ? v : "2.1" };

        while (!c.At("TEXTURES"))
        {
            var t = c.NextTokens();
            var key = t[0].ToUpperInvariant();
            switch (key)
            {
                case "LEVELNAME": lev.LevelName = Arg(t); break;
                case "PALETTE": lev.Palette = Arg(t); break;
                case "MUSIC": lev.Music = Arg(t); break;
                case "PARALLAX":
                    lev.ParallaxX = ParseDouble(t[1]);
                    lev.ParallaxY = ParseDouble(t[2]);
                    break;
                default: throw c.Error($"unknown header line '{string.Join(' ', t)}'");
            }
        }

        var textureCount = ParseInt(c.Expect("TEXTURES")[0]);
        for (var i = 0; i < textureCount; i++)
            lev.Textures.Add(Arg(["", .. c.Expect("TEXTURE:")]));

        var sectorCount = ParseInt(c.Expect("NUMSECTORS")[0]);
        for (var i = 0; i < sectorCount; i++)
        {
            if (c.At("SECTOR") && c.PeekTokens() is [_, var label, ..] && label != I(i))
                lev.HadNonSequentialSectorLabels = true;
            lev.Sectors.Add(ParseSector(c));
        }

        lev.Trailing = c.Rest();
        return lev;
    }

    static string Arg(string[] t) => t.Length > 1 ? t[1] : "";

    static Sector ParseSector(LineCursor c)
    {
        c.Expect("SECTOR");
        var s = new Sector();
        while (!c.At("VERTICES"))
        {
            var t = c.NextTokens();
            var key = t[0].ToUpperInvariant();
            var sub = t.Length > 1 ? t[1].ToUpperInvariant() : "";
            switch (key, sub)
            {
                case ("NAME", _): s.Name = Arg(t); break;
                case ("AMBIENT", _): s.Ambient = ParseInt(t[1]); break;
                case ("FLOOR", "TEXTURE"): s.FloorTexture = SurfaceTexture.Parse(t[2..]); break;
                case ("FLOOR", "ALTITUDE"): s.FloorAltitude = ParseDouble(t[2]); break;
                case ("CEILING", "TEXTURE"): s.CeilingTexture = SurfaceTexture.Parse(t[2..]); break;
                case ("CEILING", "ALTITUDE"): s.CeilingAltitude = ParseDouble(t[2]); break;
                case ("SECOND", "ALTITUDE"): s.SecondAltitude = ParseDouble(t[2]); break;
                case ("FLAGS", _): s.Flags1 = ParseLong(t[1]); s.Flags2 = ParseLong(t[2]); s.Flags3 = ParseLong(t[3]); break;
                case ("LAYER", _): s.Layer = ParseInt(t[1]); break;
                default: throw c.Error($"unknown sector line '{string.Join(' ', t)}'");
            }
        }

        var vertexCount = ParseInt(c.Expect("VERTICES")[0]);
        for (var i = 0; i < vertexCount; i++)
        {
            var kv = new KeyValues(c.NextTokens());
            s.Vertices.Add(new Vertex(ParseDouble(kv.Require("X", "vertex")[0]), ParseDouble(kv.Require("Z", "vertex")[0])));
        }

        var wallCount = ParseInt(c.Expect("WALLS")[0]);
        for (var i = 0; i < wallCount; i++)
        {
            var tokens = new List<string>(c.Expect("WALL"));
            // Tolerate a wall definition split across lines (continuations start with a "KEY:" token).
            while (!c.Eof && c.PeekTokens() is [var first, ..] && first.EndsWith(':'))
                tokens.AddRange(c.NextTokens());
            s.Walls.Add(Wall.Parse(new KeyValues(tokens)));
        }
        return s;
    }

    public string Write()
    {
        var w = new StringBuilder();
        void L(string line) => w.Append(line).Append(NewLine);

        L($"LEV {Version}");
        L($"LEVELNAME {LevelName}");
        L($"PALETTE   {Palette}");
        L($"MUSIC     {Music}");
        L($"PARALLAX  {F(ParallaxX)} {F(ParallaxY)}");
        L($"TEXTURES {Textures.Count}");
        for (var i = 0; i < Textures.Count; i++)
            L($" TEXTURE: {Textures[i],-12} # {i}");
        L($"NUMSECTORS {Sectors.Count}");
        for (var si = 0; si < Sectors.Count; si++)
        {
            var s = Sectors[si];
            L("");
            L($"SECTOR {si}");
            L($" NAME             {s.Name}".TrimEnd());
            L($" AMBIENT          {s.Ambient}");
            L($" FLOOR TEXTURE    {s.FloorTexture}");
            L($" FLOOR ALTITUDE   {F(s.FloorAltitude)}");
            L($" CEILING TEXTURE  {s.CeilingTexture}");
            L($" CEILING ALTITUDE {F(s.CeilingAltitude)}");
            L($" SECOND ALTITUDE  {F(s.SecondAltitude)}");
            L($" FLAGS            {I(s.Flags1)} {I(s.Flags2)} {I(s.Flags3)}");
            L($" LAYER            {s.Layer}");
            L($" VERTICES {s.Vertices.Count}");
            for (var i = 0; i < s.Vertices.Count; i++)
                L($"  X: {F(s.Vertices[i].X)} Z: {F(s.Vertices[i].Z)} # {i}");
            L($" WALLS {s.Walls.Count}");
            foreach (var wall in s.Walls)
                L($"  {wall}");
        }
        return w.ToString();
    }
}

public sealed class Sector
{
    public string Name { get; set; } = "";
    public int Ambient { get; set; }
    public SurfaceTexture FloorTexture { get; set; } = new(-1, 0, 0, 0);
    public double FloorAltitude { get; set; }
    public SurfaceTexture CeilingTexture { get; set; } = new(-1, 0, 0, 0);
    public double CeilingAltitude { get; set; }
    public double SecondAltitude { get; set; }
    public long Flags1 { get; set; }
    public long Flags2 { get; set; }
    public long Flags3 { get; set; }
    public int Layer { get; set; }
    public List<Vertex> Vertices { get; private set; } = [];
    public List<Wall> Walls { get; private set; } = [];

    /// <summary>Deep copy (vertices and walls included).</summary>
    public Sector Clone()
    {
        var c = (Sector)MemberwiseClone();
        c.Vertices = [.. Vertices];
        c.Walls = [.. Walls.Select(w => w.Clone())];
        return c;
    }
}

public readonly record struct Vertex(double X, double Z);

/// <summary>Texture index, two offsets and a trailing int the engine ignores (absent on SIGN).</summary>
public readonly record struct SurfaceTexture(int Index, double OffsetX, double OffsetY, int? Unused)
{
    public static SurfaceTexture Parse(IReadOnlyList<string> v) => new(
        ParseInt(v[0]),
        v.Count > 1 ? ParseDouble(v[1]) : 0,
        v.Count > 2 ? ParseDouble(v[2]) : 0,
        v.Count > 3 ? ParseInt(v[3]) : null);

    public override string ToString() =>
        $"{Index} {F(OffsetX)} {F(OffsetY)}" + (Unused is { } u ? $" {u}" : "");
}

public sealed class Wall
{
    public int Left { get; set; }
    public int Right { get; set; }
    public SurfaceTexture Mid { get; set; }
    public SurfaceTexture Top { get; set; }
    public SurfaceTexture Bot { get; set; }
    public SurfaceTexture Sign { get; set; } = new(-1, 0, 0, null);
    public int Adjoin { get; set; } = -1;
    public int Mirror { get; set; } = -1;
    public int Walk { get; set; } = -1;
    public long Flags1 { get; set; }
    public long Flags2 { get; set; }
    public long Flags3 { get; set; }
    public int Light { get; set; }

    public Wall Clone() => (Wall)MemberwiseClone();

    internal static Wall Parse(KeyValues kv)
    {
        const string ctx = "WALL";
        var flags = kv.Require("FLAGS", ctx);
        return new Wall
        {
            Left = ParseInt(kv.Require("LEFT", ctx)[0]),
            Right = ParseInt(kv.Require("RIGHT", ctx)[0]),
            Mid = SurfaceTexture.Parse(kv.Require("MID", ctx)),
            Top = SurfaceTexture.Parse(kv.Require("TOP", ctx)),
            Bot = SurfaceTexture.Parse(kv.Require("BOT", ctx)),
            Sign = SurfaceTexture.Parse(kv.Require("SIGN", ctx)),
            Adjoin = ParseInt(kv.Require("ADJOIN", ctx)[0]),
            Mirror = ParseInt(kv.Require("MIRROR", ctx)[0]),
            Walk = ParseInt(kv.Get("WALK")?[0] ?? kv.Require("ADJOIN", ctx)[0]),
            Flags1 = ParseLong(flags[0]),
            Flags2 = ParseLong(flags[1]),
            Flags3 = ParseLong(flags[2]),
            Light = ParseInt(kv.Require("LIGHT", ctx)[0]),
        };
    }

    public override string ToString() =>
        $"WALL LEFT: {Left} RIGHT: {Right} MID: {Mid} TOP: {Top} BOT: {Bot} SIGN: {Sign} " +
        $"ADJOIN: {Adjoin} MIRROR: {Mirror} WALK: {Walk} FLAGS: {I(Flags1)} {I(Flags2)} {I(Flags3)} LIGHT: {Light}";
}
