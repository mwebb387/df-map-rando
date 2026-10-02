using DarkForces.Core.Geometry;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;
using DarkForces.Core.Text;

namespace DarkForces.Core.Generation;

/// <summary>Two connectors joined portal to portal.</summary>
public sealed record Join(int InstanceA, string ConnectorA, int InstanceB, string ConnectorB);

/// <summary>Placed rooms plus how they connect and where the player starts.</summary>
public sealed class LevelLayout
{
    public List<RoomInstance> Instances { get; } = [];
    public List<Join> Joins { get; } = [];
    public int StartInstance { get; set; }
    public string StartConnector { get; set; } = "";

    /// <summary>World facing of a connector of an instance.</summary>
    public Facing FacingOf(int instance, string connector)
    {
        var inst = Instances[instance];
        var c = inst.Source.Metadata.Connectors.Single(x => x.Id == connector);
        return inst.Placement.Apply(c.Facing);
    }
}

public sealed record MergedLevel(LevFile Lev, ObjFile Objects, InfFile Inf, List<string> Log);

/// <summary>
/// Combines placed room instances into one level: renumbers sectors, merges texture and resource tables,
/// adjoins joined portals, rebuilds one original door per join, and adds the player start.
/// </summary>
public static class LevelMerger
{
    /// <summary>INF door classes that can be rebuilt as-is on a stub (their stops come from the sector's heights).</summary>
    static readonly HashSet<string> RebuildableInfDoors = new(StringComparer.OrdinalIgnoreCase) { "door", "door_mid", "door_inv" };

    /// <summary>Name of the INF elevator whose "complete" stop ends the mission (engine convention).</summary>
    public const string CompleteElevator = "complete";

    /// <summary>GOL trigger number the exit fires (<c>message: complete 1</c> -> <c>GOAL: 0 TRIG: 1</c>).</summary>
    public const int ExitGoalTrigger = 1;

    static readonly Dictionary<string, string> KeyFrames = new(StringComparer.OrdinalIgnoreCase)
        { ["RED"] = "IKEYR.FME", ["BLUE"] = "IKEYB.FME", ["YELLOW"] = "IKEYY.FME" };

    public static MergedLevel Merge(LevelLayout layout, string levelName) => Merge(layout, levelName, ProgressionPlan.Empty);

    public static MergedLevel Merge(LevelLayout layout, string levelName, ProgressionPlan plan)
    {
        var log = new List<string>();
        var first = layout.Instances[0].Lev;
        var lev = new LevFile
        {
            LevelName = levelName, Palette = first.Palette, Music = first.Music,
            ParallaxX = first.ParallaxX, ParallaxY = first.ParallaxY,
        };
        var obj = new ObjFile { LevelName = levelName };
        var inf = new InfFile { LevelName = levelName };

        int Texture(string name)
        {
            var i = lev.Textures.FindIndex(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) return i;
            lev.Textures.Add(name);
            return lev.Textures.Count - 1;
        }

        // ---- sectors, textures, adjoins
        var offsets = new List<int>();
        foreach (var inst in layout.Instances)
        {
            var offset = lev.Sectors.Count;
            offsets.Add(offset);
            var map = inst.Lev.Textures.Select(Texture).ToArray();
            SurfaceTexture Remap(SurfaceTexture t) => t.Index >= 0 && t.Index < map.Length ? t with { Index = map[t.Index] } : t;
            foreach (var src in inst.Lev.Sectors)
            {
                var s = src.Clone();
                s.FloorTexture = Remap(s.FloorTexture);
                s.CeilingTexture = Remap(s.CeilingTexture);
                foreach (var w in s.Walls)
                {
                    w.Mid = Remap(w.Mid); w.Top = Remap(w.Top); w.Bot = Remap(w.Bot); w.Sign = Remap(w.Sign);
                    if (w.Adjoin >= 0) { w.Adjoin += offset; w.Walk = w.Adjoin; }
                }
                lev.Sectors.Add(s);
            }
        }

        // ---- objects and INF
        for (var ii = 0; ii < layout.Instances.Count; ii++)
        {
            var inst = layout.Instances[ii];
            foreach (var src in inst.Objects.Objects)
            {
                if (LogicCatalog.GoalItemOf(src) is { } goal && !plan.Keeps(ii, goal))
                    continue; // a repeated goal room, or goals turned off
                if (LogicCatalog.IsPlayer(src))
                    continue; // a room's player object only marks its start point; the generator places the player
                var o = src.Clone();
                if (inst.Objects.TableFor(o.Class) is { } srcTable && obj.TableFor(o.Class) is { } table)
                {
                    var name = srcTable[o.Data];
                    var idx = table.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) { idx = table.Count; table.Add(name); }
                    o.Data = idx;
                }
                obj.Objects.Add(o);
            }
            inf.Items.AddRange(inst.Inf.Items.Select(i => i.Clone()));
        }

        // ---- joins
        var connectors = layout.Instances.Select(i => RoomValidator.FindConnectors(i.Source.Lev).ToDictionary(c => c.Id)).ToList();
        var lockOf = plan.Locks.ToDictionary(l => l.JoinIndex, l => l.Color);
        for (var ji = 0; ji < layout.Joins.Count; ji++)
        {
            var j = layout.Joins[ji];
            var ca = connectors[j.InstanceA][j.ConnectorA];
            var cb = connectors[j.InstanceB][j.ConnectorB];
            var sa = offsets[j.InstanceA] + ca.StubSector;
            var sb = offsets[j.InstanceB] + cb.StubSector;
            var stubA = lev.Sectors[sa];
            var stubB = lev.Sectors[sb];
            var pa = stubA.Walls[ca.PortalWall];
            var pb = stubB.Walls[cb.PortalWall];

            var (a1, a2) = Geo.Segment(stubA, pa);
            var (b1, b2) = Geo.Segment(stubB, pb);
            if (!Same(a1, b2) || !Same(a2, b1))
                throw new InvalidOperationException(
                    $"join {j}: portals do not coincide ({a1.X},{a1.Z})-({a2.X},{a2.Z}) vs ({b1.X},{b1.Z})-({b2.X},{b2.Z})");
            if (Math.Abs(stubA.FloorAltitude - stubB.FloorAltitude) > Geo.Epsilon)
                throw new InvalidOperationException($"join {j}: connector floors differ ({stubA.FloorAltitude} vs {stubB.FloorAltitude})");

            pa.Adjoin = pa.Walk = sb; pa.Mirror = cb.PortalWall;
            pb.Adjoin = pb.Walk = sa; pb.Mirror = ca.PortalWall;

            // One door per join: the first room's original door if it had one, else the second's.
            var metaA = layout.Instances[j.InstanceA].Source.Metadata.Connectors.Single(c => c.Id == j.ConnectorA);
            var metaB = layout.Instances[j.InstanceB].Source.Metadata.Connectors.Single(c => c.Id == j.ConnectorB);
            var (door, doorSector) = metaA.Door != null ? (metaA.Door, sa) : (metaB.Door, sb);
            if (door == null)
            {
                log.Add($"join {Describe(layout, j)}: open passage (neither side had a door)");
                continue;
            }
            BuildDoor(lev, inf, doorSector, door, lockOf.GetValueOrDefault(ji), Texture, log, Describe(layout, j));
        }

        // ---- keys
        foreach (var k in plan.Keys)
        {
            var frame = KeyFrames[k.Color];
            var idx = obj.Frames.FindIndex(f => string.Equals(f, frame, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) { idx = obj.Frames.Count; obj.Frames.Add(frame); }
            obj.Objects.Add(new DfObject
            {
                Class = "FRAME", Data = idx, X = k.X, Y = k.Y, Z = k.Z, Difficulty = 1,
                Seq = [new SeqEntry("LOGIC", ["ITEM", k.Color.ToUpperInvariant()])],
            });
        }

        // ---- exit and the complete elevator
        if (plan.Exit is { } exit)
        {
            var exitSector = exit.Connector != null
                ? offsets[exit.Instance] + connectors[exit.Instance][exit.Connector].StubSector
                : offsets[exit.Instance] + exit.Sector!.Value;
            var s = lev.Sectors[exitSector];
            if (s.Name.Length == 0) s.Name = layout.Instances[exit.Instance].Prefix + "exit";
            s.Ambient = 31; // lit up so it reads as the way out
            var trigger = new InfItem { Type = "sector", Name = s.Name };
            trigger.Statements.AddRange([
                new InfStatement("class", ["trigger", "single"]),
                new InfStatement("event_mask", ["4"]), // player enters the sector
                new InfStatement("message", ["complete", ExitGoalTrigger.ToString()]),
                new InfStatement("client", [CompleteElevator]),
            ]);
            inf.Items.Add(trigger);
            log.Add($"exit: {s.Name} fires GOL trigger {ExitGoalTrigger}");
        }

        // Every goal item picked up and the exit trigger each move the complete elevator one stop; the last one
        // to happen, in any order, reaches its complete stop and ends the mission.
        var goalEvents = plan.Goals.Count + (plan.Exit != null ? 1 : 0);
        if (goalEvents > 0)
        {
            AddCompleteElevator(lev, inf, Texture(lev.Textures.Count > 0 ? lev.Textures[0] : "DEFAULT.BM"), goalEvents);
            log.Add($"complete: the mission ends after {string.Join(" and ", plan.Goals.Select(g => $"taking {g.Item}").Append(plan.Exit != null ? "reaching the exit" : null).OfType<string>())}, in any order");
        }

        // ---- player start
        var startConnector = connectors[layout.StartInstance][layout.StartConnector];
        var startInst = layout.Instances[layout.StartInstance];
        // The start room's own start point (its player object, else room.json startPoints), else just inside a doorway.
        var roomStart = RoomStarts.Of(startInst);
        var spot = roomStart is { Point: var p }
            ? new Spot(p.X, p.Y, p.Z, p.Yaw)
            : ConnectorLocator.SpawnInside(startInst.Lev, startConnector, layout.FacingOf(layout.StartInstance, layout.StartConnector));
        obj.Objects.InsertRange(0, [
            new DfObject
            {
                Class = "SPIRIT", X = spot.X, Y = spot.Y, Z = spot.Z, Yaw = spot.Yaw, Difficulty = 1,
                Seq = [new SeqEntry("LOGIC", ["PLAYER"]), new SeqEntry("EYE", ["TRUE"])],
            },
            new DfObject { Class = "SAFE", X = spot.X, Y = spot.Y, Z = spot.Z, Yaw = spot.Yaw, Difficulty = 1 },
        ]);
        log.Add($"start: {startInst.Source.Metadata.Id} (instance {layout.StartInstance}) " + (roomStart?.Source switch
        {
            StartSource.PlayerObject => "at the room's player object",
            StartSource.Metadata => "at the room's start point (room.json)",
            _ => $"inside connector {layout.StartConnector}",
        }));
        return new MergedLevel(lev, obj, inf, log);
    }

    /// <summary>
    /// The engine ends the mission when the elevator named "complete" reaches a <c>complete</c> stop. It lives in a
    /// small isolated sector away from every room, as in the stock levels.
    /// </summary>
    static void AddCompleteElevator(LevFile lev, InfFile inf, int texture, int events)
    {
        var b = Geo.BoundsOf(lev.Sectors);
        double x0 = Math.Ceiling((b.MaxX + 64) / RoomRules.Grid) * RoomRules.Grid, z0 = Math.Floor(b.MinZ / RoomRules.Grid) * RoomRules.Grid;
        var t = new SurfaceTexture(texture, 0, 0, 0);
        var s = new Sector
        {
            Name = CompleteElevator, FloorAltitude = 0, CeilingAltitude = -8, FloorTexture = t, CeilingTexture = t,
        };
        s.Vertices.AddRange([new(x0, z0), new(x0, z0 + 4), new(x0 + 4, z0 + 4), new(x0 + 4, z0)]); // clockwise
        for (var i = 0; i < 4; i++)
            s.Walls.Add(new Wall { Left = i, Right = (i + 1) % 4, Mid = t, Top = t, Bot = t, Sign = new SurfaceTexture(-1, 0, 0, null) });
        lev.Sectors.Add(s);

        var item = new InfItem { Type = "sector", Name = CompleteElevator };
        item.Statements.AddRange([
            new InfStatement("class", ["elevator", "move_floor"]),
            new InfStatement("speed", ["0"]),
            new InfStatement("event_mask", ["0"]),
        ]);
        for (var i = 0; i < events; i++)
            item.Statements.Add(new InfStatement("stop", [$"@{i}", "hold"]));
        item.Statements.Add(new InfStatement("stop", [$"@{events}", "complete"]));
        inf.Items.Add(item);
    }

    static void BuildDoor(LevFile lev, InfFile inf, int doorSector, DoorInfo door, string? lockColor,
                          Func<string, int> texture, List<string> log, string join)
    {
        var stub = lev.Sectors[doorSector];
        var panel = new SurfaceTexture(texture(door.Panel), 0, 0, 0);
        var frame = new SurfaceTexture(texture(door.Frame.Length > 0 ? door.Frame : door.Panel), 0, 0, 0);

        // A closed door shows its neighbours' TOP textures over the opening; the stub's side walls are the frame.
        foreach (var w in stub.Walls)
        {
            if (w.Adjoin >= 0)
            {
                w.Top = panel;
                var back = lev.Sectors[w.Adjoin].Walls[w.Mirror];
                back.Top = panel;
            }
            else
            {
                w.Mid = frame;
            }
        }

        // A lock needs INF (a flag door cannot take a key), so a locked flag door becomes an INF "door".
        var infKind = RebuildableInfDoors.Contains(door.Kind) ? door.Kind : lockColor != null ? "door" : null;
        if (infKind != null && stub.Name.Length > 0)
        {
            var item = new InfItem { Type = "sector", Name = stub.Name };
            item.Statements.Add(new InfStatement("class", ["elevator", infKind]));
            if (lockColor != null)
                item.Statements.Add(new InfStatement("key", [lockColor.ToLowerInvariant()]));
            if (infKind == door.Kind)
                foreach (var line in door.Inf)
                {
                    var t = DfText.Tokens(line);
                    if (t.Length > 0 && t[0].EndsWith(':'))
                        item.Statements.Add(new InfStatement(t[0][..^1].ToLowerInvariant(), [.. t[1..]]));
                }
            inf.Items.Add(item);
            log.Add($"join {join}: {(lockColor != null ? lockColor + "-locked " : "")}INF door ({infKind}) on {stub.Name}, panel {door.Panel}");
        }
        else
        {
            stub.Flags1 |= 2; // sector flag DOOR: instant door
            log.Add($"join {join}: flag door on {stub.Name}, panel {door.Panel}" +
                    (door.Kind != "flag" ? $" (original was INF {door.Kind}; rebuilt as a flag door)" : ""));
        }
    }

    static bool Same(Vertex a, Vertex b) => Math.Abs(a.X - b.X) < Geo.Epsilon && Math.Abs(a.Z - b.Z) < Geo.Epsilon;

    static string Describe(LevelLayout l, Join j) =>
        $"{l.Instances[j.InstanceA].Prefix}{j.ConnectorA} <-> {l.Instances[j.InstanceB].Prefix}{j.ConnectorB}";
}
