// -----------------------------------------------------------------------------
// File: Scene/Hit.cs
// Purpose: Intersection result.
//
// Mutable record of the nearest ray hit, including distance, position, normal, material, and texture coordinate.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Nearest-hit record filled during ray traversal.</summary>
public sealed class Hit
{
    public double T { get; }
    public Vec3 Point { get; }
    public Vec3 Normal { get; }
    public Material Material { get; }
    public int GroupId { get; }
    public double TextureU { get; }
    public double TextureV { get; }

    /// <summary>Constructs and initializes this component.</summary>
    public Hit(double t, Vec3 point, Vec3 normal, Material material, int groupId = -1, double textureU = 0.0, double textureV = 0.0)
    {
        T = t;
        Point = point;
        Normal = normal;
        Material = material;
        GroupId = groupId;
        TextureU = textureU;
        TextureV = textureV;
    }
}
