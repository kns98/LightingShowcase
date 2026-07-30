// -----------------------------------------------------------------------------
// File: Scene/Aabb.cs
// Purpose: Axis-aligned bounding box.
//
// Stores object/BVH bounds and provides slab intersection tests for acceleration.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;
using LightingShowcase.Rendering;

namespace LightingShowcase.SceneGraph;

/// <summary>Axis-aligned bounding box used for object extents and BVH acceleration.</summary>
public readonly struct Aabb
{
    public readonly Vec3 Min;
    public readonly Vec3 Max;

    /// <summary>Constructs and initializes this component.</summary>
    public Aabb(Vec3 min, Vec3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Tests a ray against the primitive or bounds and returns hit information.</summary>
    public bool Intersect(Ray ray, double tMin, double tMax)
    {
        if (!HitAxis(ray.Origin.X, ray.Direction.X, Min.X, Max.X, ref tMin, ref tMax)) return false;
        if (!HitAxis(ray.Origin.Y, ray.Direction.Y, Min.Y, Max.Y, ref tMin, ref tMax)) return false;
        if (!HitAxis(ray.Origin.Z, ray.Direction.Z, Min.Z, Max.Z, ref tMin, ref tMax)) return false;
        return true;
    }

    /// <summary>Implements the hit axis operation for this file's subsystem.</summary>
    private static bool HitAxis(double origin, double direction, double min, double max, ref double tMin, ref double tMax)
    {
        const double eps = 1e-12;

        if (System.Math.Abs(direction) < eps)
            return origin >= min && origin <= max;

        double invD = 1.0 / direction;
        double t0 = (min - origin) * invD;
        double t1 = (max - origin) * invD;

        if (invD < 0.0)
            (t0, t1) = (t1, t0);

        if (t0 > tMin) tMin = t0;
        if (t1 < tMax) tMax = t1;

        return tMax > tMin;
    }

    /// <summary>Implements the around operation for this file's subsystem.</summary>
    public static Aabb Around(Triangle triangle)
    {
        const double pad = 1e-5;

        double minX = System.Math.Min(triangle.A.X, System.Math.Min(triangle.B.X, triangle.C.X)) - pad;
        double minY = System.Math.Min(triangle.A.Y, System.Math.Min(triangle.B.Y, triangle.C.Y)) - pad;
        double minZ = System.Math.Min(triangle.A.Z, System.Math.Min(triangle.B.Z, triangle.C.Z)) - pad;

        double maxX = System.Math.Max(triangle.A.X, System.Math.Max(triangle.B.X, triangle.C.X)) + pad;
        double maxY = System.Math.Max(triangle.A.Y, System.Math.Max(triangle.B.Y, triangle.C.Y)) + pad;
        double maxZ = System.Math.Max(triangle.A.Z, System.Math.Max(triangle.B.Z, triangle.C.Z)) + pad;

        return new Aabb(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));
    }

    /// <summary>Implements the surrounding operation for this file's subsystem.</summary>
    public static Aabb Surrounding(Aabb a, Aabb b)
    {
        Vec3 min = new(
            System.Math.Min(a.Min.X, b.Min.X),
            System.Math.Min(a.Min.Y, b.Min.Y),
            System.Math.Min(a.Min.Z, b.Min.Z));

        Vec3 max = new(
            System.Math.Max(a.Max.X, b.Max.X),
            System.Math.Max(a.Max.Y, b.Max.Y),
            System.Math.Max(a.Max.Z, b.Max.Z));

        return new Aabb(min, max);
    }
}
