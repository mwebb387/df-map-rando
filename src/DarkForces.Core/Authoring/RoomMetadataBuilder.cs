using System.Text.Json;
using DarkForces.Core.Geometry;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Objects;
using DarkForces.Core.Rooms;

namespace DarkForces.Core.Authoring;

/// <param name="Metadata">The new <c>room.json</c>.</param>
/// <param name="Changes">What differs from the previous <c>room.json</c> (or what was drafted), for the author to review.</param>
/// <param name="Findings">The room validated with the new metadata.</param>
public sealed record MetadataResult(RoomMetadata Metadata, List<string> Changes, List<RoomFinding> Findings)
{
    public bool IsValid => Findings.All(f => f.Severity != Severity.Error);
}

/// <summary>
/// Builds <c>room.json</c> from a room's geometry and objects (docs/authoring-rooms.md, "Metadata").
/// Facts the files already state are <b>derived</b> and always rewritten: palette, exterior, bounds, connector
/// ids/facings/floors, resources, goal items. Facts only the author knows are <b>kept</b> from the existing
/// <c>room.json</c>: id, name, author, tags, notes, rotatable, source, doors, traversal, item slots, start points,
/// and each goal's <c>reachableFrom</c>. Traversal for connectors without any is drafted as "reaches every other
/// connector" and marked <see cref="UnverifiedNote"/>, for the author to check in game.
/// </summary>
public static class RoomMetadataBuilder
{
    public const string UnverifiedNote = "auto: unverified";

    public static MetadataResult Build(LevFile lev, ObjFile obj, InfFile inf, RoomMetadata? existing, string defaultId)
    {
        var changes = new List<string>();
        var old = existing ?? new RoomMetadata { Id = defaultId, Name = defaultId };
        var meta = new RoomMetadata
        {
            Id = old.Id.Length > 0 ? old.Id : defaultId,
            Name = old.Name,
            Author = old.Author,
            Source = old.Source,
            Rotatable = old.Rotatable,
            ItemSlots = [.. old.ItemSlots],
            StartPoints = [.. old.StartPoints],
            Tags = [.. old.Tags],
            Notes = [.. old.Notes],

            Palette = lev.Palette,
            Exterior = lev.Sectors.Any(s => (s.Flags1 & (1 | 128)) != 0),
        };

        if (lev.Sectors.Count > 0)
        {
            var b = Geo.BoundsOf(lev.Sectors);
            meta.Bounds = new RoomBounds
            {
                MinX = b.MinX, MinZ = b.MinZ, MaxX = b.MaxX, MaxZ = b.MaxZ,
                MinY = lev.Sectors.Min(s => s.CeilingAltitude), MaxY = lev.Sectors.Max(s => s.FloorAltitude),
            };
        }

        // ---- connectors: one per CX_ stub; the door comes from the author's existing entry
        var oldConnectors = old.Connectors.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var g in RoomValidator.FindConnectors(lev).OrderBy(g => g.Id, StringComparer.Ordinal))
        {
            if (g.Facing is not { } facing)
            {
                changes.Add($"{RoomRules.StubNameFor(g.Id)}: skipped, its portal wall is not axis-aligned (see the C rules below)");
                continue;
            }
            var door = oldConnectors.GetValueOrDefault(g.Id)?.Door;
            meta.Connectors.Add(new ConnectorInfo(g.Id, facing, g.Floor, door));
            if (!oldConnectors.ContainsKey(g.Id))
                changes.Add($"connector {g.Id}: new, facing {facing}, floor {g.Floor}" + (existing != null ? "" : " (add a \"door\" if it should have one)"));
            else if (oldConnectors[g.Id] is var o && (o.Facing != facing || Math.Abs(o.Floor - g.Floor) > Geo.Epsilon))
                changes.Add($"connector {g.Id}: was {o.Facing} floor {o.Floor}, now {facing} floor {g.Floor}");
        }
        var ids = meta.Connectors.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in oldConnectors.Keys.Where(k => !ids.Contains(k)))
            changes.Add($"connector {gone}: removed (no {RoomRules.StubNameFor(gone)} stub in ROOM.LEV); its door and traversal are dropped");

        // ---- traversal: keep the author's edges between ids that still exist, draft edges for new connectors
        var nodes = ids.Concat(meta.ItemSlots.Select(s => s.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropped = new List<string>();
        foreach (var e in old.Traversal)
        {
            if (nodes.Contains(e.From) && nodes.Contains(e.To))
                meta.Traversal.Add(e);
            else
                dropped.Add($"{e.From}->{e.To}");
        }
        if (dropped.Count > 0)
            changes.Add($"traversal: dropped {dropped.Count} edge(s) to unknown connectors or item slots: {string.Join(", ", dropped)}");
        var covered = old.Traversal.SelectMany(e => new[] { e.From, e.To }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drafted = 0;
        foreach (var c in meta.Connectors.Where(c => !covered.Contains(c.Id)))
            foreach (var other in meta.Connectors.Where(o => o.Id != c.Id))
            {
                foreach (var (from, to) in new[] { (c.Id, other.Id), (other.Id, c.Id) })
                    if (!meta.Traversal.Any(e => e.From == from && e.To == to))
                    {
                        meta.Traversal.Add(new TraversalEdge(from, to, [], Note: UnverifiedNote));
                        drafted++;
                    }
            }
        if (drafted > 0)
            changes.Add($"traversal: drafted {drafted} edge(s) as \"every connector reaches every other\" ({UnverifiedNote}); check them in game");
        meta.Traversal.Sort((x, y) => string.CompareOrdinal(x.From + ">" + x.To, y.From + ">" + y.To));

        // ---- goals: every goal item in ROOM.O, keeping the author's reachableFrom
        foreach (var o in obj.Objects)
        {
            if (LogicCatalog.GoalItemOf(o) is not { } item) continue;
            var prior = old.Goals.FirstOrDefault(g => string.Equals(g.Item, item, StringComparison.OrdinalIgnoreCase));
            meta.Goals.Add(new GoalInfo(item, o.X, o.Y, o.Z, [.. (prior?.ReachableFrom ?? []).Where(ids.Contains)]));
            if (prior == null)
                changes.Add($"goal {item}: found in ROOM.O at ({o.X}, {o.Z})");
        }
        foreach (var g in old.Goals.Where(g => !meta.Goals.Any(n => string.Equals(n.Item, g.Item, StringComparison.OrdinalIgnoreCase))))
            changes.Add($"goal {g.Item}: removed (no longer in ROOM.O)");

        meta.Resources = new RoomResources { Pods = [.. obj.Pods], Sprites = [.. obj.Sprites], Frames = [.. obj.Frames], Sounds = [.. obj.Sounds] };

        if (existing != null)
            foreach (var (field, a, b) in new (string, object, object)[]
                     {
                         ("palette", existing.Palette, meta.Palette), ("exterior", existing.Exterior, meta.Exterior),
                         ("bounds", existing.Bounds, meta.Bounds), ("resources", existing.Resources, meta.Resources),
                     })
                if (JsonSerializer.Serialize(a, RoomMetadata.Json) != JsonSerializer.Serialize(b, RoomMetadata.Json))
                    changes.Add($"{field}: updated from the room files");

        var findings = RoomValidator.Validate(new RoomPackage(meta, lev, obj, inf));
        return new MetadataResult(meta, changes, findings);
    }

    /// <summary>Builds the metadata for a package folder (its <c>room.json</c>, if any, supplies the kept fields).</summary>
    public static MetadataResult Build(string directory)
    {
        var (meta, lev, obj, inf) = RoomPackage.LoadParts(directory);
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))).ToLowerInvariant();
        return Build(lev, obj, inf, meta, id);
    }
}
