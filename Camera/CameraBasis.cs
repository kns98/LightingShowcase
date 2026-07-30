// -----------------------------------------------------------------------------
// File: Camera/CameraBasis.cs
// Purpose: Camera coordinate frame.
//
// Stores the orthonormal right/up/forward vectors used to turn screen pixels into world-space ray directions.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.CameraSystem;

/// <summary>Right/up/forward camera basis vectors used for ray generation.</summary>
public readonly struct CameraBasis
{
    public readonly Vec3 Forward;
    public readonly Vec3 Right;
    public readonly Vec3 Up;

    /// <summary>Constructs and initializes this component.</summary>
    public CameraBasis(Vec3 forward, Vec3 right, Vec3 up)
    {
        Forward = forward;
        Right = right;
        Up = up;
    }
}
