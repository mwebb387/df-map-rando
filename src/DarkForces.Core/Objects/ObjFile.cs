using System.Text;
using DarkForces.Core.Text;
using static DarkForces.Core.Text.DfText;

namespace DarkForces.Core.Objects;

/// <summary>An O file: resource tables (3DO/WAX/FME/VOC) and the object list.</summary>
public sealed class ObjFile
{
    public string Version { get; set; } = "1.1";
    public string LevelName { get; set; } = "";
    public List<string> Pods { get; } = [];
    public List<string> Sprites { get; } = [];
    public List<string> Frames { get; } = [];
    public List<string> Sounds { get; } = [];
    public List<DfObject> Objects { get; } = [];
    public string Trailing { get; set; } = "";

    /// <summary>The resource table a class's DATA: index refers to, or null for SPIRIT/SAFE.</summary>
    public List<string>? TableFor(string objectClass) => objectClass.ToUpperInvariant() switch
    {
        "3D" => Pods,
        "SPRITE" => Sprites,
        "FRAME" => Frames,
        "SOUND" => Sounds,
        _ => null,
    };

    static readonly (string Count, string Entry)[] TableKeys =
        [("PODS", "POD:"), ("SPRS", "SPR:"), ("FMES", "FME:"), ("SOUNDS", "SOUND:")];

    List<string> Table(int i) => i switch { 0 => Pods, 1 => Sprites, 2 => Frames, _ => Sounds };

    public static ObjFile Parse(string text)
    {
        var c = new LineCursor(CleanLines(text), "O");
        var o = new ObjFile { Version = c.Expect("O") is [var v, ..] ? v : "1.1" };
        if (c.At("LEVELNAME"))
            o.LevelName = c.Expect("LEVELNAME") is [var n, ..] ? n : "";

        for (var ti = 0; ti < TableKeys.Length; ti++)
        {
            var (countKey, entryKey) = TableKeys[ti];
            var count = ParseInt(c.Expect(countKey)[0]);
            for (var i = 0; i < count; i++)
                o.Table(ti).Add(c.Expect(entryKey) is [var name, ..] ? name : "");
        }

        var objectCount = ParseInt(c.Expect("OBJECTS")[0]);
        for (var i = 0; i < objectCount; i++)
            o.Objects.Add(DfObject.Parse(c));

        o.Trailing = c.Rest();
        return o;
    }

    public string Write()
    {
        var w = new StringBuilder();
        void L(string line) => w.Append(line).Append(NewLine);

        L($"O {Version}");
        L($"LEVELNAME {LevelName}");
        for (var ti = 0; ti < TableKeys.Length; ti++)
        {
            var (countKey, entryKey) = TableKeys[ti];
            var table = Table(ti);
            L("");
            L($"{countKey} {table.Count}");
            for (var i = 0; i < table.Count; i++)
                L($" {entryKey} {table[i],-12} # {i}");
        }
        L("");
        L($"OBJECTS {Objects.Count}");
        for (var i = 0; i < Objects.Count; i++)
        {
            L($"/* {i} */");
            Objects[i].Write(L);
        }
        return w.ToString();
    }
}

public sealed class DfObject
{
    /// <summary>SPIRIT, SAFE, SPRITE, FRAME, 3D or SOUND.</summary>
    public string Class { get; set; } = "SPIRIT";
    public int Data { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }
    public double Roll { get; set; }
    public int Difficulty { get; set; } = 1;

    /// <summary>SEQ ... SEQEND body, one entry per "KEY: values" pair, or null if the object has no SEQ.</summary>
    public List<SeqEntry>? Seq { get; set; }

    /// <summary>
    /// Header keys beyond the standard set, kept and written back after DIFF:.
    /// WDFUSE writes <c>SEC: n</c> (its record of the containing sector; not read by the game).
    /// </summary>
    public List<SeqEntry> ExtraHeader { get; private set; } = [];

    /// <summary>Deep copy (SEQ entries and extra header keys included).</summary>
    public DfObject Clone()
    {
        var c = (DfObject)MemberwiseClone();
        c.Seq = Seq?.Select(e => e with { Values = [.. e.Values] }).ToList();
        c.ExtraHeader = [.. ExtraHeader.Select(e => e with { Values = [.. e.Values] })];
        return c;
    }

    static readonly HashSet<string> KnownHeaderKeys =
        new(["CLASS", "DATA", "X", "Y", "Z", "PCH", "YAW", "ROL", "DIFF"], StringComparer.OrdinalIgnoreCase);

    internal static DfObject Parse(LineCursor c)
    {
        var tokens = new List<string>(c.NextTokens());
        // The header may be split across lines; it ends with DIFF: n.
        while (!tokens.Any(t => KeyIs(t, "DIFF:")))
            tokens.AddRange(c.NextTokens());
        var kv = new KeyValues(tokens);
        const string ctx = "object";
        var o = new DfObject
        {
            Class = kv.Require("CLASS", ctx)[0],
            Data = ParseInt(kv.Require("DATA", ctx)[0]),
            X = ParseDouble(kv.Require("X", ctx)[0]),
            Y = ParseDouble(kv.Require("Y", ctx)[0]),
            Z = ParseDouble(kv.Require("Z", ctx)[0]),
            Pitch = ParseDouble(kv.Require("PCH", ctx)[0]),
            Yaw = ParseDouble(kv.Require("YAW", ctx)[0]),
            Roll = ParseDouble(kv.Require("ROL", ctx)[0]),
            Difficulty = ParseInt(kv.Require("DIFF", ctx)[0]),
        };
        foreach (var (key, values) in kv.Pairs)
            if (!KnownHeaderKeys.Contains(key))
                o.ExtraHeader.Add(new SeqEntry(key, values));

        if (c.At("SEQ"))
        {
            c.Next();
            o.Seq = [];
            while (!c.At("SEQEND"))
            {
                var line = new KeyValues(c.NextTokens());
                if (line.Leading.Count > 0)
                    o.Seq.Add(new SeqEntry("", line.Leading));
                foreach (var (key, values) in line.Pairs)
                    o.Seq.Add(new SeqEntry(key, values));
            }
            c.Next();
        }
        return o;
    }

    internal void Write(Action<string> L)
    {
        var extra = string.Concat(ExtraHeader.Select(e => $" {e.Key}: {string.Join(' ', e.Values)}"));
        L($"CLASS: {Class,-6} DATA: {Data} X: {F(X)} Y: {F(Y)} Z: {F(Z)} PCH: {F(Pitch)} YAW: {F(Yaw)} ROL: {F(Roll)} DIFF: {Difficulty}{extra}");
        if (Seq == null)
            return;
        L(" SEQ");
        foreach (var e in Seq)
            L("  " + e);
        L(" SEQEND");
    }

    /// <summary>All LOGIC:/TYPE: values (the keywords are interchangeable).</summary>
    public IEnumerable<string> Logics =>
        Seq?.Where(e => KeyIs(e.Key, "LOGIC") || KeyIs(e.Key, "TYPE")).Select(e => string.Join(' ', e.Values)) ?? [];
}

public sealed record SeqEntry(string Key, List<string> Values)
{
    public override string ToString() =>
        Key.Length == 0 ? string.Join(' ', Values) : $"{Key + ":",-10} {string.Join(' ', Values)}".TrimEnd();
}
