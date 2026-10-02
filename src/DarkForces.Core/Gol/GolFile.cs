using System.Text;
using DarkForces.Core.Text;
using static DarkForces.Core.Text.DfText;

namespace DarkForces.Core.Gol;

public enum GoalKind
{
    /// <summary>Fired when a goal-item logic (PLANS, PHRIK, NAVA, DATATAPE, DT_WEAPON, PILE) is picked up.</summary>
    Item,

    /// <summary>Fired by an INF <c>complete [num]</c> message.</summary>
    Trig,
}

public sealed record Goal(int Number, GoalKind Kind, int Value);

/// <summary>A GOL file: the PDA objective list.</summary>
public sealed class GolFile
{
    public string Version { get; set; } = "1.0";
    public List<Goal> Goals { get; } = [];
    public string Trailing { get; set; } = "";

    public static GolFile Parse(string text)
    {
        var c = new LineCursor(CleanLines(text), "GOL");
        var gol = new GolFile { Version = c.Expect("GOL") is [var v, ..] ? v : "1.0" };
        while (c.At("GOAL:"))
        {
            var kv = new KeyValues(c.NextTokens());
            var number = ParseInt(kv.Require("GOAL", "GOL")[0]);
            gol.Goals.Add(kv.Get("ITEM") is [var item, ..]
                ? new Goal(number, GoalKind.Item, ParseInt(item))
                : new Goal(number, GoalKind.Trig, ParseInt(kv.Require("TRIG", "GOL")[0])));
        }
        gol.Trailing = c.Rest();
        return gol;
    }

    public string Write()
    {
        var w = new StringBuilder();
        w.Append($"GOL {Version}").Append(NewLine);
        foreach (var g in Goals)
            w.Append($"  GOAL: {g.Number}\t{g.Kind.ToString().ToUpperInvariant()}:\t{g.Value}").Append(NewLine);
        return w.ToString();
    }
}
