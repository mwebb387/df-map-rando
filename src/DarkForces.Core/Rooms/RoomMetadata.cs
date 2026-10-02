using System.Text.Json;
using System.Text.Json.Serialization;
using DarkForces.Core.Geometry;

namespace DarkForces.Core.Rooms;

/// <summary><c>room.json</c> — see docs/room-spec.md §8.</summary>
public sealed class RoomMetadata
{
    public int Spec { get; set; } = RoomRules.SpecVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public RoomSource? Source { get; set; }
    public string Palette { get; set; } = "";
    public bool Exterior { get; set; }
    public bool Rotatable { get; set; } = true;
    public RoomBounds Bounds { get; set; } = new();
    public List<ConnectorInfo> Connectors { get; set; } = [];
    public List<TraversalEdge> Traversal { get; set; } = [];
    public List<ItemSlot> ItemSlots { get; set; } = [];

    /// <summary>Mission goal items the room keeps in ROOM.O (e.g. the Death Star plans); see rule O2.</summary>
    public List<GoalInfo> Goals { get; set; } = [];
    public List<StartPoint> StartPoints { get; set; } = [];
    public RoomResources Resources { get; set; } = new();
    public List<string> Tags { get; set; } = [];

    /// <summary>Free-form notes, e.g. what an extractor changed or what still needs a human check.</summary>
    public List<string> Notes { get; set; } = [];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static RoomMetadata Parse(string json) =>
        JsonSerializer.Deserialize<RoomMetadata>(json, Json) ?? throw new InvalidDataException("room.json is empty");

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>Where a room was extracted from (stock-level rooms only).</summary>
public sealed record RoomSource(string Level, List<int> Sectors);

/// <summary>Room extent. Y points down: <see cref="MinY"/> is the highest ceiling, <see cref="MaxY"/> the lowest floor.</summary>
public sealed class RoomBounds
{
    public double MinX { get; set; }
    public double MinZ { get; set; }
    public double MaxX { get; set; }
    public double MaxZ { get; set; }
    public double MinY { get; set; }
    public double MaxY { get; set; }
}

/// <param name="Door">The room's original door at this doorway, rebuilt by the generator when the connector is joined.</param>
public sealed record ConnectorInfo(string Id, Facing Facing, double Floor, DoorInfo? Door = null);

/// <summary>
/// An original door, kept as data: when two connectors join, the generator turns one of the two stubs into this door
/// (one working door per join). Unjoined, the stub's portal shows <see cref="Panel"/>, like a shut door.
/// </summary>
/// <param name="Kind"><c>flag</c> (sector DOOR flag, an "instant door") or the INF elevator class (door, door_mid, door_inv, ...).</param>
/// <param name="Panel">Texture name seen on the closed door (the frame's TOP texture over the door sector).</param>
/// <param name="Frame">Texture name of the door sector's side walls.</param>
/// <param name="Inf">The door's INF statements other than class/stop/key, e.g. <c>speed: 10</c>, <c>sound: 1 door.voc</c>.</param>
/// <param name="Key">The original key requirement, for reference; locks are decided by the progression knobs.</param>
public sealed record DoorInfo(string Kind, string Panel, string Frame, List<string> Inf, string? Key = null);

/// <param name="From">Connector or item-slot id.</param>
/// <param name="To">Connector or item-slot id.</param>
/// <param name="Requires">Item logic names needed to cross (O2 names), empty if none.</param>
public sealed record TraversalEdge(string From, string To, List<string> Requires, bool OneWay = false, string? Note = null);

public sealed record ItemSlot(string Id, double X, double Y, double Z, List<string> ReachableFrom, List<string> Requires);

/// <summary>A goal item that stays in the room: <paramref name="Item"/> is its logic (PLANS, PHRIK, NAVA, DATATAPE, DT_WEAPON, PILE).</summary>
/// <param name="ReachableFrom">Connectors the item can be reached from; empty means all of them.</param>
public sealed record GoalInfo(string Item, double X, double Y, double Z, List<string> ReachableFrom);

public sealed record StartPoint(double X, double Y, double Z, double Yaw);

public sealed class RoomResources
{
    public List<string> Pods { get; set; } = [];
    public List<string> Sprites { get; set; } = [];
    public List<string> Frames { get; set; } = [];
    public List<string> Sounds { get; set; } = [];
}
