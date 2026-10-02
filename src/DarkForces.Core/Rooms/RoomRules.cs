using System.Text.RegularExpressions;

namespace DarkForces.Core.Rooms;

/// <summary>Numeric rules and naming conventions from docs/room-spec.md (spec version 0).</summary>
public static partial class RoomRules
{
    public const int SpecVersion = 0;

    /// <summary>X/Z placement grid; portal wall vertices lie on it (§2, C3).</summary>
    public const int Grid = 8;

    public const double StubWidth = 8;
    public const double StubDepth = 4;

    /// <summary>Connector opening height: ceiling = floor - StubHeight (C6). Matches stock doors (8 high).</summary>
    public const double StubHeight = 8;

    /// <summary>Player metrics (docs/room-spec.md §2): highest walkable step, and the height needed to walk standing.</summary>
    public const double MaxStep = 3.5;
    public const double MinHeight = 6.8;

    public const int MaxConnectors = 4;
    public const double MaxBounds = 256;
    public const int MaxSectors = 64;
    public const int MaxWalls = 512;

    /// <summary>INF-visible sector names: the generator adds an instance prefix, so keep them short (I2).</summary>
    public const int MaxInfNameLength = 12;

    public const string StubPrefix = "CX_";
    public const string AdapterPrefix = "CA_";

    [GeneratedRegex("^CX_([A-Z0-9]{1,4})$")]
    public static partial Regex StubName();

    [GeneratedRegex("^CA_([A-Z0-9]{1,4})$")]
    public static partial Regex AdapterName();

    [GeneratedRegex("^[A-Za-z0-9_.]+$")]
    public static partial Regex InfName();

    public static string StubNameFor(string connectorId) => StubPrefix + connectorId;
    public static string AdapterNameFor(string connectorId) => AdapterPrefix + connectorId;
}
