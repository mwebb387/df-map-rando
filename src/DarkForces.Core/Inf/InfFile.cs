using System.Text;
using DarkForces.Core.Text;
using static DarkForces.Core.Text.DfText;

namespace DarkForces.Core.Inf;

/// <summary>
/// An INF file. Items keep their seq body as an ordered statement list; <see cref="InfItem.Classes"/>
/// groups statements under the <c>class:</c> they follow.
/// </summary>
public sealed class InfFile
{
    public string Version { get; set; } = "1.0";
    public string LevelName { get; set; } = "";
    public List<InfItem> Items { get; } = [];

    /// <summary>The ITEMS count as written in the source file (not always accurate in the wild).</summary>
    public int DeclaredItemCount { get; set; }

    public string Trailing { get; set; } = "";

    public static InfFile Parse(string text)
    {
        var c = new LineCursor(CleanLines(text), "INF");
        var inf = new InfFile { Version = c.Expect("INF") is [var v, ..] ? v : "1.0" };
        if (c.At("LEVELNAME"))
            inf.LevelName = c.Expect("LEVELNAME") is [var n, ..] ? n : "";
        inf.DeclaredItemCount = ParseInt(c.Expect("ITEMS")[0]);

        // Parse every item present rather than trusting the declared count.
        while (c.At("item:"))
            inf.Items.Add(InfItem.Parse(c));

        inf.Trailing = c.Rest();
        return inf;
    }

    public string Write()
    {
        var w = new StringBuilder();
        void L(string line) => w.Append(line).Append(NewLine);

        L($"INF {Version}");
        L($"LEVELNAME {LevelName}");
        L("");
        L($"items {Items.Count}");
        foreach (var item in Items)
        {
            L("");
            item.Write(L);
        }
        return w.ToString();
    }
}

public sealed class InfItem
{
    /// <summary>sector, line or level.</summary>
    public string Type { get; set; } = "sector";

    /// <summary>Sector name (absent for <c>item: level</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Wall number for <c>item: line</c>.</summary>
    public int? Num { get; set; }

    public List<InfStatement> Statements { get; } = [];

    public bool IsLine => KeyIs(Type, "line");

    /// <summary>Deep copy (statement argument lists included).</summary>
    public InfItem Clone()
    {
        var c = new InfItem { Type = Type, Name = Name, Num = Num };
        c.Statements.AddRange(Statements.Select(s => s with { Args = [.. s.Args] }));
        return c;
    }

    internal static InfItem Parse(LineCursor c)
    {
        var kv = new KeyValues(c.NextTokens());
        var item = new InfItem
        {
            Type = kv.Require("item", "INF item") is [var t, ..] ? t : "",
            Name = kv.Get("name") is [var n, ..] ? n : null,
            Num = kv.Get("num") is [var num, ..] ? ParseInt(num) : null,
        };

        c.Expect("seq");
        var tokens = new List<string>();
        while (!c.At("seqend"))
            tokens.AddRange(c.NextTokens());
        c.Next();

        var body = new KeyValues(tokens);
        if (body.Leading.Count > 0)
            throw c.Error($"statement without a key in item '{item.Name}': {string.Join(' ', body.Leading)}");
        foreach (var (key, values) in body.Pairs)
            item.Statements.Add(new InfStatement(key.ToLowerInvariant(), values));
        return item;
    }

    internal void Write(Action<string> L)
    {
        var header = $"item: {Type}";
        if (Name != null) header += $" name: {Name}";
        if (Num != null) header += $" num: {Num}";
        L(header);
        L(" seq");
        foreach (var s in Statements)
            L((s.Key == "class" ? "   " : "     ") + s);
        L(" seqend");
    }

    /// <summary>Statements grouped by the <c>class:</c> that precedes them. Statements before any class go in a group with Kind "".</summary>
    public IEnumerable<InfClass> Classes
    {
        get
        {
            var current = new InfClass("", "", []);
            foreach (var s in Statements)
            {
                if (s.Key == "class")
                {
                    if (current.Kind != "" || current.Statements.Count > 0)
                        yield return current;
                    current = new InfClass(
                        s.Args.Count > 0 ? s.Args[0].ToLowerInvariant() : "",
                        string.Join(' ', s.Args.Skip(1)).ToLowerInvariant(),
                        []);
                }
                else
                {
                    current.Statements.Add(s);
                }
            }
            if (current.Kind != "" || current.Statements.Count > 0)
                yield return current;
        }
    }
}

/// <summary>One <c>key: args...</c> statement inside seq/seqend. Keys are stored lower-case without the colon.</summary>
public sealed record InfStatement(string Key, List<string> Args)
{
    public override string ToString() => $"{Key}: {string.Join(' ', Args)}".TrimEnd();
}

/// <param name="Kind">elevator, trigger or teleporter.</param>
/// <param name="Subtype">e.g. move_floor, switch1, chute; empty for a plain <c>class: trigger</c>.</param>
public sealed record InfClass(string Kind, string Subtype, List<InfStatement> Statements);
