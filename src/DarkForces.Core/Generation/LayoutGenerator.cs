using DarkForces.Core.Geometry;
using DarkForces.Core.Knobs;
using DarkForces.Core.Lev;
using DarkForces.Core.Rooms;

namespace DarkForces.Core.Generation;

/// <summary>
/// Grows a level by attaching rooms to open connectors. Each step picks an open connector, then either joins a
/// room to it directly (a room, rotation and connector facing it, translated so the portals coincide) or grows a
/// generated hallway from it and joins a room to the hallway's far end. A placement is kept only if its footprint
/// overlaps nothing placed so far. Driven entirely by <see cref="LayoutSettings"/> and the seed.
/// </summary>
public sealed class LayoutGenerator(IReadOnlyList<RoomPackage> pool, LayoutSettings settings, SeededRandom rng)
{
    sealed record Open(int Instance, string Connector);

    sealed class State
    {
        public LevelLayout Layout { get; } = new();
        public List<Bounds> Footprints { get; } = [];
        public List<Open> Open { get; } = [];
        public int Hallways { get; set; }

        public int Rooms => Layout.Instances.Count(i => !HallwayBuilder.IsHallway(i.Source));

        public (int Instances, int Joins, List<Open> Open, int Hallways) Save() =>
            (Layout.Instances.Count, Layout.Joins.Count, [.. Open], Hallways);

        public void Restore((int Instances, int Joins, List<Open> Open, int Hallways) s)
        {
            Layout.Instances.RemoveRange(s.Instances, Layout.Instances.Count - s.Instances);
            Footprints.RemoveRange(s.Instances, Footprints.Count - s.Instances);
            Layout.Joins.RemoveRange(s.Joins, Layout.Joins.Count - s.Joins);
            Open.Clear();
            Open.AddRange(s.Open);
            Hallways = s.Hallways;
        }
    }

    public LevelLayout Generate()
    {
        var usable = pool.Where(r => r.Metadata.Connectors.Count > 0).ToList();
        if (usable.Count == 0)
            throw new InvalidOperationException("no rooms with connectors in the pool");
        if (!settings.AllowRepeats && settings.RoomCount > usable.Count)
            throw new InvalidOperationException(
                $"layout.roomCount = {settings.RoomCount} but only {usable.Count} distinct rooms and layout.allowRepeats = false");

        for (var attempt = 1; attempt <= settings.Attempts; attempt++)
        {
            if (TryOnce(usable) is { } layout)
                return layout;
        }
        throw new InvalidOperationException(
            $"could not place {settings.RoomCount} rooms in {settings.Attempts} attempts (try a lower layout.roomCount, " +
            "layout.allowRepeats = true, layout.allowRotation = true, layout.hallways = true, or more rooms)");
    }

    LevelLayout? TryOnce(List<RoomPackage> usable)
    {
        var st = new State();
        var firstRoom = rng.Pick(usable);
        Place(st, firstRoom, new Placement(Turns(firstRoom), 0, 0, 0), null, "R1_");

        while (st.Rooms < settings.RoomCount && st.Open.Count > 0)
        {
            var target = st.Open[rng.Next(st.Open.Count)];
            st.Open.Remove(target);
            // If neither works, the connector simply stays sealed.
            if (settings.Hallways && rng.NextDouble() < settings.HallwayChance)
                _ = TryHallway(st, usable, target) || TryDirect(st, usable, target);
            else
                _ = TryDirect(st, usable, target) || (settings.Hallways && TryHallway(st, usable, target));
        }

        if (st.Rooms < settings.RoomCount)
            return null;
        st.Layout.StartInstance = 0;
        st.Layout.StartConnector = st.Layout.Instances[0].Source.Metadata.Connectors[0].Id;
        return st.Layout;
    }

    /// <summary>Joins a room from the pool straight onto <paramref name="target"/>.</summary>
    bool TryDirect(State st, List<RoomPackage> usable, Open target)
    {
        var want = Geo.Opposite(st.Layout.FacingOf(target.Instance, target.Connector));
        var used = st.Layout.Instances.Select(i => i.Source).ToHashSet();
        var candidates =
            (from room in usable
             where settings.AllowRepeats || !used.Contains(room)
             from c in room.Metadata.Connectors
             from turns in AllowedTurns(room)
             where new Placement(turns, 0, 0, 0).Apply(c.Facing) == want
             select (room, c.Id, turns)).ToList();

        foreach (var (room, connector, turns) in rng.Shuffled(candidates))
            if (TryAttach(st, room, connector, turns, target, $"R{st.Rooms + 1}_"))
                return true;
        return false;
    }

    /// <summary>
    /// Grows a random hallway from <paramref name="target"/> and joins a room to its far end. If no room fits there,
    /// the hallway is removed again so no dead-end corridors are left behind.
    /// </summary>
    bool TryHallway(State st, List<RoomPackage> usable, Open target)
    {
        var saved = st.Save();
        var shape = settings.HallwayTurns && rng.Next(2) == 1 ? HallwayShape.Turn : HallwayShape.Straight;
        var rise = settings.HallwayMaxRise > 0 ? rng.Next(2 * settings.HallwayMaxRise + 1) - settings.HallwayMaxRise : 0;
        var grid = RoomRules.Grid;
        var minLen = Math.Max(HallwayBuilder.MinLength(rise), (int)Math.Ceiling(settings.HallwayMinLength / (double)grid) * grid);
        var maxLen = Math.Max(minLen, settings.HallwayMaxLength / grid * grid);
        var length = minLen + grid * rng.Next((maxLen - minLen) / grid + 1);
        var length2 = grid * (1 + rng.Next(Math.Max(1, maxLen / grid)));

        st.Hallways++;
        var hall = HallwayBuilder.Build($"hallway-{st.Hallways}", shape, length, length2, rise, TexturesAt(st.Layout, target));
        var (end, far) = rng.Next(2) == 0 ? ("A", "B") : ("B", "A");
        var want = Geo.Opposite(st.Layout.FacingOf(target.Instance, target.Connector));
        var facing = hall.Metadata.Connectors.Single(c => c.Id == end).Facing;
        var turns = Enumerable.Range(0, 4).First(t => new Placement(t, 0, 0, 0).Apply(facing) == want);

        if (TryAttach(st, hall, end, turns, target, $"H{st.Hallways}_"))
        {
            var farEnd = new Open(st.Layout.Instances.Count - 1, far);
            st.Open.Remove(farEnd);
            if (TryDirect(st, usable, farEnd))
                return true;
        }
        st.Restore(saved);
        return false;
    }

    /// <summary>Rotates <paramref name="room"/>, translates its connector's portal onto the target's, and places it.</summary>
    bool TryAttach(State st, RoomPackage room, string connector, int turns, Open target, string prefix)
    {
        var (p1, p2, floor) = WorldPortal(st.Layout, target);
        var local = LocalPortal(room, connector);
        var rot = new Placement(turns, 0, 0, 0);
        var q1 = rot.Apply(local.A);
        var q2 = rot.Apply(local.B);
        double dx = p2.X - q1.X, dz = p2.Z - q1.Z;
        if (Math.Abs(q2.X + dx - p1.X) > Geo.Epsilon || Math.Abs(q2.Z + dz - p1.Z) > Geo.Epsilon)
            return false; // portal widths/orientation disagree
        var c = room.Metadata.Connectors.Single(x => x.Id == connector);
        return Place(st, room, new Placement(turns, dx, floor - c.Floor, dz), (target, connector), prefix);
    }

    bool Place(State st, RoomPackage room, Placement placement, (Open Target, string Connector)? join, string prefix)
    {
        var index = st.Layout.Instances.Count;
        var inst = RoomTransformer.Apply(room, placement, prefix);
        var fp = Geo.BoundsOf(inst.Lev.Sectors);
        if (st.Footprints.Any(f => Overlaps(f, fp)))
            return false;

        st.Layout.Instances.Add(inst);
        st.Footprints.Add(fp);
        foreach (var c in room.Metadata.Connectors)
            if (join == null || c.Id != join.Value.Connector)
                st.Open.Add(new Open(index, c.Id));
        if (join is { } j)
            st.Layout.Joins.Add(new Join(j.Target.Instance, j.Target.Connector, index, j.Connector));
        return true;
    }

    /// <summary>Hallways borrow the wall, floor and ceiling textures of the stub they grow out of.</summary>
    static HallwayTextures TexturesAt(LevelLayout layout, Open o)
    {
        var inst = layout.Instances[o.Instance];
        var c = RoomValidator.FindConnectors(inst.Source.Lev).Single(x => x.Id == o.Connector);
        var stub = inst.Lev.Sectors[c.StubSector];
        string Name(SurfaceTexture t) => t.Index >= 0 && t.Index < inst.Lev.Textures.Count ? inst.Lev.Textures[t.Index] : "DEFAULT.BM";
        var side = stub.Walls.Where((w, i) => w.Adjoin < 0 && i != c.PortalWall).Select(w => w.Mid).FirstOrDefault();
        return new HallwayTextures(Name(side), Name(stub.FloorTexture), Name(stub.CeilingTexture), inst.Lev.Palette, stub.Ambient);
    }

    /// <summary>Footprints overlap with positive area; rooms that only touch (at a shared portal) do not.</summary>
    static bool Overlaps(Bounds a, Bounds b) =>
        a.MinX < b.MaxX - Geo.Epsilon && b.MinX < a.MaxX - Geo.Epsilon &&
        a.MinZ < b.MaxZ - Geo.Epsilon && b.MinZ < a.MaxZ - Geo.Epsilon;

    int Turns(RoomPackage room)
    {
        var allowed = AllowedTurns(room);
        return allowed[rng.Next(allowed.Count)];
    }

    /// <summary>Rooms rotate only if the knob and the room allow it; hallways always may (they exist to connect).</summary>
    List<int> AllowedTurns(RoomPackage room) =>
        HallwayBuilder.IsHallway(room) || (settings.AllowRotation && room.Metadata.Rotatable) ? [0, 1, 2, 3] : [0];

    static (Vertex A, Vertex B) LocalPortal(RoomPackage room, string connector)
    {
        var c = RoomValidator.FindConnectors(room.Lev).Single(x => x.Id == connector);
        var s = room.Lev.Sectors[c.StubSector];
        return Geo.Segment(s, s.Walls[c.PortalWall]);
    }

    static (Vertex A, Vertex B, double Floor) WorldPortal(LevelLayout layout, Open o)
    {
        var inst = layout.Instances[o.Instance];
        var c = RoomValidator.FindConnectors(inst.Source.Lev).Single(x => x.Id == o.Connector);
        var s = inst.Lev.Sectors[c.StubSector];
        var (a, b) = Geo.Segment(s, s.Walls[c.PortalWall]);
        return (a, b, s.FloorAltitude);
    }
}
