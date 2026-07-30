// -----------------------------------------------------------------------------
// File: Math/Vec2.cs
// Purpose: 2D vector math.
//
// Small immutable helper for texture coordinates and other two-dimensional numeric data.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Math3D;

/// <summary>Immutable two-dimensional vector used primarily for UV texture coordinates.</summary>
public readonly struct Vec2
{
    public readonly double U;
    public readonly double V;

    /// <summary>Constructs and initializes this component.</summary>
    public Vec2(double u, double v)
    {
        U = u;
        V = v;
    }

    public static Vec2 Zero => new(0, 0);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.U + b.U, a.V + b.V);
    public static Vec2 operator *(Vec2 a, double s) => new(a.U * s, a.V * s);
}
