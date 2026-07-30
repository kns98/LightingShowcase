// -----------------------------------------------------------------------------
// File: Scene/MeshSimplifier.cs
// Purpose: Topology-conscious selected-object triangle reduction.
//
// The first Simplify implementation reduced triangle count by deleting spatially
// distributed representative triangles.  That was fast, but it necessarily left
// holes because faces were removed without rebuilding neighboring topology.
//
// This version uses vertex clustering instead: nearby vertices are collapsed to
// shared averaged positions and the original faces are rebuilt against those
// collapsed vertices.  Degenerate faces are then removed.  This is still a
// lightweight editor simplifier, not a full quadric-error optimizer, but it is
// much safer for closed surfaces because it does not intentionally delete random
// surface patches.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Fast topology-conscious simplifier used by the Selection tab Simplify command.</summary>
internal static class MeshSimplifier
{
    private const double MinAreaSquared = 1e-18;

    /// <summary>
    /// Returns a simplified triangle list.  The algorithm uses vertex clustering
    /// rather than arbitrary face deletion, so simplification keeps adjacent
    /// faces stitched together far better and avoids the visible holes caused by
    /// dropping isolated triangles.
    /// </summary>
    public static List<Triangle> Simplify(IReadOnlyList<Triangle> triangles, double keepFraction)
    {
        if (triangles.Count <= 3 || keepFraction >= 0.999)
            return triangles.ToList();

        keepFraction = Math.Clamp(keepFraction, 0.02, 1.0);
        int targetCount = Math.Clamp((int)Math.Ceiling(triangles.Count * keepFraction), 4, triangles.Count);
        if (targetCount >= triangles.Count)
            return triangles.ToList();

        GetVertexBounds(triangles, out Vec3 min, out Vec3 max);
        Vec3 span = new(
            Math.Max(1e-9, max.X - min.X),
            Math.Max(1e-9, max.Y - min.Y),
            Math.Max(1e-9, max.Z - min.Z));

        double maxSpan = Math.Max(span.X, Math.Max(span.Y, span.Z));
        if (maxSpan <= 1e-9)
            return triangles.ToList();

        // Binary-search a grid cell size.  Larger cells collapse more vertices,
        // which removes more degenerate faces.  We prefer the largest result
        // that is at or below the requested triangle budget, but fall back to the
        // closest smaller reduction if the mesh does not simplify smoothly.
        List<Triangle>? bestAtOrBelowTarget = null;
        List<Triangle>? closestReduction = null;
        int closestDelta = int.MaxValue;

        double low = maxSpan / 4096.0;
        double high = maxSpan;

        for (int pass = 0; pass < 11; pass++)
        {
            double cellSize = Math.Sqrt(low * high);
            List<Triangle> candidate = SimplifyWithCellSize(triangles, min, cellSize);
            int count = candidate.Count;

            int delta = Math.Abs(count - targetCount);
            if (count < triangles.Count && delta < closestDelta)
            {
                closestDelta = delta;
                closestReduction = candidate;
            }

            if (count <= targetCount && count > 0)
            {
                bestAtOrBelowTarget = candidate;
                high = cellSize;
            }
            else
            {
                low = cellSize;
            }
        }

        List<Triangle> result = bestAtOrBelowTarget ?? closestReduction ?? triangles.ToList();

        // Safety valve: if clustering would destroy almost the whole object,
        // keep the original geometry rather than returning a broken shell.
        if (result.Count < 4 || result.Count >= triangles.Count)
            return triangles.ToList();

        return result;
    }

    private static List<Triangle> SimplifyWithCellSize(IReadOnlyList<Triangle> triangles, Vec3 min, double cellSize)
    {
        Dictionary<CellKey, ClusterAccumulator> clusters = new(triangles.Count);

        // Pass 1: create one averaged vertex per spatial cell.  UVs are averaged
        // as a best-effort preview/editing value; material assignment remains per
        // rebuilt face below.
        foreach (Triangle tri in triangles)
        {
            AddVertex(clusters, tri.A, tri.UvA, min, cellSize);
            AddVertex(clusters, tri.B, tri.UvB, min, cellSize);
            AddVertex(clusters, tri.C, tri.UvC, min, cellSize);
        }

        Dictionary<CellKey, ClusterVertex> vertices = new(clusters.Count);
        foreach (KeyValuePair<CellKey, ClusterAccumulator> pair in clusters)
        {
            ClusterAccumulator c = pair.Value;
            double inv = 1.0 / c.Count;
            vertices[pair.Key] = new ClusterVertex(
                new Vec3(c.X * inv, c.Y * inv, c.Z * inv),
                new Vec2(c.U * inv, c.V * inv));
        }

        List<Triangle> result = new(triangles.Count);
        HashSet<TriangleKey> seenFaces = new();

        foreach (Triangle tri in triangles)
        {
            CellKey ka = ToCell(tri.A, min, cellSize);
            CellKey kb = ToCell(tri.B, min, cellSize);
            CellKey kc = ToCell(tri.C, min, cellSize);

            // If all vertices collapsed into the same cell, this face contributes
            // no visible surface at the simplified resolution.
            if (ka.Equals(kb) || kb.Equals(kc) || kc.Equals(ka))
                continue;

            TriangleKey faceKey = TriangleKey.From(ka, kb, kc);
            if (!seenFaces.Add(faceKey))
                continue;

            ClusterVertex a = vertices[ka];
            ClusterVertex b = vertices[kb];
            ClusterVertex c = vertices[kc];

            if (TriangleAreaSquared(a.Position, b.Position, c.Position) < MinAreaSquared)
                continue;

            result.Add(new Triangle(
                a.Position, b.Position, c.Position,
                a.Uv, b.Uv, c.Uv,
                tri.Material,
                tri.GroupId));
        }

        return result;
    }

    private static void AddVertex(Dictionary<CellKey, ClusterAccumulator> clusters, Vec3 p, Vec2 uv, Vec3 min, double cellSize)
    {
        CellKey key = ToCell(p, min, cellSize);
        if (!clusters.TryGetValue(key, out ClusterAccumulator c))
            c = new ClusterAccumulator();

        c.X += p.X;
        c.Y += p.Y;
        c.Z += p.Z;
        c.U += uv.U;
        c.V += uv.V;
        c.Count++;
        clusters[key] = c;
    }

    private static CellKey ToCell(Vec3 p, Vec3 min, double cellSize)
    {
        return new CellKey(
            (int)Math.Floor((p.X - min.X) / cellSize),
            (int)Math.Floor((p.Y - min.Y) / cellSize),
            (int)Math.Floor((p.Z - min.Z) / cellSize));
    }

    private static void GetVertexBounds(IReadOnlyList<Triangle> triangles, out Vec3 min, out Vec3 max)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        foreach (Triangle tri in triangles)
        {
            Include(tri.A);
            Include(tri.B);
            Include(tri.C);
        }

        min = new Vec3(minX, minY, minZ);
        max = new Vec3(maxX, maxY, maxZ);

        void Include(Vec3 p)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
        }
    }

    private static double TriangleAreaSquared(Vec3 a, Vec3 b, Vec3 c)
    {
        Vec3 cross = (b - a).Cross(c - a);
        return cross.Dot(cross) * 0.25;
    }

    private readonly record struct CellKey(int X, int Y, int Z);

    private readonly record struct ClusterVertex(Vec3 Position, Vec2 Uv);

    private struct ClusterAccumulator
    {
        public double X;
        public double Y;
        public double Z;
        public double U;
        public double V;
        public int Count;
    }

    /// <summary>
    /// Orientation-independent face key used only to remove duplicate faces that
    /// can appear after neighboring vertices are clustered together.
    /// </summary>
    private readonly record struct TriangleKey(CellKey A, CellKey B, CellKey C)
    {
        public static TriangleKey From(CellKey a, CellKey b, CellKey c)
        {
            CellKey[] keys = new[] { a, b, c };
            Array.Sort(keys, Compare);
            return new TriangleKey(keys[0], keys[1], keys[2]);
        }

        private static int Compare(CellKey left, CellKey right)
        {
            int x = left.X.CompareTo(right.X);
            if (x != 0) return x;
            int y = left.Y.CompareTo(right.Y);
            if (y != 0) return y;
            return left.Z.CompareTo(right.Z);
        }
    }
}
