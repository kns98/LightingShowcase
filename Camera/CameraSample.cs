// -----------------------------------------------------------------------------
// File: Camera/CameraSample.cs
// Purpose: Interpolated camera sample.
//
// Immutable value returned after sampling the demo path at a specific time.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.CameraSystem;

/// <summary>Interpolated camera state produced by sampling the demo path.</summary>
public readonly struct CameraSample
{
    public readonly Vec3 Position;
    public readonly Vec3 Target;

    /// <summary>Constructs and initializes this component.</summary>
    public CameraSample(Vec3 position, Vec3 target)
    {
        Position = position;
        Target = target;
    }
}
