// -----------------------------------------------------------------------------
// File: Rendering/Ray.cs
// Purpose: Ray primitive.
//
// Immutable origin/direction pair passed into intersection and shading routines.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

/// <summary>World-space ray with an origin and normalized direction.</summary>
public readonly struct Ray
{
    public readonly Vec3 Origin;
    public readonly Vec3 Direction;

    /// <summary>Constructs and initializes this component.</summary>
    public Ray(Vec3 origin, Vec3 direction)
    {
        Origin = origin;
        Direction = direction.Normalize();
    }
}
