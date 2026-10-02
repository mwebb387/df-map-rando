namespace DarkForces.Core.Inf;

/// <summary>A sector name an INF statement refers to, with the wall number if it was <c>name(wall)</c>.</summary>
public readonly record struct SectorRef(string Sector, int? Wall);

/// <summary>Finds the sector names INF statements refer to (the targets a room transform must rename).</summary>
public static class InfReferences
{
    public static readonly IReadOnlySet<string> Messages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "m_trigger", "goto_stop", "next_stop", "prev_stop", "master_on", "master_off",
        "clear_bits", "set_bits", "complete", "done", "wakeup", "lights",
    };

    /// <summary>
    /// Sector references in one statement:
    /// <c>client:</c>/<c>slave:</c>/<c>target:</c> [name];
    /// <c>message:</c> [stop] [target] [msg] (elevators) — a trigger's <c>message: [msg]</c> has no target;
    /// <c>adjoin:</c> [stop] [sector1] [wall1] [sector2] [wall2];
    /// <c>stop:</c> [sectorname] [time] (a stop that copies another sector's value).
    /// </summary>
    public static IEnumerable<SectorRef> Of(InfStatement s)
    {
        var a = s.Args;
        switch (s.Key)
        {
            case "client" or "slave" or "target" when a.Count > 0:
                yield return Parse(a[0]);
                break;
            case "message" when a.Count > 0:
                if (IsInteger(a[0]) && a.Count >= 2)
                    yield return Parse(a[1]);
                else if (!Messages.Contains(a[0]))
                    yield return Parse(a[0]);
                break;
            case "adjoin" when a.Count >= 5:
                yield return new SectorRef(a[1], IsInteger(a[2]) ? int.Parse(a[2]) : null);
                yield return new SectorRef(a[3], IsInteger(a[4]) ? int.Parse(a[4]) : null);
                break;
            case "stop" when a.Count > 0 && !a[0].StartsWith('@') && !double.TryParse(a[0], System.Globalization.CultureInfo.InvariantCulture, out _):
                yield return Parse(a[0]);
                break;
        }
    }

    public static IEnumerable<SectorRef> Of(InfItem item) => item.Statements.SelectMany(Of);

    static bool IsInteger(string s) => int.TryParse(s, out _);

    static SectorRef Parse(string token)
    {
        var open = token.IndexOf('(');
        if (open > 0 && token.EndsWith(')') && int.TryParse(token[(open + 1)..^1], out var wall))
            return new SectorRef(token[..open], wall);
        return new SectorRef(token, null);
    }
}
