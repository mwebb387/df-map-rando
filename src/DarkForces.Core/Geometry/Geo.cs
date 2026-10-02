using DarkForces.Core.Lev;

namespace DarkForces.Core.Geometry;

/// <summary>Compass direction in the XZ plane; +Z is north.</summary>
public enum Facing { N, E, S, W }

public readonly record struct Bounds(double MinX, double MinZ, double MaxX, double MaxZ)
{
    public double Width => MaxX - MinX;
    public double Depth => MaxZ - MinZ;
    public bool Contains(double x, double z, double eps = 1e-6) =>
        x >= MinX - eps && x <= MaxX + eps && z >= MinZ - eps && z <= MaxZ + eps;
}

/// <summary>Sector geometry helpers. DF sectors wind their walls clockwise (negative signed area, +Z north).</summary>
public static class Geo
{
    public const double Epsilon = 1e-6;

    public static (Vertex A, Vertex B) Segment(Sector s, Wall w) => (s.Vertices[w.Left], s.Vertices[w.Right]);

    public static double Length(Sector s, Wall w)
    {
        var (a, b) = Segment(s, w);
        return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
    }

    /// <summary>Shoelace sum over all walls (inner loops included); negative for DF's clockwise winding.</summary>
    public static double SignedArea(Sector s) =>
        s.Walls.Sum(w =>
        {
            var (a, b) = Segment(s, w);
            return a.X * b.Z - b.X * a.Z;
        }) / 2;

    /// <summary>Even-odd point test over every wall, so holes are handled.</summary>
    public static bool Contains(Sector s, double x, double z)
    {
        var inside = false;
        foreach (var w in s.Walls)
        {
            var (a, b) = Segment(s, w);
            if (a.Z > z != b.Z > z && x < a.X + (z - a.Z) * (b.X - a.X) / (b.Z - a.Z))
                inside = !inside;
        }
        return inside;
    }

    public static Bounds BoundsOf(IEnumerable<Sector> sectors)
    {
        var vs = sectors.SelectMany(s => s.Vertices).ToList();
        return new Bounds(vs.Min(v => v.X), vs.Min(v => v.Z), vs.Max(v => v.X), vs.Max(v => v.Z));
    }

    /// <summary>
    /// Outward facing of a wall of a clockwise sector: the side away from the sector interior.
    /// Null if the wall is not axis-aligned.
    /// </summary>
    public static Facing? OutwardFacing(Sector s, Wall w)
    {
        var (a, b) = Segment(s, w);
        double dx = b.X - a.X, dz = b.Z - a.Z;
        // For clockwise winding the interior is on the right of a->b, so outward is the left normal (-dz, dx).
        var (nx, nz) = (-dz, dx);
        if (Math.Abs(nx) < Epsilon && nz > 0) return Facing.N;
        if (Math.Abs(nx) < Epsilon && nz < 0) return Facing.S;
        if (Math.Abs(nz) < Epsilon && nx > 0) return Facing.E;
        if (Math.Abs(nz) < Epsilon && nx < 0) return Facing.W;
        return null;
    }

    public static Facing Opposite(Facing f) => f switch
    {
        Facing.N => Facing.S,
        Facing.S => Facing.N,
        Facing.E => Facing.W,
        _ => Facing.E,
    };

    /// <summary>Unit vector of a facing, as (dx, dz).</summary>
    public static (int Dx, int Dz) Direction(Facing f) => f switch
    {
        Facing.N => (0, 1),
        Facing.S => (0, -1),
        Facing.E => (1, 0),
        _ => (-1, 0),
    };

    public static bool IsInteger(double v) => Math.Abs(v - Math.Round(v)) < Epsilon;

    public static bool OnGrid(double v, int grid) => IsInteger(v / grid);

    /// <summary>True if every vertex is used as LEFT exactly as often as RIGHT (walls form closed loops).</summary>
    public static bool LoopsClosed(Sector s)
    {
        var balance = new int[s.Vertices.Count];
        foreach (var w in s.Walls)
        {
            if (w.Left < 0 || w.Left >= balance.Length || w.Right < 0 || w.Right >= balance.Length)
                return false;
            balance[w.Left]++;
            balance[w.Right]--;
        }
        return balance.All(b => b == 0);
    }

    /// <summary>True if a single-loop sector is convex (all turns the same way, allowing collinear points).</summary>
    public static bool IsConvex(Sector s)
    {
        var pts = OrderedLoop(s);
        if (pts == null || pts.Count < 3) return false;
        var sign = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var (a, b, c) = (pts[i], pts[(i + 1) % pts.Count], pts[(i + 2) % pts.Count]);
            var cross = (b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X);
            if (Math.Abs(cross) < Epsilon) continue;
            var sg = Math.Sign(cross);
            if (sign == 0) sign = sg;
            else if (sg != sign) return false;
        }
        return true;
    }

    /// <summary>Vertices in wall order for a sector with exactly one loop; null otherwise.</summary>
    public static List<Vertex>? OrderedLoop(Sector s)
    {
        if (s.Walls.Count == 0) return null;
        var byLeft = s.Walls.ToLookup(w => w.Left);
        var result = new List<Vertex>();
        var w0 = s.Walls[0];
        var cur = w0;
        for (var i = 0; i < s.Walls.Count; i++)
        {
            result.Add(s.Vertices[cur.Left]);
            var next = byLeft[cur.Right].FirstOrDefault();
            if (next == null) return null;
            if (ReferenceEquals(next, w0)) return i == s.Walls.Count - 1 ? result : null;
            cur = next;
        }
        return null;
    }
}
