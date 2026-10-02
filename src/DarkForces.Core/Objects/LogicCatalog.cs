namespace DarkForces.Core.Objects;

/// <summary>Object logic classifications used by the room rules (docs/room-spec.md §6).</summary>
public static class LogicCatalog
{
    static HashSet<string> Set(IEnumerable<string> s) => new(s, StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> Keys =
        Set(["RED", "BLUE", "YELLOW", .. Enumerable.Range(1, 9).Select(i => $"CODE{i}")]);

    public static readonly IReadOnlySet<string> GoalItems = Set(["PLANS", "PHRIK", "NAVA", "DATATAPE", "DT_WEAPON", "PILE"]);

    /// <summary>GOL <c>ITEM:</c> number of each goal item (Df_specs, GOL files).</summary>
    public static readonly IReadOnlyDictionary<string, int> GoalNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["PLANS"] = 0, ["PHRIK"] = 1, ["NAVA"] = 2, ["DATATAPE"] = 4, ["DT_WEAPON"] = 5, ["PILE"] = 6 };

    public static readonly IReadOnlySet<string> GatingItems = Set(["CLEATS", "MASK", "GOGGLES"]);

    public static readonly IReadOnlySet<string> KeyCarriers =
        Set(["I_OFFICERR", "I_OFFICERB", "I_OFFICERY", .. Enumerable.Range(1, 9).Select(i => $"I_OFFICER{i}")]);

    /// <summary>O2: items only the generator may place.</summary>
    public static readonly IReadOnlySet<string> Progression = Set([.. Keys, .. GoalItems, .. GatingItems, .. KeyCarriers]);

    /// <summary>
    /// A logic value reduced to its item/enemy name: <c>ITEM RED</c> -> RED, <c>GENERATOR STORM1</c> -> STORM1.
    /// </summary>
    public static string BaseName(string logic)
    {
        var parts = logic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        return parts[0].ToUpperInvariant() is "ITEM" or "GENERATOR" && parts.Length > 1
            ? parts[1].ToUpperInvariant()
            : parts[0].ToUpperInvariant();
    }

    public static bool IsPlayer(DfObject o) => o.Logics.Any(l => BaseName(l) == "PLAYER");

    public static bool IsProgression(DfObject o) => o.Logics.Any(l => Progression.Contains(BaseName(l)));

    /// <summary>The object's goal-item logic (PLANS, ...), or null.</summary>
    public static string? GoalItemOf(DfObject o) => o.Logics.Select(BaseName).FirstOrDefault(GoalItems.Contains);

    /// <summary>O5: VUE motion (LOGIC: KEY, or a VUE:/VUE_APPEND: entry).</summary>
    public static bool UsesVue(DfObject o) =>
        o.Logics.Any(l => BaseName(l) == "KEY") ||
        (o.Seq?.Any(e => e.Key.StartsWith("VUE", StringComparison.OrdinalIgnoreCase)) ?? false);
}
