// -----------------------------------------------------------------------------
// File: Scene/Triangle.cs
// Purpose: Triangle primitive.
//
// Stores one renderable triangle and performs ray intersection and bounds calculation.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;
using LightingShowcase.Rendering;

namespace LightingShowcase.SceneGraph;

/// <summary>Renderable triangle primitive with material and optional UV coordinates.</summary>
public sealed class Triangle
{
    public Vec3 A { get; }
    public Vec3 B { get; }
    public Vec3 C { get; }
    public Vec2 UvA { get; }
    public Vec2 UvB { get; }
    public Vec2 UvC { get; }
    public Material Material { get; }
    public int GroupId { get; }
    public Vec3 Normal { get; }
    public Vec3 NormalA { get; }
    public Vec3 NormalB { get; }
    public Vec3 NormalC { get; }
    public Vec3 Centroid { get; }
    public Aabb Bounds { get; }

    private readonly Vec3 edge1;
    private readonly Vec3 edge2;

    /// <summary>Constructs and initializes this component.</summary>
    public Triangle(Vec3 a, Vec3 b, Vec3 c, Material material, int groupId = -1)
        : this(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1), material, groupId)
    {
    }

    /// <summary>Constructs and initializes this component.</summary>
    public Triangle(Vec3 a, Vec3 b, Vec3 c, Vec2 uvA, Vec2 uvB, Vec2 uvC, Material material, int groupId = -1)
        : this(a, b, c, uvA, uvB, uvC, Vec3.Zero, Vec3.Zero, Vec3.Zero, material, groupId)
    {
    }

    /// <summary>Constructs a triangle with authored per-vertex shading normals.</summary>
    public Triangle(
        Vec3 a, Vec3 b, Vec3 c,
        Vec2 uvA, Vec2 uvB, Vec2 uvC,
        Vec3 normalA, Vec3 normalB, Vec3 normalC,
        Material material, int groupId = -1)
    {
        A = a; B = b; C = c;
        UvA = uvA; UvB = uvB; UvC = uvC;
        Material = material; GroupId = groupId;
        edge1 = b - a;
        edge2 = c - a;
        Normal = edge1.Cross(edge2).Normalize();
        NormalA = NormalizeOrFallback(normalA, Normal);
        NormalB = NormalizeOrFallback(normalB, Normal);
        NormalC = NormalizeOrFallback(normalC, Normal);
        Centroid = (a + b + c) / 3.0;
        Bounds = Aabb.Around(this);
    }

    /// <summary>Tests a ray against the primitive or bounds and returns hit information.</summary>
    public Hit? Intersect(Ray ray)
    {
        const double eps = 1e-6;
        Vec3 h = ray.Direction.Cross(edge2);
        double det = edge1.Dot(h);
        if (System.Math.Abs(det) < eps) return null;

        double invDet = 1.0 / det;
        Vec3 s = ray.Origin - A;
        double u = invDet * s.Dot(h);
        if (u < 0.0 || u > 1.0) return null;

        Vec3 q = s.Cross(edge1);
        double v = invDet * ray.Direction.Dot(q);
        if (v < 0.0 || u + v > 1.0) return null;

        double t = invDet * edge2.Dot(q);
        if (t < eps) return null;

        double w = 1.0 - u - v;
        Vec2 uv = UvA * w + UvB * u + UvC * v;
        Vec3 shadingNormal = NormalizeOrFallback(NormalA * w + NormalB * u + NormalC * v, Normal);
        if (shadingNormal.Dot(Normal) < 0.0)
            shadingNormal = -shadingNormal;
        return new Hit(t, ray.Origin + ray.Direction * t, shadingNormal, Material, GroupId, uv.U, uv.V);
    }

    private static Vec3 NormalizeOrFallback(Vec3 value, Vec3 fallback)
    {
        double length = value.Length();
        return double.IsFinite(length) && length > 1e-12 ? value / length : fallback;
    }
}
