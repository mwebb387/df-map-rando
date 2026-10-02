using System.Text;
using DarkForces.Core.Text;
using static DarkForces.Core.Text.DfText;

namespace DarkForces.Core.Lvl;

/// <param name="Description">Name shown in the mission menu.</param>
/// <param name="Name">Level file base name (SECBASE -> SECBASE.LEV/.O/.INF/.GOL).</param>
/// <param name="Paths">Development-time search paths; unused by the game.</param>
public sealed record LevelEntry(string Description, string Name, string Paths);

/// <summary>JEDI.LVL: the mission list.</summary>
public sealed class JediLvl
{
    public List<LevelEntry> Levels { get; } = [];
    public string Trailing { get; set; } = "";

    public static JediLvl Parse(string text)
    {
        var c = new LineCursor(CleanLines(text), "JEDI.LVL");
        var count = ParseInt(c.Expect("LEVELS")[0]);
        var lvl = new JediLvl();
        for (var i = 0; i < count; i++)
        {
            var parts = c.Next().Split(',', 3);
            if (parts.Length < 2)
                throw c.Error("level entry needs 'Description, NAME, paths'");
            lvl.Levels.Add(new LevelEntry(parts[0].Trim(), parts[1].Trim(), parts.Length > 2 ? parts[2].Trim() : ""));
        }
        lvl.Trailing = c.Rest();
        return lvl;
    }

    public string Write()
    {
        var w = new StringBuilder();
        w.Append($"LEVELS {Levels.Count}").Append(NewLine);
        foreach (var l in Levels)
            w.Append($"{l.Description + ",",-20} {l.Name + ",",-10} {l.Paths}".TrimEnd()).Append(NewLine);
        // DF requires comments at the end of this file.
        w.Append(NewLine).Append("//      (Comments must be at the end of this file)").Append(NewLine);
        return w.ToString();
    }
}
