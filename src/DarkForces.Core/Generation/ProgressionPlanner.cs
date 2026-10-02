using DarkForces.Core.Geometry;
using DarkForces.Core.Knobs;
using DarkForces.Core.Lev;
using DarkForces.Core.Rooms;

namespace DarkForces.Core.Generation;

/// <summary>A join whose door is locked with a key color.</summary>
public sealed record LockedJoin(int JoinIndex, string Color);

/// <summary>A key pickup in world coordinates, placed in instance <paramref name="Instance"/>.</summary>
public sealed record KeyPlacement(string Color, int Instance, double X, double Y, double Z);

/// <summary>The exit: an unused connector's stub, or (fallback) a plain room sector.</summary>
public sealed record ExitPlacement(int Instance, string? Connector, int? Sector);

/// <summary>A goal item kept in instance <paramref name="Instance"/> (its room declares it in <c>goals</c>).</summary>
public sealed record GoalPlacement(string Item, int Instance, GoalInfo Goal);

public sealed record ProgressionPlan(List<LockedJoin> Locks, List<KeyPlacement> Keys, ExitPlacement? Exit, List<string> Log,
                                     List<GoalPlacement> Goals)
{
    public static readonly ProgressionPlan Empty = new([], [], null, [], []);

    /// <summary>Whether instance <paramref name="instance"/> keeps its goal item <paramref name="item"/> (only one copy of each is used).</summary>
    public bool Keeps(int instance, string item) =>
        Goals.Any(g => g.Instance == instance && string.Equals(g.Item, item, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Decides locks, key positions and the exit for a layout, then proves the result is finishable.
/// Reachability is modelled at connector level: a room's <c>traversal</c> edges inside it (their
/// requirements must be satisfied by collected items), joins between rooms (a locked join needs its key).
/// Keys are only ever placed where the player can already get without them, so every plan is beatable.
/// </summary>
public sealed class ProgressionPlanner(LevelLayout layout, ProgressionSettings settings, SeededRandom rng)
{
    readonly record struct Node(int Instance, string Connector);

    public ProgressionPlan Plan()
    {
        var log = new List<string>();
        var locks = ChooseLocks(log);
        var keys = PlaceKeys(locks, log);
        var exit = settings.Exit ? PlaceExit(log) : null;
        var goals = ChooseGoals(log);

        var plan = new ProgressionPlan(locks, keys, exit, log, goals);
        if (!IsBeatable(plan, out var why))
            throw new InvalidOperationException($"progression plan is not finishable: {why}");
        return plan;
    }

    // ---------------------------------------------------------------- goals

    /// <summary>
    /// Goal items stay where their rooms put them. Each goal item works once per level (picking one up fires its
    /// GOL goal and advances the <c>complete</c> elevator), so a repeated room keeps it only in its first copy.
    /// </summary>
    List<GoalPlacement> ChooseGoals(List<string> log)
    {
        var goals = new List<GoalPlacement>();
        if (!settings.Goals) return goals;
        for (var i = 0; i < layout.Instances.Count; i++)
            foreach (var g in layout.Instances[i].Source.Metadata.Goals)
            {
                if (goals.Any(x => string.Equals(x.Item, g.Item, StringComparison.OrdinalIgnoreCase)))
                {
                    log.Add($"progression: {g.Item.ToUpperInvariant()} in {layout.Instances[i].Prefix}* removed (already in the level)");
                    continue;
                }
                goals.Add(new GoalPlacement(g.Item.ToUpperInvariant(), i, g));
                log.Add($"progression: goal {g.Item.ToUpperInvariant()} in {layout.Instances[i].Prefix}* (kept from {layout.Instances[i].Source.Metadata.Id})");
            }
        return goals;
    }

    // ---------------------------------------------------------------- locks

    List<LockedJoin> ChooseLocks(List<string> log)
    {
        if (!settings.LockedDoors || settings.LockedDoorsMax == 0)
            return [];
        var lockable = Enumerable.Range(0, layout.Joins.Count).Where(HasDoor).ToList();
        var want = settings.LockedDoorsMin + rng.Next(settings.LockedDoorsMax - settings.LockedDoorsMin + 1);
        var count = Math.Min(want, lockable.Count);
        if (count < want)
            log.Add($"progression: wanted {want} locked door(s), only {lockable.Count} join(s) have a door");

        var colors = rng.Shuffled(settings.ParsedKeyColors());
        return [.. rng.Shuffled(lockable).Take(count)
            .Select((j, i) => new LockedJoin(j, i < colors.Count ? colors[i] : colors[rng.Next(colors.Count)]))
            .OrderBy(l => l.JoinIndex)];
    }

    bool HasDoor(int joinIndex)
    {
        var j = layout.Joins[joinIndex];
        return Meta(j.InstanceA, j.ConnectorA).Door != null || Meta(j.InstanceB, j.ConnectorB).Door != null;
    }

    // ---------------------------------------------------------------- keys

    List<KeyPlacement> PlaceKeys(List<LockedJoin> locks, List<string> log)
    {
        var keys = new List<KeyPlacement>();
        var have = new HashSet<string>();
        var usedSpots = new HashSet<(int, int)>();
        var pending = locks.Select(l => l.Color).Distinct().ToList();

        while (pending.Count > 0)
        {
            var reach = Reachable(locks, have);
            // Prefer a color whose door is on the frontier (one side reachable), so keys unlock in a sensible order.
            var frontier = pending.Where(c => locks.Any(l => l.Color == c && OnFrontier(l, reach))).ToList();
            var color = rng.Pick(frontier.Count > 0 ? frontier : pending);

            var spots = ItemSpots(reach, have)
                .Where(s => !usedSpots.Contains((s.Instance, s.Index))).ToList();
            if (spots.Count == 0)
                throw new InvalidOperationException($"no free item spot reachable for the {color} key");
            var spot = settings.KeyPlacement.Equals("far", StringComparison.OrdinalIgnoreCase)
                ? Farthest(spots, s => s.Instance)
                : rng.Pick(spots);

            usedSpots.Add((spot.Instance, spot.Index));
            keys.Add(new KeyPlacement(color, spot.Instance, spot.X, spot.Y, spot.Z));
            have.Add(color);
            pending.Remove(color);
            log.Add($"progression: {color} key in {layout.Instances[spot.Instance].Prefix}* ({spot.Source})");
        }
        foreach (var l in locks)
        {
            var j = layout.Joins[l.JoinIndex];
            log.Add($"progression: {l.Color} lock on {layout.Instances[j.InstanceA].Prefix}{j.ConnectorA} <-> " +
                    $"{layout.Instances[j.InstanceB].Prefix}{j.ConnectorB}");
        }
        return keys;
    }

    bool OnFrontier(LockedJoin l, HashSet<Node> reach)
    {
        var j = layout.Joins[l.JoinIndex];
        return reach.Contains(new Node(j.InstanceA, j.ConnectorA)) != reach.Contains(new Node(j.InstanceB, j.ConnectorB));
    }

    sealed record Spot(int Instance, int Index, double X, double Y, double Z, string Source);

    /// <summary>
    /// Item spots the player can reach: the room's itemSlots (by reachableFrom/requires) plus one automatic spot
    /// per room (inside its largest non-connector sector), which is reachable from any reachable connector.
    /// </summary>
    List<Spot> ItemSpots(HashSet<Node> reach, HashSet<string> have)
    {
        var spots = new List<Spot>();
        for (var i = 0; i < layout.Instances.Count; i++)
        {
            var inst = layout.Instances[i];
            if (HallwayBuilder.IsHallway(inst.Source)) continue; // items belong in rooms, not corridors
            var meta = inst.Source.Metadata;
            var reachedHere = meta.Connectors.Where(c => reach.Contains(new Node(i, c.Id))).Select(c => c.Id).ToHashSet();
            if (reachedHere.Count == 0) continue;

            for (var s = 0; s < meta.ItemSlots.Count; s++)
            {
                var slot = meta.ItemSlots[s];
                if (!slot.ReachableFrom.Any(reachedHere.Contains) || !slot.Requires.All(have.Contains)) continue;
                var v = inst.Placement.Apply(new Vertex(slot.X, slot.Z));
                spots.Add(new Spot(i, s, v.X, slot.Y + inst.Placement.Dy, v.Z, $"item slot {slot.Id}"));
            }
            if (AutoSpot(inst) is { } auto)
                spots.Add(new Spot(i, -1, auto.X, auto.Y, auto.Z, "room center"));
        }
        return spots;
    }

    /// <summary>
    /// Sector flags that make a floor a bad place for an item: 128 pit (no floor), 1 sky, 2048/4096 damage.
    /// </summary>
    const long UnsafeFloorFlags = 1 | 128 | 2048 | 4096;

    /// <summary>
    /// A point inside the room's largest plain sector, on its floor: not a connector part, not driven by INF
    /// (an item on a lift would ride it), not a pit, sky or damaging floor, and tall enough to walk into.
    /// </summary>
    static (double X, double Y, double Z)? AutoSpot(RoomInstance inst)
    {
        var lev = inst.Lev;
        var infNames = inst.Inf.Items.Where(i => i.Name != null).Select(i => i.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in lev.Sectors
                     .Where(s => !IsConnectorPart(s) && s.FloorAltitude - s.CeilingAltitude >= 6.8)
                     .Where(s => (s.Flags1 & UnsafeFloorFlags) == 0 && s.SecondAltitude == 0 && !infNames.Contains(s.Name))
                     .OrderByDescending(s => Math.Abs(Geo.SignedArea(s))))
        {
            var cx = s.Vertices.Average(v => v.X);
            var cz = s.Vertices.Average(v => v.Z);
            if (Geo.Contains(s, cx, cz)) return (cx, s.FloorAltitude, cz);
            var b = Geo.BoundsOf([s]);
            var (bx, bz) = ((b.MinX + b.MaxX) / 2, (b.MinZ + b.MaxZ) / 2);
            if (Geo.Contains(s, bx, bz)) return (bx, s.FloorAltitude, bz);
        }
        return null;
    }

    static bool IsConnectorPart(Sector s) =>
        s.Name.Contains(RoomRules.StubPrefix, StringComparison.Ordinal) || s.Name.Contains(RoomRules.AdapterPrefix, StringComparison.Ordinal);

    // ---------------------------------------------------------------- exit

    ExitPlacement? PlaceExit(List<string> log)
    {
        var joined = layout.Joins.SelectMany(j => new[] { new Node(j.InstanceA, j.ConnectorA), new Node(j.InstanceB, j.ConnectorB) }).ToHashSet();
        var start = new Node(layout.StartInstance, layout.StartConnector);
        var free = Enumerable.Range(0, layout.Instances.Count)
            .SelectMany(i => layout.Instances[i].Source.Metadata.Connectors.Select(c => new Node(i, c.Id)))
            .Where(n => !joined.Contains(n) && n != start && !HallwayBuilder.IsHallway(layout.Instances[n.Instance].Source))
            .ToList();
        // Not in the start room if anything else is available.
        var elsewhere = free.Where(n => n.Instance != layout.StartInstance).ToList();
        if (elsewhere.Count > 0) free = elsewhere;

        var far = settings.ExitPlacement.Equals("far", StringComparison.OrdinalIgnoreCase);
        if (free.Count > 0)
        {
            var n = far ? Farthest(free, x => x.Instance) : rng.Pick(free);
            log.Add($"progression: exit at {layout.Instances[n.Instance].Prefix}{n.Connector} (unused doorway)");
            return new ExitPlacement(n.Instance, n.Connector, null);
        }

        // Every doorway is used: fall back to a room sector without INF logic.
        var rooms = Enumerable.Range(0, layout.Instances.Count)
            .Where(i => !HallwayBuilder.IsHallway(layout.Instances[i].Source))
            .Where(i => i != layout.StartInstance || layout.Instances.Count == 1).ToList();
        var ordered = far ? rooms.OrderByDescending(Distance).ToList() : rng.Shuffled(rooms);
        foreach (var i in ordered)
        {
            var inst = layout.Instances[i];
            var infNames = inst.Inf.Items.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sector = inst.Lev.Sectors
                .Select((s, si) => (s, si))
                .Where(p => !IsConnectorPart(p.s) && !infNames.Contains(p.s.Name))
                .OrderByDescending(p => Math.Abs(Geo.SignedArea(p.s)))
                .Select(p => (int?)p.si).FirstOrDefault();
            if (sector is { } si)
            {
                log.Add($"progression: exit is sector {si} of {inst.Prefix}* (no unused doorway left)");
                return new ExitPlacement(i, null, si);
            }
        }
        log.Add("progression: no place for an exit; the level cannot be completed");
        return null;
    }

    // ---------------------------------------------------------------- graph

    ConnectorInfo Meta(int instance, string connector) =>
        layout.Instances[instance].Source.Metadata.Connectors.Single(c => c.Id == connector);

    /// <summary>Connectors reachable from the start with the given keys.</summary>
    HashSet<Node> Reachable(List<LockedJoin> locks, HashSet<string> have)
    {
        var lockOf = locks.ToDictionary(l => l.JoinIndex, l => l.Color);
        var start = new Node(layout.StartInstance, layout.StartConnector);
        var seen = new HashSet<Node> { start };
        var queue = new Queue<Node>([start]);
        while (queue.TryDequeue(out var n))
        {
            var meta = layout.Instances[n.Instance].Source.Metadata;
            foreach (var e in meta.Traversal.Where(e => e.From == n.Connector && e.Requires.All(have.Contains)))
                if (meta.Connectors.Any(c => c.Id == e.To) && seen.Add(new Node(n.Instance, e.To)))
                    queue.Enqueue(new Node(n.Instance, e.To));
            for (var ji = 0; ji < layout.Joins.Count; ji++)
            {
                var j = layout.Joins[ji];
                if (lockOf.TryGetValue(ji, out var color) && !have.Contains(color)) continue;
                Node? other = n == new Node(j.InstanceA, j.ConnectorA) ? new Node(j.InstanceB, j.ConnectorB)
                            : n == new Node(j.InstanceB, j.ConnectorB) ? new Node(j.InstanceA, j.ConnectorA)
                            : null;
                if (other is { } o && seen.Add(o))
                    queue.Enqueue(o);
            }
        }
        return seen;
    }

    /// <summary>Simulates play: collect every reachable key until nothing changes; every goal and the exit must be reached.</summary>
    public bool IsBeatable(ProgressionPlan plan, out string why)
    {
        var have = new HashSet<string>();
        while (true)
        {
            var reach = Reachable(plan.Locks, have);
            var reachedRooms = reach.Select(n => n.Instance).ToHashSet();
            var picked = plan.Keys.Where(k => !have.Contains(k.Color) && reachedRooms.Contains(k.Instance)).ToList();
            if (picked.Count == 0)
            {
                var problems = new List<string>();
                var stuck = plan.Locks.Where(l => !have.Contains(l.Color)).Select(l => l.Color).Distinct().ToList();
                if (stuck.Count > 0)
                    problems.Add($"keys never reachable: {string.Join(", ", stuck)}");
                if (plan.Exit is { } exit && !reachedRooms.Contains(exit.Instance))
                    problems.Add($"exit in instance {exit.Instance} is unreachable with keys [{string.Join(", ", have)}]");
                foreach (var g in plan.Goals.Where(g => !GoalReached(g, reach)))
                    problems.Add($"goal {g.Item} in instance {g.Instance} is unreachable with keys [{string.Join(", ", have)}]");
                why = string.Join("; ", problems);
                return problems.Count == 0;
            }
            foreach (var k in picked) have.Add(k.Color);
        }
    }

    bool GoalReached(GoalPlacement g, HashSet<Node> reach) =>
        g.Goal.ReachableFrom.Count == 0
            ? reach.Any(n => n.Instance == g.Instance)
            : g.Goal.ReachableFrom.Any(c => reach.Contains(new Node(g.Instance, c)));

    /// <summary>Room-graph hops from the start room, ignoring locks.</summary>
    int Distance(int instance)
    {
        var dist = new Dictionary<int, int> { [layout.StartInstance] = 0 };
        var queue = new Queue<int>([layout.StartInstance]);
        while (queue.TryDequeue(out var i))
            foreach (var j in layout.Joins)
            {
                var other = j.InstanceA == i ? j.InstanceB : j.InstanceB == i ? j.InstanceA : -1;
                if (other >= 0 && dist.TryAdd(other, dist[i] + 1))
                    queue.Enqueue(other);
            }
        return dist.GetValueOrDefault(instance, 0);
    }

    T Farthest<T>(List<T> items, Func<T, int> instanceOf)
    {
        var best = items.Max(x => Distance(instanceOf(x)));
        return rng.Pick(items.Where(x => Distance(instanceOf(x)) == best).ToList());
    }
}
