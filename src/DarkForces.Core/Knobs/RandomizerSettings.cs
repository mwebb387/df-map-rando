
namespace DarkForces.Core.Knobs;

/// <summary>
/// Everything that decides what the generator produces, besides the room library itself.
/// The same seed + settings + rooms always produce a byte-identical GOB.
/// </summary>
public sealed class RandomizerSettings
{
    /// <summary>Knob schema version; presets record it (docs/knobs.md principle 6).</summary>
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;

    public ulong Seed { get; set; }

    [KnobSection] public LayoutSettings Layout { get; set; } = new();
    [KnobSection] public ProgressionSettings Progression { get; set; } = new();
    [KnobSection] public OutputSettings Output { get; set; } = new();
}

public sealed class LayoutSettings
{
    [Knob("Number of room instances placed.", Min = 1, Max = 64)]
    public int RoomCount { get; set; } = 3;

    [Knob("Allow 90-degree rotations (rooms with rotatable: false are never rotated).")]
    public bool AllowRotation { get; set; } = true;

    [Knob("Allow the same room to be placed more than once.")]
    public bool AllowRepeats { get; set; } = true;

    [Knob("Layout attempts before giving up (each attempt restarts from an empty level).", Min = 1, Max = 100000)]
    public int Attempts { get; set; } = 200;

    [Knob("Generate hallways: always when no room can join a doorway directly, sometimes otherwise (see hallwayChance). Hallways do not count toward roomCount.")]
    public bool Hallways { get; set; } = true;

    [Knob("Chance (0-1) of a hallway between two rooms even when they could join directly.", Min = 0, Max = 1)]
    public double HallwayChance { get; set; } = 0.3;

    [Knob("Shortest hallway leg, in DF units (rounded up to the 8-unit grid; stairs may need more).", Min = 8, Max = 512)]
    public int HallwayMinLength { get; set; } = 16;

    [Knob("Longest hallway leg, in DF units (rounded down to the 8-unit grid).", Min = 8, Max = 512)]
    public int HallwayMaxLength { get; set; } = 48;

    [Knob("Largest floor height change across a hallway, built as stairs (3 units per step at most).", Min = 0, Max = 128)]
    public int HallwayMaxRise { get; set; } = 8;

    [Knob("Allow L-shaped hallways, which connect doorways that face at right angles.")]
    public bool HallwayTurns { get; set; } = true;
}

public sealed class ProgressionSettings
{
    [Knob("Add a level exit that completes the mission (an unused doorway alcove, lit up).")]
    public bool Exit { get; set; } = true;

    [Knob("Keep the goal items rooms declare (e.g. the Death Star plans) as mission objectives: the mission ends once " +
          "every goal is taken and the exit reached. Off removes them. A repeated room keeps its goal only once.")]
    public bool Goals { get; set; } = true;

    [Knob("Where the exit goes: 'far' = the room farthest from the start, 'random' = any room but the start's.",
          Choices = ["far", "random"])]
    public string ExitPlacement { get; set; } = "far";

    [Knob("Lock some of the joined doors with keys (keys are always reachable before their doors).")]
    public bool LockedDoors { get; set; } = true;

    [Knob("Fewest locked doors (capped by how many joins have a door).", Min = 0, Max = 32)]
    public int LockedDoorsMin { get; set; } = 1;

    [Knob("Most locked doors (capped by how many joins have a door).", Min = 0, Max = 32)]
    public int LockedDoorsMax { get; set; } = 3;

    [Knob("Key colors that may be used, comma-separated (RED, BLUE, YELLOW). Doors beyond the color count share colors.")]
    public string KeyColors { get; set; } = "RED,BLUE,YELLOW";

    [Knob("Where keys go: 'anywhere' reachable, or 'far' = the reachable room farthest from the start.",
          Choices = ["anywhere", "far"])]
    public string KeyPlacement { get; set; } = "anywhere";

    public static readonly string[] AllKeyColors = ["RED", "BLUE", "YELLOW"];

    public List<string> ParsedKeyColors() =>
        [.. KeyColors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant()).Distinct()];
}

public sealed class OutputSettings
{
    [Knob("Mission (JEDI.LVL level name) the generated level replaces.")]
    public string LevelSlot { get; set; } = "SECBASE";

    [Knob("Mission-menu name shown for the generated level.")]
    public string LevelTitle { get; set; } = "Randomized";

    [Knob("Also write a .zip containing the GOB (The Force Engine loads mods from zip files).")]
    public bool Zip { get; set; } = true;

    [Knob("Store the seed and settings in the GOB (RANDO.TXT) so the level can be reproduced.")]
    public bool EmbedSettings { get; set; } = true;
}
