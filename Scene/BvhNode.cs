// -----------------------------------------------------------------------------
// File: Scene/BvhNode.cs
// Purpose: Bounding volume hierarchy.
//
// Builds and traverses a recursive acceleration tree so ray/triangle tests remain fast on larger scenes.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Rendering;

namespace LightingShowcase.SceneGraph;

/// <summary>Recursive bounding volume hierarchy node for accelerating ray/triangle queries.</summary>
public sealed class BvhNode
{
    private const int LeafSize = 8;

    private readonly BvhNode? left;
    private readonly BvhNode? right;
    private readonly Triangle[]? triangles;

    public Aabb Bounds { get; }

    /// <summary>Constructs and initializes this component.</summary>
    private BvhNode(List<Triangle> source, int start, int count)
    {
        Bounds = ComputeBounds(source, start, count);

        if (count <= LeafSize)
        {
            triangles = new Triangle[count];
            for (int i = 0; i < count; i++)
                triangles[i] = source[start + i];
            return;
        }

        int axis = LongestAxis(Bounds);
        source.Sort(start, count, Comparer<Triangle>.Create((a, b) => CompareCentroid(a, b, axis)));

        int leftCount = count / 2;
        int rightCount = count - leftCount;

        left = new BvhNode(source, start, leftCount);
        right = new BvhNode(source, start + leftCount, rightCount);
    }

    /// <summary>Builds default scene content or acceleration data depending on the owning class.</summary>
    public static BvhNode? Build(List<Triangle> triangles)
    {
        if (triangles.Count == 0)
            return null;

        List<Triangle> sorted = new(triangles);
        return new BvhNode(sorted, 0, sorted.Count);
    }

    /// <summary>Tests a ray against the primitive or bounds and returns hit information.</summary>
    public Hit? Intersect(Ray ray, double tMin, double tMax)
    {
        if (!Bounds.Intersect(ray, tMin, tMax))
            return null;

        if (triangles != null)
            return IntersectLeaf(ray, tMin, tMax);

        Hit? leftHit = left?.Intersect(ray, tMin, tMax);
        double closestSoFar = leftHit?.T ?? tMax;
        Hit? rightHit = right?.Intersect(ray, tMin, closestSoFar);

        return rightHit ?? leftHit;
    }

    /// <summary>Implements the any intersection operation for this file's subsystem.</summary>
    public bool AnyIntersection(Ray ray, double tMin, double tMax)
    {
        if (!Bounds.Intersect(ray, tMin, tMax))
            return false;

        if (triangles != null)
        {
            foreach (Triangle triangle in triangles)
            {
                Hit? hit = triangle.Intersect(ray);
                if (hit != null && hit.T > tMin && hit.T < tMax)
                    return true;
            }

            return false;
        }

        return (left?.AnyIntersection(ray, tMin, tMax) ?? false)
            || (right?.AnyIntersection(ray, tMin, tMax) ?? false);
    }

    /// <summary>Accumulates approximate opacity along a shadow ray, allowing transparent glTF material to transmit light.</summary>
    public double ShadowOpacity(Ray ray, double tMin, double tMax, int maxSamples)
    {
        if (maxSamples <= 0 || !Bounds.Intersect(ray, tMin, tMax))
            return 0.0;

        if (triangles != null)
        {
            double remaining = 1.0;
            int samples = 0;
            foreach (Triangle triangle in triangles)
            {
                Hit? hit = triangle.Intersect(ray);
                if (hit == null || hit.T <= tMin || hit.T >= tMax)
                    continue;

                double opacity = hit.Material.SampleAlpha(hit.TextureU, hit.TextureV) * (1.0 - hit.Material.Transmission * 0.82);
                remaining *= 1.0 - Math.Clamp(opacity, 0.0, 1.0);
                samples++;
                if (remaining <= 0.02 || samples >= maxSamples)
                    break;
            }

            return 1.0 - remaining;
        }

        double leftOpacity = left?.ShadowOpacity(ray, tMin, tMax, maxSamples) ?? 0.0;
        if (leftOpacity >= 0.98)
            return leftOpacity;
        double rightOpacity = right?.ShadowOpacity(ray, tMin, tMax, maxSamples) ?? 0.0;
        return 1.0 - (1.0 - leftOpacity) * (1.0 - rightOpacity);
    }

    /// <summary>Implements the intersect leaf operation for this file's subsystem.</summary>
    private Hit? IntersectLeaf(Ray ray, double tMin, double tMax)
    {
        Hit? closest = null;
        double closestSoFar = tMax;

        foreach (Triangle triangle in triangles!)
        {
            Hit? hit = triangle.Intersect(ray);
            if (hit != null && hit.T > tMin && hit.T < closestSoFar)
            {
                closestSoFar = hit.T;
                closest = hit;
            }
        }

        return closest;
    }

    /// <summary>Implements the compute bounds operation for this file's subsystem.</summary>
    private static Aabb ComputeBounds(List<Triangle> source, int start, int count)
    {
        Aabb bounds = source[start].Bounds;
        for (int i = 1; i < count; i++)
            bounds = Aabb.Surrounding(bounds, source[start + i].Bounds);
        return bounds;
    }

    /// <summary>Implements the longest axis operation for this file's subsystem.</summary>
    private static int LongestAxis(Aabb bounds)
    {
        double x = bounds.Max.X - bounds.Min.X;
        double y = bounds.Max.Y - bounds.Min.Y;
        double z = bounds.Max.Z - bounds.Min.Z;

        if (x >= y && x >= z) return 0;
        if (y >= z) return 1;
        return 2;
    }

    /// <summary>Implements the compare centroid operation for this file's subsystem.</summary>
    private static int CompareCentroid(Triangle a, Triangle b, int axis)
    {
        double ca = axis switch
        {
            0 => a.Centroid.X,
            1 => a.Centroid.Y,
            _ => a.Centroid.Z
        };

        double cb = axis switch
        {
            0 => b.Centroid.X,
            1 => b.Centroid.Y,
            _ => b.Centroid.Z
        };

        return ca.CompareTo(cb);
    }
}
