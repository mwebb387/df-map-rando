using DarkForces.Core.Geometry;
using DarkForces.Core.Gob;
using DarkForces.Core.Gol;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;
using DarkForces.Core.Text;

namespace DarkForces.Core.Authoring;

/// <summary>
/// Wraps one room package in a user GOB that replaces a mission (default SECBASE), so the room can be
/// walked on its own: <c>dark -uARC41.GOB</c>, then start that mission (or load the zip in The Force Engine).
/// The player and a SAFE point start just inside one connector, facing into the room; connectors stay sealed.
/// </summary>
public static class RoomPlaytest
{
    public static GobArchive Build(RoomPackage room, GobArchive darkGob, string slot, string? connectorId)
    {
        var (x, floor, z, yaw) = Spawn(room, connectorId);

        var lev = room.Lev; // written under the slot's name; palette stays the room's own
        lev.LevelName = slot;

        var obj = new ObjFile { LevelName = slot };
        obj.Pods.AddRange(room.Objects.Pods);
        obj.Sprites.AddRange(room.Objects.Sprites);
        obj.Frames.AddRange(room.Objects.Frames);
        obj.Sounds.AddRange(room.Objects.Sounds);
        obj.Objects.Add(new DfObject
        {
            Class = "SPIRIT", X = x, Y = floor, Z = z, Yaw = yaw, Difficulty = 1,
            Seq = [new SeqEntry("LOGIC", ["PLAYER"]), new SeqEntry("EYE", ["TRUE"])],
        });
        obj.Objects.Add(new DfObject { Class = "SAFE", X = x, Y = floor, Z = z, Yaw = yaw, Difficulty = 1 });
        obj.Objects.AddRange(room.Objects.Objects.Where(o => !LogicCatalog.IsPlayer(o)).Select(o => o.Clone()));

        var inf = room.Inf;
        inf.LevelName = slot;

        var gob = new GobArchive();
        gob.Put($"{slot}.LEV", DfText.Encode(lev.Write()));
        gob.Put($"{slot}.O", DfText.Encode(obj.Write()));
        gob.Put($"{slot}.INF", DfText.Encode(inf.Write()));
        gob.Put($"{slot}.GOL", DfText.Encode(new GolFile().Write()));

        // The level's colormap must match its palette; ship the room palette's CMP under the slot's name.
        var cmp = Path.ChangeExtension(lev.Palette, ".CMP");
        if (darkGob.Find(cmp) is { } cmpEntry)
            gob.Put($"{slot}.CMP", cmpEntry.Data);
        return gob;
    }

    /// <summary>
    /// Where the player starts: just inside connector <paramref name="connectorId"/> when given; otherwise the
    /// room's start point (player object, else room.json), otherwise just inside the first connector.
    /// </summary>
    public static Spot Spawn(RoomPackage room, string? connectorId)
    {
        if (connectorId == null && RoomStarts.Of(room) is { Point: var p })
            return new Spot(p.X, p.Y, p.Z, p.Yaw);
        var connectors = RoomValidator.FindConnectors(room.Lev);
        var c = connectorId == null
            ? connectors.FirstOrDefault() ?? throw new InvalidOperationException("room has no connectors")
            : connectors.SingleOrDefault(x => string.Equals(x.Id, connectorId, StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"no connector '{connectorId}' (the room has {string.Join(", ", connectors.Select(x => x.Id))})");
        var facing = c.Facing ?? throw new InvalidOperationException($"connector {c.Id} is not axis-aligned");
        return ConnectorLocator.SpawnInside(room.Lev, c, facing);
    }
}
