using DarkForces.Core.Geometry;
using DarkForces.Core.Lev;

namespace DarkForces.Core.Rooms;

/// <summary>A standing spot for the player: position on a floor and a yaw (degrees clockwise from north).</summary>
public readonly record struct Spot(double X, double Y, double Z, double Yaw);

/// <summary>Finds connector parts in a (possibly transformed) room's geometry.</summary>
public static class ConnectorLocator
{
    /// <summary>The adapter (or room) sector the stub's inner wall adjoins.</summary>
    public static int InnerNeighbor(LevFile lev, ConnectorGeometry c) =>
        lev.Sectors[c.StubSector].Walls.Single(w => w.Adjoin >= 0).Adjoin;

    /// <summary>
    /// Just inside the room from a connector (4 units past the adapter's room-side wall), on the room floor,
    /// looking into the room. Works on transformed instances: pass the instance's facing.
    /// </summary>
    public static Spot SpawnInside(LevFile lev, ConnectorGeometry c, Facing worldFacing)
    {
        var inner = InnerNeighbor(lev, c);
        var adapter = lev.Sectors[inner];
        // If the stub adjoins the room directly (no adapter), the room is the inner neighbour itself.
        var shared = adapter.Walls.FirstOrDefault(w => w.Adjoin >= 0 && w.Adjoin != c.StubSector);
        var (room, (a, b)) = shared != null
            ? (lev.Sectors[shared.Adjoin], Geo.Segment(adapter, shared))
            : (adapter, Geo.Segment(lev.Sectors[c.StubSector], lev.Sectors[c.StubSector].Walls.Single(w => w.Adjoin >= 0)));
        var into = Geo.Opposite(worldFacing);
        var (dx, dz) = Geo.Direction(into);
        const double inset = 4;
        var yaw = into switch { Facing.N => 0.0, Facing.E => 90.0, Facing.S => 180.0, _ => 270.0 };
        return new Spot((a.X + b.X) / 2 + dx * inset, room.FloorAltitude, (a.Z + b.Z) / 2 + dz * inset, yaw);
    }
}
