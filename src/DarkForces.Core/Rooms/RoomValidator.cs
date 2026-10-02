using DarkForces.Core.Geometry;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;

namespace DarkForces.Core.Rooms;

public enum Severity { Error, Warning }

public sealed record RoomFinding(string Rule, Severity Severity, string Message)
{
    public override string ToString() => $"{(Severity == Severity.Error ? "error" : "warn ")} {Rule,-4} {Message}";
}

/// <summary>A connector found in the geometry: its stub sector and the stub's portal (outer) wall.</summary>
public sealed record ConnectorGeometry(string Id, int StubSector, int PortalWall, Facing? Facing, double Floor);

/// <summary>
/// Checks a room package against docs/room-spec.md. Rules in the PoC scope (§11) are errors;
/// the rest are warnings until the spec leaves draft.
/// </summary>
public static class RoomValidator
{
    public static List<RoomFinding> Validate(RoomPackage room)
    {
        var v = new Run(room);
        v.Geometry();
        var connectors = v.Connectors();
        v.Adapters();
        v.Objects();
        v.Inf();
        v.Metadata(connectors);
        return v.Findings;
    }

    /// <summary>Stub sectors (named CX_id) and their portal walls, as the geometry defines them.</summary>
    public static List<ConnectorGeometry> FindConnectors(LevFile lev)
    {
        var result = new List<ConnectorGeometry>();
        for (var si = 0; si < lev.Sectors.Count; si++)
        {
            var s = lev.Sectors[si];
            var m = RoomRules.StubName().Match(s.Name);
            if (!m.Success) continue;
            var portal = PortalWall(s);
            result.Add(new ConnectorGeometry(m.Groups[1].Value, si, portal,
                portal >= 0 ? Geo.OutwardFacing(s, s.Walls[portal]) : null, s.FloorAltitude));
        }
        return result;
    }

    /// <summary>The unadjoined wall sharing no vertex with the stub's single adjoined wall; -1 if ambiguous.</summary>
    static int PortalWall(Sector s)
    {
        var inner = s.Walls.FindIndex(w => w.Adjoin >= 0);
        if (inner < 0 || s.Walls.Count(w => w.Adjoin >= 0) != 1) return -1;
        var iw = s.Walls[inner];
        var candidates = Enumerable.Range(0, s.Walls.Count)
            .Where(i => s.Walls[i].Adjoin < 0 && !Shares(s, s.Walls[i], iw)).ToList();
        return candidates.Count == 1 ? candidates[0] : -1;

        static bool Shares(Sector s, Wall a, Wall b)
        {
            Vertex[] va = [s.Vertices[a.Left], s.Vertices[a.Right]];
            return va.Contains(s.Vertices[b.Left]) || va.Contains(s.Vertices[b.Right]);
        }
    }

    sealed class Run(RoomPackage room)
    {
        public List<RoomFinding> Findings { get; } = [];
        readonly LevFile _lev = room.Lev;
        readonly RoomMetadata _meta = room.Metadata;
        readonly HashSet<int> _stubs = [];
        readonly HashSet<int> _adapters = [];

        void Error(string rule, string msg) => Findings.Add(new RoomFinding(rule, Severity.Error, msg));
        void Warn(string rule, string msg) => Findings.Add(new RoomFinding(rule, Severity.Warning, msg));

        string S(int si) => _lev.Sectors[si].Name is { Length: > 0 } n ? $"S{si} '{n}'" : $"S{si}";

        public void Geometry()
        {
            var sectors = _lev.Sectors;
            for (var si = 0; si < sectors.Count; si++)
            {
                var s = sectors[si];
                if (!Geo.LoopsClosed(s))
                    Error("G1", $"{S(si)}: walls do not form closed loops");
                else if (Geo.SignedArea(s) >= 0)
                    Error("G1", $"{S(si)}: walls must wind clockwise (as every stock sector does)");
                for (var wi = 0; wi < s.Walls.Count; wi++)
                {
                    var w = s.Walls[wi];
                    if (w.Adjoin < 0) continue;
                    if (w.Adjoin >= sectors.Count || w.Mirror < 0 || w.Mirror >= sectors[w.Adjoin].Walls.Count)
                    {
                        Error("G3", $"{S(si)} wall {wi}: adjoins {w.Adjoin}/{w.Mirror}, which is not in the room");
                        continue;
                    }
                    var back = sectors[w.Adjoin].Walls[w.Mirror];
                    if (back.Adjoin != si || back.Mirror != wi)
                        Error("G2", $"{S(si)} wall {wi}: adjoin {w.Adjoin}/{w.Mirror} does not point back");
                }
                if (s.CeilingAltitude > s.FloorAltitude)
                    Error("G1", $"{S(si)}: ceiling {s.CeilingAltitude} is below floor {s.FloorAltitude} (Y points down)");
                else if (s.FloorAltitude - s.CeilingAltitude < RoomRules.MinHeight && !IsConnectorPart(s))
                    Warn("G7", $"{S(si)}: floor to ceiling is {s.FloorAltitude - s.CeilingAltitude} (walkable needs {RoomRules.MinHeight})");
                if ((s.Flags1 & (1 | 128)) != 0 && !_meta.Exterior)
                    Error("G8", $"{S(si)}: sky/pit sector but room.json has \"exterior\": false");
            }

            // G7 for openings: a step the player can walk up, between two sectors tall enough to stand in, but whose
            // shared opening is too low to pass standing (e.g. a step straight off an 8-high stub: 8 - 3 = 5).
            // Doors and INF-driven sectors are skipped: their heights change in play.
            var moving = room.Inf.Items.Where(i => i.Name != null).Select(i => i.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool Fixed(Sector x) => (x.Flags1 & 2) == 0 && !moving.Contains(x.Name);
            for (var si = 0; si < sectors.Count; si++)
                foreach (var w in sectors[si].Walls.Where(w => w.Adjoin > si && w.Adjoin < sectors.Count))
                {
                    Sector p = sectors[si], q = sectors[w.Adjoin];
                    if (!Fixed(p) || !Fixed(q) || Math.Abs(p.FloorAltitude - q.FloorAltitude) > RoomRules.MaxStep) continue;
                    if (p.FloorAltitude - p.CeilingAltitude < RoomRules.MinHeight || q.FloorAltitude - q.CeilingAltitude < RoomRules.MinHeight) continue;
                    var opening = Math.Min(p.FloorAltitude, q.FloorAltitude) - Math.Max(p.CeilingAltitude, q.CeilingAltitude);
                    if (opening < RoomRules.MinHeight - Geo.Epsilon)
                        Warn("G7", $"{S(si)} -> {S(w.Adjoin)}: opening is {opening} high (walking through needs {RoomRules.MinHeight}; the player must crouch)");
                }

            if (sectors.Count > RoomRules.MaxSectors)
                Error("G6", $"{sectors.Count} sectors (max {RoomRules.MaxSectors})");
            var walls = sectors.Sum(s => s.Walls.Count);
            if (walls > RoomRules.MaxWalls)
                Error("G6", $"{walls} walls (max {RoomRules.MaxWalls})");
            if (sectors.Count == 0)
            {
                Error("G6", "room has no sectors");
                return;
            }
            var b = Geo.BoundsOf(sectors);
            if (b.Width > RoomRules.MaxBounds || b.Depth > RoomRules.MaxBounds)
                Error("G6", $"footprint {b.Width} x {b.Depth} (max {RoomRules.MaxBounds})");

            var mb = _meta.Bounds;
            var declared = new Bounds(mb.MinX, mb.MinZ, mb.MaxX, mb.MaxZ);
            if (!sectors.SelectMany(s => s.Vertices).All(p => declared.Contains(p.X, p.Z)))
                Error("G4", $"vertices extend outside room.json bounds ({b.MinX},{b.MinZ})-({b.MaxX},{b.MaxZ})");
            var minY = sectors.Min(s => s.CeilingAltitude);
            var maxY = sectors.Max(s => s.FloorAltitude);
            if (minY < mb.MinY - Geo.Epsilon || maxY > mb.MaxY + Geo.Epsilon)
                Error("M6", $"room.json minY/maxY must cover ceilings/floors {minY}..{maxY}");
        }

        static bool IsConnectorPart(Sector s) =>
            RoomRules.StubName().IsMatch(s.Name) || RoomRules.AdapterName().IsMatch(s.Name);

        /// <summary>A declared goal matches an object with the same goal logic within a unit of its X/Z.</summary>
        static bool DeclaresGoal(GoalInfo g, string item, DfObject o) =>
            string.Equals(g.Item, item, StringComparison.OrdinalIgnoreCase) && Math.Abs(g.X - o.X) < 1 && Math.Abs(g.Z - o.Z) < 1;

        public List<ConnectorGeometry> Connectors()
        {
            var found = FindConnectors(_lev);
            if (found.Count == 0)
                Error("C9", "no connectors (sectors named CX_<id>)");
            if (found.Count > RoomRules.MaxConnectors)
                Error("C9", $"{found.Count} connectors (PoC max {RoomRules.MaxConnectors})");
            foreach (var dup in found.GroupBy(c => c.Id).Where(g => g.Count() > 1))
                Error("C1", $"connector id '{dup.Key}' used by {dup.Count()} stub sectors");
            foreach (var s in _lev.Sectors.Where(s => s.Name.StartsWith(RoomRules.StubPrefix, StringComparison.OrdinalIgnoreCase) && !RoomRules.StubName().IsMatch(s.Name)))
                Error("C1", $"'{s.Name}' looks like a stub but is not CX_ + 1-4 of [A-Z0-9]");

            var bounds = _lev.Sectors.Count > 0 ? Geo.BoundsOf(_lev.Sectors) : default;
            foreach (var c in found)
            {
                _stubs.Add(c.StubSector);
                var s = _lev.Sectors[c.StubSector];
                var tag = $"connector {c.Id} ({S(c.StubSector)})";

                if (s.Vertices.Count != 4 || s.Walls.Count != 4 || !Geo.LoopsClosed(s))
                    Error("C2", $"{tag}: stub must be a 4-vertex, 4-wall rectangle");
                if (!s.Vertices.All(p => Geo.IsInteger(p.X) && Geo.IsInteger(p.Z)))
                    Error("C3", $"{tag}: stub vertices must have integer coordinates");
                if (c.PortalWall < 0)
                {
                    Error("C4", $"{tag}: needs exactly one adjoined inner wall and one unadjoined portal wall opposite it");
                    continue;
                }

                var portal = s.Walls[c.PortalWall];
                var (pa, pb) = Geo.Segment(s, portal);
                if (Math.Abs(Geo.Length(s, portal) - RoomRules.StubWidth) > Geo.Epsilon)
                    Error("C2", $"{tag}: portal wall is {Geo.Length(s, portal)} wide (must be {RoomRules.StubWidth})");
                if (c.Facing == null)
                    Error("C2", $"{tag}: portal wall is not axis-aligned");
                foreach (var (w, i) in s.Walls.Select((w, i) => (w, i)))
                {
                    if (i == c.PortalWall || w.Adjoin >= 0) continue;
                    if (Math.Abs(Geo.Length(s, w) - RoomRules.StubDepth) > Geo.Epsilon)
                        Error("C2", $"{tag}: side wall {i} is {Geo.Length(s, w)} long (stub depth is {RoomRules.StubDepth})");
                }
                if (!(Geo.OnGrid(pa.X, RoomRules.Grid) && Geo.OnGrid(pa.Z, RoomRules.Grid) &&
                      Geo.OnGrid(pb.X, RoomRules.Grid) && Geo.OnGrid(pb.Z, RoomRules.Grid)))
                    Error("C3", $"{tag}: portal vertices ({pa.X},{pa.Z})-({pb.X},{pb.Z}) are not on the {RoomRules.Grid}-unit grid");

                var inner = s.Walls.Single(w => w.Adjoin >= 0);
                if (inner.Adjoin >= 0 && inner.Adjoin < _lev.Sectors.Count && _stubs.Contains(inner.Adjoin))
                    Error("C5", $"{tag}: inner wall adjoins another stub");

                if (!Geo.IsInteger(s.FloorAltitude))
                    Error("C6", $"{tag}: floor {s.FloorAltitude} is not an integer");
                if (Math.Abs(s.FloorAltitude - s.CeilingAltitude - RoomRules.StubHeight) > Geo.Epsilon)
                    Error("C6", $"{tag}: opening is {s.FloorAltitude - s.CeilingAltitude} high (must be {RoomRules.StubHeight})");
                if (s.Flags1 != 0 || s.Flags2 != 0 || s.Flags3 != 0 || s.SecondAltitude != 0)
                    Error("C7", $"{tag}: stub must have FLAGS 0 0 0 and SECOND ALTITUDE 0");
                if (portal.Mid.Index < 0 || portal.Mid.Index >= _lev.Textures.Count)
                    Error("C8", $"{tag}: portal MID (seal) texture {portal.Mid.Index} is not in the texture table");

                if (c.Facing is { } f)
                {
                    var onEdge = f switch
                    {
                        Facing.N => Math.Abs(pa.Z - bounds.MaxZ) < Geo.Epsilon,
                        Facing.S => Math.Abs(pa.Z - bounds.MinZ) < Geo.Epsilon,
                        Facing.E => Math.Abs(pa.X - bounds.MaxX) < Geo.Epsilon,
                        _ => Math.Abs(pa.X - bounds.MinX) < Geo.Epsilon,
                    };
                    if (!onEdge)
                        Error("G5", $"{tag}: portal (facing {f}) is not on the room's {f} bounds edge");
                }
            }
            return found;
        }

        public void Adapters()
        {
            for (var si = 0; si < _lev.Sectors.Count; si++)
            {
                var s = _lev.Sectors[si];
                if (!RoomRules.AdapterName().IsMatch(s.Name)) continue;
                _adapters.Add(si);
                var tag = $"adapter {S(si)}";
                if (!Geo.IsConvex(s))
                    Error("C11", $"{tag}: must be a single convex loop");
                var adj = s.Walls.Where(w => w.Adjoin >= 0).Select(w => w.Adjoin).ToList();
                if (adj.Count(a => _stubs.Contains(a)) != 1 || adj.Count(a => !_stubs.Contains(a)) < 1)
                    Error("C11", $"{tag}: must adjoin exactly one stub and at least one room sector");
            }
        }

        public void Objects()
        {
            var objs = room.Objects;
            // A player object is the room's start point (RoomStarts); one at most.
            if (objs.Objects.Count(LogicCatalog.IsPlayer) is var players and > 1)
                Error("O1", $"{players} player objects; a room may have one (it marks the room's start point)");
            for (var oi = 0; oi < objs.Objects.Count; oi++)
            {
                var o = objs.Objects[oi];
                var tag = $"object {oi} ({o.Class} {string.Join('/', o.Logics)})";
                if (LogicCatalog.GoalItemOf(o) is { } goal)
                {
                    if (!_meta.Goals.Any(g => DeclaresGoal(g, goal, o)))
                        Error("O2", $"{tag}: goal item not declared in room.json goals");
                }
                else if (LogicCatalog.IsProgression(o))
                {
                    Error("O2", $"{tag}: progression item; place it via itemSlots instead");
                }
                if (LogicCatalog.UsesVue(o))
                    Error("O5", $"{tag}: VUE motion is not allowed");

                var home = Enumerable.Range(0, _lev.Sectors.Count).Where(si => Geo.Contains(_lev.Sectors[si], o.X, o.Z)).ToList();
                if (home.Count == 0)
                    Error("O3", $"{tag}: at ({o.X},{o.Z}) is outside every sector");
                else if (home.All(si => _stubs.Contains(si) || _adapters.Contains(si)))
                    Error("O3", $"{tag}: is inside a connector stub/adapter");
                else if (!home.Any(si => o.Y >= _lev.Sectors[si].CeilingAltitude - 0.5 && o.Y <= _lev.Sectors[si].FloorAltitude + 0.5))
                    Warn("O3", $"{tag}: Y {o.Y} is outside its sector's floor/ceiling range");

                if (objs.TableFor(o.Class) is { } table && (o.Data < 0 || o.Data >= table.Count))
                    Error("O6", $"{tag}: DATA {o.Data} is not in the {o.Class} table");
            }

            var r = _meta.Resources;
            void Compare(string kind, List<string> declared, List<string> actual)
            {
                var d = declared.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var a = actual.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!d.SetEquals(a))
                    Warn("M7", $"resources.{kind} [{string.Join(", ", d.Order())}] != ROOM.O [{string.Join(", ", a.Order())}]");
            }
            Compare("pods", r.Pods, UsedNames(objs, "3D"));
            Compare("sprites", r.Sprites, UsedNames(objs, "SPRITE"));
            Compare("frames", r.Frames, UsedNames(objs, "FRAME"));
            Compare("sounds", r.Sounds, UsedNames(objs, "SOUND"));
        }

        static List<string> UsedNames(ObjFile objs, string cls) =>
            objs.Objects.Where(o => string.Equals(o.Class, cls, StringComparison.OrdinalIgnoreCase))
                .Select(o => objs.TableFor(cls)![o.Data]).Distinct().ToList();

        public void Inf()
        {
            var names = _lev.Sectors.Select(s => s.Name).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in room.Inf.Items)
            {
                var tag = $"INF item '{item.Name ?? item.Type}'";
                if (string.Equals(item.Type, "level", StringComparison.OrdinalIgnoreCase))
                {
                    Error("I7", "item: level is not allowed (it is level-wide)");
                    continue;
                }
                if (item.Name == null || !names.Contains(item.Name))
                    Error("I1", $"{tag}: names a sector that is not in the room");
                else if (IsConnectorPart(_lev.Sectors[room.SectorIndex(item.Name)]))
                    Error("C7", $"{tag}: connector stubs/adapters must not have INF logic");
                if (item.Name is { } n && (n.Length > RoomRules.MaxInfNameLength || !RoomRules.InfName().IsMatch(n)))
                    Warn("I2", $"{tag}: INF sector names should be at most {RoomRules.MaxInfNameLength} chars of [A-Za-z0-9_.]");

                foreach (var r in InfReferences.Of(item))
                {
                    if (string.Equals(r.Sector, "SYSTEM", StringComparison.OrdinalIgnoreCase))
                        Error("I3", $"{tag}: messages SYSTEM (level-wide)");
                    else if (string.Equals(r.Sector, "complete", StringComparison.OrdinalIgnoreCase))
                        Error("I3", $"{tag}: references the 'complete' elevator (generator-owned)");
                    else if (!names.Contains(r.Sector))
                        Error("I1", $"{tag}: references '{r.Sector}', which is not in the room");
                    else if (IsConnectorPart(_lev.Sectors[room.SectorIndex(r.Sector)]))
                        Error("C7", $"{tag}: references connector part '{r.Sector}'");
                }
                foreach (var s in item.Statements)
                {
                    if (s.Key == "key")
                        Error("I4", $"{tag}: key: locks are generator-owned");
                    if (s.Key == "message" && s.Args.Any(a => string.Equals(a, "complete", StringComparison.OrdinalIgnoreCase)))
                        Error("I3", $"{tag}: sends 'complete' (goals are generator-owned)");
                }
                if (item.Classes.Any(c => c.Kind == "teleporter"))
                    Warn("I5", $"{tag}: teleporter chute; declare it as a one-way traversal edge");
            }
        }

        public void Metadata(List<ConnectorGeometry> geometry)
        {
            if (_meta.Spec != RoomRules.SpecVersion)
                Warn("M0", $"room.json targets spec {_meta.Spec}; validator is spec {RoomRules.SpecVersion}");
            var byId = geometry.ToDictionary(c => c.Id, c => c);
            foreach (var c in _meta.Connectors)
            {
                if (!byId.TryGetValue(c.Id, out var g))
                {
                    Error("M1", $"connector '{c.Id}' has no {RoomRules.StubNameFor(c.Id)} stub sector");
                    continue;
                }
                if (g.Facing != null && g.Facing != c.Facing)
                    Error("C10", $"connector '{c.Id}': room.json says {c.Facing}, geometry faces {g.Facing}");
                if (Math.Abs(g.Floor - c.Floor) > Geo.Epsilon)
                    Error("M1", $"connector '{c.Id}': room.json floor {c.Floor}, stub floor {g.Floor}");
            }
            foreach (var g in geometry.Where(g => _meta.Connectors.All(c => c.Id != g.Id)))
                Error("M1", $"stub {RoomRules.StubNameFor(g.Id)} is not listed in room.json connectors");

            var nodes = _meta.Connectors.Select(c => c.Id).Concat(_meta.ItemSlots.Select(s => s.Id)).ToHashSet();
            foreach (var e in _meta.Traversal)
                foreach (var end in new[] { e.From, e.To }.Where(x => !nodes.Contains(x)))
                    Error("M2", $"traversal {e.From}->{e.To}: '{end}' is not a connector or item slot");
            foreach (var r in _meta.Traversal.SelectMany(e => e.Requires).Concat(_meta.ItemSlots.SelectMany(s => s.Requires)))
                if (!LogicCatalog.Progression.Contains(r) && r is not ("jump" or "crouch"))
                    Error("M3", $"unknown requirement '{r}'");
            foreach (var g in _meta.Goals)
            {
                if (!LogicCatalog.GoalItems.Contains(g.Item))
                    Error("M8", $"goal '{g.Item}' is not a goal item ({string.Join(", ", LogicCatalog.GoalItems)})");
                else if (!room.Objects.Objects.Any(o => LogicCatalog.GoalItemOf(o) is { } item && DeclaresGoal(g, item, o)))
                    Error("M8", $"goal '{g.Item}' at ({g.X},{g.Z}) has no matching object in ROOM.O");
                foreach (var from in g.ReachableFrom.Where(x => !nodes.Contains(x)))
                    Error("M8", $"goal '{g.Item}': reachableFrom '{from}' is not a connector");
            }
            foreach (var g in _meta.Goals.GroupBy(g => g.Item.ToUpperInvariant()).Where(g => g.Count() > 1))
                Error("M8", $"goal '{g.Key}' is declared {g.Count()} times; a level can use each goal item once");
            foreach (var slot in _meta.ItemSlots)
            {
                if (!_lev.Sectors.Any(s => Geo.Contains(s, slot.X, slot.Z) && !IsConnectorPart(s)))
                    Error("M4", $"item slot '{slot.Id}' is not inside a room sector");
                foreach (var from in slot.ReachableFrom.Where(x => !nodes.Contains(x)))
                    Error("M4", $"item slot '{slot.Id}': reachableFrom '{from}' is not a connector");
            }
        }
    }
}
