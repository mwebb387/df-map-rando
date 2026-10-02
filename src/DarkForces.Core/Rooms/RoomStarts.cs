using DarkForces.Core.Lev;
using DarkForces.Core.Objects;

namespace DarkForces.Core.Rooms;

/// <summary>Where a room's start point came from.</summary>
public enum StartSource { PlayerObject, Metadata }

public sealed record RoomStart(StartPoint Point, StartSource Source);

/// <summary>
/// A room's start point: its player object (<c>LOGIC: PLAYER</c> in ROOM.O) if it has one, otherwise the first
/// <c>startPoints</c> entry in room.json. The player object itself never reaches a generated level: the generator
/// (and playtest) remove it and spawn the player at this point.
/// </summary>
public static class RoomStarts
{
    public static RoomStart? Of(RoomPackage room) => Find(room.Objects, room.Metadata, p => p);

    /// <summary>For a placed room: positions and yaw in world space.</summary>
    public static RoomStart? Of(RoomInstance inst) => Find(inst.Objects, inst.Source.Metadata, p =>
    {
        // Player objects in an instance are already transformed; metadata points are room-local.
        var v = inst.Placement.Apply(new Vertex(p.X, p.Z));
        return new StartPoint(v.X, p.Y + inst.Placement.Dy, v.Z, inst.Placement.ApplyAngle(p.Yaw));
    });

    static RoomStart? Find(ObjFile objects, RoomMetadata meta, Func<StartPoint, StartPoint> placeMetadata)
    {
        if (objects.Objects.FirstOrDefault(LogicCatalog.IsPlayer) is { } player)
            return new RoomStart(new StartPoint(player.X, player.Y, player.Z, player.Yaw), StartSource.PlayerObject);
        return meta.StartPoints.Count > 0 ? new RoomStart(placeMetadata(meta.StartPoints[0]), StartSource.Metadata) : null;
    }
}
